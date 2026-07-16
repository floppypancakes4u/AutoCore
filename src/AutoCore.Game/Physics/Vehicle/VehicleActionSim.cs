namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Per-substep orchestrator — port of <c>VehicleAction::applyAction</c> @ <c>0x598650</c>
/// calling subsystem modules that exist. Missing modules (full friction writeback,
/// transmission, collision-window airStab entity path) are skipped without throwing.
/// </summary>
/// <remarks>
/// <para>
/// Retail call order (verified <c>fn_00598650_applyAction.md</c> +
/// <c>fn_00636a60_tickSubsystems.md</c>):
/// </para>
/// <code>
/// accept axes (PushDrive / caller)
/// early-outs
/// tickSubsystems (fw):
///   preUpdate / wheel collide
///   driverInput, steering angles, engine-slot, xmit, brake
///   suspension forces
///   aerodynamics
///   postTick: susp impulse (F*dt), friction solve, AVD action-list
/// anti-sink lift
/// stage-1 steer ramp (VA+0x24 toward entity+0x618, rate VA+0x20 * dt * {1|2})
/// stage-2 final steer (VA+0x28, speedFactor min(|v|/20,1), ±0.05/tick)
/// calcWheelTorque  (feeds NEXT postTick — one-substep lag)
/// airStabilization
/// (world integrator steps rigid body outside applyAction)
/// </code>
/// <para>
/// This port maps that spine to available modules (see <see cref="ApplyAction"/> body).
/// Suspension uses retail postTick suspImpulse (C2): per grounded wheel
/// <c>I = suspForce · dt · n̂</c> via <see cref="HkRigidBody.ApplyPointImpulse"/> at the wheel
/// contact point (wheel+0x20, see <c>0.4-suspension.md</c> §2 / <c>0x64bc70</c> decompile) —
/// linear Δv equals ApplyForce(F)+Integrate(dt) but r×J adds the retail weight-transfer
/// torque, and the impulse lands immediately before friction (retail order).
/// Throttle sign is preserved (retail keeps AI/player thr sign; no flip).
/// </para>
/// </remarks>
public static class VehicleActionSim
{
    /// <summary>
    /// Run one sub-step of vehicle action + framework force path on <paramref name="inst"/>.
    /// Non-positive or non-finite <paramref name="dt"/> still accepts drive axes (no integrate).
    /// </summary>
    public static void ApplyAction(
        VehiclePhysicsInstance inst,
        float throttleInput,
        float steerInput,
        bool handbrake,
        float dt,
        IVehicleCollisionQuery query)
    {
        ArgumentNullException.ThrowIfNull(inst);
        query ??= NullVehicleCollisionQuery.Instance;

        // --- 0. Accept drive axes (always; retail: entity thr/steer/hb + PushDrive) ---
        inst.Handbrake = handbrake;
        inst.Throttle = PhysicsMath.Clamp(
            throttleInput,
            HkPhysicsConstants.SteerInputMin,
            HkPhysicsConstants.SteerInputMax);
        inst.SteerInput = PhysicsMath.Clamp(
            steerInput,
            HkPhysicsConstants.SteerInputMin,
            HkPhysicsConstants.SteerInputMax);

        if (!float.IsFinite(dt) || dt <= 0f)
            return;

        var data = inst.Data;
        var body = inst.Body;

        // Chassis basis: +Y up, +Z forward (HkVehicleData convention).
        RotateBodyVector(body, 0f, 1f, 0f, out var upX, out var upY, out var upZ);
        RotateBodyVector(body, 0f, 0f, 1f, out var fwdX, out var fwdY, out var fwdZ);

        // Terrain casts: use world-down when roughly upright so flipped body-down doesn't miss ground.
        float upDotWorld = upY; // world up = (0,1,0)
        float downX, downY, downZ;
        if (upDotWorld >= HkPhysicsConstants.TerrainCastWorldDownDot)
        {
            downX = 0f;
            downY = -1f;
            downZ = 0f;
        }
        else
        {
            downX = -upX;
            downY = -upY;
            downZ = -upZ;
        }

        float chassisSpeed = PhysicsMath.Length(body.LinVelX, body.LinVelY, body.LinVelZ);
        float forwardSpeed = body.LinVelX * fwdX + body.LinVelY * fwdY + body.LinVelZ * fwdZ;

        // =====================================================================
        // tickSubsystems mapping (retail step 4 — BEFORE stage-1/2 and torque)
        // =====================================================================

        // preUpdate / wheel collide + physical wheel angles from prior SteerFinal
        // (retail: steering child update uses previous stage-1 sample via driverInput).
        ApplySteeringWheelAngles(inst, forwardSpeed);
        bool anyContact = ApplyWheelCollideAndSuspension(
            inst, query, downX, downY, downZ, forwardSpeed, dt);
        inst.AllWheelsAirborne = !anyContact;

        // postTick: friction uses PRIOR-tick DriveTorque (retail one-substep lag on torque path)
        TryApplyFriction(inst, fwdX, fwdY, fwdZ, upX, upY, upZ, dt);

        // aerodynamics (child before postTick in retail; after susp either way for force sum)
        var (aeroFx, aeroFy, aeroFz) = HkVehicleAerodynamics.ComputeForce(
            data.AirDensity,
            data.FrontalArea,
            data.DragCoefficient,
            data.LiftCoefficient,
            data.ExtraGravityX,
            data.ExtraGravityY,
            data.ExtraGravityZ,
            fwdX, fwdY, fwdZ,
            upX, upY, upZ,
            body.LinVelX, body.LinVelY, body.LinVelZ,
            body.Mass);
        body.ApplyForce(aeroFx, aeroFy, aeroFz);

        // AVD (retail: framework action-list inside postTick, not a 7-child slot)
        var (wx, wy, wz, _) = HkVehicleVelocityDamper.DampenAngular(
            body.AngVelX, body.AngVelY, body.AngVelZ, 0f,
            dt,
            data.AvdNormalSpinDamping,
            data.AvdCollisionSpinDamping,
            data.AvdCollisionThreshold);
        body.AngVelX = wx;
        body.AngVelY = wy;
        body.AngVelZ = wz;

        // =====================================================================
        // AA layer after tickSubsystems (retail steps 6–9)
        // =====================================================================

        // stage-1: VA+0x24 toward SteerInput (entity+0x618); rate = VA+0x20 * dt * factor
        inst.SteerRamp = HkVehicleSteering.RampStage1(
            inst.SteerRamp,
            inst.SteerInput,
            dt,
            HkPhysicsConstants.SteerStage1RateBase);

        // stage-2: VA+0x28 toward SteerRamp * min(|v|/20, 1), ±0.05 per tick
        float speedFactor = HkVehicleSteering.ModeSpeedFactor(chassisSpeed);
        float targetFinal = inst.SteerRamp * speedFactor;
        inst.SteerFinal = HkVehicleSteering.RampSteer(inst.SteerFinal, targetFinal);

        // calcWheelTorque — writes DriveTorque for NEXT substep's friction (retail lag)
        ApplyEngineTorque(inst, upX, upY, upZ, chassisSpeed);

        // upright-restore / air-stab essentials (opt-in pure call with body-up + angVel + throttle).
        // Retail upright gate is in applyAction non-mode-0x02; continuous AVD is above
        // (HkVehicleVelocityDamper). Collision-window 6400 ms entity path is DEFERRED.
        float invI = (body.InvInertiaX + body.InvInertiaY + body.InvInertiaZ) * (1f / 3f);
        float angX = body.AngVelX, angY = body.AngVelY, angZ = body.AngVelZ;
        HkVehicleAirStabilization.ApplyUprightRestore(
            bodyUpX: upX, bodyUpY: upY, bodyUpZ: upZ,
            ref angX, ref angY, ref angZ,
            throttle: inst.Throttle,
            dt: dt,
            invInertia: invI);
        body.AngVelX = angX;
        body.AngVelY = angY;
        body.AngVelZ = angZ;

        // world integrator (retail outside applyAction; skeleton integrates here).
        // Use HkVehicleData.GravityY (ServerConfig baked at cache build), not hardcoded default.
        body.Integrate(dt, applyGravity: true, gravityY: data.GravityY);
    }

    /// <summary>
    /// Wheel casts + preUpdate spin integrate, then suspension spring/damper applied as
    /// postTick point impulses: <c>I = F · dt · n̂</c> at the wheel contact point (wheel+0x20).
    /// Cast/force use pre-impulse chassis velocity (retail: preUpdate then postTick).
    /// </summary>
    private static bool ApplyWheelCollideAndSuspension(
        VehiclePhysicsInstance inst,
        IVehicleCollisionQuery query,
        float downX, float downY, float downZ,
        float chassisLongVel,
        float dt)
    {
        var data = inst.Data;
        var body = inst.Body;
        bool anyContact = false;

        // Snapshot linVel for all casts — retail preUpdate finishes before any susp impulse.
        float velX = body.LinVelX, velY = body.LinVelY, velZ = body.LinVelZ;

        // Pass 1: collide + compute susp force (no velocity mutation).
        Span<float> forceX = stackalloc float[HkVehicleData.MaxWheels];
        Span<float> forceY = stackalloc float[HkVehicleData.MaxWheels];
        Span<float> forceZ = stackalloc float[HkVehicleData.MaxWheels];
        Span<bool> grounded = stackalloc bool[HkVehicleData.MaxWheels];

        for (var i = 0; i < data.WheelCount; i++)
        {
            var setup = data.Wheels[i];
            var wheel = inst.Wheels[i];

            RotateBodyVector(body, setup.HardpointX, setup.HardpointY, setup.HardpointZ,
                out var hx, out var hy, out var hz);
            float originX = body.PosX + hx;
            float originY = body.PosY + hy;
            float originZ = body.PosZ + hz;

            // ClosingSpeed (wheel+0xB4) = dot(linVel, contactNormal) for suspension damper.
            var contact = HkVehicleWheelCollide.CastWheel(
                query,
                originX, originY, originZ,
                downX, downY, downZ,
                setup.Radius,
                setup.SuspensionRestLength,
                velX, velY, velZ);

            float scaling = setup.SuspensionRestLength > 1e-6f
                ? 1f / setup.SuspensionRestLength
                : 1f;
            wheel.ApplyContact(contact, scaling);

            float maxDist = setup.Radius + setup.SuspensionRestLength;
            wheel.ContactPointX = originX + downX * maxDist * contact.Fraction;
            wheel.ContactPointY = originY + downY * maxDist * contact.Fraction;
            wheel.ContactPointZ = originZ + downZ * maxDist * contact.Fraction;

            // preUpdate Loop 3: ω = (LongContactVel + chassisLongVel) / radius; angle += dt·ω
            // Server gate: integrate only when in contact (client uses unlockChar[]).
            float spin = wheel.Spin;
            float spinAngle = wheel.SpinAngle;
            HkVehicleWheelKinematics.IntegrateSpin(
                ref spin,
                ref spinAngle,
                longContactVel: wheel.LongContactVel,
                chassisLongVel: chassisLongVel,
                radius: setup.Radius,
                dt: dt,
                integrate: contact.InContact);
            wheel.Spin = spin;
            wheel.SpinAngle = spinAngle;

            if (contact.InContact)
            {
                anyContact = true;
                grounded[i] = true;
                float force = HkVehicleSuspension.ComputeForce(
                    inContact: true,
                    restLength: setup.SuspensionRestLength,
                    strength: setup.SuspensionStrength,
                    dampCompression: setup.DampingCompression,
                    dampExtension: setup.DampingExtension,
                    currentLength: contact.Length,
                    scalingFactor: scaling,
                    closingSpeed: contact.ClosingSpeed,
                    invMass: body.InvMass);

                forceX[i] = force * contact.NormalX;
                forceY[i] = force * contact.NormalY;
                forceZ[i] = force * contact.NormalZ;
            }
            else
            {
                grounded[i] = false;
                wheel.ClearContact(setup.SuspensionRestLength, downX, downY, downZ);
            }
        }

        // Pass 2 (C2, retail postTickApplyForces 0x64bc70): per grounded wheel,
        // impulse = suspForce · dt · contactNormal (wheel+0x30), applied via the chassis
        // rigid body's applyPointImpulse at the wheel CONTACT POINT (wheel+0x20) — not the
        // COM. r×J at the contact point produces the retail weight-transfer torque.
        for (var i = 0; i < data.WheelCount; i++)
        {
            if (!grounded[i])
                continue;
            var wheel = inst.Wheels[i];
            body.ApplyPointImpulse(
                forceX[i] * dt, forceY[i] * dt, forceZ[i] * dt,
                wheel.ContactPointX, wheel.ContactPointY, wheel.ContactPointZ);
        }

        // Anti-sink (applyAction step 3): position-only chassis lift from suspension penetration.
        ApplyAntiSink(inst, data.WheelCount);

        return anyContact;
    }

    /// <summary>
    /// Retail-exact anti-sink — <c>VehicleAction::applyAction</c> @ <c>0x598650</c> step 3.
    /// Evidence: <c>fn_00598650_applyAction.md</c> §5 step 5 ("Suspension anti-sink lift":
    /// scan wheelsArray stride 0xC0 field <c>+0xB0</c>, <c>minComp = min(wheel[+0xB0])</c>;
    /// if <c>minComp &lt; 0</c>, raise chassis Y by <c>-minComp</c>) and
    /// <c>fn_offsets_vehicleAction.md</c> §8 step 3. <c>wheel+0xB0</c> == current suspension
    /// length == <see cref="HkWheelRuntimeState.CurrentLength"/> (see 0.4-suspension.md §4,
    /// 0.5-wheel-collide.md §3: <c>(radius+suspRestLen)·hitFraction − radius</c>, miss ⇒ full
    /// suspLen — so only genuinely penetrating wheels can go negative). The decompile shows a
    /// position write only (<c>rb+0xB4</c> / pose Y) — no velocity modification — so this must
    /// NOT touch <c>LinVelY</c>.
    /// <para>
    /// CONFIRMED by task B2 (2026-07-16): the fresh <c>applyAction</c>/<c>postTick</c> decompiles
    /// corroborate a position-only Y lift with no velocity write; the suspension force path
    /// (<c>0x64de50</c> → <c>0x64bc70</c> hardpoint impulse) is separate. See 0.4-suspension.md
    /// "Task B2 — anti-sink". No change needed.
    /// </para>
    /// </summary>
    private static void ApplyAntiSink(VehiclePhysicsInstance inst, int wheelCount)
    {
        float minLength = float.PositiveInfinity;
        for (var i = 0; i < wheelCount; i++)
        {
            float len = inst.Wheels[i].CurrentLength;
            if (len < minLength)
                minLength = len;
        }

        if (minLength < 0f)
            inst.Body.PosY -= minLength;
    }

    private static void ApplyEngineTorque(
        VehiclePhysicsInstance inst,
        float upX, float upY, float upZ,
        float chassisSpeed)
    {
        var data = inst.Data;
        // bodyUp · worldUp (0,1,0); retail upright = |dot|^4 when |dot| < 0.8
        float uprightDot = upY; // (upX,upY,upZ)·(0,1,0)
        float uprightFactor = HkVehicleEngine.ComputeUprightFactor(uprightDot);

        // Retail calcWheelTorque: t = torqueCurve2D(contact.x, contact.z).
        // Contact world X/Z bins are almost always OOR → factors[0] = MinTorqueFactor
        // (docs/reconstruction/physics/0.7-transmission.md). Throttle sign is applied in
        // friction; |throttle| is NOT the curve factor.
        for (var i = 0; i < data.WheelCount; i++)
        {
            var setup = data.Wheels[i];
            var wheel = inst.Wheels[i];
            if (!wheel.InContact)
            {
                wheel.DriveTorque = 0f;
                continue;
            }

            float curveFactor = HkVehicleEngine.EvaluateTorqueCurveFactor(
                data,
                wheel.ContactPointX,
                wheel.ContactPointZ);
            if (curveFactor <= 0f)
            {
                wheel.DriveTorque = 0f;
                continue;
            }

            wheel.DriveTorque = HkVehicleEngine.ComputeWheelTorque(
                torqueCurveFactor: curveFactor,
                frictionMu: setup.Friction,
                uprightFactor: uprightFactor,
                chassisSpeed: chassisSpeed,
                isRear: setup.IsRear,
                handbrake: inst.Handbrake,
                driverMod: 0f);
        }
    }

    private static void ApplySteeringWheelAngles(VehiclePhysicsInstance inst, float forwardSpeed)
    {
        var data = inst.Data;
        var doesSteer = new bool[data.WheelCount];
        for (var i = 0; i < data.WheelCount; i++)
            doesSteer[i] = data.Wheels[i].DoesSteer;

        float[] angles = HkVehicleSteering.ComputeWheelAngles(
            inst.SteerFinal,
            data.SteeringMaxAngle,
            data.SteeringFullSpeedLimit,
            forwardSpeed,
            doesSteer);

        for (var i = 0; i < data.WheelCount; i++)
            inst.Wheels[i].SteerAngle = angles[i];
    }

    /// <summary>
    /// 2-axle friction: build <see cref="AxleFrictionInput"/> from wheel contact,
    /// prior-tick drive packs, suspension normal load, and <b>wheel-relative</b> slip; call
    /// <see cref="HkVehicleFrictionSolver.Solve"/>; apply long/lat impulses at
    /// axle-averaged contact points (r×F); write wheel+0x94 / +0xa0.
    /// </summary>
    /// <remarks>
    /// SlipLong = (v_contact · forward) − spin·radius (not absolute chassis speed — that
    /// was the crawl root cause). SlipLat = v_contact · right with
    /// right = normalize(cross(up,fwd)) (body +X fallback). v_contact includes ω×r.
    /// |N| is |susp force| only (no gravity-share floor). μ0/μslope/μmax from the
    /// wheels friction table (viscosity slope + μmax = μ0×1.5). Drive pack is
    /// axle-averaged torque×scale from prior-tick <see cref="HkWheelRuntimeState.DriveTorque"/>,
    /// signed with throttle so <c>impLong -= driveBias</c> yields chassis push along +forward
    /// for retail thr base <c>−1</c>.
    /// </remarks>
    private static void TryApplyFriction(
        VehiclePhysicsInstance inst,
        float fwdX, float fwdY, float fwdZ,
        float upX, float upY, float upZ,
        float dt)
    {
        var data = inst.Data;
        var body = inst.Body;

        // Chassis right: cross(up, fwd); body +X if degenerate.
        PhysicsMath.Cross(upX, upY, upZ, fwdX, fwdY, fwdZ, out var rightX, out var rightY, out var rightZ);
        float rightLen = PhysicsMath.Length(rightX, rightY, rightZ);
        if (rightLen > 1e-8f)
        {
            float inv = 1f / rightLen;
            rightX *= inv;
            rightY *= inv;
            rightZ *= inv;
        }
        else
        {
            RotateBodyVector(body, 1f, 0f, 0f, out rightX, out rightY, out rightZ);
        }

        // Map throttle axis → drive direction along +forward without mutating Throttle.
        float throttleSign = inst.Throttle > 0f ? 1f : (inst.Throttle < 0f ? -1f : 0f);

        Span<float> frontT = stackalloc float[HkVehicleData.MaxWheels];
        Span<float> frontS = stackalloc float[HkVehicleData.MaxWheels];
        Span<float> rearT = stackalloc float[HkVehicleData.MaxWheels];
        Span<float> rearS = stackalloc float[HkVehicleData.MaxWheels];
        int nFront = 0, nRear = 0;

        // Chassis planar velocity in the ground-plane basis (COM). Wheel-relative
        // long slip subtracts spin·radius so pure rolling does not cancel speed (crawl fix).
        // Using COM (not ω×r contact vel) avoids pitch-from-suspension creating false long
        // slip that fights the drive pack under unit mass; full dual-body J·v is residual.
        float chassisLong = body.LinVelX * fwdX + body.LinVelY * fwdY + body.LinVelZ * fwdZ;
        float chassisLat = body.LinVelX * rightX + body.LinVelY * rightY + body.LinVelZ * rightZ;

        float frontLoadSum = 0f, rearLoadSum = 0f;
        float frontMuSum = 0f, rearMuSum = 0f;
        float frontSlipLongSum = 0f, rearSlipLongSum = 0f;
        float frontSlipLatSum = 0f, rearSlipLatSum = 0f;
        float frontContactX = 0f, frontContactY = 0f, frontContactZ = 0f;
        float rearContactX = 0f, rearContactY = 0f, rearContactZ = 0f;
        int nFrontContact = 0, nRearContact = 0;

        for (var i = 0; i < data.WheelCount; i++)
        {
            var setup = data.Wheels[i];
            var wheel = inst.Wheels[i];

            // Clear prior writeback; grounded wheels re-filled after Solve.
            wheel.LongImpulse = 0f;
            wheel.LatImpulse = 0f;

            if (setup.IsRear)
            {
                rearT[nRear] = wheel.DriveTorque;
                rearS[nRear] = setup.TorqueRatio;
                nRear++;
            }
            else
            {
                frontT[nFront] = wheel.DriveTorque;
                frontS[nFront] = setup.TorqueRatio;
                nFront++;
            }

            if (!wheel.InContact)
                continue;

            // Retail |N| = aggregated suspension force only (no gravity-share floor).
            float suspForce = HkVehicleSuspension.ComputeForce(
                inContact: true,
                restLength: setup.SuspensionRestLength,
                strength: setup.SuspensionStrength,
                dampCompression: setup.DampingCompression,
                dampExtension: setup.DampingExtension,
                currentLength: wheel.CurrentLength,
                scalingFactor: wheel.Scaling,
                closingSpeed: wheel.ClosingSpeed,
                invMass: body.InvMass);
            float load = MathF.Abs(suspForce);
            float mu = setup.Friction;

            // Wheel-relative long slip: chassis long − spin·radius (not absolute chassis speed).
            float slipLong = chassisLong - wheel.Spin * setup.Radius;
            float slipLat = chassisLat;

            if (setup.IsRear)
            {
                rearLoadSum += load;
                rearMuSum += mu;
                rearSlipLongSum += slipLong;
                rearSlipLatSum += slipLat;
                rearContactX += wheel.ContactPointX;
                rearContactY += wheel.ContactPointY;
                rearContactZ += wheel.ContactPointZ;
                nRearContact++;
            }
            else
            {
                frontLoadSum += load;
                frontMuSum += mu;
                frontSlipLongSum += slipLong;
                frontSlipLatSum += slipLat;
                frontContactX += wheel.ContactPointX;
                frontContactY += wheel.ContactPointY;
                frontContactZ += wheel.ContactPointZ;
                nFrontContact++;
            }
        }

        if (nFrontContact == 0 && nRearContact == 0)
            return;

        float frontPackMag = nFront > 0
            ? HkVehicleFrictionSolver.AggregateDrivePack(frontT[..nFront], frontS[..nFront])
            : 0f;
        float rearPackMag = nRear > 0
            ? HkVehicleFrictionSolver.AggregateDrivePack(rearT[..nRear], rearS[..nRear])
            : 0f;

        // Retail thr base −1 = forward. driveBias = f(DrivePack); impLong -= driveBias;
        // Need DrivePack < 0 when thr < 0 so driveBias < 0 → +long impulse along +fwd.
        // Torque curve magnitudes are unsigned; sign comes only from throttle axis.
        float frontPack = throttleSign * MathF.Abs(frontPackMag);
        float rearPack = throttleSign * MathF.Abs(rearPackMag);

        Span<AxleFrictionInput> inputs = stackalloc AxleFrictionInput[HkVehicleFrictionSolver.AxleCount];
        inputs[0] = BuildAxleInput(
            body,
            inContact: nFrontContact > 0,
            drivePack: frontPack,
            slipLong: nFrontContact > 0 ? frontSlipLongSum / nFrontContact : 0f,
            slipLat: nFrontContact > 0 ? frontSlipLatSum / nFrontContact : 0f,
            normalLoad: nFrontContact > 0 ? frontLoadSum / nFrontContact : 0f,
            mu0: nFrontContact > 0 ? frontMuSum / nFrontContact : 0f,
            avgContactX: nFrontContact > 0 ? frontContactX / nFrontContact : body.PosX,
            avgContactY: nFrontContact > 0 ? frontContactY / nFrontContact : body.PosY,
            avgContactZ: nFrontContact > 0 ? frontContactZ / nFrontContact : body.PosZ,
            fwdX, fwdY, fwdZ, rightX, rightY, rightZ);
        inputs[1] = BuildAxleInput(
            body,
            inContact: nRearContact > 0,
            drivePack: rearPack,
            slipLong: nRearContact > 0 ? rearSlipLongSum / nRearContact : 0f,
            slipLat: nRearContact > 0 ? rearSlipLatSum / nRearContact : 0f,
            normalLoad: nRearContact > 0 ? rearLoadSum / nRearContact : 0f,
            mu0: nRearContact > 0 ? rearMuSum / nRearContact : 0f,
            avgContactX: nRearContact > 0 ? rearContactX / nRearContact : body.PosX,
            avgContactY: nRearContact > 0 ? rearContactY / nRearContact : body.PosY,
            avgContactZ: nRearContact > 0 ? rearContactZ / nRearContact : body.PosZ,
            fwdX, fwdY, fwdZ, rightX, rightY, rightZ);

        Span<AxleFrictionImpulse> impulses = stackalloc AxleFrictionImpulse[HkVehicleFrictionSolver.AxleCount];
        HkVehicleFrictionSolver.Solve(dt, inputs, body.InvMass, impulses);

        ApplyAxleImpulses(inst, axleIndex: 0, impulses[0], nFrontContact,
            fwdX, fwdY, fwdZ, rightX, rightY, rightZ);
        ApplyAxleImpulses(inst, axleIndex: 1, impulses[1], nRearContact,
            fwdX, fwdY, fwdZ, rightX, rightY, rightZ);
    }

    private static AxleFrictionInput BuildAxleInput(
        HkRigidBody body,
        bool inContact,
        float drivePack,
        float slipLong,
        float slipLat,
        float normalLoad,
        float mu0,
        float avgContactX,
        float avgContactY,
        float avgContactZ,
        float fwdX, float fwdY, float fwdZ,
        float rightX, float rightY, float rightZ)
    {
        // Retail μ table: μ0 from wheel friction (rear already × RearWheelFrictionScalar
        // in HkVehicleData), slope = viscosity 0.001, μmax = μ0 × 1.5.
        float muMax = mu0 > 0f
            ? mu0 * HkPhysicsConstants.WheelsMuMaxScale
            : HkPhysicsConstants.WheelsMuMaxScale;

        float invKLong = 0f;
        float invKLat = 0f;
        float coupling = 0f;
        if (inContact)
        {
            float rx = avgContactX - body.PosX;
            float ry = avgContactY - body.PosY;
            float rz = avgContactZ - body.PosZ;
            invKLong = HkVehicleFrictionSolver.ComputeInvKeffFromContact(
                body.InvMass, body.InvInertiaX, body.InvInertiaY, body.InvInertiaZ,
                rx, ry, rz, fwdX, fwdY, fwdZ);
            invKLat = HkVehicleFrictionSolver.ComputeInvKeffFromContact(
                body.InvMass, body.InvInertiaX, body.InvInertiaY, body.InvInertiaZ,
                rx, ry, rz, rightX, rightY, rightZ);
            coupling = HkVehicleFrictionSolver.ComputeKeffCoupling(
                body.InvMass, body.InvInertiaX, body.InvInertiaY, body.InvInertiaZ,
                rx, ry, rz, fwdX, fwdY, fwdZ, rightX, rightY, rightZ);
        }

        return new AxleFrictionInput
        {
            InContact = inContact,
            DriveEnabled = inContact && drivePack != 0f,
            DrivePack = drivePack,
            SlipLongitudinal = slipLong,
            SlipLateral = slipLat,
            NormalLoad = normalLoad,
            Mu0 = mu0,
            MuSlope = HkPhysicsConstants.WheelsViscosityFriction,
            MuMax = muMax,
            InvKeffLong = invKLong,
            InvKeffLat = invKLat,
            Coupling = coupling,
        };
    }

    /// <summary>
    /// Write axle long/lat impulses onto wheels and apply <b>once per axle</b> at the
    /// average contact point (point impulse with r×F weight transfer).
    /// </summary>
    private static void ApplyAxleImpulses(
        VehiclePhysicsInstance inst,
        int axleIndex,
        in AxleFrictionImpulse axleImpulse,
        int nContact,
        float fwdX, float fwdY, float fwdZ,
        float rightX, float rightY, float rightZ)
    {
        if (nContact <= 0)
            return;

        var data = inst.Data;
        var body = inst.Body;

        float jx = axleImpulse.Longitudinal * fwdX + axleImpulse.Lateral * rightX;
        float jy = axleImpulse.Longitudinal * fwdY + axleImpulse.Lateral * rightY;
        float jz = axleImpulse.Longitudinal * fwdZ + axleImpulse.Lateral * rightZ;

        float sumX = 0f, sumY = 0f, sumZ = 0f;
        int n = 0;
        for (var i = 0; i < data.WheelCount; i++)
        {
            var setup = data.Wheels[i];
            if (setup.AxleIndex != axleIndex)
                continue;

            var wheel = inst.Wheels[i];
            wheel.LatImpulse = axleImpulse.Lateral;
            if (!wheel.InContact)
            {
                wheel.LongImpulse = 0f;
                continue;
            }

            wheel.LongImpulse = axleImpulse.Longitudinal;
            sumX += wheel.ContactPointX;
            sumY += wheel.ContactPointY;
            sumZ += wheel.ContactPointZ;
            n++;
        }

        if (n <= 0)
            return;

        // C4: retail postTick applies axle friction impulse at the averaged contact
        // point (r×F), not COM-only. Suspension already uses the same point-impulse path (C2).
        float invN = 1f / n;
        body.ApplyPointImpulse(jx, jy, jz, sumX * invN, sumY * invN, sumZ * invN);
    }

    /// <summary>Rotate body-space vector by chassis quaternion (x,y,z,w).</summary>
    private static void RotateBodyVector(
        HkRigidBody body,
        float vx, float vy, float vz,
        out float ox, out float oy, out float oz)
    {
        float qx = body.QuatX, qy = body.QuatY, qz = body.QuatZ, qw = body.QuatW;
        float tx = 2f * (qy * vz - qz * vy);
        float ty = 2f * (qz * vx - qx * vz);
        float tz = 2f * (qx * vy - qy * vx);
        ox = vx + qw * tx + (qy * tz - qz * ty);
        oy = vy + qw * ty + (qz * tx - qx * tz);
        oz = vz + qw * tz + (qx * ty - qy * tx);
    }
}

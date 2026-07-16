namespace AutoCore.Game.Npc;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Physics.Vehicle;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Physics-tier mover for path-following NPC vehicles (sim-authoritative).
/// <para>
/// Steers toward the hard navigator's look-ahead aim via the retail
/// <see cref="VehicleDriveController"/> axes, steps the Havok vehicle sim, and
/// publishes <c>inst.Body</c> pose/velocity/angular velocity verbatim. No
/// kinematic force-restore of pose; recovery teleports + <see cref="VehiclePhysicsInstance.ReGround"/>
/// only on first-create, non-finite state, out-of-world fall, or path divergence.
/// </para>
/// <para>
/// Opt-in only via <see cref="ServerConfig"/> (enabled + controllerTier=physics).
/// Reverse throttle is a service-brake pedal only (C8) — this controller must not
/// also apply kinematic reverse-throttle deceleration.
/// </para>
/// </summary>
public static class NpcVehiclePhysicsController
{
    /// <summary>
    /// Max |body.Pos − hard.NewPosition| before path-divergence recovery (world units).
    /// </summary>
    public static float ResyncDriftThreshold { get; set; } = 8f;

    /// <summary>Y drop below terrain support that triggers out-of-world recovery.</summary>
    public const float OutOfWorldSupportMargin = 50f;

    /// <summary>
    /// When body-up · world-up falls below this, treat as flipped and recover
    /// (teleport to hard pose + ReGround). Keeps NPCs from thrashing indefinitely.
    /// </summary>
    public const float MinUprightDot = 0.35f;

    /// <summary>P-gain on planar heading error → desired yaw rate (1/s).</summary>
    public const float PathHeadingGain = 5.0f;

    /// <summary>Max |yaw rate| from path assist at full speed (rad/s).</summary>
    public const float PathHeadingMaxYawRate = 2.0f;

    /// <summary>
    /// Planar speed (m/s) at which path-heading assist reaches full max yaw rate.
    /// Below this, max yaw rate scales down — prevents clock-spin when nearly stopped.
    /// </summary>
    public const float PathHeadingFullSpeed = 4f;

    /// <summary>Below this planar speed, path assist max yaw rate is heavily limited.</summary>
    public const float PathHeadingMinSpeed = 0.25f;

    /// <summary>
    /// When |heading error| exceeds this (rad) and the body has any crawl speed, allow a
    /// small minimum yaw-authority floor so NPCs can start a turn without full clock-spin.
    /// </summary>
    public const float PathHeadingLargeErrorRad = 0.6f; // ~34°

    /// <summary>Minimum speedScale when large heading error and planar speed &gt; crawl.</summary>
    public const float PathHeadingErrorSpeedFloor = 0.22f;

    /// <summary>
    /// Lateral track soft-pull rate (1/s) toward the hard navigator's planar position.
    /// Only the component orthogonal to path tangent is corrected — progress along path
    /// stays sim-authored.
    /// </summary>
    public const float PathTrackLateralPull = 2.8f;

    /// <summary>Max lateral correction distance per second (world units).</summary>
    public const float PathTrackLateralMaxSpeed = 12f;

    /// <summary>
    /// Blend planar velocity toward body forward after heading assist (0–1 per second
    /// equivalent via 1-exp). Cuts crab-walk when yaw catches the path.
    /// </summary>
    public const float PathVelocityAlignRate = 3.5f;

    /// <summary>
    /// |heading error| (rad) above which throttle is scaled down so NPCs turn before
    /// accelerating off the path. Kept fairly large so mild cornering does not starve speed.
    /// </summary>
    public const float PathThrottleCutErrorRad = 0.95f; // ~54°

    /// <summary>Minimum thr scale when fully misaligned (still some drive to build speed for yaw).</summary>
    public const float PathThrottleCutMinScale = 0.55f;

    /// <summary>
    /// Soft-match planar speed toward the hard navigator when roughly aligned (1/s).
    /// Closes the gap between reduced-sim crawl and programmed path speed.
    /// </summary>
    public const float PathSpeedMatchRate = 2.2f;

    /// <summary>Only speed-match when |heading error| is below this (rad).</summary>
    public const float PathSpeedMatchMaxErrorRad = 0.65f;

    /// <summary>Max same-frame yaw delta from path assist (rad) — prevents snap turns.</summary>
    public const float PathHeadingMaxYawStep = 0.028f; // ~1.6°/tick @ 60 Hz

    /// <summary>Max same-frame pitch/roll delta from terrain stance (rad).</summary>
    public const float TerrainStanceMaxStep = 0.12f;

    /// <summary>Terrain stance blend rate (1/s) toward slope pitch/roll.</summary>
    public const float TerrainStanceRate = 18f;

    /// <summary>Max pitch/roll from stance (~50°) — steep ramps without full flip.</summary>
    public const float TerrainStanceMaxTilt = 0.87f;

    /// <summary>Target ride height above average terrain sample (m).</summary>
    public const float TerrainStanceRideHeight = 0.65f;

    /// <summary>
    /// A corner sample is "in contact" when bodyY − sampleY is below this (m).
    /// Used to decide grounded vs free-flight (not average clearance alone).
    /// </summary>
    public const float TerrainContactClearance = 1.05f;

    /// <summary>Need this many near-contact corners (of 4) to count as grounded.</summary>
    public const int TerrainMinContactCorners = 3;

    /// <summary>Ballistic free-flight: blend rate for nose toward velocity direction (1/s).</summary>
    public const float BallisticPitchRate = 6f;

    /// <summary>Max ballistic pitch step per tick (rad).</summary>
    public const float BallisticPitchMaxStep = 0.08f;

    /// <summary>Half-length (m) along body forward for terrain pitch samples.</summary>
    public const float TerrainStanceSampleHalfLength = 1.8f;

    /// <summary>Half-width (m) along body right for terrain roll samples.</summary>
    public const float TerrainStanceSampleHalfWidth = 1.0f;

    /// <summary>
    /// Advance one vehicle via sim-authoritative path-following.
    /// On failure (no data / bad dt) returns <paramref name="hard"/> unchanged (fail closed).
    /// </summary>
    public static PathStepResult Apply(
        PathStepResult hard,
        Vehicle vehicle,
        MapPathTemplate path,
        long nowMs,
        float dt,
        SectorMap map,
        NpcAiState npcAi)
    {
        if (vehicle == null || path == null || path.Points.Count == 0 || dt <= 0f || !float.IsFinite(dt))
            return hard;

        bool firstCreate = vehicle.PhysicsInstance == null;
        var inst = vehicle.GetOrCreatePhysicsInstance();
        if (inst == null)
        {
            if (ServerConfig.DebugLogging)
            {
                Logger.WriteLog(LogType.Error,
                    $"NpcVehiclePhysics: no HkVehicleData for cbid={vehicle.CBID}; fail closed to hard path");
            }
            return hard;
        }

        if (hard.Arrived && hard.WaitUntilMs > nowMs)
        {
            return new PathStepResult
            {
                NewPosition = vehicle.Position,
                Velocity = default,
                Rotation = vehicle.Rotation,
                NewIndex = hard.NewIndex,
                NewDirection = hard.NewDirection,
                Arrived = true,
                FireReactionCoid = hard.FireReactionCoid,
                WaitUntilMs = hard.WaitUntilMs,
                NowReversing = hard.NowReversing,
                Throttle = 0f,
                Steering = 0f,
                SharpTurn = 1,
                HasDriveInputs = true,
                AngularVelocity = default,
            };
        }

        var body = inst.Body;
        float supportY = ResolveSupportY(map, body.PosX, body.PosZ, hard.NewPosition.Y);
        IVehicleCollisionQuery query = BuildCollisionQuery(map, supportY, excludeSelf: vehicle);

        // Recovery: first-create seats; non-finite / freefall / flip / path-divergence.
        // Always re-upright on recovery — keeping inverted orientation was leaving NPCs
        // driving on their roofs after a flip recovery.
        if (firstCreate)
        {
            ForceUprightPreserveYaw(body);
            inst.ReGround(query);
            RaiseChassisToWheelRest(inst);
        }
        else if (NeedsRecovery(body, hard, supportY))
        {
            SetPoseFromHard(inst, hard);
            ForceUprightPreserveYaw(body);
            supportY = ResolveSupportY(map, body.PosX, body.PosZ, hard.NewPosition.Y);
            query = BuildCollisionQuery(map, supportY, excludeSelf: vehicle);
            inst.ReGround(query);
            RaiseChassisToWheelRest(inst);
        }
        else if (BodyUpDotWorldUp(body) < 0.55f)
        {
            // Continuous safety net: never keep driving inverted.
            ForceUprightPreserveYaw(body);
        }

        var lane = npcAi?.PathLaneOffset ?? 0f;
        var bodyPos = new Vector3(body.PosX, body.PosY, body.PosZ);
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(bodyPos, hard, path, lane);

        // Drive axes from current sim body basis/velocity (retail 0x4fc650).
        var bodyRot = new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW);
        ExtractBasis(bodyRot, out var right, out var forward);
        var velocity = new Vector3(body.LinVelX, body.LinVelY, body.LinVelZ);
        var acceptDist = ResolveAcceptDistance(hard, path);
        var cornerScale = ResolveCruiseScale(hard, path);
        // allowReverse=false: path NPCs must not thrash thr sign when yaw lags the aim
        // (live symptom: reverse/forward oscillation while wheels turn). alwaysDrive keeps
        // forward thr engaged so they do not idle-stop when slightly off-aim.
        var (thr, steer, sharp) = VehicleDriveController.ComputeAxes(
            bodyPos,
            right,
            forward,
            velocity,
            aim,
            acceptDist,
            cruiseScale: cornerScale > 0f ? cornerScale : 1f,
            allowReverse: false,
            alwaysDrive: true);

        // Cut thr when badly misaligned so they reorient instead of driving off-path.
        thr *= ScaleThrottleForHeadingError(forward, aim, bodyPos);

        // Step free-running; do not force-restore pose after. C8 brake owns reverse thr as pedal —
        // no kinematic reverse-throttle deceleration here.
        inst.Step(thr, steer, handbrake: sharp != 0, dt, query);

        // Path assists (reduced tire model is not enough for reliable NPC pathing).
        // Terrain stance returns grounded vs airborne so we don't Y-stick through a ledge
        // or speed-match while ballistic.
        ApplyPathHeadingAssist(body, aim, dt);
        AlignPlanarVelocityToHeading(body, dt);
        bool grounded = ApplyTerrainStanceAssist(body, query, dt);
        if (grounded)
        {
            ApplyPathTrackLateralPull(body, hard, dt);
            ApplyPathSpeedMatch(body, hard, aim, dt);
        }

        // Post-assist upright guard (stance/heading must never leave the car inverted).
        if (BodyUpDotWorldUp(body) < 0.55f)
            ForceUprightPreserveYaw(body);

        // Publish inst.Body pose/rot/vel/angVel (post-assist).
        return new PathStepResult
        {
            NewPosition = new Vector3(body.PosX, body.PosY, body.PosZ),
            Velocity = new Vector3(body.LinVelX, body.LinVelY, body.LinVelZ),
            Rotation = new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            NewIndex = hard.NewIndex,
            NewDirection = hard.NewDirection,
            Arrived = hard.Arrived,
            FireReactionCoid = hard.FireReactionCoid,
            WaitUntilMs = hard.WaitUntilMs,
            NowReversing = hard.NowReversing,
            Throttle = thr,
            Steering = steer,
            SharpTurn = sharp,
            HasDriveInputs = true,
            AngularVelocity = new Vector3(body.AngVelX, body.AngVelY, body.AngVelZ),
        };
    }

    /// <summary>
    /// Scale retail-forward thr down when planar heading error is large (turn-before-push).
    /// </summary>
    internal static float ScaleThrottleForHeadingError(Vector3 forward, Vector3 aim, Vector3 bodyPos)
    {
        float dx = aim.X - bodyPos.X;
        float dz = aim.Z - bodyPos.Z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 0.5f)
            return 1f;
        float inv = 1f / len;
        float aimX = dx * inv;
        float aimZ = dz * inv;
        float cross = aimX * forward.Z - aimZ * forward.X;
        float dot = forward.X * aimX + forward.Z * aimZ;
        float err = MathF.Abs(MathF.Atan2(cross, dot));
        if (err <= PathThrottleCutErrorRad)
            return 1f;
        // Linear from 1 at cut threshold → PathThrottleCutMinScale at π
        float t = Math.Clamp(
            (err - PathThrottleCutErrorRad) / (MathF.PI - PathThrottleCutErrorRad),
            0f, 1f);
        return PathThrottleCutMinScale + (1f - PathThrottleCutMinScale) * (1f - t);
    }

    /// <summary>
    /// Drive <see cref="HkRigidBody.AngVelY"/> toward the planar aim heading. Speed-scales
    /// max rate; damps pitch/roll rates when upright so assist does not reintroduce tumble.
    /// Integrator already advanced this substep — yaw rate applies on the next integrate
    /// inside the following <see cref="VehiclePhysicsInstance.Step"/> / residual dt path.
    /// Also applies a single small yaw delta to the quaternion this frame so heading responds
    /// within the same publish (NPC ghosts would otherwise lag a full tick).
    /// </summary>
    internal static void ApplyPathHeadingAssist(HkRigidBody body, Vector3 aim, float dt)
    {
        if (body == null || !(dt > 0f) || !float.IsFinite(dt))
            return;

        float dx = aim.X - body.PosX;
        float dz = aim.Z - body.PosZ;
        float aimLen = MathF.Sqrt(dx * dx + dz * dz);
        if (aimLen < 0.5f)
        {
            // No aim: kill residual spin when nearly stopped.
            float spdHold = MathF.Sqrt(body.LinVelX * body.LinVelX + body.LinVelZ * body.LinVelZ);
            if (spdHold < PathHeadingMinSpeed)
                body.AngVelY *= 0.5f;
            return;
        }

        float invAim = 1f / aimLen;
        float aimX = dx * invAim;
        float aimZ = dz * invAim;

        var rot = new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW);
        ExtractBasis(rot, out _, out var forward);

        // Signed planar heading error about +Y (RH): positive = need CCW yaw (toward +X from +Z).
        // sin = aim × fwd in XZ = aim.X·fwd.Z − aim.Z·fwd.X (points which way to rotate fwd→aim).
        float cross = aimX * forward.Z - aimZ * forward.X;
        float dot = forward.X * aimX + forward.Z * aimZ;
        float err = MathF.Atan2(cross, dot); // [-π, π]

        float planar = MathF.Sqrt(body.LinVelX * body.LinVelX + body.LinVelZ * body.LinVelZ);
        // Speed gate: ~0 yaw authority when stopped (no clock-spin), full by PathHeadingFullSpeed.
        float speedScale = planar <= PathHeadingMinSpeed
            ? 0f
            : Math.Clamp((planar - PathHeadingMinSpeed) / (PathHeadingFullSpeed - PathHeadingMinSpeed), 0f, 1f);

        // Large heading error while crawling: allow a small floor so NPCs can start a turn
        // without needing full cruise speed (still zero when fully stopped).
        if (speedScale < PathHeadingErrorSpeedFloor
            && planar > PathHeadingMinSpeed
            && MathF.Abs(err) >= PathHeadingLargeErrorRad)
        {
            speedScale = PathHeadingErrorSpeedFloor;
        }

        float maxRate = PathHeadingMaxYawRate * speedScale;
        float desiredYawRate = Math.Clamp(err * PathHeadingGain, -maxRate, maxRate);

        // When nearly stopped, actively kill any residual yaw (friction yaw-scale is already 0).
        if (speedScale <= 0f)
        {
            body.AngVelY *= 0.25f;
            if (MathF.Abs(body.AngVelY) < 0.02f)
                body.AngVelY = 0f;
        }
        else
        {
            // Stronger blend toward path command (tire model alone under-yaws).
            body.AngVelY = body.AngVelY * 0.2f + desiredYawRate * 0.8f;
        }

        // Only damp extreme pitch/roll spin (do not kill terrain stance rates every tick).
        if (BodyUpDotWorldUp(body) >= MinUprightDot)
        {
            if (MathF.Abs(body.AngVelX) > 4f) body.AngVelX *= 0.5f;
            if (MathF.Abs(body.AngVelZ) > 4f) body.AngVelZ *= 0.5f;
        }

        // Same-frame heading nudge — hard-capped per tick so corners ease in (no snap).
        float yawStep = Math.Clamp(body.AngVelY * dt, -maxRate * dt, maxRate * dt);
        yawStep = Math.Clamp(yawStep, -PathHeadingMaxYawStep, PathHeadingMaxYawStep);
        // Also never jump more than a fraction of the remaining error in one tick.
        float maxErrStep = MathF.Abs(err) * 0.35f;
        if (maxErrStep > 1e-5f)
            yawStep = Math.Clamp(yawStep, -maxErrStep, maxErrStep);
        if (speedScale > 0f && MathF.Abs(yawStep) > 1e-6f)
            ApplyWorldYawDelta(body, yawStep);
    }

    /// <summary>
    /// Soft-match planar speed to the hard navigator when heading is roughly aligned.
    /// </summary>
    internal static void ApplyPathSpeedMatch(HkRigidBody body, PathStepResult hard, Vector3 aim, float dt)
    {
        if (body == null || !(dt > 0f) || !float.IsFinite(dt))
            return;

        float hardSpd = MathF.Sqrt(
            hard.Velocity.X * hard.Velocity.X + hard.Velocity.Z * hard.Velocity.Z);
        if (hardSpd < 0.5f)
            return;

        ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        float dx = aim.X - body.PosX;
        float dz = aim.Z - body.PosZ;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 0.5f)
            return;
        float inv = 1f / len;
        float aimX = dx * inv, aimZ = dz * inv;
        float cross = aimX * forward.Z - aimZ * forward.X;
        float dot = forward.X * aimX + forward.Z * aimZ;
        float err = MathF.Abs(MathF.Atan2(cross, dot));
        if (err > PathSpeedMatchMaxErrorRad)
            return;

        float fx = forward.X, fz = forward.Z;
        float fLen = MathF.Sqrt(fx * fx + fz * fz);
        if (fLen < 1e-4f)
            return;
        inv = 1f / fLen;
        fx *= inv;
        fz *= inv;

        float planar = body.LinVelX * fx + body.LinVelZ * fz; // along nose
        if (planar >= hardSpd * 0.98f)
            return;

        float alpha = 1f - MathF.Exp(-PathSpeedMatchRate * dt);
        float target = planar + (hardSpd - planar) * alpha;
        // Rewrite planar along-nose component; keep lateral residual.
        float latX = -fz, latZ = fx;
        float lat = body.LinVelX * latX + body.LinVelZ * latZ;
        body.LinVelX = fx * target + latX * lat;
        body.LinVelZ = fz * target + latZ * lat;
    }

    /// <summary>
    /// Soft pitch/roll to terrain when enough corners are in contact; otherwise free-flight
    /// (ballistic nose from velocity, no Y-stick / path glue).
    /// Returns <c>true</c> when grounded.
    /// </summary>
    internal static bool ApplyTerrainStanceAssist(
        HkRigidBody body,
        IVehicleCollisionQuery query,
        float dt)
    {
        if (body == null || query == null || !(dt > 0f) || !float.IsFinite(dt))
            return false;

        if (BodyUpDotWorldUp(body) < 0.55f)
            return false;

        float yaw = ExtractYawRadians(body);
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float fx = sy, fz = cy;
        float rx = cy, rz = -sy;

        float halfL = TerrainStanceSampleHalfLength;
        float halfW = TerrainStanceSampleHalfWidth;
        float ox = body.PosX, oy = body.PosY + 12f, oz = body.PosZ;

        if (!SampleTerrainY(query, ox + fx * halfL + rx * halfW, oy, oz + fz * halfL + rz * halfW, out float yFL))
            return false;
        if (!SampleTerrainY(query, ox + fx * halfL - rx * halfW, oy, oz + fz * halfL - rz * halfW, out float yFR))
            return false;
        if (!SampleTerrainY(query, ox - fx * halfL + rx * halfW, oy, oz - fz * halfL + rz * halfW, out float yRL))
            return false;
        if (!SampleTerrainY(query, ox - fx * halfL - rx * halfW, oy, oz - fz * halfL - rz * halfW, out float yRR))
            return false;

        // Per-corner contact: only corners with terrain near the chassis count.
        Span<float> ys = stackalloc float[4] { yFL, yFR, yRL, yRR };
        int near = 0;
        float nearSum = 0f;
        for (var i = 0; i < 4; i++)
        {
            // Negative clearance = chassis below sample (penetrating / under-slope) — still contact.
            float clr = body.PosY - ys[i];
            if (clr >= -0.85f && clr <= TerrainContactClearance)
            {
                near++;
                nearSum += ys[i];
            }
        }

        bool grounded = near >= TerrainMinContactCorners;
        if (!grounded)
        {
            // Free flight / ramp lip: no ground glue — gravity owns the arc; pitch into velocity.
            ApplyBallisticPitch(body, yaw, dt);
            return false;
        }

        // For stance, use only near-contact corners for slope estimate when possible.
        float yFront = 0.5f * (yFL + yFR);
        float yRear = 0.5f * (yRL + yRR);
        float yLeft = 0.5f * (yFL + yRL);
        float yRight = 0.5f * (yFR + yRR);
        // If a side is airborne (ledge), bias samples toward supported corners.
        bool frontNear = (body.PosY - yFL) <= TerrainContactClearance
                         || (body.PosY - yFR) <= TerrainContactClearance;
        bool rearNear = (body.PosY - yRL) <= TerrainContactClearance
                        || (body.PosY - yRR) <= TerrainContactClearance;
        if (frontNear && !rearNear)
        {
            yRear = yFront; // don't pitch into the void
        }
        else if (rearNear && !frontNear)
        {
            yFront = yRear;
        }

        float maxTilt = TerrainStanceMaxTilt;
        float desiredNoseUp = Math.Clamp(MathF.Atan2(yFront - yRear, 2f * halfL), -maxTilt, maxTilt);
        float desiredLeftUp = Math.Clamp(MathF.Atan2(yLeft - yRight, 2f * halfW), -maxTilt, maxTilt);

        ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out var right, out var forward);
        float curNoseUp = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
        float curLeftUp = MathF.Asin(Math.Clamp(-right.Y, -1f, 1f));

        float alpha = 1f - MathF.Exp(-TerrainStanceRate * dt);
        // Strong catch-up: approach desired tilt quickly so ramps read as "planted".
        float newNoseUp = curNoseUp + Math.Clamp((desiredNoseUp - curNoseUp) * MathF.Max(alpha, 0.55f),
            -TerrainStanceMaxStep, TerrainStanceMaxStep);
        float newLeftUp = curLeftUp + Math.Clamp((desiredLeftUp - curLeftUp) * MathF.Max(alpha, 0.55f),
            -TerrainStanceMaxStep, TerrainStanceMaxStep);
        newNoseUp = Math.Clamp(newNoseUp, -maxTilt, maxTilt);
        newLeftUp = Math.Clamp(newLeftUp, -maxTilt, maxTilt);

        float yprPitch = -newNoseUp;
        float yprRoll = -newLeftUp;
        var q = TerrainContactPlane.FromYawPitchRoll(yaw, yprPitch, yprRoll);
        body.QuatX = q.X;
        body.QuatY = q.Y;
        body.QuatZ = q.Z;
        body.QuatW = q.W;

        if (BodyUpDotWorldUp(body) < 0.5f)
            ForceUprightPreserveYaw(body);

        // Seat on average of *near* samples so we don't pull into a void corner.
        float supportY = nearSum / near;
        float targetY = supportY + TerrainStanceRideHeight;
        float yErr = targetY - body.PosY;
        if (MathF.Abs(yErr) > 0.01f)
            body.PosY += Math.Clamp(yErr * MathF.Min(1f, alpha * 2f), -0.22f, 0.22f);

        // Kill residual upward float when firmly grounded (helps plant on ramps).
        if (body.LinVelY > 0.5f && yErr > -0.1f)
            body.LinVelY *= 0.5f;

        return true;
    }

    /// <summary>
    /// Free-flight: soft-pitch the nose toward the velocity vector so ramps/ledges read as
    /// ballistic arcs instead of flat skating through the air.
    /// </summary>
    internal static void ApplyBallisticPitch(HkRigidBody body, float yaw, float dt)
    {
        if (body == null || !(dt > 0f))
            return;

        float planar = MathF.Sqrt(body.LinVelX * body.LinVelX + body.LinVelZ * body.LinVelZ);
        // desired geometric nose-up: positive when climbing (velY > 0), negative when falling.
        float desiredNoseUp = 0f;
        if (planar > 0.5f || MathF.Abs(body.LinVelY) > 0.5f)
            desiredNoseUp = Math.Clamp(MathF.Atan2(body.LinVelY, MathF.Max(planar, 0.25f)), -0.85f, 0.85f);

        ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        float curNoseUp = MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
        float alpha = 1f - MathF.Exp(-BallisticPitchRate * dt);
        float newNoseUp = curNoseUp + Math.Clamp((desiredNoseUp - curNoseUp) * alpha,
            -BallisticPitchMaxStep, BallisticPitchMaxStep);

        var q = TerrainContactPlane.FromYawPitchRoll(yaw, pitch: -newNoseUp, roll: 0f);
        body.QuatX = q.X;
        body.QuatY = q.Y;
        body.QuatZ = q.Z;
        body.QuatW = q.W;
        // Flatten residual roll in air.
        body.AngVelX *= 0.9f;
        body.AngVelZ *= 0.85f;
    }

    private static bool SampleTerrainY(
        IVehicleCollisionQuery query,
        float x, float yStart, float z,
        out float hitY)
    {
        hitY = 0f;
        const float maxDist = 40f;
        if (!query.CastRay(x, yStart, z, 0f, -1f, 0f, maxDist, out var hit))
            return false;
        hitY = hit.PointY;
        return float.IsFinite(hitY);
    }

    /// <summary>
    /// Flatten pitch/roll to world-up while preserving planar yaw. Used after flips and as a
    /// continuous safety net so path NPCs never cruise inverted.
    /// </summary>
    internal static void ForceUprightPreserveYaw(HkRigidBody body)
    {
        if (body == null)
            return;
        float yaw = ExtractYawRadians(body);
        var q = TerrainContactPlane.YawOnly(yaw);
        body.QuatX = q.X;
        body.QuatY = q.Y;
        body.QuatZ = q.Z;
        body.QuatW = q.W;
        body.AngVelX = 0f;
        body.AngVelZ = 0f;
    }

    /// <summary>Planar yaw from body forward (+Z). Does not add π when inverted (roll-flip keeps nose).</summary>
    internal static float ExtractYawRadians(HkRigidBody body)
    {
        ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        float fx = forward.X;
        float fz = forward.Z;
        float len = MathF.Sqrt(fx * fx + fz * fz);
        // Nearly pure pitch flip: planar forward collapses — use body +X as fallback.
        if (len < 0.15f)
        {
            ExtractBasis(
                new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
                out var right, out _);
            return MathF.Atan2(right.X, right.Z) - MathF.PI * 0.5f;
        }
        return MathF.Atan2(fx, fz);
    }

    /// <summary>
    /// Soft-pull body XZ toward the hard navigator, correcting only the component orthogonal
    /// to the hard path tangent (hard velocity, else hard−body when still).
    /// </summary>
    internal static void ApplyPathTrackLateralPull(HkRigidBody body, PathStepResult hard, float dt)
    {
        if (body == null || !(dt > 0f) || !float.IsFinite(dt))
            return;

        float errX = hard.NewPosition.X - body.PosX;
        float errZ = hard.NewPosition.Z - body.PosZ;
        float errLen = MathF.Sqrt(errX * errX + errZ * errZ);
        if (errLen < 1e-4f)
            return;

        // Path tangent: prefer hard velocity; fallback to error direction (no lateral then).
        float tx = hard.Velocity.X;
        float tz = hard.Velocity.Z;
        float tLen = MathF.Sqrt(tx * tx + tz * tz);
        if (tLen < 0.05f)
        {
            // Still navigator: pull fully toward hard (gentle).
            float alpha = 1f - MathF.Exp(-PathTrackLateralPull * 0.5f * dt);
            float step = Math.Min(errLen, PathTrackLateralMaxSpeed * dt);
            float s = (step / errLen) * alpha;
            body.PosX += errX * s;
            body.PosZ += errZ * s;
            return;
        }

        float invT = 1f / tLen;
        tx *= invT;
        tz *= invT;
        // Lateral unit (path right) = (tz, -tx) in XZ... wait (tx,tz) cross +Y → (tz, -tx)?
        // right = (tz, 0, -tx) so right·error = tz*errX - tx*errZ
        float lat = tz * errX - tx * errZ;
        if (MathF.Abs(lat) < 1e-4f)
            return;

        // Pull only lateral component of error.
        float latX = tz * lat;
        float latZ = -tx * lat;
        float latLen = MathF.Abs(lat);
        float alphaPull = 1f - MathF.Exp(-PathTrackLateralPull * dt);
        float stepMax = PathTrackLateralMaxSpeed * dt;
        float pull = Math.Min(latLen * alphaPull, stepMax);
        float sPull = pull / latLen;
        body.PosX += latX * sPull;
        body.PosZ += latZ * sPull;
    }

    /// <summary>
    /// Blend planar linear velocity toward body forward (same speed). Reduces crabbing after
    /// path-heading yaw so thr push follows the nose.
    /// </summary>
    internal static void AlignPlanarVelocityToHeading(HkRigidBody body, float dt)
    {
        if (body == null || !(dt > 0f) || !float.IsFinite(dt))
            return;

        float vx = body.LinVelX;
        float vz = body.LinVelZ;
        float spd = MathF.Sqrt(vx * vx + vz * vz);
        if (spd < 0.15f)
            return;

        ExtractBasis(
            new Quaternion(body.QuatX, body.QuatY, body.QuatZ, body.QuatW),
            out _, out var forward);
        float fx = forward.X;
        float fz = forward.Z;
        float fLen = MathF.Sqrt(fx * fx + fz * fz);
        if (fLen < 1e-4f)
            return;
        float invF = 1f / fLen;
        fx *= invF;
        fz *= invF;

        // Desired planar vel = spd * forward (XZ).
        float dvx = fx * spd - vx;
        float dvz = fz * spd - vz;
        float alpha = 1f - MathF.Exp(-PathVelocityAlignRate * dt);
        body.LinVelX += dvx * alpha;
        body.LinVelZ += dvz * alpha;
    }

    /// <summary>Left-multiply body orientation by a world-Y yaw delta (radians).</summary>
    internal static void ApplyWorldYawDelta(HkRigidBody body, float yawRadians)
    {
        float half = yawRadians * 0.5f;
        float s = MathF.Sin(half);
        float c = MathF.Cos(half);
        // yaw quat (x,y,z,w) = (0, s, 0, c) * body
        float bx = body.QuatX, by = body.QuatY, bz = body.QuatZ, bw = body.QuatW;
        float x = c * bx + s * bz;
        float y = c * by + s * bw;
        float z = c * bz - s * bx;
        float w = c * bw - s * by;
        float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
        if (len > 1e-8f)
        {
            float inv = 1f / len;
            body.QuatX = x * inv;
            body.QuatY = y * inv;
            body.QuatZ = z * inv;
            body.QuatW = w * inv;
        }
    }

    /// <summary>
    /// True when the free-running body is non-finite, flipped, fell out of world, or drifted
    /// past the path-divergence threshold from the hard navigator.
    /// </summary>
    internal static bool NeedsRecovery(HkRigidBody body, PathStepResult hard, float supportY)
    {
        if (body == null)
            return true;

        if (!IsBodyFinite(body))
            return true;

        if (body.PosY < supportY - OutOfWorldSupportMargin)
            return true;

        // Flipped / on roof — sim alone rarely self-rights under path thrash; re-seat on path.
        if (BodyUpDotWorldUp(body) < MinUprightDot)
            return true;

        float thr = ResyncDriftThreshold;
        if (thr > 0f)
        {
            float dx = body.PosX - hard.NewPosition.X;
            float dy = body.PosY - hard.NewPosition.Y;
            float dz = body.PosZ - hard.NewPosition.Z;
            if ((dx * dx) + (dy * dy) + (dz * dz) > thr * thr)
                return true;
        }

        return false;
    }

    /// <summary>Body +Y axis · world +Y (1 = upright, −1 = inverted).</summary>
    internal static float BodyUpDotWorldUp(HkRigidBody body)
    {
        // Rotate local (0,1,0) by body quat → world up.
        float x = body.QuatX, y = body.QuatY, z = body.QuatZ, w = body.QuatW;
        // up = q * (0,1,0) * q^-1 ; Y component of rotated unit Y:
        // 1 - 2(x² + z²) for the world-Y of body-up, or expanded:
        float upY = 1f - 2f * (x * x + z * z);
        return upY;
    }

    internal static bool IsBodyFinite(HkRigidBody body)
    {
        return float.IsFinite(body.PosX) && float.IsFinite(body.PosY) && float.IsFinite(body.PosZ)
            && float.IsFinite(body.QuatX) && float.IsFinite(body.QuatY)
            && float.IsFinite(body.QuatZ) && float.IsFinite(body.QuatW)
            && float.IsFinite(body.LinVelX) && float.IsFinite(body.LinVelY) && float.IsFinite(body.LinVelZ)
            && float.IsFinite(body.AngVelX) && float.IsFinite(body.AngVelY) && float.IsFinite(body.AngVelZ);
    }

    internal static void SetPoseFromHard(VehiclePhysicsInstance inst, PathStepResult hard)
    {
        // Position from hard navigator; yaw from current body (or hard) but always upright.
        // Never preserve a flipped orientation — that left NPCs roof-driving after recovery.
        var body = inst.Body;
        ForceUprightPreserveYaw(body);
        float yaw = ExtractYawRadians(body);
        // Prefer hard yaw when body was garbage/inverted and hard has a valid facing.
        var hr = hard.Rotation;
        if (float.IsFinite(hr.W) && (hr.X * hr.X + hr.Y * hr.Y + hr.Z * hr.Z + hr.W * hr.W) > 1e-6f)
        {
            ExtractBasis(hr, out _, out var hardFwd);
            if (MathF.Abs(hardFwd.X) + MathF.Abs(hardFwd.Z) > 1e-3f)
                yaw = MathF.Atan2(hardFwd.X, hardFwd.Z);
        }

        var upright = TerrainContactPlane.YawOnly(yaw);
        inst.SetPose(
            hard.NewPosition.X, hard.NewPosition.Y, hard.NewPosition.Z,
            upright.X, upright.Y, upright.Z, upright.W);
    }

    /// <summary>
    /// After <see cref="VehiclePhysicsInstance.ReGround"/> (origin on terrain), lift the chassis
    /// so wheel hardpoints sit near rest length above the ground plane.
    /// Uses the <b>average</b> rest clearance (not max) so one long spring does not float
    /// the whole car and leave opposite wheels airborne.
    /// </summary>
    internal static void RaiseChassisToWheelRest(VehiclePhysicsInstance inst)
    {
        if (inst?.Data?.Wheels == null || inst.Data.Wheels.Count == 0)
            return;

        float sum = 0f;
        int n = 0;
        foreach (var w in inst.Data.Wheels)
        {
            // bodyY = ground − hardpointY + radius + rest  (ground already applied by ReGround)
            float r = -w.HardpointY + w.Radius + w.SuspensionRestLength;
            if (r > 0f && float.IsFinite(r))
            {
                sum += r;
                n++;
            }
        }

        if (n > 0)
        {
            float raise = sum / n;
            if (raise > 0f && float.IsFinite(raise))
                inst.Body.PosY += raise;
        }
    }

    /// <summary>
    /// Terrain heightfield query, optionally wrapped in <see cref="CompositeVehicleCollisionQuery"/>
    /// when <see cref="ServerConfig.CompositeWheelCollisionEnabled"/> is on (CW; default off).
    /// </summary>
    /// <param name="excludeSelf">Casting vehicle — skipped by the object pass so wheels miss own skirt.</param>
    internal static IVehicleCollisionQuery BuildCollisionQuery(
        SectorMap map,
        float fallbackGroundY,
        ClonedObjectBase excludeSelf = null)
    {
        var field = map?.MapData?.Heightfield;
        IVehicleCollisionQuery terrain;
        if (field != null)
        {
            terrain = new TerrainHeightfieldCollisionQuery(field);
        }
        else
        {
            // No map heightfield: flat plane at support / hard Y.
            float groundY = fallbackGroundY;
            terrain = new TerrainHeightfieldCollisionQuery(
                (float x, float z, out float y) =>
                {
                    y = groundY;
                    return true;
                });
        }

        if (ServerConfig.CompositeWheelCollisionEnabled && map != null)
            return new CompositeVehicleCollisionQuery(terrain, map, excludeSelf);

        return terrain;
    }

    /// <summary>
    /// Terrain sample under (x,z) when available; else hard-path Y as support reference.
    /// </summary>
    internal static float ResolveSupportY(SectorMap map, float x, float z, float hardY)
    {
        var field = map?.MapData?.Heightfield;
        if (field != null && field.TrySample(x, z, out var hy))
            return hy;
        return hardY;
    }

    internal static float ResolveTerrainSupportY(
        MapTerrainHeightfield field, float x, float z, float fallbackY)
    {
        if (field != null && field.TrySample(x, z, out var hy))
            return hy;
        return fallbackY;
    }

    private static float ResolveAcceptDistance(PathStepResult hard, MapPathTemplate path)
    {
        var n = path.Points.Count;
        if (n == 0)
            return 3f;
        var idx = Math.Clamp(hard.NewIndex, 0, n - 1);
        var ad = path.Points[idx].AcceptDistance;
        return ad > 0f ? ad : 3f;
    }

    private static float ResolveCruiseScale(PathStepResult hard, MapPathTemplate path)
    {
        var n = path.Points.Count;
        if (n < 3)
            return 1f;
        var idx = Math.Clamp(hard.NewIndex, 0, n - 1);
        var prev = path.Points[(idx - 1 + n) % n].Position;
        var cur = path.Points[idx].Position;
        var next = path.Points[(idx + 1) % n].Position;
        return PathCurvature.SpeedScale(PathCurvature.Radius(prev, cur, next));
    }

    internal static void ExtractBasis(Quaternion q, out Vector3 right, out Vector3 forward)
    {
        right = Rotate(q, 1f, 0f, 0f);
        forward = Rotate(q, 0f, 0f, 1f);
    }

    private static Vector3 Rotate(Quaternion q, float vx, float vy, float vz)
    {
        float qx = q.X, qy = q.Y, qz = q.Z, qw = q.W;
        float tx = 2f * (qy * vz - qz * vy);
        float ty = 2f * (qz * vx - qx * vz);
        float tz = 2f * (qx * vy - qy * vx);
        return new Vector3(
            vx + qw * tx + (qy * tz - qz * ty),
            vy + qw * ty + (qz * tx - qx * tz),
            vz + qw * tz + (qx * ty - qy * tx));
    }
}

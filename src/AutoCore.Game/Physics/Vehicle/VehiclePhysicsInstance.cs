namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Per-vehicle mutable physics state: chassis rigid body, per-wheel runtime, and
/// VehicleAction drive axes (throttle / steer ramp / handbrake).
/// <para>
/// <see cref="Step"/> splits <c>frameDt</c> with <see cref="HkVehicleSubstep"/> and runs
/// <see cref="VehicleActionSim.ApplyAction"/> once per sub-step. Subsystems that are not
/// yet ported are skipped by the orchestrator.
/// </para>
/// Primary client anchors: <c>CVOGSectorMap::StepTo</c> 0x4d6c80 (substep),
/// <c>VehicleAction::applyAction</c> 0x598650 (per-substep driver).
/// </summary>
public sealed class VehiclePhysicsInstance
{
    public VehiclePhysicsInstance(HkVehicleData data)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Body = CreateBody(data);
        Wheels = new HkWheelRuntimeState[data.WheelCount];
        for (var i = 0; i < Wheels.Length; i++)
            Wheels[i] = new HkWheelRuntimeState();
    }

    /// <summary>Immutable per-clonebase setup (shared / cached).</summary>
    public HkVehicleData Data { get; }

    /// <summary>Chassis rigid body (pose, velocity, force accumulators).</summary>
    public HkRigidBody Body { get; }

    /// <summary>Per-wheel runtime state (contact, steer angle, drive torque, spin).</summary>
    public HkWheelRuntimeState[] Wheels { get; }

    /// <summary>
    /// Live throttle axis (entity+0x614 / driverInput+0x20), clamped [-1,1].
    /// Not VehicleAction+0x20 — that field is the fixed stage-1 rate base
    /// (<see cref="HkPhysicsConstants.SteerStage1RateBase"/>).
    /// </summary>
    public float Throttle;

    /// <summary>Entity +0x618 — raw steer axis target accepted this substep (clamped [-1,1]).</summary>
    public float SteerInput;

    /// <summary>VehicleAction +0x24 — stage-1 steer ramp toward <see cref="SteerInput"/>.</summary>
    public float SteerRamp;

    /// <summary>VehicleAction +0x28 — stage-2 final steer (speed-scaled, ramped ±0.05/tick).</summary>
    public float SteerFinal;

    /// <summary>Entity +0x61c handbrake / sharp-turn flag.</summary>
    public bool Handbrake;

    /// <summary>VehicleAction +0x2c — true when no wheel was in contact this substep.</summary>
    public bool AllWheelsAirborne = true;

    /// <summary>
    /// Advance the vehicle by one server frame. Uses client sub-step split when
    /// <see cref="HkVehicleSubstep"/> is available.
    /// </summary>
    /// <param name="throttle">Desired longitudinal axis (clamped [-1,1]).</param>
    /// <param name="steer">Desired steer axis (clamped [-1,1]).</param>
    /// <param name="handbrake">Handbrake / sharp-turn active.</param>
    /// <param name="frameDt">Frame delta seconds (clamped/split internally).</param>
    /// <param name="query">Wheel/chassis cast query (null → treated as always-miss).</param>
    public void Step(
        float throttle,
        float steer,
        bool handbrake,
        float frameDt,
        IVehicleCollisionQuery query)
    {
        query ??= NullVehicleCollisionQuery.Instance;

        var (n, substepDt) = HkVehicleSubstep.Compute(frameDt);
        for (var i = 0; i < n; i++)
            VehicleActionSim.ApplyAction(this, throttle, steer, handbrake, substepDt, query);
    }

    /// <summary>Teleport chassis pose; clears velocities and force accumulators.</summary>
    public void SetPose(float x, float y, float z, float qx, float qy, float qz, float qw)
    {
        Body.PosX = x;
        Body.PosY = y;
        Body.PosZ = z;
        Body.QuatX = qx;
        Body.QuatY = qy;
        Body.QuatZ = qz;
        Body.QuatW = qw;
        Body.LinVelX = Body.LinVelY = Body.LinVelZ = 0f;
        Body.AngVelX = Body.AngVelY = Body.AngVelZ = 0f;
        Body.ForceX = Body.ForceY = Body.ForceZ = 0f;
        Body.TorqueX = Body.TorqueY = Body.TorqueZ = 0f;
    }

    /// <summary>
    /// Recovery / spawn primitive: cast straight down from <c>PosY + 10</c>, snap the chassis
    /// origin onto the ground hit, and clear all motion, drive axes, and wheel state. Mirrors the
    /// retail post-collision recovery in <c>VehicleAction_airStabilization 0x598320</c> §3.2/§3.3
    /// (zero lin+ang velocity → SetDriveAxes(0) → terrain-snap). On a miss the position is left
    /// unchanged (no teleport into unknown), but motion/axes/wheels are still reset.
    /// </summary>
    public void ReGround(IVehicleCollisionQuery query)
    {
        query ??= NullVehicleCollisionQuery.Instance;

        var startY = HkVehicleAirStabilization.ComputeReGroundCastStartY(Body.PosY);
        if (query.CastRay(
                Body.PosX, startY, Body.PosZ,
                0f, -1f, 0f,
                HkPhysicsConstants.ReGroundCastMaxDistance,
                out var hit))
        {
            Body.PosY = HkVehicleAirStabilization.ResolveReGroundPositionY(hit.PointY);
        }

        Body.LinVelX = Body.LinVelY = Body.LinVelZ = 0f;
        Body.AngVelX = Body.AngVelY = Body.AngVelZ = 0f;
        Body.ForceX = Body.ForceY = Body.ForceZ = 0f;
        Body.TorqueX = Body.TorqueY = Body.TorqueZ = 0f;

        // SetDriveAxes(0) — retail clears throttle/steer/handbrake on recovery.
        Throttle = 0f;
        SteerInput = 0f;
        SteerRamp = 0f;
        SteerFinal = 0f;
        Handbrake = false;

        foreach (var w in Wheels)
        {
            w.InContact = false;
            w.Spin = 0f;
            w.LongContactVel = 0f;
            w.LongImpulse = 0f;
            w.LatImpulse = 0f;
            w.ClosingSpeed = 0f;
        }
    }

    private static HkRigidBody CreateBody(HkVehicleData data)
    {
        var body = new HkRigidBody();
        body.SetMass(data.Mass);
        // Diagonal inv-inertia from unit-inertia components (mass=1 → I = RVInertia*).
        // Chassis basis is front=+Z, up=+Y, lateral=±X (VehicleActionSim + live B4 read),
        // so the principal inertia pairs by rotation axis:
        //   Roll  (about forward Z) → InvInertiaZ
        //   Pitch (about lateral X) → InvInertiaX
        //   Yaw   (about up Y)      → InvInertiaY
        // Live-verified (0.2-mass-inertia.md §2.1): rb+0xe0 = (1/4500,1/4500,1/1500) on (X,Y,Z)
        // for a car with DB Roll=1,Pitch=3,Yaw=3 → forward/Z carries the low roll inertia.
        body.InvInertiaX = InvOrZero(data.InertiaPitch);
        body.InvInertiaY = InvOrZero(data.InertiaYaw);
        body.InvInertiaZ = InvOrZero(data.InertiaRoll);
        body.QuatW = 1f;
        return body;
    }

    private static float InvOrZero(float inertia)
        => inertia > 0f ? 1f / inertia : 0f;
}

namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Brake subsystem for the vehicle physics port — handbrake rear-traction cut
/// (calcWheelTorque) plus the ticked Havok <c>hkDefaultBrake_update</c> @ <c>0x64e6f0</c>
/// service-brake path (Task B8 / C8).
/// <para>
/// Retail tick order (<c>VehicleAction_tickSubsystems 0x636a60</c>): preUpdate → driverInput
/// → steering → engine-slot → transmission → <b>brake</b> → suspension → aero → postTick.
/// Pedal is the reverse component of the throttle axis (Accel=−1 / Reverse=+1) from
/// <c>hkDefaultAnalogDriverInput_calcStatus 0x5fe520</c>. Outputs:
/// per-wheel brake torque (folded into friction-solver input) and isBlocked lock flags
/// (force wheel spin to 0 in the next preUpdate).
/// </para>
/// </summary>
public static class HkVehicleBrake
{
    /// <summary>
    /// Applies the calcWheelTorque handbrake rear-traction cut.
    /// When handbrake is active and the wheel is rear, multiplies <paramref name="torque"/>
    /// by <see cref="HkPhysicsConstants.HandbrakeRearTorqueScale"/> (0.5); otherwise returns
    /// torque unchanged. Front wheels are never scaled.
    /// </summary>
    /// <param name="torque">Per-wheel drive torque before the handbrake cut.</param>
    /// <param name="isRear">True if the wheel is on the rear axle (index &gt; vehicleData+0x4cc).</param>
    /// <param name="handbrakeActive">Entity handbrake / sharp-turn byte (<c>+0x61c</c>) nonzero.</param>
    public static float ApplyHandbrakeDriveTorqueScale(float torque, bool isRear, bool handbrakeActive)
    {
        if (isRear && handbrakeActive)
            return torque * HkPhysicsConstants.HandbrakeRearTorqueScale;
        return torque;
    }

    /// <summary>
    /// Service-brake peak magnitude:
    /// <c>peak = m_maxBreakingTorque * pedalInput</c> (pedal ∈ [0,1]).
    /// Retail <c>hkDefaultBrake_update</c> then clamps the opposing-spin torque into ±peak.
    /// </summary>
    /// <param name="maxBreakingTorque">Wheel <c>m_maxBreakingTorque</c> (front/rear from DB).</param>
    /// <param name="pedalInput">Brake pedal in [0,1]; values outside are not clamped here.</param>
    public static float ComputeServiceBrakeTorque(float maxBreakingTorque, float pedalInput)
        => maxBreakingTorque * pedalInput;

    /// <summary>
    /// Derive Havok brake pedal from the raw throttle axis (entity <c>+0x614</c> /
    /// driverInput <c>+0x20</c>). Retail convention: Accel = −1, Reverse = +1.
    /// <c>hkDefaultAnalogDriverInput_calcStatus</c> writes
    /// <c>brakePedal = (v &gt;= 0) ? v : 0</c> (gear-flip path residual — not applied here).
    /// </summary>
    public static float DeriveBrakePedal(float throttleAxis)
        => throttleAxis > 0f ? throttleAxis : 0f;

    /// <summary>
    /// Per-wheel isBlocked lock flag from <c>hkDefaultBrake_update</c>.
    /// True when (handbrake-connected AND handbrake asserted) OR
    /// (pedal ≥ minPedalInputToBlock). AA builder forces <c>minTimeToBlock = 0</c>,
    /// so lock is immediate once the pedal threshold is met — no dwell timer in the port.
    /// </summary>
    public static bool ComputeIsBlocked(
        bool handbrakeConnected,
        bool handbrakeActive,
        float pedalInput,
        float minPedalInputToBlock)
    {
        if (handbrakeConnected && handbrakeActive)
            return true;
        return minPedalInputToBlock <= pedalInput;
    }

    /// <summary>
    /// Opposing-spin service-brake torque from retail <c>hkDefaultBrake_update 0x64e6f0</c>:
    /// <c>raw = −spin · radius² · wheelsMass · invDt</c>, then
    /// <c>out = clamp(raw, −peak, +peak)</c> with
    /// <c>peak = pedal · maxBreakingTorque</c>.
    /// <para>
    /// <paramref name="wheelsMass"/> is the fixed wheels-builder payload at
    /// <c>wheel+0x84</c> (<see cref="HkPhysicsConstants.WheelsMassScale"/> = 15.0).
    /// <paramref name="invDt"/> is the step inverse-dt (classic Havok kill-spin stiffness;
    /// AA packs step info as the second float of the substep context — see brake-spec.md).
    /// </para>
    /// </summary>
    public static float ComputeOpposingSpinBrakeTorque(
        float spin,
        float radius,
        float wheelsMass,
        float invDt,
        float peak)
    {
        if (peak <= 0f || radius == 0f)
            return 0f;

        // raw = -spin * r² * mass * invDt  (float32 product order matching decompile grouping)
        float raw = (0f - spin * radius * wheelsMass * invDt) * radius;
        float absRaw = MathF.Abs(raw);
        if (peak < absRaw)
            return raw > 0f ? peak : 0f - peak;
        return raw;
    }

    /// <summary>
    /// Full per-wheel <c>hkDefaultBrake_update</c> outputs for one substep.
    /// </summary>
    /// <param name="pedalInput">Service brake pedal [0,1] from <see cref="DeriveBrakePedal"/>.</param>
    /// <param name="handbrakeActive">Raw entity handbrake / sharp-turn bit.</param>
    /// <param name="maxBreakingTorque">Wheel max brake torque (setup).</param>
    /// <param name="minPedalInputToBlock">Wheel min pedal to lock (setup).</param>
    /// <param name="handbrakeConnected">Wheel connected to handbrake (setup).</param>
    /// <param name="spin">Current wheel spin ω (rad/s), wheel+0x8c.</param>
    /// <param name="radius">Wheel radius (wheels+0x10[i]).</param>
    /// <param name="wheelsMass">wheel+0x84 scale (default 15.0).</param>
    /// <param name="invDt">1/dt for the kill-spin stiffness term.</param>
    /// <param name="brakeTorque">Output: signed opposing-spin torque (brake+0x10[i]).</param>
    /// <param name="isBlocked">Output: lock flag (brake+0x1c[i]).</param>
    public static void UpdateWheel(
        float pedalInput,
        bool handbrakeActive,
        float maxBreakingTorque,
        float minPedalInputToBlock,
        bool handbrakeConnected,
        float spin,
        float radius,
        float wheelsMass,
        float invDt,
        out float brakeTorque,
        out bool isBlocked)
    {
        float peak = ComputeServiceBrakeTorque(maxBreakingTorque, pedalInput);
        brakeTorque = ComputeOpposingSpinBrakeTorque(spin, radius, wheelsMass, invDt, peak);
        isBlocked = ComputeIsBlocked(
            handbrakeConnected, handbrakeActive, pedalInput, minPedalInputToBlock);
    }

    /// <summary>
    /// postTick force-equivalent of brake torque for the friction-solver input row:
    /// <c>local_3ec = brakeTorque / radius</c> (transmission residual is 0 in AA).
    /// Non-positive radius yields 0.
    /// </summary>
    public static float BrakeTorqueToFrictionForce(float brakeTorque, float radius)
    {
        if (radius <= 0f)
            return 0f;
        return brakeTorque / radius;
    }
}

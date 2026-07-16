namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Steering helpers for the vehicle physics port.
/// <para>
/// Physical wheel angles: <c>hkDefaultSteering_update</c> @ <c>0x64f840</c> —
/// <c>angle = maxAngle * steerInput</c>, then quadratic inverse-speed falloff when
/// <c>fullSpeedLimit &lt;= forwardSpeed</c>: <c>angle *= (fullSpeedLimit/forwardSpeed)²</c>.
/// Per-wheel: <c>doesSteer[i] ? angle : 0</c>.
/// </para>
/// <para>
/// Input ramps / mode-0x02 speed factor live in <c>VehicleAction::applyAction</c> @ <c>0x598650</c>:
/// stage-1 rate base <see cref="HkPhysicsConstants.SteerStage1RateBase"/> (<c>DAT_009d54e0</c> ≈ 2.857)
/// × open-band factor <see cref="HkPhysicsConstants.SteerStage1OpenBandFactor"/> (<c>DAT_00a10e74</c> = 2),
/// stage-2 step <see cref="HkPhysicsConstants.SteerRampPerTick"/> (<c>DAT_00a10e78</c> = 0.05),
/// speed divisor <see cref="HkPhysicsConstants.SteerSpeedFactorDivisor"/> (<c>0xaf3388</c> = 20).
/// </para>
/// </summary>
public static class HkVehicleSteering
{
    /// <summary>
    /// Stage-1 steer ramp (VA+0x24 toward entity+0x618): 
    /// <c>step = rateBase * dt * factor</c>, then move by min(|delta|, step) and clamp ±1.
    /// Open-band <paramref name="factor"/> is 2 when current is strictly off-zero and target
    /// is still inside the open clamp interval; else 1. Non-positive <paramref name="dt"/>
    /// leaves <paramref name="current"/> unchanged.
    /// </summary>
    public static float RampStage1(
        float current,
        float target,
        float dt,
        float rateBase = HkPhysicsConstants.SteerStage1RateBase)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
            return current;

        float delta = target - current;
        if (delta == 0f)
            return current;

        // factor = 2 when (current < 0 && target > -1) || (current > 0 && target < +1); else 1
        float factor = HkPhysicsConstants.One;
        if ((current < 0f && target > HkPhysicsConstants.SteerInputMin) ||
            (current > 0f && target < HkPhysicsConstants.SteerInputMax))
            factor = HkPhysicsConstants.SteerStage1OpenBandFactor;

        float step = MathF.Abs(rateBase) * dt * factor;
        float absDelta = MathF.Abs(delta);
        if (absDelta < step)
            step = absDelta;

        float next = delta > 0f ? current + step : current - step;
        if (next > HkPhysicsConstants.SteerInputMax)
            return HkPhysicsConstants.SteerInputMax;
        if (next < HkPhysicsConstants.SteerInputMin)
            return HkPhysicsConstants.SteerInputMin;
        return next;
    }

    /// <summary>
    /// Ramps <paramref name="current"/> toward <paramref name="target"/> by at most
    /// <paramref name="step"/> (default <see cref="HkPhysicsConstants.SteerRampPerTick"/> = 0.05),
    /// then clamps to <see cref="HkPhysicsConstants.SteerInputMin"/> /
    /// <see cref="HkPhysicsConstants.SteerInputMax"/>.
    /// Matches applyAction mode-0x02 VA+0x28 ramp toward targetSteer (per tick, not ×dt).
    /// </summary>
    public static float RampSteer(
        float current,
        float target,
        float step = HkPhysicsConstants.SteerRampPerTick)
    {
        float next;
        var delta = target - current;
        if (delta > step)
            next = current + step;
        else if (delta < -step)
            next = current - step;
        else
            next = target;

        if (next > HkPhysicsConstants.SteerInputMax)
            return HkPhysicsConstants.SteerInputMax;
        if (next < HkPhysicsConstants.SteerInputMin)
            return HkPhysicsConstants.SteerInputMin;
        return next;
    }

    /// <summary>
    /// Mode-0x02 speed factor: <c>min(|speed| / 20, 1)</c> where 20 is
    /// <see cref="HkPhysicsConstants.SteerSpeedFactorDivisor"/> (<c>0xaf3388</c>).
    /// Applied as <c>targetSteer = desiredSteer * speedFactor</c> before the ramp.
    /// </summary>
    public static float ModeSpeedFactor(float absOrSignedSpeed)
    {
        var speed = absOrSignedSpeed < 0f ? -absOrSignedSpeed : absOrSignedSpeed;
        var factor = speed / HkPhysicsConstants.SteerSpeedFactorDivisor;
        return factor > HkPhysicsConstants.One ? HkPhysicsConstants.One : factor;
    }

    /// <summary>
    /// Computes per-wheel steer angles from normalized input (hkDefaultSteering_update @ 0x64f840).
    /// <list type="bullet">
    /// <item><c>angle = maxAngle * steerInput</c></item>
    /// <item>If <c>fullSpeedLimit &lt;= forwardSpeed</c> and <c>forwardSpeed &gt; 0</c>:
    /// <c>angle *= (fullSpeedLimit / forwardSpeed)²</c></item>
    /// <item><c>out[i] = doesSteer[i] ? angle : 0</c></item>
    /// </list>
    /// </summary>
    /// <param name="steerInput">Normalized steer ∈ [-1, +1] (post-ramp).</param>
    /// <param name="maxAngle">SteeringMaxAngle (radians).</param>
    /// <param name="fullSpeedLimit">SteeringFullSpeedLimit (m/s).</param>
    /// <param name="forwardSpeed">Chassis forward-axis linear speed (m/s).</param>
    /// <param name="doesSteer">Per-wheel steer flags from descriptor.</param>
    public static float[] ComputeWheelAngles(
        float steerInput,
        float maxAngle,
        float fullSpeedLimit,
        float forwardSpeed,
        bool[] doesSteer)
    {
        ArgumentNullException.ThrowIfNull(doesSteer);

        var angle = maxAngle * steerInput;

        // Binary: if (fullSpeedLimit <= forwardSpeed) { r = full/speed; angle *= r*r; }
        // Extra guard forwardSpeed > 0 avoids div-by-zero on degenerate inputs.
        if (fullSpeedLimit <= forwardSpeed && forwardSpeed > 0f)
        {
            var r = fullSpeedLimit / forwardSpeed;
            angle = r * r * angle;
        }

        var outAngles = new float[doesSteer.Length];
        for (var i = 0; i < doesSteer.Length; i++)
            outAngles[i] = doesSteer[i] ? angle : 0f;
        return outAngles;
    }
}

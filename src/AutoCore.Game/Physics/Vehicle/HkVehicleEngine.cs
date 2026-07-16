namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// AA custom engine torque assembly for the vehicle physics port.
/// <para>
/// Retail path: <c>VehicleAction::calcWheelTorque</c> @ <c>0x598040</c> (Ghidra decompile).
/// There is no <c>hkDefaultEngine</c> — this replaces it. Output feeds
/// <c>hkDefaultWheels+0x28[i]</c>, aggregated by <c>postTickApplyForces</c> (0x64bc70)
/// as the drive impulse into the friction solver.
/// </para>
/// <para>
/// Upright falloff (Ghidra re-verify of 0x598040 + double @ 0x9d54e8): when
/// <c>|dot(bodyUp, worldUp)| &lt; 0.8</c>, <c>upright = |dot|^4</c>; else 1.0.
/// Use <see cref="ComputeUprightFactor"/> or pass a precomputed factor.
/// </para>
/// </summary>
public static class HkVehicleEngine
{
    /// <summary>
    /// Retail upright scalar from chassis up · world up.
    /// <c>|dot| &lt; 0.8</c> → <c>|dot|^4</c>; else 1.0 (step, not continuous at 0.8).
    /// </summary>
    public static float ComputeUprightFactor(float bodyUpDotWorldUp)
    {
        var absDot = MathF.Abs(bodyUpDotWorldUp);
        if (absDot < HkPhysicsConstants.UprightDotThreshold)
            return MathF.Pow(absDot, HkPhysicsConstants.UprightPowExponent);
        return HkPhysicsConstants.One;
    }

    /// <summary>
    /// Constant torque-curve factor used when the 2D LUT bins are out of range
    /// (retail <c>0x4a9750</c> returns <c>factors[0]</c>). Populated as
    /// <see cref="VehicleSpecific.MinTorqueFactor"/> at setup.
    /// </summary>
    public static float DefaultConstantFactor(float minTorqueFactor) => minTorqueFactor;

    /// <summary>
    /// <see cref="DefaultConstantFactor(float)"/> from setup — equals <c>EngineFactors[0]</c>.
    /// </summary>
    public static float DefaultConstantFactor(HkVehicleData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.EngineFactorsArray[0];
    }

    /// <summary>
    /// <c>VehicleEngine::torqueCurve2D</c> @ <c>0x4a9750</c> with retail contact X/Z args
    /// (<c>calcWheelTorque</c> passes <c>wheel+0x20</c>/<c>+0x28</c>).
    /// Trivial (0-row) LUTs always OOR → <c>factors[0]</c> = MinTorqueFactor.
    /// </summary>
    public static float EvaluateTorqueCurveFactor(HkVehicleData data, float contactX, float contactZ)
    {
        ArgumentNullException.ThrowIfNull(data);
        return TorqueCurve2D.Evaluate(
            data.EngineEnabled,
            data.EngineRows,
            data.EngineCols,
            data.EngineRangeScale,
            data.EngineFactorsArray,
            data.EngineLutArray,
            contactX,
            contactZ);
    }

    /// <summary>
    /// Per-wheel drive torque from <c>calcWheelTorque</c> @ <c>0x598040</c>.
    /// </summary>
    /// <param name="torqueCurveFactor">
    /// Output of <c>VehicleEngine::torqueCurve2D</c> (factor, typically in [0, ~1.6]).
    /// </param>
    /// <param name="frictionMu">Per-wheel friction μ from wheels descriptor (<c>FUN_004f5550</c>).</param>
    /// <param name="uprightFactor">
    /// From <see cref="ComputeUprightFactor"/> (1.0 upright; |dot|^4 when tilted).
    /// </param>
    /// <param name="chassisSpeed">Chassis linear speed |v| (world units/s).</param>
    /// <param name="isRear">True if wheel index &gt; rear-axle boundary (<c>vehicleData+0x4cc</c>).</param>
    /// <param name="handbrake">Entity handbrake / sharp-turn byte (<c>+0x61c</c>) nonzero.</param>
    /// <param name="driverMod">Driver skill/mod float (entity..+0x118); default 0 leaves curve unchanged.</param>
    /// <returns>Clamped torque in [0, <see cref="HkPhysicsConstants.TorqueClampMax"/>].</returns>
    public static float ComputeWheelTorque(
        float torqueCurveFactor,
        float frictionMu,
        float uprightFactor,
        float chassisSpeed,
        bool isRear,
        bool handbrake,
        float driverMod = 0f)
    {
        var t = ApplyDriverMod(torqueCurveFactor, driverMod, isRear);
        var mu = ApplyLowSpeedTractionBoost(frictionMu, chassisSpeed);
        var torque = mu * uprightFactor * t;

        if (isRear && handbrake)
            torque *= HkPhysicsConstants.HandbrakeRearTorqueScale;

        return ClampTorque(torque);
    }

    /// <summary>
    /// Driver modifier on the torque-curve factor (calcWheelTorque mid-block).
    /// m &gt; 0: blend toward 1; m &lt; 0: scale by (1+m), with rear m×2 first.
    /// </summary>
    public static float ApplyDriverMod(float torqueCurveFactor, float driverMod, bool isRear)
    {
        if (driverMod > 0f)
            return HkPhysicsConstants.One
                   - (HkPhysicsConstants.One - driverMod) * (HkPhysicsConstants.One - torqueCurveFactor);

        if (driverMod < 0f)
        {
            var m = driverMod;
            if (isRear)
                m *= HkPhysicsConstants.RearDriverModScale;
            return (m + HkPhysicsConstants.One) * torqueCurveFactor;
        }

        return torqueCurveFactor;
    }

    /// <summary>
    /// Low-speed traction boost: if |v| &lt; 15, μ ×= (15−v)×0.2 + 1.
    /// </summary>
    public static float ApplyLowSpeedTractionBoost(float frictionMu, float chassisSpeed)
    {
        if (chassisSpeed < HkPhysicsConstants.LowSpeedTractionCutoff)
        {
            return frictionMu * ((HkPhysicsConstants.LowSpeedTractionCutoff - chassisSpeed)
                                 * HkPhysicsConstants.LowSpeedTractionSlope
                                 + HkPhysicsConstants.One);
        }

        return frictionMu;
    }

    /// <summary>Clamp to retail [0, 1000] (DAT_00a0f520).</summary>
    public static float ClampTorque(float torque)
    {
        if (torque < HkPhysicsConstants.Zero)
            return HkPhysicsConstants.Zero;
        if (torque > HkPhysicsConstants.TorqueClampMax)
            return HkPhysicsConstants.TorqueClampMax;
        return torque;
    }
}

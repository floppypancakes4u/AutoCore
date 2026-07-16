namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Frame-delta → sub-step count / dt for Havok vehicle sim.
/// Port of <c>CVOGSectorMap::StepTo</c> sub-step split at <c>autoassault.exe</c> <c>0x004d6c80</c>.
/// Constants: max frame dt <c>0x00a0f730</c> = 0.1; Hz cap <c>0x009cc798</c> = 29.9999998f.
/// </summary>
/// <remarks>
/// Client math:
/// <code>
/// frameDt    = min(frameDt, 0.1)
/// N          = floor(frameDt * 29.9999998) + 1
/// substep_dt = frameDt / N
/// </code>
/// Ensures no sub-step exceeds ~1/30 s; 60 fps and 30 fps both take a single step.
/// </remarks>
public static class HkVehicleSubstep
{
    /// <summary>
    /// Computes sub-step count and per-sub-step dt from a frame delta (seconds).
    /// Non-finite or negative <paramref name="frameDt"/> → (N=1, substepDt=0).
    /// </summary>
    public static (int N, float SubstepDt) Compute(float frameDt)
    {
        if (!float.IsFinite(frameDt) || frameDt < 0f)
            return (1, 0f);

        if (frameDt > HkPhysicsConstants.MaxFrameDt)
            frameDt = HkPhysicsConstants.MaxFrameDt;

        // Matches StepTo: floor(param_2 * _DAT_009cc798) + 1, then ROUND to int.
        int n = (int)MathF.Floor(frameDt * HkPhysicsConstants.SubstepHzCap) + 1;
        if (n < 1)
            n = 1;

        float substepDt = frameDt / n;
        return (n, substepDt);
    }
}

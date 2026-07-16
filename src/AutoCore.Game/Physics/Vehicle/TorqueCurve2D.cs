namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Bit-exact port of <c>VehicleEngine::torqueCurve2D</c> @ <c>0x4a9750</c>.
/// See docs/reconstruction/physics/engine-torque-spec.md §2.
/// LUT index is row-major with stride <b>cols</b> (not rows): <c>lut[cols * xbin + ybin] &amp; 7</c>.
/// Float→int casts truncate toward zero (C <c>(int)</c> semantics).
/// </summary>
public static class TorqueCurve2D
{
    /// <summary>Client engine factor table length at <c>+0x344</c> (8 discrete levels, LUT <c>&amp;7</c>).</summary>
    public const int FactorLevelCount = 8;

    /// <summary>
    /// Build the 8-level torque-factor table as linear interpolants between
    /// <paramref name="min"/> and <paramref name="max"/> (VehicleSpecific
    /// MinTorqueFactor / MaxTorqueFactor). Index 0 is the out-of-range default
    /// returned by <see cref="Evaluate"/>. Exact client writer to <c>+0x344</c>
    /// is not pinned — this is the recommended setup helper for later engine use
    /// (engine-torque-spec §5).
    /// </summary>
    public static float[] BuildFactorsFromMinMax(float min, float max)
    {
        var factors = new float[FactorLevelCount];
        var denom = FactorLevelCount - 1; // 7
        for (var i = 0; i < FactorLevelCount; i++)
            factors[i] = min + (max - min) * (i / (float)denom);
        return factors;
    }

    /// <summary>
    /// Evaluate the 2D byte-indexed torque factor LUT.
    /// </summary>
    /// <param name="enabled">Engine enabled flag (+0x0c). False → return 1.0.</param>
    /// <param name="rows">X (RPM) bin count (+0x10); range check only.</param>
    /// <param name="cols">Y (throttle) bin count (+0x14); also LUT row stride.</param>
    /// <param name="rangeScale">Bin width (+0x18). base = scale * 0.5, inv = 1 / scale.</param>
    /// <param name="factors">Eight discrete torque-factor levels (+0x344).</param>
    /// <param name="lut">Byte LUT of length rows*cols (+0x3dc); low 3 bits select factors.</param>
    /// <param name="rpm">X-axis sample (param_2).</param>
    /// <param name="throttle">Y-axis sample (param_3).</param>
    /// <returns>Torque factor. Disabled → 1.0; out-of-range → factors[0].</returns>
    public static float Evaluate(
        bool enabled,
        int rows,
        int cols,
        float rangeScale,
        float[] factors,
        byte[] lut,
        float rpm,
        float throttle)
    {
        // DAT_00a0f2a0 — engine-disabled early-out
        if (!enabled)
            return HkPhysicsConstants.One;

        float scale = rangeScale;
        float baseVal = scale * HkPhysicsConstants.Half; // DAT_00a0f298
        float inv = HkPhysicsConstants.One / scale;

        // Truncate toward zero (C (int) float→int), not floor.
        int xbin = (int)((rpm - baseVal) * inv);
        int ybin = (int)((throttle - baseVal) * inv);

        if (xbin >= 0 && xbin < rows &&
            ybin >= 0 && ybin < cols)
        {
            // Stride is cols (+0x14), NOT rows (+0x10).
            byte b = (byte)(lut[cols * xbin + ybin] & 7);
            // Unreachable assert path after &7 (b > 7 → return 1.0) is not ported.
            return factors[b];
        }

        // Out-of-range → factors[0] (distinct from disabled → 1.0)
        return factors[0];
    }
}

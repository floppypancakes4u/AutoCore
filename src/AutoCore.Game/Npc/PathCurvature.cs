namespace AutoCore.Game.Npc;

using AutoCore.Game.Structures;

/// <summary>
/// Path turn radius from three consecutive points (client <c>CVOGMapPath_AdvanceAndSteer</c> @ 0x005df950).
/// Used to scale cruise speed on tight corners (retail threshold ~30 world units).
/// </summary>
public static class PathCurvature
{
    /// <summary>Retail <c>DAT_00a0f694</c> — radii at or above this need no corner slowdown.</summary>
    public const float FullSpeedRadius = 30f;

    /// <summary>
    /// Circumradius of triangle (a,b,c) in XZ (Y ignored). Collinear / degenerate → large radius.
    /// </summary>
    public static float Radius(Vector3 a, Vector3 b, Vector3 c)
    {
        var abx = b.X - a.X;
        var abz = b.Z - a.Z;
        var bcx = c.X - b.X;
        var bcz = c.Z - b.Z;
        var cax = a.X - c.X;
        var caz = a.Z - c.Z;

        var ab = MathF.Sqrt((abx * abx) + (abz * abz));
        var bc = MathF.Sqrt((bcx * bcx) + (bcz * bcz));
        var ca = MathF.Sqrt((cax * cax) + (caz * caz));
        // Stryker disable once equality : float epsilon boundary is observationally equivalent
        // Stryker disable once logical : short-circuit vs all-three is equivalent when any side is zero (cross→0)
        if (ab < 1e-4f || bc < 1e-4f || ca < 1e-4f)
            return float.PositiveInfinity;

        // |cross| = 2A from XZ cross of (b-a) × (c-a)
        var cross = MathF.Abs((abx * (c.Z - a.Z)) - (abz * (c.X - a.X)));
        // Stryker disable once equality : float epsilon boundary
        if (cross < 1e-6f)
            return float.PositiveInfinity;

        // Circumradius R = abc / (4A) = abc / (2 * |cross|)
        return (ab * bc * ca) / (2f * cross);
    }

    /// <summary>
    /// Multiplier in (0,1] for cruise speed. R ≥ <see cref="FullSpeedRadius"/> → 1; tighter → slower.
    /// </summary>
    public static float SpeedScale(float radius)
    {
        // Stryker disable once equality : FullSpeedRadius threshold; = and > both full speed for continuous ramp
        if (!float.IsFinite(radius) || radius >= FullSpeedRadius)
            return 1f;
        // Stryker disable once equality : floor epsilon
        if (radius <= 1e-3f)
            return 0.25f;

        // Linear ramp: R=0 → 0.25, R=30 → 1.0
        var t = Math.Clamp(radius / FullSpeedRadius, 0f, 1f);
        return 0.25f + (0.75f * t);
    }
}

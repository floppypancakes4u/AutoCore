using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

[TestClass]
public class PathCurvatureTests
{
    [TestMethod]
    public void Radius_RightTriangle_MatchesCircumradius()
    {
        // 3-4-5 right triangle: R = hypotenuse/2 = 2.5
        // abc/(4A) = 60/(4*6) = 2.5
        var r = PathCurvature.Radius(
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, 0f, 0f),
            new Vector3(0f, 0f, 3f));
        Assert.AreEqual(2.5f, r, 0.01f);
    }

    [TestMethod]
    public void Radius_Collinear_IsInfinite()
    {
        var r = PathCurvature.Radius(
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 0f),
            new Vector3(20f, 0f, 0f));
        Assert.IsTrue(float.IsPositiveInfinity(r) || r > 1e6f, $"got {r}");
    }

    [TestMethod]
    public void Radius_Equilateral_KnownValue()
    {
        // Side 2: height √3, R = side / √3 = 2/√3 ≈ 1.1547
        // For equilateral R = a / (√3)
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(2f, 0f, 0f);
        var c = new Vector3(1f, 0f, MathF.Sqrt(3f));
        var r = PathCurvature.Radius(a, b, c);
        Assert.AreEqual(2f / MathF.Sqrt(3f), r, 0.02f);
    }

    [TestMethod]
    public void SpeedScale_FullWhenRadiusAtOrAbove30()
    {
        Assert.AreEqual(1f, PathCurvature.SpeedScale(30f), 0.001f);
        Assert.AreEqual(1f, PathCurvature.SpeedScale(100f), 0.001f);
        Assert.AreEqual(1f, PathCurvature.SpeedScale(float.PositiveInfinity), 0.001f);
    }

    [TestMethod]
    public void SpeedScale_ReducesOnTightCorner()
    {
        var tight = PathCurvature.SpeedScale(5f);
        var medium = PathCurvature.SpeedScale(15f);
        Assert.IsTrue(tight < medium && medium < 1f, $"tight={tight} med={medium}");
        Assert.IsTrue(tight >= 0.25f, $"floor {tight}");
    }

    [TestMethod]
    public void Radius_DegenerateZeroLengthSegment_IsInfinite()
    {
        var r = PathCurvature.Radius(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f));
        Assert.IsTrue(float.IsPositiveInfinity(r) || r > 1e6f);
    }

    [TestMethod]
    public void SpeedScale_NearZeroRadius_AtFloor()
    {
        Assert.AreEqual(0.25f, PathCurvature.SpeedScale(0f), 0.001f);
        Assert.AreEqual(0.25f, PathCurvature.SpeedScale(1e-4f), 0.001f);
        // just below floor threshold uses ramp, not floor
        Assert.AreEqual(0.25f + 0.75f * (0.01f / 30f), PathCurvature.SpeedScale(0.01f), 1e-5f);
    }

    [TestMethod]
    public void SpeedScale_JustBelowFullSpeedRadius_IsNotOne()
    {
        // radius > FullSpeed would incorrectly return 1 for 29.999 if mutated >= to >
        Assert.AreEqual(1f, PathCurvature.SpeedScale(30f), 1e-6f);
        Assert.IsTrue(PathCurvature.SpeedScale(29.999f) < 1f);
    }

    [TestMethod]
    public void Radius_ObliqueTriangle_BothCrossTermsUsed()
    {
        var a = new Vector3(2, 0, 5);
        var b = new Vector3(6, 0, 6);
        var c = new Vector3(3, 0, 8);
        var abx = 4f; var abz = 1f;
        var cross = MathF.Abs((abx * (c.Z - a.Z)) - (abz * (c.X - a.X)));
        var ab = MathF.Sqrt(abx * abx + abz * abz);
        var bc = MathF.Sqrt((c.X - b.X) * (c.X - b.X) + (c.Z - b.Z) * (c.Z - b.Z));
        var ca = MathF.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Z - c.Z) * (a.Z - c.Z));
        Assert.AreEqual((ab * bc * ca) / (2f * cross), PathCurvature.Radius(a, b, c), 1e-3f);
    }
}

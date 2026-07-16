using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Golden vectors from docs/reconstruction/physics/engine-torque-spec.md §2
/// (VehicleEngine::torqueCurve2D @ 0x4a9750). Hand-derived; emulation impractical.
/// </summary>
[TestClass]
public class TorqueCurve2DTests
{
    // Config: enabled=1, rows=4, cols=4, scale=100 → base=50, inv=0.01
    // factors = [0.10, 0.20, 0.40, 0.60, 1.00, 1.10, 1.20, 1.60]
    // LUT row-major: row0 0,1,2,3; row1 4,5,6,7; row2 0,1,2,3; row3 4,5,6,7
    private const int Rows = 4;
    private const int Cols = 4;
    private const float RangeScale = 100f;

    private static readonly float[] Factors =
    {
        0.10f, 0.20f, 0.40f, 0.60f, 1.00f, 1.10f, 1.20f, 1.60f
    };

    private static readonly byte[] Lut =
    {
        0, 1, 2, 3,
        4, 5, 6, 7,
        0, 1, 2, 3,
        4, 5, 6, 7,
    };

    [TestMethod]
    public void Golden1_MidHigh_ReturnsFactors7()
    {
        // rpm=240, thr=350 → xbin=1, ybin=3, idx=7, &7=7 → 1.60
        float r = TorqueCurve2D.Evaluate(true, Rows, Cols, RangeScale, Factors, Lut, 240f, 350f);
        Assert.AreEqual(1.60f, r, 1e-6f);
    }

    [TestMethod]
    public void Golden2_GridOrigin_ReturnsFactors0()
    {
        // rpm=50, thr=50 → xbin=0, ybin=0, idx=0 → 0.10
        float r = TorqueCurve2D.Evaluate(true, Rows, Cols, RangeScale, Factors, Lut, 50f, 50f);
        Assert.AreEqual(0.10f, r, 1e-6f);
    }

    [TestMethod]
    public void Golden3_MidGrid_ReturnsFactors2()
    {
        // rpm=250, thr=250 → xbin=2, ybin=2, idx=10, &7=2 → 0.40
        float r = TorqueCurve2D.Evaluate(true, Rows, Cols, RangeScale, Factors, Lut, 250f, 250f);
        Assert.AreEqual(0.40f, r, 1e-6f);
    }

    [TestMethod]
    public void Golden4_RpmBelowBase_TruncatesToXbin0_NotOor()
    {
        // rpm=30, thr=350 → xbin=(int)((30-50)*0.01)=(int)(-0.2)=0, ybin=3 → 0.60
        float r = TorqueCurve2D.Evaluate(true, Rows, Cols, RangeScale, Factors, Lut, 30f, 350f);
        Assert.AreEqual(0.60f, r, 1e-6f);
    }

    [TestMethod]
    public void Golden5_XbinOutOfRange_ReturnsFactors0()
    {
        // rpm=600, thr=350 → xbin=5 ≥ rows → OOR → factors[0]=0.10
        float r = TorqueCurve2D.Evaluate(true, Rows, Cols, RangeScale, Factors, Lut, 600f, 350f);
        Assert.AreEqual(0.10f, r, 1e-6f);
    }

    [TestMethod]
    public void Golden6_EngineDisabled_ReturnsOne()
    {
        // enabled==0 → 1.0 regardless of rpm/throttle
        float r = TorqueCurve2D.Evaluate(false, Rows, Cols, RangeScale, Factors, Lut, 240f, 350f);
        Assert.AreEqual(1.0f, r, 1e-6f);
    }

    [TestMethod]
    public void BuildFactorsFromMinMax_EightLinearLevels()
    {
        // engine-torque-spec §5: factors[0..7] interpolants between Min/MaxTorqueFactor.
        float[] f = TorqueCurve2D.BuildFactorsFromMinMax(min: 0.2f, max: 1.0f);
        Assert.AreEqual(8, f.Length);
        Assert.AreEqual(0.2f, f[0], 1e-5f);
        Assert.AreEqual(1.0f, f[7], 1e-5f);
        // Midpoint of 8 levels (index 3.5) — index 3 = min + 3/7 * range
        Assert.AreEqual(0.2f + 3f / 7f * 0.8f, f[3], 1e-5f);
        Assert.AreEqual(0.2f + 4f / 7f * 0.8f, f[4], 1e-5f);
    }

    [TestMethod]
    public void BuildFactorsFromMinMax_EqualMinMax_AllSame()
    {
        float[] f = TorqueCurve2D.BuildFactorsFromMinMax(min: 1f, max: 1f);
        Assert.AreEqual(8, f.Length);
        for (var i = 0; i < 8; i++)
            Assert.AreEqual(1f, f[i], 1e-5f);
    }

    [TestMethod]
    public void BuildFactorsFromMinMax_UsableByEvaluate_OutOfRangeUsesFactors0()
    {
        float[] f = TorqueCurve2D.BuildFactorsFromMinMax(min: 0.25f, max: 1.5f);
        // OOR → factors[0] = min
        float r = TorqueCurve2D.Evaluate(true, Rows, Cols, RangeScale, f, Lut, 600f, 350f);
        Assert.AreEqual(0.25f, r, 1e-5f);
    }
}

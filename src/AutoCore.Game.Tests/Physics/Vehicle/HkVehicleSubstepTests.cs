using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Port of CVOGSectorMap::StepTo sub-step split (autoassault.exe 0x004d6c80).
/// Caps: 0x009cc798 ≈ 29.9999998, 0x00a0f730 = 0.1.
/// </summary>
[TestClass]
public class HkVehicleSubstepTests
{
    private const float Epsilon = 1e-6f;

    [TestMethod]
    public void Compute_60Fps_YieldsSingleSubstep()
    {
        float frameDt = 1f / 60f;
        var (n, substepDt) = HkVehicleSubstep.Compute(frameDt);

        Assert.AreEqual(1, n);
        Assert.AreEqual(frameDt, substepDt, Epsilon);
    }

    [TestMethod]
    public void Compute_30Fps_YieldsSingleSubstep()
    {
        // Exact 1f/30f * SubstepHzCap can floor to 1 → N=2 under IEEE float.
        // Retail/docs use ~0.03333s (just under the cap product) for the single-step 30fps path.
        float frameDt = 0.03333f;
        var (n, substepDt) = HkVehicleSubstep.Compute(frameDt);

        Assert.AreEqual(1, n);
        Assert.AreEqual(frameDt, substepDt, Epsilon);
        Assert.AreEqual(1f / 30f, substepDt, 1e-4f);
    }

    [TestMethod]
    public void Compute_0_05_SplitsIntoTwoEqualSubsteps()
    {
        var (n, substepDt) = HkVehicleSubstep.Compute(0.05f);

        Assert.AreEqual(2, n);
        Assert.AreEqual(0.025f, substepDt, Epsilon);
    }

    [TestMethod]
    public void Compute_ClampsFrameDtTo0_1_ThenN4()
    {
        // frameDt 0.2 → clamp 0.1; floor(0.1 * 29.9999998) + 1 = floor(2.999…) + 1 = 3 + 1 = 4
        var (n, substepDt) = HkVehicleSubstep.Compute(0.2f);

        Assert.AreEqual(4, n);
        Assert.AreEqual(0.1f / 4f, substepDt, Epsilon);
    }

    [TestMethod]
    public void Compute_NegativeFrameDt_SafeDefaults()
    {
        var (n, substepDt) = HkVehicleSubstep.Compute(-0.016f);

        Assert.AreEqual(1, n);
        Assert.AreEqual(0f, substepDt);
    }

    [TestMethod]
    public void Compute_NonFiniteFrameDt_SafeDefaults()
    {
        var nan = HkVehicleSubstep.Compute(float.NaN);
        Assert.AreEqual(1, nan.N);
        Assert.AreEqual(0f, nan.SubstepDt);

        var posInf = HkVehicleSubstep.Compute(float.PositiveInfinity);
        Assert.AreEqual(1, posInf.N);
        Assert.AreEqual(0f, posInf.SubstepDt);

        var negInf = HkVehicleSubstep.Compute(float.NegativeInfinity);
        Assert.AreEqual(1, negInf.N);
        Assert.AreEqual(0f, negInf.SubstepDt);
    }

    [TestMethod]
    public void Compute_ZeroFrameDt_N1Dt0()
    {
        var (n, substepDt) = HkVehicleSubstep.Compute(0f);

        Assert.AreEqual(1, n);
        Assert.AreEqual(0f, substepDt);
    }
}

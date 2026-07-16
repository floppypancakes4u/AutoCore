using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// hkDefaultSuspension::update @ 0x64de50 — spring + damper scalar force.
/// </summary>
[TestClass]
public class HkVehicleSuspensionTests
{
    // Typical rest length / strength / damping from synthetic car setup.
    private const float Rest = 0.3f;
    private const float Strength = 40f;
    private const float DampComp = 3f;
    private const float DampExt = 2f;
    private const float Scale = 1f / Rest; // 1/maxSuspLen full-raycast path
    private const float InvMass = 1f;      // RB+0x2c unit-mass model → gScale = 1

    [TestMethod]
    public void ComputeForce_Airborne_ReturnsZero()
    {
        // Compressed + non-zero closing speed would produce force if grounded.
        var f = HkVehicleSuspension.ComputeForce(
            inContact: false,
            restLength: Rest,
            strength: Strength,
            dampCompression: DampComp,
            dampExtension: DampExt,
            currentLength: 0.1f,
            scalingFactor: Scale,
            closingSpeed: -1f,
            invMass: InvMass);

        Assert.AreEqual(0f, f);
    }

    [TestMethod]
    public void ComputeForce_ClosingSpeedNegative_UsesCompressionDamping()
    {
        // closingSpeed < 0 → dampComp; verify by differing dampComp vs dampExt.
        const float current = 0.2f;   // compression = 0.1
        const float closing = -0.5f;  // compressing
        const float invMass = 1f;     // gScale = 1

        float spring = (Rest - current) * Strength * Scale;
        float expectedWithComp = (spring - DampComp * closing) * (1f / invMass);
        float expectedWithExt = (spring - DampExt * closing) * (1f / invMass);
        Assert.AreNotEqual(expectedWithComp, expectedWithExt, "test fixture must distinguish damp paths");

        var f = HkVehicleSuspension.ComputeForce(
            inContact: true,
            restLength: Rest,
            strength: Strength,
            dampCompression: DampComp,
            dampExtension: DampExt,
            currentLength: current,
            scalingFactor: Scale,
            closingSpeed: closing,
            invMass: invMass);

        Assert.AreEqual(expectedWithComp, f, 1e-5f);
        Assert.AreNotEqual(expectedWithExt, f, 1e-5f);
    }

    [TestMethod]
    public void ComputeForce_SpringTerm_RestMinusCurrentTimesStrengthAndScale()
    {
        // Zero closing speed → damper out; isolate spring * gScale.
        const float current = 0.15f;
        const float closing = 0f;
        const float invMass = 1f;

        float expected = (Rest - current) * Strength * Scale * (1f / invMass);

        var f = HkVehicleSuspension.ComputeForce(
            inContact: true,
            restLength: Rest,
            strength: Strength,
            dampCompression: DampComp,
            dampExtension: DampExt,
            currentLength: current,
            scalingFactor: Scale,
            closingSpeed: closing,
            invMass: invMass);

        Assert.AreEqual(expected, f, 1e-5f);
        Assert.IsTrue(f > 0f, "compressed spring should push positive force");
    }

    [TestMethod]
    public void ComputeForce_ClosingSpeedNonNegative_UsesExtensionDamping()
    {
        const float current = 0.25f;
        const float closing = 0.4f; // extending / rebound
        const float invMass = 1f;

        float spring = (Rest - current) * Strength * Scale;
        float expected = (spring - DampExt * closing) * (1f / invMass);

        var f = HkVehicleSuspension.ComputeForce(
            inContact: true,
            restLength: Rest,
            strength: Strength,
            dampCompression: DampComp,
            dampExtension: DampExt,
            currentLength: current,
            scalingFactor: Scale,
            closingSpeed: closing,
            invMass: invMass);

        Assert.AreEqual(expected, f, 1e-5f);
    }

    [TestMethod]
    public void ComputeForce_BodyScalarZero_ReturnsZero()
    {
        // decompile: if RB+0x2c == 0 → gScale = 0
        var f = HkVehicleSuspension.ComputeForce(
            inContact: true,
            restLength: Rest,
            strength: Strength,
            dampCompression: DampComp,
            dampExtension: DampExt,
            currentLength: 0.1f,
            scalingFactor: Scale,
            closingSpeed: -0.2f,
            invMass: 0f);

        Assert.AreEqual(0f, f);
    }

    [TestMethod]
    public void ComputeForce_InvMassNormalizer_IsReciprocalOfBodyScalar()
    {
        // bodyScalar = 0.5 → gScale = 2; force doubles vs unit-mass.
        const float current = 0.2f;
        const float closing = 0f;
        const float bodyScalar = 0.5f;

        float spring = (Rest - current) * Strength * Scale;
        float expected = spring * (1f / bodyScalar);

        var f = HkVehicleSuspension.ComputeForce(
            inContact: true,
            restLength: Rest,
            strength: Strength,
            dampCompression: DampComp,
            dampExtension: DampExt,
            currentLength: current,
            scalingFactor: Scale,
            closingSpeed: closing,
            invMass: bodyScalar);

        Assert.AreEqual(expected, f, 1e-5f);
    }
}

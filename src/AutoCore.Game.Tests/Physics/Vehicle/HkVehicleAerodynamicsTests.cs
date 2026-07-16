using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// hkDefaultAerodynamics::update @ 0x64dae0 — drag + lift/downforce + extra-gravity force.
/// Constants: DAT_00a0f298 = +0.5, DAT_00aaa6cc = -0.5 (Ghidra read_memory).
/// </summary>
[TestClass]
public class HkVehicleAerodynamicsTests
{
    private const float Eps = 1e-5f;

    // Unit axes / neutral aero params for isolating terms.
    private const float Rho = 1.2f;
    private const float A = 2f;
    private const float Cd = 0.3f;
    private const float Cl = 0.1f;
    private const float Mass = 1000f;

    [TestMethod]
    public void ComputeForce_ZeroVelocity_ReturnsOnlyExtraGravityTimesMass()
    {
        // v = 0 → dragMag = 0, liftMag = 0; F = extraG * mass only.
        const float extraGx = 0f;
        const float extraGy = -1.5f;
        const float extraGz = 0.25f;

        var (fx, fy, fz) = HkVehicleAerodynamics.ComputeForce(
            rho: Rho,
            frontalArea: A,
            dragCoefficient: Cd,
            liftCoefficient: Cl,
            extraGx: extraGx,
            extraGy: extraGy,
            extraGz: extraGz,
            worldFrontX: 0f, worldFrontY: 0f, worldFrontZ: 1f,
            worldUpX: 0f, worldUpY: 1f, worldUpZ: 0f,
            linVelX: 0f, linVelY: 0f, linVelZ: 0f,
            mass: Mass);

        Assert.AreEqual(extraGx * Mass, fx, Eps);
        Assert.AreEqual(extraGy * Mass, fy, Eps);
        Assert.AreEqual(extraGz * Mass, fz, Eps);
    }

    [TestMethod]
    public void ComputeForce_ForwardVelocity_DragOpposesMotionAlongWorldFront()
    {
        // Moving +worldFront; dragMag = -0.5 * rho * A * Cd * |v| * v is negative → force along -front.
        const float v = 20f;
        const float cl = 0f; // isolate drag

        float dragMag = HkPhysicsConstants.NegHalf * Rho * A * Cd * MathF.Abs(v) * v;
        Assert.IsTrue(dragMag < 0f, "forward motion must produce negative drag magnitude");

        var (fx, fy, fz) = HkVehicleAerodynamics.ComputeForce(
            rho: Rho,
            frontalArea: A,
            dragCoefficient: Cd,
            liftCoefficient: cl,
            extraGx: 0f, extraGy: 0f, extraGz: 0f,
            worldFrontX: 0f, worldFrontY: 0f, worldFrontZ: 1f,
            worldUpX: 0f, worldUpY: 1f, worldUpZ: 0f,
            linVelX: 0f, linVelY: 0f, linVelZ: v,
            mass: Mass);

        Assert.AreEqual(0f, fx, Eps);
        Assert.AreEqual(0f, fy, Eps);
        Assert.AreEqual(dragMag, fz, Eps);
        Assert.IsTrue(fz < 0f, "drag must oppose forward (+Z) velocity");
    }

    [TestMethod]
    public void ComputeForce_NegativeLiftCoefficient_ProducesDownforceAlongWorldUp()
    {
        // Cl < 0 → liftMag < 0 → force along -worldUp (downforce). v^2 keeps magnitude positive.
        const float v = 15f;
        const float clDown = -0.4f;
        const float cd = 0f; // isolate lift

        float liftMag = HkPhysicsConstants.Half * Rho * A * clDown * v * v;
        Assert.IsTrue(liftMag < 0f, "negative Cl must yield negative lift magnitude");

        var (fx, fy, fz) = HkVehicleAerodynamics.ComputeForce(
            rho: Rho,
            frontalArea: A,
            dragCoefficient: cd,
            liftCoefficient: clDown,
            extraGx: 0f, extraGy: 0f, extraGz: 0f,
            worldFrontX: 0f, worldFrontY: 0f, worldFrontZ: 1f,
            worldUpX: 0f, worldUpY: 1f, worldUpZ: 0f,
            linVelX: 0f, linVelY: 0f, linVelZ: v,
            mass: Mass);

        Assert.AreEqual(0f, fx, Eps);
        Assert.AreEqual(liftMag, fy, Eps);
        Assert.AreEqual(0f, fz, Eps);
        Assert.IsTrue(fy < 0f, "negative Cl must produce downforce (force along -up)");
    }
}

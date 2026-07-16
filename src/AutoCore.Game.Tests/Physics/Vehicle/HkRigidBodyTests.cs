using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Integrator skeleton goldens for <see cref="HkRigidBody"/> (Havok-style semi-implicit Euler).
/// </summary>
[TestClass]
public class HkRigidBodyTests
{
    private const float GravityY = HkPhysicsConstants.DefaultGravityY; // -9.81

    [TestMethod]
    public void FreeFall_UnderGravity_VelocityAndPositionMatchSemiImplicitEuler()
    {
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            PosY = 100f,
            QuatW = 1f,
        };

        const float dt = 0.001f;
        const int steps = 1000; // T = 1 s
        const float T = dt * steps;

        for (int i = 0; i < steps; i++)
            body.Integrate(dt, applyGravity: true);

        // v = a * T from rest (semi-implicit and explicit agree for constant a on v)
        Assert.AreEqual(GravityY * T, body.LinVelY, 1e-3f);
        Assert.AreEqual(0f, body.LinVelX, 1e-5f);
        Assert.AreEqual(0f, body.LinVelZ, 1e-5f);

        // Semi-implicit position: x0 - |g| * T * (T + dt) / 2  (g negative → subtract g*...)
        // With a = GravityY: x_N = x0 + a * dt^2 * N*(N+1)/2 = x0 + a * T * (T + dt) / 2
        float expectedY = 100f + GravityY * T * (T + dt) / 2f;
        Assert.AreEqual(expectedY, body.PosY, 1e-2f);

        // Accumulators cleared each integrate
        Assert.AreEqual(0f, body.ForceX);
        Assert.AreEqual(0f, body.ForceY);
        Assert.AreEqual(0f, body.ForceZ);
    }

    [TestMethod]
    public void Integrate_CustomGravityY_UsesPassedValueNotDefault()
    {
        // Phase 4 gravity consistency: Integrate must honor caller gravityY
        // (HkVehicleData.GravityY from ServerConfig), not only DefaultGravityY.
        const float customG = -12.5f;
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            PosY = 50f,
            QuatW = 1f,
        };

        const float dt = 0.1f;
        body.Integrate(dt, applyGravity: true, gravityY: customG);

        Assert.AreEqual(customG * dt, body.LinVelY, 1e-5f);
        Assert.AreEqual(50f + customG * dt * dt, body.PosY, 1e-5f);
        // Must not match default-gravity free-fall (regression guard).
        Assert.AreNotEqual(HkPhysicsConstants.DefaultGravityY * dt, body.LinVelY);
    }

    [TestMethod]
    public void ApplyForce_ThenIntegrate_YieldsExpectedAcceleration()
    {
        var body = new HkRigidBody
        {
            Mass = 2f,
            InvMass = 0.5f,
            QuatW = 1f,
        };

        // F = 10 N on X → a = F/m = 5 m/s²
        body.ApplyForce(10f, 0f, 0f);

        const float dt = 0.1f;
        body.Integrate(dt, applyGravity: false);

        Assert.AreEqual(5f * dt, body.LinVelX, 1e-5f); // 0.5
        Assert.AreEqual(0f, body.LinVelY, 1e-5f);
        Assert.AreEqual(0f, body.LinVelZ, 1e-5f);

        // pos uses post-step velocity (semi-implicit): Δx = v_new * dt
        Assert.AreEqual(5f * dt * dt, body.PosX, 1e-5f); // 0.05
        Assert.AreEqual(0f, body.ForceX, 1e-5f);
    }

    [TestMethod]
    public void ApplyTorque_IntegratesAngularVelocityWithInvInertia()
    {
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            InvInertiaX = 2f,
            InvInertiaY = 1f,
            InvInertiaZ = 0.5f,
            QuatW = 1f,
        };

        body.ApplyTorque(1f, 0f, 0f);
        const float dt = 0.1f;
        body.Integrate(dt, applyGravity: false);

        // α_x = τ_x * invI_x = 2 → ω_x = 0.2
        Assert.AreEqual(2f * dt, body.AngVelX, 1e-5f);
        Assert.AreEqual(0f, body.AngVelY, 1e-5f);
        Assert.AreEqual(0f, body.TorqueX, 1e-5f);
    }

    [TestMethod]
    public void ApplyPointImpulse_AddsLinearAndAngularVelocity()
    {
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            InvInertiaX = 1f,
            InvInertiaY = 1f,
            InvInertiaZ = 1f,
            PosX = 0f,
            PosY = 0f,
            PosZ = 0f,
            QuatW = 1f,
        };

        // Impulse +Y at point (+1, 0, 0): linVel += (0,1,0); r×J = (0,0,1) → angVel.Z += 1
        body.ApplyPointImpulse(0f, 1f, 0f, pointX: 1f, pointY: 0f, pointZ: 0f);

        Assert.AreEqual(0f, body.LinVelX, 1e-5f);
        Assert.AreEqual(1f, body.LinVelY, 1e-5f);
        Assert.AreEqual(0f, body.LinVelZ, 1e-5f);
        Assert.AreEqual(0f, body.AngVelX, 1e-5f);
        Assert.AreEqual(0f, body.AngVelY, 1e-5f);
        Assert.AreEqual(1f, body.AngVelZ, 1e-5f);
    }

    /// <summary>
    /// Live stability path: yaw-only point impulse keeps linear response and world-Y torque
    /// but drops pitch/roll (the tumble components from ground-plane tire forces).
    /// Lateral force at +Z contact → yaw (ty = rz·jx); pitch/roll stay zero.
    /// </summary>
    [TestMethod]
    public void ApplyPointImpulseYawOnly_KeepsYawDropsPitchRoll()
    {
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            InvInertiaX = 1f,
            InvInertiaY = 1f,
            InvInertiaZ = 1f,
            PosX = 0f,
            PosY = 1f,
            PosZ = 0f,
            QuatW = 1f,
        };

        // Lateral +X impulse at ground contact in front (+Z) and below (−Y from COM):
        // r = (0, -1, 1), J = (1, 0, 0) → full r×J has ty=1 and tz=1; yaw-only keeps ty only.
        body.ApplyPointImpulseYawOnly(1f, 0f, 0f, pointX: 0f, pointY: 0f, pointZ: 1f);

        Assert.AreEqual(1f, body.LinVelX, 1e-5f);
        Assert.AreEqual(0f, body.LinVelY, 1e-5f);
        Assert.AreEqual(0f, body.LinVelZ, 1e-5f);
        Assert.AreEqual(0f, body.AngVelX, 1e-5f, "pitch must be dropped");
        Assert.AreEqual(1f, body.AngVelY, 1e-5f, "yaw from front lateral must remain");
        Assert.AreEqual(0f, body.AngVelZ, 1e-5f, "roll must be dropped");
    }

    /// <summary>
    /// postTick suspImpulse: I = F · dt · n̂. ApplyPointImpulse(I) and ApplyForce(F)+Integrate(dt)
    /// must agree on Δv for a COM-aligned force (no lever arm).
    /// </summary>
    [TestMethod]
    public void KnownSpringForceAndDt_ApplyPointImpulse_MatchesForceIntegrateDeltaV()
    {
        const float force = 120f; // known spring magnitude along +Y
        const float dt = 1f / 60f;
        const float invMass = 0.5f; // mass = 2

        // Path A — retail postTick: immediate point impulse at COM
        var impulseBody = new HkRigidBody
        {
            Mass = 2f,
            InvMass = invMass,
            QuatW = 1f,
        };
        float jy = force * dt; // I = F * dt * n̂.y
        impulseBody.ApplyPointImpulse(0f, jy, 0f, pointX: 0f, pointY: 0f, pointZ: 0f);

        // Path B — deferred force then integrate (no gravity)
        var forceBody = new HkRigidBody
        {
            Mass = 2f,
            InvMass = invMass,
            QuatW = 1f,
        };
        forceBody.ApplyForce(0f, force, 0f);
        forceBody.Integrate(dt, applyGravity: false);

        float expectedDeltaV = force * dt * invMass;
        Assert.AreEqual(expectedDeltaV, impulseBody.LinVelY, 1e-6f);
        Assert.AreEqual(expectedDeltaV, forceBody.LinVelY, 1e-6f);
        Assert.AreEqual(impulseBody.LinVelY, forceBody.LinVelY, 1e-6f);
        Assert.AreEqual(0f, impulseBody.AngVelX + impulseBody.AngVelY + impulseBody.AngVelZ, 1e-6f);
    }

    [TestMethod]
    public void Integrate_AngularVelocity_RotatesAndNormalizesQuaternion()
    {
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            QuatW = 1f,
            AngVelY = MathF.PI, // 180°/s about Y
        };

        // First-order q̇ = ½ ω q needs small steps to approach the exact 90° map.
        const float dt = 0.001f;
        const int steps = 500; // T = 0.5 s → 90° about Y
        for (int i = 0; i < steps; i++)
            body.Integrate(dt, applyGravity: false);

        float len = MathF.Sqrt(
            body.QuatX * body.QuatX + body.QuatY * body.QuatY +
            body.QuatZ * body.QuatZ + body.QuatW * body.QuatW);
        Assert.AreEqual(1f, len, 1e-4f);

        // 90° about Y from identity: q ≈ (0, sin(π/4), 0, cos(π/4))
        const float half = 0.70710678f;
        Assert.AreEqual(0f, body.QuatX, 0.02f);
        Assert.AreEqual(half, body.QuatY, 0.02f);
        Assert.AreEqual(0f, body.QuatZ, 0.02f);
        Assert.AreEqual(half, body.QuatW, 0.02f);
    }

    [TestMethod]
    public void SetMass_UpdatesInvMass()
    {
        var body = new HkRigidBody();
        body.SetMass(4f);
        Assert.AreEqual(4f, body.Mass);
        Assert.AreEqual(0.25f, body.InvMass, 1e-6f);

        body.SetMass(0f);
        Assert.AreEqual(0f, body.Mass);
        Assert.AreEqual(0f, body.InvMass);
    }

    [TestMethod]
    public void Integrate_NonPositiveDt_IsNoOp()
    {
        var body = new HkRigidBody
        {
            Mass = 1f,
            InvMass = 1f,
            QuatW = 1f,
            LinVelY = 3f,
            PosY = 10f,
        };

        body.Integrate(0f, applyGravity: true);
        body.Integrate(-0.1f, applyGravity: true);

        Assert.AreEqual(3f, body.LinVelY);
        Assert.AreEqual(10f, body.PosY);
    }
}

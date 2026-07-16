using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Vehicle;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Port-ready air-stab / upright-restore essentials.
/// Evidence: <c>fn_upright_restore.md</c> (applyAction 0.7 gate),
/// <c>fn_00598320_airStab.md</c> (collision-window + re-ground),
/// Ghidra decompile <c>0x598320</c> / constants plate <c>0xaf3380</c>.
/// Continuous AVD is covered by <see cref="HkVehicleVelocityDamperTests"/> — not duplicated here.
/// </summary>
[TestClass]
public class HkVehicleAirStabilizationTests
{
    private const float Eps = 1e-5f;

    // --- gate: NeedsUprightRestore ---

    [TestMethod]
    public void NeedsUprightRestore_UprightAboveThreshold_False()
    {
        // cos(0) = 1 ≥ 0.7 → no righting
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(1f));
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(HkPhysicsConstants.UprightRestoreDot));
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(0.71f));
    }

    [TestMethod]
    public void NeedsUprightRestore_TiltedOpenInterval_True()
    {
        // 0.1 < upDot < 0.7 → righting active (acos(0.5) ≈ 60°)
        Assert.IsTrue(HkVehicleAirStabilization.NeedsUprightRestore(0.5f));
        Assert.IsTrue(HkVehicleAirStabilization.NeedsUprightRestore(0.69f));
        Assert.IsTrue(HkVehicleAirStabilization.NeedsUprightRestore(0.11f));
    }

    [TestMethod]
    public void NeedsUprightRestore_NearInverted_False()
    {
        // upDot ≤ 0.1 → skip (recovery elsewhere: AVD / re-ground)
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(0.1f));
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(0f));
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(-0.5f));
    }

    [TestMethod]
    public void NeedsUprightRestore_AbsForm_MatchesRetailOpenIntervalWhenPositive()
    {
        // |dot| < 0.7 and above min → same as 0.1 < dot < 0.7 for positive dots
        Assert.IsTrue(HkVehicleAirStabilization.NeedsUprightRestore(0.5f));
        Assert.IsFalse(HkVehicleAirStabilization.NeedsUprightRestore(0.8f));
    }

    // --- upright impulse ---

    [TestMethod]
    public void TryComputeUprightImpulse_Upright_NoImpulse()
    {
        bool ok = HkVehicleAirStabilization.TryComputeUprightImpulse(
            bodyUpX: 0f, bodyUpY: 1f, bodyUpZ: 0f,
            angVelX: 0.2f, angVelY: 0f, angVelZ: 0f,
            throttle: 1f, dt: 1f / 30f, invInertia: 1f,
            out float ix, out float iy, out float iz);

        Assert.IsFalse(ok);
        Assert.AreEqual(0f, ix, Eps);
        Assert.AreEqual(0f, iy, Eps);
        Assert.AreEqual(0f, iz, Eps);
    }

    [TestMethod]
    public void TryComputeUprightImpulse_ZeroThrottle_NoImpulse()
    {
        // Tilted but coasting: m ∝ throttle ⇒ no righting
        bool ok = HkVehicleAirStabilization.TryComputeUprightImpulse(
            bodyUpX: 0.5f, bodyUpY: 0.5f, bodyUpZ: 0f, // will normalize inside or treat as raw?
            angVelX: 0f, angVelY: 0f, angVelZ: 0f,
            throttle: 0f, dt: 0.033f, invInertia: 1f,
            out _, out _, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryComputeUprightImpulse_TiltedWithThrottle_ProducesRightingAboutRollAxis()
    {
        // Body tilted 60° about Z: bodyUp = (sin60, cos60, 0) = (√3/2, 0.5, 0)
        float s = MathF.Sqrt(3f) / 2f;
        float c = 0.5f;
        bool ok = HkVehicleAirStabilization.TryComputeUprightImpulse(
            bodyUpX: s, bodyUpY: c, bodyUpZ: 0f,
            angVelX: 0f, angVelY: 0f, angVelZ: 0f,
            throttle: 1f, dt: 0.1f, invInertia: 1f,
            out float ix, out float iy, out float iz);

        Assert.IsTrue(ok);
        // axis = bodyUp × worldUp = (s, c, 0) × (0,1,0) = (c*0 - 0*1, 0*0 - s*0, s*1 - c*0) = (0, 0, s)
        // → righting purely about +Z (roll back toward upright)
        Assert.AreEqual(0f, ix, Eps);
        Assert.AreEqual(0f, iy, Eps);
        Assert.IsTrue(iz > 0f, $"expected +Z righting impulse, got iz={iz}");

        // m = invI * dt * 0.8 * angle * throttle; angle = acos(0.5) = π/3
        float angle = MathF.Acos(0.5f);
        float expectedM = 1f * 0.1f * HkPhysicsConstants.UprightRestoreMagScale * angle * 1f;
        Assert.AreEqual(expectedM, iz, 1e-4f);
    }

    [TestMethod]
    public void TryComputeUprightImpulse_SubtractsAngVelDampTerm()
    {
        float s = MathF.Sqrt(3f) / 2f;
        float c = 0.5f;
        const float angZ = 2f;
        const float throttle = 1f;

        bool ok = HkVehicleAirStabilization.TryComputeUprightImpulse(
            bodyUpX: s, bodyUpY: c, bodyUpZ: 0f,
            angVelX: 0f, angVelY: 0f, angVelZ: angZ,
            throttle: throttle, dt: 0.1f, invInertia: 1f,
            out float ix, out float iy, out float iz);

        Assert.IsTrue(ok);
        Assert.AreEqual(0f, ix, Eps);
        Assert.AreEqual(0f, iy, Eps);

        float angle = MathF.Acos(0.5f);
        float m = 1f * 0.1f * HkPhysicsConstants.UprightRestoreMagScale * angle * throttle;
        float damp = HkPhysicsConstants.UprightRestoreAngDamp * throttle;
        Assert.AreEqual(m - angZ * damp, iz, 1e-4f);
    }

    [TestMethod]
    public void TryComputeUprightImpulse_NonFiniteOrNonPositiveDt_NoImpulse()
    {
        float s = MathF.Sqrt(3f) / 2f;
        Assert.IsFalse(HkVehicleAirStabilization.TryComputeUprightImpulse(
            s, 0.5f, 0f, 0f, 0f, 0f, 1f, 0f, 1f, out _, out _, out _));
        Assert.IsFalse(HkVehicleAirStabilization.TryComputeUprightImpulse(
            s, 0.5f, 0f, 0f, 0f, 0f, 1f, -0.1f, 1f, out _, out _, out _));
        Assert.IsFalse(HkVehicleAirStabilization.TryComputeUprightImpulse(
            s, 0.5f, 0f, 0f, 0f, 0f, 1f, float.NaN, 1f, out _, out _, out _));
    }

    [TestMethod]
    public void ApplyUprightRestore_AddsImpulseToAngularVelocity()
    {
        float s = MathF.Sqrt(3f) / 2f;
        float wx = 0f, wy = 0f, wz = 0f;

        bool applied = HkVehicleAirStabilization.ApplyUprightRestore(
            bodyUpX: s, bodyUpY: 0.5f, bodyUpZ: 0f,
            ref wx, ref wy, ref wz,
            throttle: 1f, dt: 0.1f, invInertia: 1f);

        Assert.IsTrue(applied);
        Assert.AreEqual(0f, wx, Eps);
        Assert.AreEqual(0f, wy, Eps);
        Assert.IsTrue(wz > 0f);
    }

    [TestMethod]
    public void ApplyUprightRestore_Upright_LeavesAngVelUnchanged()
    {
        float wx = 1f, wy = 2f, wz = 3f;
        bool applied = HkVehicleAirStabilization.ApplyUprightRestore(
            bodyUpX: 0f, bodyUpY: 1f, bodyUpZ: 0f,
            ref wx, ref wy, ref wz,
            throttle: 1f, dt: 0.1f, invInertia: 1f);

        Assert.IsFalse(applied);
        Assert.AreEqual(1f, wx, Eps);
        Assert.AreEqual(2f, wy, Eps);
        Assert.AreEqual(3f, wz, Eps);
    }

    // --- re-ground (optional, not server hot path) ---

    [TestMethod]
    public void ComputeReGroundCastStartY_RaisesByConstant10()
    {
        // DAT_00a110d8 = 10.0 — client recovery raise before terrain cast; NOT hot-path AVD.
        Assert.AreEqual(25f, HkVehicleAirStabilization.ComputeReGroundCastStartY(15f), Eps);
        Assert.AreEqual(HkPhysicsConstants.ReGroundYRaise, 10f, Eps);
        Assert.AreEqual(
            0f + HkPhysicsConstants.ReGroundYRaise,
            HkVehicleAirStabilization.ComputeReGroundCastStartY(0f),
            Eps);
    }

    [TestMethod]
    public void ResolveReGroundPositionY_UsesTerrainHeightResult()
    {
        // After cast, position Y becomes cast result (not startY).
        Assert.AreEqual(12.5f, HkVehicleAirStabilization.ResolveReGroundPositionY(terrainHeight: 12.5f), Eps);
    }

    // --- collision window helpers (param-based; entity stamp DEFERRED) ---

    [TestMethod]
    public void IsInCollisionWindow_Within6400Ms_True()
    {
        // 0x1900 = 6400 ms — full entity last-collision wiring is DEFERRED; accept timestamps.
        Assert.IsTrue(HkVehicleAirStabilization.IsInCollisionWindow(
            nowMs: 10000, lastCollisionMs: 10000));
        Assert.IsTrue(HkVehicleAirStabilization.IsInCollisionWindow(
            nowMs: 10000 + 6399, lastCollisionMs: 10000));
        Assert.IsFalse(HkVehicleAirStabilization.IsInCollisionWindow(
            nowMs: 10000 + 6400, lastCollisionMs: 10000));
        Assert.AreEqual(6400, HkPhysicsConstants.CollisionWindowMs);
    }

    [TestMethod]
    public void ShouldRunPostCollisionRecovery_EdgeWhenWindowExpires()
    {
        // Recovery runs once: was in window, now expired.
        Assert.IsTrue(HkVehicleAirStabilization.ShouldRunPostCollisionRecovery(
            nowMs: 16400, lastCollisionMs: 10000, wasInCollision: true));
        Assert.IsFalse(HkVehicleAirStabilization.ShouldRunPostCollisionRecovery(
            nowMs: 11000, lastCollisionMs: 10000, wasInCollision: true));
        Assert.IsFalse(HkVehicleAirStabilization.ShouldRunPostCollisionRecovery(
            nowMs: 20000, lastCollisionMs: 10000, wasInCollision: false));
    }

    [TestMethod]
    public void IsChassisMovingForAirStab_UsesRetailEpsilon()
    {
        // DAT_009d54a8 ≈ 1.19e-7 — in-window impulse only if |v| > eps.
        Assert.IsFalse(HkVehicleAirStabilization.IsChassisMovingForAirStab(0f, 0f, 0f));
        Assert.IsFalse(HkVehicleAirStabilization.IsChassisMovingForAirStab(
            HkPhysicsConstants.AirStabMovingEpsilon, 0f, 0f));
        Assert.IsTrue(HkVehicleAirStabilization.IsChassisMovingForAirStab(1f, 0f, 0f));
    }

    [TestMethod]
    public void Constants_MatchVerifiedPlate()
    {
        Assert.AreEqual(0.7f, HkPhysicsConstants.UprightRestoreDot, Eps);
        Assert.AreEqual(0.1f, HkPhysicsConstants.UprightRestoreMinDot, Eps);
        Assert.AreEqual(0.8f, HkPhysicsConstants.UprightRestoreMagScale, Eps);
        Assert.AreEqual(0.1f, HkPhysicsConstants.UprightRestoreAngDamp, Eps);
        Assert.AreEqual(10f, HkPhysicsConstants.ReGroundYRaise, Eps);
        Assert.AreEqual(6400, HkPhysicsConstants.CollisionWindowMs);
    }
}

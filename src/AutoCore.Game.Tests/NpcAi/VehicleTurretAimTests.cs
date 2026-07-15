using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>
/// Relative turret yaw for ghost <c>WantedTurretDirection</c> (client vehicle+0x15c).
/// Chassis-relative, wrapped to <c>[0, 2π)</c>.
/// </summary>
[TestClass]
public class VehicleTurretAimTests
{
    private const float Epsilon = 1e-4f;
    private static readonly float TwoPi = MathF.PI * 2f;

    /// <summary>Identity quaternion: yaw 0, face +Z (Atan2(x,z) convention).</summary>
    private static Quaternion FacePositiveZ => new(0f, 0f, 0f, 1f);

    [TestMethod]
    public void Compute_FaceZ_TargetAhead_ReturnsZero()
    {
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(0f, 0f, 50f));

        Assert.AreEqual(0f, dir, Epsilon);
    }

    [TestMethod]
    public void Compute_FaceZ_TargetRight_ReturnsHalfPi()
    {
        // Aim +X while facing +Z → relative yaw +π/2.
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(40f, 0f, 0f));

        Assert.AreEqual(MathF.PI * 0.5f, dir, Epsilon);
    }

    [TestMethod]
    public void Compute_FaceZ_TargetLeft_ReturnsThreeHalfPi()
    {
        // Aim −X while facing +Z → relative −π/2 wrapped into [0, 2π) → 3π/2.
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(-40f, 0f, 0f));

        Assert.AreEqual(MathF.PI * 1.5f, dir, Epsilon);
    }

    [TestMethod]
    public void Compute_Result_IsInHalfOpenTwoPi()
    {
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(1f, 0f, 1f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(-20f, 5f, -30f));

        Assert.IsTrue(dir >= 0f && dir < TwoPi, $"expected [0, 2π), got {dir}");
    }

    [TestMethod]
    public void Compute_CoincidentXZ_ReturnsZero()
    {
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(3f, 0f, 7f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(3f, 10f, 7f)); // same XZ, different Y

        Assert.AreEqual(0f, dir, Epsilon);
    }

    [TestMethod]
    public void NormalizeToTwoPi_NegativeAngle_WrapsPositive()
    {
        var wrapped = VehicleTurretAim.NormalizeToTwoPi(-MathF.PI * 0.5f);
        Assert.AreEqual(MathF.PI * 1.5f, wrapped, Epsilon);
    }

    [TestMethod]
    public void NormalizeToTwoPi_AboveTwoPi_WrapsIntoRange()
    {
        var wrapped = VehicleTurretAim.NormalizeToTwoPi(MathF.PI * 2.5f);
        Assert.AreEqual(MathF.PI * 0.5f, wrapped, Epsilon);
        Assert.IsTrue(wrapped >= 0f && wrapped < TwoPi);
    }

    [TestMethod]
    public void NormalizeToTwoPi_Zero_Unchanged()
    {
        Assert.AreEqual(0f, VehicleTurretAim.NormalizeToTwoPi(0f), Epsilon);
    }

    [TestMethod]
    public void Compute_RotatedChassis_TargetRelativeToFacing()
    {
        // Face +X (yaw = π/2), target further +X → relative aim 0.
        var half = MathF.PI * 0.25f;
        var facePlusX = new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: facePlusX,
            targetPosition: new Vector3(50f, 0f, 0f));
        Assert.AreEqual(0f, dir, 1e-3f);
    }

    [TestMethod]
    public void Compute_OffsetBasePosition_UsesDeltaNotSum()
    {
        // vehicle (2,0,3) → target (5,0,7): dx=3, dz=4 → atan2(3,4).
        // Mutating dz to target.Z+pos.Z (=10) would change the angle — must use subtraction.
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(2f, 0f, 3f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(5f, 0f, 7f));
        Assert.AreEqual(MathF.Atan2(3f, 4f), dir, 1e-4f);
    }

    [TestMethod]
    public void Compute_TargetBehind_ReturnsPi()
    {
        // Face +Z at z=10, target further south — relative π (not 0 from sum-of-Z mutant).
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 10f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(0f, 0f, 5f));
        Assert.AreEqual(MathF.PI, dir, 1e-3f);
    }

    [TestMethod]
    public void Compute_NearCoincident_ReturnsZero()
    {
        // Below EpsilonSq (1e-6): dx=1e-4 → dx²=1e-8.
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(1e-4f, 0f, 0f));
        Assert.AreEqual(0f, dir, Epsilon);
    }

    [TestMethod]
    public void Compute_JustBeyondEpsilon_ComputesAngle()
    {
        // dx=0.01 → dx²=1e-4 > 1e-6; must not treat as coincident.
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(0.01f, 0f, 0f));
        Assert.AreEqual(MathF.PI * 0.5f, dir, 1e-3f);
    }

    [TestMethod]
    public void Compute_DistSqEqualToEpsilonSq_StillComputes()
    {
        // EpsilonSq is 1e-6. Use Sqrt so dx² is exactly 1e-6f. Gate is strict <, not <=.
        // Mutating to <= would wrongly return 0.
        var dx = MathF.Sqrt(1e-6f);
        var dir = VehicleTurretAim.ComputeWantedDirection(
            vehiclePosition: new Vector3(0f, 0f, 0f),
            vehicleRotation: FacePositiveZ,
            targetPosition: new Vector3(dx, 0f, 0f));
        Assert.AreEqual(MathF.PI * 0.5f, dir, 1e-3f);
    }
}

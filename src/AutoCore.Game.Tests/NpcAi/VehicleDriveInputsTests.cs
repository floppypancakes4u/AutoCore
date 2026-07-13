using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>
/// Client <c>CVOGVehicle::MoveToTarget3DPoint</c> (0x004fc650) thr/steer from facing vs aim.
/// </summary>
[TestClass]
public class VehicleDriveInputsTests
{
    [TestMethod]
    public void Compute_FacingAim_FullThrottleLowSteer()
    {
        // Face +Z, aim further +Z.
        var (thr, steer, _) = VehicleDriveInputs.Compute(
            position: new Vector3(0f, 0f, 0f),
            facingYaw: 0f,
            aim: new Vector3(0f, 0f, 50f),
            speed: 12f);

        Assert.IsTrue(thr > 0.8f, $"aligned should thr high, got {thr}");
        Assert.IsTrue(MathF.Abs(steer) < 0.15f, $"aligned should low steer, got {steer}");
    }

    [TestMethod]
    public void Compute_AimToRight_PositiveSteer()
    {
        // Face +Z, aim to +X (right).
        var (_, steer, _) = VehicleDriveInputs.Compute(
            position: new Vector3(0f, 0f, 0f),
            facingYaw: 0f,
            aim: new Vector3(40f, 0f, 10f),
            speed: 12f);

        Assert.IsTrue(steer > 0.3f, $"aim right should steer right, got {steer}");
    }

    [TestMethod]
    public void Compute_AimToLeft_NegativeSteer()
    {
        var (_, steer, _) = VehicleDriveInputs.Compute(
            position: new Vector3(0f, 0f, 0f),
            facingYaw: 0f,
            aim: new Vector3(-40f, 0f, 10f),
            speed: 12f);

        Assert.IsTrue(steer < -0.3f, $"aim left should steer left, got {steer}");
    }
}

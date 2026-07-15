using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

[TestClass]
public class TerrainContactPlaneTests
{
    [TestCleanup]
    public void TearDown()
    {
        TerrainContactPlane.DefaultHalfLength = 4.0f;
        TerrainContactPlane.DefaultHalfWidth = 1.8f;
        TerrainContactPlane.MaxPitchRollRadians = 0.65f;
        TerrainContactPlane.DefaultGroundClearance = 0f;
    }

    [TestMethod]
    public void TryAlign_FlatField_NearZeroPitchRoll()
    {
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(10f, 0f, 10f),
            yawRadians: 0f,
            sample: Flat(5f),
            out var grounded,
            out var rot,
            groundClearance: 0.5f);

        Assert.IsTrue(ok);
        Assert.AreEqual(5.5f, grounded.Y, 0.02f); // plane + clearance
        var fwd = TerrainContactPlane.ForwardFromQuaternion(rot);
        Assert.IsTrue(MathF.Abs(fwd.Y) < 0.05f, $"flat forward.Y should be ~0, got {fwd.Y}");
    }

    [TestMethod]
    public void TryAlign_SteepSlopeAlongZ_ForwardHasPitchY()
    {
        // Y = z * 0.35 → ~19° grade; vehicle faces +Z (yaw 0)
        TerrainContactPlane.DefaultHalfLength = 4f;
        TerrainContactPlane.DefaultHalfWidth = 1.5f;
        TerrainContactPlane.DefaultGroundClearance = 0f;

        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0f, 0f, 10f),
            yawRadians: 0f,
            sample: (float x, float z, out float y) =>
            {
                y = z * 0.35f;
                return true;
            },
            out var grounded,
            out var rot,
            groundClearance: 0f);

        Assert.IsTrue(ok);
        var fwd = TerrainContactPlane.ForwardFromQuaternion(rot);
        Assert.IsTrue(fwd.Y > 0.15f,
            $"expected climb pitch (forward.Y>0.15), got fwd=({fwd.X},{fwd.Y},{fwd.Z}) rot=({rot.X},{rot.Y},{rot.Z},{rot.W})");
        // Should still mostly face +Z
        Assert.IsTrue(fwd.Z > 0.7f, $"forward.Z should dominate, got {fwd.Z}");
        // Y is center sample only (not average of look-ahead samples)
        Assert.AreEqual(10f * 0.35f, grounded.Y, 0.01f);
    }

    [TestMethod]
    public void TryAlign_HighTerrainAhead_DoesNotLiftCenterY()
    {
        // Cliff 4m ahead: multi-sample average would float the chassis; center must stay low.
        TerrainContactPlane.DefaultHalfLength = 4f;
        TerrainContactPlane.DefaultHalfWidth = 1f;
        TerrainContactPlane.DefaultGroundClearance = 0f;

        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0f, 0f, 0f),
            yawRadians: 0f, // +Z
            sample: (float x, float z, out float y) =>
            {
                y = z >= 3f ? 10f : 1f;
                return true;
            },
            out var grounded,
            out _,
            halfLength: 4f,
            halfWidth: 1f,
            groundClearance: 0f);

        Assert.IsTrue(ok);
        Assert.AreEqual(1f, grounded.Y, 0.01f);
    }

    [TestMethod]
    public void TryAlign_PerVehicleWheelHardpoints_SamplesUnderThoseWheels()
    {
        // Two vehicles on same map: short track stays at y=2, long track straddles a ramp.
        TerrainContactPlane.DefaultGroundClearance = 0f;
        TerrainContactPlane.HeightSample flatThenRamp = (float x, float z, out float y) =>
        {
            y = z > 1.5f ? 5f : 2f;
            return true;
        };

        var shortWheels = new[]
        {
            new Vector3(1f, 0f, 1f),
            new Vector3(-1f, 0f, 1f),
            new Vector3(1f, 0f, -1f),
            new Vector3(-1f, 0f, -1f),
        };
        var longWheels = new[]
        {
            new Vector3(1f, 0f, 3f),
            new Vector3(-1f, 0f, 3f),
            new Vector3(1f, 0f, -1f),
            new Vector3(-1f, 0f, -1f),
        };

        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f, flatThenRamp,
            out var shortPos, out _,
            halfLength: 1f, halfWidth: 1f, groundClearance: 0f, wheelHardPoints: shortWheels));
        Assert.IsTrue(TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f, flatThenRamp,
            out var longPos, out _,
            halfLength: 3f, halfWidth: 1f, groundClearance: 0f, wheelHardPoints: longWheels));

        // Short vehicle: all wheels on flat y=2 → Y=2
        Assert.AreEqual(2f, shortPos.Y, 0.01f);
        // Long vehicle: front wheels on ramp y=5, rear on y=2 → mean y=3.5
        Assert.AreEqual(3.5f, longPos.Y, 0.01f);
        Assert.AreNotEqual(shortPos.Y, longPos.Y, 0.01f);
    }

    [TestMethod]
    public void TryAlign_CrossSlope_RollsChassis()
    {
        // Y = x * 0.3 → slope to the side; face +Z
        TerrainContactPlane.DefaultGroundClearance = 0f;
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0f, 0f, 0f),
            yawRadians: 0f,
            sample: (float x, float z, out float y) =>
            {
                y = x * 0.3f;
                return true;
            },
            out _,
            out var rot,
            halfLength: 3f,
            halfWidth: 2f,
            groundClearance: 0f);

        Assert.IsTrue(ok);
        // Right vector should have Y component when rolled
        var x = rot.X;
        var y = rot.Y;
        var z = rot.Z;
        var w = rot.W;
        // right = rotate (1,0,0): (1-2(y²+z²), 2(xy+zw), 2(xz-yw))
        var rightY = 2f * ((x * y) + (z * w));
        Assert.IsTrue(MathF.Abs(rightY) > 0.1f, $"expected roll (right.Y), got {rightY}");
    }

    [TestMethod]
    public void TryAlign_SampleFails_ReturnsFalse()
    {
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0f, 0f, 0f),
            0f,
            sample: (float x, float z, out float y) =>
            {
                y = 0f;
                return false;
            },
            out var grounded,
            out _);

        Assert.IsFalse(ok);
        Assert.AreEqual(0f, grounded.Y, 0.001f);
    }

    [TestMethod]
    public void TryAlign_NullHeightfield_ReturnsFalse()
    {
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(1f, 2f, 3f),
            0.5f,
            heightfield: null,
            out var grounded,
            out var rot);

        Assert.IsFalse(ok);
        Assert.AreEqual(2f, grounded.Y, 0.001f);
        Assert.AreEqual(TerrainContactPlane.YawOnly(0.5f).Y, rot.Y, 0.001f);
    }

    [TestMethod]
    public void ResolveVehicleFootprint_UsesWheelHardpoints()
    {
        var hps = new[]
        {
            new Vector3(1.2f, 0f, 2.5f),
            new Vector3(-1.2f, 0f, 2.5f),
            new Vector3(1.2f, 0f, -2.5f),
            new Vector3(-1.2f, 0f, -2.5f),
            default,
            default,
        };
        var radii = new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f };

        TerrainContactPlane.ResolveVehicleFootprint(
            hps, radii, suspensionLengthFront: 0.8f,
            out var hl, out var hw, out var clr);

        Assert.AreEqual(2.5f, hl, 0.01f);
        Assert.AreEqual(1.2f, hw, 0.01f);
        // Clearance is a thin pad only (not full wheel radius — that floated NPCs).
        Assert.AreEqual(TerrainContactPlane.DefaultGroundClearance, clr, 0.001f);
    }

    private static TerrainContactPlane.HeightSample Flat(float y)
        => (float x, float z, out float worldY) =>
        {
            worldY = y;
            return true;
        };
}

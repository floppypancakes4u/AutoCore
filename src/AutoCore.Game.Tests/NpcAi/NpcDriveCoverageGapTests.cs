using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.Serialization;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Map;

/// <summary>Coverage gaps for finish-code-work on NPC drive controller stack.</summary>
[TestClass]
public class NpcDriveCoverageGapTests
{
    [TestCleanup]
    public void TearDown()
    {
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
        VehicleGroundMetricsCache.Clear();
        VehicleGroundMetricsCache.RideHeightScale = 1f;
        VehicleGroundMetricsCache.MaxRideHeight = 1.25f;
        TerrainContactPlane.DefaultGroundClearance = 0f;
        TerrainContactPlane.MaxPitchRollRadians = 0.65f;
        NpcVehicleDriveController.MaxPathDrift = 6f;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = MathF.PI * 1.5f;
    }

    [TestMethod]
    public void DriveController_WithHeightfield_AlignsAndSetsDriveInputs()
    {
        NpcVehicleDriveController.Enabled = true;
        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(4, 4, new ushort[]
        {
            256, 256, 512, 512,
            256, 256, 512, 512,
            768, 768, 1024, 1024,
            768, 768, 1024, 1024,
        });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(tga, 4, 4, 10f, out var field, out var err), err);

        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(30, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(30, 0, 30), AcceptDistance = 2f });

        var hard = new PathStepResult
        {
            NewPosition = new Vector3(5, 0, 0),
            Velocity = new Vector3(12, 0, 0),
            NewIndex = 1,
            NewDirection = 1,
        };
        var half = MathF.PI * 0.25f;
        var faceX = new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));

        var r = NpcVehicleDriveController.Apply(
            hard,
            previousPosition: new Vector3(0, 4, 0),
            previousRotation: faceX,
            cruiseSpeed: 12f,
            dt: 0.1f,
            path: path,
            nowMs: 1000,
            previousVelocity: new Vector3(12, 0, 0),
            laneOffset: 0.5f,
            heightfield: field,
            vehicle: null);

        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(r.Throttle > 0.5f);
        Assert.IsTrue(float.IsFinite(r.NewPosition.Y));
    }

    [TestMethod]
    public void DriveController_EmptyPathPoints_ReturnsHard()
    {
        NpcVehicleDriveController.Enabled = true;
        var hard = new PathStepResult { NewPosition = new Vector3(1, 0, 0) };
        var r = NpcVehicleDriveController.Apply(
            hard, default, Quaternion.Default, 12f, 0.1f, new MapPathTemplate(), 0);
        Assert.IsFalse(r.HasDriveInputs);
    }

    [TestMethod]
    public void DriveController_MaxPathDrift_PullsTowardHardTarget()
    {
        NpcVehicleDriveController.Enabled = true;
        NpcVehicleDriveController.MaxPathDrift = 1f;
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 0.1f;
        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(100, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(200, 0, 0), AcceptDistance = 2f });
        var hard = new PathStepResult
        {
            NewPosition = new Vector3(50, 0, 0),
            Velocity = new Vector3(12, 0, 0),
            NewIndex = 1,
        };
        var r = NpcVehicleDriveController.Apply(
            hard, new Vector3(0, 0, 0), Quaternion.Default, 12f, 0.1f, path, 1000,
            previousVelocity: new Vector3(0, 0, 12f));
        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(r.NewPosition.X > 0.5f, $"expected pull toward hard X, pos={r.NewPosition}");
    }

    [TestMethod]
    public void DriveController_ZeroWaitCarry_WithLaneOffset()
    {
        NpcVehicleDriveController.Enabled = true;
        var path = new MapPathTemplate();
        for (var i = 0; i < 4; i++)
            path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(i * 25f, 0, 0), AcceptDistance = 2f });
        var hard = new PathStepResult
        {
            NewPosition = path.Points[0].Position,
            Arrived = true,
            WaitUntilMs = 500,
            NewIndex = 1,
        };
        var yaw = MathF.PI * 0.5f;
        var face = new Quaternion(0f, MathF.Sin(yaw * 0.5f), 0f, MathF.Cos(yaw * 0.5f));
        var r = NpcVehicleDriveController.Apply(
            hard, path.Points[0].Position, face, 12f, 0.1f, path, nowMs: 1000,
            previousVelocity: new Vector3(12, 0, 0), laneOffset: 1.5f);
        Assert.IsTrue(r.HasDriveInputs);
        Assert.IsTrue(Xz(r.Velocity) > 5f);
    }

    [TestMethod]
    public void Terrain_MaxTiltClamp_KeepsUprightBand()
    {
        TerrainContactPlane.MaxPitchRollRadians = 0.2f;
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) => { y = z * 2f; return true; },
            out _, out var rot, halfLength: 2f, halfWidth: 1f, groundClearance: 0f);
        Assert.IsTrue(ok);
        var fwd = TerrainContactPlane.ForwardFromQuaternion(rot);
        Assert.IsTrue(MathF.Abs(fwd.Y) < 0.35f, $"tilt clamp failed fwd.Y={fwd.Y}");
    }

    [TestMethod]
    public void Terrain_SampleFailsOnSide_ReturnsFalse()
    {
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) =>
            {
                y = 0f;
                return MathF.Abs(x) < 0.01f && MathF.Abs(z) < 0.01f;
            },
            out _, out _, halfLength: 2f, halfWidth: 1f);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Terrain_ResolveFootprint_DefaultsWhenEmptyHardpoints()
    {
        TerrainContactPlane.ResolveVehicleFootprint(null, null, 0f, out var hl, out var hw, out var c);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfLength, hl, 0.01f);
        Assert.AreEqual(TerrainContactPlane.DefaultHalfWidth, hw, 0.01f);
    }

    [TestMethod]
    public void Terrain_FromBasis_OrthogonalIdentity()
    {
        var q = TerrainContactPlane.FromBasisColumns(1, 0, 0, 0, 1, 0, 0, 0, 1);
        Assert.AreEqual(0f, q.X, 0.05f);
        Assert.AreEqual(0f, q.Y, 0.05f);
        Assert.AreEqual(0f, q.Z, 0.05f);
        Assert.AreEqual(1f, MathF.Abs(q.W), 0.05f);
    }

    [TestMethod]
    public void Terrain_WheelSamples_FewerThanTwo_FallsBackToBox()
    {
        var wheels = new[] { new Vector3(1f, 0f, 1f) };
        var ok = TerrainContactPlane.TryAlign(
            new Vector3(0, 0, 0), 0f,
            sample: (float x, float z, out float y) => { y = 3f; return true; },
            out var g, out _,
            halfLength: 2f, halfWidth: 1f, groundClearance: 0f, wheelHardPoints: wheels);
        Assert.IsTrue(ok);
        Assert.AreEqual(3f, g.Y, 0.01f);
    }

    [TestMethod]
    public void Metrics_BuildFromCloneBaseVehicle_Uninitialized()
    {
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.45f, 0.45f, 0.45f, 0.45f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0.1f, 2f), new Vector3(-1f, 0.1f, 2f),
                new Vector3(1f, 0.1f, -2f), new Vector3(-1f, 0.1f, -2f),
            },
        };
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = vs;
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 77 };

        Assert.AreEqual(1, VehicleGroundMetricsCache.BuildFromCloneBases(
            new Dictionary<int, CloneBase> { [77] = cv }));
        Assert.IsTrue(VehicleGroundMetricsCache.TryGet(77, out var m));
        Assert.AreEqual(0.45f - 0.1f, m.ChassisHeightAboveTerrain, 0.01f);
        Assert.AreEqual(m.ChassisHeightAboveTerrain, VehicleGroundMetricsCache.GetRideHeight(77), 0.001f);
    }

    [TestMethod]
    public void Metrics_GetRideHeight_Miss_UsesDefaultClearance()
    {
        VehicleGroundMetricsCache.Clear();
        TerrainContactPlane.DefaultGroundClearance = 0.12f;
        Assert.AreEqual(0.12f, VehicleGroundMetricsCache.GetRideHeight(123456), 0.001f);
    }

    [TestMethod]
    public void DriveController_WithVehicleCloneBase_UsesRideHeightCache()
    {
        NpcVehicleDriveController.Enabled = true;
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 2f), new Vector3(-1f, 0f, 2f),
                new Vector3(1f, 0f, -2f), new Vector3(-1f, 0f, -2f),
            },
        };
        var cv = (CloneBaseVehicle)FormatterServices.GetUninitializedObject(typeof(CloneBaseVehicle));
        cv.VehicleSpecific = vs;
        cv.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = 9001 };
        VehicleGroundMetricsCache.BuildFromCloneBases(new Dictionary<int, CloneBase> { [9001] = cv });

        var vehicle = new Vehicle();
        vehicle.AssignCloneBaseForTests(cv);

        using var tga = MapTerrainHeightfieldTests.BuildHeightTga(2, 2, new ushort[] { 256, 256, 256, 256 });
        Assert.IsTrue(MapTerrainHeightfield.TryLoad(tga, 2, 2, 10f, out var field, out var err), err);

        var path = new MapPathTemplate();
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(0, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(20, 0, 0), AcceptDistance = 2f });
        path.Points.Add(new MapPathTemplate.MapPathPoint { Position = new Vector3(40, 0, 0), AcceptDistance = 2f });

        var hard = new PathStepResult
        {
            NewPosition = new Vector3(5, 0, 0),
            Velocity = new Vector3(12, 0, 0),
            NewIndex = 1,
        };
        var yaw = MathF.PI * 0.5f;
        var face = new Quaternion(0f, MathF.Sin(yaw * 0.5f), 0f, MathF.Cos(yaw * 0.5f));
        var r = NpcVehicleDriveController.Apply(
            hard, new Vector3(0, 0, 0), face, 12f, 0.1f, path, 1000,
            previousVelocity: new Vector3(12, 0, 0),
            heightfield: field,
            vehicle: vehicle);

        Assert.IsTrue(r.HasDriveInputs);
        // terrain Y for height16=256 scale4 = 4.0; + ride 0.5 ≈ 4.5
        Assert.IsTrue(r.NewPosition.Y > 4.2f, $"expected ride height above terrain, Y={r.NewPosition.Y}");
    }

    private static float Xz(Vector3 v) => MathF.Sqrt((v.X * v.X) + (v.Z * v.Z));
}

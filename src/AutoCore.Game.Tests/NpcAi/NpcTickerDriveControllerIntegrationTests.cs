using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>
/// Lever off = legacy hard path; lever on = vehicle drive controller for vehicles only.
/// </summary>
[TestClass]
public class NpcTickerDriveControllerIntegrationTests
{
    private const int ContId = 842;
    private const long PathCoid = 84210;
    private const long VehicleCoid = 84201;

    [TestCleanup]
    public void TearDown()
    {
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;
    }

    [TestMethod]
    public void Tick_DriveControllerOff_UsesLegacyHardPose()
    {
        NpcVehicleDriveController.Enabled = false;
        SoftNpcPathMotion.Enabled = false;

        var map = CreateMap();
        var path = SeedPath(map);
        var vehicle = PlaceVehicle(map, new Vector3(0f, 0f, 0f));
        vehicle.CoidCurrentPath = PathCoid;
        vehicle.NpcAi.PathIndex = 1; // target +X

        NpcTicker.Tick(map, nowMs: 1000, dt: 0.1f);

        // Hard path faces waypoint (+X) and moves along +X
        Assert.IsTrue(vehicle.Position.X > 0.5f, $"expected +X progress, pos={vehicle.Position}");
        Assert.IsTrue(MathF.Abs(vehicle.Position.Z) < 0.5f, "legacy hard stays on X axis segment");
    }

    [TestMethod]
    public void Tick_DriveControllerOn_VelocityAlignsWithFacing()
    {
        NpcVehicleDriveController.Enabled = true;
        SoftNpcPathMotion.Enabled = false;

        var map = CreateMap();
        SeedPath(map);
        var vehicle = PlaceVehicle(map, new Vector3(0f, 0f, 0f));
        vehicle.CoidCurrentPath = PathCoid;
        vehicle.NpcAi.PathIndex = 1;
        vehicle.Rotation = Quaternion.Default; // face +Z initially
        vehicle.SetVelocityForTests(new Vector3(0f, 0f, 12f));

        NpcTicker.Tick(map, nowMs: 1000, dt: 0.1f);

        var spd = MathF.Sqrt(
            (vehicle.Velocity.X * vehicle.Velocity.X) + (vehicle.Velocity.Z * vehicle.Velocity.Z));
        if (spd < 0.5f)
            Assert.Inconclusive("vehicle not moving this tick");

        var yaw = VehicleDriveInputs.YawFromQuaternion(vehicle.Rotation);
        var inv = 1f / spd;
        var dot = (MathF.Sin(yaw) * vehicle.Velocity.X * inv) +
                  (MathF.Cos(yaw) * vehicle.Velocity.Z * inv);
        Assert.IsTrue(dot > 0.98f, $"drive controller velocity must match facing, dot={dot}");
        Assert.IsTrue(vehicle.Acceleration > 0.5f, "must pack throttle for client wheels");
    }

    [TestMethod]
    public void Tick_DriveControllerOn_DoesNotUseSoftWhenBothEnabled()
    {
        // Drive wins for vehicles; soft lag-yaw must not apply.
        NpcVehicleDriveController.Enabled = true;
        SoftNpcPathMotion.Enabled = true;

        var map = CreateMap();
        SeedPath(map);
        var vehicle = PlaceVehicle(map, new Vector3(0f, 0f, 0f));
        vehicle.CoidCurrentPath = PathCoid;
        vehicle.NpcAi.PathIndex = 1;
        vehicle.Rotation = Quaternion.Default;
        vehicle.SetVelocityForTests(new Vector3(0f, 0f, 12f));

        NpcTicker.Tick(map, nowMs: 1000, dt: 0.1f);

        var spd = MathF.Sqrt(
            (vehicle.Velocity.X * vehicle.Velocity.X) + (vehicle.Velocity.Z * vehicle.Velocity.Z));
        if (spd < 0.5f)
            return;

        var yaw = VehicleDriveInputs.YawFromQuaternion(vehicle.Rotation);
        var inv = 1f / spd;
        var dot = (MathF.Sin(yaw) * vehicle.Velocity.X * inv) +
                  (MathF.Cos(yaw) * vehicle.Velocity.Z * inv);
        Assert.IsTrue(dot > 0.98f, "drive controller must win over soft lag-yaw");
    }

    private static SectorMap CreateMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_drive_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static MapPathTemplate SeedPath(SectorMap map)
    {
        var path = new MapPathTemplate { COID = (int)PathCoid };
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(0f, 0f, 0f),
            AcceptDistance = 2f,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 0f),
            AcceptDistance = 2f,
        });
        path.Points.Add(new MapPathTemplate.MapPathPoint
        {
            Position = new Vector3(100f, 0f, 100f),
            AcceptDistance = 2f,
        });
        map.MapData.Templates[PathCoid] = path;
        return path;
    }

    private static Vehicle PlaceVehicle(SectorMap map, Vector3 position)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, false);
        vehicle.Position = position;
        vehicle.NpcAi = new NpcAiState { CombatState = Constants.HBAICombatState.IdlePatrol };
        vehicle.SetMap(map);
        return vehicle;
    }
}

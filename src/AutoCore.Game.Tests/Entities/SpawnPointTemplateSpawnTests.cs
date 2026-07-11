using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;

[TestClass]
public class SpawnPointTemplateSpawnTests
{
    [TestInitialize]
    public void TestInitialize()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void ApplySpawnPath_SetsPathPatrolAndReverse()
    {
        var creature = new Creature();
        var template = new SpawnPointTemplate { MapPathCoid = 555, InitialPatrolDistance = 12.5f };
        var path = new MapPathTemplate { ReverseDirection = true };

        SpawnPoint.ApplySpawnPath(creature, template, path);

        Assert.AreEqual(555, creature.CoidCurrentPath);
        Assert.AreEqual(12.5f, creature.PatrolDistance);
        Assert.IsTrue(creature.PathReversing);
    }

    [TestMethod]
    public void ApplySpawnPath_NoPathCoid_LeavesDefaults()
    {
        var creature = new Creature();
        var template = new SpawnPointTemplate { MapPathCoid = 0, InitialPatrolDistance = 99f };

        SpawnPoint.ApplySpawnPath(creature, template, null);

        Assert.AreEqual(-1, creature.CoidCurrentPath);
        Assert.AreEqual(0f, creature.PatrolDistance);
        Assert.IsFalse(creature.PathReversing);
    }

    [TestMethod]
    public void ResolveDriverCbid_TemplateThenDefaultDriver()
    {
        Assert.AreEqual(2071, SpawnPoint.ResolveDriverCbid(2071, 500));
        Assert.AreEqual(500, SpawnPoint.ResolveDriverCbid(-1, 500));
        Assert.AreEqual(500, SpawnPoint.ResolveDriverCbid(0, 500));
        Assert.AreEqual(0, SpawnPoint.ResolveDriverCbid(0, 0));
    }

    [TestMethod]
    public void Spawn_TemplateMissing_ReturnsFalse()
    {
        var template = new SpawnPointTemplate();
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            IsTemplate = true,
            SpawnType = 1_999_999_999,
        });

        var spawnPoint = new SpawnPoint(template);

        Assert.IsFalse(spawnPoint.Spawn());
    }

    [TestMethod]
    public void TryGetMapPath_ResolvesFromMapDataTemplates()
    {
        var map = CreateTestMap(9101);
        var mapPath = new MapPathTemplate { COID = 777, ReverseDirection = true };
        map.MapData.Templates.Add(777, mapPath);

        var found = map.TryGetMapPath(777, out var result);

        Assert.IsTrue(found);
        Assert.AreSame(mapPath, result);

        var missing = map.TryGetMapPath(888, out var missingResult);

        Assert.IsFalse(missing);
        Assert.IsNull(missingResult);
    }

    [TestMethod]
    public void EnterMap_RegistersNpcAiEntity_LeaveMapUnregisters()
    {
        var map = CreateTestMap(9102);

        var creature = new Creature();
        creature.SetCoid(5001, false);
        creature.NpcAi = new Npc.NpcAiState();

        creature.SetMap(map);

        Assert.IsTrue(map.NpcAiEntities.Contains(creature));

        creature.SetMap(null);

        Assert.IsFalse(map.NpcAiEntities.Contains(creature));
    }

    [TestMethod]
    public void Spawn_RawVehicleWithDriverAi_RegistersVehicleInNpcAiEntities()
    {
        var map = CreateTestMap(9103);

        const int vehicleCbid = 610_001;
        const int driverCbid = 610_002;
        const int aiBehaviorId = 610_003;

        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultDriverCbid: driverCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, aiBehaviorId: aiBehaviorId, baseLevel: 5);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = aiBehaviorId }
        });

        var template = new SpawnPointTemplate { COID = 14_501 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = vehicleCbid,
            IsTemplate = false,
        });

        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(14_501, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.NpcAiEntities.OfType<Vehicle>().SingleOrDefault();
        Assert.IsNotNull(vehicle, "Raw-CBID vehicle spawn with a driver AI must register in NpcAiEntities");
        Assert.IsNotNull(vehicle.NpcAi);
        Assert.IsTrue(vehicle.ObjectId.Global, "Spawned vehicles must not occupy the client-local map-object namespace");
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(vehicle.ObjectId));
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(vehicle.ObjectId, mapHighestCoid: 0));
    }

    [TestMethod]
    public void Spawn_RawVehicleWithDefaultWheelset_EquipsWheelsetForCreateVehicle()
    {
        const int vehicleCbid = 610_010;
        const int wheelsetCbid = 610_011;
        var map = CreateTestMap(9106);
        AssetManagerTestHelper.RegisterCloneBase(wheelsetCbid, CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: wheelsetCbid);

        var template = new SpawnPointTemplate { COID = 14_510 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = vehicleCbid, IsTemplate = false });
        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.IsNotNull(vehicle.WheelSet, "CreateVehicle must contain the clonebase default wheelset before ghosting can render this NPC.");
        Assert.AreEqual(wheelsetCbid, vehicle.WheelSet.CBID);
    }

    /// <summary>
    /// Client EquipFromCreate resolves nested wheelset via TFID (packet+0x4E8) before GiveItemByCbid.
    /// Low Global=false equipment COIDs collide with client-local map objects, skip GiveItem, leave
    /// vehicle+0x258 null, and AV at 0x004F5566 when owner/ghost activates Havok.
    /// </summary>
    [TestMethod]
    public void Spawn_DefaultWheelset_UsesMapNpcGlobalIdentity_NotUnsafeLocal()
    {
        const int vehicleCbid = 610_020;
        const int wheelsetCbid = 610_021;
        var map = CreateTestMap(9107);
        map.LocalCoidCounter = 50; // low counter would produce unsafe locals under the old policy
        AssetManagerTestHelper.RegisterCloneBase(wheelsetCbid, CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: wheelsetCbid);

        var template = new SpawnPointTemplate { COID = 14_520 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = vehicleCbid, IsTemplate = false });
        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.IsNotNull(vehicle.WheelSet);
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(vehicle.WheelSet.ObjectId),
            "Nested CreateWheelSet TFID must use MapNpcIdentity (global + high range) so client GiveItemByCbid runs.");
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(vehicle.WheelSet.ObjectId, mapHighestCoid: 0));
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(vehicle.ObjectId));
        Assert.AreNotEqual(vehicle.ObjectId.Coid, vehicle.WheelSet.ObjectId.Coid,
            "Wheelset must not reuse the vehicle TFID.");
    }

    [TestMethod]
    public void Spawn_TemplateVehicleWithDriverAi_RegistersVehicleInNpcAiEntities()
    {
        var map = CreateTestMap(9104);

        const int vehicleCbid = 620_001;
        const int driverCbid = 620_002;
        const int aiBehaviorId = 620_003;
        const int templateId = 620_004;

        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, aiBehaviorId: aiBehaviorId, baseLevel: 5);
        AssetManager.Instance.SetTestCreatureAiProfiles(new[]
        {
            new CreatureAiProfile { AiId = aiBehaviorId }
        });
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = templateId,
                VehicleCbid = vehicleCbid,
                DriverCbid = driverCbid,
            }
        });

        var template = new SpawnPointTemplate { COID = 14_502 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = templateId,
            IsTemplate = true,
        });

        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(14_502, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.NpcAiEntities.OfType<Vehicle>().SingleOrDefault();
        Assert.IsNotNull(vehicle, "Template vehicle spawn with a driver AI must register in NpcAiEntities");
        Assert.IsNotNull(vehicle.NpcAi);
        Assert.IsTrue(vehicle.ObjectId.Global, "Template-spawned vehicles must use the map-NPC identity namespace");
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(vehicle.ObjectId));
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(vehicle.ObjectId, mapHighestCoid: 0));
    }

    [TestMethod]
    public void Spawn_RawVehicleAtArkBayCollisionCoid_UsesGlobalHighRangeIdentity()
    {
        const long arkBayHighestCoid = 18_097;
        const long crashingLocalCoid = 18_228;
        const int vehicleCbid = 630_001;

        var map = CreateTestMap(707);
        map.LocalCoidCounter = crashingLocalCoid;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var template = new SpawnPointTemplate { COID = 16_770 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = vehicleCbid,
            IsTemplate = false,
        });

        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.AreNotEqual(crashingLocalCoid, vehicle.ObjectId.Coid);
        Assert.AreEqual(MapNpcIdentity.CoidBase + crashingLocalCoid, vehicle.ObjectId.Coid);
        Assert.IsTrue(vehicle.ObjectId.Global);
        Assert.IsTrue(vehicle.ObjectId.Coid >= MapNpcIdentity.CoidBase);
        Assert.IsFalse(MapNpcIdentity.IsUnsafeLocalSpawnCoid(vehicle.ObjectId, arkBayHighestCoid));
    }

    [TestMethod]
    public void Spawn_TwoRawVehicles_AllocatesDistinctSequentialMapNpcIdentities()
    {
        const int vehicleCbid = 640_001;
        var map = CreateTestMap(9105);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var template = new SpawnPointTemplate { COID = 14_503 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = vehicleCbid,
            IsTemplate = false,
        });

        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());
        Assert.IsTrue(spawnPoint.Spawn());

        var vehicles = map.Objects.Values.OfType<Vehicle>().OrderBy(v => v.ObjectId.Coid).ToList();
        Assert.AreEqual(2, vehicles.Count);
        Assert.IsTrue(vehicles.All(v => MapNpcIdentity.IsMapNpcIdentity(v.ObjectId)));
        Assert.AreEqual(vehicles[0].ObjectId.Coid + 1, vehicles[1].ObjectId.Coid);
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_spawn_template_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };

        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}

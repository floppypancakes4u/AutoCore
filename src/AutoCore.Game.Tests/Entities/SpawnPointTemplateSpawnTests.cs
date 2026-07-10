using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using System.Linq;
using AutoCore.Database.World.Models;
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

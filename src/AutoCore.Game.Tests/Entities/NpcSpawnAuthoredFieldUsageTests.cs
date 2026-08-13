using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Utils.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Characterization of authored spawn-control fields versus what
/// <see cref="SpawnPoint.Spawn"/> / <see cref="SpawnPointTemplate.GetSpawn"/> /
/// <see cref="SectorMap.InitializeLocalObjects"/> actually apply.
/// Client <c>FUN_00566490</c> refills every eligible slot independently
/// (sum of per-slot Lower..Upper) and rolls SpawnChance/RespawnTime on a heartbeat.
/// The server materializes one child of one slot at map load (or Create/Activate)
/// and never re-enters that loop.
/// </summary>
[TestClass]
public class NpcSpawnAuthoredFieldUsageTests
{
    private const int CreatureCbid = 660_001;
    private const int ContinentId = 9201;

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
    public void Spawn_TwoFilledSlots_CreatesOneChildOfOneType()
    {
        // Client FUN_00566490 refills every eligible slot (sum of per-slot Lower..Upper).
        // Server GetSpawn picks one filled slot and Spawn creates one body.
        var map = CreateTestMap(ContinentId + 7);
        const int otherCbid = CreatureCbid + 1;
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);
        AssetManagerTestHelper.RegisterCreatureCloneBase(otherCbid, isNpc: 0);

        var template = new SpawnPointTemplate { COID = 20_010 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 2,
            UpperNumberOfSpawns = 3,
        });
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = otherCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 2,
        });

        var spawnPoint = PlaceSpawn(map, template);
        Assert.IsTrue(spawnPoint.Spawn());

        var children = map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList();
        Assert.AreEqual(1, children.Count,
            "server Spawn() materializes one child; client would keep 2–3 of type A plus 1–2 of type B");
        Assert.IsTrue(
            children[0].CBID == CreatureCbid || children[0].CBID == otherCbid,
            "the single child comes from one of the filled slots");
    }

    [TestMethod]
    public void Spawn_IgnoresLowerUpper_CreatesExactlyOneChild()
    {
        var map = CreateTestMap(ContinentId);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);

        var template = new SpawnPointTemplate { COID = 20_001 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 4,
            UpperNumberOfSpawns = 8,
        });

        var spawnPoint = PlaceSpawn(map, template);
        Assert.IsTrue(spawnPoint.Spawn());

        var children = map.Objects.Values.OfType<Creature>().Count(c => c is not Character);
        Assert.AreEqual(1, children,
            "Spawn() must create one child per call even when the fam slot authors Lower=4 Upper=8");
        Assert.IsTrue(spawnPoint.HasLiveSpawn());
    }

    [TestMethod]
    public void GetSpawn_IgnoresSpawnChance_StillReturnsFilledSlot()
    {
        var template = new SpawnPointTemplate { SpawnChance = 0 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 2,
            UpperNumberOfSpawns = 5,
        });
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = -1 });

        for (var i = 0; i < 16; i++)
        {
            var picked = template.GetSpawn();
            Assert.IsNotNull(picked, "GetSpawn must not consult SpawnChance (client rolls it in FUN_00566490)");
            Assert.AreEqual(CreatureCbid, picked!.SpawnType);
        }
    }

    [TestMethod]
    public void Spawn_IgnoresRadiusAndRandomOffset_PlacesAtSpawnPoint()
    {
        var map = CreateTestMap(ContinentId + 1);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);

        var template = new SpawnPointTemplate
        {
            COID = 20_002,
            Radius = 80f,
            RandomlyOffsetSpawnPosition = true,
        };
        template.Location = new Vector4(12f, 3f, 44f, 0f);
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
        });

        var spawnPoint = PlaceSpawn(map, template);
        spawnPoint.Position = new Vector3(12f, 3f, 44f);
        Assert.IsTrue(spawnPoint.Spawn());

        var creature = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreEqual(12f, creature.Position.X, 0.001f);
        Assert.AreEqual(44f, creature.Position.Z, 0.001f);
    }

    [TestMethod]
    public void InitializeLocalObjects_ActiveSpawn_StillOneChildDespiteUpperCount()
    {
        var map = CreateTestMap(ContinentId + 2);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 1);

        var template = new SpawnPointTemplate
        {
            COID = 20_003,
            IsActive = true,
            OriginalIsActive = true,
            RespawnTime = 45f,
            UseGenerator = true,
            ActivationRange = 120f,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 3,
            UpperNumberOfSpawns = 3,
        });
        map.MapData.Templates[template.COID] = template;

        map.InitializeLocalObjectsForTests();

        Assert.IsTrue(SectorMap.ShouldSpawnChildrenAtMapLoad(template));
        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsNotNull(spawn);
        Assert.IsTrue(spawn!.HasLiveSpawn());
        Assert.AreEqual(1, map.Objects.Values.OfType<Creature>().Count(c => c is not Character),
            "Map load calls Spawn() once; it does not loop to UpperNumberOfSpawns");
    }

    [TestMethod]
    public void InitializeLocalObjects_InactiveSpawn_PlacesMarkerOnly_EvenWhenUseGenerator()
    {
        var map = CreateTestMap(ContinentId + 3);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);

        var template = new SpawnPointTemplate
        {
            COID = 20_004,
            IsActive = false,
            OriginalIsActive = false,
            UseGenerator = true,
            RespawnTime = 15f,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 2,
            UpperNumberOfSpawns = 2,
        });
        map.MapData.Templates[template.COID] = template;

        map.InitializeLocalObjectsForTests();

        Assert.IsFalse(SectorMap.ShouldSpawnChildrenAtMapLoad(template));
        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsNotNull(spawn, "inactive marker is still placed so Create/Activate can find it");
        Assert.IsFalse(spawn!.HasLiveSpawn());
        Assert.AreEqual(0, map.Objects.Values.OfType<Creature>().Count(c => c is not Character));
    }

    [TestMethod]
    public void Spawn_SpawnChanceZero_StillCreatesChild()
    {
        var map = CreateTestMap(ContinentId + 4);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);

        var template = new SpawnPointTemplate { COID = 20_005, SpawnChance = 0 };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
        });

        var spawnPoint = PlaceSpawn(map, template);
        Assert.IsTrue(spawnPoint.Spawn(), "Spawn() does not roll SpawnChance; chance 0 still materializes");
        Assert.AreEqual(1, map.Objects.Values.OfType<Creature>().Count(c => c is not Character));
    }

    [TestMethod]
    public void ReadThenSpawn_ParsesLowerUpperChanceButCreatesOneChild()
    {
        // Same wire layout as EntityTemplateReadTests / live .fam v>=32.
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0L);
            writer.Write(0L);
            writer.Write(0L);
            writer.Write(10f); writer.Write(0f); writer.Write(20f); writer.Write(0f);
            writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f);
            writer.Write(80f); // Radius
            writer.Write(195000f); // RespawnTime
            writer.Write(200f); // ActivationRange
            writer.Write(true); // UseGenerator
            writer.Write(true); // HasChampion
            writer.Write((byte)50); // ChampionChance
            writer.Write((byte)0); // SpawnChance — client would skip this pulse
            writer.Write(true); // IsActive
            writer.Write(true); // RandomlyOffsetSpawnPosition
            for (var i = 0; i < 12; i++)
            {
                writer.Write((byte)4); // Lower
                writer.Write((byte)8); // Upper
                writer.Write((short)0);
                writer.Write(i == 0 ? CreatureCbid : -1);
                writer.Write((byte)2); // LevelOffset
                writer.Write(false); // IsTemplate
                writer.Write((short)0);
            }
            writer.Write(-1); // Loot
            writer.Write(0f);
            writer.Write(7701L); // MapPathCoid
            writer.Write(5f);
            writer.Write(false);
            writer.Write(-1);
            writer.Write(-1f);
            writer.WriteLengthedString("p3_s1_a_pikecamp-icebreaker");
        }

        ms.Position = 0;
        var template = new SpawnPointTemplate { COID = 20_006 };
        template.Read(new BinaryReader(ms), mapVersion: 32);

        Assert.AreEqual((byte)4, template.Spawns[0].LowerNumberOfSpawns);
        Assert.AreEqual((byte)8, template.Spawns[0].UpperNumberOfSpawns);
        Assert.AreEqual((byte)0, template.SpawnChance);
        Assert.AreEqual(195000f, template.RespawnTime);
        Assert.AreEqual(7701L, template.MapPathCoid);
        Assert.AreEqual("p3_s1_a_pikecamp-icebreaker", template.MaybeChampionName);

        var map = CreateTestMap(ContinentId + 5);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0, baseLevel: 6);
        var spawnPoint = (SpawnPoint)template.Create();
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());
        Assert.AreEqual(1, map.Objects.Values.OfType<Creature>().Count(c => c is not Character),
            "shipped Read populated Lower=4 Upper=8; shipped Spawn still creates one body");
        var creature = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreEqual((byte)8, creature.Level, "LevelOffset from the parsed slot is applied");
        Assert.AreEqual(10f, creature.Position.X, 0.001f, "Radius/offset parsed but position stays on the marker");
        Assert.AreEqual(20f, creature.Position.Z, 0.001f);
    }

    [TestMethod]
    public void Activate_InactiveSpawn_MaterializesOneChild_NotUpperCount()
    {
        var map = CreateTestMap(ContinentId + 6);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);

        var template = new SpawnPointTemplate
        {
            COID = 20_007,
            IsActive = false,
            OriginalIsActive = false,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = CreatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 3,
            UpperNumberOfSpawns = 3,
        });
        map.MapData.Templates[template.COID] = template;

        map.InitializeLocalObjectsForTests();
        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsNotNull(spawn);
        Assert.IsFalse(spawn!.HasLiveSpawn());

        var actTpl = new ReactionTemplate
        {
            COID = 20_008,
            ReactionType = ReactionType.Activate,
        };
        actTpl.Objects.Add(template.COID);
        var activate = new Reaction(actTpl);
        activate.SetCoid(20_008, false);
        activate.SetMap(map);

        var player = new Character();
        player.SetCoid(9001, true);
        player.SetMap(map);
        Assert.IsTrue(activate.TriggerIfPossible(player));

        Assert.IsTrue(spawn.HasLiveSpawn());
        Assert.AreEqual(1, map.Objects.Values.OfType<Creature>().Count(c => c is not Character),
            "Activate calls Spawn() once; it does not honor UpperNumberOfSpawns=3");
        Assert.IsFalse(template.OriginalIsActive, "Activate must not mutate fam OriginalIsActive");
    }

    [TestMethod]
    public void ApplySpawnPath_IsTravelRoute_NotAPopulationCap()
    {
        var creature = new Creature();
        var template = new SpawnPointTemplate
        {
            MapPathCoid = 7701,
            InitialPatrolDistance = 22f,
        };
        var path = new MapPathTemplate { ReverseDirection = true };

        SpawnPoint.ApplySpawnPath(creature, template, path);

        Assert.AreEqual(7701, creature.CoidCurrentPath);
        Assert.AreEqual(22f, creature.PatrolDistance);
        Assert.IsTrue(creature.PathReversing);
    }

    private static SpawnPoint PlaceSpawn(SectorMap map, SpawnPointTemplate template)
    {
        var spawnPoint = new SpawnPoint(template);
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);
        return spawnPoint;
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_spawn_fields_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };

        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}

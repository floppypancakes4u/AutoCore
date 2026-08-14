using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Pass 18 — retail SpawnPoint radius / RandomlyOffsetSpawnPosition scatter.
/// Client <c>FUN_004e9720</c> (CreateCreature @ 0x00564F60, CreateTemplateVehicle @ 0x00564290)
/// offsets X/Z independently inside an axis-aligned square of half-side Radius.
/// </summary>
[TestClass]
public class SpawnScatterTests
{
    private const int CreatureCbid = 780_001;
    private const int OtherCreatureCbid = 780_002;
    private const int VehicleCbid = 780_010;
    private const int DriverCbid = 780_011;
    private const int WheelsetCbid = 780_012;
    private const int TemplateId = 780_020;

    [TestInitialize]
    public void TestInitialize()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SpawnPoint.TestScatterRandom = null;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SpawnPoint.TestScatterRandom = null;
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void ScatterHorizontal_UInt16Extremes_ArePlusMinusRadiusOnXZOnly()
    {
        var origin = new Vector3(10f, 3f, 20f);
        var min = SpawnPoint.ScatterHorizontal(origin, 8f, 0, 0);
        Assert.AreEqual(2f, min.X, 1e-5f);
        Assert.AreEqual(12f, min.Z, 1e-5f);
        Assert.AreEqual(3f, min.Y, 1e-5f);

        var max = SpawnPoint.ScatterHorizontal(origin, 8f, 65535, 65535);
        Assert.AreEqual(18f, max.X, 1e-5f);
        Assert.AreEqual(28f, max.Z, 1e-5f);
        Assert.AreEqual(3f, max.Y, 1e-5f);
    }

    /// <summary>
    /// Characterization of the Pass-17 leftover: multi-child camps share one XYZ because
    /// Radius / RandomlyOffsetSpawnPosition were parsed and then ignored.
    /// </summary>
    [TestMethod]
    public void SpawnScatter_CurrentBug_MultipleChildrenDoNotShareExactPosition()
    {
        var (spawn, children) = SpawnCombatCamp(9801, 21_301, lower: 4, radius: 12f, offset: true);
        Assert.AreEqual(4, children.Count);
        Assert.IsTrue(CountUniqueXZ(children) > 1,
            "retail FUN_004e9720 must give multi-child camps independent XZ; " +
            $"all {children.Count} children currently share X={children[0].Position.X} Z={children[0].Position.Z}");
        Assert.AreSame(spawn.Map, children[0].Map);
    }

    [TestMethod]
    public void SpawnScatter_Disabled_UsesAuthoredPosition()
    {
        var (_, children) = SpawnCombatCamp(9802, 21_302, lower: 3, radius: 12f, offset: false);
        foreach (var child in children)
        {
            Assert.AreEqual(100f, child.Position.X, 0.001f);
            Assert.AreEqual(-50f, child.Position.Z, 0.001f);
        }
    }

    [TestMethod]
    public void SpawnScatter_ZeroRadius_UsesAuthoredHorizontalPosition()
    {
        var (_, children) = SpawnCombatCamp(9803, 21_303, lower: 3, radius: 0f, offset: true);
        foreach (var child in children)
        {
            Assert.AreEqual(100f, child.Position.X, 0.001f);
            Assert.AreEqual(-50f, child.Position.Z, 0.001f);
        }
    }

    [TestMethod]
    public void SpawnScatter_AllChildrenRemainWithinAuthoredRadius()
    {
        const float radius = 12f;
        var (_, children) = SpawnCombatCamp(9804, 21_304, lower: 8, radius, offset: true);
        Assert.AreEqual(8, children.Count);
        foreach (var child in children)
            AssertInsideSquare(new Vector3(100f, 4f, -50f), radius, child.Position);
    }

    [TestMethod]
    public void SpawnScatter_MultiSlotChildrenUseIndependentOffsets()
    {
        var map = CreateTestMap(9805);
        RegisterCombatCreature(CreatureCbid);
        RegisterCombatCreature(OtherCreatureCbid);
        var template = new SpawnPointTemplate
        {
            COID = 21_305,
            Radius = 15f,
            RandomlyOffsetSpawnPosition = true,
        };
        template.Spawns.Add(FilledSlot(CreatureCbid, 4, 4));
        template.Spawns.Add(FilledSlot(OtherCreatureCbid, 3, 3));
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);

        Assert.IsTrue(spawn.Spawn());
        var children = MappedCreatures(map);
        Assert.AreEqual(7, children.Count);
        Assert.IsTrue(CountUniqueXZ(children) > 1);
        Assert.IsTrue(children.Count(c => c.CBID == CreatureCbid) == 4);
        Assert.IsTrue(children.Count(c => c.CBID == OtherCreatureCbid) == 3);
        foreach (var child in children)
            AssertInsideSquare(spawn.Position, 15f, child.Position);
    }

    [TestMethod]
    public void SpawnScatter_RespawnGetsRetailCompatiblePosition()
    {
        SpawnPoint.TestScatterRandom = new Random(17);
        var map = CreateTestMap(9806);
        RegisterCombatCreature(CreatureCbid);
        var template = ActiveScatterTemplate(21_306, CreatureCbid, 2, 2, 10f, true);
        template.RespawnTime = 1_000f;
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);
        Assert.IsTrue(spawn.Spawn());

        var firstGen = MappedCreatures(map).Select(c => (c.Position.X, c.Position.Z)).ToList();
        foreach (var child in MappedCreatures(map).ToList())
        {
            child.SetMap(null);
            spawn.NotifySpawnedChildDied(child, null);
        }

        map.TickSpawnRespawns(spawn.RespawnDueAtMs ?? 0);
        var secondGen = MappedCreatures(map);
        Assert.AreEqual(2, secondGen.Count, "respawn must refill via the same CreateCreature scatter path");
        foreach (var child in secondGen)
            AssertInsideSquare(spawn.Position, 10f, child.Position);

        var secondXZ = secondGen.Select(c => (c.Position.X, c.Position.Z)).ToList();
        Assert.IsFalse(firstGen.OrderBy(p => p.X).SequenceEqual(secondXZ.OrderBy(p => p.X)),
            "FUN_00566490 calls CreateCreature again; FUN_004e9720 rerolls, it does not reuse the corpse pose");
    }

    [TestMethod]
    public void SpawnScatter_ActivationUsesScatter()
    {
        var map = CreateTestMap(9807);
        RegisterCombatCreature(CreatureCbid);
        var template = new SpawnPointTemplate
        {
            COID = 21_307,
            IsActive = false,
            OriginalIsActive = false,
            Radius = 12f,
            RandomlyOffsetSpawnPosition = true,
        };
        template.Spawns.Add(FilledSlot(CreatureCbid, 3, 3));
        map.MapData.Templates[template.COID] = template;
        map.InitializeLocalObjectsForTests();

        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsFalse(spawn!.HasLiveSpawn());
        spawn.Position = new Vector3(100f, 4f, -50f);

        var actTpl = new ReactionTemplate { COID = 21_317, ReactionType = ReactionType.Activate };
        actTpl.Objects.Add(template.COID);
        var activate = new Reaction(actTpl);
        activate.SetCoid(21_317, false);
        activate.SetMap(map);
        var player = new Character();
        player.SetCoid(9001, true);
        player.SetMap(map);

        Assert.IsTrue(activate.TriggerIfPossible(player));
        var children = MappedCreatures(map);
        Assert.AreEqual(3, children.Count);
        Assert.IsTrue(CountUniqueXZ(children) > 1, "Activate must run the same FUN_004e9720 scatter as map-load");
        foreach (var child in children)
            AssertInsideSquare(spawn.Position, 12f, child.Position);
    }

    [TestMethod]
    public void SpawnScatter_WalkingCreature_RemainsMappedAndGhosted()
    {
        var (_, children) = SpawnCombatCamp(9808, 21_308, lower: 2, radius: 8f, offset: true);
        foreach (var child in children)
        {
            Assert.IsNotNull(child.Map);
            Assert.IsNotNull(child.Ghost);
            Assert.IsInstanceOfType(child.Ghost, typeof(GhostCreature));
        }
    }

    [TestMethod]
    public void SpawnScatter_NpcVehicle_RemainsMappedAndDriverBound()
    {
        var map = CreateTestMap(9809);
        RegisterNpcVehicle();
        var template = new SpawnPointTemplate
        {
            COID = 21_309,
            Radius = 10f,
            RandomlyOffsetSpawnPosition = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 2,
            UpperNumberOfSpawns = 2,
        });
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);
        spawn.Rotation = new Quaternion(0f, 0.1f, 0f, 0.995f);

        Assert.IsTrue(spawn.Spawn());
        var vehicles = map.Objects.Values.OfType<Vehicle>().ToList();
        Assert.AreEqual(2, vehicles.Count);
        Assert.IsTrue(vehicles.Select(v => (v.Position.X, v.Position.Z)).Distinct().Count() > 1);
        foreach (var vehicle in vehicles)
        {
            AssertInsideSquare(spawn.Position, 10f, vehicle.Position);
            Assert.AreSame(map, vehicle.Map);
            Assert.IsInstanceOfType(vehicle.Ghost, typeof(GhostVehicle));
            Assert.IsNotNull(vehicle.Owner);
            Assert.AreEqual(DriverCbid, vehicle.Owner.CBID);
            Assert.IsNull(vehicle.Owner.Map);
            Assert.IsNull(vehicle.Owner.Ghost);
            Assert.AreEqual(vehicle.Position.X, vehicle.Owner.Position.X, 0.001f);
            Assert.AreEqual(vehicle.Position.Z, vehicle.Owner.Position.Z, 0.001f);
            Assert.AreEqual(spawn.Rotation.Y, vehicle.Rotation.Y, 0.0001f);
        }
    }

    [TestMethod]
    public void SpawnScatter_DoesNotChangeSpawnRotationUnlessRetailDoes()
    {
        var map = CreateTestMap(9810);
        RegisterCombatCreature(CreatureCbid);
        var template = ActiveScatterTemplate(21_310, CreatureCbid, 3, 3, 12f, true);
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);
        spawn.Rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.927f);

        Assert.IsTrue(spawn.Spawn());
        foreach (var child in MappedCreatures(map))
        {
            Assert.AreEqual(spawn.Rotation.X, child.Rotation.X, 0.0001f);
            Assert.AreEqual(spawn.Rotation.Y, child.Rotation.Y, 0.0001f);
            Assert.AreEqual(spawn.Rotation.Z, child.Rotation.Z, 0.0001f);
            Assert.AreEqual(spawn.Rotation.W, child.Rotation.W, 0.0001f);
        }
    }

    [TestMethod]
    public void SpawnScatter_PathAssociatedSpawn_RemainsPathCompatible()
    {
        var map = CreateTestMap(9811);
        RegisterCombatCreature(CreatureCbid);
        var template = ActiveScatterTemplate(21_311, CreatureCbid, 2, 2, 9f, true);
        template.MapPathCoid = 12635;
        template.InitialPatrolDistance = 4.5f;
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);

        Assert.IsTrue(spawn.Spawn());
        var children = MappedCreatures(map);
        Assert.AreEqual(2, children.Count);
        Assert.IsTrue(CountUniqueXZ(children) > 1);
        foreach (var child in children)
        {
            Assert.AreEqual(12635, child.CoidCurrentPath);
            Assert.AreEqual(4.5f, child.PatrolDistance, 0.001f);
            AssertInsideSquare(spawn.Position, 9f, child.Position);
        }
    }

    [TestMethod]
    public void SpawnScatter_TerrainHeightRemainsValid()
    {
        var (_, children) = SpawnCombatCamp(9812, 21_312, lower: 4, radius: 12f, offset: true);
        foreach (var child in children)
        {
            Assert.AreEqual(4f, child.Position.Y, 0.001f,
                "no heightfield: Y stays authored (FUN_004e9720 does not randomize Y; SnapToTerrain is a no-op)");
            Assert.IsTrue(float.IsFinite(child.Position.Y));
        }
    }

    [TestMethod]
    public void SpawnScatter_InteractiveNpc_DoesNotScatter()
    {
        var map = CreateTestMap(9813);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 1);
        AssetManager.Instance.GetCloneBase<CloneBaseCreature>(CreatureCbid)!.CreatureSpecific.Speed = 4f;
        var template = ActiveScatterTemplate(21_313, CreatureCbid, 2, 2, 12f, true);
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);

        Assert.IsTrue(spawn.Spawn());
        foreach (var child in MappedCreatures(map))
        {
            Assert.AreEqual(100f, child.Position.X, 0.001f,
                "CreateCreature skips FUN_004e9720 when IsNPC==1");
            Assert.AreEqual(-50f, child.Position.Z, 0.001f);
        }
    }

    [TestMethod]
    public void SpawnScatter_SpeedZeroCombat_StillScatters()
    {
        // Live FAM combat CBIDs (Scrap 13330 → 13564/2753) author Speed=0 and still walk.
        var map = CreateTestMap(9814);
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, isNpc: 0);
        var template = ActiveScatterTemplate(21_314, CreatureCbid, 3, 3, 12f, true);
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);

        Assert.IsTrue(spawn.Spawn());
        var children = MappedCreatures(map);
        Assert.AreEqual(3, children.Count);
        Assert.IsTrue(CountUniqueXZ(children) > 1);
        foreach (var child in children)
            AssertInsideSquare(spawn.Position, 12f, child.Position);
    }

    private static (SpawnPoint Spawn, List<Creature> Children) SpawnCombatCamp(
        int continentId, int coid, byte lower, float radius, bool offset)
    {
        var map = CreateTestMap(continentId);
        RegisterCombatCreature(CreatureCbid);
        var template = ActiveScatterTemplate(coid, CreatureCbid, lower, lower, radius, offset);
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(100f, 4f, -50f);
        Assert.IsTrue(spawn.Spawn());
        return (spawn, MappedCreatures(map));
    }

    private static void AssertInsideSquare(Vector3 origin, float radius, Vector3 actual)
    {
        Assert.IsTrue(MathF.Abs(actual.X - origin.X) <= radius + 1e-4f,
            $"X {actual.X} is outside authored square ±{radius} of {origin.X}");
        Assert.IsTrue(MathF.Abs(actual.Z - origin.Z) <= radius + 1e-4f,
            $"Z {actual.Z} is outside authored square ±{radius} of {origin.Z}");
    }

    private static int CountUniqueXZ(IEnumerable<Creature> children)
        => children.Select(c => (c.Position.X, c.Position.Z)).Distinct().Count();

    private static List<Creature> MappedCreatures(SectorMap map)
        => map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList();

    private static void RegisterCombatCreature(int cbid)
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, isNpc: 0);
        AssetManager.Instance.GetCloneBase<CloneBaseCreature>(cbid)!.CreatureSpecific.Speed = 4f;
    }

    private static void RegisterNpcVehicle()
    {
        AssetManagerTestHelper.RegisterCloneBase(WheelsetCbid, CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(VehicleCbid, defaultDriverCbid: DriverCbid, defaultWheelsetCbid: WheelsetCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(DriverCbid, isNpc: 0);
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = TemplateId,
                VehicleCbid = VehicleCbid,
                DriverCbid = DriverCbid,
            }
        });
    }

    private static SpawnPointTemplate ActiveScatterTemplate(
        int coid, int cbid, byte lower, byte upper, float radius, bool offset)
    {
        var template = new SpawnPointTemplate
        {
            COID = coid,
            OriginalIsActive = true,
            IsActive = true,
            Radius = radius,
            RandomlyOffsetSpawnPosition = offset,
        };
        template.Spawns.Add(FilledSlot(cbid, lower, upper));
        return template;
    }

    private static SpawnPointTemplate.SpawnList FilledSlot(int spawnType, byte lower, byte upper)
        => new()
        {
            SpawnType = spawnType,
            IsTemplate = false,
            LowerNumberOfSpawns = lower,
            UpperNumberOfSpawns = upper,
        };

    private static SpawnPoint PlaceSpawn(SectorMap map, SpawnPointTemplate template)
    {
        var spawnPoint = (SpawnPoint)template.Create();
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);
        return spawnPoint;
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_spawn_scatter_{continentId}",
            DisplayName = "spawn-scatter",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}

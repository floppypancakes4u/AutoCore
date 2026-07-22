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

    /// <summary>
    /// Client CVOGSpawnPoint_CreateCreature: when FactionDirty, FUN_00512460 writes the spawnpoint
    /// root faction onto the child (NPC.md §15.3). Clonebase faction alone is not enough.
    /// </summary>
    [TestMethod]
    public void Spawn_Creature_FactionDirty_OverridesClonebaseFaction()
    {
        const int creatureCbid = 650_001;
        var map = CreateTestMap(9110);
        // Clonebase is hostile Ambient; map author marks the spawn Neutral.
        AssetManagerTestHelper.RegisterCreatureCloneBase(creatureCbid, faction: 21, aiBehaviorId: 0);

        var template = new SpawnPointTemplate
        {
            COID = 14_600,
            Faction = -100,
            FactionDirty = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = creatureCbid,
            IsTemplate = false,
        });

        var spawnPoint = new SpawnPoint(template) { Faction = -100 };
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var creature = map.Objects.Values.OfType<Creature>().Single(c => c.CBID == creatureCbid);
        Assert.AreEqual(-100, creature.Faction, "FactionDirty must copy spawnpoint faction onto the creature");
        Assert.AreEqual(-100, creature.GetIDFaction(), "GetIDFaction must see the spawn override");
    }

    [TestMethod]
    public void Spawn_Creature_FactionDirtyFalse_KeepsClonebaseFaction()
    {
        const int creatureCbid = 650_002;
        var map = CreateTestMap(9111);
        AssetManagerTestHelper.RegisterCreatureCloneBase(creatureCbid, faction: 21, aiBehaviorId: 0);

        var template = new SpawnPointTemplate
        {
            COID = 14_601,
            Faction = -100,
            FactionDirty = false,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = creatureCbid,
            IsTemplate = false,
        });

        var spawnPoint = new SpawnPoint(template) { Faction = -100 };
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var creature = map.Objects.Values.OfType<Creature>().Single(c => c.CBID == creatureCbid);
        Assert.AreEqual(21, creature.Faction, "without FactionDirty, clonebase faction must remain");
    }

    /// <summary>
    /// Template vehicle: FactionDirty applies to chassis and driver so GetIDFaction (owner chain)
    /// returns the spawn override — client writes both when +0x1a8 is set.
    /// </summary>
    [TestMethod]
    public void Spawn_TemplateVehicle_FactionDirty_OverridesDriverAndVehicleFaction()
    {
        const int vehicleCbid = 650_010;
        const int driverCbid = 650_011;
        const int templateId = 650_012;
        var map = CreateTestMap(9112);

        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, faction: 10);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, faction: 10, aiBehaviorId: 0);
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = templateId,
                VehicleCbid = vehicleCbid,
                DriverCbid = driverCbid,
            }
        });

        var template = new SpawnPointTemplate
        {
            COID = 14_602,
            Faction = -100,
            FactionDirty = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = templateId,
            IsTemplate = true,
        });

        var spawnPoint = new SpawnPoint(template) { Faction = -100 };
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.AreEqual(-100, vehicle.Faction, "FactionDirty must override vehicle clonebase faction");
        Assert.IsNotNull(vehicle.Owner, "template spawn must attach a driver");
        Assert.AreEqual(-100, vehicle.Owner.Faction, "FactionDirty must override driver clonebase faction");
        Assert.AreEqual(-100, vehicle.GetIDFaction(), "aggro chain must resolve to spawn Neutral override");
    }

    /// <summary>
    /// Fam load stores OriginalFaction but historically left ObjectTemplate.Faction at 0.
    /// FactionDirty must promote OriginalFaction onto Faction (Gunny combat spawn 14138 → 22).
    /// </summary>
    [TestMethod]
    public void ApplyFactionDirtyAuthoredFaction_CopiesOriginalFactionOntoFaction()
    {
        var template = new SpawnPointTemplate
        {
            FactionDirty = true,
            OriginalFaction = 22,
            Faction = 0,
        };

        template.ApplyFactionDirtyAuthoredFaction();

        Assert.AreEqual(22, template.Faction,
            "FactionDirty fam rows must expose OriginalFaction as spawnpoint Faction");
    }

    [TestMethod]
    public void ApplyFactionDirtyAuthoredFaction_WhenNotDirty_LeavesFactionAlone()
    {
        var template = new SpawnPointTemplate
        {
            FactionDirty = false,
            OriginalFaction = 22,
            Faction = 0,
        };

        template.ApplyFactionDirtyAuthoredFaction();

        Assert.AreEqual(0, template.Faction);
    }

    /// <summary>
    /// Mission combat vehicles: FactionDirty + OriginalFaction only (Faction still 0 on spawnpoint)
    /// must not leave GetIDFaction at Human 0 — that makes weapons skip the target.
    /// </summary>
    [TestMethod]
    public void Spawn_TemplateVehicle_FactionDirty_OriginalFactionOnly_OverridesGetIDFaction()
    {
        const int vehicleCbid = 650_020;
        const int driverCbid = 650_021;
        const int templateId = 650_022;
        const int missionFaction = 22;
        var map = CreateTestMap(9113);

        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, faction: 10);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, faction: 10, aiBehaviorId: 0);
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = templateId,
                VehicleCbid = vehicleCbid,
                DriverCbid = driverCbid,
                BaseHp = 150,
            }
        });

        var template = new SpawnPointTemplate
        {
            COID = 14_138,
            Faction = 0,
            FactionDirty = true,
            OriginalFaction = missionFaction,
            OriginalIsActive = false,
            IsActive = false,
        };
        template.ApplyFactionDirtyAuthoredFaction();
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = templateId,
            IsTemplate = true,
        });

        // Map placement copies template.Faction; also cover spawnpoint left at 0 with OriginalFaction set.
        var spawnPoint = new SpawnPoint(template) { Faction = template.Faction };
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.IsFalse(vehicle.IsInvincible, "template NPC combat vehicles must be damageable");
        Assert.AreEqual(missionFaction, vehicle.GetIDFaction(),
            "FactionDirty OriginalFaction must win over default Human Faction=0");
        Assert.AreEqual(missionFaction, vehicle.Owner.Faction);
    }

    /// <summary>
    /// Fallback when live spawnpoint Faction was never copied from fam (still 0) but OriginalFaction is set.
    /// </summary>
    [TestMethod]
    public void Spawn_TemplateVehicle_FactionDirty_SpawnPointFactionZero_UsesOriginalFaction()
    {
        const int vehicleCbid = 650_030;
        const int driverCbid = 650_031;
        const int templateId = 650_032;
        const int missionFaction = 22;
        var map = CreateTestMap(9114);

        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, faction: 10);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, faction: 10, aiBehaviorId: 0);
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = templateId,
                VehicleCbid = vehicleCbid,
                DriverCbid = driverCbid,
            }
        });

        var template = new SpawnPointTemplate
        {
            COID = 14_139,
            Faction = 0,
            FactionDirty = true,
            OriginalFaction = missionFaction,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = templateId,
            IsTemplate = true,
        });

        var spawnPoint = new SpawnPoint(template) { Faction = 0 };
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);

        Assert.IsTrue(spawnPoint.Spawn());

        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.AreEqual(missionFaction, vehicle.GetIDFaction(),
            "ApplySpawnFactionOverride must fall back to OriginalFaction when Faction is 0");
    }

    /// <summary>
    /// Final Exam class: personal Create leaves marker; Activate materializes combat car.
    /// Must be damageable, correctly factioned, and not MapPresence-suppressed.
    /// </summary>
    [TestMethod]
    public void Activate_TemplateCombatSpawn_MaterializesDamageableUnsuppressedVehicle()
    {
        const int vehicleCbid = 650_040;
        const int driverCbid = 650_041;
        const int templateId = 580;
        const int missionFaction = 22;
        const long spawnCoid = 14138;
        const long createRx = 14139;
        const long actRx = 14142;

        var map = CreateTestMap(9115);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, faction: 10);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, faction: 10, aiBehaviorId: 0);
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = templateId,
                VehicleCbid = vehicleCbid,
                DriverCbid = driverCbid,
                BaseHp = 150,
            }
        });

        var template = new SpawnPointTemplate
        {
            COID = (int)spawnCoid,
            FactionDirty = true,
            OriginalFaction = missionFaction,
            OriginalIsActive = false,
            IsActive = false,
        };
        template.ApplyFactionDirtyAuthoredFaction();
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = templateId,
            IsTemplate = true,
        });
        map.MapData.Templates[spawnCoid] = template;

        var character = new Character();
        character.SetCoid(9001, true);
        var playerVehicle = new Vehicle();
        playerVehicle.SetCoid(9002, true);
        character.SetCurrentVehicleForTests(playerVehicle);
        character.SetMap(map);
        playerVehicle.SetMap(map);

        var createTpl = new ReactionTemplate
        {
            COID = (int)createRx,
            ReactionType = ReactionType.Create,
            DoForAllPlayers = false,
        };
        createTpl.Objects.Add(spawnCoid);
        var create = new Reaction(createTpl);
        create.SetCoid(createRx, false);
        create.SetMap(map);

        Assert.IsTrue(create.TriggerIfPossible(playerVehicle));
        var marker = map.GetObjectByCoid(spawnCoid) as SpawnPoint;
        Assert.IsNotNull(marker);
        Assert.IsFalse(marker.HasLiveSpawn(), "personal Create must leave template combat marker-only");

        var actTpl = new ReactionTemplate
        {
            COID = (int)actRx,
            ReactionType = ReactionType.Activate,
        };
        actTpl.Objects.Add(spawnCoid);
        var activate = new Reaction(actTpl);
        activate.SetCoid(actRx, false);
        activate.SetMap(map);

        Assert.IsTrue(activate.TriggerIfPossible(playerVehicle));
        Assert.IsTrue(marker.HasLiveSpawn(), "Activate must materialize combat children");

        var npcCar = map.Objects.Values.OfType<Vehicle>()
            .Single(v => v.TemplateId == templateId);
        Assert.IsFalse(npcCar.IsInvincible);
        Assert.AreEqual(missionFaction, npcCar.GetIDFaction());
        Assert.IsFalse(character.MapPresence.IsSuppressed(npcCar.ObjectId.Coid),
            "kill-target combat NPC must not be presence-suppressed");
        Assert.IsFalse(character.MapPresence.IsSuppressed(spawnCoid),
            "combat spawn marker must not be suppressed for the activator");
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

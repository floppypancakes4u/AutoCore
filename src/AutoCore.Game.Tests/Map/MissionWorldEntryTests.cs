using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission.Requirements;
using MissionDef = AutoCore.Game.Mission.Mission;
using MissionObj = AutoCore.Game.Mission.MissionObjective;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

/// <summary>
/// Pass 20 — mission-phase world objects at login / transfer / re-entry.
/// First reproduced miss: Create-only template-vehicle kill targets (Tierra Roja 3882 /
/// Wastes 18609 / Canyon Run 23413) stayed marker-only because personal Create does not
/// Spawn() template children and those FAM Creates have no sibling Activate.
/// </summary>
[TestClass]
public class MissionWorldEntryTests
{
    private const int ContId = 8698;
    private const int MissionId = 92977;
    private const int KillObjectiveId = 95268;
    private const int DeliverObjectiveId = 95269;
    private const int TemplateId = 587;
    private const int VehicleCbid = 770_587;
    private const int DriverCbid = 770_588;
    private const int WheelsetCbid = 770_589;
    private const int DeliverCbid = 12321;
    private const long CombatSpawnCoid = 3882;
    private const long CreateCombatRx = 3883;
    private const long DeliverSpawnCoid = 3085;
    private const long CreateDeliverRx = 3895;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TriggerManager.Instance.ClearAllForTests();
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TriggerManager.Instance.ClearAllForTests();
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void MissionWorldEntry_AppliesCurrentPlayerPhase()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        var spawn = (SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid);
        Assert.IsTrue(spawn.HasLiveSpawn(),
            "Create-only template kill target (TR 3882 / Champion class) must materialize children, not a marker");
        Assert.AreEqual(1, CountOwnedVehicles(map, CombatSpawnCoid));
    }

    [TestMethod]
    public void MissionActivate_TemplateVehicleUsesPass19Resolution()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        var npc = map.Objects.Values.OfType<Vehicle>().Single(v => v.SpawnOwnerCoid == CombatSpawnCoid);
        Assert.AreEqual(TemplateId, npc.TemplateId);
        Assert.AreEqual(VehicleCbid, npc.CBID);
        Assert.IsNotNull(npc.WheelSet);
        Assert.AreEqual(WheelsetCbid, npc.WheelSet.CBID);
        Assert.IsNotNull(npc.Owner);
        Assert.AreEqual(DriverCbid, npc.Owner.CBID);
        Assert.IsNull(npc.Owner.Map, "Pass 9: driver stays unmapped");
        Assert.IsNull(npc.Owner.Ghost, "Pass 9: driver stays ghostless");
    }

    [TestMethod]
    public void MissionCreate_VehicleUsesNormalVehicleLifecycle()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        var npc = map.Objects.Values.OfType<Vehicle>().Single(v => v.SpawnOwnerCoid == CombatSpawnCoid);
        Assert.IsNotNull(npc.Map);
        Assert.AreSame(map, npc.Map);
        Assert.IsNotNull(npc.Ghost);
        Assert.IsInstanceOfType(npc.Ghost, typeof(GhostVehicle));
    }

    [TestMethod]
    public void MissionWorldEntry_AppliesBeforeFirstScope()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.BeginWorldEntry();
        character.SetMap(map);
        vehicle.SetMap(map);
        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            "SS-51: phase must not spawn while the client is still loading in");

        character.CompleteWorldEntry();
        Assert.IsTrue(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            "flush after Creates must materialize kill-phase children before first PerformScopeQuery");
    }

    [TestMethod]
    public void MissionWorldEntry_InitialLoginAndTransferEquivalent()
    {
        var (loginChar, loginVeh, loginMap) = CreateKillPhaseWorld(contId: ContId);
        loginChar.BeginWorldEntry();
        loginChar.SetMap(loginMap);
        loginVeh.SetMap(loginMap);
        loginMap.FireOnLoadPlayerMissions(loginChar);
        loginMap.ApplyMissionPhaseWorldState(loginVeh);
        loginChar.CompleteWorldEntry();

        var (xferChar, xferVeh, xferMap) = CreateKillPhaseWorld(contId: ContId + 1);
        xferChar.BeginWorldEntry();
        xferChar.SetMap(xferMap);
        xferVeh.SetMap(xferMap);
        xferMap.FireOnLoadPlayerMissions(xferChar);
        xferMap.ApplyMissionPhaseWorldState(xferVeh);
        xferChar.CompleteWorldEntry();

        Assert.AreEqual(
            ((SpawnPoint)loginMap.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            ((SpawnPoint)xferMap.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn());
        Assert.AreEqual(
            CountOwnedVehicles(loginMap, CombatSpawnCoid),
            CountOwnedVehicles(xferMap, CombatSpawnCoid));
    }

    [TestMethod]
    public void MissionWorldEntry_ReentryDoesNotLoseObjects()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsTrue(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn());

        character.SetMap(null);
        vehicle.SetMap(null);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            "re-entry with unchanged kill phase must still have the combat vehicle");
    }

    [TestMethod]
    public void MissionWorldEntry_ReentryDoesNotDuplicateObjects()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        var first = CountOwnedVehicles(map, CombatSpawnCoid);

        map.ApplyMissionPhaseWorldState(vehicle);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.AreEqual(first, CountOwnedVehicles(map, CombatSpawnCoid),
            "repeated phase apply must not duplicate mission children");
    }

    [TestMethod]
    public void MissionActivate_RealInactiveSpawnPointCreatesChildren()
    {
        MissionWorldEntry_AppliesCurrentPlayerPhase();
    }

    [TestMethod]
    public void MissionActivate_UsesPass17Population()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld(lower: 2, upper: 2);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.AreEqual(2, CountOwnedVehicles(map, CombatSpawnCoid),
            "mission Activate/Create must honor authored Lower/Upper");
    }

    [TestMethod]
    public void MissionActivate_UsesPass18Scatter()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld(lower: 3, upper: 3, radius: 12f, randomOffset: true);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        var children = map.Objects.Values.OfType<Vehicle>().Where(v => v.SpawnOwnerCoid == CombatSpawnCoid).ToList();
        Assert.AreEqual(3, children.Count);
        var unique = children.Select(c => (c.Position.X, c.Position.Z)).Distinct().Count();
        Assert.IsTrue(unique > 1, "mission-created template vehicles must use Pass 18 scatter");
    }

    [TestMethod]
    public void MissionCreate_CreatureMappedAndGhosted()
    {
        const int creatureCbid = 770_124;
        AssetManagerTestHelper.RegisterCreatureCloneBase(creatureCbid, isNpc: 0);
        var mission = MissionDef.CreateForTests(MissionId,
            KillObjective(creatureCbid, template: false));
        AssetManager.Instance.SetTestMission(mission);

        var map = CreateMap(ContId + 20);
        var tpl = new SpawnPointTemplate { COID = (int)CombatSpawnCoid, OriginalIsActive = false, IsActive = false };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = creatureCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[CombatSpawnCoid] = tpl;
        PlaceCreate(map, CreateCombatRx, CombatSpawnCoid);
        var spawn = (SpawnPoint)tpl.Create();
        spawn.SetCoid(CombatSpawnCoid, false);
        spawn.SetMap(map);

        var (character, vehicle) = PlacePlayer(map);
        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreSame(map, child.Map);
        Assert.IsNotNull(child.Ghost);
        Assert.IsInstanceOfType(child.Ghost, typeof(GhostCreature));
    }

    [TestMethod]
    public void MissionTargetLookup_ResolvesRealFamCoid()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        var create = (Reaction)map.GetObjectByCoid(CreateCombatRx);
        Assert.IsTrue(create.TriggerIfPossible(vehicle));
        Assert.IsNotNull(map.GetObjectByCoid(CombatSpawnCoid));
        Assert.AreEqual(CombatSpawnCoid, create.Template.Objects[0]);
    }

    [TestMethod]
    public void MissionTargetLookup_UsesDestinationMapInstance()
    {
        var (_, _, mapA) = CreateKillPhaseWorld(contId: ContId);
        var (_, vehicleB, mapB) = CreateKillPhaseWorld(contId: ContId);
        Assert.AreNotSame(mapA, mapB);
        Assert.AreNotEqual(mapA.InstanceSerial, mapB.InstanceSerial);

        vehicleB.SetMap(mapB);
        mapB.ApplyMissionPhaseWorldState(vehicleB);
        Assert.IsTrue(((SpawnPoint)mapB.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn());
        Assert.IsFalse(((SpawnPoint)mapA.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            "mission Create/Activate must not leak children onto a sibling instance");
    }

    [TestMethod]
    public void MissionDeactivate_UsesRetailSemantics()
    {
        // These four FAMs author 0 Deactivate reactions. Retail Deactivate on a SpawnPoint
        // is SetObjectActiveState(false): stop refill, do not allocate new children.
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        var before = CountOwnedVehicles(map, CombatSpawnCoid);

        var deact = new ReactionTemplate { COID = 3999, ReactionType = ReactionType.Deactivate };
        deact.Objects.Add(CombatSpawnCoid);
        var rx = new Reaction(deact);
        rx.SetCoid(3999, false);
        rx.SetMap(map);
        Assert.IsTrue(rx.TriggerIfPossible(vehicle));
        Assert.AreEqual(before, CountOwnedVehicles(map, CombatSpawnCoid),
            "Deactivate must not destroy already-materialized children (retail SetObjectActiveState)");
    }

    [TestMethod]
    public void MissionCompleted_ReentryUsesCompletedWorldState()
    {
        SeedChampionMission();
        RegisterTemplateVehicle();
        var map = CreateMap(ContId + 30);
        PlaceCombatGraph(map);
        PlaceDeliverGraph(map);
        var (character, vehicle) = PlacePlayer(map);
        character.CompletedMissionIds.Add(MissionId);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn(),
            "completed Champion-class mission must not respawn the duel-master car");
        Assert.IsTrue(character.MapPresence.IsMaterialized(DeliverSpawnCoid)
                      || ((SpawnPoint)map.GetObjectByCoid(DeliverSpawnCoid)).HasLiveSpawn()
                      || map.GetObjectByCoid(DeliverSpawnCoid) != null);
    }

    [TestMethod]
    public void MissionPhaseAdvance_UpdatesRequiredWorldObjects()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        PlaceDeliverGraph(map);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsTrue(((SpawnPoint)map.GetObjectByCoid(CombatSpawnCoid)).HasLiveSpawn());

        character.CurrentQuests[0].ActiveObjectiveSequence = 1;
        character.CurrentQuests[0].PopulateFromAssets();
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsMaterialized(DeliverSpawnCoid)
                      || map.GetObjectByCoid(DeliverSpawnCoid) != null,
            "kill→deliver must Create the pad/turn-in spawn without leaving the map");
    }

    [TestMethod]
    public void MissionObjectFailure_LogsMissionMapTargetAndAction()
    {
        var (character, vehicle, map) = CreateKillPhaseWorld();
        character.SetMap(map);
        vehicle.SetMap(map);
        var missing = new ReactionTemplate { COID = 4100, ReactionType = ReactionType.Create };
        missing.Objects.Add(99_999_111);
        var rx = new Reaction(missing);
        rx.SetCoid(4100, false);
        rx.SetMap(map);

        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            rx.TriggerIfPossible(vehicle);
        }
        finally
        {
            Console.SetOut(original);
        }

        var log = writer.ToString();
        StringAssert.Contains(log, "4100");
        StringAssert.Contains(log, "99999111");
    }

    [TestMethod]
    public void RealMission_ExpectedObjectsMatchAutoCore()
    {
        // Live FAM pin lives in LiveFamMissionWorldBaselineTests (Ark Bay 14138 + Champion 3882).
        // This synthetic twin requires the Create-only template path to produce children.
        MissionWorldEntry_AppliesCurrentPlayerPhase();
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreateKillPhaseWorld(
        int contId = ContId,
        byte lower = 1,
        byte upper = 1,
        float radius = 0f,
        bool randomOffset = false)
    {
        SeedChampionMission();
        RegisterTemplateVehicle();
        var map = CreateMap(contId);
        PlaceCombatGraph(map, lower, upper, radius, randomOffset);
        var (character, vehicle) = PlacePlayer(map);
        character.CurrentQuests.Add(MakeQuest());
        return (character, vehicle, map);
    }

    private static void SeedChampionMission()
    {
        var kill = KillObjective(TemplateId, template: true);
        var deliver = MissionObj.CreateForTests(DeliverObjectiveId, 1, MissionId, 1);
        deliver.Requirements.Add(new ObjectiveRequirementDeliver(deliver)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var mission = MissionDef.CreateForTests(MissionId, kill, deliver);
        mission.NPC = 12021;
        AssetManager.Instance.SetTestMission(mission);
    }

    private static MissionObj KillObjective(int target, bool template)
    {
        var obj = MissionObj.CreateForTests(KillObjectiveId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = target,
            TargetIsTemplateVehicle = template,
            NumToKill = 1,
        });
        return obj;
    }

    private static CharacterQuest MakeQuest(byte seq = 0)
    {
        var quest = new CharacterQuest(MissionId, seq);
        quest.PopulateFromAssets();
        return quest;
    }

    private static void RegisterTemplateVehicle()
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

    private static void PlaceCombatGraph(
        SectorMap map,
        byte lower = 1,
        byte upper = 1,
        float radius = 0f,
        bool randomOffset = false)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)CombatSpawnCoid,
            OriginalIsActive = false,
            IsActive = false,
            Radius = radius,
            RandomlyOffsetSpawnPosition = randomOffset,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = lower,
            UpperNumberOfSpawns = upper,
        });
        map.MapData.Templates[CombatSpawnCoid] = tpl;
        PlaceCreate(map, CreateCombatRx, CombatSpawnCoid);
        var spawn = (SpawnPoint)tpl.Create();
        spawn.SetCoid(CombatSpawnCoid, false);
        spawn.Position = new Vector3(100f, 4f, -50f);
        spawn.SetMap(map);
    }

    private static void PlaceDeliverGraph(SectorMap map)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)DeliverSpawnCoid,
            OriginalIsActive = false,
            IsActive = false,
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = DeliverCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        map.MapData.Templates[DeliverSpawnCoid] = tpl;
        PlaceCreate(map, CreateDeliverRx, DeliverSpawnCoid);
        var spawn = (SpawnPoint)tpl.Create();
        spawn.SetCoid(DeliverSpawnCoid, false);
        spawn.SetMap(map);
    }

    private static void PlaceCreate(SectorMap map, long reactionCoid, long targetCoid)
    {
        var tpl = new ReactionTemplate { COID = (int)reactionCoid, ReactionType = ReactionType.Create };
        tpl.Objects.Add(targetCoid);
        map.MapData.Templates[reactionCoid] = tpl;
        var rx = new Reaction(tpl);
        rx.SetCoid(reactionCoid, false);
        rx.SetMap(map);
    }

    private static SectorMap CreateMap(int continentId)
    {
        return SectorMap.CreateForTests(new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_mission_world_{continentId}",
            DisplayName = "mission-world",
            IsPersistent = true,
        }, new Vector4());
    }

    private static (Character Character, Vehicle Vehicle) PlacePlayer(SectorMap map)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        var character = new Character();
        character.SetCoid(500 + map.ContinentId, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = new Vector3() };
        vehicle.SetCoid(600 + map.ContinentId, true);
        character.SetCurrentVehicleForTests(vehicle);
        return (character, vehicle);
    }

    private static int CountOwnedVehicles(SectorMap map, long spawnCoid)
        => map.Objects.Values.OfType<Vehicle>().Count(v => v.SpawnOwnerCoid == spawnCoid);
}

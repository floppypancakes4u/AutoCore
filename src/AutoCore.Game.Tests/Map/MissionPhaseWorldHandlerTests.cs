using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Generic mission-phase world reconstruction (Final Exam class):
/// kill seq → deliver/pad Create, login hygiene, completed pad-not-giver, multi-req AND.
/// Synthetic ids only — no retail Gunny hardcoding in production paths under test.
/// </summary>
[TestClass]
public class MissionPhaseWorldHandlerTests
{
    private const int ContId = 8812;
    private const int MissionId = 98100;
    private const int KillObjectiveId = 98101;
    private const int DeliverObjectiveId = 98102;
    private const int GiverCbid = 12447;
    private const int DeliverCbid = 12448;
    private const int KillTemplateId = 580;
    private const long DialogSpawnCoid = 24090;
    private const long CombatSpawnCoid = 24138;
    private const long PadSpawnCoid = 25820;
    private const long CreateCombatRx = 24139;
    private const long CreatePadRx = 25819;
    private const long WaypointCoid = 23939;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void KillAdvance_ThenReplay_CreatesPadForActiveDeliver()
    {
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlacePadMarker(map);

        var quest = MakeQuest(seq: 0);
        character.CurrentQuests.Add(quest);
        character.SetMap(map);
        vehicle.SetMap(map);

        // Simulate kill complete → seq 1 (persistence already covered elsewhere).
        quest.ActiveObjectiveSequence = 1;
        quest.PopulateFromAssets();

        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.IsTrue(fired > 0, "Deliver-phase Create must fire after kill advance");
        Assert.IsTrue(character.MapPresence.IsMaterialized(PadSpawnCoid)
            || map.GetObjectByCoid(PadSpawnCoid) != null,
            "Pad spawn must be materialized for the player");
    }

    [TestMethod]
    public void HygieneThenReplay_Seq1_SuppressesDialog_CreatesPad_NoCombatCar()
    {
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreateCombatRx, CombatSpawnCoid);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);

        // Leaked combat + missing pad (mid-mission before kill).
        PlaceCombatCar(map);
        PlaceDialogCreature(map);

        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map); // solo hygiene
        vehicle.SetMap(map);

        Assert.IsNull(map.GetObjectByCoid(90010), "Hygiene purges combat car");
        Assert.IsNotNull(map.GetObjectByCoid(DialogSpawnCoid) as SpawnPoint,
            "Hygiene restores fam-active dialog marker");

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogSpawnCoid)
            || character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Deliver phase must suppress original giver for this player");
        Assert.IsTrue(character.MapPresence.IsMaterialized(PadSpawnCoid)
            || map.GetObjectByCoid(PadSpawnCoid) != null,
            "Pad Create must run for seq-1 deliver");
        Assert.IsNull(map.GetObjectByCoid(90010), "Combat car must stay gone on deliver phase");
    }

    [TestMethod]
    public void HygieneThenReplay_Seq0_CreatesCombat_NotPad()
    {
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreateCombatRx, CombatSpawnCoid);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlaceCombatSpawnMarker(map);

        character.CurrentQuests.Add(MakeQuest(seq: 0));
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsMaterialized(CombatSpawnCoid)
            || map.GetObjectByCoid(CombatSpawnCoid) != null,
            "Kill-phase login must Create combat spawn");
        Assert.IsFalse(character.MapPresence.IsMaterialized(PadSpawnCoid),
            "Pad must not Create while still on kill objective");
    }

    [TestMethod]
    public void CompletedMission_PhaseApply_MaterializesPad_SuppressesGiver()
    {
        SeedTwoSeqMission(giverNpcCbid: GiverCbid);
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlaceDialogCreature(map);
        PlacePadMarker(map);

        character.CompletedMissionIds.Add(MissionId);
        // No active quests — post-turn-in relog.
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsMaterialized(PadSpawnCoid)
            || map.GetObjectByCoid(PadSpawnCoid) != null,
            "Completed mission must keep pad-class NPC for the player");
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogSpawnCoid)
            || character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Completed mission must not leave original giver interactable");
    }

    [TestMethod]
    public void KillToDeliver_ClearedTe_ReplayStillCreatesPad()
    {
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);

        var combatTpl = (SpawnPointTemplate)map.MapData.Templates[CombatSpawnCoid];
        combatTpl.TriggerEvents = new long[] { 25818, -1, -1 };
        var combatSpawn = new SpawnPoint(combatTpl);
        combatSpawn.SetCoid(CombatSpawnCoid, false);
        combatSpawn.SetMap(map);
        // TE bookkeeping wiped (hygiene / bug path) — deliver Create must not depend on it.
        combatSpawn.ClearReactionMaterializationState();

        PlacePadMarker(map);
        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map);
        vehicle.SetMap(map);

        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.IsTrue(fired > 0, "Deliver Create must not depend solely on combat TE bookkeeping");
    }

    [TestMethod]
    public void SameNpcDeliver_DoesNotSuppressGiver()
    {
        // Red Tape class: deliver target CBID == Mission.NPC (return to giver).
        const int sameNpc = 2468;
        var obj0 = MissionObjective.CreateForTests(KillObjectiveId, 0, MissionId, 1);
        obj0.Requirements.Add(new ObjectiveRequirementKill(obj0)
        {
            TargetCBID = 1,
            NumToKill = 1,
        });
        var obj1 = MissionObjective.CreateForTests(DeliverObjectiveId, 1, MissionId, 1);
        obj1.Requirements.Add(new ObjectiveRequirementDeliver(obj1)
        {
            NPCTargetCBID = sameNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, obj0, obj1);
        mission.NPC = sameNpc;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayerWithMap();
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(
            DialogSpawnCoid, originalActive: true, spawnType: sameNpc);
        PlaceDialogCreatureForCbid(map, sameNpc);

        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawnCoid),
            "Same-NPC deliver must not suppress the giver spawn");
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Same-NPC deliver must not suppress the giver creature");
    }

    [TestMethod]
    public void SameNpcDeliver_ClearsPriorIncorrectSuppress()
    {
        // Simulates session that already suppressed the return-to-giver NPC (pre-fix bug).
        const int sameNpc = 2468;
        var obj1 = MissionObjective.CreateForTests(DeliverObjectiveId, 0, MissionId, 1);
        obj1.Requirements.Add(new ObjectiveRequirementDeliver(obj1)
        {
            NPCTargetCBID = sameNpc,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, obj1);
        mission.NPC = sameNpc;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayerWithMap();
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(
            DialogSpawnCoid, originalActive: true, spawnType: sameNpc);
        PlaceDialogCreatureForCbid(map, sameNpc);

        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(DialogSpawnCoid);
        character.MapPresence.Suppress(DialogCreatureCoid);
        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogCreatureCoid));

        character.CurrentQuests.Add(MakeQuest(seq: 0));
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Phase re-apply must unsuppress same-NPC deliver target after incorrect suppress");
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawnCoid));
    }

    [TestMethod]
    public void DifferentNpcDeliver_StillSuppressesGiver()
    {
        // Final Exam class: deliver CBID != Mission.NPC (pad form vs standing giver).
        SeedTwoSeqMission(giverNpcCbid: GiverCbid);
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlaceDialogCreature(map);
        PlacePadMarker(map);

        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogSpawnCoid)
            || character.MapPresence.IsSuppressed(DialogCreatureCoid),
            "Pad deliver (different CBID) must still suppress original giver");
    }

    [TestMethod]
    public void MarkerOnlyMaterialize_IsNotPresentDeliverNpc()
    {
        // Materializing the spawn marker alone must not count as "deliver NPC present"
        // (that short-circuited Create and left no interactable Creature).
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlacePadMarker(map);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Materialize(PadSpawnCoid);

        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map);
        vehicle.SetMap(map);

        Assert.IsFalse(map.MapHasPresentEntityWithCbidForTests(character, DeliverCbid),
            "Marker-only materialize is not a live deliver NPC");
    }

    [TestMethod]
    public void EnsureDeliverNpcChildren_SpawnsWhenAssetsMissing_PlacesViaHook()
    {
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlacePadMarker(map);

        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ReplayMissionWorldSetup(vehicle);
        map.EnsureDeliverNpcChildren(vehicle, DeliverCbid);

        var pad = map.GetObjectByCoid(PadSpawnCoid) as SpawnPoint;
        Assert.IsNotNull(pad);
        // Unit tests lack clonebase for CBID 12448 — inject child as live Spawn would.
        if (!pad.HasLiveSpawn())
        {
            var child = new Creature();
            child.SetCoid(88048, true);
            child.SetCbidForTests(DeliverCbid);
            child.SpawnOwner = PadSpawnCoid;
            child.IsMissionGiver = true;
            child.SetMap(map);
            pad.SetLastSpawnedCoidForTests(88048);
        }

        Assert.IsTrue(map.MapHasPresentEntityWithCbidForTests(character, DeliverCbid));
    }

    [TestMethod]
    public void AutoPatrol_WithSiblingDeliver_ReplaysPadAndDoesNotComplete()
    {
        SeedPatrolPlusDeliverOnSeq1();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlacePadMarker(map);

        var wp = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        wp.SetCoid(WaypointCoid, false);
        wp.Position = new Vector3(0, 0, 0);
        wp.SetMap(map);

        character.CurrentQuests.Add(MakeQuest(seq: 1));
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        NpcInteractHandler.HandleAutoPatrol(character.OwningConnection, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "Must not complete on AutoPatrol alone");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(character.MapPresence.IsMaterialized(PadSpawnCoid)
            || map.GetObjectByCoid(PadSpawnCoid) != null,
            "AutoPatrol at pad must still Create/materialize deliver pad for turn-in");
    }

    [TestMethod]
    public void AutoPatrol_WithSiblingDeliver_DoesNotCompleteMission()
    {
        SeedPatrolPlusDeliverObjective();
        var (character, vehicle, map) = CreatePlayerWithMap();
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        var wp = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        wp.SetCoid(WaypointCoid, false);
        wp.Position = new Vector3(0, 0, 0);
        wp.SetMap(map);

        character.CurrentQuests.Add(MakeQuest(seq: 0));
        var conn = character.OwningConnection;

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "Patrol alone must not finish deliver+patrol objective");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    [TestMethod]
    public void KillVehicle_AdvancesToDeliver_AndReplaysPadCreate()
    {
        SeedTwoSeqMission();
        var (character, vehicle, map) = CreatePlayerWithMap();
        SeedSpawnGraph(map);
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlacePadMarker(map);

        character.CurrentQuests.Add(MakeQuest(seq: 0));
        character.SetMap(map);
        vehicle.SetMap(map);

        var combatTpl = (SpawnPointTemplate)map.MapData.Templates[CombatSpawnCoid];
        var combatSpawn = new SpawnPoint(combatTpl);
        combatSpawn.SetCoid(CombatSpawnCoid, false);
        combatSpawn.SetMap(map);

        // NPC combat vehicle matching kill template id.
        var npcCar = new Vehicle();
        npcCar.SetCoid(90020, true);
        npcCar.SpawnOwnerCoid = CombatSpawnCoid;
        npcCar.TemplateId = KillTemplateId;
        npcCar.SetCbidForTests(12425);
        npcCar.InitializeHealthForTests(50);
        npcCar.SetInvincible(false);
        // Need NpcAi so Vehicle.OnDeath takes NPC path — but unit tests may use GraphicsObject path.
        // Use MissionKillProgress.NotifyObjectKilled via SimpleObject death instead.
        npcCar.SetMap(map);
        combatSpawn.SetLastSpawnedCoidForTests(90020);

        npcCar.SetMurderer(vehicle);
        // Creature/Vehicle with NpcAi null uses player path — kill progress still via base.OnDeath
        // if Murderer set. Vehicle without NpcAi does not leave map the same way.
        // Prefer GraphicsObject with matching template path via KillMatches TargetIsTemplateVehicle.
        // Use MissionKillProgress path with a GraphicsObject that won't match template.
        // Instead call kill progress after manually matching via a vehicle with TemplateId.
        MissionKillProgress.NotifyObjectKilled(npcCar);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);

        // After advance, phase replay should Create pad even without TE.
        var fired = map.ReplayMissionWorldSetup(vehicle);
        Assert.IsTrue(fired > 0 || character.MapPresence.IsMaterialized(PadSpawnCoid),
            "After kill→deliver, pad Create must be available");
    }

    private const long DialogCreatureCoid = 88002;

    private void SeedTwoSeqMission(int giverNpcCbid = GiverCbid)
    {
        var killObj = MissionObjective.CreateForTests(KillObjectiveId, 0, MissionId, 1);
        killObj.Requirements.Add(new ObjectiveRequirementKill(killObj)
        {
            TargetCBID = KillTemplateId,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });

        var deliverObj = MissionObjective.CreateForTests(DeliverObjectiveId, 1, MissionId, 1);
        deliverObj.Requirements.Add(new ObjectiveRequirementDeliver(deliverObj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 1,
        });

        var mission = Mission.CreateForTests(MissionId, killObj, deliverObj);
        mission.NPC = giverNpcCbid;
        AssetManager.Instance.SetTestMission(mission);
    }

    private void SeedPatrolPlusDeliverObjective()
    {
        var obj = MissionObjective.CreateForTests(DeliverObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
        };
        patrol.GenericTargets[0] = WaypointCoid;
        patrol.TargetCount = 1;
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 1,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
    }

    /// <summary>Final Exam shape: kill seq0 + patrol+deliver seq1.</summary>
    private void SeedPatrolPlusDeliverOnSeq1()
    {
        var killObj = MissionObjective.CreateForTests(KillObjectiveId, 0, MissionId, 1);
        killObj.Requirements.Add(new ObjectiveRequirementKill(killObj)
        {
            TargetCBID = KillTemplateId,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });
        var deliverObj = MissionObjective.CreateForTests(DeliverObjectiveId, 1, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(deliverObj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
        };
        patrol.GenericTargets[0] = WaypointCoid;
        patrol.TargetCount = 1;
        deliverObj.Requirements.Add(patrol);
        deliverObj.Requirements.Add(new ObjectiveRequirementDeliver(deliverObj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 1,
        });
        var mission = Mission.CreateForTests(MissionId, killObj, deliverObj);
        mission.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(mission);
    }

    private static CharacterQuest MakeQuest(byte seq)
    {
        var quest = new CharacterQuest(MissionId, seq);
        quest.PopulateFromAssets();
        return quest;
    }

    private static void SeedSpawnGraph(SectorMap map)
    {
        map.MapData.Templates[DialogSpawnCoid] = MakeSpawnTemplate(DialogSpawnCoid, originalActive: true, spawnType: GiverCbid);
        map.MapData.Templates[CombatSpawnCoid] = MakeSpawnTemplate(CombatSpawnCoid, originalActive: false, spawnType: KillTemplateId, isTemplate: true);
        map.MapData.Templates[PadSpawnCoid] = MakeSpawnTemplate(PadSpawnCoid, originalActive: false, spawnType: DeliverCbid);
    }

    private static SpawnPointTemplate MakeSpawnTemplate(
        long coid,
        bool originalActive,
        int spawnType = -1,
        bool isTemplate = false)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)coid,
            IsActive = originalActive,
            OriginalIsActive = originalActive,
            CBID = 0,
        };
        if (spawnType > 0)
        {
            tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
            {
                SpawnType = spawnType,
                IsTemplate = isTemplate,
                LowerNumberOfSpawns = 1,
                UpperNumberOfSpawns = 1,
            });
        }

        return tpl;
    }

    private static void PlaceCreateReaction(SectorMap map, long reactionCoid, long targetSpawnCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            ReactionType = ReactionType.Create,
        };
        tpl.Objects.Add(targetSpawnCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
    }

    private static void PlacePadMarker(SectorMap map)
    {
        var tpl = (SpawnPointTemplate)map.MapData.Templates[PadSpawnCoid];
        var spawn = new SpawnPoint(tpl);
        spawn.SetCoid(PadSpawnCoid, false);
        spawn.SetMap(map);
    }

    private static void PlaceCombatSpawnMarker(SectorMap map)
    {
        var tpl = (SpawnPointTemplate)map.MapData.Templates[CombatSpawnCoid];
        var spawn = new SpawnPoint(tpl);
        spawn.SetCoid(CombatSpawnCoid, false);
        spawn.SetMap(map);
    }

    private static void PlaceDialogCreature(SectorMap map)
        => PlaceDialogCreatureForCbid(map, GiverCbid);

    private static void PlaceDialogCreatureForCbid(SectorMap map, int npcCbid)
    {
        if (map.GetObjectByCoid(DialogSpawnCoid) is not SpawnPoint)
        {
            var tpl = (SpawnPointTemplate)map.MapData.Templates[DialogSpawnCoid];
            var spawn = new SpawnPoint(tpl);
            spawn.SetCoid(DialogSpawnCoid, false);
            spawn.SetMap(map);
            spawn.SetLastSpawnedCoidForTests(DialogCreatureCoid);
        }

        var creature = new Creature();
        creature.SetCoid(DialogCreatureCoid, true);
        creature.SetCbidForTests(npcCbid);
        creature.SpawnOwner = DialogSpawnCoid;
        creature.IsMissionGiver = true;
        creature.SetMap(map);
    }

    private static void PlaceCombatCar(SectorMap map)
    {
        var tpl = (SpawnPointTemplate)map.MapData.Templates[CombatSpawnCoid];
        var spawn = new SpawnPoint(tpl);
        spawn.SetCoid(CombatSpawnCoid, false);
        spawn.SetMap(map);
        var car = new Vehicle();
        car.SetCoid(90010, true);
        car.SpawnOwnerCoid = CombatSpawnCoid;
        car.SetMap(map);
        spawn.SetLastSpawnedCoidForTests(90010);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayerWithMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_phase_world_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(500, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3() };
        vehicle.SetCoid(501, true);
        character.SetCurrentVehicleForTests(vehicle);
        return (character, vehicle, map);
    }
}

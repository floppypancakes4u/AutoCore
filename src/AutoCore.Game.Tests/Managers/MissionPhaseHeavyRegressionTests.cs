using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Dense regression for mission-phase deliver, UseObject resolve, AutoPatrol idempotency,
/// force-complete, and giver suppress rules.
/// </summary>
[TestClass]
public class MissionPhaseHeavyRegressionTests
{
    private const int ContId = 8950;
    private const int MissionId = 98500;
    private const int KillObjId = 98501;
    private const int DeliverObjId = 98502;
    private const int GiverCbid = 12447;
    private const int DeliverCbid = 12448;
    private const int KillTemplateId = 580;
    private const long DialogSpawn = 34090;
    private const long PadSpawn = 35820;
    private const long CreatePadRx = 35819;
    private const long Waypoint = 33939;
    private const long DialogCreature = 99002;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(DeliverCbid, maxHitPoint: 80);
        var cb = AssetManager.Instance.GetCloneBase(DeliverCbid) as AutoCore.Game.CloneBases.CloneBaseCreature;
        if (cb != null)
            cb.CreatureSpecific.IsNPC = 1;
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
        MissionClientSoftPedal.ResetForTests();
        MissionClientSoftPedal.GroupReactionSuppressMs = 0;
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        MissionClientSoftPedal.ResetForTests();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void AutoPatrol_WhenDeliverReady_NoLogSpamOrPackets()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        character.CurrentQuests.Add(MakeQuest(1));
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        PlaceWaypoint(map);

        map.EnsureDeliverTurnInNpc(vehicle, DeliverCbid);
        _sent.Clear();

        for (var i = 0; i < 30; i++)
        {
            NpcInteractHandler.HandleAutoPatrol(character.OwningConnection, new AutoPatrolPacket
            {
                Target = new TFID(Waypoint, false),
            });
        }

        Assert.AreEqual(0, _sent.OfType<CreateCreaturePacket>().Count());
        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count());
        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void UseObject_SuppressedResolvedNpc_Rejected()
    {
        SeedDeliverOnly();
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        character.CurrentQuests.Add(MakeQuest(0));

        var npc = new Creature { Position = new Vector3(0, 0, 0) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 1, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.SetMap(map);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(npc.ObjectId.Coid);

        _sent.Clear();
        NpcInteractHandler.HandleUseObject(character.OwningConnection, new UseObjectPacket
        {
            Target = new TFID(99999, false), // missing — resolve by CBID then suppress check
            ObjectiveId = DeliverObjId,
        });
        Assert.IsFalse(_sent.OfType<NpcMissionDialogPacket>().Any());
    }

    [TestMethod]
    public void UseObject_OutOfRangeResolvedNpc_Rejected()
    {
        SeedDeliverOnly();
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.CurrentQuests.Add(MakeQuest(0));

        var npc = new Creature { Position = new Vector3(500, 0, 500) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 2, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.SetMap(map);

        _sent.Clear();
        NpcInteractHandler.HandleUseObject(character.OwningConnection, new UseObjectPacket
        {
            Target = new TFID(88888, false),
            ObjectiveId = DeliverObjId,
        });
        Assert.IsFalse(_sent.OfType<NpcMissionDialogPacket>().Any());
    }

    [TestMethod]
    public void TryResolveNearbyDeliver_IgnoresSuppressedAndWrongCbid()
    {
        SeedDeliverOnly();
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        character.CurrentQuests.Add(MakeQuest(0));

        var wrong = new Creature { Position = new Vector3(0, 0, 0) };
        wrong.SetCoid(MapNpcIdentity.CoidBase + 3, true);
        wrong.SetCbidForTests(999);
        wrong.SetMap(map);

        var suppressed = new Creature { Position = new Vector3(1, 0, 0) };
        suppressed.SetCoid(MapNpcIdentity.CoidBase + 4, true);
        suppressed.LoadCloneBase(DeliverCbid);
        suppressed.SetupCBFields();
        suppressed.SetMap(map);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(suppressed.ObjectId.Coid);

        Assert.IsNull(NpcInteractHandler.TryResolveNearbyDeliverNpc(
            character, DeliverObjId, new Vector3(0, 0, 0), 1));
    }

    [TestMethod]
    public void SameNpcDeliver_NoCreateErrorAndNoSuppress()
    {
        const int same = 2468;
        var obj = MissionObjective.CreateForTests(DeliverObjId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = same,
            NPCTargetCompletes = true,
        });
        var mission = Mission.CreateForTests(MissionId, obj);
        mission.NPC = same;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, same);
        PlaceDialog(map, same);
        character.CurrentQuests.Add(MakeQuest(0));
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn));
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreature));
    }

    [TestMethod]
    public void CompletedAlternateForm_SuppressesGiver_CreatesPadDeliver()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        PlaceDialog(map, GiverCbid);
        character.CompletedMissionIds.Add(MissionId);
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsTrue(
            character.MapPresence.IsSuppressed(DialogSpawn)
            || character.MapPresence.IsSuppressed(DialogCreature));
    }

    /// <summary>
    /// Room-and-Motherboard class: NPC was a completed alternate-form giver (suppressed),
    /// then becomes an active completing deliver target on a later mission — must unsuppress
    /// so UseObject turn-in is not rejected.
    /// </summary>
    [TestMethod]
    public void CompletedAltFormGiver_BecomesActiveDeliverTarget_IsUnsuppressed()
    {
        const int completedMissionId = MissionId + 10;
        const int activeMissionId = MissionId + 11;
        const int completedObjId = DeliverObjId + 10;
        const int activeObjId = DeliverObjId + 11;
        const int hutchinsLike = 22100; // past giver, current deliver target
        const int otherDeliver = 22101;
        const int otherGiver = 22102;
        const long hutchinsSpawn = DialogSpawn;
        const long hutchinsCreature = DialogCreature;

        // Mission A completed: Hutchins-like gave quest, turn-in elsewhere (alt-form).
        var completedObj = MissionObjective.CreateForTests(completedObjId, 0, completedMissionId, 1);
        completedObj.Requirements.Add(new ObjectiveRequirementDeliver(completedObj)
        {
            NPCTargetCBID = otherDeliver,
            NPCTargetCompletes = true,
        });
        var completedMission = Mission.CreateForTests(completedMissionId, completedObj);
        completedMission.NPC = hutchinsLike;
        completedMission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(completedMission);

        // Mission B active: deliver to Hutchins-like (Room and Motherboard class).
        var activeObj = MissionObjective.CreateForTests(activeObjId, 0, activeMissionId, 1);
        activeObj.Requirements.Add(new ObjectiveRequirementDeliver(activeObj)
        {
            NPCTargetCBID = hutchinsLike,
            NPCTargetCompletes = true,
        });
        var activeMission = Mission.CreateForTests(activeMissionId, activeObj);
        activeMission.NPC = otherGiver;
        AssetManager.Instance.SetTestMission(activeMission);
        NpcInteractHandler.InvalidateMissionIndex();

        AssetManagerTestHelper.RegisterCreatureCloneBase(hutchinsLike, maxHitPoint: 80);
        if (AssetManager.Instance.GetCloneBase(hutchinsLike) is AutoCore.Game.CloneBases.CloneBaseCreature hcb)
            hcb.CreatureSpecific.IsNPC = 1;

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[hutchinsSpawn] = MakeSpawn(hutchinsSpawn, true, hutchinsLike);
        PlaceDialog(map, hutchinsLike);

        character.CompletedMissionIds.Add(completedMissionId);
        var quest = new CharacterQuest(activeMissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
        character.SetMap(map);
        vehicle.SetMap(map);

        // Simulate prior phase suppress of the completed giver (common after class intros).
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(hutchinsSpawn);
        character.MapPresence.Suppress(hutchinsCreature);
        Assert.IsTrue(character.MapPresence.IsSuppressed(hutchinsCreature));

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(hutchinsSpawn),
            "Fam-active spawn for active deliver CBID must be unsuppressed");
        Assert.IsFalse(character.MapPresence.IsSuppressed(hutchinsCreature),
            "Live deliver NPC must be unsuppressed so UseObject is not rejected");
    }

    /// <summary>
    /// Track This / class-report class: completed deliver to a different standing NPC (no pad
    /// form) must not permanently suppress the original giver — they still offer follow-ups.
    /// </summary>
    [TestMethod]
    public void CompletedAltFormGiver_NonPadDeliver_NotPermanentlySuppressed()
    {
        const int completedMissionId = MissionId + 20;
        const int completedObjId = DeliverObjId + 20;
        const int hutchinsLike = 22200;
        const int otherStandingNpc = 22201;

        var completedObj = MissionObjective.CreateForTests(completedObjId, 0, completedMissionId, 1);
        completedObj.Requirements.Add(new ObjectiveRequirementDeliver(completedObj)
        {
            NPCTargetCBID = otherStandingNpc,
            NPCTargetCompletes = true,
        });
        var completedMission = Mission.CreateForTests(completedMissionId, completedObj);
        completedMission.NPC = hutchinsLike;
        completedMission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(completedMission);
        NpcInteractHandler.InvalidateMissionIndex();

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, hutchinsLike);
        PlaceDialog(map, hutchinsLike);

        character.CompletedMissionIds.Add(completedMissionId);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(DialogSpawn);
        character.MapPresence.Suppress(DialogCreature);
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn),
            "Non-pad alt-form completed giver must not stay suppressed (follow-up offers)");
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreature),
            "Live giver must remain interactable after non-pad alt-form completion");
    }

    /// <summary>
    /// Final Exam class: completed pad-form deliver still suppresses the original fam-active giver.
    /// </summary>
    [TestMethod]
    public void CompletedAltFormGiver_PadFormDeliver_StillSuppressesGiver()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        PlaceDialog(map, GiverCbid);
        character.CompletedMissionIds.Add(MissionId);
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(
            character.MapPresence.IsSuppressed(DialogSpawn)
            || character.MapPresence.IsSuppressed(DialogCreature),
            "Pad-class completed alt-form must keep original giver suppressed");
    }

    [TestMethod]
    public void ActiveAltFormDeliver_SuppressesGiver_NotDeliverTarget()
    {
        // Active pad/alt deliver: original giver suppressed; deliver CBID stays interactable.
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        PlaceDialog(map, GiverCbid);
        character.CurrentQuests.Add(MakeQuest(1)); // deliver seq
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(
            character.MapPresence.IsSuppressed(DialogSpawn)
            || character.MapPresence.IsSuppressed(DialogCreature),
            "Active alt-form must suppress original giver");
        Assert.IsFalse(character.MapPresence.IsSuppressed(PadSpawn),
            "Pad deliver marker must not be suppressed as giver");
    }

    [TestMethod]
    public void PhaseApply_UnknownActiveQuestMission_DoesNotThrow()
    {
        var (character, vehicle, map) = CreatePlayer();
        character.CurrentQuests.Add(new CharacterQuest(999_001, 0));
        character.CompletedMissionIds.Add(999_002);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void PhaseApply_CompletedMissionNullObjectives_DoesNotThrow()
    {
        const int mid = MissionId + 80;
        const int keepAlive = MissionId + 81;
        // No objectives dictionary — pad-class check must early-out.
        var mission = Mission.CreateForTests(mid);
        mission.NPC = GiverCbid;
        mission.IsRepeatable = 0;
        mission.Objectives = null;
        AssetManager.Instance.SetTestMission(mission);

        // Keep ReplayMissionWorldSetup from early-returning (needs active quest or deliver CBIDs).
        var aliveObj = MissionObjective.CreateForTests(DeliverObjId + 81, 0, keepAlive, 1);
        aliveObj.Requirements.Add(new ObjectiveRequirementDeliver(aliveObj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var alive = Mission.CreateForTests(keepAlive, aliveObj);
        alive.NPC = DeliverCbid;
        AssetManager.Instance.SetTestMission(alive);

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, GiverCbid);
        PlaceDialog(map, GiverCbid);
        character.CompletedMissionIds.Add(mid);
        character.CurrentQuests.Add(new CharacterQuest(keepAlive, 0));
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn));
    }

    [TestMethod]
    public void PhaseApply_CompletedRepeatableAltGiver_NotSuppressed()
    {
        const int mid = MissionId + 30;
        const int oid = DeliverObjId + 30;
        const int giver = 22300;
        var obj = MissionObjective.CreateForTests(oid, 0, mid, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 22301,
            NPCTargetCompletes = true,
        });
        var mission = Mission.CreateForTests(mid, obj);
        mission.NPC = giver;
        mission.IsRepeatable = 1;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, giver);
        PlaceDialog(map, giver);
        character.CompletedMissionIds.Add(mid);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn));
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreature));
    }

    [TestMethod]
    public void PhaseUnsuppress_VehicleOwnedBySpawn_Cleared()
    {
        const int mid = MissionId + 40;
        const int oid = DeliverObjId + 40;
        const int hutchinsLike = 22400;
        const int other = 22401;
        const long vehicleCoid = 99003;

        var completedObj = MissionObjective.CreateForTests(oid, 0, mid, 1);
        completedObj.Requirements.Add(new ObjectiveRequirementDeliver(completedObj)
        {
            NPCTargetCBID = other,
            NPCTargetCompletes = true,
        });
        var completedMission = Mission.CreateForTests(mid, completedObj);
        completedMission.NPC = hutchinsLike;
        completedMission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(completedMission);

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, hutchinsLike);
        if (map.GetObjectByCoid(DialogSpawn) is not SpawnPoint)
        {
            var sp = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[DialogSpawn]);
            sp.SetCoid(DialogSpawn, false);
            sp.SetMap(map);
            sp.SetLastSpawnedCoidForTests(vehicleCoid);
        }

        var npcVehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.SetCbidForTests(hutchinsLike);
        npcVehicle.SpawnOwnerCoid = DialogSpawn;
        npcVehicle.SetMap(map);

        character.CompletedMissionIds.Add(mid);
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(DialogSpawn);
        character.MapPresence.Suppress(vehicleCoid);
        character.SetMap(map);
        vehicle.SetMap(map);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn));
        Assert.IsFalse(character.MapPresence.IsSuppressed(vehicleCoid),
            "Spawn-owned vehicle COID must unsuppress with non-pad completed giver");
    }

    [TestMethod]
    public void PhaseApply_ActiveQuestMissingObjectiveSeq_SkipsWithoutThrow()
    {
        var obj = MissionObjective.CreateForTests(DeliverObjId + 60, 0, MissionId + 60, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var mission = Mission.CreateForTests(MissionId + 60, obj);
        mission.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayer();
        // Sequence 9 does not exist on the mission.
        character.CurrentQuests.Add(new CharacterQuest(MissionId + 60, 9));
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void PhaseSuppress_VehicleChildOfGiverSpawn_Suppressed()
    {
        SeedFinalExamShape();
        const long vehicleCoid = 99004;
        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);

        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, GiverCbid);
        var sp = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[DialogSpawn]);
        sp.SetCoid(DialogSpawn, false);
        sp.SetMap(map);
        sp.SetLastSpawnedCoidForTests(vehicleCoid);

        var npcVehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.SetCbidForTests(GiverCbid);
        npcVehicle.SpawnOwnerCoid = DialogSpawn;
        npcVehicle.SetMap(map);

        character.CompletedMissionIds.Add(MissionId);
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsTrue(character.MapPresence.IsSuppressed(DialogSpawn)
            || character.MapPresence.IsSuppressed(vehicleCoid),
            "Pad-class completed giver vehicle child must be suppressable");
        Assert.IsTrue(character.MapPresence.IsSuppressed(vehicleCoid),
            "Vehicle SpawnOwnerCoid child must be suppressed with giver");
    }

    [TestMethod]
    public void SameNpcDeliver_InvalidActiveSeq_DoesNotThrow()
    {
        const int same = 2469;
        var obj = MissionObjective.CreateForTests(DeliverObjId + 70, 0, MissionId + 70, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = same,
            NPCTargetCompletes = true,
        });
        var mission = Mission.CreateForTests(MissionId + 70, obj);
        mission.NPC = same;
        AssetManager.Instance.SetTestMission(mission);

        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, same);
        PlaceDialog(map, same);
        character.CurrentQuests.Add(new CharacterQuest(MissionId + 70, 3)); // missing seq
        character.SetMap(map);
        vehicle.SetMap(map);
        map.ApplyMissionPhaseWorldState(vehicle);
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn));
    }

    [TestMethod]
    public void ActiveDeliver_UnsuppressesEvenWhenAlsoCompletedPadGiver()
    {
        // Completed pad-class Final Exam shape + active deliver to same giver CBID elsewhere.
        SeedFinalExamShape();
        const int activeMid = MissionId + 50;
        const int activeOid = DeliverObjId + 50;
        var activeObj = MissionObjective.CreateForTests(activeOid, 0, activeMid, 1);
        activeObj.Requirements.Add(new ObjectiveRequirementDeliver(activeObj)
        {
            NPCTargetCBID = GiverCbid,
            NPCTargetCompletes = true,
        });
        var active = Mission.CreateForTests(activeMid, activeObj);
        active.NPC = 9999;
        AssetManager.Instance.SetTestMission(active);

        var (character, vehicle, map) = CreatePlayer();
        SeedPadGraph(map);
        PlaceDialog(map, GiverCbid);
        character.CompletedMissionIds.Add(MissionId);
        var q = new CharacterQuest(activeMid, 0);
        q.PopulateFromAssets();
        character.CurrentQuests.Add(q);
        character.SetMap(map);
        vehicle.SetMap(map);

        // Sticky suppress as if prior pad-complete phase had hidden the giver.
        character.MapPresence.EnsureContinent(ContId);
        character.MapPresence.Suppress(DialogSpawn);
        character.MapPresence.Suppress(DialogCreature);

        map.ApplyMissionPhaseWorldState(vehicle);

        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogSpawn),
            "Active deliver must unsuppress giver spawn over completed pad-giver suppress");
        Assert.IsFalse(character.MapPresence.IsSuppressed(DialogCreature),
            "Active deliver must unsuppress live giver creature");
    }

    [TestMethod]
    public void EnsureDeliverTurnIn_InvalidArgs_NoThrow()
    {
        var (character, vehicle, map) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);
        Assert.AreEqual(0, map.EnsureDeliverTurnInNpc(null, DeliverCbid));
        Assert.AreEqual(0, map.EnsureDeliverTurnInNpc(vehicle, 0));
        Assert.AreEqual(0, map.EnsureDeliverTurnInNpc(vehicle, -1));
    }

    [TestMethod]
    public void FireMatchingCreates_FromMapDataTemplate_WhenReactionNotLive()
    {
        SeedFinalExamShape();
        var (character, vehicle, map) = CreatePlayer();
        // Template only — no live Reaction entity yet.
        map.MapData.Templates[PadSpawn] = MakeSpawn(PadSpawn, false, DeliverCbid);
        map.MapData.Templates[CreatePadRx] = new ReactionTemplate
        {
            COID = (int)CreatePadRx,
            ReactionType = ReactionType.Create,
            Objects = { PadSpawn },
        };
        character.CurrentQuests.Add(MakeQuest(1));
        character.SetMap(map);
        vehicle.SetMap(map);

        var fired = map.EnsureDeliverTurnInNpc(vehicle, DeliverCbid);
        Assert.IsTrue(fired >= 0);
        Assert.IsNotNull(map.GetObjectByCoid(CreatePadRx) as Reaction
            ?? map.GetObjectByCoid(PadSpawn));
    }

    [TestMethod]
    public void ObjectiveHelpers_BlockingAndForceComplete()
    {
        var solo = MissionObjective.CreateForTests(1, 0, MissionId, 1);
        solo.Requirements.Add(new ObjectiveRequirementDeliver(solo)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
        });
        Assert.IsFalse(NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            solo, RequirementType.Patrol));
        Assert.IsFalse(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(solo));
        Assert.IsFalse(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(null));

        var multi = MissionObjective.CreateForTests(2, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(multi) { AutoComplete = true };
        patrol.GenericTargets[0] = 1;
        patrol.TargetCount = 1;
        multi.Requirements.Add(patrol);
        multi.Requirements.Add(new ObjectiveRequirementDeliver(multi)
        {
            NPCTargetCBID = 2,
            NPCTargetCompletes = true,
        });
        Assert.IsTrue(NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            multi, RequirementType.Patrol));
        Assert.IsTrue(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(multi));
    }

    [TestMethod]
    public void DeliverTurnIn_ForceComplete_SendsJournalAnd2070()
    {
        SeedPatrolPlusDeliverSingleObjective();
        var (character, vehicle, map) = CreatePlayer();
        PlaceLiveDeliverNpc(map);
        character.CurrentQuests.Add(MakeQuest(0));
        character.SetMap(map);
        vehicle.SetMap(map);

        Action pending = null;
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 50;
        MissionClientSoftPedal.GroupReactionSuppressMs = 100;
        NpcInteractHandler.ScheduleDelayedWork = (a, d, _) =>
        {
            Assert.AreEqual(100, d);
            pending = a;
        };

        NpcInteractHandler.HandleMissionDialogResponse(character.OwningConnection,
            new MissionDialogResponsePacket
            {
                MissionId = MissionId,
                Accepted = true,
                MissionGiver = new TFID(MapNpcIdentity.CoidBase + 50, false),
            });

        Assert.IsNotNull(pending);
        _sent.Clear();
        pending();
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void ReactionCreate_Personal_SpawnsDialogNpcChild()
    {
        AssetManagerTestHelper.RegisterCreatureCloneBase(DeliverCbid, maxHitPoint: 50);
        var cb = AssetManager.Instance.GetCloneBase(DeliverCbid) as AutoCore.Game.CloneBases.CloneBaseCreature;
        if (cb != null)
            cb.CreatureSpecific.IsNPC = 1;

        var map = SectorMap.CreateForTests(new ContinentObject
        {
            Id = ContId + 1,
            MapFileName = "tm_rx",
            DisplayName = "t",
            IsPersistent = true,
        }, new Vector4());

        map.MapData.Templates[PadSpawn] = MakeSpawn(PadSpawn, false, DeliverCbid);
        var spawn = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[PadSpawn]);
        spawn.SetCoid(PadSpawn, false);
        spawn.SetMap(map);

        var rxTpl = new ReactionTemplate
        {
            COID = (int)CreatePadRx,
            ReactionType = ReactionType.Create,
        };
        rxTpl.Objects.Add(PadSpawn);
        var rx = new Reaction(rxTpl);
        rx.SetCoid(CreatePadRx, false);
        rx.SetMap(map);

        var (character, vehicle, _) = CreatePlayer();
        character.SetMap(map);
        vehicle.SetMap(map);

        Assert.IsTrue(rx.TriggerIfPossible(vehicle));
        // Spawn may succeed if clonebase registered
        Assert.IsTrue(character.MapPresence.IsMaterialized(PadSpawn)
            || spawn.HasLiveSpawn());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void SeedFinalExamShape()
    {
        var kill = MissionObjective.CreateForTests(KillObjId, 0, MissionId, 1);
        kill.Requirements.Add(new ObjectiveRequirementKill(kill)
        {
            TargetCBID = KillTemplateId,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });
        var del = MissionObjective.CreateForTests(DeliverObjId, 1, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(del)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
        };
        patrol.GenericTargets[0] = Waypoint;
        patrol.TargetCount = 1;
        del.Requirements.Add(patrol);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var m = Mission.CreateForTests(MissionId, kill, del);
        m.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(m);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private void SeedDeliverOnly()
    {
        var del = MissionObjective.CreateForTests(DeliverObjId, 0, MissionId, 1);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var m = Mission.CreateForTests(MissionId, del);
        m.NPC = GiverCbid;
        AssetManager.Instance.SetTestMission(m);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private void SeedPatrolPlusDeliverSingleObjective()
    {
        var del = MissionObjective.CreateForTests(DeliverObjId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(del) { AutoComplete = true, AutoCompleteDistance = 25 };
        patrol.GenericTargets[0] = Waypoint;
        patrol.TargetCount = 1;
        del.Requirements.Add(patrol);
        del.Requirements.Add(new ObjectiveRequirementDeliver(del)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        var m = Mission.CreateForTests(MissionId, del);
        m.NPC = DeliverCbid;
        AssetManager.Instance.SetTestMission(m);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private static CharacterQuest MakeQuest(byte seq)
    {
        var q = new CharacterQuest(MissionId, seq);
        q.PopulateFromAssets();
        return q;
    }

    private static SpawnPointTemplate MakeSpawn(long coid, bool active, int spawnType)
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)coid,
            IsActive = active,
            OriginalIsActive = active,
            CBID = 0,
            Location = new Vector4(0, 0, 0, 0),
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = spawnType,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        return tpl;
    }

    private void SeedPadGraph(SectorMap map)
    {
        map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, GiverCbid);
        map.MapData.Templates[PadSpawn] = MakeSpawn(PadSpawn, false, DeliverCbid);
        var rxTpl = new ReactionTemplate
        {
            COID = (int)CreatePadRx,
            ReactionType = ReactionType.Create,
        };
        rxTpl.Objects.Add(PadSpawn);
        var rx = new Reaction(rxTpl);
        rx.SetCoid(CreatePadRx, false);
        rx.SetMap(map);
        var pad = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[PadSpawn]);
        pad.SetCoid(PadSpawn, false);
        pad.SetMap(map);
    }

    private void PlaceDialog(SectorMap map, int cbid)
    {
        if (map.GetObjectByCoid(DialogSpawn) is not SpawnPoint)
        {
            if (!map.MapData.Templates.ContainsKey(DialogSpawn))
                map.MapData.Templates[DialogSpawn] = MakeSpawn(DialogSpawn, true, cbid);
            var sp = new SpawnPoint((SpawnPointTemplate)map.MapData.Templates[DialogSpawn]);
            sp.SetCoid(DialogSpawn, false);
            sp.SetMap(map);
            sp.SetLastSpawnedCoidForTests(DialogCreature);
        }
        var c = new Creature();
        c.SetCoid(DialogCreature, true);
        c.SetCbidForTests(cbid);
        c.SpawnOwner = DialogSpawn;
        c.IsMissionGiver = true;
        c.SetMap(map);
    }

    private void PlaceLiveDeliverNpc(SectorMap map)
    {
        var npc = new Creature { Position = new Vector3(0, 0, 0) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 50, true);
        npc.LoadCloneBase(DeliverCbid);
        npc.SetupCBFields();
        npc.IsMissionGiver = true;
        npc.SetMap(map);
    }

    private void PlaceWaypoint(SectorMap map)
    {
        var wp = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        wp.SetCoid(Waypoint, false);
        wp.Position = new Vector3(0, 0, 0);
        wp.SetMap(map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_heavy_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.ActivateGhosting();
        var character = new Character();
        character.SetCoid(1800, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        var vehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        vehicle.SetCoid(1801, true);
        character.SetCurrentVehicleForTests(vehicle);
        return (character, vehicle, map);
    }
}

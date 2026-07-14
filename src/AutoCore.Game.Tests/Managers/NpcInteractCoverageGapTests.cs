using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
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
using AutoCore.Game.Tests.Inventory;
using AutoCore.Game.TNL;

/// <summary>
/// Branch coverage for NpcInteractHandler incomplete-log and edge paths.
/// Synthetic ids only.
/// </summary>
[TestClass]
public class NpcInteractCoverageGapTests
{
    private const int MissionId = 91400;
    private const int ObjA = 92400;
    private const int ObjB = 92401;
    private const int NpcCbid = 93400;
    private const long NpcCoid = 94400;
    private const long Wp1 = 95400;
    private const long Wp2 = 95401;
    private const int ContId = 707;

    private readonly List<BasePacket> _sent = new();
    private readonly List<string> _incomplete = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        _incomplete.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        IncompleteHandlerLog.TestSink = msg => _incomplete.Add(msg);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        IncompleteHandlerLog.TestSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        _sent.Clear();
        _incomplete.Clear();
    }

    [TestMethod]
    public void AdvanceOrComplete_MultiRequirementAndCompleteCount_StillAdvances()
    {
        var o1 = MissionObjective.CreateForTests(ObjA, 0, MissionId, completeCount: 3);
        o1.Requirements.Add(new ObjectiveRequirementPatrol(o1) { AutoComplete = true, FirstStateSlot = 0 });
        o1.Requirements.Add(new ObjectiveRequirementKill(o1) { NumToKill = 1, TargetCBID = 1 });
        // Force XP so reward incomplete path logs
        // XP is private set — use reflection-free: CreateForTests doesn't set XP; incomplete path checks objective.XP
        var o2 = MissionObjective.CreateForTests(ObjB, 1, MissionId, 1);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, o1, o2));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Wp1, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        // Manually seed patrol target into first requirement
        ((ObjectiveRequirementPatrol)o1.Requirements[0]).GenericTargets[0] = Wp1;
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Wp1, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjB));
    }

    [TestMethod]
    public void AutoPatrol_MultiWaypointSequentialLaps_CompletesAndLogsIncomplete()
    {
        var o1 = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(o1)
        {
            AutoComplete = true,
            AutoCompleteDistance = 50f,
            AutoFail = true,
            AutoFailDistance = 200f,
            ContinentId = ContId,
            Laps = 2,
            Sequential = true,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = Wp1;
        patrol.GenericTargets[1] = Wp2;
        o1.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, o1));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Wp1, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Wp1, false),
        });

        // HEAD behavior: any listed pad completes the objective (known incomplete multi-pad).
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_incomplete.Any(m => m.Contains("AutoFail")));
        Assert.IsTrue(_incomplete.Any(m => m.Contains("ContinentCBID")));
    }

    [TestMethod]
    public void DeliverTurnIn_WithLaterObjectives_AdvancesSequence_KeepsQuest()
    {
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var later = MissionObjective.CreateForTests(ObjB, 1, MissionId, 1);
        var mission = Mission.CreateForTests(MissionId, dObj, later);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "mid-sequence deliver must keep the quest");
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId),
            "must not mark complete when later sequences exist");
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count(),
            "dialog turn-in must not send immediate 0x2070");
    }

    [TestMethod]
    public void DeliverTurnIn_MidSeq_WithCargo_TakesFinishAndGrantsNext()
    {
        const int finishCbid = 50101;
        const int nextCbid = 50102;

        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            ItemCBID = finishCbid,
            NumToDeliver = 1,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var later = MissionObjective.CreateForTests(ObjB, 1, MissionId, 1);
        later.Requirements.Add(new ObjectiveRequirementDeliver(later)
        {
            ItemCBID = nextCbid,
            NumToDeliver = 1,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
            NPCTargetCBID = NpcCbid + 1,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj, later);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        var harness = new InventoryTestHarness();
        character.AttachInventoryForTests(harness.Inventory);
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        // Seed finish cargo (as if GiveItemOnStart already ran).
        var granted = harness.Inventory.GrantMissionCargoItem(
            finishCbid,
            CloneBaseObjectType.Item,
            "Finish Item",
            coid: 70_001,
            characterCoid: character.ObjectId.Coid,
            quantity: 1,
            itemCreator: null);
        Assert.IsNotNull(granted.AddedItem);
        Assert.AreEqual(1, harness.Inventory.CountByCbid(finishCbid));

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(0, harness.Inventory.CountByCbid(finishCbid), "TakeItemAtEnd for finished objective");
        Assert.AreEqual(1, harness.Inventory.CountByCbid(nextCbid), "GiveItemOnStart for next objective");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void DeliverTurnIn_AfterAdvance_SecondDialogDoesNotCompleteMission()
    {
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        // Next objective is kill-only — same NPC dialog must not force-complete the mission.
        var later = MissionObjective.CreateForTests(ObjB, 1, MissionId, 1);
        later.Requirements.Add(new ObjectiveRequirementKill(later) { NumToKill = 3, TargetCBID = 9 });
        var mission = Mission.CreateForTests(MissionId, dObj, later);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        var packet = new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        };

        NpcInteractHandler.HandleMissionDialogResponse(conn, packet);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);

        NpcInteractHandler.HandleMissionDialogResponse(conn, packet);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void UseItem_MatchesTemplateCbidWithoutLiveObject()
    {
        const int cbid = 4402;
        const long templateCoid = 96001;

        var o1 = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        o1.Requirements.Add(new ObjectiveRequirementUseItem(o1)
        {
            PrimaryItem = -1,
            PrimaryCBID = cbid,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, o1));

        var (conn, character, map) = CreatePlayer();
        map.MapData.Templates[templateCoid] = new GraphicsObjectTemplate(GraphicsObjectType.Graphics)
        {
            CBID = cbid,
            Location = new Vector4(0, 0, 0, 0),
        };
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(templateCoid, false),
            ObjectiveId = -1,
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void PrepareTurnIn_HighStateSlot_StillSendsObjectiveStateWithBitmask()
    {
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 99, // >= SlotCount — float skipped, bit still set
        });
        var mission = Mission.CreateForTests(MissionId, dObj);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        Assert.IsTrue(_sent.OfType<NpcMissionDialogPacket>().Any());
        var state = _sent.OfType<ObjectiveStatePacket>().SingleOrDefault(p => p.ObjectiveId == ObjA);
        Assert.IsNotNull(state, "turn-in prep must still notify client requirement callbacks");
        Assert.AreEqual(1u, state.ObjectiveBitmask);
    }

    [TestMethod]
    public void PrepareTurnIn_SetsBitmaskAndCompleteSlot()
    {
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var state = _sent.OfType<ObjectiveStatePacket>().Single(p => p.ObjectiveId == ObjA);
        Assert.AreEqual(1u, state.ObjectiveBitmask);
        Assert.AreEqual(1.0f, state.SlotProgress[0], 0.001f);
    }

    [TestMethod]
    public void AdvanceOrComplete_SendsNextObjectiveState_WithRequirementBitmask()
    {
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var later = MissionObjective.CreateForTests(ObjB, 1, MissionId, 1);
        later.Requirements.Add(new ObjectiveRequirementPatrol(later)
        {
            AutoComplete = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj, later);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        var nextState = _sent.OfType<ObjectiveStatePacket>().SingleOrDefault(p => p.ObjectiveId == ObjB);
        Assert.IsNotNull(nextState);
        Assert.AreNotEqual(0u, nextState.ObjectiveBitmask,
            "next objective after deliver advance must set requirement change bits");
        Assert.AreEqual(0f, nextState.SlotProgress[0], 0.001f);
    }

    [TestMethod]
    public void UseObject_DeliverTurnIn_NoMissionRewards_DialogItemSlotsEmpty()
    {
        // Track This class: mission.Item all -1. Deliver cargo must NOT fill dialog reward slots
        // or client Client_MissionDialogHandleButton demands "select a reward first".
        const int itemCbid = 50211;
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            ItemCBID = itemCbid,
            NumToDeliver = 1,
            GiveItemOnStart = true,
            TakeItemAtEnd = true,
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj);
        mission.NPC = NpcCbid + 50;
        mission.Item = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        var harness = new InventoryTestHarness();
        character.AttachInventoryForTests(harness.Inventory);
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        harness.Inventory.GrantMissionCargoItem(
            itemCbid,
            CloneBaseObjectType.Item,
            "Deliver Item",
            coid: 70_201,
            characterCoid: character.ObjectId.Coid,
            quantity: 1,
            itemCreator: null);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, MissionId);
        Assert.IsTrue(dialog.MissionItemCoids.Count >= 1);
        Assert.AreEqual(-1, dialog.MissionItemCoids[0][0],
            "no selectable mission rewards → empty reward slots (not deliver cargo)");
        Assert.IsTrue(
            _sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjA && p.ObjectiveBitmask != 0),
            "turn-in readiness still via ObjectiveState, not reward item slots");
    }

    [TestMethod]
    public void UseObject_MissionWithRewardItems_DialogListsRewardCoids()
    {
        const int rewardCbid = 50212;
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj);
        mission.NPC = NpcCbid;
        mission.Item = new[] { rewardCbid, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        var harness = new InventoryTestHarness();
        character.AttachInventoryForTests(harness.Inventory);
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        harness.Inventory.GrantMissionCargoItem(
            rewardCbid,
            CloneBaseObjectType.Item,
            "Reward",
            coid: 70_301,
            characterCoid: character.ObjectId.Coid,
            quantity: 1,
            itemCreator: null);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().Single();
        Assert.AreEqual(70_301, dialog.MissionItemCoids[0][0]);
    }

    [TestMethod]
    public void DialogResponse_AtDeliverNpc_DoesNotFallThroughToAlreadyActiveResync()
    {
        // Repro: talking to turn-in NPC must complete deliver, not hit "already active" status path
        // (that path shows giver NotCompleteText / offer-style chrome).
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj);
        mission.NPC = NpcCbid + 99; // distinct giver
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId),
            "deliver response at turn-in NPC must complete, not status-resync");
        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void DialogResponse_AtGiverWhileDeliverActiveElsewhere_StatusOnly_NoComplete()
    {
        const int giverCbid = 93500;
        const long giverCoid = 94500;
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid, // turn-in is Kid Gareth class
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, dObj);
        mission.NPC = giverCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, giverCoid, giverCbid, new Vector3(1, 0, 0));
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(50, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(giverCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "giver status dialog must not complete deliver");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void DeliverChain_GiveNoTake_ThenLaterTake_CompletesAndRemovesCargo()
    {
        // Track This shape: deliver (give, no take) → middle → deliver (no give, take).
        const int itemCbid = 50201;
        const int midObj = 92402;

        var d0 = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        d0.Requirements.Add(new ObjectiveRequirementDeliver(d0)
        {
            ItemCBID = itemCbid,
            NumToDeliver = 1,
            GiveItemOnStart = true,
            TakeItemAtEnd = false,
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mid = MissionObjective.CreateForTests(midObj, 1, MissionId, 1);
        mid.Requirements.Add(new ObjectiveRequirementKill(mid)
        {
            NumToKill = 1,
            TargetCBID = 7,
            FirstStateSlot = 0,
        });
        var d2 = MissionObjective.CreateForTests(ObjB, 2, MissionId, 1);
        d2.Requirements.Add(new ObjectiveRequirementDeliver(d2)
        {
            ItemCBID = itemCbid,
            NumToDeliver = 1,
            GiveItemOnStart = false,
            TakeItemAtEnd = true,
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        var mission = Mission.CreateForTests(MissionId, d0, mid, d2);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        var harness = new InventoryTestHarness();
        character.AttachInventoryForTests(harness.Inventory);
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        harness.Inventory.GrantMissionCargoItem(
            itemCbid,
            CloneBaseObjectType.Item,
            "GPS Unit",
            coid: 70_101,
            characterCoid: character.ObjectId.Coid,
            quantity: 1,
            itemCreator: null);
        Assert.AreEqual(1, harness.Inventory.CountByCbid(itemCbid));

        // First dialog: advance only — cargo stays (TakeItemAtEnd=0).
        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, harness.Inventory.CountByCbid(itemCbid), "first deliver must keep cargo");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));

        // Complete middle kill objective.
        NpcInteractHandler.AdvanceOrCompleteObjective(
            conn,
            character,
            character.CurrentQuests[0],
            mission,
            mid,
            source: "Kill");
        Assert.AreEqual(2, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, harness.Inventory.CountByCbid(itemCbid));

        // Final dialog: take cargo + complete mission.
        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });
        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, harness.Inventory.CountByCbid(itemCbid), "final deliver TakeItemAtEnd removes cargo");
    }

    [TestMethod]
    public void GrantMission_AlreadyPresent_IsNoOp()
    {
        var o1 = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        var mission = Mission.CreateForTests(MissionId, o1);
        mission.NPC = NpcCbid;
        mission.Continent = ContId;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);
        _sent.Clear();

        // Accept offer for same mission while already active — CanOffer should block grant
        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void DialogResponse_ObjectiveIdEcho_WithoutNpcStillMappedWhenDeliverKnown()
    {
        // npc not on map → cbid 0; deliver still completes via Resolve when mission id matches
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, dObj));

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(1, 0, 0));
        GiveQuest(character, MissionId);

        // Dialog with known mission id (not objective) completes via deliver
        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void AutoPatrol_TargetCountSet_UsesListedTargetsOnly()
    {
        // UnSerialize path sets TargetCount; simulate via full XML create
        var o1 = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        var xml = System.Xml.Linq.XElement.Parse($@"
            <Requirement type=""patrol"" slot=""0"">
              <AutoComplete>1</AutoComplete>
              <AutoCompleteDistance>50</AutoCompleteDistance>
              <GenericTargetCOID>{Wp1}</GenericTargetCOID>
            </Requirement>");
        var patrol = (ObjectiveRequirementPatrol)ObjectiveRequirement.Create(o1, xml);
        o1.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, o1));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, Wp1, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(Wp1, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(1, patrol.TargetCount);
    }

    private static void GiveQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void PlaceNpc(SectorMap map, long coid, int cbid, Vector3 position)
    {
        var npc = new Creature();
        npc.SetCoid(coid, false);
        npc.SetCbidForTests(cbid);
        npc.Position = position;
        npc.SetMap(map);
    }

    private static void PlaceWaypoint(SectorMap map, long coid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = position;
        obj.SetMap(map);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_mission_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (connection, character, map);
    }
}

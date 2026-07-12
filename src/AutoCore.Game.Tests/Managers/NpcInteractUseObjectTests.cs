using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// UseObject / NPC dialog / deliver turn-in / offer accept.
/// Uses only synthetic mission/NPC ids — not retail content ids.
/// </summary>
[TestClass]
public class NpcInteractUseObjectTests
{
    // Synthetic fixtures only (must not match known retail tutorial chain ids).
    private const int MissionA = 91001;
    private const int MissionB = 91002;
    private const int ObjectiveA = 92001;
    private const int ObjectiveB = 92002;
    private const int NpcCbid = 93001;
    private const int OtherNpcCbid = 93002;
    private const long NpcCoid = 94001;
    private const int ContinentId = 707;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        // delay≤0 runs journal/re-eval synchronously (production default is 100ms).
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void HandleUseObject_NullGuards_NoThrow()
    {
        NpcInteractHandler.HandleUseObject(null, new UseObjectPacket());
        var (conn, _, _) = CreatePlayer();
        NpcInteractHandler.HandleUseObject(conn, null);
        conn.CurrentCharacter = null;
        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket { Target = new TFID(1, false) });
    }

    [TestMethod]
    public void HandleUseObject_InvalidTargetCoid_DoesNotSend()
    {
        var (conn, _, _) = CreatePlayer();
        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(0, false),
            ObjectiveId = -1,
        });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleUseObject_MissingObject_DoesNotSend()
    {
        var (conn, character, _) = CreatePlayer();
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(999999, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count());
    }

    [TestMethod]
    public void HandleUseObject_NonCreatureTarget_DoesNotSend()
    {
        var (conn, character, map) = CreatePlayer();
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(NpcCoid, false);
        obj.SetCbidForTests(NpcCbid);
        obj.Position = new Vector3(1, 0, 0);
        obj.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count());
    }

    [TestMethod]
    public void HandleUseObject_DeliverNpcInRange_SendsNpcMissionDialog()
    {
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveA,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog);
        Assert.AreEqual(NpcCoid, dialog.NpcTfid.Coid);
        CollectionAssert.Contains(dialog.MissionIds, MissionA);
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any());
    }

    [TestMethod]
    public void HandleUseObject_OutOfRange_DoesNotSendDialog()
    {
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(100f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveA,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count());
    }

    [TestMethod]
    public void HandleUseObject_DeliverTargetsDifferentNpc_DoesNotSendDialog()
    {
        SeedDeliverMission(MissionA, ObjectiveA, npcTargetCbid: OtherNpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveA,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count());
    }

    [TestMethod]
    public void HandleUseObject_InProgressFromGiver_OpensStatusDialog()
    {
        // Active quest from this NPC, but not a deliver-to-this-NPC objective.
        SeedOfferMission(MissionA, NpcCbid, continentId: ContinentId, objectiveId: ObjectiveA);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, MissionA);
    }

    [TestMethod]
    public void HandleUseObject_InProgressDeliverTargetNpc_NotYetActiveSeq_OpensStatusDialog()
    {
        // Giver is OtherNpc; later objective delivers to clicked NPC. Active seq is still 0 (non-deliver).
        SeedPatrolThenDeliverMission(
            MissionA,
            giverNpcCbid: OtherNpcCbid,
            patrolObjectiveId: ObjectiveA,
            deliverObjectiveId: ObjectiveB,
            deliverNpcCbid: NpcCbid);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog, "Deliver-target NPC should open status dialog before deliver seq is active");
        CollectionAssert.Contains(dialog.MissionIds, MissionA);
    }

    [TestMethod]
    public void HandleUseObject_ObjectiveIdHint_IncludesOwnedMissionForDeliverNpc()
    {
        // Still on patrol seq; client sends deliver objective id (Gunny-style UseObject hint).
        SeedPatrolThenDeliverMission(
            MissionA,
            giverNpcCbid: OtherNpcCbid,
            patrolObjectiveId: ObjectiveA,
            deliverObjectiveId: ObjectiveB,
            deliverNpcCbid: NpcCbid);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveB,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog, "Owned mission + objectiveId deliver hint should open dialog");
        CollectionAssert.Contains(dialog.MissionIds, MissionA);
    }

    [TestMethod]
    public void HandleUseObject_ObjectiveIdHint_UnknownOrUnowned_DoesNotInventDialog()
    {
        SeedPatrolThenDeliverMission(
            MissionA,
            giverNpcCbid: OtherNpcCbid,
            patrolObjectiveId: ObjectiveA,
            deliverObjectiveId: ObjectiveB,
            deliverNpcCbid: NpcCbid);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        // Player does not have MissionA.
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveB,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count(),
            "Must not invent dialog for missions the character does not have");
    }

    [TestMethod]
    public void HandleUseObject_ClientAheadDeliverHint_SyncsActiveSequence()
    {
        // Client already on deliver objective; server still on patrol (Guns of the Expansion desync).
        SeedPatrolThenDeliverMission(
            MissionA,
            giverNpcCbid: OtherNpcCbid,
            patrolObjectiveId: ObjectiveA,
            deliverObjectiveId: ObjectiveB,
            deliverNpcCbid: NpcCbid);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveB,
        });

        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence,
            "Client-ahead deliver objective hint must forward-sync server sequence");
        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, MissionA);
        Assert.IsTrue(
            _sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveB),
            "After reconcile, deliver objective should get ObjectiveState for turn-in prep");
    }

    [TestMethod]
    public void HandleMissionDialogResponse_AfterClientAheadHint_CompletesDeliver()
    {
        // Live log repro: UseObject objectiveId=deliver, then MissionDialogResponse missionId + accepted=false.
        SeedPatrolThenDeliverMission(
            MissionA,
            giverNpcCbid: OtherNpcCbid,
            patrolObjectiveId: ObjectiveA,
            deliverObjectiveId: ObjectiveB,
            deliverNpcCbid: NpcCbid);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveB,
        });
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionA,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count, "Deliver turn-in must complete after client-ahead reconcile");
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionA));
        // Client already ran CompleteObjective on dialog button; 0x2070 would force-complete again
        // and rebuild UI (client AV @ 0x007B6DB0 MSXML Release during re-entrant interface load).
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void HandleUseObject_UnrelatedObjectiveHint_DoesNotAdvanceSequence()
    {
        SeedPatrolThenDeliverMission(
            MissionA,
            giverNpcCbid: OtherNpcCbid,
            patrolObjectiveId: ObjectiveA,
            deliverObjectiveId: ObjectiveB,
            deliverNpcCbid: NpcCbid);
        // Deliver target is OtherNpc, not the clicked NPC.
        SeedDeliverMission(MissionB, ObjectiveB + 100, OtherNpcCbid);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);
        // Owned mission B but objective for B's deliver is for OtherNpc — clicked NpcCbid.
        GiveQuest(character, MissionB);
        Assert.AreEqual(0, character.CurrentQuests.First(q => q.MissionId == MissionB).ActiveObjectiveSequence);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = ObjectiveB + 100,
        });

        Assert.AreEqual(0, character.CurrentQuests.First(q => q.MissionId == MissionB).ActiveObjectiveSequence,
            "Must not advance sequence for objective unrelated to clicked NPC");
        Assert.AreEqual(0, character.CurrentQuests.First(q => q.MissionId == MissionA).ActiveObjectiveSequence);
    }

    [TestMethod]
    public void HandleUseObject_AfterPrereqComplete_OffersNextMission()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        character.CompletedMissionIds.Add(MissionA);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, MissionB);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_DeliverTurnIn_CompletesAndTracksHistory()
    {
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionA,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionA));
        // Dialog turn-in: journal resync only — no 0x2070 (client already completed locally).
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void HandleMissionDialogResponse_ObjectiveIdEcho_RemapsToDeliverMission()
    {
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = ObjectiveA,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionA));
    }

    [TestMethod]
    public void HandleMissionDialogResponse_MissionIdZero_InfersDeliverFromNpc()
    {
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = 0,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionA));
    }

    [TestMethod]
    public void HandleMissionDialogResponse_TurnIn_DoesNotOpenImmediateFollowUpDialog()
    {
        // Immediate 0x206D after dialog close reloads many interface XMLs while the client still
        // tears down mission UI → random AV in MSXML COM Release (0x007B6DB0). Follow-ups open
        // on the next UseObject (see HandleUseObject_AfterPrereqComplete_OffersNextMission).
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        GiveQuest(character, MissionA);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionA,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionA));
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
        Assert.IsFalse(
            _sent.OfType<NpcMissionDialogPacket>().Any(d => d.MissionIds.Contains(MissionB)),
            "Must not auto-open follow-up offer dialog on the same turn-in flush");
    }

    [TestMethod]
    public void HandleMissionDialogResponse_TurnIn_DefersJournalUntilScheduledFollowup()
    {
        // Production waits before journal/re-eval so client dialog + interact FX MSXML can settle.
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        GiveQuest(character, MissionA);
        _sent.Clear();

        Action pending = null;
        var seenDelay = -1;
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 250;
        NpcInteractHandler.ScheduleDelayedWork = (action, delayMs, token) =>
        {
            seenDelay = delayMs;
            pending = action;
        };

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionA,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionA), "Server complete is immediate");
        Assert.AreEqual(250, seenDelay);
        Assert.IsNotNull(pending);
        Assert.IsTrue(MissionClientSoftPedal.HasPendingSuppressForTests(character.ObjectId.Coid),
            "GroupReactionCall soft-pedal must arm on dialog turn-in");
        Assert.IsTrue(MissionClientSoftPedal.ShouldSuppressGroupReactionCall(character.ObjectId.Coid));
        Assert.AreEqual(0, _sent.OfType<ConvoyMissionsResponsePacket>().Count(),
            "Journal must wait for delayed follow-up");
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());

        pending();

        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any(),
            "Journal sends after delayed follow-up runs");
    }

    [TestMethod]
    public void HandleMissionDialogResponse_TurnIn_ThenUseObject_OffersFollowUpMission()
    {
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CurrentVehicle.Position = new Vector3(0f, 0f, 0f);
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionA,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        var dialog = _sent.OfType<NpcMissionDialogPacket>().SingleOrDefault();
        Assert.IsNotNull(dialog);
        CollectionAssert.Contains(dialog.MissionIds, MissionB);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_AcceptOffer_GrantsMission()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CompletedMissionIds.Add(MissionA);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionB,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(MissionB, character.CurrentQuests[0].MissionId);
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveB));
    }

    [TestMethod]
    public void HandleMissionDialogResponse_AcceptWithObjectiveId_GrantsParentMission()
    {
        // Retail Final Exam-style accept: dialog packet may echo first objective id (5422)
        // instead of mission id (3037).
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CompletedMissionIds.Add(MissionA);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = ObjectiveB, // objective id, not mission id
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(MissionB, character.CurrentQuests[0].MissionId);
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveB));
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void HandleMissionDialogResponse_AlreadyActive_ResyncsClientJournal()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CompletedMissionIds.Add(MissionA);
        GiveQuest(character, MissionB);
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionB,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(1, character.CurrentQuests.Count, "Must not duplicate the active quest");
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any(),
            "Already-active accept must still push journal so client shows the mission");
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == ObjectiveB));
    }

    [TestMethod]
    public void ResolveMissionIdForGrant_MapsObjectiveIdToMission()
    {
        SeedOfferMission(MissionB, NpcCbid, objectiveId: ObjectiveB);
        Assert.AreEqual(MissionB, NpcInteractHandler.ResolveMissionIdForGrant(ObjectiveB));
        Assert.AreEqual(MissionB, NpcInteractHandler.ResolveMissionIdForGrant(MissionB));
        Assert.AreEqual(99999, NpcInteractHandler.ResolveMissionIdForGrant(99999));
    }

    [TestMethod]
    public void HandleMissionDialogResponse_AcceptWithoutPrereq_DoesNotGrant()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId);

        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        _sent.Clear();

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionB,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_CompletedNonRepeatable_DoesNotReOffer()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        character.CompletedMissionIds.Add(MissionA);
        character.CompletedMissionIds.Add(MissionB);
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        _sent.Clear();

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count());
    }

    [TestMethod]
    public void HandleMissionDialogResponse_WrongNpcForOffer_DoesNotGrant()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: MissionA, continentId: ContinentId, objectiveId: ObjectiveB);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, OtherNpcCbid, new Vector3(5f, 0f, 0f));
        character.CompletedMissionIds.Add(MissionA);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionB,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_LevelTooLow_DoesNotGrant()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: -1, continentId: ContinentId, objectiveId: ObjectiveB, reqLevelMin: 50);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionB,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_WrongContinent_DoesNotGrant()
    {
        SeedOfferMission(MissionB, NpcCbid, reqMissionId: -1, continentId: 999, objectiveId: ObjectiveB);
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionB,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_NullGuards_NoThrow()
    {
        NpcInteractHandler.HandleMissionDialogResponse(null, new MissionDialogResponsePacket());
        var (conn, _, _) = CreatePlayer();
        NpcInteractHandler.HandleMissionDialogResponse(conn, null);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_UnknownNpc_StillCompletesWhenDeliverKnown()
    {
        // MissionGiver coid not on map: npcCbid=0 → deliver path needs cbid match fail,
        // but complete path with mission id still tries HasDeliverTurnIn(npcCbid:0) → false;
        // grant also fails. Verifies no crash.
        SeedDeliverMission(MissionA, ObjectiveA, NpcCbid);
        var (conn, character, _) = CreatePlayer();
        GiveQuest(character, MissionA);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionA,
            Accepted = true,
            MissionGiver = new TFID(888888, false),
        });

        // Without NPC cbid match, deliver does not complete.
        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleUseObject_ZeroCbidNpc_DoesNotSend()
    {
        var (conn, character, map) = CreatePlayer();
        var npc = new Creature();
        npc.SetCoid(NpcCoid, false);
        // No CBID override → CBID -1
        npc.Position = new Vector3(1, 0, 0);
        npc.SetMap(map);
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(NpcCoid, false),
            ObjectiveId = -1,
        });

        Assert.AreEqual(0, _sent.OfType<NpcMissionDialogPacket>().Count());
    }

    private static void GiveQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void SeedDeliverMission(int missionId, int objectiveId, int npcTargetCbid)
    {
        var objective = MissionObjective.CreateForTests(objectiveId, 0, missionId, 1);
        var deliver = new ObjectiveRequirementDeliver(objective)
        {
            NPCTargetCBID = npcTargetCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            NumToDeliver = 0,
            RequireItemToComplete = false,
            ItemCBID = -1,
        };
        objective.Requirements.Add(deliver);

        var mission = Mission.CreateForTests(missionId, objective);
        mission.NPC = npcTargetCbid;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    /// <summary>
    /// Seq 0 = empty/non-deliver objective (e.g. patrol placeholder); seq 1 = deliver to deliverNpcCbid.
    /// Giver NPC may differ from deliver target (retail Guns-of-the-Expansion pattern).
    /// </summary>
    private static void SeedPatrolThenDeliverMission(
        int missionId,
        int giverNpcCbid,
        int patrolObjectiveId,
        int deliverObjectiveId,
        int deliverNpcCbid)
    {
        var patrol = MissionObjective.CreateForTests(patrolObjectiveId, 0, missionId, 1);
        var deliverObj = MissionObjective.CreateForTests(deliverObjectiveId, 1, missionId, 1);
        deliverObj.Requirements.Add(new ObjectiveRequirementDeliver(deliverObj)
        {
            NPCTargetCBID = deliverNpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            NumToDeliver = 0,
            RequireItemToComplete = false,
            ItemCBID = -1,
        });

        var mission = Mission.CreateForTests(missionId, patrol, deliverObj);
        mission.NPC = giverNpcCbid;
        mission.Continent = ContinentId;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    private static void SeedOfferMission(
        int missionId,
        int npcCbid,
        int reqMissionId = -1,
        int continentId = 0,
        int objectiveId = 0,
        int reqLevelMin = 0)
    {
        var objectives = objectiveId > 0
            ? new[] { MissionObjective.CreateForTests(objectiveId, 0, missionId, 1) }
            : Array.Empty<MissionObjective>();

        var mission = Mission.CreateForTests(missionId, objectives);
        mission.NPC = npcCbid;
        mission.Continent = continentId;
        mission.ReqLevelMin = reqLevelMin;
        mission.ReqMissionId = new[] { reqMissionId, -1, -1, -1 };
        mission.IsRepeatable = 0;
        AssetManager.Instance.SetTestMission(mission);
    }

    private static SectorMap CreateMap(int continentId = ContinentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_mission_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var map = CreateMap();
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

    private static void PlaceNpc(SectorMap map, long coid, int cbid, Vector3 position)
    {
        var npc = new Creature();
        npc.SetCoid(coid, false);
        npc.SetCbidForTests(cbid);
        npc.Position = position;
        npc.SetMap(map);
    }
}

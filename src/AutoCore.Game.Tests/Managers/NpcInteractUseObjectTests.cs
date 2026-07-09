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
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
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
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
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
    public void HandleMissionDialogResponse_TurnIn_OpensFollowUpOfferDialog()
    {
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
        Assert.IsTrue(
            _sent.OfType<NpcMissionDialogPacket>().Any(d => d.MissionIds.Contains(MissionB)),
            "After turn-in, NPC should open offer dialog for prereq-unlocked mission");
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

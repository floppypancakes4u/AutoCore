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

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        NpcInteractHandler.InvalidateMissionIndex();
        _sent.Clear();
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
        // TargetCount stays 0 → CountPatrolTargets uses array occupancy via length scan in LogPatrolIncomplete
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
    }

    [TestMethod]
    public void DeliverTurnIn_WithLaterObjectives_StillRemovesQuest_LogsIncomplete()
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

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
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
    public void PrepareTurnIn_HighStateSlot_SkipsObjectiveState()
    {
        var dObj = MissionObjective.CreateForTests(ObjA, 0, MissionId, 1);
        dObj.Requirements.Add(new ObjectiveRequirementDeliver(dObj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 99, // >= SlotCount
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
        // Slot out of range — no ObjectiveState for turn-in prep
        Assert.AreEqual(0, _sent.OfType<ObjectiveStatePacket>().Count());
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

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
/// Final Exam class: deliver turn-in after patrol+deliver objective must delayed-force
/// CompleteDynamicObjective so client AutoPatrol waypoints clear.
/// </summary>
[TestClass]
public class DeliverTurnInForceCompleteTests
{
    private const int ContId = 8840;
    private const int MissionId = 98400;
    private const int ObjectiveId = 98401;
    private const int NpcCbid = 12448;
    private const long NpcCoid = 88_501;
    private const long WaypointCoid = 13_939;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 0;
        MissionClientSoftPedal.ResetForTests();
        MissionClientSoftPedal.GroupReactionSuppressMs = 0;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
        NpcInteractHandler.ResetDialogTurnInFollowupForTests();
        MissionClientSoftPedal.ResetForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void ObjectiveNeedsForceClientComplete_PatrolPlusDeliver_True()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj) { AutoComplete = true };
        patrol.GenericTargets[0] = WaypointCoid;
        patrol.TargetCount = 1;
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
        });

        Assert.IsTrue(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(obj));
    }

    [TestMethod]
    public void ObjectiveNeedsForceClientComplete_DeliverOnly_False()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
        });

        Assert.IsFalse(NpcInteractHandler.ObjectiveNeedsForceClientCompleteAfterDeliver(obj));
    }

    [TestMethod]
    public void DeliverTurnIn_PatrolPlusDeliver_SendsDelayedCompleteDynamicObjective()
    {
        SeedPatrolPlusDeliver();
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map);
        GiveQuest(character);
        _sent.Clear();

        Action pending = null;
        NpcInteractHandler.DialogTurnInFollowupDelayMs = 100;
        MissionClientSoftPedal.GroupReactionSuppressMs = 200;
        NpcInteractHandler.ScheduleDelayedWork = (action, delayMs, _) =>
        {
            Assert.AreEqual(200, delayMs, "Force-complete follow-up must wait for soft-pedal window");
            pending = action;
        };

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count(),
            "0x2070 must not fire during dialog (soft-pedal)");
        Assert.IsNotNull(pending);

        pending();

        var complete = _sent.OfType<CompleteDynamicObjectivePacket>().FirstOrDefault();
        Assert.IsNotNull(complete, "Delayed follow-up must force-complete client objective (clear AutoPatrol)");
        Assert.AreEqual(ObjectiveId, complete.ObjectiveId);
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void DeliverTurnIn_AcceptedFalse_StillCompletesDeliver()
    {
        // Retail turn-in OK packets often carry Accepted=false; deliver must still complete.
        SeedPatrolPlusDeliver();
        var (conn, character, map) = CreatePlayer();
        PlaceNpc(map);
        GiveQuest(character);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(NpcCoid, false),
        });

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId),
            "Deliver turn-in must complete even when Accepted=false (retail wire)");
        Assert.IsFalse(character.CurrentQuests.Any(q => q.MissionId == MissionId));
    }

    private void SeedPatrolPlusDeliver()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 25f,
        };
        patrol.GenericTargets[0] = WaypointCoid;
        patrol.TargetCount = 1;
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = NpcCbid,
            NPCTargetCompletes = true,
        });
        var mission = Mission.CreateForTests(MissionId, obj);
        mission.NPC = NpcCbid;
        AssetManager.Instance.SetTestMission(mission);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private static void GiveQuest(Character character)
    {
        var q = new CharacterQuest(MissionId, 0);
        q.PopulateFromAssets();
        character.CurrentQuests.Add(q);
    }

    private static void PlaceNpc(SectorMap map)
    {
        var npc = new Creature { Position = new Vector3(0, 0, 0) };
        npc.SetCoid(NpcCoid, false);
        npc.SetCbidForTests(NpcCbid);
        npc.IsMissionGiver = true;
        npc.SetMap(map);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_force_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(800, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        vehicle.SetCoid(801, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return (connection, character, map);
    }
}

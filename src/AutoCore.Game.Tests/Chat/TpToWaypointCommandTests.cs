using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

using AutoCore.Database.World.Models;
using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Utils.Logging;

/// <summary>
/// /tptowaypoint — GM-only teleport to the caller's current mission waypoint.
/// </summary>
[TestClass]
public class TpToWaypointCommandTests
{
    private const int MissionId = 88001;
    private const int ObjectiveId = 88011;
    private const long WaypointCoid = 16279;
    private const long WaypointCoidB = 16280;

    private InMemoryLogSink _sink = null!;

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        AssetManager.Instance.ClearTestMissions();
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestMethod]
    public void TpToWaypoint_IsMutatingCommand()
    {
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/tptowaypoint"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/tpToWaypoint"));
        Assert.IsTrue(ChatAdminGate.IsMutatingCommand("/tpwaypoint"));
    }

    [TestMethod]
    public void TpToWaypoint_GmLevel0_Denied_DoesNotMove()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 0);
        SeedPatrolMission(WaypointCoid);
        GiveQuest(character, MissionId);
        PlaceWaypoint(character.Map, WaypointCoid, new Vector3(100f, 5f, 200f));
        vehicle.Position = new Vector3(1f, 0f, 1f);
        character.Position = vehicle.Position;
        _sink.Clear();

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Permission denied");
        Assert.AreEqual(1f, vehicle.Position.X, 0.01f);
        Assert.AreEqual(1f, vehicle.Position.Z, 0.01f);
        Assert.IsTrue(_sink.Records.Any(r => r.EventName == "AdminCommandDenied"));
    }

    [TestMethod]
    public void TpToWaypoint_NoCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No character");
    }

    [TestMethod]
    public void TpToWaypoint_NoVehicle_ReturnsError()
    {
        var character = new Character();
        character.GMLevel = 1;
        character.SetCoid(1, true);

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "vehicle");
    }

    [TestMethod]
    public void TpToWaypoint_NoActiveMission_ReturnsError()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "No active mission");
    }

    [TestMethod]
    public void TpToWaypoint_NoResolvableWaypoint_ReturnsError()
    {
        var (character, _, _) = CreatePlayer(gmLevel: 1);
        // Objective with no patrol targets / WorldPosition / deliver.
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
        GiveQuest(character, MissionId);

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "waypoint");
    }

    [TestMethod]
    public void TpToWaypoint_PatrolPad_TeleportsVehicleAndCharacter()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        SeedPatrolMission(WaypointCoid);
        GiveQuest(character, MissionId);
        var dest = new Vector3(250f, 12f, -80f);
        PlaceWaypoint(character.Map, WaypointCoid, dest);
        vehicle.Position = new Vector3(0f, 0f, 0f);
        character.Position = vehicle.Position;

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        Assert.IsFalse(result.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(dest.X, vehicle.Position.X, 0.01f);
        Assert.AreEqual(dest.Y, vehicle.Position.Y, 0.01f);
        Assert.AreEqual(dest.Z, vehicle.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, character.Position.X, 0.01f);
        Assert.AreEqual(dest.Z, character.Position.Z, 0.01f);
        StringAssert.Contains(result.Message, WaypointCoid.ToString());
        AssertSendsClientSnap(result, character, dest);
    }

    [TestMethod]
    public void TpToWaypoint_Alias_TpWaypoint_Works()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        SeedPatrolMission(WaypointCoid);
        GiveQuest(character, MissionId);
        PlaceWaypoint(character.Map, WaypointCoid, new Vector3(10f, 0f, 20f));

        var result = ChatCommandService.Instance.Execute(character, "/tpwaypoint");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(10f, vehicle.Position.X, 0.01f);
        Assert.AreEqual(20f, vehicle.Position.Z, 0.01f);
        AssertSendsClientSnap(result, character, new Vector3(10f, 0f, 20f));
    }

    [TestMethod]
    public void TpToWaypoint_SequentialPatrol_UsesNextUncompletedPad()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        SeedMultiPadPatrol(WaypointCoid, WaypointCoidB);
        GiveQuest(character, MissionId);
        var quest = character.CurrentQuests[0];
        quest.ObjectiveProgress[0] = 1; // first pad done → next is B
        PlaceWaypoint(character.Map, WaypointCoid, new Vector3(1f, 0f, 1f));
        PlaceWaypoint(character.Map, WaypointCoidB, new Vector3(500f, 0f, 600f));

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(500f, vehicle.Position.X, 0.01f);
        Assert.AreEqual(600f, vehicle.Position.Z, 0.01f);
        StringAssert.Contains(result.Message, WaypointCoidB.ToString());
        AssertSendsClientSnap(result, character, new Vector3(500f, 0f, 600f));
    }

    [TestMethod]
    public void TpToWaypoint_VisualWaypoint_PreferredOverPatrolPad_ForGps()
    {
        // Live bug: GPS compass points at map VisualWaypoint; patrol GenericTarget is a different pad.
        var (character, vehicle, map) = CreatePlayer(gmLevel: 1);
        SeedPatrolMission(WaypointCoid);
        GiveQuest(character, MissionId);

        var gpsPos = new Vector3(900f, 3f, 800f);
        var patrolPos = new Vector3(10f, 0f, 10f);
        PlaceWaypoint(map, WaypointCoid, patrolPos);
        PlaceVisualWaypoint(map, id: 55, gpsPos, ObjectiveId);

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(gpsPos.X, vehicle.Position.X, 0.01f);
        Assert.AreEqual(gpsPos.Z, vehicle.Position.Z, 0.01f);
        StringAssert.Contains(result.Message, "visual");
        AssertSendsClientSnap(result, character, gpsPos);
    }

    [TestMethod]
    public void TpToWaypoint_MissingWorldObject_ReturnsError()
    {
        var (character, vehicle, _) = CreatePlayer(gmLevel: 1);
        SeedPatrolMission(WaypointCoid);
        GiveQuest(character, MissionId);
        // No PlaceWaypoint — position cannot be resolved.
        vehicle.Position = new Vector3(3f, 0f, 3f);

        var result = ChatCommandService.Instance.Execute(character, "/tptowaypoint");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Could not resolve");
        Assert.AreEqual(3f, vehicle.Position.X, 0.01f);
        Assert.AreEqual(0, result.Packets.Count);
    }

    static void AssertSendsClientSnap(ChatCommandExecutionResult result, Character character, Vector3 dest)
    {
        // Living snap is TeleportCharacter 0x8058 → client FUN_00808910 → CVOGReaction_TeleportTarget.
        // Ghost resync only flickers (create packets do not move the local owner). SpecialEvent
        // Respawn is death airlift only.
        Assert.AreEqual(0, result.Packets.OfType<SpecialEventPacket>().Count(),
            "SpecialEvent Respawn must not be used for living GM waypoint teleport");
        Assert.IsNotNull(character.OwningConnection);
        Assert.AreEqual(0, character.OwningConnection.ResyncLocalPlayerAtCurrentPoseCallCountForTests,
            "must not ResetGhosting for waypoint TP (causes disappear/reappear without move)");

        var tp = result.Packets.OfType<TeleportCharacterPacket>().SingleOrDefault();
        Assert.IsNotNull(tp, "must send TeleportCharacter (0x8058) so the client TeleportTarget snaps");
        Assert.AreEqual(dest.X, tp.Position.X, 0.01f);
        Assert.AreEqual(dest.Y, tp.Position.Y, 0.01f);
        Assert.AreEqual(dest.Z, tp.Position.Z, 0.01f);
        Assert.AreEqual(dest.X, character.CurrentVehicle.Position.X, 0.01f);
        Assert.AreEqual(dest.Z, character.CurrentVehicle.Position.Z, 0.01f);
    }

    static void PlaceVisualWaypoint(SectorMap map, int id, Vector3 position, params int[] objectiveIds)
    {
        var wp = VisualWaypoint.CreateForTests(id, position, objectiveIds);
        map.MapData.VisualWaypoints[id] = wp;
    }

    static void SeedPatrolMission(long pad)
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = 1,
            Sequential = true,
            Laps = 1,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = pad;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
    }

    static void SeedMultiPadPatrol(params long[] pads)
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 30f,
            TargetCount = pads.Length,
            Sequential = true,
            Laps = 1,
            FirstStateSlot = 0,
        };
        for (var i = 0; i < pads.Length; i++)
            patrol.GenericTargets[i] = pads[i];
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
    }

    static void GiveQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    static void PlaceWaypoint(SectorMap map, long coid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = position;
        obj.SetMap(map);
    }

    static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer(int gmLevel)
    {
        var continent = new ContinentObject
        {
            Id = 707,
            MapFileName = "tm_tp_waypoint_test",
            DisplayName = "tp-test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        // Unit tests have no clonebase-backed create payload; only exercise the resync call path.
        connection.SuppressCreatePacketsForTests = true;

        var character = new Character();
        character.SetCoid(91001, true);
        character.GMLevel = (byte)gmLevel;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(91002, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = new Vector3(0, 0, 0);
        return (character, vehicle, map);
    }
}

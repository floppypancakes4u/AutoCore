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
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// IncompleteHandlerLog is emitted when partial mission/reaction handlers hit ungenericized cases.
/// </summary>
[TestClass]
public class IncompleteHandlerLogTests
{
    private const int MissionId = 91300;
    private const int ObjectiveId = 92300;
    private const long WaypointA = 98101;
    private const long WaypointB = 98102;
    private const int ContId = 707;

    private readonly List<string> _incomplete = new();
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _incomplete.Clear();
        _sent.Clear();
        IncompleteHandlerLog.TestSink = msg => _incomplete.Add(msg);
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        IncompleteHandlerLog.TestSink = null;
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _incomplete.Clear();
        _sent.Clear();
    }

    [TestMethod]
    public void Warn_FormatsPrefixHandlerGapAndTodo()
    {
        IncompleteHandlerLog.Warn("UnitTest", "ctx=1", "does not X", "Implement Y");

        Assert.AreEqual(1, _incomplete.Count);
        StringAssert.Contains(_incomplete[0], IncompleteHandlerLog.Prefix);
        StringAssert.Contains(_incomplete[0], "[UnitTest]");
        StringAssert.Contains(_incomplete[0], "ctx=1");
        StringAssert.Contains(_incomplete[0], "gap: does not X");
        StringAssert.Contains(_incomplete[0], "TODO: Implement Y");
    }

    [TestMethod]
    public void AutoPatrol_MultiTarget_LogsIncompleteMultiWaypoint()
    {
        SeedMultiTargetPatrol();
        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointA, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointA, false),
        });

        Assert.IsTrue(
            _incomplete.Any(m => m.Contains("[AutoPatrol]") && m.Contains("Multi-waypoint")),
            "Expected multi-waypoint incomplete log, got: " + string.Join(" | ", _incomplete));
        // Single patrol requirement: AdvanceOrCompleteObjective only logs multi-req / CompleteCount gaps.
        // Multi-waypoint progress gaps are owned by the AutoPatrol incomplete logs above.
    }

    [TestMethod]
    public void AutoPatrol_LapsGreaterThanOne_LogsIncompleteLaps()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 50f,
            Laps = 3,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = WaypointA;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        PlaceWaypoint(map, WaypointA, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character);

        NpcInteractHandler.HandleAutoPatrol(conn, new AutoPatrolPacket
        {
            Target = new TFID(WaypointA, false),
        });

        Assert.IsTrue(
            _incomplete.Any(m => m.Contains("[AutoPatrol]") && m.Contains("Laps=3")),
            "Expected Laps incomplete log, got: " + string.Join(" | ", _incomplete));
    }

    [TestMethod]
    public void Reaction_Create_NoLongerLogsIncomplete()
    {
        // Missing template COIDs are client-only no-ops; handler is implemented.
        var template = new ReactionTemplate
        {
            Name = "test_create",
            ReactionType = ReactionType.Create,
            COID = 16275,
        };
        template.Objects.Add(1001);
        template.Objects.Add(1002);
        var reaction = new Reaction(template);

        Assert.IsTrue(reaction.TriggerIfPossible(CreateActivator()));

        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.Create]")),
            "Create is implemented — unexpected incomplete: " + string.Join(" | ", _incomplete));
    }

    [TestMethod]
    public void Reaction_Death_NoLongerLogsIncomplete()
    {
        // Missing object is client-only no-op; handler is implemented.
        var template = new ReactionTemplate
        {
            Name = "test_death",
            ReactionType = ReactionType.Death,
            COID = 14120,
        };
        template.Objects.Add(9172);
        var reaction = new Reaction(template);

        Assert.IsTrue(reaction.TriggerIfPossible(CreateActivator()));

        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.Death]")),
            "Death is implemented — unexpected incomplete: " + string.Join(" | ", _incomplete));
    }

    [TestMethod]
    public void Reaction_CompleteObjective_NoLongerLogsIncompleteStub()
    {
        // CompleteObjective is implemented via AdvanceOrCompleteObjective (GenericVar1 = objective id).
        // Without a matching active quest it no-ops cleanly — no INCOMPLETE stub.
        var template = new ReactionTemplate
        {
            Name = "test_complete",
            ReactionType = ReactionType.CompleteObjective,
            COID = 999,
            GenericVar1 = 5535,
        };
        var reaction = new Reaction(template);

        Assert.IsFalse(reaction.TriggerIfPossible(CreateActivator()),
            "No active quest for objective → false (suppress 0x206C)");

        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.CompleteObjective]")),
            "CompleteObjective is implemented — unexpected incomplete: " + string.Join(" | ", _incomplete));
    }

    private void SeedMultiTargetPatrol()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            AutoComplete = true,
            AutoCompleteDistance = 50f,
            Sequential = true,
            FirstStateSlot = 0,
        };
        patrol.GenericTargets[0] = WaypointA;
        patrol.GenericTargets[1] = WaypointB;
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
    }

    private static void GiveQuest(Character character)
    {
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void PlaceWaypoint(SectorMap map, long coid, Vector3 position)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = position;
        obj.SetMap(map);
    }

    private ClonedObjectBase CreateActivator()
    {
        var (_, character, _) = CreatePlayer();
        return character.CurrentVehicle;
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_incomplete_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(250, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(251, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);

        return (connection, character, map);
    }
}

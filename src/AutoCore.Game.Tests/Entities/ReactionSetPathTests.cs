using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Server authority for ReactionType.SetPath (43) / SetPatrolDistance (44): NPC path
/// assignment and patrol distance, both applied via GroupReactionCall 0x206C.
/// </summary>
[TestClass]
public class ReactionSetPathTests
{
    private const int ContId = 831;
    private const long ReactionCoid = 41001;
    private const long NpcVehicleCoid = 41002;
    private const long PathCoid = 41010;
    private const int PatrolDistanceVarId = 500;

    private readonly List<string> _incomplete = new();

    [TestInitialize]
    public void SetUp()
    {
        _incomplete.Clear();
        IncompleteHandlerLog.TestSink = msg => _incomplete.Add(msg);
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        IncompleteHandlerLog.TestSink = null;
        TriggerManager.Instance.ClearAllForTests();
        _incomplete.Clear();
    }

    [TestMethod]
    public void SetPath_ObjectsList_AssignsPathAndReverseFromTemplate()
    {
        var (_, playerVehicle, map) = CreatePlayer();
        SeedMapPath(map, PathCoid, reverseDirection: true);
        var npcVehicle = PlaceNpcVehicle(map, NpcVehicleCoid);

        var reaction = PlaceSetPathReaction(map, objectCoid: NpcVehicleCoid, pathCoid: (int)PathCoid);

        Assert.IsTrue(reaction.TriggerIfPossible(playerVehicle));

        Assert.AreEqual(PathCoid, npcVehicle.CoidCurrentPath);
        Assert.IsTrue(npcVehicle.PathReversing, "PathReversing must come from the resolved MapPathTemplate");
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetPath_ActOnActivator_CharacterResolvesToCurrentVehicle()
    {
        var (character, vehicle, map) = CreatePlayer();
        SeedMapPath(map, PathCoid, reverseDirection: false);

        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "set_path_self",
            ReactionType = ReactionType.SetPath,
            ActOnActivator = true,
            GenericVar1 = (int)PathCoid,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(character));

        Assert.AreEqual(PathCoid, vehicle.CoidCurrentPath, "Character activator must resolve to CurrentVehicle");
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetPath_GenericVarZeroOrNegative_ClearsPath()
    {
        var (_, vehicle, map) = CreatePlayer();
        vehicle.CoidCurrentPath = PathCoid;

        var reaction = PlaceSelfSetPathReaction(map, pathCoid: 0);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(-1L, vehicle.CoidCurrentPath);
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetPath_PreservesPatrolDistance()
    {
        var (_, vehicle, map) = CreatePlayer();
        vehicle.PatrolDistance = 42.5f;
        SeedMapPath(map, PathCoid, reverseDirection: false);

        var reaction = PlaceSelfSetPathReaction(map, pathCoid: (int)PathCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(PathCoid, vehicle.CoidCurrentPath);
        Assert.AreEqual(42.5f, vehicle.PatrolDistance, "SetPath must not touch PatrolDistance");
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetPath_ResetsFollowerIndex()
    {
        var (_, vehicle, map) = CreatePlayer();
        vehicle.NpcAi = new NpcAiState { PathIndex = 5, PathDirection = -1 };
        SeedMapPath(map, PathCoid, reverseDirection: false);

        var reaction = PlaceSelfSetPathReaction(map, pathCoid: (int)PathCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(-1, vehicle.NpcAi.PathIndex, "SetPath must reset follower index");
        Assert.AreEqual(1, vehicle.NpcAi.PathDirection, "SetPath must reset follower direction");
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetPatrolDistance_ReadsLogicVarAndWritesFloat_PreservesPath()
    {
        var (character, vehicle, map) = CreatePlayer();
        vehicle.CoidCurrentPath = PathCoid;
        map.MapData.Variables[PatrolDistanceVarId] =
            Variable.CreateForTests(PatrolDistanceVarId, LogicVariableStore.TypeConstant, 0f, 77.5f, "patrolDist");

        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "set_patrol_distance",
            ReactionType = ReactionType.SetPatrolDistance,
            ActOnActivator = true,
            GenericVar1 = PatrolDistanceVarId,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(77.5f, vehicle.PatrolDistance);
        Assert.AreEqual(PathCoid, vehicle.CoidCurrentPath, "SetPatrolDistance must not touch the path id");
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetPath_UnknownPathCoid_StillAssignsCoid_NoThrow()
    {
        var (_, vehicle, map) = CreatePlayer();
        const int unknownPathCoid = 999999;

        var reaction = PlaceSelfSetPathReaction(map, pathCoid: unknownPathCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreEqual(unknownPathCoid, vehicle.CoidCurrentPath);
        Assert.IsFalse(vehicle.PathReversing);
        AssertNoUnhandledLog();
    }

    private void AssertNoUnhandledLog()
    {
        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.Unhandled]")),
            "Expected no Reaction.Unhandled log, got: " + string.Join(" | ", _incomplete));
    }

    private static void SeedMapPath(SectorMap map, long pathCoid, bool reverseDirection)
    {
        map.MapData.Templates[pathCoid] = new MapPathTemplate
        {
            COID = (int)pathCoid,
            ReverseDirection = reverseDirection,
        };
    }

    private static Vehicle PlaceNpcVehicle(SectorMap map, long coid)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, false);
        vehicle.Position = new Vector3(5f, 0f, 5f);
        vehicle.SetMap(map);
        return vehicle;
    }

    private static Reaction PlaceSetPathReaction(SectorMap map, long objectCoid, int pathCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "set_path",
            ReactionType = ReactionType.SetPath,
            ActOnActivator = false,
            GenericVar1 = pathCoid,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static Reaction PlaceSelfSetPathReaction(SectorMap map, int pathCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "set_path_self",
            ReactionType = ReactionType.SetPath,
            ActOnActivator = true,
            GenericVar1 = pathCoid,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_set_path_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(370, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(371, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}

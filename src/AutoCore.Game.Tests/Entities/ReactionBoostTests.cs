using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Boost (24) is pure-client via 0x206C (CVOGReaction_Dispatch case 0x18).
/// Server returns success so GroupReactionCall fires; no Incomplete.Unhandled.
/// </summary>
[TestClass]
public class ReactionBoostTests
{
    private const int ContId = 824;
    private const int BoostMagnitudeVarId = 10;

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
    public void Boost_ActOnActivator_SucceedsWithoutUnhandled()
    {
        var (_, vehicle, map) = CreatePlayer();

        var tpl = new ReactionTemplate
        {
            COID = 37661,
            Name = "1_Boost",
            ReactionType = ReactionType.Boost,
            ActOnActivator = true,
            GenericVar1 = BoostMagnitudeVarId,
            GenericVar3 = 0,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(37661, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void Boost_EmptyObjects_SucceedsWithoutUnhandled()
    {
        var (_, vehicle, map) = CreatePlayer();

        var tpl = new ReactionTemplate
        {
            COID = 37662,
            Name = "boost_empty_objs",
            ReactionType = ReactionType.Boost,
            GenericVar1 = BoostMagnitudeVarId,
            GenericVar3 = -1,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(37662, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        AssertNoUnhandledLog();
    }

    private void AssertNoUnhandledLog()
    {
        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.Unhandled]")),
            "Expected no Reaction.Unhandled log, got: " + string.Join(" | ", _incomplete));
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_boost_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(350, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(351, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }
}

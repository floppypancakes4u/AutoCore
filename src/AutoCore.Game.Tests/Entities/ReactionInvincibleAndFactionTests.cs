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
/// Server authority for MakeNotInvincible / MakeInvincible / SetFactionFromVar
/// (client CVOGReaction_Dispatch cases 6, 7, 0x16).
/// Generic: uses Template.Objects + logic vars — not mission-specific.
/// </summary>
[TestClass]
public class ReactionInvincibleAndFactionTests
{
    private const int ContId = 808;
    private const long TargetCoid = 9301;
    private const int FactionVarId = 217;
    private const int HumanFactionId = 42;

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
    public void MakeNotInvincible_ClearsInvincibleOnListedObjects()
    {
        var (character, vehicle, map) = CreatePlayer();
        var target = PlaceObject(map, TargetCoid);
        target.SetInvincible(true);
        Assert.IsTrue(target.IsInvincible);

        var reaction = CreateReaction(ReactionType.MakeNotInvincbile, TargetCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.IsFalse(target.IsInvincible, "Server must clear invincible so combat can damage the object");
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void MakeInvincible_SetsInvincibleOnListedObjects()
    {
        var (character, vehicle, map) = CreatePlayer();
        var target = PlaceObject(map, TargetCoid);
        Assert.IsFalse(target.IsInvincible);

        var reaction = CreateReaction(ReactionType.MakeInvincible, TargetCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.IsTrue(target.IsInvincible);
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void MakeNotInvincible_ActOnActivator_ClearsActivator()
    {
        var (character, vehicle, map) = CreatePlayer();
        vehicle.SetInvincible(true);

        var tpl = new ReactionTemplate
        {
            COID = 16350,
            Name = "act_on_self",
            ReactionType = ReactionType.MakeNotInvincbile,
            ActOnActivator = true,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(16350, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.IsFalse(vehicle.IsInvincible);
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetFactionFromVar_AppliesLogicVarToListedObjects()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[FactionVarId] = Variable.CreateForTests(
            FactionVarId, LogicVariableStore.TypeConstant, 0f, HumanFactionId);
        character.EnsureLogicVariables().Set(FactionVarId, HumanFactionId);

        var target = PlaceObject(map, TargetCoid);
        target.Faction = -1;

        var tpl = new ReactionTemplate
        {
            COID = 17919,
            Name = "l1_setfaction_generator_human",
            ReactionType = ReactionType.SetFactionFromVar,
            GenericVar1 = FactionVarId,
        };
        tpl.Objects.Add(TargetCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(17919, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.AreEqual(HumanFactionId, target.Faction);
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetFactionFromVar_RoundsFloatVarToIntFaction()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Variables[FactionVarId] = Variable.CreateForTests(
            FactionVarId, LogicVariableStore.TypeConstant, 0f, 0f);
        character.EnsureLogicVariables().Set(FactionVarId, 7.6f);

        var target = PlaceObject(map, TargetCoid);
        var tpl = new ReactionTemplate
        {
            COID = 1,
            ReactionType = ReactionType.SetFactionFromVar,
            GenericVar1 = FactionVarId,
        };
        tpl.Objects.Add(TargetCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(1, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.AreEqual(8, target.Faction); // client ROUND()
    }

    [TestMethod]
    public void MakeNotInvincible_MissingObject_StillSucceedsWithoutUnhandled()
    {
        var (character, vehicle, map) = CreatePlayer();
        var reaction = CreateReaction(ReactionType.MakeNotInvincbile, 999999);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        AssertNoUnhandledLog();
    }

    [TestMethod]
    public void SetFactionFromVar_NoCharacter_StillSucceeds()
    {
        // Activator without character: cannot read logic vars — no-op authority, no unhandled log.
        var continent = new ContinentObject
        {
            Id = ContId + 1,
            MapFileName = "tm_faction_nocon",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var loneVehicle = new Vehicle();
        loneVehicle.SetCoid(900, true);
        loneVehicle.SetMap(map);

        var target = PlaceObject(map, TargetCoid);
        target.Faction = 1;

        var tpl = new ReactionTemplate
        {
            COID = 2,
            ReactionType = ReactionType.SetFactionFromVar,
            GenericVar1 = FactionVarId,
        };
        tpl.Objects.Add(TargetCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(2, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(loneVehicle));
        Assert.AreEqual(1, target.Faction);
        AssertNoUnhandledLog();
    }

    private void AssertNoUnhandledLog()
    {
        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.Unhandled]")),
            "Expected no Reaction.Unhandled log, got: " + string.Join(" | ", _incomplete));
    }

    private static Reaction CreateReaction(ReactionType type, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = 1000 + (int)type,
            Name = type.ToString(),
            ReactionType = type,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(1000 + (int)type, false);
        return reaction;
    }

    private static SimpleObject PlaceObject(SectorMap map, long coid)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.SetMap(map);
        return obj;
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_inv_faction_{ContId}",
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

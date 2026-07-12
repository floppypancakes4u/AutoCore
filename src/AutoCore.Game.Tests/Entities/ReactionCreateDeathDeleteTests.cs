using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Server authority for ReactionType.Create / Death / Delete (map spawn and despawn).
/// </summary>
[TestClass]
public class ReactionCreateDeathDeleteTests
{
    private const int ContId = 809;
    private const long PropCoid = 16462;
    private const long HealCoid = 16461;
    private const long ReactionCoid = 16464;

    private readonly List<string> _incomplete = new();
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _incomplete.Clear();
        _sent.Clear();
        IncompleteHandlerLog.TestSink = msg => _incomplete.Add(msg);
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        IncompleteHandlerLog.TestSink = null;
        TNLConnection.TestPacketSink = null;
        TriggerManager.Instance.ClearAllForTests();
        _incomplete.Clear();
        _sent.Clear();
    }

    [TestMethod]
    public void Create_SpawnsTemplateObjectOntoMap()
    {
        var (character, vehicle, map) = CreatePlayer();
        SeedSpawnTemplate(map, HealCoid);

        Assert.IsNull(map.GetObjectByCoid(HealCoid));

        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Create, HealCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        var spawned = map.GetObjectByCoid(HealCoid);
        Assert.IsNotNull(spawned, "Create must instantiate MapData template and place on SectorMap");
        Assert.IsInstanceOfType(spawned, typeof(GraphicsObject));
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Create_WhenAlreadyOnMap_IsIdempotent()
    {
        var (character, vehicle, map) = CreatePlayer();
        SeedSpawnTemplate(map, HealCoid);
        var existing = PlaceGraphicsProp(map, HealCoid);

        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Create, HealCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.AreSame(existing, map.GetObjectByCoid(HealCoid));
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Create_MissingTemplate_DoesNotThrowOrLogIncomplete()
    {
        var (character, vehicle, map) = CreatePlayer();
        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Create, HealCoid);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.IsNull(map.GetObjectByCoid(HealCoid));
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Death_RemovesListedObjectWithoutDestroyObject()
    {
        var (character, vehicle, map) = CreatePlayer();
        var prop = PlaceGraphicsProp(map, PropCoid);
        prop.InitializeHealthForTests(100);

        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Death, PropCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.IsNotNull(map.GetObjectByCoid(PropCoid),
            "Personal Death keeps shared prop for other players");
        Assert.IsTrue(character.MapPresence.IsSuppressed(PropCoid),
            "Death must suppress listed COID for activator");
        // Client death FX / mesh is 0x206C only — DestroyObject double-frees and crashes the client.
        Assert.IsFalse(
            _sent.OfType<DestroyObjectPacket>().Any(),
            "Reaction Death must not send DestroyObject");
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Death_ActOnActivator_RemovesActivatorPropWithoutDestroyObject()
    {
        var (character, vehicle, map) = CreatePlayer();
        var prop = PlaceGraphicsProp(map, PropCoid);
        prop.InitializeHealthForTests(50);

        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "death_self",
            ReactionType = ReactionType.Death,
            ActOnActivator = true,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(prop));
        Assert.IsNull(map.GetObjectByCoid(PropCoid));
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any());
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Death_MissingObject_SucceedsWithoutIncomplete()
    {
        var (character, vehicle, map) = CreatePlayer();
        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Death, PropCoid);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Delete_RemovesOnlyListedBlocker_LeavesSiblingGateObject()
    {
        // Gate mesh + collision blocker are separate COIDs; Delete lists only the blocker.
        const long gateMeshCoid = 20001;
        const long blockerCoid = 20002;

        var (character, vehicle, map) = CreatePlayer();
        PlaceGraphicsProp(map, gateMeshCoid);
        PlaceGraphicsProp(map, blockerCoid);

        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Delete, blockerCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.IsNotNull(map.GetObjectByCoid(blockerCoid),
            "Personal Delete keeps shared map object for other players");
        Assert.IsTrue(character.MapPresence.IsSuppressed(blockerCoid),
            "Listed collision blocker suppressed for activator");
        Assert.IsNotNull(map.GetObjectByCoid(gateMeshCoid), "Gate mesh COID must not be deleted");
        Assert.IsFalse(character.MapPresence.IsSuppressed(gateMeshCoid));
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any());
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Delete_PersonallySuppressesListedObject_SharedMapKeepsIt()
    {
        var (character, vehicle, map) = CreatePlayer();
        PlaceGraphicsProp(map, PropCoid);

        var reaction = PlaceReaction(map, ReactionCoid, ReactionType.Delete, PropCoid);
        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));

        Assert.IsNotNull(map.GetObjectByCoid(PropCoid));
        Assert.IsTrue(character.MapPresence.IsSuppressed(PropCoid));
        // Delete relies on GroupReactionCall 0x206C for client visuals (not DestroyObject).
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any());
        AssertNoCreateDeathIncomplete();
    }

    [TestMethod]
    public void Delete_ActOnActivator_RemovesActivatorFromMap()
    {
        var (character, vehicle, map) = CreatePlayer();
        var prop = PlaceGraphicsProp(map, PropCoid);

        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "delete_self",
            ReactionType = ReactionType.Delete,
            ActOnActivator = true,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(prop));
        Assert.IsNull(map.GetObjectByCoid(PropCoid));
        Assert.IsFalse(_sent.OfType<DestroyObjectPacket>().Any());
    }

    [TestMethod]
    public void Delete_ActOnActivator_RefusesVehicle()
    {
        var (character, vehicle, map) = CreatePlayer();
        var tpl = new ReactionTemplate
        {
            COID = (int)ReactionCoid,
            Name = "delete_vehicle",
            ReactionType = ReactionType.Delete,
            ActOnActivator = true,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        Assert.IsTrue(reaction.TriggerIfPossible(vehicle));
        Assert.IsNotNull(map.GetObjectByCoid(vehicle.ObjectId.Coid), "Player vehicle must stay on map");
    }

    private void AssertNoCreateDeathIncomplete()
    {
        Assert.IsFalse(
            _incomplete.Any(m => m.Contains("[Reaction.Create]") || m.Contains("[Reaction.Death]")),
            "Create/Death should be implemented — unexpected incomplete: " + string.Join(" | ", _incomplete));
    }

    private static void SeedSpawnTemplate(SectorMap map, long coid)
    {
        map.MapData.Templates[coid] = new StubSpawnTemplate
        {
            COID = (int)coid,
            CBID = 1,
            IsActive = true,
        };
    }

    private static GraphicsObject PlaceGraphicsProp(SectorMap map, long coid)
    {
        var obj = new GraphicsObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        obj.Position = new Vector3(1f, 0f, 0f);
        obj.SetMap(map);
        return obj;
    }

    private static Reaction PlaceReaction(SectorMap map, long reactionCoid, ReactionType type, long objectCoid)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)reactionCoid,
            Name = type.ToString(),
            ReactionType = type,
            ActOnActivator = false,
        };
        tpl.Objects.Add(objectCoid);
        var reaction = new Reaction(tpl);
        reaction.SetCoid(reactionCoid, false);
        reaction.SetMap(map);
        return reaction;
    }

    private static (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_create_death_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(360, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(361, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }

    /// <summary>Test template that avoids LoadCloneBase (no WAD in unit tests).</summary>
    private sealed class StubSpawnTemplate : ObjectTemplate
    {
        public override ClonedObjectBase Create()
        {
            var obj = new GraphicsObject(GraphicsObjectType.Graphics);
            obj.SetCoid(COID, false);
            obj.Position = new Vector3(10f, 0f, 5f);
            return obj;
        }
    }
}

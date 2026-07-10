using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Map props are GraphicsObject (not SimpleObject/Creature). Combat must reduce HP and
/// destroy the object so destroy-objective missions can complete.
/// </summary>
[TestClass]
public class GraphicsObjectDamageTests
{
    private const int ContId = 909;
    private const long PropCoid = 9301;
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
    }

    [TestMethod]
    public void GraphicsObject_TakeDamage_ReducesHpAndReturnsActual()
    {
        var prop = CreateDamagableProp(maxHp: 100);
        prop.SetInvincible(false);

        var dealt = prop.TakeDamage(30);

        Assert.AreEqual(30, dealt);
        Assert.AreEqual(70, prop.GetCurrentHP());
        Assert.AreEqual(100, prop.GetMaximumHP());
        Assert.IsFalse(prop.IsCorpse);
    }

    [TestMethod]
    public void SetInvincible_False_CreatesCombatGhost()
    {
        var fresh = new GraphicsObject(GraphicsObjectType.Graphics);
        fresh.InitializeHealthForTests(50);
        Assert.IsNull(fresh.Ghost, "Map props must not ghost at init (exhausts client ghost slots / hides NPCs)");

        fresh.SetInvincible(false);

        Assert.IsNotNull(fresh.Ghost, "MakeNotInvincible must ghost the prop for HealthMask HP sync");
        Assert.IsFalse(fresh.IsInvincible);
    }

    [TestMethod]
    public void TakeDamage_LazilyCreatesCombatGhost()
    {
        var prop = CreateDamagableProp(maxHp: 100);
        prop.SetInvincible(false);
        // Clear ghost from SetInvincible path to isolate TakeDamage lazy create:
        // (SetInvincible already created one — assert first damage still works with ghost present)
        Assert.IsNotNull(prop.Ghost);

        var fresh = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        fresh.InitializeHealthForTests(80);
        // Damagable without going through SetInvincible (e.g. never-invincible prop)
        Assert.IsNull(fresh.Ghost);

        Assert.AreEqual(25, fresh.TakeDamage(25));
        Assert.IsNotNull(fresh.Ghost, "First hit should create combat ghost for HP sync");
        Assert.AreEqual(55, fresh.GetCurrentHP());
    }

    [TestMethod]
    public void GraphicsObject_TakeDamage_Invincible_ReturnsZero()
    {
        var prop = CreateDamagableProp(maxHp: 50);
        prop.SetInvincible(true);

        Assert.AreEqual(0, prop.TakeDamage(999));
        Assert.AreEqual(50, prop.GetCurrentHP());
    }

    [TestMethod]
    public void GraphicsObject_TakeDamage_ToZero_ThenOnDeath_RemovesAndBroadcastsDestroy()
    {
        var (character, map) = CreatePlayerOnMap();
        var prop = CreateDamagableProp(maxHp: 40);
        prop.SetCoid(PropCoid, false);
        prop.SetMap(map);
        prop.SetInvincible(false);

        var dealt = prop.TakeDamage(100);
        Assert.AreEqual(40, dealt);
        Assert.AreEqual(0, prop.GetCurrentHP());

        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.IsTrue(prop.IsCorpse);
        Assert.IsNull(map.GetObjectByCoid(PropCoid), "Dead map prop must leave the sector map");
        Assert.IsTrue(
            _sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == PropCoid),
            "Clients need DestroyObject so the prop disappears; sent=" +
            string.Join(',', _sent.Select(p => p.Opcode)));
    }

    [TestMethod]
    public void GraphicsObject_CombatStyle_KillLoop_WorksLikeVehicleFire()
    {
        // Mirrors Vehicle.ProcessCombatInternal death check without full weapon stack.
        var (character, map) = CreatePlayerOnMap();
        var prop = CreateDamagableProp(maxHp: 25);
        prop.SetCoid(PropCoid, false);
        prop.SetMap(map);
        prop.SetInvincible(false);

        var remaining = prop.GetCurrentHP();
        while (remaining > 0)
        {
            var dmg = 10;
            var actual = prop.TakeDamage(dmg);
            Assert.IsTrue(actual > 0);
            remaining = prop.GetCurrentHP();
            if (remaining <= 0)
            {
                prop.SetMurderer(character.CurrentVehicle);
                prop.OnDeath(DeathType.Silent);
            }
        }

        Assert.IsNull(map.GetObjectByCoid(PropCoid));
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any());
    }

    private static GraphicsObject CreateDamagableProp(int maxHp)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        // Unit tests may not have clonebase.wad; seed HP directly after a fake clonebase-style init.
        prop.InitializeHealthForTests(maxHp);
        return prop;
    }

    private (Character Character, SectorMap Map) CreatePlayerOnMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_prop_dmg_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(450, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(451, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, map);
    }
}

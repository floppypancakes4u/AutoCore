using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Server-side vehicle-vs-creature ram (client parity). The retail client locally
/// soft-destroys low-HP creatures on ram contact (CollisionListener::DoVehicleCollision);
/// without a server counterpart the creature stays alive server-side, its ghost keeps
/// streaming to a client that already destroyed it, and the resulting RequestObject /
/// CreateCreature resync loop is a visible movement hitch at the moment of impact.
/// </summary>
[TestClass]
public class VehicleCreatureRamTests
{
    private const int HostileFaction = 21; // Ambient wildlife (Osterakes) — aggros players
    private const int PlayerFaction = 0;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
        VehicleCreatureRam.ResetCooldownsForTests();
        ServerConfig.ResetToDefaults();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
        VehicleCreatureRam.ResetCooldownsForTests();
        ServerConfig.ResetToDefaults();
    }

    [TestMethod]
    public void EnableCreatureRamming_DefaultsOn()
    {
        // Client-parity bug fix, not an optional feature: the client kills soft creatures
        // locally on ram no matter what the server does, so the server must agree by default.
        Assert.IsTrue(ServerConfig.EnableCreatureRamming);
    }

    [TestMethod]
    public void SoftHostileCreature_AtSpeed_DiesFromRam()
    {
        const int cbid = 9950;
        RegisterCreature(cbid, minHp: 1, maxHp: 4);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        var creature = CreateCreatureOnMap(map, coid: 89001, cbid: cbid, maxHp: 4, position: vehicle.Position, faction: HostileFaction);

        var hits = VehicleCreatureRam.Process(vehicle);

        Assert.IsTrue(hits >= 1, "moving vehicle over a soft hostile creature must ram it");
        Assert.IsTrue(creature.IsCorpse, "soft creature dies in one ram (client soft-destroy parity)");
        Assert.AreEqual(vehicle.ObjectId.Coid, creature.Murderer.Coid, "ram kill must credit the ramming vehicle");
    }

    [TestMethod]
    public void Process_WhenCreatureRammingDisabled_DoesNothing()
    {
        ServerConfig.EnableCreatureRamming = false;

        const int cbid = 9951;
        RegisterCreature(cbid, minHp: 1, maxHp: 4);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        var creature = CreateCreatureOnMap(map, coid: 89002, cbid: cbid, maxHp: 4, position: vehicle.Position, faction: HostileFaction);

        Assert.AreEqual(0, VehicleCreatureRam.Process(vehicle));
        Assert.IsFalse(creature.IsCorpse);
        Assert.AreEqual(4, creature.GetCurrentHP());
    }

    [TestMethod]
    public void NeutralCreature_IsNeverRammed()
    {
        const int cbid = 9952;
        RegisterCreature(cbid, minHp: 1, maxHp: 4);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        // -100 Neutral: never aggro either way (town ambience, quest NPCs).
        var creature = CreateCreatureOnMap(map, coid: 89003, cbid: cbid, maxHp: 4, position: vehicle.Position, faction: -100);

        Assert.AreEqual(0, VehicleCreatureRam.Process(vehicle));
        Assert.IsFalse(creature.IsCorpse);
    }

    [TestMethod]
    public void SlowVehicle_DoesNotRamCreature()
    {
        const int cbid = 9953;
        RegisterCreature(cbid, minHp: 1, maxHp: 4);

        var (vehicle, map) = CreateVehicleOnMap(speed: 1f);
        var creature = CreateCreatureOnMap(map, coid: 89004, cbid: cbid, maxHp: 4, position: vehicle.Position, faction: HostileFaction);

        Assert.AreEqual(0, VehicleCreatureRam.Process(vehicle));
        Assert.IsFalse(creature.IsCorpse);
    }

    [TestMethod]
    public void HardCreature_TakesSpeedScaledDamage_ButSurvives()
    {
        const int cbid = 9954;
        RegisterCreature(cbid, minHp: 50, maxHp: 500);

        var (vehicle, map) = CreateVehicleOnMap(speed: 10f);
        var creature = CreateCreatureOnMap(map, coid: 89005, cbid: cbid, maxHp: 500, position: vehicle.Position, faction: HostileFaction);

        var hits = VehicleCreatureRam.Process(vehicle);

        Assert.IsTrue(hits >= 1);
        Assert.IsFalse(creature.IsCorpse, "hard creature survives one moderate-speed ram");
        var dealt = 500 - creature.GetCurrentHP();
        Assert.IsTrue(dealt > 0 && dealt < 500, $"expected partial speed-scaled damage, dealt={dealt}");
    }

    [TestMethod]
    public void RepeatRam_WithinCooldown_HitsOnlyOnce()
    {
        const int cbid = 9955;
        RegisterCreature(cbid, minHp: 50, maxHp: 500);

        var (vehicle, map) = CreateVehicleOnMap(speed: 10f);
        var creature = CreateCreatureOnMap(map, coid: 89006, cbid: cbid, maxHp: 500, position: vehicle.Position, faction: HostileFaction);

        Assert.IsTrue(VehicleCreatureRam.Process(vehicle) >= 1);
        var hpAfterFirst = creature.GetCurrentHP();

        // Immediate second movement packet: same contact, must be inside the cooldown.
        Assert.AreEqual(0, VehicleCreatureRam.Process(vehicle));
        Assert.AreEqual(hpAfterFirst, creature.GetCurrentHP());
    }

    [TestMethod]
    public void Process_HitsOnlyClosestCreature_NotWholePack()
    {
        const int cbid = 9956;
        RegisterCreature(cbid, minHp: 1, maxHp: 4);

        var (vehicle, map) = CreateVehicleOnMap(speed: 25f);
        vehicle.Position = new Vector3(0, 0, 0);
        var near = CreateCreatureOnMap(map, coid: 89100, cbid: cbid, maxHp: 4, position: new Vector3(2, 0, 0), faction: HostileFaction);
        var far = CreateCreatureOnMap(map, coid: 89101, cbid: cbid, maxHp: 4, position: new Vector3(5, 0, 0), faction: HostileFaction);

        var hits = VehicleCreatureRam.Process(vehicle);

        Assert.AreEqual(1, hits, "one contact per movement packet");
        Assert.IsTrue(near.IsCorpse, "closest creature dies");
        Assert.IsFalse(far.IsCorpse, "pack must not AOE-die from one contact");
    }

    [TestMethod]
    public void CorpseCreature_IsNotRammedAgain()
    {
        const int cbid = 9957;
        RegisterCreature(cbid, minHp: 1, maxHp: 4);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        var creature = CreateCreatureOnMap(map, coid: 89007, cbid: cbid, maxHp: 4, position: vehicle.Position, faction: HostileFaction);
        creature.SetMurderer(vehicle);
        creature.OnDeath(DeathType.Violent);
        Assert.IsTrue(creature.IsCorpse);

        Assert.AreEqual(0, VehicleCreatureRam.Process(vehicle));
    }

    private static void RegisterCreature(int cbid, short minHp, short maxHp)
    {
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Creature);
        var cb = (AutoCore.Game.CloneBases.CloneBaseObject)AssetManager.Instance.GetCloneBase(cbid)!;
        cb.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MinHitPoints = minHp,
            MaxHitPoint = maxHp,
            Flags = 1,
        };
    }

    private static Creature CreateCreatureOnMap(SectorMap map, long coid, int cbid, int maxHp, Vector3 position, int faction)
    {
        var creature = new Creature();
        creature.SetCoid(coid, false);
        creature.LoadCloneBase(cbid);
        creature.InitializeHealthForTests(maxHp);
        creature.Position = position;
        creature.SetInvincible(false);
        creature.Faction = faction;
        creature.SetMap(map);
        return creature;
    }

    private static (Vehicle Vehicle, SectorMap Map) CreateVehicleOnMap(float speed)
    {
        var continent = new ContinentObject
        {
            Id = 812,
            MapFileName = "tm_creature_ram",
            DisplayName = "cram",
            DropCommodities = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(7101, true);
        character.Faction = PlayerFaction;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(7102, true);
        vehicle.InitializeHealthForTests(500);
        vehicle.Position = new Vector3(10, 0, 10);
        vehicle.SetVelocityForTests(new Vector3(speed, 0, 0));
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, map);
    }
}

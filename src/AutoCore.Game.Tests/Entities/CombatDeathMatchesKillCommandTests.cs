using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// /kill uses <see cref="DeathType.Violent"/> and correctly plays client death FX.
/// Combat fire must use the same death type (not Silent) so DestroyObject carries death=2.
/// </summary>
[TestClass]
public class CombatDeathMatchesKillCommandTests
{
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
    public void KillCommandStyleDeath_Violent_SendsDeathTypeOnDestroy()
    {
        // Mirrors ChatManager /kill: SetMurderer + OnDeath(Violent).
        var map = CreateMapWithPlayer();
        var killerVehicle = map.Objects.Values.OfType<Character>().First().CurrentVehicle
            ?? throw new AssertFailedException("player needs a vehicle");

        var target = new Vehicle();
        target.SetCoid(99001, true);
        target.NpcAi = new Npc.NpcAiState();
        target.InitializeHealthForTests(1);
        target.SetMap(map);

        target.SetMurderer(killerVehicle);
        target.OnDeath(DeathType.Violent);

        var destroy = _sent.OfType<DestroyObjectPacket>().FirstOrDefault(p => p.ObjectId.Coid == 99001);
        Assert.IsNotNull(destroy);
        Assert.AreEqual(DeathType.Violent, destroy.DeathType);
        Assert.IsFalse(destroy.Force);
    }

    [TestMethod]
    public void SilentCombatDeath_WouldNotPlayClientDeathFx()
    {
        // Documents the bug: OnDeath(Silent) forces teardown without death FX field.
        var map = CreateMapWithPlayer();
        var target = new Vehicle();
        target.SetCoid(99002, true);
        target.NpcAi = new Npc.NpcAiState();
        target.InitializeHealthForTests(1);
        target.SetMap(map);

        target.OnDeath(DeathType.Silent);

        var destroy = _sent.OfType<DestroyObjectPacket>().FirstOrDefault(p => p.ObjectId.Coid == 99002);
        Assert.IsNotNull(destroy);
        Assert.AreEqual(DeathType.Silent, destroy.DeathType);
        Assert.IsTrue(destroy.Force, "Silent path force-tears-down; no death animation");
    }

    private static SectorMap CreateMapWithPlayer()
    {
        var continent = new ContinentObject
        {
            Id = 9910,
            MapFileName = "tm_combat_death_parity",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(99100, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(99101, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return map;
    }
}

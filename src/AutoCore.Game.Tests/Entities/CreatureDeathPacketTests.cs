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
/// Creature death must broadcast a full DestroyObject with DeathType + murderer so the client
/// plays death VFX (Client_RecvDestroyObject → CompletelyDestroyObject → vfunc death FX).
/// </summary>
[TestClass]
public class CreatureDeathPacketTests
{
    private const int ContId = 9210;
    private const long CreatureCoid = 9211;
    private const long KillerCoid = 9212;

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
    public void CreatureOnDeath_BroadcastsDestroyWithDeathTypeAndMurderer_NoPlaceholderChat()
    {
        var map = CreateMapWithPlayer();
        var killer = map.Objects.Values.OfType<Character>().First();
        var murdererTf = killer.ObjectId;

        var creature = new Creature();
        creature.SetCoid(CreatureCoid, true);
        creature.InitializeHealthForTests(10);
        creature.SetMap(map);
        creature.SetMurderer(murdererTf);

        creature.OnDeath(DeathType.Violent);

        Assert.IsTrue(creature.IsCorpse);
        Assert.IsNull(map.GetObjectByCoid(CreatureCoid));

        var destroy = _sent.OfType<DestroyObjectPacket>()
            .FirstOrDefault(p => p.ObjectId.Coid == CreatureCoid);
        Assert.IsNotNull(destroy, "DestroyObject must be broadcast; sent=" +
            string.Join(',', _sent.Select(p => p.Opcode)));
        Assert.AreEqual(DeathType.Violent, destroy.DeathType);
        Assert.IsNotNull(destroy.Murderer);
        Assert.AreEqual(murdererTf.Coid, destroy.Murderer.Coid);
        Assert.AreEqual(murdererTf.Global, destroy.Murderer.Global);
        Assert.IsFalse(destroy.Force, "animated death uses force=0 so client can play FX before teardown");

        var initDeath = _sent.OfType<InitCreateObjectPacket>()
            .FirstOrDefault(p => p.ObjectCoid == CreatureCoid && !p.Create && p.DoDeath);
        Assert.IsNotNull(initDeath,
            "InitCreateObject DoDeath must be sent for combat deaths; sent=" +
            string.Join(',', _sent.Select(p => p.Opcode)));

        Assert.IsFalse(
            _sent.OfType<BroadcastPacket>().Any(p =>
                p.Message != null &&
                p.Message.Contains("Death animation packet is missing", StringComparison.Ordinal)),
            "placeholder system chat must not be sent once death packet is wired");
    }

    [TestMethod]
    public void CreatureOnDeath_Silent_StillBroadcastsDestroyWithSilentDeathType()
    {
        var map = CreateMapWithPlayer();
        var creature = new Creature();
        creature.SetCoid(CreatureCoid, true);
        creature.InitializeHealthForTests(10);
        creature.SetMap(map);

        creature.OnDeath(DeathType.Silent);

        var destroy = _sent.OfType<DestroyObjectPacket>()
            .FirstOrDefault(p => p.ObjectId.Coid == CreatureCoid);
        Assert.IsNotNull(destroy);
        Assert.AreEqual(DeathType.Silent, destroy.DeathType);
        Assert.IsTrue(destroy.Force, "silent despawn forces teardown without death FX wait");
        Assert.IsFalse(
            _sent.OfType<InitCreateObjectPacket>().Any(p => p.DoDeath),
            "silent despawn must not send InitCreateObject DoDeath");
    }

    private static SectorMap CreateMapWithPlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_creature_death_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(KillerCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        character.SetMap(map);

        return map;
    }
}

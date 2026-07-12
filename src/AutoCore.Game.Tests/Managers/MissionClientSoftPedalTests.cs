using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Soft-pedal after dialog turn-in: suppress GroupReactionCall (0x206C) briefly while server
/// reactions still run (client interact FX / MSXML crash window @ 0x007B6DB0).
/// </summary>
[TestClass]
public class MissionClientSoftPedalTests
{
    private const int ContId = 707;
    private const int ReactionCoid = 991001;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        MissionClientSoftPedal.ResetForTests();
        SectorMap.SendGroupReactionCall = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        MissionClientSoftPedal.ResetForTests();
        SectorMap.SendGroupReactionCall = true;
        _sent.Clear();
    }

    [TestMethod]
    public void ArmAfterDialogTurnIn_SuppressesThenExpires()
    {
        const long coid = 18325;
        MissionClientSoftPedal.GroupReactionSuppressMs = 5_000;
        MissionClientSoftPedal.ArmAfterDialogTurnIn(coid);

        Assert.IsTrue(MissionClientSoftPedal.ShouldSuppressGroupReactionCall(coid));
        Assert.IsTrue(MissionClientSoftPedal.HasPendingSuppressForTests(coid));

        MissionClientSoftPedal.DebugExpireForTests(coid);
        Assert.IsFalse(MissionClientSoftPedal.ShouldSuppressGroupReactionCall(coid),
            "Expired suppress must clear");
        Assert.IsFalse(MissionClientSoftPedal.HasPendingSuppressForTests(coid));
    }

    [TestMethod]
    public void TriggerReactions_WhileSuppressed_SkipsGroupReactionCall()
    {
        var (character, vehicle, map) = CreatePlayerWithBoostReaction();
        MissionClientSoftPedal.GroupReactionSuppressMs = 10_000;
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);
        _sent.Clear();

        map.TriggerReactions(vehicle, new List<long> { ReactionCoid });

        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count(),
            "0x206C must be soft-pedaled for the turn-in character");
    }

    [TestMethod]
    public void TriggerReactions_AfterSuppressExpires_SendsGroupReactionCall()
    {
        var (character, vehicle, map) = CreatePlayerWithBoostReaction();
        MissionClientSoftPedal.GroupReactionSuppressMs = 10_000;
        MissionClientSoftPedal.ArmAfterDialogTurnIn(character.ObjectId.Coid);
        MissionClientSoftPedal.DebugExpireForTests(character.ObjectId.Coid);
        _sent.Clear();

        map.TriggerReactions(vehicle, new List<long> { ReactionCoid });

        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any(),
            "After soft-pedal expires, GroupReactionCall must send again");
    }

    private (Character character, Vehicle vehicle, SectorMap map) CreatePlayerWithBoostReaction()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_softpedal_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18325, true);
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(18326, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);

        var tpl = new ReactionTemplate
        {
            COID = ReactionCoid,
            Name = "softpedal_boost",
            ReactionType = ReactionType.Boost,
            ActOnActivator = true,
            GenericVar1 = 10,
        };
        var reaction = new Reaction(tpl);
        reaction.SetCoid(ReactionCoid, false);
        reaction.SetMap(map);

        return (character, vehicle, map);
    }
}

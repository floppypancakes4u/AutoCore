using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Game.Combat;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Target-frame HP is applied by 0x2010 type=2 (FUN_0080B3A0 → SetCurrentHP), not by 0x2023.
/// These tests fail until combat pushes that absolute Health packet to attacker ∪ victim.
/// </summary>
[TestClass]
public class AbsoluteHealthSyncTests
{
    [TestInitialize]
    public void Init() => TNLConnection.TestPacketSink = null;

    [TestCleanup]
    public void Cleanup() => TNLConnection.TestPacketSink = null;

    [TestMethod]
    public void TakeDamage_PlayerHitsNpcVehicle_SendsHealthStatUpdateToAttacker()
    {
        var (attackerVeh, _, sent) = MakeOwned(60_001, hp: 200);
        var npc = new Vehicle();
        npc.SetCoid(70_002, true);
        npc.SetMaximumHP(100, triggerGhostUpdate: false);
        npc.SetHPForTests(80);

        var actual = npc.TakeDamage(25, attackerVeh);

        Assert.AreEqual(25, actual);
        Assert.AreEqual(55, npc.GetCurrentHP());

        var health = sent.OfType<MultipleStatUpdatePacket>()
            .SelectMany(p => p.Objects)
            .SelectMany(o => o.Stats.Select(s => (o.ObjectId, s)))
            .Single(x => x.s.Type == MultipleStatUpdatePacket.StatType.Health);

        Assert.AreEqual(npc.ObjectId.Coid, health.ObjectId.Coid);
        Assert.AreEqual(npc.ObjectId.Global, health.ObjectId.Global);
        Assert.AreEqual(55f, health.s.Value);
    }

    [TestMethod]
    public void TakeDamage_PlayerHitsCreature_SendsHealthStatUpdateOnCreatureTfid()
    {
        var (attackerVeh, _, sent) = MakeOwned(60_011, hp: 200);
        var cre = new Creature();
        cre.SetCoid(70_010, false);
        cre.InitializeHealthForTests(40);

        cre.TakeDamage(10, attackerVeh);

        var health = sent.OfType<MultipleStatUpdatePacket>()
            .SelectMany(p => p.Objects)
            .Single(o => o.Stats.Any(s => s.Type == MultipleStatUpdatePacket.StatType.Health));
        Assert.AreEqual(70_010, health.ObjectId.Coid);
        Assert.AreEqual(false, health.ObjectId.Global);
        Assert.AreEqual(30f, health.Stats[0].Value);
    }

    [TestMethod]
    public void TakeDamage_NpcHitsPlayer_SendsHealthStatUpdateToVictimOwner()
    {
        var (victimVeh, _, sent) = MakeOwned(60_021, hp: 200);
        var npc = new Vehicle();
        npc.SetCoid(70_020, true);

        victimVeh.TakeDamage(40, npc);

        var health = sent.OfType<MultipleStatUpdatePacket>()
            .SelectMany(p => p.Objects)
            .SelectMany(o => o.Stats.Select(s => (o.ObjectId, s)))
            .Single(x => x.s.Type == MultipleStatUpdatePacket.StatType.Health);
        Assert.AreEqual(victimVeh.ObjectId.Coid, health.ObjectId.Coid);
        Assert.AreEqual(160f, health.s.Value);
    }

    [TestMethod]
    public void Send_NullVictim_DoesNotThrow()
    {
        AbsoluteHealthSync.Send(null, null);
    }

    [TestMethod]
    public void Send_NpcWithNoConnections_SendsNothing()
    {
        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);

        var npc = new Vehicle();
        npc.SetCoid(70_050, true);
        npc.SetMaximumHP(20, triggerGhostUpdate: false);
        npc.SetHPForTests(20);

        AbsoluteHealthSync.Send(npc, attacker: null);

        Assert.AreEqual(0, sent.Count);
    }

    [TestMethod]
    public void TakeDamage_StillDirtiesGhostHealthMask()
    {
        var npc = new Vehicle();
        npc.SetCoid(70_040, true);
        npc.SetMaximumHP(100, triggerGhostUpdate: false);
        npc.SetHPForTests(80);
        npc.CreateGhost();
        npc.Ghost.ClearDirtyMaskBitsForTests();

        npc.TakeDamage(10, attacker: null);

        var bitsField = typeof(global::TNL.Entities.NetObject).GetField(
            "_dirtyMaskBits",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(bitsField);
        var bits = (ulong)bitsField.GetValue(npc.Ghost)!;
        Assert.AreNotEqual(0UL, bits & GhostObject.HealthMask);
    }

    [TestMethod]
    public void TakeDamage_ZeroDamage_DoesNotSendHealthStatUpdate()
    {
        var (attackerVeh, _, sent) = MakeOwned(60_031, hp: 200);
        var npc = new Vehicle();
        npc.SetCoid(70_030, true);
        npc.SetMaximumHP(50, triggerGhostUpdate: false);
        npc.SetHPForTests(50);
        npc.SetInvincible(true);

        Assert.AreEqual(0, npc.TakeDamage(10, attackerVeh));
        Assert.IsFalse(sent.OfType<MultipleStatUpdatePacket>()
            .SelectMany(p => p.Objects)
            .SelectMany(o => o.Stats)
            .Any(s => s.Type == MultipleStatUpdatePacket.StatType.Health));
    }

    private static (Vehicle vehicle, Character character, List<BasePacket> sent) MakeOwned(
        long coid, int hp)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        var character = new Character();
        character.SetCoid(coid + 1, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetMaximumHP(hp, triggerGhostUpdate: false);
        vehicle.SetHPForTests(hp);

        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        return (vehicle, character, sent);
    }
}

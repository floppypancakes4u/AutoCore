using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// SS-33 tripwires for the 0x2023 DamagePacket throttle. The throttle exists to stop floater
/// floods (multi-slot + splash), but keyed per attacker only it also ate the visible feedback
/// for every OTHER target hit within the window — "my shots do nothing" while HP was applied.
/// </summary>
[TestClass]
public class DamagePacketThrottleTests
{
    private readonly List<BasePacket> _sent = new();
    private TNLConnection _conn;
    private Character _attacker;

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        Vehicle.ClearCombatThrottleForTests();
        ServerConfig.ResetToDefaults();

        _conn = new TNLConnection();
        _conn.SetGhostFrom(true);
        _conn.SetGhostTo(false);
        _attacker = new Character();
        _attacker.SetCoid(96100, true);
        _attacker.SetOwningConnection(_conn);
        _conn.CurrentCharacter = _attacker;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        Vehicle.ClearCombatThrottleForTests();
        ServerConfig.ResetToDefaults();
    }

    private static GraphicsObject Victim(long coid)
    {
        var v = new GraphicsObject(GraphicsObjectType.Graphics);
        v.InitializeHealthForTests(10);
        v.SetCoid(coid, false);
        return v;
    }

    private void Send(long attackerVehicleCoid, params GraphicsObject[] victims)
        => SendAmount(attackerVehicleCoid, 5, victims);

    private void SendAmount(long attackerVehicleCoid, int amount, params GraphicsObject[] victims)
    {
        var packet = new DamagePacket { Source = new TFID(attackerVehicleCoid, true) };
        foreach (var v in victims)
            packet.AddHit(v.ObjectId, amount);
        Vehicle.TrySendDamagePacketMulti(_attacker, packet, new TFID(attackerVehicleCoid, true), victims);
    }

    private int ShippedTotalFor(long victimCoid) => _sent.OfType<DamagePacket>()
        .SelectMany(p => p.Entries)
        .Where(e => e.Target.Coid == victimCoid)
        .Sum(e => (int)e.Amount);

    [TestMethod]
    public void SameTarget_WithinWindow_SecondPacketDropped()
    {
        var victim = Victim(96200);

        Send(96101, victim);
        Send(96101, victim);

        Assert.AreEqual(1, _sent.OfType<DamagePacket>().Count(),
            "flood protection: same attacker→target pair within the window must coalesce");
    }

    [TestMethod]
    public void DifferentTargets_WithinWindow_AreNotSuppressed()
    {
        var first = Victim(96201);
        var second = Victim(96202);

        Send(96101, first);
        Send(96101, second);

        Assert.AreEqual(2, _sent.OfType<DamagePacket>().Count(),
            "a hit on a DIFFERENT target must not be eaten by the attacker-keyed throttle");
    }

    [TestMethod]
    public void ThrottleWindow_Configurable_ZeroDisablesThrottle()
    {
        ServerConfig.DamagePacketThrottleMs = 0;
        var victim = Victim(96203);

        Send(96101, victim);
        Send(96101, victim);

        Assert.AreEqual(2, _sent.OfType<DamagePacket>().Count(),
            "window 0 must disable the throttle entirely");
    }

    [TestMethod]
    public void MultiHitPacket_WithOneFreshTarget_IsSent()
    {
        var first = Victim(96204);
        var second = Victim(96205);

        Send(96101, first);
        Send(96101, first, second); // first is throttled, second is fresh — packet must still ship

        Assert.AreEqual(2, _sent.OfType<DamagePacket>().Count(),
            "a multi-hit packet containing any un-throttled target must be sent");
    }

    // --- SS-39: suppressed damage must accumulate, not vanish ---

    /// <summary>
    /// SS-39 tripwire: the throttle DISCARDED suppressed volleys — at 20 Hz NPC fire up to half
    /// the real damage was never rendered in any form ("health drains out of nowhere").
    /// Suppressed amounts must fold into the next shipped packet for that pair.
    /// </summary>
    [TestMethod]
    public void SuppressedDamage_FoldsIntoNextShippedPacket()
    {
        var clock = 1_000L;
        Vehicle.CombatThrottleClock = () => clock;
        var victim = Victim(96210);

        SendAmount(96101, 5, victim);           // ships (5)
        SendAmount(96101, 5, victim);           // inside window → suppressed, must fold
        clock += ServerConfig.DamagePacketThrottleMs + 50;
        SendAmount(96101, 5, victim);           // ships — must carry the folded 5

        var packets = _sent.OfType<DamagePacket>().ToList();
        Assert.AreEqual(2, packets.Count, "fold must not change packet count");
        Assert.AreEqual(10, (int)packets[1].Entries.Single().Amount,
            "the suppressed volley's 5 must fold into the next shipped packet");
    }

    [TestMethod]
    public void ShippedTotal_EqualsAppliedTotal_AcrossThrottledVolleys()
    {
        var clock = 1_000L;
        Vehicle.CombatThrottleClock = () => clock;
        var victim = Victim(96211);

        SendAmount(96101, 5, victim);
        SendAmount(96101, 5, victim);
        SendAmount(96101, 5, victim);
        clock += ServerConfig.DamagePacketThrottleMs + 50;
        SendAmount(96101, 5, victim);

        Assert.AreEqual(20, ShippedTotalFor(96211),
            "every point of applied damage must eventually be displayed (SS-39 conservation)");
    }

    /// <summary>SS-39: the killing blow must never be suppressed (backstops SS-37).</summary>
    [TestMethod]
    public void KillingBlow_InsideWindow_IsNeverSuppressed()
    {
        var clock = 1_000L;
        Vehicle.CombatThrottleClock = () => clock;
        var victim = Victim(96212);

        SendAmount(96101, 5, victim);   // ships, stamps the window
        victim.TakeDamage(10);          // dead at 0 HP
        SendAmount(96101, 5, victim);   // inside window, but the victim is dead — must ship

        Assert.AreEqual(2, _sent.OfType<DamagePacket>().Count(),
            "a volley containing a killing blow must never be throttled away");
    }

    [TestMethod]
    public void FoldedAmount_ClampsToDisplayMax()
    {
        var clock = 1_000L;
        Vehicle.CombatThrottleClock = () => clock;
        var victim = Victim(96213);

        SendAmount(96101, 100, victim);      // ships
        SendAmount(96101, 32_000, victim);   // suppressed
        SendAmount(96101, 32_000, victim);   // suppressed — pending clamps
        clock += ServerConfig.DamagePacketThrottleMs + 50;
        SendAmount(96101, 100, victim);

        var last = _sent.OfType<DamagePacket>().Last().Entries.Single();
        Assert.AreEqual(DamagePacket.MaxDisplayAmount, (int)last.Amount,
            "folded amounts must clamp to the client-safe display max (32766)");
    }
}

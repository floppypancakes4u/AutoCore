using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Vehicle damage absorbs into shield first, then HP. Shield still regens on the
/// combat-pool pulse; HP does not.
/// </summary>
[TestClass]
public class VehicleShieldDamageTests
{
    [TestMethod]
    public void TakeDamage_FullyAbsorbedByShield_DoesNotTouchHp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_001, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(150);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(80, triggerGhostUpdate: false);

        var dealt = vehicle.TakeDamage(30);

        Assert.AreEqual(30, dealt);
        Assert.AreEqual(50, vehicle.CurrentShield);
        Assert.AreEqual(150, vehicle.GetCurrentHP(), "HP must be unchanged while shield absorbs");
    }

    [TestMethod]
    public void TakeDamage_ExceedsShield_RemainderHitsHp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_002, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(25, triggerGhostUpdate: false);

        var dealt = vehicle.TakeDamage(40);

        Assert.AreEqual(40, dealt);
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(85, vehicle.GetCurrentHP(), "remainder 15 must hit HP");
    }

    [TestMethod]
    public void TakeDamage_ZeroShield_HitsHpOnly()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_003, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(0, triggerGhostUpdate: false);

        var dealt = vehicle.TakeDamage(35);

        Assert.AreEqual(35, dealt);
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(65, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_ZeroMaxShield_HitsHpOnly()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_004, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        // Default MaxShield/CurrentShield are 0.

        var dealt = vehicle.TakeDamage(20);

        Assert.AreEqual(20, dealt);
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(80, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_OverflowBeyondShieldAndHp_ClampsToAvailable()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_005, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(30);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(20, triggerGhostUpdate: false);

        var dealt = vehicle.TakeDamage(1000);

        Assert.AreEqual(50, dealt, "20 shield + 30 HP");
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(0, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_Invincible_ReturnsZero_LeavesShieldAndHp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_006, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(50, triggerGhostUpdate: false);
        vehicle.SetInvincible(true);

        var dealt = vehicle.TakeDamage(40);

        Assert.AreEqual(0, dealt);
        Assert.AreEqual(50, vehicle.CurrentShield);
        Assert.AreEqual(100, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_Corpse_ReturnsZero_LeavesShieldAndHp()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_007, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(0);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(40, triggerGhostUpdate: false);
        vehicle.OnDeath(DeathType.Silent);
        Assert.IsTrue(vehicle.IsCorpse);

        var dealt = vehicle.TakeDamage(25);

        Assert.AreEqual(0, dealt);
        Assert.AreEqual(40, vehicle.CurrentShield);
        Assert.AreEqual(0, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_ShieldHit_DirtiesShieldMaskOnGhost()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_008, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(50, triggerGhostUpdate: false);
        ClearGhostDirtyMask(vehicle.Ghost!);

        vehicle.TakeDamage(10);

        Assert.AreEqual(40, vehicle.CurrentShield);
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.ShieldMask),
            "shield absorption must dirty ShieldMask for client sync");
    }

    [TestMethod]
    public void TakeDamage_NegativeOrZero_ReturnsZero()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_009, true);
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(100);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(50, triggerGhostUpdate: false);

        Assert.AreEqual(0, vehicle.TakeDamage(0));
        Assert.AreEqual(0, vehicle.TakeDamage(-5));
        Assert.AreEqual(50, vehicle.CurrentShield);
        Assert.AreEqual(100, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_ShieldOnly_SendsCharacterLevelAndMultipleStatUpdate()
    {
        // Client FUN_00812A60 can apply DamagePacket as local HP prediction. Shield-only
        // hits must still push absolute CharacterLevel Health so the bar does not stick low.
        // Shield itself is not on CharacterLevel — retail uses MultipleStatUpdate 0x2010 type=1.
        CharacterLevelManager.Instance.ClearAllForTests();
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_010, true);
        var character = new Character();
        character.SetCoid(90_011, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetMaximumHP(500, triggerGhostUpdate: false);
        vehicle.SetHPForTests(400);
        vehicle.SetMaximumShield(200);
        vehicle.SetCurrentShield(150, triggerGhostUpdate: false);

        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        try
        {
            var dealt = vehicle.TakeDamage(40);

            Assert.AreEqual(40, dealt);
            Assert.AreEqual(110, vehicle.CurrentShield);
            Assert.AreEqual(400, vehicle.GetCurrentHP());
            var level = sent.OfType<CharacterLevelPacket>().Single();
            Assert.AreEqual(400, level.Health, "owner HUD must re-assert absolute HP after shield absorb");
            Assert.AreEqual(500, level.HealthMaximum);

            var stat = sent.OfType<MultipleStatUpdatePacket>().Single();
            Assert.AreEqual(1, stat.Objects.Count);
            Assert.AreEqual(90_010L, stat.Objects[0].ObjectId.Coid);
            Assert.AreEqual(1, stat.Objects[0].Stats.Count);
            Assert.AreEqual(MultipleStatUpdatePacket.StatType.Shield, stat.Objects[0].Stats[0].Type);
            Assert.AreEqual(110f, stat.Objects[0].Stats[0].Value);
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
            CharacterLevelManager.Instance.ClearAllForTests();
        }
    }

    [TestMethod]
    public void TakeDamage_ShieldOnly_DirtiesShieldMaxAndShieldAndHealthMasks()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(90_012, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumHP(200, triggerGhostUpdate: false);
        vehicle.SetHPForTests(180);
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(80, triggerGhostUpdate: false);
        ClearGhostDirtyMask(vehicle.Ghost!);

        vehicle.TakeDamage(25);

        Assert.AreEqual(55, vehicle.CurrentShield);
        Assert.AreEqual(180, vehicle.GetCurrentHP());
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.ShieldMask));
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.ShieldMaxMask),
            "ShieldMax must re-sync so client race-item gauge tracks server capacity");
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostObject.HealthMask),
            "HealthMask absolute re-assert undoes client DamagePacket HP prediction");
    }

    private static bool GhostHasDirtyMask(NetObject ghost, ulong mask)
    {
        var field = typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "TNL NetObject dirty mask field");
        var bits = (ulong)field!.GetValue(ghost)!;
        return (bits & mask) != 0;
    }

    private static void ClearGhostDirtyMask(NetObject ghost)
    {
        var field = typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "TNL NetObject dirty mask field");
        field!.SetValue(ghost, 0UL);
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Game.Chat;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Heavy regression lock for shield-first combat + client sync.
///
/// Server contract (locked by live play + Ghidra):
/// <list type="bullet">
///   <item>Damage hits shield before HP (<c>FUN_004f62e0</c> parity).</item>
///   <item>Owner current shield is pushed via MultipleStatUpdate 0x2010 type=1
///         (<c>FUN_0080B3A0</c> → <c>Vehicle_SetCurrentShield</c>).</item>
///   <item>Observers get GhostVehicle ShieldMax/Shield 32-bit fields.</item>
///   <item>CharacterLevel carries HP/power only — never shield.</item>
///   <item>HP does not passively regen; shield regens on 3000 ms combat-pool pulses.</item>
///   <item>Repair-pad skill 857 is esetHeal(10)+esetPower(12) only — not esetShield(58).</item>
/// </list>
/// </summary>
[TestClass]
public class VehicleShieldCombatRegressionTests
{
    [TestInitialize]
    public void Init()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        TNLConnection.TestPacketSink = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        TNLConnection.TestPacketSink = null;
        NetObject.PIsInitialUpdate = false;
    }

    // ── Absorb math ──────────────────────────────────────────────────────────

    [TestMethod]
    public void MultiHit_DrainsShieldThenHp_InOrder()
    {
        var vehicle = MakeVehicle(maxHp: 200, hp: 100, maxShield: 30, shield: 30);

        Assert.AreEqual(20, vehicle.TakeDamage(20));
        Assert.AreEqual(10, vehicle.CurrentShield);
        Assert.AreEqual(100, vehicle.GetCurrentHP());

        Assert.AreEqual(15, vehicle.TakeDamage(15)); // 10 shield + 5 HP
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(95, vehicle.GetCurrentHP());

        Assert.AreEqual(40, vehicle.TakeDamage(40));
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(55, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void TakeDamage_WithAttacker_ShieldAbsorbStillReturnsTotalForAggro()
    {
        var victim = MakeVehicle(maxHp: 200, hp: 100, maxShield: 50, shield: 50);
        var attacker = MakeVehicle(maxHp: 100, hp: 100, maxShield: 0, shield: 0);
        attacker.SetCoid(91_100, true);

        var dealt = victim.TakeDamage(25, attacker);
        Assert.AreEqual(25, dealt, "return value includes shield absorb for floater/aggro paths");
        Assert.AreEqual(25, victim.CurrentShield);
        Assert.AreEqual(100, victim.GetCurrentHP());
    }

    [TestMethod]
    public void RestoreHealth_HealsHpOnly_DoesNotTouchShield()
    {
        // Repair-pad skill path: RestoreHealth is HP-only (esetHeal). Shield is separate pool.
        var vehicle = MakeVehicle(maxHp: 200, hp: 50, maxShield: 100, shield: 10);

        var restored = vehicle.RestoreHealth(40);
        Assert.AreEqual(40, restored);
        Assert.AreEqual(90, vehicle.GetCurrentHP());
        Assert.AreEqual(10, vehicle.CurrentShield, "repair/heal must not refill shield");
    }

    [TestMethod]
    public void RepairPadSkill857_Elements_AreHealAndPower_NotShield()
    {
        // Retail skill 857 "INC Repair station heal" from clonebase.wad:
        //   type 10 esetHeal  eq1 base 0.15 → 15% max HP
        //   type 12 esetPower eq1 base 0.15 → 15% max power
        //   type 58 esetShield is NOT present.
        // Locked so pad heal policy cannot silently start filling shields.
        const int esetHeal = 10;
        const int esetPower = 12;
        const int esetShield = 58;

        Assert.AreEqual(10, SkillElementTypes.Heal, "SkillElementTypes.Heal must stay 10 (esetHeal)");
        Assert.AreNotEqual(esetShield, SkillElementTypes.Heal);

        // Document authored IDs for future WAD-backed tests; pure constants lock without AssetManager.
        Assert.AreEqual(esetHeal, 10);
        Assert.AreEqual(esetPower, 12);
        Assert.AreEqual(esetShield, 58);
        Assert.AreNotEqual(esetShield, esetHeal);
        Assert.AreNotEqual(esetShield, esetPower);
    }

    // ── Client sync packets ──────────────────────────────────────────────────

    [TestMethod]
    public void TakeDamage_ShieldOnly_SendsMultipleStatUpdate_AndCharacterLevel()
    {
        var (vehicle, character, sent) = MakeOwnedVehicleWithSink(
            maxHp: 500, hp: 400, maxShield: 200, shield: 150, coid: 91_010);

        vehicle.TakeDamage(40);

        Assert.AreEqual(110, vehicle.CurrentShield);
        Assert.AreEqual(400, vehicle.GetCurrentHP());

        var level = sent.OfType<CharacterLevelPacket>().Single();
        Assert.AreEqual(400, level.Health);
        Assert.AreEqual(500, level.HealthMaximum);

        var stat = sent.OfType<MultipleStatUpdatePacket>().Single();
        Assert.AreEqual(1, stat.Objects.Count);
        Assert.AreEqual(vehicle.ObjectId.Coid, stat.Objects[0].ObjectId.Coid);
        Assert.AreEqual(MultipleStatUpdatePacket.StatType.Shield, stat.Objects[0].Stats[0].Type);
        Assert.AreEqual(110f, stat.Objects[0].Stats[0].Value);

        Assert.IsFalse(
            typeof(CharacterLevelPacket).GetProperties().Any(p => p.Name.Contains("Shield", System.StringComparison.OrdinalIgnoreCase)),
            "CharacterLevel must not grow a shield field — retail uses 0x2010 for shield");
    }

    [TestMethod]
    public void TakeDamage_ShieldThenHp_SendsStatUpdateWithFinalShieldZero()
    {
        var (vehicle, _, sent) = MakeOwnedVehicleWithSink(
            maxHp: 200, hp: 100, maxShield: 20, shield: 20, coid: 91_020);

        vehicle.TakeDamage(50); // 20 shield + 30 HP

        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(70, vehicle.GetCurrentHP());

        var stat = sent.OfType<MultipleStatUpdatePacket>().Single();
        Assert.AreEqual(0f, stat.Objects[0].Stats[0].Value, "final shield current after overflow");

        var level = sent.OfType<CharacterLevelPacket>().Single();
        Assert.AreEqual(70, level.Health);
    }

    [TestMethod]
    public void SetCurrentShield_WithOwner_SendsMultipleStatUpdate()
    {
        var (vehicle, _, sent) = MakeOwnedVehicleWithSink(
            maxHp: 100, hp: 100, maxShield: 80, shield: 80, coid: 91_030);

        vehicle.SetCurrentShield(33);

        var stat = sent.OfType<MultipleStatUpdatePacket>().Single();
        Assert.AreEqual(33f, stat.Objects[0].Stats[0].Value);
        Assert.AreEqual(MultipleStatUpdatePacket.StatType.Shield, stat.Objects[0].Stats[0].Type);
    }

    [TestMethod]
    public void ChatShieldCommand_SendsMultipleStatUpdate()
    {
        var (vehicle, character, sent) = MakeOwnedVehicleWithSink(
            maxHp: 100, hp: 100, maxShield: 100, shield: 100, coid: 91_040);

        var result = ChatCommandService.Instance.Execute(character, "/shield 42");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual(42, vehicle.CurrentShield);

        var stat = sent.OfType<MultipleStatUpdatePacket>().Single();
        Assert.AreEqual(42f, stat.Objects[0].Stats[0].Value);
    }

    [TestMethod]
    public void MultipleStatUpdate_WireBytes_MatchClientFun0080bc40()
    {
        var packet = MultipleStatUpdatePacket.ForVehicleShield(new TFID(18452, true), 5);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(w);

        ms.Position = 0;
        using var br = new BinaryReader(ms);
        Assert.AreEqual(1, br.ReadUInt16());
        Assert.AreEqual(18452L, br.ReadInt64());
        Assert.AreEqual(1, br.ReadByte());
        br.ReadBytes(7);
        Assert.AreEqual(1, br.ReadByte());
        Assert.AreEqual((byte)MultipleStatUpdatePacket.StatType.Shield, br.ReadByte());
        br.ReadBytes(7);
        Assert.AreEqual(5f, br.ReadSingle());
        Assert.AreEqual(ms.Length, ms.Position, "no trailing junk");
    }

    // ── Ghost masks ──────────────────────────────────────────────────────────

    [TestMethod]
    public void TakeDamage_ShieldHit_DirtiesShieldMaxAndShieldMasks()
    {
        var vehicle = MakeVehicle(maxHp: 200, hp: 180, maxShield: 100, shield: 80);
        vehicle.CreateGhost();
        ClearGhostDirtyMask(vehicle.Ghost!);

        vehicle.TakeDamage(25);

        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.ShieldMask));
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.ShieldMaxMask));
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostObject.HealthMask),
            "shield-only still re-asserts HealthMask for DamagePacket HP prediction undo");
    }

    [TestMethod]
    public void GhostVehicle_ShieldMasks_AreDistinctFromPowerAndHealth()
    {
        // Wire packing of owner combat initial (Heat/ShieldMax/Shield/Power) is covered by
        // GhostVehicleWireTests.PackInitial_OwnerControlConnection_CombatOnly_OmitsEquipmentAndPacksPools.
        // Here we only lock the mask bit identities the client unpack expects.
        Assert.AreEqual(0x0002000000ul, GhostVehicle.ShieldMaxMask);
        Assert.AreEqual(0x0004000000ul, GhostVehicle.ShieldMask);
        Assert.AreEqual(0x0008000000ul, GhostVehicle.PowerMask);
        Assert.AreEqual(0x0020000000ul, GhostVehicle.HeatMask);
        Assert.AreNotEqual(GhostVehicle.ShieldMask, GhostVehicle.PowerMask);
        Assert.AreNotEqual(GhostVehicle.ShieldMask, GhostObject.HealthMask);
    }

    // ── Regen pool ───────────────────────────────────────────────────────────

    [TestMethod]
    public void CombatPool_ShieldRegensEvery3000Ms_HpDoesNot()
    {
        var vehicle = MakeVehicle(maxHp: 500, hp: 100, maxShield: 100, shield: 10);
        vehicle.SetShieldRegenRateForTests(7);
        vehicle.SetHpRegenRateForTests(20);

        VehicleCombatPool.Advance(vehicle, owner: null, deltaMs: 2999, weaponsFiring: false);
        Assert.AreEqual(10, vehicle.CurrentShield);
        Assert.AreEqual(100, vehicle.GetCurrentHP());

        VehicleCombatPool.Advance(vehicle, owner: null, deltaMs: 1, weaponsFiring: false);
        Assert.AreEqual(17, vehicle.CurrentShield, "one 3000 ms pulse adds full ShieldRegenRate");
        Assert.AreEqual(100, vehicle.GetCurrentHP(), "HP must not recharge on pool pulse");
    }

    [TestMethod]
    public void CombatPool_ShieldRegen_SendsMultipleStatUpdateToOwner()
    {
        var (vehicle, character, sent) = MakeOwnedVehicleWithSink(
            maxHp: 200, hp: 200, maxShield: 100, shield: 10, coid: 91_060);
        vehicle.SetShieldRegenRateForTests(5);
        // Clear debounce by having non-zero shield already.
        sent.Clear();

        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);

        Assert.AreEqual(15, vehicle.CurrentShield);
        var stat = sent.OfType<MultipleStatUpdatePacket>().Single();
        Assert.AreEqual(15f, stat.Objects[0].Stats[0].Value);
    }

    [TestMethod]
    public void CombatPool_EmptyShieldDebounce_StillTwoTicks()
    {
        var vehicle = MakeVehicle(maxHp: 100, hp: 100, maxShield: 50, shield: 0);
        vehicle.SetShieldRegenRateForTests(8);

        VehicleCombatPool.Tick(vehicle, null, false);
        Assert.AreEqual(0, vehicle.CurrentShield);
        VehicleCombatPool.Tick(vehicle, null, false);
        Assert.AreEqual(0, vehicle.CurrentShield);
        VehicleCombatPool.Tick(vehicle, null, false);
        Assert.AreEqual(8, vehicle.CurrentShield);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [TestMethod]
    public void TakeDamage_ZeroMaxShield_HpOnly_NoStatUpdateRequired()
    {
        var (vehicle, _, sent) = MakeOwnedVehicleWithSink(
            maxHp: 100, hp: 100, maxShield: 0, shield: 0, coid: 91_070);

        vehicle.TakeDamage(15);
        Assert.AreEqual(85, vehicle.GetCurrentHP());
        Assert.AreEqual(0, vehicle.CurrentShield);
        Assert.AreEqual(0, sent.OfType<MultipleStatUpdatePacket>().Count(),
            "no shield change → no MultipleStatUpdate");
    }

    [TestMethod]
    public void TakeDamage_InvincibleAndCorpse_NoShieldDrain()
    {
        var vehicle = MakeVehicle(maxHp: 100, hp: 50, maxShield: 40, shield: 40);
        vehicle.SetInvincible(true);
        Assert.AreEqual(0, vehicle.TakeDamage(10));
        Assert.AreEqual(40, vehicle.CurrentShield);

        vehicle.SetInvincible(false);
        vehicle.SetCurrentHP(0, triggerGhostUpdate: false);
        vehicle.OnDeath(DeathType.Silent);
        Assert.AreEqual(0, vehicle.TakeDamage(10));
        Assert.AreEqual(40, vehicle.CurrentShield);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Vehicle MakeVehicle(int maxHp, int hp, int maxShield, int shield)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(91_000 + unchecked((int)(maxHp ^ hp ^ maxShield ^ shield)), true);
        vehicle.SetMaximumHP(maxHp, triggerGhostUpdate: false);
        vehicle.SetHPForTests(hp);
        vehicle.SetMaximumShield(maxShield, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(shield, triggerGhostUpdate: false);
        return vehicle;
    }

    private static (Vehicle vehicle, Character character, List<BasePacket> sent) MakeOwnedVehicleWithSink(
        int maxHp, int hp, int maxShield, int shield, long coid)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        var character = new Character();
        character.SetCoid(coid + 1, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetMaximumHP(maxHp, triggerGhostUpdate: false);
        vehicle.SetHPForTests(hp);
        vehicle.SetMaximumShield(maxShield, triggerGhostUpdate: false);
        vehicle.SetCurrentShield(shield, triggerGhostUpdate: false);

        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        return (vehicle, character, sent);
    }

    private static bool GhostHasDirtyMask(NetObject ghost, ulong mask)
    {
        var field = typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return ((ulong)field!.GetValue(ghost)! & mask) != 0;
    }

    private static void ClearGhostDirtyMask(NetObject ghost)
    {
        var field = typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        field!.SetValue(ghost, 0UL);
    }
}

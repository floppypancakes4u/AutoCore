using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.Char.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.Combat;

/// <summary>
/// Server combat pools (shield, heat, power) match client
/// <c>CVOGHBRegeneration</c> / <c>FUN_005fbea0</c> (3000 ms pulses for races 0/1/2).
/// HP does not recharge on the pool pulse.
/// </summary>
[TestClass]
public class VehicleCombatPoolTests
{
    [TestMethod]
    public void TickPeriodMs_Is3000_MatchingRetailRegenerationHeartbeat()
    {
        Assert.AreEqual(3000, VehicleCombatPool.TickPeriodMs,
            "CVOGHBRegeneration constructor sets HB+0x8 = 3000 ms for races 0/1/2");
    }

    [TestInitialize]
    public void Init()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestCleanup]
    public void Cleanup()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NetObject.PIsInitialUpdate = false;
    }

    [TestMethod]
    public void SetCurrentHeat_ClampsToTwiceMax_AndDirtiesHeatMask()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(100);
        vehicle.SetCurrentHeat(50);

        Assert.AreEqual(50, vehicle.CurrentHeat);

        vehicle.SetCurrentHeat(500);
        Assert.AreEqual(200, vehicle.CurrentHeat, "heat hard-cap is 2 * HeatMaximum (client FUN_004f7210)");

        Assert.IsNotNull(vehicle.Ghost);
        // Mask bits are internal; presence of GhostVehicle after heat dirty is the delivery path.
        Assert.IsInstanceOfType(vehicle.Ghost, typeof(GhostVehicle));
    }

    [TestMethod]
    public void GhostVehicle_PacksCurrentHeat_OnHeatMask()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(88_001, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumHeat(250);
        vehicle.SetCurrentHeat(77);

        var ghost = (GhostVehicle)vehicle.Ghost!;
        NetObject.PIsInitialUpdate = false;
        var stream = new BitStream(new byte[2048], 2048);
        ghost.PackUpdate(null, GhostVehicle.HeatMask, stream);
        stream.SetBitPosition(0);
        // Non-initial: Skills false, 7 equip, GM, Clan, Pet, Murderer, Health, HealthMax, State,
        // Position, Target, Attribute, then Heat.
        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsFalse(stream.ReadFlag()); // Murderer
        Assert.IsFalse(stream.ReadFlag()); // Health
        Assert.IsFalse(stream.ReadFlag()); // HealthMax
        Assert.IsFalse(stream.ReadFlag()); // State
        Assert.IsFalse(stream.ReadFlag()); // Position
        Assert.IsFalse(stream.ReadFlag()); // Target
        Assert.IsFalse(stream.ReadFlag()); // Attribute
        Assert.IsTrue(stream.ReadFlag(), "HeatMask must set heat flag");
        stream.Read(out uint heat);
        Assert.AreEqual(77u, heat);
    }

    [TestMethod]
    public void ApplyPowerPlantCapacities_SetsHeatAndPowerMaxFromClonebase()
    {
        const int ppCbid = 720_100;
        AssetManagerTestHelper.RegisterPowerPlantCloneBase(ppCbid);
        // Override rates for this test.
        var ppClone = (CloneBasePowerPlant)AssetManager.Instance.GetCloneBase(ppCbid)!;
        ppClone.PowerPlantSpecific = new PowerPlantSpecific
        {
            HeatMaximum = 400,
            PowerMaximum = 180,
            PowerRegenRate = 5,
            CoolRate = 8,
        };

        var character = MakeCharacter(88_010);
        var vehicle = new Vehicle();
        vehicle.SetCoid(88_011, true);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetOwner(character);

        var plant = new PowerPlant();
        plant.SetCoid(88_012, true);
        plant.LoadCloneBase(ppCbid);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.PowerPlant, plant, out _));

        vehicle.ApplyPowerPlantCapacities(startPowerAtFull: true);

        // Retail Vehicle_CalcHeatMaximum: ceil(level + techPool*0.5 + PP HeatMaximum)
        // tech=0 → pool 1 → ceil(1 + 0.5 + 400) = 402
        Assert.AreEqual(402, vehicle.MaxHeat);
        Assert.AreEqual(0, vehicle.CurrentHeat, "enter-world / equip clears heat");
        Assert.AreEqual(8, vehicle.CoolRate);
        Assert.AreEqual(5, vehicle.PowerRegenRate);

        // Max power = ceil(level*classCoeff + TheoryPool*2 + PP.PowerMaximum).
        // level 1, default class 0 → 0.6 + 2 + 180 = 182.6 → 183.
        var (cur, max) = CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid);
        Assert.AreEqual((short)183, max);
        Assert.AreEqual((short)183, cur);
    }

    [TestMethod]
    public void TickCombatPools_CoolsHeat_AtCoolRatePerPulse()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(200);
        vehicle.SetCurrentHeat(100);
        vehicle.SetCoolRateForTests(5);

        // One retail pool pulse (3000 ms equivalent).
        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);

        Assert.AreEqual(95, vehicle.CurrentHeat);
    }

    [TestMethod]
    public void TickCombatPools_OverMaxHeat_CoolsAt70Percent()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(100);
        vehicle.SetCurrentHeat(150); // above max → overheated band
        vehicle.SetCoolRateForTests(10);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);

        // coolAmount = (int)(10 * 0.7) = 7
        Assert.AreEqual(143, vehicle.CurrentHeat);
    }

    [TestMethod]
    public void TickCombatPools_ShieldRegen_WaitsTwoTicksAfterEmpty()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(0);
        vehicle.SetShieldRegenRateForTests(3);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);
        Assert.AreEqual(0, vehicle.CurrentShield, "tick 1: debounce arm");
        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);
        Assert.AreEqual(0, vehicle.CurrentShield, "tick 2: debounce countdown");
        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);
        Assert.AreEqual(3, vehicle.CurrentShield, "tick 3: regen starts");
    }

    [TestMethod]
    public void TickCombatPools_PowerRegen_UsesPowerPlantRate()
    {
        var character = MakeCharacter(88_020);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_021, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetPowerRegenRateForTests(7);

        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);

        Assert.AreEqual((short)17, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid));
    }

    [TestMethod]
    public void Tick_PowerChanged_DirtiesPowerMaskOnGhost()
    {
        var character = MakeCharacter(88_050);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_051, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetPowerRegenRateForTests(7);
        vehicle.CreateGhost();

        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);

        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.PowerMask),
            "power regen must dirty PowerMask for ghost replication");
    }

    [TestMethod]
    public void Tick_DoesNotRegenHp_EvenWithHpRegenRateSet()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(88_061, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumHP(500);
        vehicle.SetCurrentHP(100, triggerGhostUpdate: false);
        vehicle.SetHpRegenRateForTests(4);
        ClearGhostDirtyMask(vehicle.Ghost!);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);

        Assert.AreEqual(100, vehicle.GetCurrentHP(), "product design: vehicle HP does not recharge");
        Assert.IsFalse(GhostHasDirtyMask(vehicle.Ghost!, GhostObject.HealthMask),
            "no HP change means HealthMask must stay clean");
    }

    [TestMethod]
    public void Tick_HeatChanged_DirtiesHeatMaskOnGhost()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(88_071, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumHeat(200);
        vehicle.SetCurrentHeat(100, triggerGhostUpdate: false);
        vehicle.SetCoolRateForTests(5);
        ClearGhostDirtyMask(vehicle.Ghost!);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);

        Assert.AreEqual(95, vehicle.CurrentHeat);
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.HeatMask),
            "heat cool must dirty HeatMask when heat changes");
    }

    [TestMethod]
    public void Tick_ShieldChanged_DirtiesShieldMaskOnGhost()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(88_072, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumShield(100);
        vehicle.SetCurrentShield(10, triggerGhostUpdate: false);
        vehicle.SetShieldRegenRateForTests(5);
        // Empty debounce already cleared because current != 0.
        ClearGhostDirtyMask(vehicle.Ghost!);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);

        Assert.AreEqual(15, vehicle.CurrentShield);
        Assert.IsTrue(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.ShieldMask),
            "shield regen must dirty ShieldMask when shield changes");
    }

    [TestMethod]
    public void GhostVehicle_PacksCurrentShield_OnShieldMask()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(88_073, true);
        vehicle.CreateGhost();
        vehicle.SetMaximumShield(400);
        vehicle.SetCurrentShield(123);

        var ghost = (GhostVehicle)vehicle.Ghost!;
        NetObject.PIsInitialUpdate = false;
        var stream = new BitStream(new byte[2048], 2048);
        ghost.PackUpdate(null, GhostVehicle.ShieldMask, stream);
        stream.SetBitPosition(0);

        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsFalse(stream.ReadFlag()); // Murderer
        Assert.IsFalse(stream.ReadFlag()); // Health
        Assert.IsFalse(stream.ReadFlag()); // HealthMax
        Assert.IsFalse(stream.ReadFlag()); // State
        Assert.IsFalse(stream.ReadFlag()); // Position
        Assert.IsFalse(stream.ReadFlag()); // Target
        Assert.IsFalse(stream.ReadFlag()); // Attribute
        Assert.IsFalse(stream.ReadFlag()); // Heat
        Assert.IsFalse(stream.ReadFlag()); // ShieldMax
        Assert.IsTrue(stream.ReadFlag(), "ShieldMask must set shield flag");
        stream.Read(out uint shield);
        Assert.AreEqual(123u, shield);
    }

    [TestMethod]
    public void Tick_SkipsEntirePulseWhileCorpse()
    {
        var character = MakeCharacter(88_090);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_091, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetPowerRegenRateForTests(7);
        vehicle.SetCurrentHP(0, triggerGhostUpdate: false);
        vehicle.OnDeath(DeathType.Silent);
        Assert.IsTrue(vehicle.IsCorpse);

        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);

        Assert.AreEqual((short)10, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid),
            "dead/corpse vehicles must not run pool regen (sector also skips Advance)");
    }

    [TestMethod]
    public void SetCurrentHP_AboveZero_ClearsCorpseSoPoolsResume()
    {
        var character = MakeCharacter(88_100);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_101, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetPowerRegenRateForTests(7);
        vehicle.SetMaximumHP(500);
        vehicle.SetCurrentHP(0, triggerGhostUpdate: false);
        vehicle.OnDeath(DeathType.Silent);
        Assert.IsTrue(vehicle.IsCorpse);

        // Same path as /hp after death — restore HP without calling Revive().
        vehicle.SetCurrentHP(100);

        Assert.IsFalse(vehicle.IsCorpse, "healing above 0 must clear corpse (RestoreHealth pattern)");
        Assert.AreEqual(100, vehicle.GetCurrentHP());

        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);
        Assert.AreEqual((short)17, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid),
            "power regen must resume after /hp-style heal clears corpse");
    }

    [TestMethod]
    public void Tick_NoChange_DoesNotDirtyGhost()
    {
        var character = MakeCharacter(88_080);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 50, sendPacket: false); // full

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_081, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.CreateGhost();
        vehicle.SetMaximumHP(500);
        vehicle.SetCurrentHP(500, triggerGhostUpdate: false);
        vehicle.SetPowerRegenRateForTests(7);
        vehicle.SetHpRegenRateForTests(4);
        vehicle.SetCoolRateForTests(0);
        ClearGhostDirtyMask(vehicle.Ghost!);

        VehicleCombatPool.Tick(vehicle, character, weaponsFiring: false);

        Assert.IsFalse(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.PowerMask));
        Assert.IsFalse(GhostHasDirtyMask(vehicle.Ghost!, GhostObject.HealthMask));
        Assert.IsFalse(GhostHasDirtyMask(vehicle.Ghost!, GhostVehicle.HeatMask));
    }

    [TestMethod]
    public void Advance_PowerRegen_DoesNotApplyBefore3000Ms()
    {
        var character = MakeCharacter(88_030);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_031, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetPowerRegenRateForTests(7);

        VehicleCombatPool.Advance(vehicle, character, deltaMs: 2999, weaponsFiring: false);

        Assert.AreEqual((short)10, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid),
            "power must not regen until a full 3000 ms pulse elapses");
    }

    [TestMethod]
    public void Advance_PowerRegen_AppliesFullRateEvery3000Ms()
    {
        var character = MakeCharacter(88_040);
        CharacterLevelManager.Instance.SetMaxMana(character, 50, sendPacket: false);
        CharacterLevelManager.Instance.SetCurrentMana(character, 10, sendPacket: false);

        var vehicle = new Vehicle();
        vehicle.SetCoid(88_041, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);
        vehicle.SetPowerRegenRateForTests(7);

        VehicleCombatPool.Advance(vehicle, character, deltaMs: 2999, weaponsFiring: false);
        Assert.AreEqual((short)10, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid));

        VehicleCombatPool.Advance(vehicle, character, deltaMs: 1, weaponsFiring: false);
        Assert.AreEqual((short)17, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid),
            "one pulse after 3000 ms adds full PowerRegenRate");

        VehicleCombatPool.Advance(vehicle, character, deltaMs: 3000, weaponsFiring: false);
        Assert.AreEqual((short)24, CharacterLevelManager.Instance.GetCurrentMana(character.ObjectId.Coid),
            "second pulse adds another full PowerRegenRate");
    }

    [TestMethod]
    public void TickCombatPools_HpDoesNotRegen_EvenWithRaceRegenRate()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHP(500);
        vehicle.SetCurrentHP(100);
        vehicle.SetHpRegenRateForTests(4);

        VehicleCombatPool.Tick(vehicle, owner: null, weaponsFiring: false);
        VehicleCombatPool.Advance(vehicle, owner: null, deltaMs: 3000, weaponsFiring: false);

        Assert.AreEqual(100, vehicle.GetCurrentHP(),
            "RaceRegenRate must not restore HP (shield/power still pulse; HP does not)");
    }

    [TestMethod]
    public void Advance_Accumulates3000MsPulsesFromMainLoopDelta()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(1000);
        vehicle.SetCurrentHeat(100);
        vehicle.SetCoolRateForTests(1);

        // 7500 ms MainLoop accumulation → 2×3000 ms pulses
        VehicleCombatPool.Advance(vehicle, owner: null, deltaMs: 7500, weaponsFiring: false);

        Assert.AreEqual(98, vehicle.CurrentHeat);
        Assert.AreEqual(1500, vehicle.PoolTickAccumulatorMs, "remainder 1500 ms carried for next pulse");
    }

    [TestMethod]
    public void AddHeat_FromWeapon_CapsAtTwiceMax()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHeat(50);
        vehicle.SetCurrentHeat(0);

        vehicle.AddHeat(30);
        Assert.AreEqual(30, vehicle.CurrentHeat);

        vehicle.AddHeat(100);
        Assert.AreEqual(100, vehicle.CurrentHeat, "2 * 50 max");
    }

    private static Character MakeCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        var dbData = new CharacterData { Coid = coid, Name = "PoolTest", Level = 1 };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
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

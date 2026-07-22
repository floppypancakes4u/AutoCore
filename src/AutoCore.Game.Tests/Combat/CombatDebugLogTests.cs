using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Game.Combat;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Npc;

[TestClass]
public class CombatDebugLogTests
{
    [TestInitialize]
    public void SetUp() => ServerConfig.ResetToDefaults();

    [TestCleanup]
    public void TearDown() => ServerConfig.ResetToDefaults();

    [TestMethod]
    public void IsPlayerOwnedCombatTarget_PlayerVehicle_ReturnsTrue()
    {
        var vehicle = new Vehicle();
        var character = new Character();
        vehicle.SetOwner(character);

        Assert.IsTrue(CombatDebugLog.IsPlayerOwnedCombatTarget(vehicle));
    }

    [TestMethod]
    public void IsPlayerOwnedCombatTarget_NpcVehicle_ReturnsFalse()
    {
        var vehicle = new Vehicle();
        vehicle.NpcAi = new NpcAiState();

        Assert.IsFalse(CombatDebugLog.IsPlayerOwnedCombatTarget(vehicle));
    }

    [TestMethod]
    public void IsPlayerOwnedCombatTarget_NpcVehicleWithCharacterOwner_ReturnsFalse()
    {
        // NpcAi wins over character owner (AI-driven chassis).
        var vehicle = new Vehicle();
        vehicle.SetOwner(new Character());
        vehicle.NpcAi = new NpcAiState();

        Assert.IsFalse(CombatDebugLog.IsPlayerOwnedCombatTarget(vehicle));
    }

    [TestMethod]
    public void IsPlayerOwnedCombatTarget_Creature_ReturnsFalse()
    {
        Assert.IsFalse(CombatDebugLog.IsPlayerOwnedCombatTarget(new Creature()));
    }

    [TestMethod]
    public void IsPlayerOwnedCombatTarget_Null_ReturnsFalse()
    {
        Assert.IsFalse(CombatDebugLog.IsPlayerOwnedCombatTarget(null));
    }

    [TestMethod]
    public void ShouldLogDamage_PlayerTarget_HonorsLogDamageToPlayers()
    {
        var attacker = MakeNpcVehicle();
        var vehicle = new Vehicle();
        vehicle.SetOwner(new Character());

        Assert.IsTrue(CombatDebugLog.ShouldLogDamage(attacker, vehicle));

        ServerConfig.LogDamageToPlayers = false;
        Assert.IsFalse(CombatDebugLog.ShouldLogDamage(attacker, vehicle));
    }

    [TestMethod]
    public void ShouldLogDamage_PlayerAttackingNpc_HonorsLogDamageToNpcs()
    {
        var attacker = MakePlayerVehicle();
        var vehicle = MakeNpcVehicle();

        Assert.IsTrue(CombatDebugLog.ShouldLogDamage(attacker, vehicle));

        ServerConfig.LogDamageToNpcs = false;
        Assert.IsFalse(CombatDebugLog.ShouldLogDamage(attacker, vehicle));
    }

    [TestMethod]
    public void ShouldLogDamage_Creature_HonorsLogDamageToNpcs_WhenPlayerAttacker()
    {
        var attacker = MakePlayerVehicle();
        var creature = new Creature();

        Assert.IsTrue(CombatDebugLog.ShouldLogDamage(attacker, creature));

        ServerConfig.LogDamageToNpcs = false;
        Assert.IsFalse(CombatDebugLog.ShouldLogDamage(attacker, creature));
    }

    [TestMethod]
    public void ShouldLogDamage_NullTarget_ReturnsFalse()
    {
        Assert.IsFalse(CombatDebugLog.ShouldLogDamage(MakePlayerVehicle(), null));
    }

    [TestMethod]
    public void ShouldLogDamage_NpcToNpc_DefaultOff()
    {
        var attacker = MakeNpcVehicle();
        var target = MakeNpcVehicle();

        Assert.IsFalse(ServerConfig.LogNpcToNpc);
        Assert.IsFalse(CombatDebugLog.ShouldLogDamage(attacker, target));
        Assert.IsFalse(CombatDebugLog.ShouldLogHitChanceRoll(attacker, target));
    }

    [TestMethod]
    public void ShouldLogDamage_NpcToNpc_WhenEnabled_HonorsLogDamageToNpcs()
    {
        ServerConfig.LogNpcToNpc = true;
        var attacker = MakeNpcVehicle();
        var target = MakeNpcVehicle();

        Assert.IsTrue(CombatDebugLog.ShouldLogDamage(attacker, target));

        ServerConfig.LogDamageToNpcs = false;
        Assert.IsFalse(CombatDebugLog.ShouldLogDamage(attacker, target));
    }

    [TestMethod]
    public void ShouldLogHitChanceRoll_PlayerInvolved_HonorsServerConfig()
    {
        var player = MakePlayerVehicle();
        var npc = MakeNpcVehicle();

        Assert.IsTrue(CombatDebugLog.ShouldLogHitChanceRoll(player, npc));
        Assert.IsTrue(CombatDebugLog.ShouldLogHitChanceRoll(npc, player));

        ServerConfig.LogHitChanceRolls = false;
        Assert.IsFalse(CombatDebugLog.ShouldLogHitChanceRoll(player, npc));
    }

    [TestMethod]
    public void ShouldLogHitChanceRoll_NpcToNpc_RequiresLogNpcToNpc()
    {
        var a = MakeNpcVehicle();
        var b = MakeNpcVehicle();

        Assert.IsFalse(CombatDebugLog.ShouldLogHitChanceRoll(a, b));

        ServerConfig.LogNpcToNpc = true;
        Assert.IsTrue(CombatDebugLog.ShouldLogHitChanceRoll(a, b));
    }

    [TestMethod]
    public void IsNpcToNpc_BothNpc_ReturnsTrue()
    {
        Assert.IsTrue(CombatDebugLog.IsNpcToNpc(MakeNpcVehicle(), MakeNpcVehicle()));
        Assert.IsTrue(CombatDebugLog.IsNpcToNpc(MakeNpcVehicle(), new Creature()));
    }

    [TestMethod]
    public void IsNpcToNpc_PlayerInvolved_ReturnsFalse()
    {
        var player = MakePlayerVehicle();
        var npc = MakeNpcVehicle();
        Assert.IsFalse(CombatDebugLog.IsNpcToNpc(player, npc));
        Assert.IsFalse(CombatDebugLog.IsNpcToNpc(npc, player));
    }

    private static Vehicle MakePlayerVehicle()
    {
        var vehicle = new Vehicle();
        vehicle.SetOwner(new Character());
        return vehicle;
    }

    private static Vehicle MakeNpcVehicle()
    {
        var vehicle = new Vehicle();
        vehicle.NpcAi = new NpcAiState();
        return vehicle;
    }
}

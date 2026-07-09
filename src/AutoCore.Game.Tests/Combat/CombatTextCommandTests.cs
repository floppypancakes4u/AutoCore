using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class CombatTextCommandTests
{
    private static CombatTextCommand.Context SelfCtx() => new()
    {
        HasVehicle = true,
        Source = new TFID(100, false),
        Target = new TFID(100, false),
        HasExplicitTarget = false,
        TargetTypeName = "Vehicle"
    };

    private static CombatTextCommand.Context TargetCtx() => new()
    {
        HasVehicle = true,
        Source = new TFID(100, false),
        Target = new TFID(200, true),
        HasExplicitTarget = true,
        TargetTypeName = "Creature"
    };

    [TestMethod]
    public void Execute_NoVehicle_ReturnsHint()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "dmg" }, new CombatTextCommand.Context { HasVehicle = false });
        Assert.IsTrue(result.Message.Contains("vehicle", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, result.Packets.Count);
    }

    [TestMethod]
    public void Execute_NullContext_ReturnsHint()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct" }, null);
        Assert.IsTrue(result.Message.Contains("vehicle", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_Help_NoPackets()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "help" }, SelfCtx());
        Assert.IsTrue(result.Message.Contains("Combat text", StringComparison.OrdinalIgnoreCase)
                      || result.Message.Contains("dmg", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, result.Packets.Count);
    }

    [TestMethod]
    public void Execute_QuestionMark_IsHelp()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "?" }, SelfCtx());
        Assert.IsTrue(result.Message.Contains("dmg", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_DefaultArg_IsHelp()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct" }, SelfCtx());
        Assert.IsTrue(result.Message.Contains("dmg", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_Go_SendsDamage42WithSkipOpcode()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "go" }, TargetCtx());
        Assert.AreEqual(1, result.Packets.Count);
        Assert.IsTrue(result.Packets[0].SkipOpcode);
        var dmg = (DamagePacket)result.Packets[0].Packet;
        Assert.AreEqual(GameOpcode.Damage, dmg.Opcode);
        Assert.AreEqual(1, dmg.Entries.Count);
        Assert.AreEqual(42, dmg.Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Test_AliasOfGo()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "test" }, SelfCtx());
        Assert.AreEqual(1, result.Packets.Count);
    }

    [TestMethod]
    public void Execute_Dmg_Default50()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "dmg" }, TargetCtx());
        Assert.AreEqual(50, ((DamagePacket)result.Packets[0].Packet).Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Hit_AliasOfDmg()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "hit", "99" }, TargetCtx());
        Assert.AreEqual(99, ((DamagePacket)result.Packets[0].Packet).Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Damage_AliasWithAmount()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "damage", "250" }, TargetCtx());
        var dmg = (DamagePacket)result.Packets[0].Packet;
        Assert.AreEqual(250, dmg.Entries[0].Amount);
        Assert.AreEqual(200L, dmg.Entries[0].Target.Coid);
        Assert.AreEqual(100L, dmg.Source.Coid);
    }

    [TestMethod]
    public void Execute_Dmg_ClampsAboveSafeMax()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "dmg", "99999" }, SelfCtx());
        Assert.AreEqual(DamagePacket.MaxDisplayAmount, ((DamagePacket)result.Packets[0].Packet).Entries[0].Amount);
        Assert.IsTrue(result.Message.Contains("clamped"));
    }

    [TestMethod]
    public void Execute_Dmg_ClampsBelowOne()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "dmg", "0" }, SelfCtx());
        Assert.AreEqual(1, ((DamagePacket)result.Packets[0].Packet).Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Dmg_InvalidAmountKeepsDefault()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "dmg", "nope" }, SelfCtx());
        Assert.AreEqual(50, ((DamagePacket)result.Packets[0].Packet).Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Resist_SendsFlagWithAmount1()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "resist" }, TargetCtx());
        var dmg = (DamagePacket)result.Packets[0].Packet;
        Assert.AreEqual(1, dmg.Entries[0].Amount);
        Assert.IsTrue(dmg.Entries[0].Flags.IsResist);
        Assert.IsFalse(dmg.Entries[0].Flags.IsDeflect);
        Assert.IsFalse(dmg.Entries[0].Flags.IsCrit);
    }

    [TestMethod]
    public void Execute_Deflect_SendsFlagWithAmount1()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "deflect" }, TargetCtx());
        Assert.IsTrue(((DamagePacket)result.Packets[0].Packet).Entries[0].Flags.IsDeflect);
    }

    [TestMethod]
    public void Execute_Crit_DefaultAndCustomAmount()
    {
        var def = CombatTextCommand.Execute(new[] { "/ct", "crit" }, SelfCtx());
        Assert.AreEqual(75, ((DamagePacket)def.Packets[0].Packet).Entries[0].Amount);
        Assert.IsTrue(((DamagePacket)def.Packets[0].Packet).Entries[0].Flags.IsCrit);

        var custom = CombatTextCommand.Execute(new[] { "/ct", "crit", "120" }, SelfCtx());
        Assert.AreEqual(120, ((DamagePacket)custom.Packets[0].Packet).Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Crit_ClampsHugeAmount()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "crit", "99999" }, SelfCtx());
        Assert.AreEqual(DamagePacket.MaxDisplayAmount, ((DamagePacket)result.Packets[0].Packet).Entries[0].Amount);
    }

    [TestMethod]
    public void Execute_Xp_SendsGiveXPPacket()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "xp", "777" }, SelfCtx());
        Assert.AreEqual(1, result.Packets.Count);
        Assert.IsFalse(result.Packets[0].SkipOpcode);
        var xp = (GiveXPPacket)result.Packets[0].Packet;
        Assert.AreEqual(GameOpcode.GiveXP, xp.Opcode);
        Assert.AreEqual(777, xp.Amount);
    }

    [TestMethod]
    public void Execute_GiveXp_AliasDefaultAmount()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "givexp" }, SelfCtx());
        Assert.AreEqual(500, ((GiveXPPacket)result.Packets[0].Packet).Amount);
    }

    [TestMethod]
    public void Execute_Credits_SendsNoPacket_NotCombatText()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "credits", "42" }, SelfCtx());
        Assert.AreEqual(0, result.Packets.Count);
        Assert.IsTrue(result.Message.Contains("Credits", StringComparison.OrdinalIgnoreCase)
                      || result.Message.Contains("currency", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(result.Message.Contains("No packet", StringComparison.OrdinalIgnoreCase)
                      || result.Message.Contains("/currency", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_GiveCredits_AliasSendsNoPacket()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "givecredits" }, SelfCtx());
        Assert.AreEqual(0, result.Packets.Count);
    }

    [TestMethod]
    public void Execute_Miss_SendsNoPacket_PureCombatTextOnly()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "miss" }, TargetCtx());
        Assert.AreEqual(0, result.Packets.Count);
        Assert.IsTrue(result.Message.Contains("Miss", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(result.Message.Contains("No packet", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_Hp_SendsNoPacket_PureCombatTextOnly()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "hp" }, SelfCtx());
        Assert.AreEqual(0, result.Packets.Count);
        Assert.IsTrue(result.Message.Contains("HP", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(result.Message.Contains("No packet", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_Pp_SendsNoPacket_PureCombatTextOnly()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "pp" }, SelfCtx());
        Assert.AreEqual(0, result.Packets.Count);
        Assert.IsTrue(result.Message.Contains("PP", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(result.Message.Contains("No packet", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Execute_Unknown_ReturnsHint()
    {
        var result = CombatTextCommand.Execute(new[] { "/ct", "banana" }, SelfCtx());
        Assert.IsTrue(result.Message.Contains("Unknown"));
        Assert.AreEqual(0, result.Packets.Count);
    }

    [TestMethod]
    public void BuildDamagePacket_NullSourceAndTarget_UsesDefaults()
    {
        var packet = CombatTextCommand.BuildDamagePacket(null, null, 10);
        Assert.IsNotNull(packet.Source);
        Assert.AreEqual(1, packet.Entries.Count);
        Assert.IsNotNull(packet.Entries[0].Target);
        Assert.AreEqual(10, packet.Entries[0].Amount);
    }
}

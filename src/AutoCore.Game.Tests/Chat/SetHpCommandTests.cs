using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Chat;

[TestClass]
public class SetHpCommandTests
{
    [TestCleanup]
    public void Cleanup()
    {
        CharacterLevelManager.Instance.ClearAllForTests();
    }

    private static Character CharacterWithVehicle(out Vehicle vehicle)
    {
        vehicle = new Vehicle();
        // Default SimpleObject ctor sets HP = MaxHP = 500.
        var character = new Character();
        character.SetCoid(9001, true);
        character.SetCurrentVehicleForTests(vehicle);
        return character;
    }

    [TestMethod]
    public void SetHP_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/setHP 50");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void SetHP_WithoutVehicle_ReturnsError()
    {
        var character = new Character();

        var result = ChatCommandService.Instance.Execute(character, "/setHP 50");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "vehicle");
    }

    [TestMethod]
    public void SetHP_InvalidArgs_ReturnsUsage()
    {
        var character = CharacterWithVehicle(out _);

        var missing = ChatCommandService.Instance.Execute(character, "/setHP");
        var bad = ChatCommandService.Instance.Execute(character, "/setHP nope");

        Assert.IsTrue(missing.Handled);
        StringAssert.Contains(missing.Message, "Usage:");
        Assert.IsTrue(bad.Handled);
        StringAssert.Contains(bad.Message, "Usage:");
    }

    [TestMethod]
    public void SetHP_SetsCurrentHpAndReportsValues()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var result = ChatCommandService.Instance.Execute(character, "/setHP 100");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(100, vehicle.GetCurrentHP());
        Assert.AreEqual(500, vehicle.GetMaximumHP());
        StringAssert.Contains(result.Message, "100/500");
        // Owner HUD uses CharacterLevel (0x2017) absolute Health fields — same path as /power.
        var level = result.Packets.OfType<CharacterLevelPacket>().Single();
        Assert.AreEqual(100, level.Health);
        Assert.AreEqual(500, level.HealthMaximum);
    }

    [TestMethod]
    public void SetHP_ClampsAboveMaxAndBelowZero()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var high = ChatCommandService.Instance.Execute(character, "/setHP 99999");
        Assert.AreEqual(500, vehicle.GetCurrentHP());
        StringAssert.Contains(high.Message, "500/500");

        var low = ChatCommandService.Instance.Execute(character, "/setHP -10");
        Assert.AreEqual(0, vehicle.GetCurrentHP());
        StringAssert.Contains(low.Message, "0/500");
    }

    [TestMethod]
    public void SetHP_AliasHp_IsHandled()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var result = ChatCommandService.Instance.Execute(character, "/hp 42");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(42, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void SetHP_AfterDeath_ClearsCorpseFlag()
    {
        var character = CharacterWithVehicle(out var vehicle);
        vehicle.SetCurrentHP(0);
        vehicle.OnDeath(DeathType.Silent);
        Assert.IsTrue(vehicle.IsCorpse);

        var result = ChatCommandService.Instance.Execute(character, "/hp 250");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(250, vehicle.GetCurrentHP());
        Assert.IsFalse(vehicle.IsCorpse,
            "/hp after death must clear corpse so combat pools and living state resume");
    }

    [TestMethod]
    public void SetMaxHP_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/setMaxHP 1000");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void SetMaxHP_WithoutVehicle_ReturnsError()
    {
        var character = new Character();

        var result = ChatCommandService.Instance.Execute(character, "/setMaxHP 1000");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "vehicle");
    }

    [TestMethod]
    public void SetMaxHP_InvalidArgs_ReturnsUsage()
    {
        var character = CharacterWithVehicle(out _);

        var missing = ChatCommandService.Instance.Execute(character, "/setMaxHP");
        var bad = ChatCommandService.Instance.Execute(character, "/setMaxHP nope");

        Assert.IsTrue(missing.Handled);
        StringAssert.Contains(missing.Message, "Usage:");
        Assert.IsTrue(bad.Handled);
        StringAssert.Contains(bad.Message, "Usage:");
    }

    [TestMethod]
    public void SetMaxHP_SetsMaxAndReportsValues()
    {
        var character = CharacterWithVehicle(out var vehicle);
        vehicle.SetCurrentHP(200, triggerGhostUpdate: false);

        var result = ChatCommandService.Instance.Execute(character, "/setMaxHP 1000");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(1000, vehicle.GetMaximumHP());
        Assert.AreEqual(200, vehicle.GetCurrentHP());
        StringAssert.Contains(result.Message, "200/1000");
        var level = result.Packets.OfType<CharacterLevelPacket>().Single();
        Assert.AreEqual(200, level.Health);
        Assert.AreEqual(1000, level.HealthMaximum);
    }

    [TestMethod]
    public void SetMaxHP_ClampsCurrentWhenMaxDrops()
    {
        var character = CharacterWithVehicle(out var vehicle);
        // Default 500/500; lower max should pull current down.
        var result = ChatCommandService.Instance.Execute(character, "/setMaxHP 50");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(50, vehicle.GetMaximumHP());
        Assert.AreEqual(50, vehicle.GetCurrentHP());
        StringAssert.Contains(result.Message, "50/50");
    }

    [TestMethod]
    public void SetMaxHP_ClampsBelowOneAndAboveWireMax()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var low = ChatCommandService.Instance.Execute(character, "/setMaxHP 0");
        Assert.AreEqual(1, vehicle.GetMaximumHP());
        Assert.AreEqual(1, vehicle.GetCurrentHP());

        var high = ChatCommandService.Instance.Execute(character, "/setMaxHP 999999");
        Assert.AreEqual(SimpleObject.MaxWireHP, vehicle.GetMaximumHP());
        StringAssert.Contains(high.Message, $"/{SimpleObject.MaxWireHP}");
    }

    [TestMethod]
    public void SetMaxHP_AliasMhp_IsHandled()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var result = ChatCommandService.Instance.Execute(character, "/mhp 777");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(777, vehicle.GetMaximumHP());
    }

    [TestMethod]
    public void SimpleObject_SetCurrentHP_ClampsToMax()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHP(100);
        vehicle.SetCurrentHP(150);

        Assert.AreEqual(100, vehicle.GetCurrentHP());
        Assert.AreEqual(100, vehicle.GetMaximumHP());
    }

    [TestMethod]
    public void SimpleObject_SetMaximumHP_ClampsCurrentDown()
    {
        var vehicle = new Vehicle();
        vehicle.SetMaximumHP(1000);
        vehicle.SetCurrentHP(800);
        vehicle.SetMaximumHP(100);

        Assert.AreEqual(100, vehicle.GetMaximumHP());
        Assert.AreEqual(100, vehicle.GetCurrentHP());
    }

    [TestMethod]
    public void Vehicle_SetCurrentHP_CreatesGhostWhenMissing()
    {
        var vehicle = new Vehicle();
        Assert.IsNull(vehicle.Ghost);

        vehicle.SetCurrentHP(10);

        Assert.IsNotNull(vehicle.Ghost, "HP dirty must ensure ghost exists for client delivery");
        Assert.IsInstanceOfType(vehicle.Ghost, typeof(GhostVehicle));
    }

    [TestMethod]
    public void Vehicle_SetCurrentHP_WithOwningConnection_SendsCharacterLevelPacket()
    {
        var character = CharacterWithVehicle(out var vehicle);
        vehicle.SetOwner(character);
        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var sent = new System.Collections.Generic.List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        try
        {
            vehicle.SetCurrentHP(77);

            var level = sent.OfType<CharacterLevelPacket>().Single();
            Assert.AreEqual(77, level.Health);
            Assert.AreEqual(vehicle.GetMaximumHP(), level.HealthMaximum);
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
        }
    }

    [TestMethod]
    public void Vehicle_TakeDamage_WithOwningConnection_SendsCharacterLevelPacket()
    {
        var character = CharacterWithVehicle(out var vehicle);
        vehicle.SetOwner(character);
        vehicle.SetMaximumHP(500, triggerGhostUpdate: false);
        vehicle.SetCurrentHP(200, triggerGhostUpdate: false);
        var connection = new TNLConnection();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var sent = new System.Collections.Generic.List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        try
        {
            vehicle.TakeDamage(40);

            Assert.AreEqual(160, vehicle.GetCurrentHP());
            var level = sent.OfType<CharacterLevelPacket>().Single();
            Assert.AreEqual(160, level.Health);
        }
        finally
        {
            TNLConnection.TestPacketSink = null;
        }
    }

    [TestMethod]
    public void Shield_SetMaxThenCurrent_ClampsAndReports()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var maxResult = ChatCommandService.Instance.Execute(character, "/mshield 500");
        Assert.IsTrue(maxResult.Handled);
        Assert.AreEqual(500, vehicle.MaxShield);
        StringAssert.Contains(maxResult.Message, "500");
        Assert.IsNotNull(vehicle.Ghost, "shield max dirty must ensure ghost for ShieldMaxMask delivery");

        var curResult = ChatCommandService.Instance.Execute(character, "/shield 250");
        Assert.IsTrue(curResult.Handled);
        Assert.AreEqual(250, vehicle.CurrentShield);
        StringAssert.Contains(curResult.Message, "250/500");

        var over = ChatCommandService.Instance.Execute(character, "/shield 9999");
        Assert.AreEqual(500, vehicle.CurrentShield);

        var dropMax = ChatCommandService.Instance.Execute(character, "/mshield 100");
        Assert.AreEqual(100, vehicle.MaxShield);
        Assert.AreEqual(100, vehicle.CurrentShield);
    }

    [TestMethod]
    public void Shield_WithoutVehicle_ReturnsError()
    {
        var character = new Character();
        var result = ChatCommandService.Instance.Execute(character, "/shield 10");
        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "vehicle");
    }

    [TestMethod]
    public void Power_SetsUnifiedCurrentAndMaximumState_AndSendsLevelPacket()
    {
        var character = CharacterWithVehicle(out var vehicle);

        var maxResult = ChatCommandService.Instance.Execute(character, "/mpower 200");
        Assert.IsTrue(maxResult.Handled);
        var state = CharacterLevelManager.Instance.GetOrCreate(character.ObjectId.Coid);
        Assert.AreEqual((short)200, state.MaxMana);
        Assert.IsTrue(maxResult.Packets.OfType<CharacterLevelPacket>().Any(p => p.MaxMana == 200));

        var curResult = ChatCommandService.Instance.Execute(character, "/power 50");
        Assert.IsTrue(curResult.Handled);
        Assert.AreEqual((short)50, state.CurrentMana);
        Assert.AreEqual((short)50, state.MaxMana);
        var packet = curResult.Packets.OfType<CharacterLevelPacket>().Single();
        Assert.AreEqual((short)50, packet.CurrentMana);
        Assert.AreEqual((short)50, packet.MaxMana);
        StringAssert.Contains(curResult.Message, "50/50");
    }

    [TestMethod]
    public void Power_ReplacesExistingMaximum()
    {
        var character = CharacterWithVehicle(out _);
        ChatCommandService.Instance.Execute(character, "/mpower 30");
        ChatCommandService.Instance.Execute(character, "/power 100");

        var state = CharacterLevelManager.Instance.GetOrCreate(character.ObjectId.Coid);
        Assert.AreEqual((short)100, state.CurrentMana);
        Assert.AreEqual((short)100, state.MaxMana);
    }

    [TestMethod]
    public void Power_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/power 10");
        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void Power_WithoutValue_ReportsServerState()
    {
        var character = CharacterWithVehicle(out _);
        CharacterLevelManager.Instance.SetPower(character, 100, sendPacket: false);

        var result = ChatCommandService.Instance.Execute(character, "/power");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("Server power: 100/100.", result.Message);
        Assert.AreEqual(0, result.Packets.Count);
    }
}

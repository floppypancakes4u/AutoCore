using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class CurrencySyncTests
{
    [TestMethod]
    public void ClearCreateCharacterCredits_ZerosBothFields()
    {
        var packet = new CreateCharacterExtendedPacket
        {
            Credits = 1_234_567_890L,
            CreditDebt = 99
        };

        CurrencySync.ClearCreateCharacterCredits(packet);

        Assert.AreEqual(0L, packet.Credits);
        Assert.AreEqual(0L, packet.CreditDebt);
    }

    [TestMethod]
    public void ClearCreateCharacterCredits_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CurrencySync.ClearCreateCharacterCredits(null));
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_ZeroBalance_ReturnsNull()
    {
        var character = CreateCharacter(coid: 1, startingCredits: 0);
        Assert.IsNull(CurrencySync.TryCreateLoginRestorePacket(character));
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_NonZero_BuildsCharacterLevelAbsolute()
    {
        var character = CreateCharacter(coid: 55, startingCredits: 1_002_003_004L);
        var packet = CurrencySync.TryCreateLoginRestorePacket(character);

        Assert.IsNotNull(packet);
        Assert.AreEqual(character.ObjectId, packet.CharacterId);
        Assert.AreEqual(character.Level, packet.Level);
        Assert.AreEqual(1_002_003_004L, packet.Currency);
    }

    [TestMethod]
    public void LoginRestore_MatchesCurrencyCommandPacket_SameAbsoluteBalance()
    {
        // Login must do the same client update as /currency: CharacterLevel absolute set.
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(coid: 42, startingCredits: 0, inventory);

        var command = CurrencySync.TryApplyCurrencyCommand(
            character,
            new[] { "/currency", "1", "2", "3", "4" });
        Assert.IsTrue(command.Success);
        Assert.IsNotNull(command.Packet);

        // Simulate relog: in-memory balance may be stale; authoritative value is in persistence.
        character.SetCredits(0);
        persistence.CreditsToLoad = command.Absolute;

        var restore = CurrencySync.TryCreateLoginRestorePacket(character, persistence);
        Assert.IsNotNull(restore);
        Assert.AreEqual(command.Packet.Opcode, restore.Opcode);
        Assert.AreEqual(command.Packet.CharacterId, restore.CharacterId);
        Assert.AreEqual(command.Packet.Level, restore.Level);
        Assert.AreEqual(command.Packet.Currency, restore.Currency);
        Assert.AreEqual(command.Absolute, character.Credits);
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_WithPersistence_ReloadsAuthoritativeBalance()
    {
        var persistence = new RecordingInventoryPersistence { CreditsToLoad = 9_008_007_006L };
        var character = CreateCharacter(coid: 99, startingCredits: 0);

        var packet = CurrencySync.TryCreateLoginRestorePacket(character, persistence);

        Assert.IsNotNull(packet);
        Assert.AreEqual(9_008_007_006L, packet.Currency);
        Assert.AreEqual(9_008_007_006L, character.Credits);
    }

    [TestMethod]
    public void CreateAbsoluteCurrencyPacket_MatchesCurrencyCommandFields()
    {
        var character = CreateCharacter(coid: 7, startingCredits: 500);
        var packet = CurrencySync.CreateAbsoluteCurrencyPacket(character, 1_002_003_004L);

        Assert.AreEqual(GameOpcode.CharacterLevel, packet.Opcode);
        Assert.AreEqual(character.ObjectId, packet.CharacterId);
        Assert.AreEqual(character.Level, packet.Level);
        Assert.AreEqual(1_002_003_004L, packet.Currency);
    }

    [TestMethod]
    public void TryCreateLoginRestorePacket_Null_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CurrencySync.TryCreateLoginRestorePacket(null));
    }

    [TestMethod]
    public void TryApplyCurrencyCommand_TooFewArgs_Fails()
    {
        var result = CurrencySync.TryApplyCurrencyCommand(
            CreateCharacter(1, 0),
            new[] { "/currency", "1", "2" });

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("Usage", StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(result.Packet);
    }

    [TestMethod]
    public void TryApplyCurrencyCommand_NullParts_Fails()
    {
        var result = CurrencySync.TryApplyCurrencyCommand(CreateCharacter(1, 0), null);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Packet);
    }

    [TestMethod]
    public void TryApplyCurrencyCommand_NonNumeric_Fails()
    {
        var result = CurrencySync.TryApplyCurrencyCommand(
            CreateCharacter(1, 0),
            new[] { "/currency", "a", "b", "c", "d" });

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Message.Contains("numbers", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void TryApplyCurrencyCommand_NullCharacter_Fails()
    {
        var result = CurrencySync.TryApplyCurrencyCommand(
            null,
            new[] { "/currency", "1", "2", "3", "4" });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("No character.", result.Message);
    }

    [TestMethod]
    public void TryApplyCurrencyCommand_Valid_PersistsAndBuildsPacket()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(coid: 77, startingCredits: 0, inventory);

        var result = CurrencySync.TryApplyCurrencyCommand(
            character,
            new[] { "/currency", "1", "2", "3", "4" });

        var expected = CharacterLevelPacket.BuildCurrency(1, 2, 3, 4);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(expected, result.Absolute);
        Assert.AreEqual(expected, character.Credits);
        Assert.IsNotNull(result.Packet);
        Assert.AreEqual(expected, result.Packet.Currency);
        Assert.AreEqual(character.ObjectId, result.Packet.CharacterId);
        Assert.AreEqual((77L, expected), persistence.CreditsSaves[0]);
        Assert.IsTrue(result.Message.Contains("persisted=", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryApplyCurrencyCommand_PersistenceThrows_ReturnsFailure()
    {
        var inventory = new InventoryManager(new ThrowingCreditsPersistence());
        var character = CreateCharacter(coid: 88, startingCredits: 0, inventory);

        var result = CurrencySync.TryApplyCurrencyCommand(
            character,
            new[] { "/currency", "1", "0", "0", "0" });

        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Packet);
        Assert.IsTrue(result.Message.Contains("Failed to set currency", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AddCredits_Positive_PersistsAndBuildsDelta()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = CreateCharacter(10, 100);

        var result = CurrencySync.AddCredits(persistence, character, 50);

        Assert.AreEqual(100L, result.Previous);
        Assert.AreEqual(150L, result.NewBalance);
        Assert.AreEqual(50L, result.AppliedDelta);
        Assert.IsNotNull(result.DeltaPacket);
        Assert.AreEqual(50L, result.DeltaPacket.Amount);
        Assert.AreEqual((10L, 150L), persistence.CreditsSaves[0]);
    }

    [TestMethod]
    public void AddCredits_Negative_FloorsAtZero()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = CreateCharacter(11, 30);

        var result = CurrencySync.AddCredits(persistence, character, -100);

        Assert.AreEqual(0L, result.NewBalance);
        Assert.AreEqual(-30L, result.AppliedDelta);
        Assert.AreEqual(-30L, result.DeltaPacket.Amount);
    }

    [TestMethod]
    public void AddCredits_AllowDebt_CanGoNegative()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = CreateCharacter(12, 10);

        var result = CurrencySync.AddCredits(persistence, character, -40, allowDebt: true);

        Assert.AreEqual(-30L, result.NewBalance);
        Assert.AreEqual(-40L, result.AppliedDelta);
        Assert.AreEqual(-30L, character.Credits);
    }

    [TestMethod]
    public void AddCredits_ZeroDelta_NoPacket()
    {
        var result = CurrencySync.AddCredits(
            new RecordingInventoryPersistence(),
            CreateCharacter(13, 5),
            0);

        Assert.AreEqual(0L, result.AppliedDelta);
        Assert.IsNull(result.DeltaPacket);
    }

    [TestMethod]
    public void AddCredits_NullCharacter_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CurrencySync.AddCredits(new RecordingInventoryPersistence(), null, 1));
    }

    [TestMethod]
    public void SetCreditsAbsolute_Persists()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = CreateCharacter(14, 1);

        var balance = CurrencySync.SetCreditsAbsolute(persistence, character, 999_888_777_666L);

        Assert.AreEqual(999_888_777_666L, balance);
        Assert.AreEqual(balance, character.Credits);
        Assert.AreEqual((14L, balance), persistence.CreditsSaves[0]);
    }

    [TestMethod]
    public void SetCreditsAbsolute_Negative_FloorsWithoutDebt()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = CreateCharacter(15, 50);

        var balance = CurrencySync.SetCreditsAbsolute(persistence, character, -9);

        Assert.AreEqual(0L, balance);
        Assert.AreEqual(0L, character.Credits);
    }

    [TestMethod]
    public void SetCreditsAbsolute_AllowDebt_KeepsNegative()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = CreateCharacter(16, 0);

        var balance = CurrencySync.SetCreditsAbsolute(persistence, character, -123, allowDebt: true);

        Assert.AreEqual(-123L, balance);
        Assert.AreEqual(-123L, character.Credits);
    }

    [TestMethod]
    public void SetCreditsAbsolute_NullCharacter_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            CurrencySync.SetCreditsAbsolute(new RecordingInventoryPersistence(), null, 1));
    }

    [TestMethod]
    public void PersistCredits_NullPersistence_DoesNotThrow()
    {
        var character = CreateCharacter(17, 0);
        CurrencySync.PersistCredits(null, character, 42);
        // No exception; balance remains in-memory only.
        Assert.AreEqual(0L, character.Credits);
    }

    [TestMethod]
    public void PersistCredits_InvalidCoid_DoesNotSave()
    {
        var persistence = new RecordingInventoryPersistence();
        var character = new Character();
        character.AttachTestDataForTests("NoCoid");
        // Fresh TFID defaults Coid to -1 (invalid for character rows).
        Assert.IsTrue(character.ObjectId.Coid <= 0);
        CurrencySync.PersistCredits(persistence, character, 99);
        Assert.AreEqual(0, persistence.CreditsSaves.Count);
    }

    [TestMethod]
    public void PersistCredits_NullCharacter_DoesNotSave()
    {
        var persistence = new RecordingInventoryPersistence();
        CurrencySync.PersistCredits(persistence, null, 99);
        Assert.AreEqual(0, persistence.CreditsSaves.Count);
    }

    private static Character CreateCharacter(long coid, long startingCredits, InventoryManager inventory = null)
    {
        var character = new Character();
        character.SetCoid(coid, global: true);
        character.AttachTestDataForTests("CurrencyTester");
        character.SetCredits(startingCredits);
        if (inventory != null)
            character.AttachInventoryForTests(inventory);
        return character;
    }

    /// <summary>Minimal persistence that fails only on credit saves.</summary>
    private sealed class ThrowingCreditsPersistence : IInventoryPersistence
    {
        public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) =>
            Array.Empty<CharacterInventoryItem>();

        public void UpsertCargo(long characterCoid, CharacterInventoryItem item) { }
        public void MoveCargo(long characterCoid, CharacterInventoryItem item) { }
        public void DeleteCargo(long characterCoid, long itemCoid) { }
        public void ClearCargo(long characterCoid) { }
        public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0) { }
        public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot) { }
        public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount) { }
        public long LoadCredits(long characterCoid) => 0;
        public void SaveCredits(long characterCoid, long credits) =>
            throw new InvalidOperationException("db down");
    }
}

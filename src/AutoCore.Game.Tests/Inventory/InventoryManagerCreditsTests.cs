using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerCreditsTests
{
    [TestMethod]
    public void AddCredits_Positive_PersistsAndBuildsDeltaPacket()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(inventory, coid: 42, startingCredits: 100);

        var result = inventory.AddCredits(character, 50);

        Assert.AreEqual(100L, result.Previous);
        Assert.AreEqual(150L, result.NewBalance);
        Assert.AreEqual(50L, result.AppliedDelta);
        Assert.IsNotNull(result.DeltaPacket);
        Assert.AreEqual(50L, result.DeltaPacket.Amount);
        Assert.AreEqual(150L, character.Credits);
        Assert.AreEqual(1, persistence.CreditsSaves.Count);
        Assert.AreEqual((42L, 150L), persistence.CreditsSaves[0]);
    }

    [TestMethod]
    public void AddCredits_Negative_FloorsAtZero()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(inventory, coid: 7, startingCredits: 30);

        var result = inventory.AddCredits(character, -100);

        Assert.AreEqual(30L, result.Previous);
        Assert.AreEqual(0L, result.NewBalance);
        Assert.AreEqual(-30L, result.AppliedDelta);
        Assert.AreEqual(-30L, result.DeltaPacket.Amount);
        Assert.AreEqual(0L, character.Credits);
    }

    [TestMethod]
    public void AddCredits_ZeroDelta_NoPacket()
    {
        var inventory = new InventoryManager(new RecordingInventoryPersistence());
        var character = CreateCharacter(inventory, coid: 1, startingCredits: 10);

        var result = inventory.AddCredits(character, 0);

        Assert.AreEqual(0L, result.AppliedDelta);
        Assert.IsNull(result.DeltaPacket);
    }

    [TestMethod]
    public void SetCreditsAbsolute_Persists()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(inventory, coid: 9, startingCredits: 1);

        var balance = inventory.SetCreditsAbsolute(character, 999_888_777_666L);

        Assert.AreEqual(999_888_777_666L, balance);
        Assert.AreEqual(balance, character.Credits);
        Assert.AreEqual((9L, balance), persistence.CreditsSaves[0]);
    }

    [TestMethod]
    public void AddCredits_NullCharacter_Throws()
    {
        var inventory = new InventoryManager(new RecordingInventoryPersistence());
        Assert.ThrowsException<ArgumentNullException>(() => inventory.AddCredits(null, 1));
    }

    [TestMethod]
    public void SetCreditsAbsolute_NullCharacter_Throws()
    {
        var inventory = new InventoryManager(new RecordingInventoryPersistence());
        Assert.ThrowsException<ArgumentNullException>(() => inventory.SetCreditsAbsolute(null, 1));
    }

    [TestMethod]
    public void AddCredits_AllowDebt_CanGoNegative()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(inventory, coid: 8, startingCredits: 5);

        var result = inventory.AddCredits(character, -20, allowDebt: true);

        Assert.AreEqual(-15L, result.NewBalance);
        Assert.AreEqual(-15L, character.Credits);
    }

    [TestMethod]
    public void SetCreditsAbsolute_Negative_FloorsAtZero()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = CreateCharacter(inventory, coid: 3, startingCredits: 100);

        var balance = inventory.SetCreditsAbsolute(character, -50);

        Assert.AreEqual(0L, balance);
        Assert.AreEqual(0L, character.Credits);
    }

    [TestMethod]
    public void AddCreditsResult_ExposesFields()
    {
        var delta = new AutoCore.Game.Packets.Sector.GiveCreditsPacket { Amount = 9 };
        var result = new AddCreditsResult(1, 10, 9, delta);
        Assert.AreEqual(1L, result.Previous);
        Assert.AreEqual(10L, result.NewBalance);
        Assert.AreEqual(9L, result.AppliedDelta);
        Assert.AreSame(delta, result.DeltaPacket);
    }

    private static Character CreateCharacter(InventoryManager inventory, long coid, long startingCredits)
    {
        var character = new Character();
        character.SetCoid(coid, false);
        character.AttachTestDataForTests("CreditTester");
        character.SetCredits(startingCredits);
        character.AttachInventoryForTests(inventory);
        return character;
    }
}

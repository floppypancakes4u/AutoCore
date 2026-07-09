using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

[TestClass]
public class CharacterCreditsTests
{
    [TestMethod]
    public void Credits_DefaultZero_WithoutDbData()
    {
        var character = new Character();
        Assert.AreEqual(0L, character.Credits);
        Assert.AreEqual(0L, character.CreditDebt);
    }

    [TestMethod]
    public void SetCredits_WithoutDbData_IsNoOp()
    {
        var character = new Character();
        character.SetCredits(500);
        character.SetCreditDebt(10);
        Assert.AreEqual(0L, character.Credits);
        Assert.AreEqual(0L, character.CreditDebt);
    }

    [TestMethod]
    public void SetCredits_AndDebt_UpdateDbData()
    {
        var character = new Character();
        character.SetCoid(9, true);
        character.AttachTestDataForTests("Cash");

        character.SetCredits(42_000);
        character.SetCreditDebt(7);

        Assert.AreEqual(42_000L, character.Credits);
        Assert.AreEqual(7L, character.CreditDebt);
    }

    [TestMethod]
    public void LoginPath_ClearCreditsOnCreateCharacter_IndependentOfLiveBalance()
    {
        var character = new Character();
        character.SetCoid(3, true);
        character.AttachTestDataForTests("Rich");
        character.SetCredits(9_999_999_999L);

        var packet = new CreateCharacterExtendedPacket
        {
            Credits = character.Credits,
            CreditDebt = 1
        };
        CurrencySync.ClearCreateCharacterCredits(packet);

        Assert.AreEqual(0L, packet.Credits);
        Assert.AreEqual(0L, packet.CreditDebt);
        // Server still keeps live balance for CharacterLevel restore.
        Assert.AreEqual(9_999_999_999L, character.Credits);
    }
}

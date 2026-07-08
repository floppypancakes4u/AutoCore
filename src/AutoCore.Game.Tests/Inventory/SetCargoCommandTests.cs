using AutoCore.Game.Chat;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class SetCargoCommandTests
{
    [TestMethod]
    public void SetCargo_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/setcargo 5");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void SetCargo_InvalidArgs_ReturnsUsage()
    {
        var result = ChatCommandService.Instance.Execute(null, "/setcargo");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Usage:");
    }

    [TestMethod]
    public void SetCargo_WithCharacter_UpdatesCapacityPersistsAndSendsCargoSendAll()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var character = new Character();
        character.SetCoid(5001, true);
        character.AttachTestDataForTests();
        character.AttachInventoryForTests(inventory);

        var result = ChatCommandService.Instance.Execute(character, "/setcargo 5 8");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "8x5");
        Assert.AreEqual(8, inventory.Width);
        Assert.AreEqual(5, inventory.PageCount);
        Assert.AreEqual(1, persistence.CapacitySaves.Count);
        Assert.AreEqual((5001L, 8, 5), persistence.CapacitySaves[0]);
        Assert.IsTrue(result.Packets.Any(p => p is InventoryCargoSendAllPacket));
    }
}

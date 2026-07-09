using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class ChatCommandServiceTests
{
    [TestCleanup]
    public void Cleanup() => AssetManagerTestHelper.ClearRegisteredCloneBases();

    [TestMethod]
    public void Execute_UnknownCommand_IsNotHandled()
    {
        var result = ChatCommandService.Instance.Execute(null, "/unknown");

        Assert.IsFalse(result.Handled);
    }

    [TestMethod]
    public void ClearCargo_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/clearcargo");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void CargoInfo_WithoutCharacter_ReturnsError()
    {
        var result = ChatCommandService.Instance.Execute(null, "/cargoinfo");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual("No character loaded.", result.Message);
    }

    [TestMethod]
    public void ClearCargo_WithCharacter_ClearsCargoAndReturnsSnapshot()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 2));

        var result = ChatCommandService.Instance.Execute(harness.Character, "/clearcargo");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Cleared 1 cargo item");
        Assert.AreEqual(0, harness.Inventory.Items.Count);
        Assert.AreEqual(1, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(InventoryCargoSendAllPacket));
    }

    [TestMethod]
    public void CargoInfo_WithCharacter_ReturnsDescribeCargoStatus()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 100, 0, 0, 1));
        harness.Inventory.TryAdd(new CharacterInventoryItem(11, CloneBaseObjectType.Item, "B", 101, 1, 0, 3));

        var result = ChatCommandService.Instance.Execute(harness.Character, "/cargoinfo");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "2 item(s) loaded");
        StringAssert.Contains(result.Message, "slots occupied");
    }

    [TestMethod]
    public void AddItem_InvalidUsage_IsHandledWithMessage()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);

        var result = ChatCommandService.Instance.Execute(harness.Character, "/addItem");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "Invalid addItem command");
    }

    [TestMethod]
    public void AddItem_WithQuantity_RoutesThroughInventoryCommandService()
    {
        const int cbid = 88001;
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Item);

        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: 2000);

        var result = ChatCommandService.Instance.Execute(harness.Character, $"/addItem {cbid} 4");

        Assert.IsTrue(result.Handled);
        StringAssert.Contains(result.Message, "x4");
        Assert.AreEqual(4, harness.Inventory.Items.Single().Quantity);
        Assert.AreEqual(3, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(CreateSimpleObjectPacket));
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryAddItemResponsePacket));
        Assert.IsInstanceOfType(result.Packets[2], typeof(InventoryCargoSendAllPacket));
    }
}

using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryOperationResultTests
{
    [TestMethod]
    public void SinglePacket_WithNullPacket_ReturnsEmptyPacketList()
    {
        var result = InventoryOperationResult.SinglePacket(null, "noop");
        Assert.AreEqual(0, result.Packets.Count);
        Assert.AreEqual("noop", result.LogMessage);
    }

    [TestMethod]
    public void Constructor_PreservesWorldObjectToDestroy()
    {
        var packet = new InventoryGrabResponsePacket();
        var result = new InventoryOperationResult(new[] { packet }, "grab", worldObjectToDestroy: null);
        Assert.IsNull(result.WorldObjectToDestroy);
        Assert.AreSame(packet, result.Packets[0]);
    }
}

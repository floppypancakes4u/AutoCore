using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryTossDebugLogTests
{
    [TestCleanup]
    public void Cleanup()
    {
        InventoryDropDebugLog.Clear();
    }

    [TestMethod]
    public void ShouldRecordIncoming_IncludesDropMmAndDestroyItem()
    {
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordIncoming(GameOpcode.InventoryDrop));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordIncoming(GameOpcode.InventoryDropMM));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordIncoming(GameOpcode.InventoryDestroyItem));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordIncoming(GameOpcode.ItemDrop));
        Assert.IsFalse(InventoryDropDebugLog.ShouldRecordIncoming(GameOpcode.InventoryGrab));
    }

    [TestMethod]
    public void ShouldRecordOutgoing_IncludesDropResponsesAndWorldObjects()
    {
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordOutgoing(GameOpcode.InventoryDropResponse));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordOutgoing(GameOpcode.InventoryDropMMResponse));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordOutgoing(GameOpcode.CreateSimpleObject));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordOutgoing(GameOpcode.DestroyObject));
        Assert.IsTrue(InventoryDropDebugLog.ShouldRecordOutgoing(GameOpcode.ItemDropResponse));
        Assert.IsFalse(InventoryDropDebugLog.ShouldRecordOutgoing(GameOpcode.InventoryGrabResponse));
    }

    [TestMethod]
    public void RecordIncomingIfTossRelated_StoresOnlyMatchingOpcodes()
    {
        var dropBytes = new byte[] { 0x36, 0x20, 0x00, 0x00, 0x01 };
        var grabBytes = new byte[] { 0x34, 0x20, 0x00, 0x00, 0x02 };

        InventoryDropDebugLog.RecordIncomingIfTossRelated(GameOpcode.InventoryDrop, dropBytes);
        InventoryDropDebugLog.RecordIncomingIfTossRelated(GameOpcode.InventoryGrab, grabBytes);

        var snapshot = InventoryDropDebugLog.Snapshot();
        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual("incoming", snapshot[0].Direction);
        Assert.AreEqual(dropBytes.Length, snapshot[0].Length);
    }

    [TestMethod]
    public void RecordOutgoingIfTossRelated_StoresCreateSimpleObject()
    {
        var bytes = new byte[] { 0x21, 0x20, 0x00, 0x00, 0x03 };

        InventoryDropDebugLog.RecordOutgoingIfTossRelated(GameOpcode.CreateSimpleObject, bytes);

        var snapshot = InventoryDropDebugLog.Snapshot();
        Assert.AreEqual(1, snapshot.Count);
        Assert.AreEqual("outgoing", snapshot[0].Direction);
    }
}

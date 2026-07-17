using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// Mass Move (client "move like items") sends InventoryGrabMM + InventoryDropMM per item.
/// Server reuses Grab/Drop logic and normal response opcodes (client early-outs on 0x2039/0x203B).
/// </summary>
[TestClass]
public class InventoryMassMoveTests
{
    [TestMethod]
    public void GrabMM_ToGrabPacket_PreservesGridFields()
    {
        var bytes = new byte[0x20];
        BitConverter.GetBytes((uint)GameOpcode.InventoryGrabMM).CopyTo(bytes, 0);
        BitConverter.GetBytes(11673L).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x18] = InventoryTypes.Cargo;
        BitConverter.GetBytes(1).CopyTo(bytes, 0x1c);

        var mm = ReadPacket<InventoryGrabMMPacket>(bytes);
        Assert.AreEqual(11673L, mm.ItemCoid);
        Assert.AreEqual(InventoryTypes.Cargo, mm.InventoryType);

        var grab = mm.ToGrabPacket();
        Assert.AreEqual(11673L, grab.ItemCoid);
        Assert.IsTrue(grab.ItemGlobal);
        Assert.AreEqual(InventoryTypes.Cargo, grab.InventoryType);
        Assert.AreEqual(1, grab.Quantity);
    }

    [TestMethod]
    public void DropMM_ToDropPacket_PreservesLiveCaptureLayout()
    {
        // Live: opcode 0x203A, coid=11673, global=1, slot=2,1, invType=3
        var hex = "3A20000060000000992D00000000000001F51901E665466F0201030101000000";
        var bytes = Convert.FromHexString(hex);

        var mm = ReadPacket<InventoryDropMMPacket>(bytes);
        Assert.AreEqual(11673L, mm.ItemCoid);
        Assert.IsTrue(mm.ItemGlobal);
        Assert.AreEqual((byte)2, mm.InventoryPositionX);
        Assert.AreEqual((byte)1, mm.InventoryPositionY);
        Assert.AreEqual(InventoryTypes.Locker, mm.InventoryType);

        var drop = mm.ToDropPacket();
        Assert.AreEqual(11673L, drop.ItemCoid);
        Assert.AreEqual((byte)2, drop.InventoryPositionX);
        Assert.AreEqual((byte)1, drop.InventoryPositionY);
        Assert.AreEqual(InventoryTypes.Locker, drop.InventoryType);
    }

    [TestMethod]
    public void MassMove_CargoToLocker_GrabThenDrop_TransfersAndPersists()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 11673, 0, 0, 1));

        var grabMm = BuildGrabMM(11673, InventoryTypes.Cargo);
        var grabResult = harness.Inventory.Grab(grabMm.ToGrabPacket(), harness.Character);
        Assert.IsTrue(((InventoryGrabResponsePacket)grabResult.Packets[0]).WasSuccessful);
        Assert.IsNotNull(harness.Inventory.FindByCoid(11673), "Grab does not remove from cargo");

        var dropMm = BuildDropMM(11673, x: 2, y: 1, InventoryTypes.Locker);
        var dropResult = harness.Inventory.Drop(dropMm.ToDropPacket(), harness.Character);

        Assert.IsNull(harness.Inventory.FindByCoid(11673));
        var locker = harness.Inventory.FindLockerByCoid(11673);
        Assert.IsNotNull(locker);
        Assert.AreEqual((byte)2, locker.InventoryPositionX);
        Assert.AreEqual((byte)1, locker.InventoryPositionY);

        var response = (InventoryDropResponsePacket)dropResult.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(InventoryTypes.Locker, response.InventoryType);
        Assert.AreEqual(GameOpcode.InventoryDropResponse, response.Opcode);
        Assert.AreEqual(1, harness.Persistence.LockerUpserted.Count);
    }

    [TestMethod]
    public void MassMove_MultipleLikeItems_EachPairTransfers()
    {
        var harness = new InventoryTestHarness();
        long[] coids = [11673, 11838, 11842, 11865];
        for (var i = 0; i < coids.Length; i++)
        {
            harness.Inventory.TryAdd(
                new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", coids[i], (byte)i, 0, 1));
        }

        for (var i = 0; i < coids.Length; i++)
        {
            var coid = coids[i];
            harness.Inventory.Grab(BuildGrabMM(coid, InventoryTypes.Cargo).ToGrabPacket(), harness.Character);
            var result = harness.Inventory.Drop(
                BuildDropMM(coid, x: (byte)(i + 2), y: 1, InventoryTypes.Locker).ToDropPacket(),
                harness.Character);
            Assert.IsTrue(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful, $"coid={coid}");
            Assert.IsNull(harness.Inventory.FindByCoid(coid));
            Assert.IsNotNull(harness.Inventory.FindLockerByCoid(coid));
        }

        Assert.AreEqual(0, harness.Inventory.Items.Count);
        Assert.AreEqual(4, harness.Inventory.LockerItems.Count);
        Assert.AreEqual(4, harness.Persistence.LockerUpserted.Count);
    }

    [TestMethod]
    public void MassMove_LockerToCargo_Works()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 500, 0, 0, 1));

        harness.Inventory.Grab(BuildGrabMM(500, InventoryTypes.Locker).ToGrabPacket(), harness.Character);
        var result = harness.Inventory.Drop(
            BuildDropMM(500, x: 1, y: 2, InventoryTypes.Cargo).ToDropPacket(),
            harness.Character);

        Assert.IsTrue(((InventoryDropResponsePacket)result.Packets[0]).WasSuccessful);
        Assert.IsNull(harness.Inventory.FindLockerByCoid(500));
        Assert.IsNotNull(harness.Inventory.FindByCoid(500));
    }

    private static InventoryGrabMMPacket BuildGrabMM(long itemCoid, byte inventoryType)
    {
        var bytes = new byte[0x20];
        BitConverter.GetBytes((uint)GameOpcode.InventoryGrabMM).CopyTo(bytes, 0);
        BitConverter.GetBytes(itemCoid).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x18] = inventoryType;
        BitConverter.GetBytes(1).CopyTo(bytes, 0x1c);
        return ReadPacket<InventoryGrabMMPacket>(bytes);
    }

    private static InventoryDropMMPacket BuildDropMM(long itemCoid, byte x, byte y, byte inventoryType)
    {
        var bytes = new byte[0x20];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDropMM).CopyTo(bytes, 0);
        BitConverter.GetBytes(itemCoid).CopyTo(bytes, 8);
        bytes[0x10] = 1;
        bytes[0x18] = x;
        bytes[0x19] = y;
        bytes[0x1a] = inventoryType;
        return ReadPacket<InventoryDropMMPacket>(bytes);
    }

    private static T ReadPacket<T>(byte[] bytes) where T : BasePacket, new()
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();
        var packet = new T();
        packet.Read(reader);
        return packet;
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// Locks CreateVehicle fixed layout against retail client offsets used by EquipFromCreate
/// (FUN_00504480) and ghost buffer init (FUN_005F5AD0):
/// wheel opcode @ 0x458, wheel CBID @ 0x45C, nested TFID COID @ 0x4E8.
/// </summary>
[TestClass]
public class CreateVehicleLayoutTests
{
    public const int ClientWheelOpcodeOffset = 0x458;
    public const int ClientWheelCbidOffset = 0x45C;
    public const int ClientWheelTfidCoidOffset = 0x4E8;
    public const int ClientCreateVehicleSize = 0xD78; // 3448

    [TestMethod]
    public void Write_EmptyNestedWheelset_PlacesMinusOneAtClientWheelCbidOffset()
    {
        var bytes = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 12425,
            ObjectId = new TFID(1, true),
        });

        Assert.AreEqual(ClientCreateVehicleSize, bytes.Length,
            "CreateVehicle including root opcode must match client FUN_005F5AD0 size 0xD78.");
        Assert.AreEqual((uint)GameOpcode.CreateWheelSet, BitConverter.ToUInt32(bytes, ClientWheelOpcodeOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientWheelCbidOffset),
            "Empty nested path must wire CBID -1; client ghost zero-fill leaves 0 and Path A treats that as equip failure.");
    }

    [TestMethod]
    public void Write_FullNestedWheelset_PlacesPositiveCbidAtClientOffset()
    {
        const int wheelCbid = 52;
        var bytes = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 15478,
            ObjectId = new TFID(2, true),
            CreateWheelSet = new CreateWheelSetPacket
            {
                CBID = wheelCbid,
                ObjectId = new TFID(0x50000009L, true),
                Name = "w",
            },
        });

        Assert.AreEqual(ClientCreateVehicleSize, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.CreateWheelSet, BitConverter.ToUInt32(bytes, ClientWheelOpcodeOffset));
        Assert.AreEqual(wheelCbid, BitConverter.ToInt32(bytes, ClientWheelCbidOffset));
        Assert.AreEqual(0x50000009L, BitConverter.ToInt64(bytes, ClientWheelTfidCoidOffset));
        Assert.AreEqual(1, bytes[0x4F0]);
    }

    [TestMethod]
    public void Write_MustNeverEmitNestedWheelCbidZero()
    {
        // Server contract: empty uses -1; full uses clonebase DefaultWheelset (>0).
        // Client GiveItemByCbid(0) fails → +0x258 stays null → owner-on AV at 0x004F5566.
        var empty = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 1,
            ObjectId = new TFID(3, true),
        });
        Assert.AreNotEqual(0, BitConverter.ToInt32(empty, ClientWheelCbidOffset));

        var full = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 2,
            ObjectId = new TFID(4, true),
            CreateWheelSet = new CreateWheelSetPacket
            {
                CBID = 40,
                ObjectId = new TFID(0x5000000AL, true),
            },
        });
        Assert.AreNotEqual(0, BitConverter.ToInt32(full, ClientWheelCbidOffset));
        Assert.IsTrue(BitConverter.ToInt32(full, ClientWheelCbidOffset) > 0);
    }

    private static byte[] SerializeWithRootOpcode(CreateVehiclePacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)GameOpcode.CreateVehicle);
        packet.Write(writer);
        ms.SetLength(ms.Position);
        return ms.ToArray();
    }
}

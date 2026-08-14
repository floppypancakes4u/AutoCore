using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// PDB Pass 5 crash-precondition locks.
/// Client: Vehicle_EquipFromCreate 0x00504480 GiveItemByCbid(packet+0x45C);
/// FUN_004F5560 reads *(vehicle+0x258)+0xB0 (wheel count) with no null check.
/// </summary>
[TestClass]
public class VehicleCrashRegressionTests
{
    [TestMethod]
    public void EmptyWheelNest_NeverWiresCbidZero()
    {
        var bytes = Serialize(new CreateVehiclePacket
        {
            CBID = 12425,
            ObjectId = new TFID(1, true),
        });

        var wheelCbid = BitConverter.ToInt32(bytes, CreateVehicleClientOffsetTests.ClientWheelCbidOffset);
        Assert.AreNotEqual(0, wheelCbid,
            "CBID 0 is the FUN_005F5AD0 zero-fill value. EquipFromCreate then GiveItemByCbid(0) and +0x258 stays null.");
        Assert.AreEqual(-1, wheelCbid,
            "Empty nest must be CBID -1 so it matches armor/PP/weapon skip sentinels.");
    }

    [TestMethod]
    public void ValidWheelNest_NeverWiresCbidZeroOrMinusOne()
    {
        var bytes = Serialize(new CreateVehiclePacket
        {
            CBID = 15478,
            ObjectId = new TFID(2, true),
            CreateWheelSet = new CreateWheelSetPacket
            {
                CBID = 40,
                ObjectId = new TFID(0x5000000AL, true),
            },
        });

        var wheelCbid = BitConverter.ToInt32(bytes, CreateVehicleClientOffsetTests.ClientWheelCbidOffset);
        Assert.IsTrue(wheelCbid > 0, "A present wheel nest must be a cloneable CBID.");
    }

    [TestMethod]
    public void OwnerField_IsCharacterOrDriverCoidAtPlusD8()
    {
        const long owner = 0x1000_0000_0000_0042L;
        var bytes = Serialize(new CreateVehiclePacket
        {
            CBID = 1,
            ObjectId = new TFID(3, true),
            CoidCurrentOwner = owner,
        });

        Assert.AreEqual(owner, BitConverter.ToInt64(bytes, CreateVehicleClientOffsetTests.ClientOwnerCoidOffset));
    }

    [TestMethod]
    public void ObjectTfid_IsWrittenAtPlus90()
    {
        var bytes = Serialize(new CreateVehiclePacket
        {
            CBID = 9,
            ObjectId = new TFID(0, true),
        });

        Assert.AreEqual(0L, BitConverter.ToInt64(bytes, CreateVehicleClientOffsetTests.ClientTfidCoidOffset),
            "Writer must place TFID at +0x90 even when Coid is 0 so the test documents the slot.");
        Assert.AreEqual(1, bytes[CreateVehicleClientOffsetTests.ClientTfidGlobalOffset]);
    }

    private static byte[] Serialize(CreateVehiclePacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(ms.Position);
        return ms.ToArray();
    }
}

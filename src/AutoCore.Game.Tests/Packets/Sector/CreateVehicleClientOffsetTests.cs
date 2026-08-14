using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// PDB Pass 5 goldens. Offsets come from client FUN_005F5AD0 (ghost CreateVehicle
/// buffer init, RVA 0x005F5AD0) and Vehicle_EquipFromCreate (RVA 0x00504480).
/// Opcode is at absolute 0. Do not treat AutoCore field names as proof.
/// </summary>
[TestClass]
public class CreateVehicleClientOffsetTests
{
    // FUN_005F5AD0: operator_new(0xD78); zero-fill 0x35E dwords; **buf = 0x201D.
    public const int ClientCreateVehicleSize = 0xD78;

    public const int ClientCbidOffset = 0x04;
    public const int ClientTfidCoidOffset = 0x90;
    public const int ClientTfidGlobalOffset = 0x98;
    public const int ClientIsItemLinkOffset = 0xA1;
    public const int ClientOwnerCoidOffset = 0xD8;
    public const int ClientSpawnOwnerOffset = 0xE0;

    // FUN_005F5AD0 nest opcodes / empty CBIDs.
    public const int ClientOrnamentOpcodeOffset = 0x158;
    public const int ClientOrnamentCbidOffset = 0x15C;
    public const int ClientRaceItemCbidOffset = 0x234;
    public const int ClientPowerPlantCbidOffset = 0x30C;
    public const int ClientWheelOpcodeOffset = 0x458;
    public const int ClientWheelCbidOffset = 0x45C;
    public const int ClientWheelTfidCoidOffset = 0x4E8;
    public const int ClientArmorOpcodeOffset = 0x5B0;
    public const int ClientArmorCbidOffset = 0x5B4;
    public const int ClientMeleeOpcodeOffset = 0x708;
    public const int ClientMeleeCbidOffset = 0x70C;
    public const int ClientFrontWeaponOpcodeOffset = 0x890;
    public const int ClientFrontWeaponCbidOffset = 0x894;
    public const int ClientWeaponNestStride = 0x188;

    public const int ClientNameOffset = 0xD78 - 36; // 33-byte name + 3 pad at end of 80-byte tail
    public const int ClientWeaponCbidArrayOffset = 0xD78 - 12 - 36;

    [TestMethod]
    public void Write_EmptyVehicle_MatchesClientGhostInitSizeAndOpcodes()
    {
        var bytes = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 12425,
            ObjectId = new TFID(0x11, true),
            CoidCurrentOwner = 0x22,
            CoidSpawnOwner = -1,
        });

        Assert.AreEqual(ClientCreateVehicleSize, bytes.Length,
            "FUN_005F5AD0 allocates 0xD78 including opcode 0x201D.");
        Assert.AreEqual((uint)GameOpcode.CreateVehicle, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(12425, BitConverter.ToInt32(bytes, ClientCbidOffset));
        Assert.AreEqual(0x11L, BitConverter.ToInt64(bytes, ClientTfidCoidOffset));
        Assert.AreEqual(1, bytes[ClientTfidGlobalOffset]);
        Assert.AreEqual(0x22L, BitConverter.ToInt64(bytes, ClientOwnerCoidOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientSpawnOwnerOffset));

        Assert.AreEqual((uint)GameOpcode.CreateSimpleObject, BitConverter.ToUInt32(bytes, ClientOrnamentOpcodeOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientOrnamentCbidOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientRaceItemCbidOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientPowerPlantCbidOffset));

        Assert.AreEqual((uint)GameOpcode.CreateWheelSet, BitConverter.ToUInt32(bytes, ClientWheelOpcodeOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientWheelCbidOffset),
            "Empty wheel must be CBID -1. Client ghost init zero-fills +0x45C to 0; EquipFromCreate then GiveItemByCbid(0).");

        Assert.AreEqual((uint)GameOpcode.CreateArmor, BitConverter.ToUInt32(bytes, ClientArmorOpcodeOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientArmorCbidOffset));
        Assert.AreEqual((uint)GameOpcode.CreateWeapon, BitConverter.ToUInt32(bytes, ClientMeleeOpcodeOffset));
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientMeleeCbidOffset));

        for (var i = 0; i < 3; ++i)
        {
            var opcodeAt = ClientFrontWeaponOpcodeOffset + i * ClientWeaponNestStride;
            var cbidAt = ClientFrontWeaponCbidOffset + i * ClientWeaponNestStride;
            Assert.AreEqual((uint)GameOpcode.CreateWeapon, BitConverter.ToUInt32(bytes, opcodeAt),
                $"weapon nest {i} opcode");
            Assert.AreEqual(-1, BitConverter.ToInt32(bytes, cbidAt), $"weapon nest {i} empty CBID");
        }
    }

    [TestMethod]
    public void Write_FullWheelNest_PlacesCbidAndTfidAtEquipFromCreateOffsets()
    {
        const int wheelCbid = 52;
        const long wheelCoid = 0x50000009L;
        var bytes = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 15478,
            ObjectId = new TFID(2, true),
            CreateWheelSet = new CreateWheelSetPacket
            {
                CBID = wheelCbid,
                ObjectId = new TFID(wheelCoid, true),
                Name = "w",
            },
        });

        Assert.AreEqual(ClientCreateVehicleSize, bytes.Length);
        Assert.AreEqual(wheelCbid, BitConverter.ToInt32(bytes, ClientWheelCbidOffset));
        Assert.AreEqual(wheelCoid, BitConverter.ToInt64(bytes, ClientWheelTfidCoidOffset));
        Assert.AreEqual(1, bytes[ClientWheelTfidCoidOffset + 8]);
    }

    [TestMethod]
    public void Write_OwnerBlock_IsInt64AtPacketPlusD8()
    {
        var bytes = SerializeWithRootOpcode(new CreateVehiclePacket
        {
            CBID = 1,
            ObjectId = new TFID(3, true),
            CoidCurrentOwner = unchecked((long)0x0000_000A_0000_000BL),
        });

        Assert.AreEqual(0x0000_000A_0000_000BL, BitConverter.ToInt64(bytes, ClientOwnerCoidOffset));
        Assert.AreEqual(0x0000000B, BitConverter.ToInt32(bytes, ClientOwnerCoidOffset));
        Assert.AreEqual(0x0000000A, BitConverter.ToInt32(bytes, ClientOwnerCoidOffset + 4));
    }

    private static byte[] SerializeWithRootOpcode(CreateVehiclePacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        ms.SetLength(ms.Position);
        return ms.ToArray();
    }
}

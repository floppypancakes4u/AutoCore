using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// PDB Pass 7. Offsets from client <c>CVOGCharacter_SerializeCreatePacket</c> <c>0x0052F650</c>
/// (returns <c>0x1A8</c> when not extended) and
/// <c>CVOGCharacter_ApplyCreateFromPacket</c> <c>0x00534BD0</c>.
/// Opcode at 0. Character-specific tail starts at <c>0xD8</c> after the SimpleObject prefix.
/// </summary>
[TestClass]
public class CreateCharacterClientOffsetTests
{
    public const int ClientVehicleCoidOffset = 0xD8;
    public const int ClientTrailerCoidOffset = 0xE0;
    public const int ClientHeadIdOffset = 0xE8;
    public const int ClientBodyIdOffset = 0xEC;
    public const int ClientAccessory1Offset = 0xF0;
    public const int ClientAccessory2Offset = 0xF4;
    public const int ClientHairIdOffset = 0xF8;
    public const int ClientMouthIdOffset = 0xFC;
    public const int ClientEyesIdOffset = 0x100;
    public const int ClientHelmetIdOffset = 0x104;
    public const int ClientPrimaryColorOffset = 0x108;
    public const int ClientLastTownOffset = 0x120;
    public const int ClientLastStationOffset = 0x124;
    public const int ClientLevelOffset = 0x128;
    public const int ClientFlagsOffset = 0x129;
    public const int ClientGmLevelOffset = 0x12A;
    public const int ClientServerTimeOffset = 0x130;
    public const int ClientNameOffset = 0x138;
    public const int ClientClanNameOffset = 0x16B;
    public const int ClientScaleOffset = 0x1A0;
    public const int ClientCreateCharacterSize = 0x1A8;

    [TestMethod]
    public void Write_CoversClientApplyTail_SizeIs0x1A8()
    {
        var bytes = SerializeWithRootOpcode(new CreateCharacterPacket
        {
            CBID = 21001,
            ObjectId = new TFID(0x6000_1234L, true),
            CurrentVehicleCoid = 0x6000_9999L,
            CurrentTrailerCoid = -1L,
            HeadId = 11,
            BodyId = 22,
            AccessoryId1 = 33,
            AccessoryId2 = 44,
            HairId = 55,
            MouthId = 66,
            EyesId = 77,
            HelmetId = 88,
            PrimaryColor = 0x112233,
            LastTownId = 698,
            LastStationMapId = 707,
            Level = 9,
            UsingVehicle = true,
            UsingTrailer = false,
            IsPosessingCreature = true,
            GMLevel = 3,
            Name = "PilotName",
            ClanName = "ClanName",
            CharacterScaleOffset = 1.25f,
            CustomizedName = "",
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Default,
        });

        Assert.AreEqual(ClientCreateCharacterSize, bytes.Length,
            "CVOGCharacter_SerializeCreatePacket returns 0x1A8 for the non-extended packet.");
        Assert.AreEqual((uint)GameOpcode.CreateCharacter, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(21001, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(0x6000_1234L, BitConverter.ToInt64(bytes, 0x90));
        Assert.AreEqual(0x6000_9999L, BitConverter.ToInt64(bytes, ClientVehicleCoidOffset));
        Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, ClientTrailerCoidOffset));
        Assert.AreEqual(11, BitConverter.ToInt32(bytes, ClientHeadIdOffset));
        Assert.AreEqual(22, BitConverter.ToInt32(bytes, ClientBodyIdOffset));
        Assert.AreEqual(33, BitConverter.ToInt32(bytes, ClientAccessory1Offset));
        Assert.AreEqual(44, BitConverter.ToInt32(bytes, ClientAccessory2Offset));
        Assert.AreEqual(55, BitConverter.ToInt32(bytes, ClientHairIdOffset));
        Assert.AreEqual(66, BitConverter.ToInt32(bytes, ClientMouthIdOffset));
        Assert.AreEqual(77, BitConverter.ToInt32(bytes, ClientEyesIdOffset));
        Assert.AreEqual(88, BitConverter.ToInt32(bytes, ClientHelmetIdOffset));
        Assert.AreEqual(0x112233u, BitConverter.ToUInt32(bytes, ClientPrimaryColorOffset));
        Assert.AreEqual(698, BitConverter.ToInt32(bytes, ClientLastTownOffset));
        Assert.AreEqual(707, BitConverter.ToInt32(bytes, ClientLastStationOffset));
        Assert.AreEqual(9, bytes[ClientLevelOffset]);
        Assert.AreEqual((byte)0x05, bytes[ClientFlagsOffset],
            "bit0 UsingVehicle | bit2 Possess. ApplyCreateFromPacket reads these two bits only.");
        Assert.AreEqual(3, bytes[ClientGmLevelOffset]);
        Assert.AreEqual("PilotName", ReadFixedUtf8(bytes, ClientNameOffset, 51));
        Assert.AreEqual("ClanName", ReadFixedUtf8(bytes, ClientClanNameOffset, 51));
        Assert.AreEqual(1.25f, BitConverter.ToSingle(bytes, ClientScaleOffset));
        Assert.AreEqual(0, bytes[ClientNameOffset + 9], "Name is NUL-terminated inside the 51-byte field.");
        Assert.AreEqual(0, bytes[ClientClanNameOffset + 8]);
    }

    [TestMethod]
    public void Extended_FixedBodyEndsAt0x1358_AndInventoryStartsAt0x960()
    {
        var packet = new CreateCharacterExtendedPacket
        {
            ObjectId = new TFID(1, true),
            Name = "A",
            ClanName = "",
            CustomizedName = "",
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Default,
            CurrentVehicleCoid = -1,
            CurrentTrailerCoid = -1,
        };
        packet.InventoryCoids[0] = 0x1111L;
        packet.InventoryCoids[311] = 0x2222L;

        var bytes = SerializeWithRootOpcode(packet);
        Assert.AreEqual(CreateCharacterExtendedPacket.FixedPacketSizeIncludingOpcode, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.CreateCharacterExtended, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(0x1111L, BitConverter.ToInt64(bytes, 0x960),
            "Client serialize zeros 312 i64s at param_2+600 (0x960) then fills locker COIDs.");
        Assert.AreEqual(0x2222L, BitConverter.ToInt64(bytes, 0x960 + 311 * 8));
        Assert.AreEqual(-1L, BitConverter.ToInt64(bytes, 0x960 + 8),
            "Empty locker slots stay −1 (client operator loop writes 0xFFFFFFFF).");
    }

    [TestMethod]
    public void Extended_ZeroCounts_DoNotEmitTails()
    {
        var packet = new CreateCharacterExtendedPacket
        {
            ObjectId = new TFID(1, true),
            Name = "",
            ClanName = "",
            CustomizedName = "",
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Default,
            NumSkills = 0,
            NumCompletedQuests = 0,
            NumAchievements = 0,
            NumDisciplines = 0,
            NumCurrentQuests = 0,
        };

        var bytes = SerializeWithRootOpcode(packet);
        Assert.AreEqual(0x1358, bytes.Length,
            "Client size formula with all counts 0 is 0x4D6*4 = 0x1358.");
    }

    private static byte[] SerializeWithRootOpcode(CreateCharacterPacket packet)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)packet.Opcode);
            packet.Write(writer);
            stream.SetLength(stream.Position);
        }

        return stream.ToArray();
    }

    private static string ReadFixedUtf8(byte[] bytes, int offset, int width)
    {
        var end = offset;
        while (end < offset + width && bytes[end] != 0)
            end++;
        return System.Text.Encoding.UTF8.GetString(bytes, offset, end - offset);
    }
}

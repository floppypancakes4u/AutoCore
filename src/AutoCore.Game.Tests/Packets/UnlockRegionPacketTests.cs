using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

[TestClass]
public class UnlockRegionPacketTests
{
    [TestMethod]
    public void UnlockRegion_WritesSixteenBytesIncludingOpcode()
    {
        var packet = new UnlockRegionPacket
        {
            ContinentId = 42,
            UnlockFlag = 1,
            ExploredBits = 0xDEADBEEFu,
        };

        var bytes = WriteWithOpcode(packet);

        Assert.AreEqual(UnlockRegionPacket.PacketSizeIncludingOpcode, bytes.Length);
        Assert.AreEqual((uint)GameOpcode.UnlockRegion, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(42, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(1, bytes[8]);
        Assert.AreEqual(0, bytes[9]);
        Assert.AreEqual(0, bytes[10]);
        Assert.AreEqual(0, bytes[11]);
        Assert.AreEqual(0xDEADBEEFu, BitConverter.ToUInt32(bytes, 0xC));
    }

    [TestMethod]
    public void CreateCharacterExtended_WritesExplorationFromCharacter()
    {
        var character = new Character();
        character.SetCoid(1001, true);
        character.SetExplorationsForTests(new[]
        {
            new CharacterExploration { CharacterCoid = 1001, ContinentId = 7, ExploredBits = 0x00000005u },
            new CharacterExploration { CharacterCoid = 1001, ContinentId = 9, ExploredBits = 0x00000003u },
        });

        var packet = CreateMinimalExtendedPacket();
        character.WriteExploration(packet);

        var bytes = WriteWithOpcode(packet);

        Assert.AreEqual(7, BitConverter.ToInt32(bytes, 0x1B8));
        Assert.AreEqual(1, bytes[0x1BC]);
        Assert.AreEqual(0x00000005u, BitConverter.ToUInt32(bytes, 0x1C0));
        Assert.AreEqual(9, BitConverter.ToInt32(bytes, 0x1B8 + 12));
        Assert.AreEqual(0x00000003u, BitConverter.ToUInt32(bytes, 0x1C0 + 12));
        Assert.AreEqual(CreateCharacterExtendedPacket.FixedPacketSizeIncludingOpcode, bytes.Length);
    }

    [TestMethod]
    public void Character_TryRevealArea_SetsBitsOnce()
    {
        var character = new Character();
        character.SetCoid(55, true);

        Assert.IsTrue(character.TryRevealArea(3, 1, out var bits1));
        Assert.AreEqual(1u, bits1);
        Assert.IsFalse(character.TryRevealArea(3, 1, out _));
        Assert.IsTrue(character.TryRevealArea(3, 2, out var bits2));
        Assert.AreEqual(3u, bits2);
        Assert.AreEqual(3u, character.GetExploredBits(3));
    }

    private static CreateCharacterExtendedPacket CreateMinimalExtendedPacket()
    {
        return new CreateCharacterExtendedPacket
        {
            ObjectId = new TFID(1, true),
            Name = "Test",
            ClanName = "",
            CustomizedName = "",
            Position = new Vector3(0, 0, 0),
            Rotation = Quaternion.Default,
        };
    }

    private static byte[] WriteWithOpcode(BasePacket packet)
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
}

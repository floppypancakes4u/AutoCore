using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// TeleportCharacter 0x8058 — living GM snap via client CVOGReaction_TeleportTarget.
/// </summary>
[TestClass]
public class TeleportCharacterPacketTests
{
    [TestMethod]
    public void Opcode_IsTeleportCharacter()
    {
        Assert.AreEqual(GameOpcode.TeleportCharacter, new TeleportCharacterPacket().Opcode);
        Assert.AreEqual(0x8058u, (uint)GameOpcode.TeleportCharacter);
    }

    [TestMethod]
    public void Write_PadsThenFloat4_WithClientYAndZBiasInverted()
    {
        // Client FUN_00808910: wireY += 2, wireZ -= 1, then TeleportTarget.
        // Server must pre-compensate so the applied pose matches Position.
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write((uint)GameOpcode.TeleportCharacter);

        new TeleportCharacterPacket
        {
            Position = new Vector3(100f, 50f, 200f),
        }.Write(writer);
        ms.SetLength(ms.Position);

        Assert.AreEqual(4 + 12 + 16, ms.Length);
        ms.Position = 0;
        using var reader = new BinaryReader(ms);
        Assert.AreEqual(0x8058u, reader.ReadUInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        Assert.AreEqual(0, reader.ReadInt32());
        Assert.AreEqual(100f, reader.ReadSingle(), 0.0001f);
        Assert.AreEqual(50f - TeleportCharacterPacket.ClientYBias, reader.ReadSingle(), 0.0001f);
        Assert.AreEqual(200f + TeleportCharacterPacket.ClientZBias, reader.ReadSingle(), 0.0001f);
        Assert.AreEqual(0f, reader.ReadSingle(), 0.0001f);
    }
}

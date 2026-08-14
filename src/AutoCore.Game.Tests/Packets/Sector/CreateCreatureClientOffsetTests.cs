using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

using System.IO;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;

/// <summary>
/// PDB Pass 6. Offsets from client <c>FUN_0080af70</c> → <c>FUN_004c82b0</c>
/// (<c>createFromPacket</c>) and <c>CVOGCreature_PostCreateFromPacket</c> <c>0x004c5c30</c>.
/// Opcode at 0. Game-packet apply reads spawn owner at +0x108, skill count at +0x10c,
/// AI at +0x127, on-use trigger at +0x128 / reaction at +0x12c. Those reads require
/// serialized size at least <c>0x130</c>; unset optional COIDs must be −1, not 0.
/// </summary>
[TestClass]
public class CreateCreatureClientOffsetTests
{
    public const int ClientSpawnOwnerOffset = 0x108;
    public const int ClientSkillCountOffset = 0x10C;
    public const int ClientAiStateOffset = 0x127;
    public const int ClientOnUseTriggerOffset = 0x128;
    public const int ClientOnUseReactionOffset = 0x12C;
    public const int ClientCreateCreatureSize = 0x130;

    [TestMethod]
    public void Write_CoversClientApplyTail_AndUnsetIdsAreMinusOne()
    {
        var bytes = SerializeWithRootOpcode(new CreateCreaturePacket
        {
            CBID = 12001,
            ObjectId = new TFID(0x5000_1234L, true),
            CoidCurrentVehicle = 0x5000_9999L,
            Level = 7,
            EnhancementId = -1,
        });

        Assert.AreEqual(ClientCreateCreatureSize, bytes.Length,
            "FUN_004c82b0 reads +0x128 as i32; packet must include +0x12c.");
        Assert.AreEqual((uint)GameOpcode.CreateCreature, BitConverter.ToUInt32(bytes, 0));
        Assert.AreEqual(12001, BitConverter.ToInt32(bytes, 4));
        Assert.AreEqual(0x5000_1234L, BitConverter.ToInt64(bytes, 0x90));
        Assert.AreEqual(0x5000_9999L, BitConverter.ToInt64(bytes, CreateCreaturePacket.ClientVehicleCoidOffset));
        Assert.AreEqual(7, BitConverter.ToInt32(bytes, CreateCreaturePacket.ClientLevelOffset));

        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, 0x100),
            "Ghost init FUN_005d2520 sets +0x100 = −1.");
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, 0x104),
            "Ghost init FUN_005d2520 sets +0x104 = −1.");
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientSpawnOwnerOffset),
            "FUN_004c82b0 treats +0x108 != −1 as spawn-owner COID. Zero is a lookup, not unset.");
        Assert.AreEqual(0, bytes[ClientSkillCountOffset],
            "FUN_004c82b0 loops +0x10c skill entries from +0x138.");
        Assert.AreEqual(0, bytes[ClientAiStateOffset],
            "FUN_004c82b0 copies +0x127 into creature AI state.");
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientOnUseTriggerOffset),
            "FUN_004c82b0 calls FUN_004d4040 when +0x128 != −1.");
        Assert.AreEqual(-1, BitConverter.ToInt32(bytes, ClientOnUseReactionOffset));
    }

    [TestMethod]
    public void Write_BodyOnly_PadsToClientBodySize()
    {
        var packet = new CreateCreaturePacket
        {
            CBID = 7,
            ObjectId = new TFID(8, false),
            Level = 1,
        };
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        packet.Write(writer);
        if (stream.Position > stream.Length)
            stream.SetLength(stream.Position);
        Assert.AreEqual(ClientCreateCreatureSize - 4, stream.ToArray().Length);
    }

    private static byte[] SerializeWithRootOpcode(CreateCreaturePacket packet)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        if (stream.Position > stream.Length)
            stream.SetLength(stream.Position);
        return stream.ToArray();
    }
}

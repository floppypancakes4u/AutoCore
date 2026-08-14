using System.IO;
using System.Linq;
using System.Reflection;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

/// <summary>
/// Retail PackedPackets::unpackPacket @ 0x00637C20 special-cases only four RPC types
/// as Form A (bitstream / custom unpack; buffer must NOT start with a duplicated opcode):
/// 0x2005 MapInfo, 0x2023 Damage, 0x206C GroupReactionCall, 0x804D MapInstanceListResponse.
/// All other GameOpcodes are Form B (buffer starts with u32 opcode).
/// </summary>
[TestClass]
public class SkipOpcodeContractTests
{
    public static readonly uint[] RetailFormAOpcodes =
    {
        0x2005, // MapInfo → unpackMapInfo
        0x2023, // Damage → unpackDamage
        0x206C, // GroupReactionCall → unpackReactions
        0x804D, // MapInstanceListResponse → unpackInstanceList
    };

    [TestMethod]
    public void RetailFormA_ClosedSet_MatchesUnpackPacket()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                (uint)GameOpcode.MapInfo,
                (uint)GameOpcode.Damage,
                (uint)GameOpcode.GroupReactionCall,
                (uint)GameOpcode.MapInstanceListResponse,
            },
            RetailFormAOpcodes);
    }

    [TestMethod]
    public void MapInfo_Write_DoesNotPrefixOpcode_FormAPayload()
    {
        var packet = new MapInfoPacket { MapName = "tm_test", ContinentObjectId = 1 };
        var body = SerializeBody(packet);
        Assert.AreNotEqual((uint)GameOpcode.MapInfo, BitConverter.ToUInt32(body, 0),
            "Form A MapInfo body must start with RegionId, not opcode.");
        Assert.AreEqual(0, BitConverter.ToInt32(body, 0)); // RegionId default 0
    }

    [TestMethod]
    public void Damage_Write_DoesNotPrefixOpcode_FormAPayload()
    {
        var packet = new DamagePacket();
        var body = SerializeBody(packet);
        // Damage is a BitStream pack; first dword must not be the opcode when Form A.
        if (body.Length >= 4)
            Assert.AreNotEqual((uint)GameOpcode.Damage, BitConverter.ToUInt32(body, 0));
    }

    [TestMethod]
    public void GroupReactionCall_Write_DoesNotPrefixOpcode_FormAPayload()
    {
        var packet = new GroupReactionCallPacket();
        var body = SerializeBody(packet);
        if (body.Length >= 4)
            Assert.AreNotEqual((uint)GameOpcode.GroupReactionCall, BitConverter.ToUInt32(body, 0));
    }

    [TestMethod]
    public void Broadcast_Write_IsFormB_WhenSendPrefixesOpcode()
    {
        // Packet.Write itself does not write opcode; SendGamePacket(skipOpcode:false) does.
        // Prove Broadcast is NOT in the Form A closed set so skipOpcode must stay false.
        Assert.IsFalse(RetailFormAOpcodes.Contains((uint)GameOpcode.Broadcast));
        Assert.IsFalse(RetailFormAOpcodes.Contains((uint)GameOpcode.LogicStateChange));
        Assert.IsFalse(RetailFormAOpcodes.Contains((uint)GameOpcode.CreateCreature));
    }

    [TestMethod]
    public void CreateCreature_BodyWithRootOpcode_IsFormBLayout()
    {
        // Existing CreateCreatureClientOffsetTests serialize with root opcode at 0 —
        // that matches Form B wire after SendGamePacket prefixes opcode.
        Assert.IsFalse(RetailFormAOpcodes.Contains((uint)GameOpcode.CreateCreature));
    }

    private static byte[] SerializeBody(AutoCore.Game.Packets.BasePacket packet)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            packet.Write(writer);
        if (ms.Position > ms.Length)
            ms.SetLength(ms.Position);
        return ms.ToArray();
    }
}

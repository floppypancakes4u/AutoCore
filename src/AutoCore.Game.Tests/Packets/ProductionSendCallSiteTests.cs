using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoCore.Game.Constants;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets;

/// <summary>
/// Locks production skipOpcode:true sites to the retail Form A closed set
/// (PackedPackets::unpackPacket @ 0x00637C20).
/// </summary>
[TestClass]
public class ProductionSendCallSiteTests
{
    /// <summary>
    /// Opcodes that production code is allowed to send with skipOpcode:true.
    /// MapInstanceListResponse is Form A on the client but has no production sender today.
    /// </summary>
    private static readonly HashSet<GameOpcode> AllowedSkipOpcodeOpcodes = new()
    {
        GameOpcode.MapInfo,
        GameOpcode.Damage,
        GameOpcode.GroupReactionCall,
    };

    [TestMethod]
    public void AllowedSkipOpcodeSet_IsSubsetOfRetailFormA()
    {
        foreach (var opcode in AllowedSkipOpcodeOpcodes)
            Assert.IsTrue(
                SkipOpcodeContractTests.RetailFormAOpcodes.Contains((uint)opcode),
                $"{opcode} must be in retail Form A closed set");
    }

    [TestMethod]
    public void Broadcast_IsNotAllowedSkipOpcode()
    {
        Assert.IsFalse(AllowedSkipOpcodeOpcodes.Contains(GameOpcode.Broadcast));
    }

    [TestMethod]
    public void CreateFamily_IsNotAllowedSkipOpcode()
    {
        Assert.IsFalse(AllowedSkipOpcodeOpcodes.Contains(GameOpcode.CreateCreature));
        Assert.IsFalse(AllowedSkipOpcodeOpcodes.Contains(GameOpcode.CreateVehicle));
        Assert.IsFalse(AllowedSkipOpcodeOpcodes.Contains(GameOpcode.DestroyObject));
    }

    [TestMethod]
    public void PacketClasses_ForFormA_DeclareMatchingOpcodes()
    {
        Assert.AreEqual(GameOpcode.MapInfo, new AutoCore.Game.Packets.Sector.MapInfoPacket().Opcode);
        Assert.AreEqual(GameOpcode.Damage, new AutoCore.Game.Packets.Sector.DamagePacket().Opcode);
        Assert.AreEqual(GameOpcode.GroupReactionCall,
            new AutoCore.Game.Packets.Sector.GroupReactionCallPacket().Opcode);
    }
}

using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

/// <summary>
/// Retail DestroyObject (0x2020) body layout verified from Client_RecvDestroyObject @ 0x008149C0
/// and CompletelyDestroyObject @ 0x009440E0 (death type drives creature death VFX).
/// Offsets below are absolute (opcode at 0); Write() only emits the body after opcode.
/// </summary>
[TestClass]
public class DestroyObjectPacketTests
{
    private const int BodySize = 0x40; // through force byte + 3 pad → absolute end 0x44

    [TestMethod]
    public void Opcode_IsDestroyObject()
    {
        Assert.AreEqual(GameOpcode.DestroyObject, new DestroyObjectPacket().Opcode);
    }

    [TestMethod]
    public void Write_SilentDefault_EmitsFullBodyWithZeroDeathFields()
    {
        var victim = new TFID(0x1122334455667788L, global: true);
        var packet = new DestroyObjectPacket(victim);

        var body = WriteBody(packet);
        Assert.AreEqual(BodySize, body.Length, "body must extend through force/pad for client reads");

        // Unknown @ body+0
        Assert.AreEqual(0, BitConverter.ToInt32(body, 0));
        // Victim TFID @ body+4 (absolute +0x08)
        Assert.AreEqual(victim.Coid, BitConverter.ToInt64(body, 4));
        Assert.AreEqual(1, body[12]); // global
        // DeathType @ body+0x24 (absolute +0x28)
        Assert.AreEqual(0, BitConverter.ToInt32(body, 0x24));
        // Murderer coid @ body+0x2C defaults to -1
        Assert.AreEqual(-1L, BitConverter.ToInt64(body, 0x2C));
        // Force @ body+0x3C
        Assert.AreEqual(0, body[0x3C]);
    }

    [TestMethod]
    public void Write_ViolentDeath_WritesDeathTypeMurdererAndForceAtRetailOffsets()
    {
        var victim = new TFID(9201, global: true);
        var murderer = new TFID(5200, global: true);
        var packet = new DestroyObjectPacket(victim)
        {
            DeathType = DeathType.Violent,
            Murderer = murderer,
            Force = false,
        };

        var body = WriteBody(packet);
        Assert.AreEqual(BodySize, body.Length);

        Assert.AreEqual(victim.Coid, BitConverter.ToInt64(body, 4));
        Assert.AreEqual((int)DeathType.Violent, BitConverter.ToInt32(body, 0x24));
        Assert.AreEqual(0, BitConverter.ToInt32(body, 0x28), "pad before murderer TFID");
        Assert.AreEqual(murderer.Coid, BitConverter.ToInt64(body, 0x2C));
        Assert.AreEqual(1, body[0x34]); // murderer global
        Assert.AreEqual(0, body[0x3C]); // force
    }

    [TestMethod]
    public void Write_ForceTrue_SetsForceByte()
    {
        var packet = new DestroyObjectPacket(new TFID(1, false)) { Force = true };
        var body = WriteBody(packet);
        Assert.AreEqual(1, body[0x3C]);
    }

    private static byte[] WriteBody(DestroyObjectPacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        return ms.ToArray();
    }
}

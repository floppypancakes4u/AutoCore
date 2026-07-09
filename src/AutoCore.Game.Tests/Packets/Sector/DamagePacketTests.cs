using AutoCore.Game.Constants;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Utils;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class DamagePacketTests
{
    [TestMethod]
    public void Opcode_IsDamage()
    {
        Assert.AreEqual(GameOpcode.Damage, new DamagePacket().Opcode);
    }

    [TestMethod]
    public void DamageEntryFlags_StaticFactories()
    {
        Assert.IsFalse(DamagePacket.DamageEntryFlags.None.IsCrit);
        Assert.IsTrue(DamagePacket.DamageEntryFlags.Crit.IsCrit);
        Assert.IsTrue(DamagePacket.DamageEntryFlags.Resist.IsResist);
        Assert.IsTrue(DamagePacket.DamageEntryFlags.Deflect.IsDeflect);
    }

    [TestMethod]
    public void Write_EmptyEntries_WritesZeroCount()
    {
        var packet = new DamagePacket { Source = new TFID(99, true) };
        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);
        Assert.IsTrue(stream.Read(out long coid));
        Assert.AreEqual(99L, coid);
        Assert.IsTrue(stream.ReadFlag());
        Assert.AreEqual(0u, stream.ReadInt(16));
    }

    [TestMethod]
    public void Write_NullSource_UsesMinusOneCoid()
    {
        var packet = new DamagePacket { Source = null };
        packet.AddHit(new TFID(5, false), 3);
        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);
        Assert.IsTrue(stream.Read(out long coid));
        Assert.AreEqual(-1L, coid);
        Assert.IsFalse(stream.ReadFlag());
    }

    [TestMethod]
    public void Write_CritAndDeflectFlags()
    {
        var critPacket = new DamagePacket { Source = new TFID(1, false) };
        critPacket.AddHit(new TFID(2, false), 10, DamagePacket.DamageEntryFlags.Crit);
        var critBytes = WritePacket(critPacket);
        var stream = new BitStream(critBytes, (uint)critBytes.Length);
        stream.Read(out long _);
        stream.ReadFlag();
        stream.ReadInt(16);
        Assert.IsTrue(stream.ReadFlag()); // crit
        Assert.AreEqual(10u, stream.ReadInt(16));

        var defPacket = new DamagePacket { Source = new TFID(1, false) };
        defPacket.AddHit(new TFID(2, false), 1, DamagePacket.DamageEntryFlags.Deflect);
        var defBytes = WritePacket(defPacket);
        stream = new BitStream(defBytes, (uint)defBytes.Length);
        stream.Read(out long _);
        stream.ReadFlag();
        stream.ReadInt(16);
        Assert.IsFalse(stream.ReadFlag()); // crit false
        stream.ReadInt(16);
        stream.Read(out long _);
        stream.ReadFlag();
        Assert.IsFalse(stream.ReadFlag()); // resist
        Assert.IsTrue(stream.ReadFlag()); // deflect
    }

    [TestMethod]
    public void Write_MultipleEntries()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), 10);
        packet.AddHit(new TFID(3, false), 20, DamagePacket.DamageEntryFlags.Crit);
        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);
        stream.Read(out long _);
        stream.ReadFlag();
        Assert.AreEqual(2u, stream.ReadInt(16));

        stream.ReadFlag();
        Assert.AreEqual(10u, stream.ReadInt(16));
        stream.Read(out long t1);
        Assert.AreEqual(2L, t1);
        stream.ReadFlag();
        for (var i = 0; i < 9; i++) stream.ReadFlag();

        Assert.IsTrue(stream.ReadFlag()); // crit on second
        Assert.AreEqual(20u, stream.ReadInt(16));
        stream.Read(out long t2);
        Assert.AreEqual(3L, t2);
    }

    [TestMethod]
    public void AddHit_NullTarget_UsesDefaultTfId()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(null, 5);
        Assert.IsNotNull(packet.Entries[0].Target);
        Assert.AreEqual(5, packet.Entries[0].Amount);
    }

    [TestMethod]
    public void Write_SingleHit_RoundTripsThroughBitStreamUnpackLayout()
    {
        var source = new TFID(0x1122334455667788L, global: false);
        var target = new TFID(0x0AABBCCDDEEFF001L, global: true);
        const short amount = 42;

        var packet = new DamagePacket { Source = source };
        packet.AddHit(target, amount);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);

        Assert.IsTrue(stream.Read(out long headerCoid));
        Assert.AreEqual(source.Coid, headerCoid);
        Assert.AreEqual(source.Global, stream.ReadFlag());
        Assert.AreEqual(1u, stream.ReadInt(16));

        Assert.IsFalse(stream.ReadFlag()); // crit
        Assert.AreEqual((uint)(ushort)amount, stream.ReadInt(16));
        Assert.IsTrue(stream.Read(out long entryCoid));
        Assert.AreEqual(target.Coid, entryCoid);
        Assert.IsTrue(stream.ReadFlag()); // target.Global
        Assert.IsFalse(stream.ReadFlag()); // resist
        Assert.IsFalse(stream.ReadFlag()); // deflect
    }

    [TestMethod]
    public void Write_Amount250_NotClampedTo99()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), 250);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);

        Assert.IsTrue(stream.Read(out long _));
        _ = stream.ReadFlag();
        Assert.AreEqual(1u, stream.ReadInt(16));
        _ = stream.ReadFlag();
        Assert.AreEqual(250u, stream.ReadInt(16));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(99)]
    [DataRow(250)]
    [DataRow(9999)]
    [DataRow(16384)]
    [DataRow(30000)]
    [DataRow(32766)]
    public void Write_Amount_RoundTripsUpToSafeMax(int amount)
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), amount);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);

        Assert.IsTrue(stream.Read(out long _));
        Assert.IsFalse(stream.ReadFlag());
        Assert.AreEqual(1u, stream.ReadInt(16));
        Assert.IsFalse(stream.ReadFlag()); // crit must stay clear (bit packing must not bleed)
        var got = (int)(short)stream.ReadInt(16);
        Assert.AreEqual(amount, got, $"amount round-trip failed for {amount}");
        Assert.IsTrue(stream.Read(out long _));
        Assert.IsFalse(stream.ReadFlag()); // global may be false
        Assert.IsFalse(stream.ReadFlag()); // resist must stay clear
        Assert.IsFalse(stream.ReadFlag()); // deflect must stay clear
    }

    [TestMethod]
    public void AddHit_ClampsInt16MaxToSafeDisplayMax()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), short.MaxValue); // 32767
        Assert.AreEqual(DamagePacket.MaxDisplayAmount, packet.Entries[0].Amount);
    }

    [TestMethod]
    public void AddHit_ClampsInt16MinToSafeDisplayMin()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), short.MinValue); // -32768
        Assert.AreEqual(DamagePacket.MinDisplayAmount, packet.Entries[0].Amount);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(-83)]
    [DataRow(-250)]
    [DataRow(-32766)]
    public void Write_NegativeAmount_RoundTripsForHealProbe(int amount)
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), amount);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);

        Assert.IsTrue(stream.Read(out long _));
        Assert.IsFalse(stream.ReadFlag());
        Assert.AreEqual(1u, stream.ReadInt(16));
        Assert.IsFalse(stream.ReadFlag()); // crit
        var got = (int)(short)stream.ReadInt(16);
        Assert.AreEqual(amount, got, $"negative amount round-trip failed for {amount}");
    }

    [TestMethod]
    public void Write_AmountZero_AllowedForMissProbe()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        packet.AddHit(new TFID(2, false), 0);
        Assert.AreEqual(0, packet.Entries[0].Amount);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);
        Assert.IsTrue(stream.Read(out long _));
        _ = stream.ReadFlag();
        Assert.AreEqual(1u, stream.ReadInt(16));
        _ = stream.ReadFlag();
        Assert.AreEqual(0u, stream.ReadInt(16));
    }

    [TestMethod]
    public void Write_ResistFlag_SetsFirstPostTfidFlag()
    {
        var packet = new DamagePacket { Source = new TFID(1, false) };
        // Non-zero amount required on the wire so the client queues the combat event.
        packet.AddHit(new TFID(2, false), 1, DamagePacket.DamageEntryFlags.Resist);

        var bytes = WritePacket(packet);
        var stream = new BitStream(bytes, (uint)bytes.Length);
        Assert.IsTrue(stream.Read(out long _));
        _ = stream.ReadFlag();
        Assert.AreEqual(1u, stream.ReadInt(16));
        Assert.IsFalse(stream.ReadFlag()); // crit
        Assert.AreEqual(1u, stream.ReadInt(16));
        Assert.IsTrue(stream.Read(out long _));
        _ = stream.ReadFlag(); // target global
        Assert.IsTrue(stream.ReadFlag()); // resist
        Assert.IsFalse(stream.ReadFlag()); // deflect
    }

    private static byte[] WritePacket(DamagePacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        writer.Flush();
        return ms.ToArray();
    }
}

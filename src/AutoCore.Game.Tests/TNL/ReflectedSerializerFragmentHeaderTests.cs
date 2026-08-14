using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Structures;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB foundation: client fragment RPC fields are 16-bit ushorts
/// (<c>FUN_005A32A0</c> / <c>FUN_005A3230</c>). AutoCore write already used
/// <see cref="BitStream.Write(ushort)"/>; the reflected <c>UInt16</c> read
/// must consume the same 16 bits, not a 32-bit uint.
/// </summary>
[TestClass]
public class ReflectedSerializerFragmentHeaderTests
{
    private const uint Type = 0x2016;
    private const ushort Fragment = 7;
    private const ushort FragmentId = 2;
    private const ushort FragmentCount = 5;

    [TestMethod]
    public void FragmentRpc_Write_Uses32_16_16_16_10_PlusPayloadBits()
    {
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        var stream = WriteFragmentRpc(Type, Fragment, FragmentId, FragmentCount, payload);

        var expectedBits = 32 + 16 + 16 + 16 + 10 + payload.Length * 8;
        Assert.AreEqual((uint)expectedBits, stream.GetBitPosition());
    }

    [TestMethod]
    public void FragmentRpc_RoundTrip_PreservesFields_AndBitLength()
    {
        var payload = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        var written = WriteFragmentRpc(Type, Fragment, FragmentId, FragmentCount, payload);
        var bits = written.GetBitPosition();

        written.SetBitPosition(0);
        Assert.AreEqual(Type, (uint)ReflectedSerializer.Read(written, typeof(uint)));
        Assert.AreEqual(Fragment, (ushort)ReflectedSerializer.Read(written, typeof(ushort)));
        Assert.AreEqual(FragmentId, (ushort)ReflectedSerializer.Read(written, typeof(ushort)));
        Assert.AreEqual(FragmentCount, (ushort)ReflectedSerializer.Read(written, typeof(ushort)));

        var buffer = (ByteBuffer)ReflectedSerializer.Read(written, typeof(ByteBuffer));
        Assert.AreEqual((uint)payload.Length, buffer.GetBufferSize());
        CollectionAssert.AreEqual(payload, buffer.GetBuffer());
        Assert.AreEqual(bits, written.GetBitPosition(), "read cursor must land on the write bit count (no 16-bit/32-bit drift)");
    }

    [TestMethod]
    public void FragmentRpc_HandPackedRetailHeader_DecodesWithoutBitOffsetDrift()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var stream = new BitStream(new byte[64], 64);

        // Retail pack FUN_005A32A0: u32 type, u16 × 3, then 10-bit ByteBuffer length + payload.
        stream.WriteInt(Type, 32);
        stream.Write(Fragment);
        stream.Write(FragmentId);
        stream.Write(FragmentCount);
        stream.WriteInt((uint)payload.Length, 10);
        stream.Write((uint)payload.Length, payload);

        var packedBits = stream.GetBitPosition();
        Assert.AreEqual((uint)(32 + 16 + 16 + 16 + 10 + payload.Length * 8), packedBits);

        stream.SetBitPosition(0);
        Assert.AreEqual(Type, (uint)ReflectedSerializer.Read(stream, typeof(uint)));
        Assert.AreEqual(Fragment, (ushort)ReflectedSerializer.Read(stream, typeof(ushort)));
        Assert.AreEqual(FragmentId, (ushort)ReflectedSerializer.Read(stream, typeof(ushort)));
        Assert.AreEqual(FragmentCount, (ushort)ReflectedSerializer.Read(stream, typeof(ushort)));

        var buffer = (ByteBuffer)ReflectedSerializer.Read(stream, typeof(ByteBuffer));
        CollectionAssert.AreEqual(payload, buffer.GetBuffer());
        Assert.AreEqual(packedBits, stream.GetBitPosition());
    }

    [TestMethod]
    public void UInt32_StillConsumes32Bits()
    {
        var stream = new BitStream(new byte[16], 16);
        stream.Write((uint)0xAABBCCDD);
        Assert.AreEqual(32u, stream.GetBitPosition());

        stream.SetBitPosition(0);
        var value = (uint)ReflectedSerializer.Read(stream, typeof(uint));
        Assert.AreEqual(0xAABBCCDDu, value);
        Assert.AreEqual(32u, stream.GetBitPosition());
    }

    [TestMethod]
    public void Int16_RoundTrip_Consumes16Bits()
    {
        var stream = new BitStream(new byte[8], 8);
        ReflectedSerializer.Write(stream, (short)-300, typeof(short));
        Assert.AreEqual(16u, stream.GetBitPosition());

        stream.SetBitPosition(0);
        var value = (short)ReflectedSerializer.Read(stream, typeof(short));
        Assert.AreEqual((short)-300, value);
        Assert.AreEqual(16u, stream.GetBitPosition());
    }

    private static BitStream WriteFragmentRpc(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, byte[] payload)
    {
        var stream = new BitStream(new byte[128], 128);
        ReflectedSerializer.Write(stream, type, typeof(uint));
        ReflectedSerializer.Write(stream, fragment, typeof(ushort));
        ReflectedSerializer.Write(stream, fragmentId, typeof(ushort));
        ReflectedSerializer.Write(stream, fragmentCount, typeof(ushort));
        ReflectedSerializer.Write(stream, new ByteBuffer(payload, (uint)payload.Length), typeof(ByteBuffer));
        return stream;
    }
}

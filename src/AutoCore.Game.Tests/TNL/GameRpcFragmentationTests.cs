using System.Reflection;
using AutoCore.Game.Constants;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Structures;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB foundation: client <c>TNLWrapper::AddMessageToQueue</c> (0x005A05A0)
/// fragments when serialized size &gt; 220, using the same 220-byte chunk size.
/// </summary>
[TestClass]
public class GameRpcFragmentationTests
{
    private readonly List<TNLConnection.GameRpcCapture> _rpcs = new();
    private TNLConnection _conn;

    [TestInitialize]
    public void Init()
    {
        _rpcs.Clear();
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = (_, rpc) => _rpcs.Add(rpc);
        TNLConnection.TestReassembledBufferSink = null;

        _conn = new TNLConnection();
        _conn.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = null;
        TNLConnection.TestReassembledBufferSink = null;
        _rpcs.Clear();
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(219)]
    [DataRow(220)]
    public void SerializedSize_AtOrBelow220_SendsOneNonFragmentedRpc(int serializedBytes)
    {
        SendSized(serializedBytes);

        Assert.AreEqual(1, _rpcs.Count, $"size {serializedBytes} must be a single RPC");
        Assert.IsFalse(_rpcs[0].IsFragmented, $"size {serializedBytes} must not fragment");
        Assert.AreEqual(nameof(TNLConnection.rpcMsgGuaranteedOrdered), _rpcs[0].Method);
        Assert.IsTrue(_rpcs[0].Data.Length <= 220);
        Assert.AreEqual(serializedBytes, _rpcs[0].Data.Length);
        Assert.AreEqual((uint)GameOpcode.News, _rpcs[0].Type);
    }

    [TestMethod]
    [DataRow(221)]
    [DataRow(1023)]
    [DataRow(1024)]
    [DataRow(1400)]
    [DataRow(1401)]
    [DataRow(4096)]
    public void SerializedSize_Above220_FragmentsInto220ByteChunks(int serializedBytes)
    {
        SendSized(serializedBytes);

        var expectedCount = (int)Math.Ceiling(serializedBytes / 220.0);
        Assert.AreEqual(expectedCount, _rpcs.Count, $"size {serializedBytes}");
        Assert.IsTrue(_rpcs.All(r => r.IsFragmented), $"size {serializedBytes} must use fragmented RPCs");
        Assert.IsTrue(_rpcs.All(r => r.Method == nameof(TNLConnection.rpcMsgGuaranteedOrderedFragmented)));
        Assert.IsTrue(_rpcs.All(r => r.Data.Length <= 220), "every chunk must be <= 220");
        Assert.IsTrue(_rpcs.All(r => r.FragmentCount == expectedCount));
        Assert.IsTrue(_rpcs.All(r => r.Type == (uint)GameOpcode.News));

        var ids = _rpcs.Select(r => r.FragmentId!.Value).OrderBy(i => i).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(0, expectedCount).Select(i => (ushort)i).ToArray(), ids);

        var sequences = _rpcs.Select(r => r.Fragment!.Value).Distinct().ToArray();
        Assert.AreEqual(1, sequences.Length, "sequence increments once per original message");
    }

    [TestMethod]
    public void FragmentSequence_IncrementsOncePerMessage()
    {
        SendSized(221);
        var firstSeq = _rpcs[0].Fragment!.Value;
        _rpcs.Clear();

        SendSized(400);
        var secondSeq = _rpcs[0].Fragment!.Value;

        Assert.AreEqual((ushort)(firstSeq + 1), secondSeq);
    }

    [TestMethod]
    public void Fragments_ReassembleToOriginalBytes_IncludingOpcodeAtOffsetZero()
    {
        const int size = 1024;
        var original = SerializeSized(size, skipOpcode: false);
        SendSized(size);

        byte[] reassembled = null;
        TNLConnection.TestReassembledBufferSink = bytes => reassembled = bytes;

        InvokeProcessFragments(_conn, _rpcs);

        Assert.IsNotNull(reassembled);
        CollectionAssert.AreEqual(original, reassembled);
        Assert.AreEqual((uint)GameOpcode.News, BitConverter.ToUInt32(reassembled, 0));
    }

    [TestMethod]
    public void NonFragmentedPath_NeverEmitsByteBufferLargerThan220()
    {
        foreach (var size in new[] { 0, 1, 219, 220, 221, 1023, 1024, 1400, 1401, 4096 })
        {
            _rpcs.Clear();
            SendSized(size);
            foreach (var rpc in _rpcs.Where(r => !r.IsFragmented))
                Assert.IsTrue(rpc.Data.Length <= 220, $"non-fragmented RPC of {rpc.Data.Length} bytes at size {size}");
        }
    }

    [TestMethod]
    public void CreateCharacterExtended_FragmentsAndReassembles()
    {
        var packet = new CreateCharacterExtendedPacket
        {
            ObjectId = new TFID(1, true),
            Name = "T",
            ClanName = ""
        };
        var original = SerializePacket(packet, skipOpcode: false);
        Assert.AreEqual(CreateCharacterExtendedPacket.FixedPacketSizeIncludingOpcode, original.Length);

        _conn.SendGamePacket(packet);

        Assert.IsTrue(_rpcs.Count > 1);
        Assert.IsTrue(_rpcs.All(r => r.IsFragmented));
        Assert.IsTrue(_rpcs.All(r => r.Data.Length <= 220));
        Assert.AreEqual((uint)GameOpcode.CreateCharacterExtended, _rpcs[0].Type);

        byte[] reassembled = null;
        TNLConnection.TestReassembledBufferSink = bytes => reassembled = bytes;
        InvokeProcessFragments(_conn, _rpcs);

        CollectionAssert.AreEqual(original, reassembled);
        Assert.AreEqual((uint)GameOpcode.CreateCharacterExtended, BitConverter.ToUInt32(reassembled, 0));
    }

    [TestMethod]
    public void InventoryCargoSendAll_FragmentsAndReassembles()
    {
        var packet = new InventoryCargoSendAllPacket();
        var original = SerializePacket(packet, skipOpcode: false);
        Assert.AreEqual(4 + 4 + InventoryCargoSendAllPacket.ItemCount * 16, original.Length);

        _conn.SendGamePacket(packet);

        Assert.IsTrue(_rpcs.Count > 1);
        Assert.IsTrue(_rpcs.All(r => r.IsFragmented && r.Data.Length <= 220));
        Assert.AreEqual((uint)GameOpcode.InventoryCargoSendAll, _rpcs[0].Type);

        byte[] reassembled = null;
        TNLConnection.TestReassembledBufferSink = bytes => reassembled = bytes;
        InvokeProcessFragments(_conn, _rpcs);

        CollectionAssert.AreEqual(original, reassembled);
    }

    [TestMethod]
    public void FragmentedRpc_WireHeader_Is32_16_16_16_10()
    {
        SendSized(221);
        var chunk = _rpcs[0];
        var stream = new BitStream(new byte[512], 512);
        ReflectedSerializer.Write(stream, chunk.Type, typeof(uint));
        ReflectedSerializer.Write(stream, chunk.Fragment!.Value, typeof(ushort));
        ReflectedSerializer.Write(stream, chunk.FragmentId!.Value, typeof(ushort));
        ReflectedSerializer.Write(stream, chunk.FragmentCount!.Value, typeof(ushort));
        ReflectedSerializer.Write(stream, new ByteBuffer(chunk.Data, (uint)chunk.Data.Length), typeof(ByteBuffer));

        Assert.AreEqual((uint)(32 + 16 + 16 + 16 + 10 + chunk.Data.Length * 8), stream.GetBitPosition());
    }

    private void SendSized(int serializedBytes)
    {
        _conn.SendGamePacket(new SizedBodyPacket(serializedBytes), skipOpcode: serializedBytes < 4);
    }

    private static byte[] SerializeSized(int serializedBytes, bool skipOpcode)
    {
        return SerializePacket(new SizedBodyPacket(serializedBytes), skipOpcode);
    }

    private static byte[] SerializePacket(BasePacket packet, bool skipOpcode)
    {
        using var stream = new MemoryStream(0x4000);
        using var writer = new BinaryWriter(stream);
        if (!skipOpcode)
            writer.Write((uint)packet.Opcode);
        packet.Write(writer);
        stream.SetLength(stream.Position);
        return stream.ToArray();
    }

    private static void InvokeProcessFragments(TNLConnection conn, IReadOnlyList<TNLConnection.GameRpcCapture> rpcs)
    {
        var method = typeof(TNLConnection).GetMethod("ProcessFragment", BindingFlags.Instance | BindingFlags.NonPublic);
        var bucket = typeof(TNLConnection)
            .GetProperty("FragmentGuaranteedOrdered", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(conn);

        foreach (var rpc in rpcs.OrderBy(r => r.FragmentId))
        {
            method!.Invoke(conn, new object[]
            {
                new ByteBuffer(rpc.Data, (uint)rpc.Data.Length),
                bucket,
                rpc.Type,
                rpc.Fragment!.Value,
                rpc.FragmentId!.Value,
                rpc.FragmentCount!.Value
            });
        }
    }

    private sealed class SizedBodyPacket : BasePacket
    {
        private readonly byte[] _body;

        public SizedBodyPacket(int serializedBytes)
        {
            // Form B (opcode prefix) unless the requested size is smaller than 4 bytes.
            var bodyLen = serializedBytes < 4 ? serializedBytes : serializedBytes - 4;
            _body = new byte[bodyLen];
            for (var i = 0; i < _body.Length; i++)
                _body[i] = (byte)(i + 1);
        }

        public override GameOpcode Opcode => GameOpcode.News;

        public override void Write(BinaryWriter writer)
        {
            if (_body.Length > 0)
                writer.Write(_body);
        }
    }
}

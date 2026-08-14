using System.Net;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Utils.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using global::TNL.Utils;

namespace AutoCore.Game.Tests.Packets;

/// <summary>
/// Closure-pass contracts for the 13 leftover production PARTIAL opcodes:
/// retail ConvoyMissions wire, social stub safety, Firing capture, unknown-opcode context.
/// </summary>
[TestClass]
public class OpcodeClosureTests
{
    private InMemoryLogSink _sink = null!;

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        FiringPacketCapture.Clear();
        FiringPacketCapture.Enabled = true;
        TNLConnection.TestPacketSink = null;
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        FiringPacketCapture.Clear();
        FiringPacketCapture.Enabled = true;
        TNLConnection.TestPacketSink = null;
    }

    [TestMethod]
    public void ConvoyMissionsResponse_Write_MatchesRetailHeaderAndU16MissionIds()
    {
        var packet = new ConvoyMissionsResponsePacket
        {
            CoidMember = 0x1122334455667788L,
            MissionIds = [1001, 874],
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        // Form B body after SendGamePacket's opcode prefix:
        // +0 pad4, +4 i64 coidMember, +12 u16 count, +14 pad2, +16 ptr slot, +20 u16[] ids
        Assert.AreEqual(24, bytes.Length);
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, 0));
        Assert.AreEqual(0x1122334455667788L, BitConverter.ToInt64(bytes, 4));
        Assert.AreEqual((ushort)2, BitConverter.ToUInt16(bytes, 12));
        Assert.AreEqual((ushort)0, BitConverter.ToUInt16(bytes, 14));
        Assert.AreEqual(0, BitConverter.ToInt32(bytes, 16));
        Assert.AreEqual((ushort)1001, BitConverter.ToUInt16(bytes, 20));
        Assert.AreEqual((ushort)874, BitConverter.ToUInt16(bytes, 22));
    }

    [TestMethod]
    public void ConvoyMissionsResponse_Write_EmptyList_Is24ByteHeaderMinusOpcode()
    {
        var packet = new ConvoyMissionsResponsePacket { CoidMember = 9001 };
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();

        Assert.AreEqual(20, bytes.Length);
        Assert.AreEqual(9001L, BitConverter.ToInt64(bytes, 4));
        Assert.AreEqual((ushort)0, BitConverter.ToUInt16(bytes, 12));
    }

    [TestMethod]
    public void ConvoyMissionsResponse_Write_TruncatesMissionIdToU16()
    {
        var packet = new ConvoyMissionsResponsePacket
        {
            MissionIds = [91001],
        };

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        packet.Write(writer);
        var bytes = ms.ToArray();
        Assert.AreEqual(unchecked((ushort)91001), BitConverter.ToUInt16(bytes, 20));
    }

    [TestMethod]
    public void ConvoyMissions_IsFormB_NotInRetailFormAClosedSet()
    {
        Assert.IsFalse(SkipOpcodeContractTests.RetailFormAOpcodes.Contains((uint)GameOpcode.ConvoyMissionsRequest));
        Assert.IsFalse(SkipOpcodeContractTests.RetailFormAOpcodes.Contains((uint)GameOpcode.ConvoyMissionsResponse));
    }

    [TestMethod]
    public void GameOpcode_HasNoNameForClientDialogOffset_0x650()
    {
        Assert.IsFalse(Enum.IsDefined(typeof(GameOpcode), 0x650u));
        Assert.AreEqual(0x206Eu, (uint)GameOpcode.MissionDialogResponse);
    }

    [TestMethod]
    [DataRow(GameOpcode.AddFriend)]
    [DataRow(GameOpcode.RemoveFriend)]
    [DataRow(GameOpcode.GetFriends)]
    [DataRow(GameOpcode.AddIgnore)]
    [DataRow(GameOpcode.RemoveIgnore)]
    [DataRow(GameOpcode.GetIgnored)]
    [DataRow(GameOpcode.AddEnemy)]
    [DataRow(GameOpcode.GetEnemies)]
    [DataRow(GameOpcode.RemoveEnemy)]
    [DataRow(GameOpcode.RequestClanInfo)]
    public void SocialStub_KnownOpcodeDoesNotThrow(GameOpcode opcode)
    {
        var conn = CreateConnection();
        Dispatch(conn, opcode, Array.Empty<byte>());
        Dispatch(conn, opcode, new byte[1]);
        Dispatch(conn, opcode, new byte[0x40]);
        Dispatch(conn, opcode, Array.Empty<byte>());
    }

    [TestMethod]
    public void HandlePacket_Firing_RecordsCaptureWithoutReinterpreting()
    {
        var conn = CreateConnection();
        var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        Dispatch(conn, GameOpcode.Firing, body);

        var snap = FiringPacketCapture.Snapshot();
        Assert.AreEqual(1, snap.Count);
        Assert.AreEqual((uint)GameOpcode.Firing, snap[0].Opcode);
        Assert.AreEqual(8, snap[0].Length);
        StringAssert.StartsWith(snap[0].Hex.ToUpperInvariant(), "22200000");
        StringAssert.EndsWith(snap[0].Hex.ToUpperInvariant(), "AABBCCDD");
    }

    [TestMethod]
    public void HandlePacket_VehicleMoved_DoesNotRecordFiringCapture()
    {
        var conn = CreateConnection();
        Dispatch(conn, GameOpcode.VehicleMoved, new byte[16]);
        Assert.AreEqual(0, FiringPacketCapture.Snapshot().Count);
    }

    [TestMethod]
    public void HandlePacket_UnknownOpcode_IncludesSizeSurfaceAndIdentity()
    {
        var conn = CreateConnection();
        conn.SetPlayerCOID(4242);
        _sink.Clear();

        Dispatch(conn, (GameOpcode)0x2FFF, new byte[] { 1, 2, 3, 4 });

        var rec = _sink.Single("UnknownOpcodeReceived");
        Assert.AreEqual("NET-001", rec.GetProperty("ErrorCode"));
        Assert.AreEqual(8, Convert.ToInt32(rec.GetProperty("PacketSize")));
        Assert.AreEqual("Global", rec.GetProperty("Surface"));
        Assert.AreEqual(4242L, Convert.ToInt64(rec.GetProperty("CharacterCoid")));
        Assert.AreEqual(-1, Convert.ToInt32(rec.GetProperty("Map")));
    }

    private static TNLConnection CreateConnection()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        connection.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
        return connection;
    }

    private static void Dispatch(TNLConnection connection, GameOpcode opcode, byte[] body)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)opcode);
            writer.Write(body);
            writer.Flush();
        }

        connection.HandlePacketForTests(new ByteBuffer(stream.ToArray(), (uint)stream.Length));
    }
}

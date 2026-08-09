using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.Tests.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Utils.Logging;
using global::TNL.Utils;

/// <summary>Phase 4B: unknown opcodes emit UnknownOpcodeReceived (NET-001).</summary>
[TestClass]
public class UnknownOpcodeLoggingTests
{
    private InMemoryLogSink _sink = null!;

    [TestInitialize]
    public void Init()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
    }

    [TestMethod]
    public void HandlePacket_UnknownOpcode_EmitsNet001()
    {
        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var bytes = BitConverter.GetBytes((uint)0x2FFF);
        var buffer = new ByteBuffer(bytes, (uint)bytes.Length);
        _sink.Clear();

        conn.HandlePacketForTests(buffer);

        var rec = _sink.Single("UnknownOpcodeReceived");
        Assert.AreEqual("NET-001", rec.GetProperty("ErrorCode"));
    }

    [TestMethod]
    public void HandlePacket_StuntComplete_IsAcceptedSilently()
    {
        // The client fires StuntComplete whenever its local stunt/airborne state ends —
        // including on ram impacts — so it must be a known no-op, not an unknown-opcode error.
        var conn = new TNLConnection();
        conn.SetGhostFrom(true);
        conn.SetGhostTo(false);

        var bytes = BitConverter.GetBytes((uint)Constants.GameOpcode.StuntComplete);
        var buffer = new ByteBuffer(bytes, (uint)bytes.Length);
        _sink.Clear();

        conn.HandlePacketForTests(buffer);

        Assert.AreEqual(0, _sink.Records.Count(r => r.EventName == "UnknownOpcodeReceived"),
            "StuntComplete is a known client notification and must not log as unknown.");
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.TNL;

/// <summary>
/// Sector NPC pose is ~500 bits per GhostVehicle. TNL default rates (2500 B/s, 96ms)
/// yield ~240 B/packet — only a few poses fit, so each NPC updates every few packets
/// (~250–500ms skip cadence). <see cref="TNLConnection"/> floors rates when ghosting.
/// </summary>
[TestClass]
public class SectorGhostRateFloorTests
{
    [TestMethod]
    public void ComputeNegotiatedRate_WhenGhosting_FloorsBandwidthAndPeriodAgainstDefaultRemote()
    {
        var conn = new TNLConnection();
        conn.SetGhostFrom(true);

        // Simulate client rate info that collapsed both sides to TNL defaults
        // (LocalRate and RemoteRate share one object until fully independent).
        conn.SetFixedRateParameters(
            minPacketSendPeriod: 96,
            minPacketRecvPeriod: 96,
            maxSendBandwidth: 2500,
            maxRecvBandwidth: 2500);

        Assert.IsTrue(conn.NegotiatedPacketSendPeriodMs <= TNLConnection.SectorGhostMaxSendPeriodMs,
            $"Period must be floored to ≤{TNLConnection.SectorGhostMaxSendPeriodMs}ms, got {conn.NegotiatedPacketSendPeriodMs}");

        // Floor size = SectorGhostMinSendBandwidth * period * 0.001 (≥ 20000*32*0.001 if period drops).
        Assert.IsTrue(conn.NegotiatedPacketSendSizeBytes >= 500,
            $"Packet size must fit multi-NPC pose (got {conn.NegotiatedPacketSendSizeBytes} B)");
    }

    [TestMethod]
    public void ComputeNegotiatedRate_WhenNotGhosting_DoesNotApplySectorFloor()
    {
        var conn = new TNLConnection();
        // No SetGhostFrom — DoesGhostFrom() is false.
        conn.SetFixedRateParameters(96, 96, 2500, 2500);

        // Without ghost floor, size = 2500 * 96 * 0.001 = 240.
        Assert.AreEqual(240u, conn.NegotiatedPacketSendSizeBytes);
        Assert.AreEqual(96u, conn.NegotiatedPacketSendPeriodMs);
    }
}

using System.Net;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// The Back Range freeze survives a clean map-transfer handshake: Stage2/Stage3/ack all complete,
/// the Creates go out byte-identical to a working entry, and then nothing. The 2026-08-13 WireDiag
/// capture shows why — every ForeignGhostScope during the frozen entry reports
/// <c>ghosting=0 scoping=1</c>, i.e. <c>ActivateGhosting</c> ran but the client's
/// <c>rpcReadyForNormalGhosts</c> never flipped <c>Ghosting</c> true.
/// <para>
/// TNL drops that reply silently when it does not match the current sequence
/// (<c>if (sequence == GhostingSequence) Ghosting = true;</c> — no else), so the connection sits
/// scoping into a stream the client is not consuming and the player never leaves the loading
/// screen. This watchdog makes that state say so.
/// </para>
/// </summary>
[TestClass]
public class GhostingNeverStartedWatchdogTests
{
    private const long CharCoid = 9_082_000_301L;
    private const long VehicleCoid = 9_082_000_302L;
    private const int DestContinentId = 693;

    private readonly List<BasePacket> _sent = new();
    private long _now;

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        _now = 5_000_000L;
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        TNLConnection.MapTransferHandshakeClock = () => _now;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.MapTransferHandshakeClock = static () => Environment.TickCount64;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        AutoCore.Utils.Logging.GameLog.ResetForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void NoWorldEntry_NeverReportsGhostingStall()
    {
        var connection = CreateConnection();

        Assert.IsFalse(connection.ReportGhostingNeverStarted(_now + 600_000));
    }

    [TestMethod]
    public void WorldEntryComplete_AndGhosting_NeverReports()
    {
        var connection = CreateConnection();
        connection.MarkWorldEntryCompletedForTests(_now);
        connection.ForceGhostingForTests(true);

        Assert.IsFalse(connection.ReportGhostingNeverStarted(_now + 600_000));
    }

    [TestMethod]
    public void WorldEntryComplete_NotGhosting_BelowThreshold_DoesNotReport()
    {
        var connection = CreateConnection();
        connection.MarkWorldEntryCompletedForTests(_now);

        Assert.IsFalse(connection.ReportGhostingNeverStarted(_now + TNLConnection.GhostingStartWarnMs - 1));
    }

    [TestMethod]
    public void WorldEntryComplete_NotGhosting_PastThreshold_ReportsWithSequenceAndScoping()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);

        var connection = CreateConnection();
        connection.ActivateGhostingForTests();
        connection.MarkWorldEntryCompletedForTests(_now);

        Assert.IsTrue(connection.ReportGhostingNeverStarted(_now + TNLConnection.GhostingStartWarnMs));

        var record = sink.Single("GhostingNeverStartedAfterWorldEntry");
        Assert.AreEqual(AutoCore.Utils.Logging.StructuredLogLevel.Warning, record.Level);
        Assert.AreEqual(true, Property(record, "Scoping"));
        Assert.AreEqual(false, Property(record, "Ghosting"));
        Assert.AreEqual((long)TNLConnection.GhostingStartWarnMs, Property(record, "StalledForMs"));
        Assert.IsNotNull(Property(record, "GhostingSequence"),
            "the sequence is what the client's ready RPC has to match — report it");
    }

    [TestMethod]
    public void GhostingStall_IsReportedOncePerBand()
    {
        var connection = CreateConnection();
        connection.MarkWorldEntryCompletedForTests(_now);

        Assert.IsTrue(connection.ReportGhostingNeverStarted(_now + TNLConnection.GhostingStartWarnMs));
        Assert.IsFalse(connection.ReportGhostingNeverStarted(_now + TNLConnection.GhostingStartWarnMs + 1),
            "the sector tick calls this every frame");
        Assert.IsTrue(connection.ReportGhostingNeverStarted(_now + (2 * TNLConnection.GhostingStartWarnMs)));
    }

    /// <summary>
    /// A later entry re-arms the watch: a connection that recovered and then broke again on the
    /// next transfer must report that one too.
    /// </summary>
    [TestMethod]
    public void NewWorldEntry_ReArmsTheWatch()
    {
        var connection = CreateConnection();
        connection.MarkWorldEntryCompletedForTests(_now);
        Assert.IsTrue(connection.ReportGhostingNeverStarted(_now + TNLConnection.GhostingStartWarnMs));

        _now += 100_000;
        connection.MarkWorldEntryCompletedForTests(_now);

        Assert.IsFalse(connection.ReportGhostingNeverStarted(_now + 1));
        Assert.IsTrue(connection.ReportGhostingNeverStarted(_now + TNLConnection.GhostingStartWarnMs));
    }

    private static object Property(AutoCore.Utils.Logging.StructuredLogRecord record, string key)
    {
        foreach (var pair in record.Properties)
        {
            if (pair.Key == key)
                return pair.Value;
        }

        Assert.Fail($"property '{key}' missing; present: [{string.Join(", ", record.Properties.Select(p => p.Key))}]");
        return null;
    }

    private static TNLConnection CreateConnection()
    {
        var continent = new ContinentObject
        {
            Id = DestContinentId,
            MapFileName = "tm_ghoststall_693",
            DisplayName = "ghost-stall",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));

        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);

        ObjectManager.Instance.Add(character);
        return connection;
    }
}

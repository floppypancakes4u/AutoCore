using System.Net;
using System.Reflection;
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
/// Diagnostics for the "client sits at a full loading bar forever" report. The map-transfer
/// handshake (MapInfo → Stage2 → Stage3 → ack → Creates) has no timeout: a stage packet that is
/// never sent, never received, or rejected as stale leaves the connection parked in
/// <see cref="SectorTransferPhase.WaitingForStage2"/> / <see cref="SectorTransferPhase.WaitingForStage3Ack"/>
/// with nothing written to the log after "waiting for Stage2.".
/// <para>
/// These cover the log-only watchdog: it must name the stalled phase and destination map so a
/// repro identifies which side of the handshake stopped. It deliberately does NOT repair the
/// handshake — root cause first.
/// </para>
/// </summary>
[TestClass]
public class MapTransferStallDiagnosticsTests
{
    private const long CharCoid = 9_081_000_201L;
    private const long VehicleCoid = 9_081_000_202L;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;

    private readonly List<BasePacket> _sent = new();
    private Func<int, SectorMap> _previousResolver;
    private bool _previousSuppress;
    private long _now;

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        _now = 1_000_000L;
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        TNLConnection.MapTransferHandshakeClock = () => _now;
        _previousResolver = MapManager.Instance.ResolveMapForTests;
        _previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        TNLConnection.MissionFlushForTests = () => { };
        TNLConnection.WorldStatePersistenceForTests = new NoopWorldStatePersistence();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.MapTransferHandshakeClock = static () => Environment.TickCount64;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        MapManager.Instance.ResolveMapForTests = _previousResolver;
        MapManager.Instance.SuppressCreatePacketsForTests = _previousSuppress;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        AutoCore.Utils.Logging.GameLog.ResetForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void NoHandshakePending_NeverReportsStall()
    {
        var (_, connection) = CreateTransferableOnSourceMap();

        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        Assert.IsFalse(connection.ReportMapTransferHandshakeStall(_now + 600_000));
    }

    [TestMethod]
    public void PendingStage2_BelowThreshold_DoesNotReportStall()
    {
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        _now += TNLConnection.MapTransferStallWarnMs - 1;

        Assert.IsFalse(connection.ReportMapTransferHandshakeStall(_now));
    }

    [TestMethod]
    public void PendingStage2_PastThreshold_ReportsStalledPhaseAndMap()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);

        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        _now += TNLConnection.MapTransferStallWarnMs;

        Assert.IsTrue(connection.ReportMapTransferHandshakeStall(_now));

        var record = sink.Single("MapTransferHandshakeStalled");
        Assert.AreEqual("WaitingForStage2", Property(record, "TransferPhase"));
        Assert.AreEqual(DestContinentId, Property(record, "ToMapId"));
        Assert.AreEqual(SourceContinentId, Property(record, "FromMapId"));
        Assert.AreEqual(CharCoid, Property(record, "CharacterId"));
        Assert.AreEqual((long)TNLConnection.MapTransferStallWarnMs, Property(record, "StalledForMs"));
    }

    [TestMethod]
    public void Stall_IsNotRepeatedWithinTheSameBand()
    {
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        _now += TNLConnection.MapTransferStallWarnMs;
        Assert.IsTrue(connection.ReportMapTransferHandshakeStall(_now));

        _now += 1;
        Assert.IsFalse(connection.ReportMapTransferHandshakeStall(_now),
            "watchdog runs every tick; one line per band or it floods the log");
    }

    [TestMethod]
    public void Stall_RepeatsOnceEachSubsequentBand()
    {
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        _now += TNLConnection.MapTransferStallWarnMs;
        Assert.IsTrue(connection.ReportMapTransferHandshakeStall(_now));

        _now += TNLConnection.MapTransferStallWarnMs;
        Assert.IsTrue(connection.ReportMapTransferHandshakeStall(_now),
            "a still-stalled handshake must keep reporting so the operator sees it is not progressing");
    }

    /// <summary>
    /// The two halves of the handshake fail for different reasons — no Stage2 means the client
    /// never finished the destination load (or the server never saw it), while no Stage3 ack means
    /// the client took our Stage3 pose and never finished the terrain preload. Timing the phase
    /// rather than the whole transfer is what distinguishes them.
    /// </summary>
    [TestMethod]
    public void Stage2Advance_RestartsTheStallClockForStage3Ack()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);

        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        _now += TNLConnection.MapTransferStallWarnMs - 1;
        InvokeStage2(connection, CharCoid);
        Assert.AreEqual(SectorTransferPhase.WaitingForStage3Ack, connection.TransferPhase);

        _now += 1;
        Assert.IsFalse(connection.ReportMapTransferHandshakeStall(_now),
            "Stage2 made progress; the Stage3-ack wait starts its own clock");

        _now += TNLConnection.MapTransferStallWarnMs;
        Assert.IsTrue(connection.ReportMapTransferHandshakeStall(_now));
        Assert.AreEqual("WaitingForStage3Ack",
            Property(sink.Single("MapTransferHandshakeStalled"), "TransferPhase"));
    }

    [TestMethod]
    public void CompletedHandshake_NeverReportsStall()
    {
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));
        InvokeStage2(connection, CharCoid);
        InvokeStage3Ack(connection, CharCoid);

        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        Assert.IsFalse(connection.ReportMapTransferHandshakeStall(_now + 600_000));
    }

    [TestMethod]
    public void AbortedHandshake_NeverReportsStall()
    {
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        connection.AbortPendingMapTransferHandshake("test");

        Assert.IsFalse(connection.ReportMapTransferHandshakeStall(_now + 600_000));
    }

    /// <summary>
    /// A stale stage packet is the one failure mode that is invisible today: the handler returns
    /// without advancing, so the client waits forever while the console shows nothing after
    /// "waiting for Stage2.". The rejection must be an operator-visible event.
    /// </summary>
    [TestMethod]
    public void StaleStagePacket_EmitsWarningWithReason()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);

        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, CreateMap(DestContinentId));

        // Stage3 ack arriving while we still await Stage2 — the client answered a stage we never
        // sent, so the handshake can never complete.
        InvokeStage3Ack(connection, CharCoid);

        var record = sink.Single("MapTransferStaleStagePacket");
        Assert.AreEqual(AutoCore.Utils.Logging.StructuredLogLevel.Warning, record.Level);
        Assert.AreEqual("Stage3BeforeStage2", Property(record, "Reason"));
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
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

    private void TransferOnto(Character character, SectorMap dest)
    {
        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, dest.ContinentId));
    }

    private static void InvokeStage2(TNLConnection connection, long characterCoid)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0u);
            writer.Write(characterCoid);
        }

        InvokeHandler(connection, "HandleTransferFromGlobalStage2Packet", stream.ToArray());
    }

    private static void InvokeStage3Ack(TNLConnection connection, long characterCoid)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0u);
            writer.Write(characterCoid);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0);
        }

        InvokeHandler(connection, "HandleTransferFromGlobalStage3Packet", stream.ToArray());
    }

    private static void InvokeHandler(TNLConnection connection, string methodName, byte[] payload)
    {
        var method = typeof(TNLConnection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Missing handler {methodName}");

        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_xfer_stall_{continentId}",
            DisplayName = "xfer-stall",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(10f, 20f, 30f, 0f));
    }

    private static (Character Character, TNLConnection Connection) CreateTransferableOnSourceMap()
    {
        var source = CreateMap(SourceContinentId);

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
        character.SetMap(source);
        vehicle.SetMap(source);

        ObjectManager.Instance.Add(character);
        return (character, connection);
    }

    private sealed class NoopWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot)
        {
        }
    }
}

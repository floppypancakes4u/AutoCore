using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using System.Net;
using System.Reflection;
using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.Tests.Fakes;
using AutoCore.Utils;
using AutoCore.Utils.Logging;
using global::TNL.Entities;
using global::TNL.Utils;

/// <summary>
/// Phase 2 session identity + lifecycle traceability: server-generated SessionId, connection
/// accept/close events, per-packet ambient LogContext scope, session end, and the
/// world-state save operation scope.
/// </summary>
[TestClass]
public class SessionLifecycleLoggingTests
{
    private const long CharCoid = 9_050_000_201L;
    private const long VehicleCoid = 9_050_000_202L;

    private InMemoryLogSink _sink;

    [TestInitialize]
    public void Init()
    {
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        TNLConnection.TestPacketSink = (_, _) => { };
        TNLConnection.MissionFlushForTests = () => { };
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        TNLConnection.TestPacketSink = null;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
    }

    private static TNLConnection CreateClient()
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(false);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        return connection;
    }

    private static void InvokeHandlePacket(TNLConnection connection, byte[] payload)
    {
        var method = typeof(TNLConnection).GetMethod(
            "HandlePacket", BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(connection, new object[] { new ByteBuffer(payload, (uint)payload.Length) });
    }

    private static byte[] OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode opcode)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)opcode);
        writer.Flush();
        return stream.ToArray();
    }

    private StructuredLogRecord SingleLegacyContaining(string fragment)
    {
        var matches = _sink.Records
            .Where(r => r.EventName == "Legacy" && (r.Message?.Contains(fragment) ?? false))
            .ToArray();
        Assert.AreEqual(1, matches.Length,
            $"Exactly one mirrored legacy line containing '{fragment}' is expected " +
            $"(got {matches.Length}); the dispatch path must run exactly once.");
        return matches[0];
    }

    // ---- Session identity ----

    [TestMethod]
    public void SessionId_IsServerGenerated_Stable_AndUniquePerConnection()
    {
        var a = CreateClient();
        var b = CreateClient();

        Assert.AreEqual(16, a.SessionId.Length,
            "SessionId must be the 16-char GUID prefix so it is compact but unguessable.");
        Assert.IsTrue(a.SessionId.All(Uri.IsHexDigit),
            "SessionId must be hex (GUID 'N' format) — never derived from client input.");
        Assert.AreEqual(a.SessionId, a.SessionId,
            "SessionId must be stable for the lifetime of the connection.");
        Assert.AreNotEqual(a.SessionId, b.SessionId,
            "Each connection must get its own server-generated identity.");
        Assert.IsTrue(a.SessionStartedUtc <= DateTime.UtcNow,
            "SessionStartedUtc must capture connection construction time.");
    }

    // ---- Interface accept/close ----

    [TestMethod]
    public void AddConnection_EmitsConnectionAccepted_WithSessionAndConnectionId()
    {
        var iface = new TNLInterface(doGhosting: false, skipNetworkBind: true);
        var conn = CreateClient();

        iface.AddConnection(conn);

        var record = _sink.Single("ConnectionAccepted");
        Assert.AreEqual(conn.SessionId, record.GetProperty("SessionId"),
            "ConnectionAccepted must carry the server-generated SessionId, not the client-writable PlayerCoid.");
        Assert.AreEqual(conn.GetPlayerCOID(), record.GetProperty("ConnectionId"),
            "ConnectionAccepted must carry the interface-assigned connection id.");
    }

    [TestMethod]
    public void RemoveConnection_EmitsConnectionClosed()
    {
        var iface = new TNLInterface(doGhosting: false, skipNetworkBind: true);
        var conn = CreateClient();
        iface.AddConnection(conn);
        _sink.Clear();

        var remove = typeof(TNLInterface).GetMethod(
            "RemoveConnection", BindingFlags.Instance | BindingFlags.NonPublic);
        remove!.Invoke(iface, new object[] { (NetConnection)conn });

        var record = _sink.Single("ConnectionClosed");
        Assert.AreEqual(conn.SessionId, record.GetProperty("SessionId"),
            "ConnectionClosed must correlate with the accept event via SessionId.");
        Assert.AreEqual(conn.GetPlayerCOID(), record.GetProperty("ConnectionId"),
            "ConnectionClosed must carry the connection id for interface-level bookkeeping.");
    }

    // ---- Per-packet ambient scope ----

    [TestMethod]
    public void HandlePacket_DispatchLogs_CarrySessionScope()
    {
        var conn = CreateClient();
        conn.SetPlayerCOID(4242);

        // ObjectMoved is a defined opcode with no server handler → the dispatch default case
        // logs "Unhandled Opcode" at Error, which the dual-write layer mirrors with ambient context.
        InvokeHandlePacket(conn, OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode.ObjectMoved));

        var record = SingleLegacyContaining("Unhandled Opcode");
        Assert.AreEqual(conn.SessionId, record.GetProperty("SessionId"),
            "Every log line inside the dispatch switch must be attributable to the session.");
        Assert.AreEqual(4242L, record.GetProperty("ConnectionId"),
            "The ambient scope must carry the connection id.");
        Assert.AreEqual("ObjectMoved", record.GetProperty("Opcode"),
            "The ambient scope must carry the parsed opcode name.");
        var correlation = record.GetProperty("CorrelationId") as string;
        Assert.IsNotNull(correlation, "Each packet dispatch must carry a correlation id.");
        StringAssert.StartsWith(correlation, conn.SessionId + "-",
            "CorrelationId must be derived from the session so packets group per session.");
    }

    [TestMethod]
    public void HandlePacket_CorrelationId_IsUniquePerPacket()
    {
        var conn = CreateClient();

        InvokeHandlePacket(conn, OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode.ObjectMoved));
        InvokeHandlePacket(conn, OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode.ObjectMoved));

        var correlations = _sink.Records
            .Where(r => r.EventName == "Legacy" && (r.Message?.Contains("Unhandled Opcode") ?? false))
            .Select(r => r.GetProperty("CorrelationId") as string)
            .ToArray();

        Assert.AreEqual(2, correlations.Length, "Both dispatches must be mirrored.");
        Assert.AreNotEqual(correlations[0], correlations[1],
            "The per-connection counter must advance per packet so each dispatch is distinguishable.");
    }

    [TestMethod]
    public void HandlePacket_WithCharacter_CarriesCharacterIdentity()
    {
        var conn = CreateClient();
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests("ScopePilot");
        conn.CurrentCharacter = character;
        conn.Account = new Account { Id = 77, Name = "acct77" };

        InvokeHandlePacket(conn, OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode.ObjectMoved));

        var record = SingleLegacyContaining("Unhandled Opcode");
        Assert.AreEqual(CharCoid, record.GetProperty("CharacterId"),
            "Once a character is bound, dispatch logs must be attributable to it.");
        Assert.AreEqual("ScopePilot", record.GetProperty("CharacterName"),
            "CharacterName makes ops queries human-readable without a coid join.");
        Assert.AreEqual(77u, record.GetProperty("AccountId"),
            "AccountId links the packet to the authenticated account.");
    }

    [TestMethod]
    public void HandlePacket_WithCharacterOnMap_CarriesMapIdentity()
    {
        var conn = CreateClient();
        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests("MapPilot");

        var continent = new ContinentObject
        {
            Id = 698,
            DisplayName = "Tierra Roja Dam",
            MapFileName = "trd",
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        character.SetMap(map);

        conn.CurrentCharacter = character;

        InvokeHandlePacket(conn, OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode.ObjectMoved));

        var record = SingleLegacyContaining("Unhandled Opcode");
        Assert.AreEqual(698, record.GetProperty("MapId"),
            "MapId must be ambient so Debug lines can prefix map identity.");
        Assert.AreEqual("Tierra Roja Dam", record.GetProperty("MapName"),
            "MapName (DisplayName) must be ambient for human-readable Debug prefixes.");
    }

    [TestMethod]
    public void HandlePacket_AmbientScope_IsRestoredAfterDispatch()
    {
        var conn = CreateClient();

        InvokeHandlePacket(conn, OpcodeOnlyPacket(AutoCore.Game.Constants.GameOpcode.ObjectMoved));

        Assert.AreEqual(0, LogContext.CurrentProperties.Count,
            "The per-packet scope must not leak into subsequent work on the same async context.");
    }

    // ---- Session end ----

    [TestMethod]
    public void OnConnectionTerminated_EmitsSessionEnded_WithReasonAndDuration()
    {
        var conn = CreateClient();
        conn.SetPlayerCOID(17);

        conn.OnConnectionTerminated(TerminationReason.ReasonTimedOut, "idle too long");

        var record = _sink.Single("SessionEnded");
        Assert.AreEqual(conn.SessionId, record.GetProperty("SessionId"),
            "SessionEnded must close the loop opened by ConnectionAccepted.");
        Assert.AreEqual(17L, record.GetProperty("ConnectionId"),
            "SessionEnded must carry the connection id.");
        Assert.AreEqual("ReasonTimedOut", record.GetProperty("Reason"),
            "The TNL termination reason distinguishes timeouts from disconnects and errors.");
        Assert.AreEqual("idle too long", record.GetProperty("Detail"),
            "The free-text detail from TNL must be preserved.");
        var duration = record.GetProperty("SessionDurationMs");
        Assert.IsInstanceOfType(duration, typeof(long),
            "SessionDurationMs must be a queryable numeric duration.");
        Assert.IsTrue((long)duration >= 0L,
            "Duration is measured from SessionStartedUtc and can never be negative.");
    }

    [TestMethod]
    public void OnConnectionTerminated_WithCharacter_IncludesCharacterAndAccount()
    {
        var map = CreateMap(910);
        var character = CreateCharacterWithVehicle(map, out var conn);
        conn.Account = new Account { Id = 55, Name = "acct55" };
        TNLConnection.WorldStatePersistenceForTests = new RecordingPersistence();

        conn.OnConnectionTerminated(TerminationReason.ReasonSelfDisconnect, "bye");

        var record = _sink.Single("SessionEnded");
        Assert.AreEqual(CharCoid, record.GetProperty("CharacterId"),
            "SessionEnded must record which character the session was for, even though " +
            "EndCharacterSession clears CurrentCharacter before the event is emitted.");
        Assert.AreEqual(55u, record.GetProperty("AccountId"),
            "SessionEnded must record the account for audit trails.");
    }

    // ---- World-state save operation ----

    [TestMethod]
    public void EndCharacterSession_EmitsWorldStateSaveOperation_CompletedOnSuccess()
    {
        var map = CreateMap(911);
        var character = CreateCharacterWithVehicle(map, out var conn);
        TNLConnection.WorldStatePersistenceForTests = new RecordingPersistence();

        conn.EndCharacterSession();

        var started = _sink.Single("CharacterWorldStateSaveStarted");
        Assert.AreEqual(CharCoid, started.GetProperty("CharacterId"),
            "The save operation must name the character being persisted.");
        var completed = _sink.Single("CharacterWorldStateSaveCompleted");
        Assert.IsNotNull(completed.GetProperty("DurationMs"),
            "Completed operations carry DurationMs so slow logout saves are visible.");
        Assert.AreEqual(0, _sink.Records.Count(r => r.EventName == "CharacterWorldStateSaveFailed"),
            "A successful save must not also emit Failed.");
    }

    [TestMethod]
    public void EndCharacterSession_PersistenceThrows_EmitsWorldStateSaveFailed()
    {
        var map = CreateMap(912);
        var character = CreateCharacterWithVehicle(map, out var conn);
        TNLConnection.WorldStatePersistenceForTests = new ThrowingPersistence();

        conn.EndCharacterSession();

        var failed = _sink.Single("CharacterWorldStateSaveFailed");
        Assert.AreEqual(CharCoid, failed.GetProperty("CharacterId"),
            "The failed save must still be attributable to the character whose state was lost.");
        Assert.AreEqual("InvalidOperationException", failed.GetProperty("ExceptionType"),
            "The failure record must carry the exception type for triage.");
        Assert.IsNull(conn.CurrentCharacter,
            "Existing SS-03/SS-04 teardown semantics must be untouched: teardown still completes.");
    }

    // ---- helpers ----

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = "test_map",
            DisplayName = "Test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
    }

    private static Character CreateCharacterWithVehicle(SectorMap map, out TNLConnection connection)
    {
        connection = CreateClient();

        var character = new Character();
        character.SetCoid(CharCoid, true);
        character.AttachTestDataForTests();
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.AttachTestDataForTests();
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);

        connection.CurrentCharacter = character;
        return character;
    }

    private sealed class RecordingPersistence : ICharacterWorldStatePersistence
    {
        public List<CharacterWorldStateSnapshot> Saves { get; } = new();
        public void Save(CharacterWorldStateSnapshot snapshot) => Saves.Add(snapshot);
    }

    private sealed class ThrowingPersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot)
            => throw new InvalidOperationException("simulated persistence failure");
    }
}

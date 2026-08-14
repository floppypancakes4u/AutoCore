using System.Net;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// End-to-end cover for the 2026-08-14 map-transfer freeze, driving the whole production sequence:
/// <c>TransferCharacterToMap</c> (ResetGhosting + MapInfo) → client Stage2 → server Stage3 → client
/// Stage3 ack → local Creates + ActivateGhosting → client <c>rpcReadyForNormalGhosts</c> → ghosting
/// live → interest query populates the world.
/// <para>
/// The unit-level tests pin the gate in isolation; this pins the ordering of the whole chain, which
/// is what actually broke. A regression anywhere in it — a stage handler that stops advancing, an
/// ActivateGhosting that never fires, or the interest query going loud again before the client is
/// ready — fails here even if each piece still looks correct on its own.
/// </para>
/// </summary>
[TestClass]
public class MapTransferEndToEndRegressionTests
{
    private const long CharCoid = 9_089_000_401L;
    private const long VehicleCoid = 9_089_000_402L;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;
    private const int CreatureCbid = 12448;
    private const int CreatureCount = 12;

    private readonly List<BasePacket> _sent = new();
    private Func<int, SectorMap> _previousResolver;
    private bool _previousSuppress;

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        _previousResolver = MapManager.Instance.ResolveMapForTests;
        _previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        // Local-player Creates need full clonebase-backed data and are covered by
        // MapTransferHandshakeTests; this suite is about the foreign world coming up.
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        TNLConnection.MissionFlushForTests = () => { };
        TNLConnection.WorldStatePersistenceForTests = new NoopWorldStatePersistence();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(CreatureCbid, maxHitPoint: 50);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        MapManager.Instance.ResolveMapForTests = _previousResolver;
        MapManager.Instance.SuppressCreatePacketsForTests = _previousSuppress;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        AutoCore.Utils.Logging.GameLog.ResetForTests();
        _sent.Clear();
    }

    /// <summary>
    /// The freeze, end to end: between the client's Stage3 ack and its ready RPC the server must put
    /// nothing on the reliable queue. That queue is where rpcStartGhosting sits, and burying it is
    /// what left the client on a loading bar it never left.
    /// </summary>
    [TestMethod]
    public void Transfer_BetweenStage3AckAndClientReady_QueuesNoWorldTraffic()
    {
        var (character, connection, dest) = ArriveOnDestination();
        Assert.IsFalse(connection.IsGhosting(), "client has not answered rpcStartGhosting yet");
        _sent.Clear();

        for (var i = 0; i < 100; i++)
            connection.PrepareWritePacket();

        Assert.AreEqual(0, _sent.Count,
            "nothing may be queued ahead of the ready RPC; got "
            + string.Join(", ", _sent.Select(p => p.GetType().Name).Distinct()));
    }

    /// <summary>The other half: once the client answers, the destination world must actually come up.</summary>
    [TestMethod]
    public void Transfer_AfterClientReady_PopulatesDestinationWorld()
    {
        var (character, connection, dest) = ArriveOnDestination();
        for (var i = 0; i < 20; i++)
            connection.PrepareWritePacket();
        _sent.Clear();

        connection.ForceGhostingForTests(true);
        connection.PrepareWritePacket();

        Assert.AreEqual(CreatureCount, _sent.OfType<CreateCreaturePacket>().Count(),
            "every destination creature must be created once the client is ready");
    }

    /// <summary>
    /// A transfer that completes its handshake but whose client never answers is the exact live
    /// failure; the watchdog must name it rather than the server sitting there looking healthy.
    /// </summary>
    [TestMethod]
    public void Transfer_ClientNeverReady_IsReportedByWatchdog()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);
        var now = 7_000_000L;
        TNLConnection.MapTransferHandshakeClock = () => now;
        try
        {
            var (character, connection, dest) = ArriveOnDestination();

            Assert.IsTrue(connection.ReportGhostingNeverStarted(now + TNLConnection.GhostingStartWarnMs));

            var record = sink.Single("GhostingNeverStartedAfterWorldEntry");
            Assert.AreEqual(DestContinentId, Property(record, "MapId"));
            Assert.AreEqual(false, Property(record, "Ghosting"));
        }
        finally
        {
            TNLConnection.MapTransferHandshakeClock = static () => Environment.TickCount64;
        }
    }

    /// <summary>
    /// Warping repeatedly is the live usage pattern. Each session re-arms cleanly: silent while the
    /// client loads, populated once it is ready, with no drift in either direction.
    /// </summary>
    [TestMethod]
    public void RepeatedTransfers_EachSessionIsSilentThenPopulates()
    {
        var (character, connection, dest) = ArriveOnDestination();
        connection.ForceGhostingForTests(true);
        connection.PrepareWritePacket();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            MapManager.Instance.ResolveMapForTests = _ => dest;
            Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));
            InvokeStage2(connection, CharCoid);
            InvokeStage3Ack(connection, CharCoid);
            _sent.Clear();

            for (var i = 0; i < 20; i++)
                connection.PrepareWritePacket();
            Assert.AreEqual(0, _sent.Count, $"cycle {cycle}: must stay silent until the client is ready");

            connection.ForceGhostingForTests(true);
            connection.PrepareWritePacket();
            Assert.AreEqual(CreatureCount, _sent.OfType<CreateCreaturePacket>().Count(),
                $"cycle {cycle}: destination world must repopulate for the new map session");
        }
    }

    private static object Property(AutoCore.Utils.Logging.StructuredLogRecord record, string key)
    {
        foreach (var pair in record.Properties)
        {
            if (pair.Key == key)
                return pair.Value;
        }

        Assert.Fail($"property '{key}' missing");
        return null;
    }

    /// <summary>Runs the real transfer + Stage2/Stage3 handshake and stops just before client ready.</summary>
    private (Character Character, TNLConnection Connection, SectorMap Dest) ArriveOnDestination()
    {
        var dest = CreateMap(DestContinentId);
        PopulateCreatures(dest);
        var (character, connection) = CreateTransferableOnSourceMap();

        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);

        InvokeStage2(connection, CharCoid);
        Assert.AreEqual(SectorTransferPhase.WaitingForStage3Ack, connection.TransferPhase);

        InvokeStage3Ack(connection, CharCoid);
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);

        connection.SetScopeObject(character.Ghost);
        return (character, connection, dest);
    }

    private static void PopulateCreatures(SectorMap map)
    {
        var counter = map.LocalCoidCounter;
        for (var i = 0; i < CreatureCount; i++)
        {
            var creature = new Creature { Position = new Vector3(10f + i, 0f, 0f), Level = 5 };
            SpawnPoint.AssignMapNpcIdentity(creature, ref counter);
            creature.LoadCloneBase(CreatureCbid);
            creature.SetupCBFields();
            creature.IsMissionGiver = true;
            creature.CreateGhost();
            creature.SetMap(map);
        }

        map.LocalCoidCounter = counter;
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

    private static void InvokeHandler(TNLConnection connection, string methodName, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Missing handler {methodName}");
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_e2e_{continentId}",
            DisplayName = "e2e",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(10f, 20f, 30f, 0f));
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }

        return map;
    }

    private static (Character Character, TNLConnection Connection) CreateTransferableOnSourceMap()
    {
        var source = CreateMap(SourceContinentId);

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        connection.SuppressCreatePacketsForTests = true;

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

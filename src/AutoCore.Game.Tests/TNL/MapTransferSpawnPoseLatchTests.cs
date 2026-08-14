using System.Net;
using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// Continent transfer must deliver the player at the pose <see cref="MapTransferSpawn"/>
/// resolved, not at whatever pose the entities happen to hold when the client finally
/// finishes loading the destination FAM.
///
/// Since the Stage2/Stage3 gate landed, the destination pose is read from live entity state
/// at two client-triggered moments — <c>SendTransferStage3</c> (on Stage2) and
/// <c>SendLocalPlayerCreatePackets</c> (on the Stage3 ack) — seconds after the transfer began.
/// The client keeps sending VehicleMoved/CreatureMoved with OLD-map coordinates until it
/// processes MapInfo (client <c>Process_EMSG_Sector_MapInfo</c> @0x008153B0 is what runs
/// DestroyCharacterArray/CleanupCOList/ReInitPhysics), so those in-flight packets land on the
/// server after the spawn pose was written and overwrite it.
///
/// That matters twice over: client <c>Process_EMSG_Sector_TransferFromGlobal_Stage3</c>
/// @0x00809AD0 pushes the Stage3 position into the culling system and calls
/// <c>CVOGCullingSystem::JumpstartPreloader</c>, so a stale Stage3 position also preloads the
/// wrong terrain region — the player arrives with no ground under them.
/// </summary>
[TestClass]
public class MapTransferSpawnPoseLatchTests
{
    private const long CharCoid = 9_081_000_101L;
    private const long VehicleCoid = 9_081_000_102L;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;

    // SectorMap.CreateForTests seeds this as the destination map header EntryPoint, which is
    // what MapTransferSpawn falls back to when no origin-keyed EnterPoint exists.
    private static readonly Vector4 DestEntryPoint = new(1500f, 87.5f, 2400f, 0f);

    // A plausible pose on the SOURCE map — what the client's last in-flight VehicleMoved carries.
    private static readonly Vector3 StaleSourcePose = new(8123.5f, 61.25f, 411.75f);

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
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        TNLConnection.MissionFlushForTests = () => { };
        TNLConnection.WorldStatePersistenceForTests = new NoopWorldStatePersistence();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        MapManager.Instance.ResolveMapForTests = _previousResolver;
        MapManager.Instance.SuppressCreatePacketsForTests = _previousSuppress;
        ObjectManager.Instance.Remove(CharCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        _sent.Clear();
    }

    /// <summary>
    /// The client has no local player between MapInfo and the Stage3 ack, so any move packet
    /// arriving in that window is stale old-map data and must not move the server entities.
    /// </summary>
    [TestMethod]
    public void StaleVehicleMovedDuringHandshake_DoesNotDisturbSpawnPose()
    {
        var (character, connection) = ArrangeTransferInFlight();

        DeliverVehicleMoved(connection, VehicleCoid, StaleSourcePose);

        AssertAtSpawnPose(character, "stale in-flight VehicleMoved must be dropped mid-transfer");
        Assert.AreEqual(1, connection.StaleMoveDropsDuringMapTransferForTests,
            "the drop must be counted, not silently swallowed");
    }

    /// <summary>Same window, on-foot flavour (CreatureMoved).</summary>
    [TestMethod]
    public void StaleCreatureMovedDuringHandshake_DoesNotDisturbSpawnPose()
    {
        var (character, connection) = ArrangeTransferInFlight();

        DeliverCreatureMoved(connection, CharCoid, StaleSourcePose);

        AssertAtSpawnPose(character, "stale in-flight CreatureMoved must be dropped mid-transfer");
    }

    /// <summary>
    /// Stage3 drives the client's terrain preload, so it must carry the resolved spawn pose
    /// even if the pose was disturbed after MapInfo went out.
    /// </summary>
    [TestMethod]
    public void Stage3_CarriesResolvedSpawnPose_NotLivePose()
    {
        var (character, connection) = ArrangeTransferInFlight();

        // Belt and braces: force the live pose off the spawn point the way any unguarded
        // writer would, then prove Stage3 still preloads around the authored arrival point.
        character.Position = StaleSourcePose;
        character.CurrentVehicle.SetPosition(StaleSourcePose);

        InvokeStage2(connection, CharCoid);

        var stage3 = _sent.OfType<TransferFromGlobalStage3Packet>().Single();
        Assert.AreEqual(DestEntryPoint.X, stage3.PositionX, 0.001f, "Stage3 X must be the spawn pose");
        Assert.AreEqual(DestEntryPoint.Y, stage3.PositionY, 0.001f, "Stage3 Y must be the spawn pose");
        Assert.AreEqual(DestEntryPoint.Z, stage3.PositionZ, 0.001f, "Stage3 Z must be the spawn pose");
    }

    /// <summary>
    /// The Creates released on the Stage3 ack are what actually place the local player, so the
    /// entities must be back on the resolved spawn pose before they are written.
    /// </summary>
    [TestMethod]
    public void Stage3Ack_PlacesEntitiesAtSpawnPose_BeforeCreates()
    {
        var (character, connection) = ArrangeTransferInFlight();
        character.Position = StaleSourcePose;
        character.CurrentVehicle.SetPosition(StaleSourcePose);

        InvokeStage2(connection, CharCoid);
        InvokeStage3Ack(connection, CharCoid);

        AssertAtSpawnPose(character, "Creates must go out at the resolved spawn pose");
    }

    /// <summary>Once the handshake is done the player drives again — movement must resume.</summary>
    [TestMethod]
    public void VehicleMovedAfterHandshakeCompletes_IsAppliedNormally()
    {
        var (character, connection) = ArrangeTransferInFlight();
        InvokeStage2(connection, CharCoid);
        InvokeStage3Ack(connection, CharCoid);
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);

        var driven = new Vector3(1510f, 88f, 2415f);
        DeliverVehicleMoved(connection, VehicleCoid, driven);

        Assert.AreEqual(driven.X, character.CurrentVehicle.Position.X, 0.001f);
        Assert.AreEqual(driven.Z, character.CurrentVehicle.Position.Z, 0.001f);
        Assert.AreEqual(0, connection.StaleMoveDropsDuringMapTransferForTests,
            "nothing may be dropped once the handshake is done");
    }

    /// <summary>
    /// Login has no pending map-transfer handshake and no resolved spawn pose; its Stage3 must
    /// still report the character's own (DB-restored) position.
    /// </summary>
    [TestMethod]
    public void LoginStage3_StillUsesCharacterPosition()
    {
        var (character, connection) = CreateTransferableOnSourceMap();
        var restored = new Vector3(4242f, 70f, 909f);
        character.Position = restored;
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        _sent.Clear();

        InvokeStage2(connection, CharCoid);

        var stage3 = _sent.OfType<TransferFromGlobalStage3Packet>().Single();
        Assert.AreEqual(restored.X, stage3.PositionX, 0.001f);
        Assert.AreEqual(restored.Y, stage3.PositionY, 0.001f);
        Assert.AreEqual(restored.Z, stage3.PositionZ, 0.001f);
    }

    // ─── helpers ───────────────────────────────────────────────────────────

    private (Character Character, TNLConnection Connection) ArrangeTransferInFlight()
    {
        var dest = CreateMap(DestContinentId, DestEntryPoint);
        var (character, connection) = CreateTransferableOnSourceMap();
        MapManager.Instance.ResolveMapForTests = _ => dest;

        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        _sent.Clear();
        return (character, connection);
    }

    private static void AssertAtSpawnPose(Character character, string because)
    {
        Assert.AreEqual(DestEntryPoint.X, character.CurrentVehicle.Position.X, 0.001f, $"vehicle X: {because}");
        Assert.AreEqual(DestEntryPoint.Y, character.CurrentVehicle.Position.Y, 0.001f, $"vehicle Y: {because}");
        Assert.AreEqual(DestEntryPoint.Z, character.CurrentVehicle.Position.Z, 0.001f, $"vehicle Z: {because}");
        Assert.AreEqual(DestEntryPoint.X, character.Position.X, 0.001f, $"character X: {because}");
        Assert.AreEqual(DestEntryPoint.Y, character.Position.Y, 0.001f, $"character Y: {because}");
        Assert.AreEqual(DestEntryPoint.Z, character.Position.Z, 0.001f, $"character Z: {because}");
    }

    private static void DeliverVehicleMoved(TNLConnection connection, long coid, Vector3 location)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WriteObjectMovedBody(writer, coid, location);
            writer.Write(0f);            // Acceleration
            writer.Write(0f);            // Steering
            writer.Write(0f);            // TurretDirection
            writer.Write((byte)0);       // VehicleFlags
            writer.Write((byte)0);       // Firing
            writer.Write((ushort)0);     // reserved
            writer.WriteTFID(-1L, false);
        }

        InvokeHandler(connection, "HandleVehicleMovedPacket", stream.ToArray());
    }

    private static void DeliverCreatureMoved(TNLConnection connection, long coid, Vector3 location)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WriteObjectMovedBody(writer, coid, location);
            writer.Write(0f);            // MoveState / speed tail
            writer.WriteTFID(-1L, false);
            writer.Write(new byte[32]);  // slack for any trailing fields
        }

        InvokeHandler(connection, "HandleCreatureMovedPacket", stream.ToArray());
    }

    private static void WriteObjectMovedBody(BinaryWriter writer, long coid, Vector3 location)
    {
        writer.Write(0);                       // leading padding
        writer.WriteTFID(coid, true);
        writer.Write(location.X);
        writer.Write(location.Y);
        writer.Write(location.Z);
        writer.Write(0f); writer.Write(0f); writer.Write(0f);           // Velocity
        writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f); // Rotation
        writer.Write(0f); writer.Write(0f); writer.Write(0f);           // AngularVelocity
        writer.Write(true);                                             // Absolute
        writer.Write(new byte[3]);
        writer.Write(0f); writer.Write(0f); writer.Write(0f);           // TargetPosition
        writer.Write(0);                                                // trailing padding
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

    private static SectorMap CreateMap(int continentId, Vector4 entryPoint)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_xfer_latch_{continentId}",
            DisplayName = "xfer-latch",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, entryPoint);
    }

    private static (Character Character, TNLConnection Connection) CreateTransferableOnSourceMap()
    {
        var source = CreateMap(SourceContinentId, new Vector4(10f, 20f, 30f, 0f));

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

using System.Net;
using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 3: ordinary same-connection map transfer must wait for the client's
/// MapInfo → FAM/static load → Stage2 → Stage3 → Stage3-ack handshake before
/// destination Creates or normal ghost activation.
/// </summary>
[TestClass]
public class MapTransferHandshakeTests
{
    private const long CharCoid = 9_080_000_101L;
    private const long VehicleCoid = 9_080_000_102L;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;

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
    /// Sending MapInfo during a map transfer begins an asynchronous client load;
    /// server-visible world-entry traffic must remain gated until the subsequent
    /// Stage2/Stage3 exchange completes. Client: RecvMapInfo 0x008153B0 →
    /// ReinitPhysics 0x009463B0 → loader 0x008BB9E0 → Stage2 0x009347B0.
    /// </summary>
    [TestMethod]
    public void Transfer_SendsMapInfo_AndDoesNotReleaseWorldEntryTraffic()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        MapManager.Instance.ResolveMapForTests = _ => dest;

        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));

        Assert.IsTrue(_sent.OfType<MapInfoPacket>().Any(), "destination MapInfo must go out");
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any(),
            "CreateVehicleExtended must wait for Stage3 ack");
        Assert.IsFalse(_sent.OfType<CreateCharacterExtendedPacket>().Any(),
            "CreateCharacterExtended must wait for Stage3 ack");
        Assert.IsFalse(_sent.OfType<InventoryCargoSendAllPacket>().Any(),
            "InventoryCargoSendAll must wait for Stage3 ack");
        Assert.IsNull(connection.GetScopeObject(),
            "ResetGhosting must remain in effect until Stage3 ack; destination ghosts cannot start");
        Assert.IsFalse(character.WorldEntryComplete,
            "mission replay must stay gated until Creates");
        Assert.IsFalse(connection.IsScopingForTests,
            "ActivateGhosting must wait for Stage3 ack");
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.AreEqual(character.ObjectId.Coid, connection.TransferHandshakeCharacterCoid);
        Assert.AreEqual(DestContinentId, connection.TransferHandshakeDestinationContinentId);
    }

    [TestMethod]
    public void Transfer_ArmsWaitingForStage2_AfterMapInfo()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.AreEqual(character.ObjectId.Coid, connection.TransferHandshakeCharacterCoid);
        Assert.AreEqual(DestContinentId, connection.TransferHandshakeDestinationContinentId);
        Assert.IsTrue(connection.TransferHandshakeGeneration > 0);
    }

    [TestMethod]
    public void Transfer_Stage2_SendsStage3_AndDoesNotCreate()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        _sent.Clear();

        InvokeStage2(connection, character.ObjectId.Coid);

        Assert.AreEqual(1, _sent.OfType<TransferFromGlobalStage3Packet>().Count());
        Assert.AreEqual(SectorTransferPhase.WaitingForStage3Ack, connection.TransferPhase);
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any());
        Assert.IsFalse(_sent.OfType<CreateCharacterExtendedPacket>().Any());
        Assert.IsFalse(connection.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
    }

    [TestMethod]
    public void Transfer_Stage3Ack_ReleasesCreatesThenActivatesGhosting()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        InvokeStage2(connection, character.ObjectId.Coid);
        Assert.IsFalse(connection.IsScopingForTests, "ghosting must still be held after Stage2");

        InvokeStage3Ack(connection, character.ObjectId.Coid);

        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        Assert.IsTrue(character.WorldEntryComplete,
            "Stage3 ack releases Creates / CompleteWorldEntry");
        Assert.IsTrue(connection.IsScopingForTests,
            "ActivateGhosting runs only after the create-release step");
        Assert.AreSame(character.Ghost, connection.GetScopeObject());
    }

    [TestMethod]
    public void Transfer_Stage3Ack_BeforeStage2_IsIgnored()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        _sent.Clear();

        InvokeStage3Ack(connection, character.ObjectId.Coid);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.IsFalse(_sent.OfType<TransferFromGlobalStage3Packet>().Any());
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any());
        Assert.IsFalse(connection.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
    }

    [TestMethod]
    public void Transfer_DuplicateStage2_DoesNotResendStage3()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        _sent.Clear();

        InvokeStage2(connection, character.ObjectId.Coid);
        InvokeStage2(connection, character.ObjectId.Coid);

        Assert.AreEqual(1, _sent.OfType<TransferFromGlobalStage3Packet>().Count());
        Assert.AreEqual(SectorTransferPhase.WaitingForStage3Ack, connection.TransferPhase);
    }

    [TestMethod]
    public void Transfer_DuplicateStage3Ack_DoesNotRerelease()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        InvokeStage2(connection, character.ObjectId.Coid);
        InvokeStage3Ack(connection, character.ObjectId.Coid);
        var countAfterFirst = _sent.Count;

        InvokeStage3Ack(connection, character.ObjectId.Coid);

        Assert.AreEqual(countAfterFirst, _sent.Count);
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
    }

    [TestMethod]
    public void Transfer_WrongCoidStage2_DoesNotAdvance()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        _sent.Clear();

        InvokeStage2(connection, character.ObjectId.Coid + 1);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.IsFalse(_sent.OfType<TransferFromGlobalStage3Packet>().Any());
    }

    [TestMethod]
    public void Transfer_WrongCoidStage3_DoesNotRelease()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);
        InvokeStage2(connection, character.ObjectId.Coid);

        InvokeStage3Ack(connection, character.ObjectId.Coid + 1);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage3Ack, connection.TransferPhase);
        Assert.IsFalse(character.WorldEntryComplete);
        Assert.IsFalse(connection.IsScopingForTests);
    }

    [TestMethod]
    public void Transfer_SecondTransfer_SupersedesFirst_StaleAckCannotReleaseB()
    {
        var mapB = CreateMap(693);
        var mapC = CreateMap(694);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, mapB);
        var genB = connection.TransferHandshakeGeneration;

        TransferOnto(character, mapC);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.AreEqual(694, connection.TransferHandshakeDestinationContinentId);
        Assert.IsTrue(connection.TransferHandshakeGeneration > genB);
        Assert.AreSame(mapC, character.Map);

        InvokeStage3Ack(connection, character.ObjectId.Coid);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.IsFalse(character.WorldEntryComplete);
        Assert.AreSame(mapC, character.Map);
    }

    [TestMethod]
    public void Transfer_DisconnectDuringPending_ClearsPhase()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);

        connection.EndCharacterSession();

        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
    }

    [TestMethod]
    public void Login_Stage2_WhenPhaseIsNone_StillSendsStage3()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        character.SetMap(dest);
        character.CurrentVehicle.SetMap(dest);
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        _sent.Clear();

        InvokeStage2(connection, character.ObjectId.Coid);

        Assert.AreEqual(1, _sent.OfType<TransferFromGlobalStage3Packet>().Count());
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any(),
            "login Stage2 must not send Creates; those wait for Stage3 ack");
    }

    [TestMethod]
    public void Transfer_CompletedHandshake_ThenImmediateNextTransfer_DoesNotReplayPriorCreates()
    {
        var mapB = CreateMap(693);
        var mapC = CreateMap(694);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, mapB);
        InvokeStage2(connection, character.ObjectId.Coid);
        InvokeStage3Ack(connection, character.ObjectId.Coid);
        Assert.AreEqual(SectorTransferPhase.None, connection.TransferPhase);
        _sent.Clear();

        TransferOnto(character, mapC);

        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.AreSame(mapC, character.Map);
        Assert.IsTrue(_sent.OfType<MapInfoPacket>().Any());
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any());
        Assert.IsFalse(connection.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
    }

    [TestMethod]
    public void Transfer_PendingHandshake_PersistsDestinationMap()
    {
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        TransferOnto(character, dest);

        Assert.AreEqual(DestContinentId, character.LastTownId);
        Assert.AreEqual(10f, character.GetDbPositionXForTests());
        connection.EndCharacterSession();
        Assert.AreEqual(DestContinentId, character.LastTownId,
            "disconnect during pending handshake must persist the destination map, not the source");
    }

    [TestMethod]
    public void Transfer_IntoInstance_KeepsOwnerUntilLeave()
    {
        const int instancedId = 9992;
        MapManager.Instance.ClearMapsForTests();
        InstancedContinents.SetForTests(new HashSet<int> { instancedId });
        MapManager.Instance.CreateInstanceForTests = id => CreateMap(id);
        try
        {
            var shared = CreateMap(SourceContinentId);
            MapManager.Instance.RegisterMapForTests(shared);
            var (character, connection) = CreateTransferableOnSourceMap();
            character.SetMap(shared);
            character.CurrentVehicle.SetMap(shared);
            MapManager.Instance.ResolveMapForTests = null;
            MapManager.Instance.SuppressCreatePacketsForTests = true;

            Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, instancedId));
            Assert.IsTrue(character.Map.IsInstance);
            Assert.AreEqual(character.ObjectId.Coid, character.Map.InstanceOwnerCoid);
            Assert.AreEqual(1, character.Map.PlayerCount);
            Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
            var instance = character.Map;

            connection.EndCharacterSession();
            CollectionAssert.DoesNotContain(MapManager.Instance.AllMapsForTests(), instance);
        }
        finally
        {
            MapManager.Instance.CreateInstanceForTests = null;
            InstancedContinents.SetForTests(null);
            MapManager.Instance.ClearMapsForTests();
        }
    }

    [TestMethod]
    public void Transfer_Handshake_EmitsStructuredEvents()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);
        try
        {
            var dest = CreateMap(DestContinentId);
            var (character, connection) = CreateTransferableOnSourceMap();
            TransferOnto(character, dest);
            sink.Single("MapTransferHandshakeWaiting");

            InvokeStage2(connection, character.ObjectId.Coid);
            sink.Single("MapTransferStage2Received");
            sink.Single("MapTransferStage3Sent");

            InvokeStage3Ack(connection, character.ObjectId.Coid);
            sink.Single("MapTransferStage3AckReceived");
            sink.Single("MapTransferCreatesReleased");
            sink.Single("MapTransferGhostingActivated");
        }
        finally
        {
            AutoCore.Utils.Logging.GameLog.ResetForTests();
        }
    }

    [TestMethod]
    public void Transfer_MalformedStage2_DoesNotThrowOutOfDispatch()
    {
        var dest = CreateMap(DestContinentId);
        var (_, connection) = CreateTransferableOnSourceMap();
        TransferOnto(connection.CurrentCharacter, dest);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)GameOpcode.TransferFromGlobalStage2);
            writer.Write((byte)0x01);
        }

        connection.HandlePacketForTests(new ByteBuffer(stream.ToArray(), (uint)stream.Length));
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
        Assert.IsFalse(connection.CurrentCharacter.WorldEntryComplete);
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
            MapFileName = $"tm_xfer_hs_{continentId}",
            DisplayName = "xfer-hs",
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
        connection.SetNetAddress(new IPEndPoint(System.Net.IPAddress.Loopback, 0));

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

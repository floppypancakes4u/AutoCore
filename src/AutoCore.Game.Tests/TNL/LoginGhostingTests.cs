using System.Net;
using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 4: initial Sector login must not start normal ghosting until Stage3
/// ack has released local Creates. Client rpcStartGhosting_remote 0x00781300
/// replies immediately (no Stage1/2/3 check); FUN_008078B0 applies foreign
/// ghosts before game packets.
/// </summary>
[TestClass]
public class LoginGhostingTests
{
    private const long CharCoid = 9_081_000_101L;
    private const long VehicleCoid = 9_081_000_102L;
    private const long NpcCoid = 9_081_000_201L;
    private const int StartContinentId = 558;
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
        ObjectManager.Instance.Remove(NpcCoid);
        _sent.Clear();
    }

    [TestMethod]
    public void Connect_DoesNotStartNormalGhosting()
    {
        var conn = CreateSectorConnection();
        var seq = conn.GetGhostingSequence();

        conn.OnConnectionEstablished();

        Assert.IsTrue(conn.DoesGhostFrom(), "SetGhostFrom must remain at connect");
        Assert.IsFalse(conn.IsScopingForTests, "rpcStartGhosting must not be posted at connect");
        Assert.AreEqual(seq, conn.GetGhostingSequence());
        Assert.IsFalse(conn.IsGhosting());
        Assert.IsNull(conn.GetScopeObject());
    }

    [TestMethod]
    public void Stage1Window_SendsNoGhosting_AfterMapReadyState()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();

        Assert.IsFalse(conn.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any());
        Assert.IsFalse(_sent.OfType<CreateCharacterExtendedPacket>().Any());
    }

    [TestMethod]
    public void Stage2_SendsStage3_GhostingStillOff()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();
        _sent.Clear();

        InvokeStage2(conn, character.ObjectId.Coid);

        Assert.AreEqual(1, _sent.OfType<TransferFromGlobalStage3Packet>().Count());
        Assert.IsFalse(conn.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any());
        Assert.AreEqual(SectorTransferPhase.None, conn.TransferPhase);
    }

    [TestMethod]
    public void Stage3Ack_SendsCreates_ThenActivatesGhostingExactlyOnce()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var seqAtConnect = conn.GetGhostingSequence();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();
        conn.SuppressCreatePacketsForTests = true;
        InvokeStage2(conn, character.ObjectId.Coid);

        InvokeStage3Ack(conn, character.ObjectId.Coid);

        Assert.IsTrue(character.WorldEntryComplete);
        Assert.IsTrue(conn.IsScopingForTests);
        Assert.IsTrue(conn.GetGhostingSequence() > seqAtConnect,
            "ActivateGhosting must run exactly once, at Stage3, not at connect");
        Assert.AreSame(character.Ghost, conn.GetScopeObject());

        var ops = conn.WorldEntryOpsForTests;
        var vehicle = ops.IndexOf("CreateVehicleExtended");
        var characterOp = ops.IndexOf("CreateCharacterExtended");
        var activate = ops.IndexOf("ActivateGhosting");
        Assert.IsTrue(vehicle >= 0, "CreateVehicleExtended must be recorded");
        Assert.IsTrue(characterOp >= 0, "CreateCharacterExtended must be recorded");
        Assert.IsTrue(activate >= 0, "ActivateGhosting must be recorded");
        Assert.IsTrue(vehicle < characterOp, "CreateVehicleExtended < CreateCharacterExtended");
        Assert.IsTrue(characterOp < activate, "CreateCharacterExtended < ActivateGhosting");
        Assert.AreEqual(1, ops.Count(o => o == "ActivateGhosting"));
    }

    [TestMethod]
    public void DuplicateStage3_DoesNotActivateGhostingTwice()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();
        conn.SuppressCreatePacketsForTests = true;
        InvokeStage2(conn, character.ObjectId.Coid);
        InvokeStage3Ack(conn, character.ObjectId.Coid);
        var seq = conn.GetGhostingSequence();
        var activateCount = conn.WorldEntryOpsForTests.Count(o => o == "ActivateGhosting");

        InvokeStage3Ack(conn, character.ObjectId.Coid);

        Assert.AreEqual(seq, conn.GetGhostingSequence());
        Assert.AreEqual(activateCount, conn.WorldEntryOpsForTests.Count(o => o == "ActivateGhosting"));
        Assert.AreEqual(SectorTransferPhase.None, conn.TransferPhase);
    }

    [TestMethod]
    public void Stage3BeforeStage2_DoesNotActivateGhosting()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();
        conn.SuppressCreatePacketsForTests = true;

        InvokeStage3Ack(conn, character.ObjectId.Coid);

        Assert.IsFalse(conn.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
        Assert.IsFalse(conn.WorldEntryOpsForTests.Contains("ActivateGhosting"));
    }

    [TestMethod]
    public void Stage1WithoutStage2_DoesNotActivateGhosting()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();

        Assert.IsFalse(conn.IsScopingForTests);
        Assert.IsFalse(character.WorldEntryComplete);
        Assert.IsFalse(conn.WorldEntryOpsForTests.Contains("ActivateGhosting"));
    }

    [TestMethod]
    public void DisconnectBeforeStage3_DoesNotAssumeGhostingWasActive()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();

        conn.EndCharacterSession();

        Assert.IsFalse(conn.IsScopingForTests);
        Assert.IsFalse(conn.IsGhosting());
        Assert.IsNull(conn.CurrentCharacter);
    }

    [TestMethod]
    public void WrongCharacterCoid_DoesNotActivateGhosting()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();
        conn.SuppressCreatePacketsForTests = true;
        InvokeStage2(conn, character.ObjectId.Coid);

        InvokeStage3Ack(conn, character.ObjectId.Coid + 99);

        Assert.IsFalse(character.WorldEntryComplete);
        Assert.IsFalse(conn.IsScopingForTests);
    }

    [TestMethod]
    public void FailedCharacterLoad_DoesNotActivateGhosting()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();

        InvokeStage3Ack(conn, 9_081_999_999L);

        Assert.IsFalse(conn.IsScopingForTests);
        Assert.IsFalse(_sent.OfType<CreateVehicleExtendedPacket>().Any());
    }

    [TestMethod]
    public void NoForeignGhostRecordsBeforeStage3Ack()
    {
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, map) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();

        var npc = new Creature();
        npc.SetCoid(NpcCoid, true);
        npc.SetMap(map);
        npc.CreateGhost();

        SimulateReadyForNormalGhosts(conn);
        conn.PrepareWritePacket();

        Assert.IsNull(conn.GetScopeObject(), "ScopeObject is the WritePacket record gate");
        Assert.IsNull(npc.Ghost.GetFirstObjectRef(),
            "PerformScopeQuery must not ObjectInScope foreign ghosts before Stage3");
    }

    [TestMethod]
    public void CompletedLogin_ThenMapTransfer_ResetThenReactivateStillWorks()
    {
        var dest = CreateMap(DestContinentId);
        var conn = CreateSectorConnection();
        conn.OnConnectionEstablished();
        var (character, _) = AttachLoginCharacter(conn, StartContinentId);
        character.BeginWorldEntry();
        conn.SuppressCreatePacketsForTests = true;
        InvokeStage2(conn, character.ObjectId.Coid);
        InvokeStage3Ack(conn, character.ObjectId.Coid);
        Assert.IsTrue(conn.IsScopingForTests);
        var loginSeq = conn.GetGhostingSequence();

        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));
        Assert.IsFalse(conn.IsScopingForTests, "ResetGhosting must tear down the login lifecycle");
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, conn.TransferPhase);

        InvokeStage2(conn, character.ObjectId.Coid);
        InvokeStage3Ack(conn, character.ObjectId.Coid);

        Assert.AreEqual(SectorTransferPhase.None, conn.TransferPhase);
        Assert.IsTrue(conn.IsScopingForTests);
        Assert.IsTrue(conn.GetGhostingSequence() > loginSeq,
            "transfer must start a new ghosting sequence after ResetGhosting");
        Assert.IsTrue(character.WorldEntryComplete);
    }

    [TestMethod]
    public void Login_Handshake_EmitsStructuredEvents()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);
        try
        {
            var conn = CreateSectorConnection();
            conn.OnConnectionEstablished();
            sink.Single("SectorGhostingDeferredForWorldEntry");

            var (character, _) = AttachLoginCharacter(conn, StartContinentId);
            character.BeginWorldEntry();
            conn.SuppressCreatePacketsForTests = true;
            InvokeStage2(conn, character.ObjectId.Coid);
            InvokeStage3Ack(conn, character.ObjectId.Coid);
            sink.Single("SectorGhostingActivatedAfterWorldEntry");

            InvokeStage3Ack(conn, character.ObjectId.Coid);
            sink.Single("SectorGhostingDuplicateActivationPrevented");
        }
        finally
        {
            AutoCore.Utils.Logging.GameLog.ResetForTests();
        }
    }

    [TestMethod]
    public void DisconnectBeforeActivation_EmitsStructuredEvent()
    {
        var sink = new AutoCore.Game.Tests.Fakes.InMemoryLogSink();
        AutoCore.Utils.Logging.GameLog.SetSinkForTests(sink);
        try
        {
            var conn = CreateSectorConnection();
            conn.OnConnectionEstablished();
            var (character, _) = AttachLoginCharacter(conn, StartContinentId);
            character.BeginWorldEntry();
            sink.Clear();

            conn.EndCharacterSession();
            sink.Single("SectorGhostingDisconnectBeforeActivation");
        }
        finally
        {
            AutoCore.Utils.Logging.GameLog.ResetForTests();
        }
    }

    private static TNLConnection CreateSectorConnection()
    {
        var iface = new TNLInterface(doGhosting: true, skipNetworkBind: true);
        var conn = new TNLConnection();
        conn.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        conn.SetInterface(iface);
        conn.SetPlayerCOID(5);
        return conn;
    }

    private static (Character Character, SectorMap Map) AttachLoginCharacter(
        TNLConnection connection,
        int continentId)
    {
        var map = CreateMap(continentId);
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
        return (character, map);
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_login_gh_{continentId}",
            DisplayName = "login-gh",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(10f, 20f, 30f, 0f));
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

    private static void SimulateReadyForNormalGhosts(TNLConnection connection)
    {
        var method = typeof(global::TNL.Entities.GhostConnection).GetMethod(
            "rpcReadyForNormalGhosts_remote",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            return;
        method.Invoke(connection, new object[] { connection.GetGhostingSequence() });
    }

    private sealed class NoopWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot)
        {
        }
    }
}

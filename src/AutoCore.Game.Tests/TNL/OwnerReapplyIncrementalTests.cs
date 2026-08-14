using System.Net;
using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 13 — ForeignOwnerAttachReapply vs live GhostVehicle incremental updates.
///
/// Client <c>VehicleNet_UnpackGhostVehicle</c> (0x005F7720) loads
/// <c>wrapper-&gt;vtbl+0x1D4()</c> into the incremental apply pointer and
/// skips every view write when that pointer is NULL. These tests pin the
/// AutoCore schedule, Destroy→Create order, dirty-mask set, and lifecycle
/// cancels around that client contract. Production reapply is not removed:
/// <c>FUN_0080AF70</c> no-ops a second CreateCreature of the same TFID.
/// </summary>
[TestClass]
public class OwnerReapplyIncrementalTests
{
    private const int VehicleCbid = 813_100;
    private const int DriverCbid = 813_101;
    private const long ObserverCoid = MapNpcIdentity.CoidBase + 83_099;
    private const long VehicleCoid = MapNpcIdentity.CoidBase + 83_001;
    private const long DriverCoid = MapNpcIdentity.CoidBase + 83_002;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;

    private static readonly FieldInfo DirtyMaskBitsField =
        typeof(NetObject).GetField("_dirtyMaskBits", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("NetObject._dirtyMaskBits missing.");

    private int _savedHoldMs;
    private int _savedHoldQueries;
    private int _savedStaleMs;
    private int _savedReapplyMs;
    private Func<int, SectorMap> _previousResolver;
    private bool _previousSuppress;

    [TestInitialize]
    public void Init()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterVehicleCloneBase(VehicleCbid, defaultWheelsetCbid: 40);
        AssetManagerTestHelper.RegisterCreatureCloneBase(DriverCbid, maxHitPoint: 40);
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        _savedHoldMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        _savedHoldQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        _savedStaleMs = TNLConnection.ForeignCreateHoldStaleGraceMilliseconds;
        _savedReapplyMs = TNLConnection.ForeignOwnerAttachReapplyMilliseconds;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 15000;
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 10_000;
        _previousResolver = MapManager.Instance.ResolveMapForTests;
        _previousSuppress = MapManager.Instance.SuppressCreatePacketsForTests;
        TNLConnection.MissionFlushForTests = () => { };
        TNLConnection.WorldStatePersistenceForTests = new NoopWorldStatePersistence();
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = null;
        TNLConnection.MissionFlushForTests = null;
        TNLConnection.WorldStatePersistenceForTests = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.ForeignGhostScopeHoldMilliseconds = _savedHoldMs;
        TNLConnection.ForeignGhostScopeHoldQueries = _savedHoldQueries;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = _savedStaleMs;
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = _savedReapplyMs;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        MapManager.Instance.ResolveMapForTests = _previousResolver;
        MapManager.Instance.SuppressCreatePacketsForTests = _previousSuppress;
        ObjectManager.Instance.Remove(ObserverCoid);
        ObjectManager.Instance.Remove(VehicleCoid);
        ObjectManager.Instance.Remove(DriverCoid);
    }

    [TestMethod]
    public void OwnerReapply_RunsOnlyAfterGhostVehicleEstablished()
    {
        var scene = ArrangeNpcVehicleInRange();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "Hold must defer GhostVehicle on the create query.");
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "Reapply is scheduled on first GhostVehicle scope, not on CreateVehicle.");

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "After the hold, GhostVehicle is live.");
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "nowGhosted && !wasGhosted arms the delayed Destroy+Recreate.");
    }

    [TestMethod]
    public void OwnerReapply_DoesNotRunDuringForeignCreateHold()
    {
        var scene = ArrangeNpcVehicleInRange();
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 0;

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(VehicleCoid));
        Assert.IsFalse(scene.Packets.OfType<DestroyObjectPacket>().Any(),
            "Zero-delay reapply must not fire while ObjectInScope is still held.");
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
    }

    [TestMethod]
    public void OwnerReapply_DestroyCreateOrder_IsGuaranteedOrdered()
    {
        var scene = ArrangeNpcVehicleInRange();
        var packets = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle);
        Assert.AreEqual(4, packets.Count);

        var rpcs = new List<TNLConnection.GameRpcCapture>();
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = (_, rpc) => rpcs.Add(rpc);

        var conn = new TNLConnection();
        conn.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
        foreach (var packet in packets)
            conn.SendGamePacket(packet);

        Assert.IsTrue(rpcs.Count > 4, "CreateVehicle 0xD78 must fragment at 220.");
        Assert.IsTrue(rpcs.All(r =>
                r.Method == nameof(TNLConnection.rpcMsgGuaranteedOrdered)
                || r.Method == nameof(TNLConnection.rpcMsgGuaranteedOrderedFragmented)),
            "Every reapply RPC is GuaranteedOrdered. Client cannot reorder Destroy past Create fragments.");

        var firstDestroy = rpcs.FindIndex(r => r.Type == (uint)GameOpcode.DestroyObject);
        var lastCreateVehicle = rpcs.FindLastIndex(r => r.Type == (uint)GameOpcode.CreateVehicle);
        var createCreature = rpcs.FindIndex(r => r.Type == (uint)GameOpcode.CreateCreature);
        Assert.IsTrue(firstDestroy >= 0 && lastCreateVehicle > firstDestroy && createCreature > lastCreateVehicle,
            "Ordered stream is Destroy* then CreateVehicle fragments then CreateCreature.");
    }

    [TestMethod]
    public void OwnerReapply_ExistingGhostRemainsOrIsSafelyRebound()
    {
        var scene = FirstScopeUntilGhosted();
        var ghostBefore = scene.Vehicle.Ghost;
        Assert.IsNotNull(ghostBefore.GetFirstObjectRef());

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreSame(ghostBefore, scene.Vehicle.Ghost,
            "Reapply must not KillGhost / allocate a new GhostVehicle. Client FUN_00807550 rebinds the live ghost.");
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "GhostVehicle stays scoped across Destroy→Create. Slot 10 cannot re-enter (+0x5C already freed).");
    }

    [TestMethod]
    public void OwnerReapply_DirtyMasks_DoNotTargetNullVehicleView()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Vehicle.CreateGhost();
        scene.Vehicle.Ghost.ClearDirtyMaskBitsForTests();

        Assert.IsTrue(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, scene.Vehicle.Ghost));

        var dirty = GetDirtyMaskBits(scene.Vehicle.Ghost);
        Assert.AreEqual(GhostObject.HealthMask | GhostObject.HealthMaxMask,
            dirty & (GhostObject.HealthMask | GhostObject.HealthMaxMask),
            "Reapply always dirties Health/HealthMax. UnpackUpdate apply is gated on iVar11==0 (0x1D4 NULL).");
        if (scene.Vehicle.WheelSet != null && scene.Vehicle.WheelSet.CBID > 0)
        {
            Assert.AreNotEqual(0UL, dirty & GhostVehicle.WheelSetMask,
                "When a live wheelset exists, WheelSetMask is added. Incremental wheel reads also NULL-check local_12c.");
        }

        Assert.AreEqual(0UL, dirty & GhostObject.PositionMask,
            "Reapply must not force PositionMask. Path pose stays independently dirty and is also NULL-view safe.");
        Assert.AreEqual(0UL, dirty & GhostVehicle.GMMask,
            "GM/owner-only masks must stay off. Those incremental branches write through owner MI.");
    }

    [TestMethod]
    public void OwnerReapply_VehicleDiesBeforeSchedule_NoReapply()
    {
        var scene = FirstScopeUntilGhosted();
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        scene.Vehicle.OnDeath(DeathType.Violent);
        Assert.IsNull(scene.Vehicle.Map);

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Off-map corpse is not selected. TryConsume never runs.");
        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count());
        Assert.AreEqual(0, scene.Packets.OfType<CreateCreaturePacket>().Count());
    }

    [TestMethod]
    public void OwnerReapply_LeavesScopeBeforeSchedule_NoReapply()
    {
        var scene = FirstScopeUntilGhosted();
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        scene.Observer.Position = new Vector3(10_000f, 0f, 10_000f);
        scene.Observer.CurrentVehicle.Position = scene.Observer.Position;
        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Out-of-range vehicles are not in the interest set. Pending stays but does not fire.");
        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count());
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "Leave-scope does not cancel the deadline; only TryConsume / ClearGlobalVehicleCreateTracking do.");
    }

    [TestMethod]
    public void OwnerReapply_MapTransferCancelsOrSkips()
    {
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        var npc = PlaceNpcVehicle(character.Map, VehicleCoid);
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        character.CreateGhost();
        connection.SetScopeObject(character.Ghost);

        character.Map.PerformScopeQuery(null, character, connection);
        character.Map.PerformScopeQuery(null, character, connection);
        Assert.IsTrue(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "precondition: first GhostVehicle scheduled reapply");

        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));

        Assert.IsFalse(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "ResetGhosting / transfer ClearGlobalVehicleCreateTracking must drop the deadline.");
        Assert.IsFalse(npc.Ghost.IsGhostedTo(connection));
    }

    [TestMethod]
    public void OwnerReapply_IsIdempotent()
    {
        var scene = FirstScopeUntilGhosted();
        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var first = scene.Packets.OfType<DestroyObjectPacket>().Count();
        Assert.IsTrue(first >= 2, "First due query emits the Destroy pair.");
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        scene.Packets.Clear();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Ghost stays scoped (wasGhosted). Consume is one-shot. No second Destroy+Create.");
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
    }

    [TestMethod]
    public void InitialDriverBind_IsCompleteBeforeReapply()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var vehicleCreate = scene.Packets.OfType<CreateVehiclePacket>()
            .Single(p => p.ObjectId.Coid == VehicleCoid);
        var driverCreate = scene.Packets.OfType<CreateCreaturePacket>()
            .Single(p => p.ObjectId.Coid == DriverCoid);
        Assert.AreEqual(DriverCoid, vehicleCreate.CoidCurrentOwner,
            "CreateVehicle +0xD8 is the driver so Vehicle_applyCreatePacket can SetVehicle.");
        Assert.AreEqual(VehicleCoid, driverCreate.CoidCurrentVehicle,
            "CreateCreature +0xF8 is the chassis so PostCreate SetVehicle runs.");
        Assert.IsTrue(
            scene.Packets.FindIndex(p => p is CreateVehiclePacket) <
            scene.Packets.FindIndex(p => p is CreateCreaturePacket),
            "Pass 9 order: chassis exists before PostCreate.");
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "Bind packets are on the wire before GhostVehicle, therefore before reapply is even scheduled.");
        Assert.AreSame(scene.Driver, scene.Vehicle.Owner);
        Assert.IsNull(scene.Driver.GetAsCharacter());
    }

    [TestMethod]
    public void RequestObjectVehicleDriverRecoveryStillWorks()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Connection.CurrentCharacter = scene.Observer;
        scene.Observer.SetMap(scene.Map);
        scene.Packets.Clear();

        InvokeRequestObject(scene.Connection, VehicleCoid, global: true);

        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Any(p => p.ObjectId.Coid == VehicleCoid));
        var driverCreate = scene.Packets.OfType<CreateCreaturePacket>()
            .SingleOrDefault(p => p.ObjectId.Coid == DriverCoid);
        Assert.IsNotNull(driverCreate,
            "RequestObject vehicle still calls ForeignNpcDriverWire.TrySendDriverCreate.");
        Assert.AreEqual(VehicleCoid, driverCreate.CoidCurrentVehicle);
    }

    [TestMethod]
    public void OwnerReapply_DoesNotScheduleForCharacterOwner()
    {
        var scene = ArrangeNpcVehicleInRange();
        var character = new Character();
        character.SetCoid(MapNpcIdentity.CoidBase + 83_050, false);
        scene.Vehicle.SetOwner(character);

        Assert.IsFalse(ForeignNpcDriverWire.HasPureCreatureDriver(scene.Vehicle));
        Assert.IsFalse(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(scene.Connection, hasCreatureOwner: false));
        Assert.AreEqual(0, ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle).Count,
            "Character-owned chassis must never take the NPC Destroy+Recreate path.");
    }

    [TestMethod]
    public void OwnerReapply_CreateVehicleFragmentsSpanMultipleTnlPackets()
    {
        var scene = ArrangeNpcVehicleInRange();
        var packets = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle);

        var rpcs = new List<TNLConnection.GameRpcCapture>();
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = (_, rpc) => rpcs.Add(rpc);

        var conn = new TNLConnection();
        conn.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
        foreach (var packet in packets)
            conn.SendGamePacket(packet);

        var createFrags = rpcs.Where(r => r.Type == (uint)GameOpcode.CreateVehicle).ToList();
        Assert.IsTrue(createFrags.Count >= 2, "0xD78 / 220 requires multiple fragments.");
        Assert.IsTrue(createFrags.Sum(r => r.Data.Length) > 220,
            "CreateVehicle cannot complete in one 220-byte RPC. Client may apply Destroy before the last fragment.");
        Assert.AreEqual(createFrags[0].FragmentCount, (ushort)createFrags.Count);
    }

    private Scene FirstScopeUntilGhosted()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());
        return scene;
    }

    private Scene ArrangeNpcVehicleInRange()
    {
        var map = CreateFieldMap();
        var driver = new Creature { Level = 3, Position = new Vector3(15f, 0f, 0f) };
        driver.SetCoid(DriverCoid, true);
        driver.LoadCloneBase(DriverCbid);
        driver.SetupCBFields();

        var vehicle = new Vehicle { Position = new Vector3(15f, 0f, 0f) };
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.LoadCloneBase(VehicleCbid);
        vehicle.SetupCBFields();
        vehicle.NpcAi = new NpcAiState();
        vehicle.SetOwner(driver);
        vehicle.CreateGhost();
        vehicle.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(ObserverCoid, true);
        observer.AttachTestDataForTests("OwnerReapplyObserver");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        observer.SetMap(map);

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        observer.SetOwningConnection(connection);
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);
        return new Scene(map, observer, vehicle, driver, connection, packets);
    }

    private static Vehicle PlaceNpcVehicle(SectorMap map, long coid)
    {
        var vehicle = new Vehicle { Position = new Vector3(15f, 0f, 0f) };
        vehicle.SetCoid(coid, true);
        vehicle.LoadCloneBase(VehicleCbid);
        vehicle.SetupCBFields();
        vehicle.NpcAi = new NpcAiState();
        var driver = new Creature { Position = vehicle.Position };
        driver.SetCoid(DriverCoid, true);
        driver.LoadCloneBase(DriverCbid);
        driver.SetupCBFields();
        vehicle.SetOwner(driver);
        vehicle.CreateGhost();
        vehicle.SetMap(map);
        return vehicle;
    }

    private static SectorMap CreateFieldMap() => CreateMap(708);

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_ownerreapply_{continentId}",
            DisplayName = "owner-reapply",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        EnsureScopeLists(map);
        return map;
    }

    private static void EnsureScopeLists(SectorMap map)
    {
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }
    }

    private static (Character Character, TNLConnection Connection) CreateTransferableOnSourceMap()
    {
        var source = CreateMap(SourceContinentId);
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));

        var character = new Character();
        character.SetCoid(ObserverCoid, true);
        character.AttachTestDataForTests("XferOwnerReapply");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 83_090, true);
        vehicle.AttachTestDataForTests();
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(source);
        vehicle.SetMap(source);
        ObjectManager.Instance.Add(character);
        return (character, connection);
    }

    private static void InvokeRequestObject(TNLConnection connection, long coid, bool global)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        writer.Write(new byte[3]);
        writer.WriteTFID(coid, global);
        writer.Flush();

        var method = typeof(TNLConnection).GetMethod(
            "HandleRequestObjectPacket",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method);
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private static ulong GetDirtyMaskBits(NetObject obj) => (ulong)DirtyMaskBitsField.GetValue(obj);

    private sealed class NoopWorldStatePersistence : ICharacterWorldStatePersistence
    {
        public void Save(CharacterWorldStateSnapshot snapshot)
        {
        }
    }

    private sealed record Scene(
        SectorMap Map,
        Character Observer,
        Vehicle Vehicle,
        Creature Driver,
        TNLConnection Connection,
        List<BasePacket> Packets);
}

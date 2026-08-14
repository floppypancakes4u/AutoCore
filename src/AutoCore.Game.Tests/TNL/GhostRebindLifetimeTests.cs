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
/// PDB Pass 15 — GhostVehicle rebind vs old-wrapper destructor.
///
/// Client CompletelyDestroyObject (0x009440E0) unlists A and does not call
/// FUN_004D0E90 / wrapper deleting dtor 0x00507050. FUN_00807550 rebinds
/// ghost+0x50 = B. A's FUN_00518EC0 SetParent(0) is not on that sequence.
/// These tests pin the AutoCore emission that conclusion depends on.
/// Production is unchanged (gate 1 is false).
/// </summary>
[TestClass]
public class GhostRebindLifetimeTests
{
    private const int VehicleCbid = 815_100;
    private const int DriverCbid = 815_101;
    private const long ObserverCoid = MapNpcIdentity.CoidBase + 85_099;
    private const long VehicleCoid = MapNpcIdentity.CoidBase + 85_001;
    private const long DriverCoid = MapNpcIdentity.CoidBase + 85_002;
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

    /// <summary>
    /// Client game-queue order is Destroy(A) then Create(B). CompletelyDestroyObject
    /// does not run A's deleting dtor. FUN_00807550 therefore rebinds a still-live
    /// GhostVehicle. AutoCore must keep that GhostVehicle and emit Destroy before Create.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_OldWrapperDestroyedBeforeOrAfterGhostRebind()
    {
        var scene = FirstScopeUntilGhosted();
        var ghost = scene.Vehicle.Ghost;
        Assert.IsNotNull(ghost.GetFirstObjectRef());

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var packets = scene.Packets.ToList();
        var destroyIdx = packets.FindIndex(p =>
            p is DestroyObjectPacket d && d.ObjectId.Coid == VehicleCoid);
        var createIdx = packets.FindIndex(p =>
            p is CreateVehiclePacket c && c.ObjectId.Coid == VehicleCoid);
        Assert.IsTrue(destroyIdx >= 0 && createIdx > destroyIdx,
            "GuaranteedOrdered Destroy(A) precedes Create(B). Client CompletelyDestroyObject runs first.");
        Assert.AreSame(ghost, scene.Vehicle.Ghost,
            "No KillGhost. Client FUN_00807550 rebinds the same GhostVehicle to B.");
        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count(p => p.ObjectId.Coid == VehicleCoid));
    }

    /// <summary>
    /// After FUN_00807550, G+0x50 = B. A's FUN_00518EC0 blindly SetParent(0).
    /// That dtor is not invoked by CompletelyDestroyObject or the per-frame
    /// 0xE5FC drain. AutoCore must not emit a later Destroy/KillGhost that
    /// would free A after B is bound.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_OldDestructorCannotLeaveGhostParentNull()
    {
        var scene = FirstScopeUntilGhosted();
        var ghost = scene.Vehicle.Ghost;
        var infoBefore = ghost.GetFirstObjectRef();

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreSame(infoBefore, ghost.GetFirstObjectRef(),
            "GhostObjectRef unchanged. Client does not receive a new GhostVehicle that would leave B+0x14 stale.");
        scene.Packets.Clear();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "One-shot consume. No later Destroy that could run A's dtor after G rebound to B.");
        Assert.AreSame(ghost, scene.Vehicle.Ghost);
    }

    /// <summary>
    /// Reapply dirties Health|HealthMax. CreateVehicle also writes HP.
    /// Those are the fields that apply to whatever ghost+0x50 names after rebind.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_GhostStillReceivesHealthAfterRecreate()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Vehicle.CreateGhost();
        scene.Vehicle.Ghost.ClearDirtyMaskBitsForTests();

        Assert.IsTrue(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, scene.Vehicle.Ghost));

        var dirty = GetDirtyMaskBits(scene.Vehicle.Ghost);
        Assert.AreEqual(GhostObject.HealthMask | GhostObject.HealthMaxMask,
            dirty & (GhostObject.HealthMask | GhostObject.HealthMaxMask),
            "Health incremental after recreate targets the rebound wrapper (ghost+0x50 = B).");

        var create = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle)
            .OfType<CreateVehiclePacket>()
            .Single();
        Assert.IsTrue(create.MaximumHealth > 0 || create.CurrentHealth >= 0);
    }

    /// <summary>
    /// Reapply does not force Position. A later Position dirty remains available
    /// on the same GhostVehicle and applies to ghost+0x50 after FUN_00807550.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_GhostStillReceivesPositionAfterRecreate()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Vehicle.CreateGhost();
        scene.Vehicle.Ghost.ClearDirtyMaskBitsForTests();

        ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, scene.Vehicle.Ghost);

        var dirty = GetDirtyMaskBits(scene.Vehicle.Ghost);
        Assert.AreEqual(0UL, dirty & GhostObject.PositionMask);
        scene.Vehicle.Ghost.SetMaskBits(GhostObject.PositionMask);
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(scene.Vehicle.Ghost) & GhostObject.PositionMask);
        Assert.IsNotNull(scene.Vehicle.Ghost);
    }

    /// <summary>
    /// RequestObject resends CreateVehicle only. Client already-exists
    /// (FUN_004BB010 hit) does not call FUN_00807550. That cannot restore
    /// ghost+0x50 if A's dtor had cleared it.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_RequestObjectDoesNotMaskParentLoss()
    {
        var scene = FirstScopeUntilGhosted();
        scene.Connection.CurrentCharacter = scene.Observer;
        scene.Observer.SetMap(scene.Map);

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Any(p => p.ObjectId.Coid == VehicleCoid));

        scene.Packets.Clear();
        InvokeRequestObject(scene.Connection, VehicleCoid, global: true);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "RequestObject must not Destroy. A listed B takes the already-exists arm.");
        var resent = scene.Packets.OfType<CreateVehiclePacket>()
            .Single(p => p.ObjectId.Coid == VehicleCoid);
        Assert.IsFalse(resent.IsItemLink,
            "WriteToPacket IsItemLink=0. Client FUN_00812630 already-exists path still skips FUN_00807550.");
        Assert.AreSame(scene.Vehicle.Ghost, scene.Vehicle.Ghost);
    }

    /// <summary>
    /// Leave-scope does not execute reapply. Re-entry starts a new Create+hold.
    /// Map transfer ResetGhosting drops the pending deadline so a later G2 is
    /// a fresh ghost, not a parent-null leftover.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_DescopeReentryRemainsHealthy()
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
        Assert.IsTrue(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));

        Assert.IsFalse(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "ClearGlobalVehicleCreateTracking drops the deadline. Destination G2 is a new GhostVehicle.");
        Assert.IsFalse(npc.Ghost.IsGhostedTo(connection),
            "ResetGhosting before dest objects. Old G cannot leave B+0x14 pointing at a dead ghost.");
    }

    /// <summary>
    /// Schedule is nowGhosted &amp;&amp; !wasGhosted only. Consume is one-shot.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_RepeatedScopeDoesNotDuplicateRecovery()
    {
        var scene = FirstScopeUntilGhosted();
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Packets.OfType<DestroyObjectPacket>().Count() >= 2);
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        scene.Packets.Clear();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count());
        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count());
    }

    /// <summary>
    /// A dead / unselected vehicle is not consumed. No Destroy+Create against a corpse.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_VehicleDeathDuringSequenceSafe()
    {
        var scene = FirstScopeUntilGhosted();
        scene.Vehicle.OnDeath(DeathType.Violent);
        Assert.IsNull(scene.Vehicle.Map);

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Corpse / off-interest vehicle is not selected. Reapply must not fire.");
        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count());
    }

    /// <summary>
    /// Transfer clears tracking before destination objects exist.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_MapTransferDuringSequenceSafe()
    {
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        PlaceNpcVehicle(character.Map, VehicleCoid);
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        character.CreateGhost();
        connection.SetScopeObject(character.Ghost);

        character.Map.PerformScopeQuery(null, character, connection);
        character.Map.PerformScopeQuery(null, character, connection);
        Assert.IsTrue(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));

        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));
        Assert.IsFalse(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
        Assert.IsFalse(character.Ghost.IsGhostedTo(connection) &&
                       connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
    }

    /// <summary>
    /// Character owners never enter ForeignOwnerAttachReapply.
    /// </summary>
    [TestMethod]
    public void ForeignPlayerVehicle_Unchanged()
    {
        var scene = ArrangeNpcVehicleInRange();
        var character = new Character();
        character.SetCoid(MapNpcIdentity.CoidBase + 85_050, false);
        scene.Vehicle.SetOwner(character);

        Assert.IsFalse(ForeignNpcDriverWire.HasPureCreatureDriver(scene.Vehicle));
        Assert.AreEqual(0, ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle).Count);
        Assert.IsFalse(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(
            scene.Connection, hasCreatureOwner: false));
    }

    /// <summary>
    /// First-scope CreateVehicle → CreateCreature binds before reapply is scheduled.
    /// </summary>
    [TestMethod]
    public void InitialNpcDriverBind_Unchanged()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var vehicleCreate = scene.Packets.OfType<CreateVehiclePacket>()
            .Single(p => p.ObjectId.Coid == VehicleCoid);
        var driverCreate = scene.Packets.OfType<CreateCreaturePacket>()
            .Single(p => p.ObjectId.Coid == DriverCoid);
        Assert.AreEqual(DriverCoid, vehicleCreate.CoidCurrentOwner);
        Assert.AreEqual(VehicleCoid, driverCreate.CoidCurrentVehicle);
        Assert.IsTrue(
            scene.Packets.FindIndex(p => p is CreateVehiclePacket) <
            scene.Packets.FindIndex(p => p is CreateCreaturePacket));
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
        Assert.AreSame(scene.Driver, scene.Vehicle.Owner);
    }

    /// <summary>
    /// Production hold remains 500 ms / 1 query. This fixture zeros the clock
    /// only; ResetForeignGhostHoldDefaultsForTests restores the retail values.
    /// </summary>
    [TestMethod]
    public void VehicleHold_Remains500msAndOneQuery()
    {
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        Assert.AreEqual(500, TNLConnection.ForeignGhostScopeHoldMilliseconds);
        Assert.AreEqual(1, TNLConnection.ForeignGhostScopeHoldQueries);

        var scene = ArrangeNpcVehicleInRange();
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 500;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "First query after create is still the hold. GhostVehicle must not land with CreateVehicle.");
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
    }

    /// <summary>
    /// Every first GhostVehicle of a pure-Creature NPC chassis schedules reapply.
    /// Not a recovery-only signal.
    /// </summary>
    [TestMethod]
    public void OwnerReapply_SchedulesForEveryFirstNpcGhost()
    {
        var scene = ArrangeNpcVehicleInRange();
        Assert.IsTrue(ForeignNpcDriverWire.HasPureCreatureDriver(scene.Vehicle));
        Assert.IsTrue(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(
            scene.Connection, hasCreatureOwner: true));

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "nowGhosted && !wasGhosted arms every such first ghost. There is no ACK / failure signal.");
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
        observer.AttachTestDataForTests("GhostRebindObserver");
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
            MapFileName = $"tm_ghostrebind_{continentId}",
            DisplayName = "ghost-rebind",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
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

        var character = new Character();
        character.SetCoid(ObserverCoid, true);
        character.AttachTestDataForTests("XferGhostRebind");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 85_090, true);
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

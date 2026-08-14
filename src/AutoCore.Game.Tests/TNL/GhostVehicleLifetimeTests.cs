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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 12 — GhostVehicle slot-10 / NULL vehicle-view lifetime.
///
/// Client FUN_008078B0 calls GhostVehicle vtable slot 10 (<c>FUN_005F9F10</c>) only when
/// a vehicle TFID already exists and the wrapper is still waiting (<c>object+0x14 == 0</c>).
/// That apply calls <c>wrapper-&gt;vtbl+0x1D4()</c> and immediately reads
/// <c>view+0x103</c> (AV at 0x005F9F50) with no NULL guard.
/// These tests pin the AutoCore sequences that must not manufacture that
/// waiting-wrapper-with-NULL-view state.
/// </summary>
[TestClass]
public class GhostVehicleLifetimeTests
{
    private const int VehicleCbid = 812_100;
    private const int DriverCbid = 812_101;
    private const long ObserverCoid = MapNpcIdentity.CoidBase + 82_099;
    private const long VehicleCoid = MapNpcIdentity.CoidBase + 82_001;
    private const long DriverCoid = MapNpcIdentity.CoidBase + 82_002;
    private const long ForeignCharCoid = MapNpcIdentity.CoidBase + 82_003;
    private const int SourceContinentId = 558;
    private const int DestContinentId = 693;

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
        ObjectManager.Instance.Remove(ForeignCharCoid);
    }

    [TestMethod]
    public void ForeignVehicleFirstScope_CreatePrecedesGhost()
    {
        var scene = ArrangeNpcVehicleInRange();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(1, scene.Packets.OfType<CreateVehiclePacket>().Count(p => p.ObjectId.Coid == VehicleCoid),
            "First sighting must emit CreateVehicle so FUN_004BB010 can resolve a live 0x1D4 view.");
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "500 ms / 1-query hold must defer GhostVehicle. Same-tick ghost would take create-from-ghost, not slot 10.");
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(VehicleCoid));

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "After the hold, GhostVehicle is assigned to a wrapper whose CreateVehicle already ran — intended slot-10 path B.");
        Assert.IsFalse(scene.Connection.HasActiveForeignCreateHold(VehicleCoid));
    }

    [TestMethod]
    public void ForeignVehicleReentry_ExistingCreateCanAcceptFreshGhost()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        var ghostInfo = scene.Vehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo);
        scene.Packets.Clear();

        scene.Connection.DetachObject(ghostInfo);
        Assert.IsFalse(scene.Vehicle.Ghost.IsGhostedTo(scene.Connection),
            "DetachObject is what TNL WritePacket does on range exit. PerformScopeQuery alone does not.");
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Temporary range exit must not DestroyObject — the client wrapper/view stay valid for slot 10 re-entry.");

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Any(p => p.ObjectId.Coid == VehicleCoid),
            "Re-entry after detach re-sends CreateVehicle (existing TFID is a no-op; view stays live).");
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(VehicleCoid),
            "Re-entry must re-open the hold so GhostVehicle cannot beat the create pump.");
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "Hold still defers GhostVehicle on the first re-entry query.");

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "Intended slot-10 route: existing CreateVehicle object, fresh GhostVehicle, live 0x1D4.");
    }

    [TestMethod]
    public void VehicleDeath_DoesNotReleaseStaleGhostAfterDestroy()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());

        scene.Vehicle.OnDeath(DeathType.Violent);

        var destroy = scene.Packets.OfType<DestroyObjectPacket>().SingleOrDefault(p => p.ObjectId.Coid == VehicleCoid);
        Assert.IsNotNull(destroy, "NPC vehicle death must send DestroyObject.");
        Assert.IsNull(scene.Vehicle.Map, "OnDeath must SetMap(null) so the next query cannot ObjectInScope it.");

        scene.Packets.Clear();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count(),
            "A destroyed off-map vehicle must not emit another CreateVehicle.");
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count());
        Assert.IsFalse(scene.Connection.HasActiveForeignCreateHold(VehicleCoid),
            "Hold was cleared on the earlier ObjectInScope; death must not reopen it.");
    }

    [TestMethod]
    public void DestroyDuringForeignCreateHold_CancelsFutureGhost()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(VehicleCoid));
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef());

        scene.Vehicle.OnDeath(DeathType.Violent);
        Assert.IsNotNull(scene.Packets.OfType<DestroyObjectPacket>().SingleOrDefault(p => p.ObjectId.Coid == VehicleCoid));
        Assert.IsNull(scene.Vehicle.Map);

        scene.Packets.Clear();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "A vehicle that died mid-hold must never ObjectInScope. Stale GhostVehicle would be slot 10 on a torn-down view.");
        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count());
    }

    [TestMethod]
    public void ForeignOwnerAttachReapply_DoesNotOverlapUnsafeGhostLifetime()
    {
        var scene = ArrangeNpcVehicleInRange(withMappedDriver: false);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(), "reapply is scheduled only after first GhostVehicle");
        Assert.IsTrue(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
        Assert.AreSame(scene.Map, scene.Vehicle.Map, "Reapply does not SetMap(null).");

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var packets = scene.Packets;
        var destroyVehicle = packets.FindIndex(p => p is DestroyObjectPacket d && d.ObjectId.Coid == VehicleCoid);
        var destroyDriver = packets.FindIndex(p => p is DestroyObjectPacket d && d.ObjectId.Coid == DriverCoid);
        var createVehicle = packets.FindIndex(p => p is CreateVehiclePacket c && c.ObjectId.Coid == VehicleCoid);
        var createDriver = packets.FindIndex(p => p is CreateCreaturePacket c && c.ObjectId.Coid == DriverCoid);

        Assert.IsTrue(destroyVehicle >= 0 && destroyDriver > destroyVehicle,
            "Reapply is Destroy(vehicle) then Destroy(driver).");
        Assert.IsTrue(createVehicle > destroyDriver && createDriver > createVehicle,
            "Then CreateVehicle then CreateCreature on the same TFID.");
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "Reapply must not KillGhost. Slot 10 needs a new/waiting ghost; the existing one already freed +0x5C.");
        Assert.AreSame(scene.Map, scene.Vehicle.Map);
        Assert.IsFalse(scene.Connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid));
    }

    [TestMethod]
    public void TemporaryVehicleDescope_DoesNotSendDestroyObject()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());
        scene.Packets.Clear();

        scene.Observer.Position = new Vector3(10_000f, 0f, 10_000f);
        scene.Observer.CurrentVehicle.Position = scene.Observer.Position;
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Leave-range is TNL KillGhost only. DestroyObject would leave a waiting wrapper for slot 10.");
        Assert.AreSame(scene.Map, scene.Vehicle.Map);
    }

    [TestMethod]
    public void VehicleLogout_DescopeOnly()
    {
        var scene = ArrangeMountedForeignPlayer();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());

        scene.Packets.Clear();
        scene.ForeignCharacter.SetMap(null);
        scene.Vehicle.SetMap(null);
        scene.ForeignCharacter.ClearGhost();
        scene.Vehicle.ClearGhost();

        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "Logout is SetMap(null) + ClearGhost. Other clients lose the chassis via TNL descope.");
        Assert.IsNull(scene.Vehicle.Ghost);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count());
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count());
    }

    [TestMethod]
    public void MapTransfer_ResetGhostingBeforeVehicleTeardown()
    {
        MapManager.Instance.SuppressCreatePacketsForTests = true;
        var dest = CreateMap(DestContinentId);
        var (character, connection) = CreateTransferableOnSourceMap();
        var npc = PlaceNpcVehicle(character.Map, VehicleCoid);
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();
        character.CreateGhost();
        connection.SetScopeObject(character.Ghost);

        character.Map.PerformScopeQuery(null, character, connection);
        character.Map.PerformScopeQuery(null, character, connection);
        Assert.IsTrue(npc.Ghost.IsGhostedTo(connection), "precondition: old-map vehicle is ghosted");

        var sent = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => sent.Add(packet);
        MapManager.Instance.ResolveMapForTests = _ => dest;

        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));

        Assert.IsFalse(connection.IsScopingForTests,
            "ResetGhosting must run before MapInfo so rpcEndGhosting deletes GhostVehicle before FAM teardown.");
        Assert.IsTrue(sent.OfType<MapInfoPacket>().Any());
        Assert.IsFalse(sent.OfType<CreateVehiclePacket>().Any());
        Assert.IsFalse(sent.OfType<CreateVehicleExtendedPacket>().Any(),
            "Destination Creates wait for Stage3 ack.");
        Assert.AreEqual(SectorTransferPhase.WaitingForStage2, connection.TransferPhase);
    }

    [TestMethod]
    public void SameTfidVehicleRecreate_DoesNotReuseStaleGhost()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        var ghostBefore = scene.Vehicle.Ghost;
        Assert.IsNotNull(ghostBefore.GetFirstObjectRef());

        var packets = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle);
        Assert.AreEqual(VehicleCoid, ((DestroyObjectPacket)packets[0]).ObjectId.Coid);
        Assert.AreEqual(VehicleCoid, ((CreateVehiclePacket)packets[2]).ObjectId.Coid);
        Assert.IsFalse(((CreateVehiclePacket)packets[2]).IsItemLink);
        Assert.AreSame(ghostBefore, scene.Vehicle.Ghost,
            "Same-TFID recreate keeps the live GhostVehicle instance. A new initial would be required for slot 10.");
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void RequestObjectDestroyedVehicle_DoesNotResurrect()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Observer.SetMap(scene.Map);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Vehicle.OnDeath(DeathType.Violent);
        Assert.IsNull(scene.Vehicle.Map);

        scene.Packets.Clear();
        InvokeRequestObject(scene.Connection, VehicleCoid, global: true);

        Assert.AreEqual(0, scene.Packets.OfType<CreateVehiclePacket>().Count(),
            "RequestObject for an off-map vehicle must not resend CreateVehicle.");
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count());
        Assert.IsNull(scene.Vehicle.Map);
    }

    [TestMethod]
    public void CreateVehicleOrderedFragments_PrecedeLaterDestroyObject()
    {
        var rpcs = new List<TNLConnection.GameRpcCapture>();
        TNLConnection.TestPacketSink = null;
        TNLConnection.TestOutboundRpcSink = (_, rpc) => rpcs.Add(rpc);

        var conn = new TNLConnection();
        conn.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));

        var create = new CreateVehiclePacket { ObjectId = new TFID(VehicleCoid, true) };
        conn.SendGamePacket(create);
        var createCount = rpcs.Count;
        Assert.IsTrue(createCount > 1, "CreateVehicle 0xD78 must fragment at 220.");
        Assert.IsTrue(rpcs.All(r => r.IsFragmented));
        Assert.IsTrue(rpcs.All(r => r.Method == nameof(TNLConnection.rpcMsgGuaranteedOrderedFragmented)));
        Assert.IsTrue(rpcs.All(r => r.Type == (uint)GameOpcode.CreateVehicle));

        conn.SendGamePacket(new DestroyObjectPacket(new TFID(VehicleCoid, true)));

        var destroy = rpcs.Skip(createCount).ToList();
        Assert.AreEqual(1, destroy.Count);
        Assert.IsFalse(destroy[0].IsFragmented);
        Assert.AreEqual(nameof(TNLConnection.rpcMsgGuaranteedOrdered), destroy[0].Method);
        Assert.AreEqual((uint)GameOpcode.DestroyObject, destroy[0].Type);
        Assert.IsTrue(rpcs.Take(createCount).All(r => r.Fragment == rpcs[0].Fragment),
            "CreateVehicle fragments share one ordered sequence. DestroyObject is a later ordered event and cannot overtake them.");
    }

    [TestMethod]
    public void LocalVehicle_IsSkippedByInterestAndCreatedBeforeGhosting()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Observer.SetCoid(ObserverCoid, true);
        scene.Observer.CurrentVehicle.SetCoid(MapNpcIdentity.CoidBase + 82_090, true);
        scene.Observer.CurrentVehicle.CreateGhost();
        scene.Observer.SetMap(scene.Map);
        scene.Observer.CurrentVehicle.SetMap(scene.Map);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNull(scene.Observer.CurrentVehicle.Ghost.GetFirstObjectRef(),
            "PerformScopeQuery continues past IsLocalPlayerVehicle. Local chassis is ObjectLocalScopeAlways after Stage3 Creates, not the foreign hold/slot-10 path.");
        Assert.IsFalse(scene.Packets.OfType<CreateVehiclePacket>().Any(p => p.ObjectId.Coid == scene.Observer.CurrentVehicle.ObjectId.Coid),
            "Local vehicle must not receive foreign CreateVehicle from interest.");
        Assert.IsFalse(scene.Connection.HasActiveForeignCreateHold(scene.Observer.CurrentVehicle.ObjectId.Coid));
    }

    [TestMethod]
    public void HoldExitWithoutGhost_ThenReentry_IsIntendedSlot10Route()
    {
        var scene = ArrangeNpcVehicleInRange();
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 1500;

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(1, scene.Packets.OfType<CreateVehiclePacket>().Count());
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef());

        scene.Vehicle.SetMap(null);
        scene.Connection.DebugAgeForeignCreateHoldForTests(VehicleCoid, 10_000);
        Assert.IsFalse(scene.Connection.HasActiveForeignCreateHold(VehicleCoid));

        scene.Vehicle.SetMap(scene.Map);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Count() >= 2);
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef());

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "Create processed, never ghosted, left, re-entered: slot 10 applies a fresh ghost onto the existing live view.");
    }

    private Scene ArrangeNpcVehicleInRange(bool withMappedDriver = false)
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
        if (withMappedDriver)
        {
            driver.CreateGhost();
            driver.SetMap(map);
        }

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(ObserverCoid, true);
        observer.AttachTestDataForTests("VehLifeObserver");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        observer.SetMap(map);

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        observer.SetOwningConnection(connection);
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);
        return new Scene(map, observer, vehicle, driver, null, connection, packets);
    }

    private Scene ArrangeMountedForeignPlayer()
    {
        var map = CreateFieldMap();
        var foreign = new Character { Position = new Vector3(20f, 0f, 0f) };
        foreign.SetCoid(ForeignCharCoid, true);
        foreign.AttachTestDataForTests("VehLifeForeign");
        foreign.CreateGhost();

        var vehicle = new Vehicle { Position = new Vector3(20f, 0f, 0f) };
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.LoadCloneBase(VehicleCbid);
        vehicle.SetupCBFields();
        foreign.SetCurrentVehicleForTests(vehicle);
        vehicle.CreateGhost();
        vehicle.SetMap(map);
        foreign.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(ObserverCoid, true);
        observer.AttachTestDataForTests("VehLifeObserver");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        observer.SetMap(map);

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        observer.SetOwningConnection(connection);
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);
        return new Scene(map, observer, vehicle, null, foreign, connection, packets);
    }

    private static Vehicle PlaceNpcVehicle(SectorMap map, long coid)
    {
        var vehicle = new Vehicle { Position = new Vector3(15f, 0f, 0f) };
        vehicle.SetCoid(coid, true);
        vehicle.LoadCloneBase(VehicleCbid);
        vehicle.SetupCBFields();
        vehicle.NpcAi = new NpcAiState();
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
            MapFileName = $"tm_vehlife_{continentId}",
            DisplayName = "vehicle-lifetime",
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
        character.AttachTestDataForTests("XferVehLife");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 82_090, true);
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
        Character ForeignCharacter,
        TNLConnection Connection,
        List<BasePacket> Packets);
}

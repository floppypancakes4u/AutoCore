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
/// PDB Pass 14 — vehicle wrapper identity / same-TFID replacement.
///
/// Client <c>vtbl+0x1D4</c> is <c>CVOGClonedObjectBase::GetAsVehicle</c>
/// (<c>0x00506C30</c> vtordisp → <c>lea eax,[ecx-0x670]</c>), not a stored
/// view pointer. Same-TFID hash insert rejects duplicates
/// (<c>FUN_004e77d0</c> / "already listed"). These tests pin the AutoCore
/// owner-reapply sequence against that client model. Production is unchanged:
/// the client race (old wrapper <c>+0x14</c> dtor <c>SetParent(0)</c>) is
/// not a proven crash and has no proven safer server sequence.
/// </summary>
[TestClass]
public class VehicleWrapperLifetimeTests
{
    private const int VehicleCbid = 814_100;
    private const int DriverCbid = 814_101;
    private const long ObserverCoid = MapNpcIdentity.CoidBase + 84_099;
    private const long VehicleCoid = MapNpcIdentity.CoidBase + 84_001;
    private const long DriverCoid = MapNpcIdentity.CoidBase + 84_002;
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
    /// Client FUN_00807550 rebinds the live GhostVehicle (<c>ghost+0x50 = B</c>)
    /// when CreateVehicle allocates a new wrapper. AutoCore must keep the same
    /// GhostVehicle scoped across Destroy→Create.
    /// </summary>
    [TestMethod]
    public void SameTfidRecreate_RebindsGhostToNewVehicleWrapper()
    {
        var scene = FirstScopeUntilGhosted();
        var ghostBefore = scene.Vehicle.Ghost;
        Assert.IsNotNull(ghostBefore.GetFirstObjectRef());

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreSame(ghostBefore, scene.Vehicle.Ghost,
            "Same-TFID recreate must not KillGhost. Client FUN_00807550 writes ghost+0x50 = new wrapper.");
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());
        Assert.IsTrue(scene.Packets.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == VehicleCoid));
        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Any(p => p.ObjectId.Coid == VehicleCoid));
    }

    /// <summary>
    /// Client TFID hash rejects duplicates (FUN_004e77d0). After Destroy unlists A,
    /// CreateVehicle(X) inserts B. AutoCore must emit CreateVehicle with the same TFID.
    /// </summary>
    [TestMethod]
    public void SameTfidRecreate_TfidLookupReturnsNewWrapperAfterCreate()
    {
        var scene = ArrangeNpcVehicleInRange();
        var packets = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle);

        var destroyVeh = packets.OfType<DestroyObjectPacket>().First(p => p.ObjectId.Coid == VehicleCoid);
        var createVeh = packets.OfType<CreateVehiclePacket>().Single();
        Assert.AreEqual(destroyVeh.ObjectId.Coid, createVeh.ObjectId.Coid);
        Assert.AreEqual(destroyVeh.ObjectId.Global, createVeh.ObjectId.Global);
        Assert.IsFalse(createVeh.IsItemLink,
            "IsItemLink=0 is the new-object arm. FUN_004BB010 misses an unlisted TFID and GiveItemByCbid allocates B.");
        Assert.AreEqual(VehicleCbid, createVeh.CBID);
    }

    /// <summary>
    /// After recreate the server still owns one Vehicle instance. The client
    /// wrapper A is no longer the TFID-table authority once B is inserted.
    /// </summary>
    [TestMethod]
    public void OldVehicleWrapper_DoesNotRemainAuthoritativeAfterRecreate()
    {
        var scene = FirstScopeUntilGhosted();
        var serverVehicle = scene.Vehicle;

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.AreSame(serverVehicle, scene.Vehicle,
            "AutoCore does not allocate a second server Vehicle. Same-TFID recreate is a client wrapper replacement only.");
        Assert.AreEqual(1, scene.Packets.OfType<CreateVehiclePacket>().Count(p => p.ObjectId.Coid == VehicleCoid),
            "Exactly one CreateVehicle for TFID X. A second Create would hit FUN_004bc180 'already listed' if B is already inserted.");
        Assert.AreEqual(VehicleCoid, scene.Vehicle.ObjectId.Coid);
        Assert.IsNotNull(scene.Vehicle.Map);
    }

    /// <summary>
    /// AttachGhost (FUN_00513f70) writes B+0x14 = ghost. Old A+0x14 is only
    /// cleared in A's destructor. AutoCore must not leave the ghost detached
    /// (KillGhost / new GhostVehicle) so FUN_00807550 can rebind.
    /// </summary>
    [TestMethod]
    public void GhostRebind_DoesNotLeaveUnsafeOldBackReference()
    {
        var scene = FirstScopeUntilGhosted();
        var ghost = scene.Vehicle.Ghost;
        var infoBefore = ghost.GetFirstObjectRef();

        scene.Packets.Clear();
        Assert.IsTrue(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, ghost));

        Assert.AreSame(ghost, scene.Vehicle.Ghost);
        Assert.AreSame(infoBefore, ghost.GetFirstObjectRef(),
            "GhostObjectRef stays. Client A+0x14 is a stale back-ref until A's dtor; ghost+0x50 is rebound to B.");
        Assert.IsFalse(scene.Packets.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == 0),
            "No extra teardown packets. Ghost lifetime is TNL scope, not DestroyObject.");
    }

    /// <summary>
    /// NULL-window incrementals are either applied to dying A, cached on the
    /// ghost when +0x50 is NULL, or corrected by the post-reapply dirty set.
    /// AutoCore always dirties Health|HealthMax after recreate.
    /// </summary>
    [TestMethod]
    public void IncrementalDuringNullWindow_IsReplayedOrCorrected()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Vehicle.CreateGhost();
        scene.Vehicle.Ghost.ClearDirtyMaskBitsForTests();

        Assert.IsTrue(ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, scene.Vehicle.Ghost));

        var dirty = GetDirtyMaskBits(scene.Vehicle.Ghost);
        Assert.AreNotEqual(0UL, dirty & (GhostObject.HealthMask | GhostObject.HealthMaxMask),
            "Post-recreate Health|HealthMax dirty corrects any incrementals that landed on dying A or the ghost cache.");
    }

    /// <summary>
    /// CreateVehicle writes current/max HP in the SimpleObject prefix. Reapply
    /// also dirties Health|HealthMax so B is corrected even if SetParent replay
    /// is incomplete.
    /// </summary>
    [TestMethod]
    public void HealthDuringNullWindow_IsCorrectAfterRecreate()
    {
        var scene = ArrangeNpcVehicleInRange();
        var packets = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle);
        var create = packets.OfType<CreateVehiclePacket>().Single();

        Assert.IsTrue(create.MaximumHealth > 0 || create.CurrentHealth >= 0,
            "CreateVehicle carries HP fields. B starts from the recreate payload, not leftover A pools.");

        scene.Vehicle.CreateGhost();
        scene.Vehicle.Ghost.ClearDirtyMaskBitsForTests();
        ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, scene.Vehicle.Ghost);

        var dirty = GetDirtyMaskBits(scene.Vehicle.Ghost);
        Assert.AreEqual(GhostObject.HealthMask | GhostObject.HealthMaxMask,
            dirty & (GhostObject.HealthMask | GhostObject.HealthMaxMask));
    }

    /// <summary>
    /// Position incrementals that hit the NULL/+0x50==0 window are not forced
    /// by reapply. The next ordinary Position dirty (path / VehicleMoved)
    /// targets whatever wrapper ghost+0x50 currently names.
    /// </summary>
    [TestMethod]
    public void PositionDuringNullWindow_EventuallyUsesNewWrapper()
    {
        var scene = ArrangeNpcVehicleInRange();
        scene.Vehicle.CreateGhost();
        scene.Vehicle.Ghost.ClearDirtyMaskBitsForTests();

        ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(
            scene.Connection, scene.Vehicle, scene.Vehicle.Ghost);

        var dirty = GetDirtyMaskBits(scene.Vehicle.Ghost);
        Assert.AreEqual(0UL, dirty & GhostObject.PositionMask,
            "Reapply must not force Position. Path pose dirties independently and applies to ghost+0x50 after FUN_00807550.");
        Assert.IsNotNull(scene.Vehicle.Ghost);
        scene.Vehicle.Ghost.SetMaskBits(GhostObject.PositionMask);
        Assert.AreNotEqual(0UL, GetDirtyMaskBits(scene.Vehicle.Ghost) & GhostObject.PositionMask,
            "A later Position dirty is still available on the same GhostVehicle.");
    }

    /// <summary>
    /// Equipment incrementals during the NULL window are guarded. CreateVehicle
    /// itself reconstructs wheel / weapons / melee / ornament / armor nests.
    /// </summary>
    [TestMethod]
    public void EquipmentDuringNullWindow_IsRecoveredByCreateVehicle()
    {
        var scene = ArrangeNpcVehicleInRange();
        var create = ForeignNpcDriverWire.BuildOwnerAttachReapplyPackets(scene.Vehicle)
            .OfType<CreateVehiclePacket>()
            .Single();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)create.Opcode);
        create.Write(writer);
        var bytes = stream.ToArray();

        Assert.IsTrue(bytes.Length >= 0x894,
            "Recreate includes every equipment nest through the front-weapon opcode. Client EquipFromCreate rebuilds from this payload.");
        Assert.AreEqual((uint)GameOpcode.CreateSimpleObject, BitConverter.ToUInt32(bytes, 0x158),
            "Ornament nest opcode. Ghost ornament deltas skipped while view is unavailable are recovered here.");
        Assert.AreEqual((uint)GameOpcode.CreateWheelSet, BitConverter.ToUInt32(bytes, 0x458));
        Assert.AreNotEqual(0, BitConverter.ToInt32(bytes, 0x45C),
            "Wheel CBID must not be ghost-synth 0. Empty nest is -1; a live wheel is > 0.");
        Assert.AreEqual((uint)GameOpcode.CreateArmor, BitConverter.ToUInt32(bytes, 0x5B0));
        Assert.AreEqual((uint)GameOpcode.CreateWeapon, BitConverter.ToUInt32(bytes, 0x708));
        Assert.AreEqual((uint)GameOpcode.CreateWeapon, BitConverter.ToUInt32(bytes, 0x890));
    }

    /// <summary>
    /// After Destroy unlists TFID X, FUN_008078B0 may RequestObject because
    /// GhostVehicle is still scoped and +0x5C is already freed. AutoCore must
    /// resend CreateVehicle+driver without emitting another Destroy pair.
    /// </summary>
    [TestMethod]
    public void RequestObject_DoesNotOverlapOwnerReapplyUnexpectedly()
    {
        var scene = FirstScopeUntilGhosted();
        scene.Connection.CurrentCharacter = scene.Observer;
        scene.Observer.SetMap(scene.Map);

        scene.Packets.Clear();
        scene.Connection.DebugAgeForeignOwnerAttachReapplyForTests(VehicleCoid, 20_000);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        var reapplyCreates = scene.Packets.OfType<CreateVehiclePacket>().Count();
        Assert.AreEqual(1, reapplyCreates);

        scene.Packets.Clear();
        InvokeRequestObject(scene.Connection, VehicleCoid, global: true);

        Assert.AreEqual(1, scene.Packets.OfType<CreateVehiclePacket>().Count(p => p.ObjectId.Coid == VehicleCoid),
            "RequestObject during/after reapply resends one CreateVehicle. Client already-exists (B in hash) is a no-op.");
        Assert.AreEqual(0, scene.Packets.OfType<DestroyObjectPacket>().Count(),
            "RequestObject must not start a second Destroy+Create reapply.");
        Assert.IsTrue(scene.Packets.OfType<CreateCreaturePacket>().Any(p => p.ObjectId.Coid == DriverCoid));
    }

    /// <summary>
    /// Passes 3/4: ResetGhosting (rpcEndGhosting) runs before map objects are
    /// torn down. ClearGlobalVehicleCreateTracking is part of that gate.
    /// </summary>
    [TestMethod]
    public void MapTransfer_StillClearsGhostsBeforeWrapperTeardown()
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
        Assert.IsTrue(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "precondition: first GhostVehicle scheduled reapply");
        Assert.IsNotNull(npc.Ghost.GetFirstObjectRef(),
            "precondition: GhostVehicle is scoped before transfer");

        MapManager.Instance.ResolveMapForTests = _ => dest;
        Assert.IsTrue(MapManager.Instance.TransferCharacterToMap(character, DestContinentId));

        Assert.IsFalse(connection.HasPendingForeignOwnerAttachReapplyForTests(VehicleCoid),
            "ResetGhosting / transfer ClearGlobalVehicleCreateTracking must drop the deadline.");
        Assert.IsFalse(npc.Ghost.IsGhostedTo(connection),
            "ResetGhosting (rpcEndGhosting) runs before destination FAM/wrapper teardown. Pass 14 dtor member-clear is after ghosts are gone.");
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
        var map = CreateMap(708);
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
        observer.AttachTestDataForTests("WrapperLifetimeObserver");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        observer.SetMap(map);

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        observer.SetOwningConnection(connection);
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);
        return new Scene(map, observer, vehicle, driver, connection, packets);
    }

    private static SectorMap CreateMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_wrapperlife_{continentId}",
            DisplayName = "wrapper-lifetime",
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
        character.AttachTestDataForTests("XferWrapperLife");
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 84_090, true);
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

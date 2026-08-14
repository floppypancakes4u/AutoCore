using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 9 — foreign NPC driver CreateCreature → GhostCreature ordering.
///
/// Client invariant (FUN_008078B0): ghost records are applied BEFORE the Sector
/// game-packet queue. RPC send order is not apply order.
///
/// Production spawn (<c>SpawnPoint.BuildDriver</c> / <c>CloneSpawner.BuildDriver</c>)
/// builds drivers as unmapped, ghostless <c>Vehicle.Owner</c> objects. First scope
/// therefore cannot <c>ObjectInScope(driver.Ghost)</c>. The creature-side bind is
/// <c>CreateCreature +0xF8</c> via <c>ForeignNpcDriverWire</c> after CreateVehicle.
/// Same-window GhostCreature-first would still be repaired by later CreateVehicle
/// (<c>Vehicle_applyCreatePacket</c> → <c>FUN_004c49d0</c>) the way Pass 8 repaired
/// Character. A driver GhostCreature hold is not required and must not be added
/// as a generic mounted-entity gate (Characters stay immediate).
/// </summary>
[TestClass]
public class NpcDriverOrderingTests
{
    private int _savedHoldMs;
    private int _savedHoldQueries;
    private int _savedStaleMs;

    [TestInitialize]
    public void Init()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        _savedHoldMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        _savedHoldQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        _savedStaleMs = TNLConnection.ForeignCreateHoldStaleGraceMilliseconds;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 15000;
        TNLConnection.ForceForeignCreateReapply = false;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.ForeignGhostScopeHoldMilliseconds = _savedHoldMs;
        TNLConnection.ForeignGhostScopeHoldQueries = _savedHoldQueries;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = _savedStaleMs;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    /// <summary>
    /// Names the client pump so a later change cannot "fix" ordering by assuming
    /// RPC send order equals apply order. FUN_008078B0 walks pending ghosts
    /// (conn+0x244) then drains Sector queue client+0xC84.
    /// </summary>
    [TestMethod]
    public void ClientAppliesGhostRecordsBeforeGameQueue_IsWhySendOrderIsNotApplyOrder()
    {
        Assert.AreEqual(
            nameof(ClientAppliesGhostRecordsBeforeGameQueue_IsWhySendOrderIsNotApplyOrder),
            "ClientAppliesGhostRecordsBeforeGameQueue_IsWhySendOrderIsNotApplyOrder",
            "FUN_008078B0 applies GhostCreature before CreateCreature even when the RPC is earlier on the wire.");
    }

    [TestMethod]
    public void MountedNpcDriver_FirstScope_SendsCreates_ButDoesNotScopeDriverGhost()
    {
        var scene = ArrangeMountedNpcDriver();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var vehicleIdx = scene.Packets.FindIndex(p => p is CreateVehiclePacket);
        var driverIdx = scene.Packets.FindIndex(p => p is CreateCreaturePacket);
        Assert.IsTrue(vehicleIdx >= 0 && driverIdx > vehicleIdx,
            "CreateVehicle then CreateCreature(driver). FUN_008078B0 apply order is still ghosts-then-game.");
        Assert.AreEqual(scene.Vehicle.ObjectId.Coid,
            ((CreateCreaturePacket)scene.Packets[driverIdx]).CoidCurrentVehicle,
            "packet+0xF8 must be chassis COID so CVOGCreature_PostCreateFromPacket calls SetVehicle.");
        Assert.IsNull(scene.Driver.Ghost,
            "Production driver is ghostless (SpawnPoint.BuildDriver). ObjectInScope(driver.Ghost) cannot fire.");
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "Existing vehicle create-hold must still defer GhostVehicle.");
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.Vehicle.ObjectId.Coid));
    }

    [TestMethod]
    public void MountedNpcDriver_ReleasesGhostVehicleAfterForeignVehicleCreateHold()
    {
        var scene = ArrangeMountedNpcDriver();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef());
        Assert.IsNull(scene.Driver.Ghost);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "GhostVehicle releases on the existing 1-query hold (holdMs=0 in this fixture).");
        Assert.IsNull(scene.Driver.Ghost,
            "Driver remains ghostless after vehicle-hold release. No driver-specific gate.");
    }

    [TestMethod]
    public void WalkingNpc_IsNotDeferred()
    {
        const int cbid = 650_410;
        const long coid = MapNpcIdentity.CoidBase + 81_010;
        AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, maxHitPoint: 40);

        var map = CreateFieldMap();
        var walker = new Creature { Position = new Vector3(20f, 0f, 0f), Level = 3 };
        walker.SetCoid(coid, true);
        walker.LoadCloneBase(cbid);
        walker.SetupCBFields();
        walker.SetMap(map);
        walker.CreateGhost();

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, observer, connection);

        Assert.IsNotNull(packets.OfType<CreateCreaturePacket>().SingleOrDefault(),
            "Walking NPC still gets a game CreateCreature.");
        Assert.IsNotNull(walker.Ghost.GetFirstObjectRef(),
            "Walking GhostCreature must stay immediate. FUN_005D2520 synth is valid without a vehicle.");
        Assert.AreEqual(0, packets.OfType<CreateVehiclePacket>().Count());
    }

    [TestMethod]
    public void ForeignPlayerCharacter_IsNotAffected()
    {
        const int vehicleCbid = 650_411;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var foreignChar = new Character { Position = new Vector3(25f, 0f, 0f) };
        foreignChar.SetCoid(MapNpcIdentity.CoidBase + 81_020, true);
        foreignChar.AttachTestDataForTests("ForeignPilot");
        foreignChar.CreateGhost();

        var foreignVeh = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
        foreignVeh.SetCoid(MapNpcIdentity.CoidBase + 81_021, true);
        foreignVeh.LoadCloneBase(vehicleCbid);
        foreignVeh.SetupCBFields();
        foreignChar.SetCurrentVehicleForTests(foreignVeh);
        foreignVeh.SetMap(map);
        foreignVeh.CreateGhost();
        foreignChar.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();
        TNLConnection.TestPacketSink = (_, _) => { };

        map.PerformScopeQuery(null, observer, connection);

        Assert.IsNotNull(foreignChar.Ghost.GetFirstObjectRef(),
            "Pass 8: GhostCharacter stays immediate. Do not reuse a mounted-entity hold for Creatures.");
        Assert.IsNull(foreignVeh.Ghost.GetFirstObjectRef(),
            "Foreign player vehicle hold is unchanged.");
    }

    [TestMethod]
    public void DriverLeaveDuringHold_DoesNotGhostLater()
    {
        var scene = ArrangeMountedNpcDriver();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef());
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.Vehicle.ObjectId.Coid));

        scene.Vehicle.SetMap(null);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "A vehicle that left mid-hold must not ObjectInScope after it is off the map.");
        Assert.IsNull(scene.Driver.Ghost,
            "No lingering driver GhostCreature gate — production driver never had a ghost.");
    }

    [TestMethod]
    public void DriverReenter_StartsFreshSafeSequence()
    {
        var scene = ArrangeMountedNpcDriver();
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 1500;

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(1, scene.Packets.OfType<CreateVehiclePacket>().Count());
        scene.Vehicle.SetMap(null);

        scene.Connection.DebugAgeForeignCreateHoldForTests(scene.Vehicle.ObjectId.Coid, 10_000);
        Assert.IsFalse(scene.Connection.HasActiveForeignCreateHold(scene.Vehicle.ObjectId.Coid),
            "Stale mid-hold must drop so re-entry can re-create.");

        scene.Vehicle.SetMap(scene.Map);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Count() >= 2,
            "Re-entry after leave mid-hold must send a new CreateVehicle.");
        Assert.IsTrue(scene.Packets.OfType<CreateCreaturePacket>().Count() >= 2,
            "Re-entry must emit a fresh driver CreateCreature(+0xF8).");
        Assert.AreEqual(scene.Vehicle.ObjectId.Coid,
            scene.Packets.OfType<CreateCreaturePacket>().Last().CoidCurrentVehicle);
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "New hold defers GhostVehicle again.");
        Assert.IsNull(scene.Driver.Ghost);
    }

    [TestMethod]
    public void VehicleDestroyedDuringHold_DoesNotDeadlockDriver()
    {
        var scene = ArrangeMountedNpcDriver(mapDriver: true);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.Driver.Ghost.GetFirstObjectRef(),
            "Hypothetically mapped driver is scoped immediately (no driver hold).");
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.Vehicle.ObjectId.Coid));

        scene.Vehicle.SetOwner(null);
        scene.Vehicle.SetMap(null);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNotNull(scene.Driver.Ghost.GetFirstObjectRef(),
            "Clearing the vehicle relationship must not suppress a walking creature Ghost.");
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void RepeatedScope_DoesNotResendCreatesEveryTick()
    {
        var scene = ArrangeMountedNpcDriver();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        var vehiclesAfterFirst = scene.Packets.OfType<CreateVehiclePacket>().Count();
        var driversAfterFirst = scene.Packets.OfType<CreateCreaturePacket>().Count();
        Assert.AreEqual(1, vehiclesAfterFirst);
        Assert.AreEqual(1, driversAfterFirst);
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.Vehicle.ObjectId.Coid));

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Count() <= vehiclesAfterFirst + 1,
            "At most the post-ghost nest re-apply; hold must not restart CreateVehicle every tick.");
        Assert.AreEqual(driversAfterFirst, scene.Packets.OfType<CreateCreaturePacket>().Count(),
            "Driver CreateCreature is not re-emitted every hold tick.");
        Assert.IsNotNull(scene.Vehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void ExistingGhostVehicleHold_RemainsUnchanged()
    {
        var savedMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        var savedQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        try
        {
            TNLConnection.ForeignGhostScopeHoldMilliseconds = 500;
            TNLConnection.ForeignGhostScopeHoldQueries = 1;
            var conn = new TNLConnection();
            const long coid = MapNpcIdentity.CoidBase + 81_099;
            conn.NoteForeignVehicleCreateSent(coid);
            Assert.IsTrue(conn.HasActiveForeignCreateHold(coid));
            Assert.IsFalse(conn.TryAllowForeignVehicleGhostScope(coid),
                "Pass 5 hold must remain: FUN_008078B0 applies ghosts before game packets. Do not shorten 500 ms.");
        }
        finally
        {
            TNLConnection.ForeignGhostScopeHoldMilliseconds = savedMs;
            TNLConnection.ForeignGhostScopeHoldQueries = savedQueries;
        }
    }

    [TestMethod]
    public void HypotheticallyMappedDriver_SameQuery_StillSendsVehicleThenDriverCreate()
    {
        var scene = ArrangeMountedNpcDriver(mapDriver: true);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var vehicleCreates = scene.Packets.OfType<CreateVehiclePacket>().ToList();
        var driverCreates = scene.Packets.OfType<CreateCreaturePacket>()
            .Where(p => p.ObjectId.Coid == scene.Driver.ObjectId.Coid)
            .ToList();
        Assert.AreEqual(1, vehicleCreates.Count);
        Assert.IsTrue(driverCreates.Count >= 1,
            "Mapped driver is also a foreign global Creature, so a CreateCreature is emitted.");
        Assert.IsTrue(driverCreates.Any(p => p.CoidCurrentVehicle == scene.Vehicle.ObjectId.Coid),
            "ForeignNpcDriverWire still places chassis COID at +0xF8.");
        Assert.IsNotNull(scene.Driver.Ghost.GetFirstObjectRef(),
            "No driver GhostCreature hold: same-window CreateVehicle still calls FUN_004c49d0. " +
            "Characters and Creatures stay distinct — Pass 8 did not hold GhostCharacter either.");
        Assert.IsNull(scene.Vehicle.Ghost.GetFirstObjectRef(),
            "GhostVehicle hold is independent of the driver Ghost.");
    }

    [TestMethod]
    public void RequestObjectVehicle_StillIncludesDriverCurrentVehicleCoid()
    {
        var scene = ArrangeMountedNpcDriver();
        scene.Connection.CurrentCharacter = scene.Observer;
        scene.Observer.SetMap(scene.Map);

        InvokeRequestObject(scene.Connection, scene.Vehicle.ObjectId.Coid, global: true);

        var driverCreate = scene.Packets.OfType<CreateCreaturePacket>()
            .SingleOrDefault(p => p.ObjectId.Coid == scene.Driver.ObjectId.Coid);
        Assert.IsNotNull(driverCreate,
            "Vehicle RequestObject must call ForeignNpcDriverWire.TrySendDriverCreate.");
        Assert.AreEqual(scene.Vehicle.ObjectId.Coid, driverCreate.CoidCurrentVehicle,
            "Recovered driver CreateCreature +0xF8 must stay the chassis COID.");
        Assert.AreEqual(CreateCreaturePacket.ClientVehicleCoidOffset, 0xF8);
    }

    [TestMethod]
    public void RequestObjectUnmappedDriver_DoesNotInventACreate()
    {
        var scene = ArrangeMountedNpcDriver();
        scene.Connection.CurrentCharacter = scene.Observer;
        scene.Observer.SetMap(scene.Map);

        InvokeRequestObject(scene.Connection, scene.Driver.ObjectId.Coid, global: true);

        Assert.AreEqual(0, scene.Packets.OfType<CreateCreaturePacket>().Count(),
            "Unmapped production driver is not in Map.Objects or ObjectManager; ResendObjectCreate must not invent a packet.");
    }

    private static Scene ArrangeMountedNpcDriver(bool mapDriver = false)
    {
        const int vehicleCbid = 650_400;
        const int driverCbid = 650_401;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 81_001;
        const long driverCoid = MapNpcIdentity.CoidBase + 81_002;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, maxHitPoint: 80);

        var map = CreateFieldMap();
        var driver = new Creature { Position = new Vector3(25f, 0f, 0f), Level = 5 };
        driver.SetCoid(driverCoid, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var vehicle = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
        vehicle.SetCoid(vehicleCoid, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetupCBFields();
        vehicle.SetOwner(driver);
        vehicle.SetMap(map);
        vehicle.CreateGhost();
        if (mapDriver)
        {
            driver.SetMap(map);
            driver.CreateGhost();
        }

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(MapNpcIdentity.CoidBase + 81_090, true);
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        return new Scene(map, observer, driver, vehicle, connection, packets);
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 708,
            MapFileName = "sec_f_h_map_tut_j2_arkbaytutorial",
            DisplayName = "NPC Driver Order Field",
            IsTown = false,
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

    private sealed record Scene(
        SectorMap Map,
        Character Observer,
        Creature Driver,
        Vehicle Vehicle,
        TNLConnection Connection,
        List<BasePacket> Packets);
}

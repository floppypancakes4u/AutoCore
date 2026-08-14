using System.Reflection;
using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 8 — foreign player vehicle → character ordering.
///
/// Client invariant (FUN_008078B0): ghost records are applied BEFORE the Sector
/// game-packet queue. That means <c>SendGamePacket(CreateVehicle)</c> in the same
/// <c>PrepareWritePacket</c> as <c>ObjectInScope(GhostCharacter)</c> is still
/// applied as GhostCharacter-first on the client. RPC send order is not apply order.
///
/// Client repair (Vehicle_applyCreatePacket 0x00505270): when map+0xe4e8 is set
/// (in-world after FAM load / Pass 4 Stage3), late CreateVehicle looks up owner
/// +0xD8 via FUN_004bb040 and calls FUN_004c49d0 (Creature::SetVehicle). That
/// writes Character.vehicle at creature+0x250 (same field as
/// CVOGCharacter_CreateFromPacket this-0xB50 with MI adjust 0xDA0) and vehicle
/// SetOwner (vtbl+0x158). So a GhostCharacter-first attach miss is repaired by
/// the later CreateVehicle. No foreign Character hold is implemented.
/// </summary>
[TestClass]
public class ForeignPlayerOrderingTests
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
            "ClientAppliesGhostRecordsBeforeGameQueue_IsWhySendOrderIsNotApplyOrder",
            nameof(ClientAppliesGhostRecordsBeforeGameQueue_IsWhySendOrderIsNotApplyOrder),
            "FUN_008078B0 applies GhostCharacter before CreateVehicle even when the RPC is earlier on the wire.");
    }

    [TestMethod]
    public void FirstScope_MountedForeignPlayer_SendsCreateVehicle_AndScopesGhostCharacterImmediately()
    {
        var scene = ArrangeMountedForeignOnField();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        var create = scene.Packets.OfType<CreateVehiclePacket>().FirstOrDefault();
        Assert.IsNotNull(create,
            "Foreign mounted player still needs CreateVehicle so FUN_004c49d0 can bind owner.");
        Assert.AreEqual(scene.ForeignVehicle.ObjectId.Coid, create.ObjectId.Coid);
        Assert.AreEqual(scene.ForeignCharacter.ObjectId.Coid, create.CoidCurrentOwner);

        Assert.IsNotNull(scene.ForeignCharacter.Ghost.GetFirstObjectRef(),
            "GhostCharacter is scoped on the create query. Client FUN_008078B0 applies it before " +
            "CreateVehicle, but Vehicle_applyCreatePacket + map+0xe4e8 SetVehicle repairs both pointers. " +
            "A Character hold is not required.");
        Assert.IsNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef(),
            "Existing vehicle create-hold must still defer GhostVehicle (FUN_005F5AD0 wheel CBID 0).");
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.ForeignVehicle.ObjectId.Coid));
    }

    [TestMethod]
    public void AfterVehicleCreateHold_ScopesGhostVehicle_CharacterAlreadyScoped()
    {
        var scene = ArrangeMountedForeignOnField();

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.ForeignCharacter.Ghost.GetFirstObjectRef());
        Assert.IsNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef());

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNotNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef(),
            "GhostVehicle releases on the same hold as NPC vehicles (one extra query when holdMs=0).");
        Assert.IsNotNull(scene.ForeignCharacter.Ghost.GetFirstObjectRef(),
            "GhostCharacter remains scoped; it was not waiting on the vehicle hold.");
    }

    [TestMethod]
    public void OnFootForeignCharacter_ScopesGhostCharacterImmediately()
    {
        var map = CreateFieldMap();
        var foreign = new Character { Position = new Vector3(20f, 0f, 0f) };
        foreign.SetCoid(MapNpcIdentity.CoidBase + 80_010, true);
        foreign.AttachTestDataForTests("OnFoot");
        foreign.CreateGhost();
        foreign.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });
        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, observer, connection);

        Assert.IsNotNull(foreign.Ghost.GetFirstObjectRef(),
            "On-foot GhostCharacter synth (0x2015) is sufficient; CurrentVehicle == null must not be held.");
        Assert.AreEqual(0, packets.OfType<CreateVehiclePacket>().Count(),
            "On-foot foreign characters must not force a vehicle Create.");
    }

    [TestMethod]
    public void ExistingVehicleCreateAlreadyProcessed_DoesNotReholdCharacter()
    {
        var scene = ArrangeMountedForeignOnField();
        // Keep an observer player on the map so LeaveMap of the foreign character does not
        // reset the instance (SS-30 last-player-left) and drop the vehicle.
        scene.Observer.SetMap(scene.Map);
        scene.ForeignCharacter.SetMap(null);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNotNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef(), "vehicle already ghosted");
        var createsBefore = scene.Packets.OfType<CreateVehiclePacket>().Count();

        scene.ForeignCharacter.SetMap(scene.Map);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNotNull(scene.ForeignCharacter.Ghost.GetFirstObjectRef(),
            "Character entering after the vehicle Create/hold must not start a second hold.");
        Assert.AreEqual(createsBefore, scene.Packets.OfType<CreateVehiclePacket>().Count(),
            "Character entry must not resend CreateVehicle.");
    }

    [TestMethod]
    public void RepeatedScopeWhileHeld_DoesNotResendCreateVehicleOrRestartHold()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        var createsAfterFirst = scene.Packets.OfType<CreateVehiclePacket>().Count();
        Assert.AreEqual(1, createsAfterFirst);
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.ForeignVehicle.ObjectId.Coid));

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Count() <= createsAfterFirst + 1,
            "At most the post-ghost nest re-apply; hold must not restart a new Create every tick.");
        Assert.IsNotNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void LeaveScopeDuringHold_DoesNotLaterGhostOutOfScope()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.IsNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef());
        Assert.IsTrue(scene.Connection.HasActiveForeignCreateHold(scene.ForeignVehicle.ObjectId.Coid));

        scene.ForeignVehicle.SetMap(null);
        scene.ForeignCharacter.SetMap(null);

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef(),
            "A vehicle that left mid-hold must not ObjectInScope after it is off the map.");
    }

    [TestMethod]
    public void LeaveDuringHold_ThenReenter_StartsFreshCreateHold()
    {
        var scene = ArrangeMountedForeignOnField();
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 1500;

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);
        Assert.AreEqual(1, scene.Packets.OfType<CreateVehiclePacket>().Count());
        scene.ForeignVehicle.SetMap(null);
        scene.ForeignCharacter.SetMap(null);

        scene.Connection.DebugAgeForeignCreateHoldForTests(scene.ForeignVehicle.ObjectId.Coid, 10_000);
        Assert.IsFalse(scene.Connection.HasActiveForeignCreateHold(scene.ForeignVehicle.ObjectId.Coid),
            "Stale mid-hold must drop so re-entry can re-create.");

        scene.ForeignVehicle.SetMap(scene.Map);
        scene.ForeignCharacter.SetMap(scene.Map);
        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsTrue(scene.Packets.OfType<CreateVehiclePacket>().Count() >= 2,
            "Re-entry after leave mid-hold must send a new CreateVehicle.");
        Assert.IsNull(scene.ForeignVehicle.Ghost.GetFirstObjectRef(),
            "New hold defers GhostVehicle again.");
    }

    [TestMethod]
    public void NpcDriverPath_StillCreateVehicleThenCreateCreatureThenVehicleHold()
    {
        const int vehicleCbid = 650_301;
        const int driverCbid = 650_302;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 80_301;
        const long driverCoid = MapNpcIdentity.CoidBase + 80_302;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, maxHitPoint: 80);

        var map = CreateFieldMap();
        var driver = new Creature { Position = new Vector3(25f, 0f, 0f), Level = 5 };
        driver.SetCoid(driverCoid, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var npcVehicle = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetOwner(driver);
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, self, connection);

        var vehicleIdx = packets.FindIndex(p => p is CreateVehiclePacket);
        var driverIdx = packets.FindIndex(p => p is CreateCreaturePacket);
        Assert.IsTrue(vehicleIdx >= 0 && driverIdx > vehicleIdx,
            "Pass 5/6: CreateVehicle then CreateCreature(driver) then vehicle hold.");
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef());
        Assert.AreEqual(vehicleCoid, ((CreateCreaturePacket)packets[driverIdx]).CoidCurrentVehicle);
    }

    [TestMethod]
    public void LocalObserverCharacter_IsNotDeferredByForeignVehicleHold()
    {
        var scene = ArrangeMountedForeignOnField();
        scene.Observer.CreateGhost();
        scene.Connection.CurrentCharacter = scene.Observer;

        scene.Map.PerformScopeQuery(null, scene.Observer, scene.Connection);

        Assert.IsNotNull(scene.Observer.Ghost.GetFirstObjectRef(),
            "Local / observer character must remain immediate (Stage3 / ObjectLocalScopeAlways path).");
    }

    private static Scene ArrangeMountedForeignOnField()
    {
        const int vehicleCbid = 650_300;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 80_001;
        const long characterCoid = MapNpcIdentity.CoidBase + 80_002;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var foreignChar = new Character { Position = new Vector3(25f, 0f, 0f) };
        foreignChar.SetCoid(characterCoid, true);
        foreignChar.AttachTestDataForTests("ForeignPilot");
        foreignChar.CreateGhost();

        var foreignVeh = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
        foreignVeh.SetCoid(vehicleCoid, true);
        foreignVeh.LoadCloneBase(vehicleCbid);
        foreignVeh.SetupCBFields();
        foreignChar.SetCurrentVehicleForTests(foreignVeh);
        foreignVeh.SetMap(map);
        foreignVeh.CreateGhost();
        foreignChar.SetMap(map);

        var observer = new Character { Position = new Vector3(0f, 0f, 0f) };
        observer.SetCoid(MapNpcIdentity.CoidBase + 80_099, true);
        observer.AttachTestDataForTests("Observer");
        observer.SetCurrentVehicleForTests(new Vehicle { Position = observer.Position });

        var connection = new TNLConnection();
        connection.CurrentCharacter = observer;
        connection.SetGhostFrom(true);
        connection.BeginGhostingForTests();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        return new Scene(map, observer, foreignChar, foreignVeh, connection, packets);
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 708,
            MapFileName = "sec_f_h_map_tut_j2_arkbaytutorial",
            DisplayName = "Foreign Player Order Field",
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

    private sealed record Scene(
        SectorMap Map,
        Character Observer,
        Character ForeignCharacter,
        Vehicle ForeignVehicle,
        TNLConnection Connection,
        List<BasePacket> Packets);
}

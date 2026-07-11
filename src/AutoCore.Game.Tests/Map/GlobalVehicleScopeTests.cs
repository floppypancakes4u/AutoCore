using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using System.Reflection;

[TestClass]
public class GlobalVehicleScopeTests
{
    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ResetScopeLevers();
        // Deterministic tests: two post-create scope queries, no wall-clock wait.
        TNLConnection.ForeignGhostScopeHoldQueries = 2;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForceForeignCreateReapply = false;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        ResetScopeLevers();
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    private static void ResetScopeLevers()
    {
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        SectorMap.SendGroupReactionCall = true;
    }

    [TestMethod]
    public void PerformScopeQuery_FirstGlobalNpcVehicle_SendsCreateBeforeRegisteringGhost()
    {
        const int vehicleCbid = 650_001;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 18_134;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle
        {
            Position = new Vector3(25f, 2.349889f, 0f),
            Rotation = new Quaternion(0f, 0.00005f, 0f, 1f),
        };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        // Retail client FUN_008078b0 processes ghost object-create BEFORE game packet queues.
        // Hold ObjectInScope for ForeignGhostScopeHoldQueries further scope passes after create.
        map.PerformScopeQuery(null, self, connection);

        var create = packets.OfType<CreateVehiclePacket>().SingleOrDefault();
        Assert.IsNotNull(create, "A global map vehicle needs an external create before its first ghost update");
        Assert.AreEqual(vehicleCoid, create.ObjectId.Coid);
        Assert.IsTrue(create.ObjectId.Global);
        Assert.AreEqual(npcVehicle.Position, create.Position);
        Assert.AreEqual(npcVehicle.Rotation, create.Rotation);
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "Create query must not ObjectInScope");

        map.PerformScopeQuery(null, self, connection);
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "HoldQueries=2: first post-create query still defers ghost");

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count(),
            "Re-scoping must not duplicate CreateVehicle");
        Assert.IsNotNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "Ghost scopes only after hold queries elapse");
    }

    [TestMethod]
    public void PerformScopeQuery_ForceForeignCreateReapply_SetsIsItemLinkOnWire()
    {
        const int vehicleCbid = 650_011;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 18_150;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        TNLConnection.ForceForeignCreateReapply = true;

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle { Position = new Vector3(10f, 0f, 0f) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, self, connection);

        var create = packets.OfType<CreateVehiclePacket>().Single();
        Assert.IsTrue(create.IsItemLink,
            "ForceForeignCreateReapply maps to client packet+0xA1 so late create re-applies after ghost race");
    }

    [TestMethod]
    public void PerformScopeQuery_DropThenReScope_DoesNotResendCreate()
    {
        const int vehicleCbid = 650_004;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 18_140;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle
        {
            Position = new Vector3(25f, 2.349889f, 0f),
            Rotation = new Quaternion(0f, 0.00005f, 0f, 1f),
        };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        // Create, then hold queries, then ghost.
        map.PerformScopeQuery(null, self, connection);
        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count(), "first scope sends the create");
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef(), "ghost deferred on create query");

        map.PerformScopeQuery(null, self, connection);
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef(), "still held on first post-create query");

        map.PerformScopeQuery(null, self, connection);
        var ghostInfo = npcVehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "vehicle ghost must be scoped after hold queries");

        // Player drives past the drop radius: TNL kills the ghost (no DestroyObject is ever sent, so
        // the client still holds the created object). IsGhostedTo now reports not-ghosted.
        connection.DetachObject(ghostInfo);
        Assert.IsFalse(npcVehicle.Ghost.IsGhostedTo(connection),
            "detaching must drop the ghost so the re-scope path is exercised");

        // Player returns into scope: the ghost re-registers, but the create must NOT be re-sent —
        // the client still holds the object from the first create (duplicate-create regression).
        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count(),
            "a drop/re-add scope cycle must not deliver a duplicate CreateVehicle for an object the client still holds");
        Assert.IsNotNull(npcVehicle.Ghost.GetFirstObjectRef(), "ghost must be re-scoped after re-entry");
    }

    [TestMethod]
    public void PerformScopeQuery_AfterMapTransfer_ResendsCreateForNewMapSession()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();

        map.PerformScopeQuery(null, self, connection);
        Assert.AreEqual(1, packets.OfType<CreateVehiclePacket>().Count(), "first session sends the create");

        // Map transfer tears down ghosting; the client discards its local object table, so the next
        // map session legitimately needs the create again.
        connection.ResetGhosting();
        connection.EnsureGhostsAndScopeAfterMapTransfer(self);

        map.PerformScopeQuery(null, self, connection);
        Assert.AreEqual(2, packets.OfType<CreateVehiclePacket>().Count(),
            "a fresh map session must clear the sent-create tracking so the create is re-sent");
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehiclesFalse_SkipsCreateAndGhost()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();
        SectorMap.ScopeGlobalVehicles = false;

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, packets.OfType<CreateVehiclePacket>().Count());
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef());
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehicleCreateFalse_NoCreateButStillGhosts()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();
        SectorMap.ScopeGlobalVehicleCreate = false;

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, packets.OfType<CreateVehiclePacket>().Count());
        Assert.IsNotNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "Ghost lever still on — ObjectInScope should run (probe only; live client may die without create)");
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehicleGhostFalse_CreateWithoutGhost()
    {
        var (map, npcVehicle, self, connection, packets) = ArrangeScopedNpc();
        SectorMap.ScopeGlobalVehicleGhost = false;

        map.PerformScopeQuery(null, self, connection);

        Assert.IsNotNull(packets.OfType<CreateVehiclePacket>().SingleOrDefault());
        Assert.IsNull(npcVehicle.Ghost.GetFirstObjectRef(),
            "Ghost lever off must skip ObjectInScope after create");
    }

    [TestMethod]
    public void IsLocalPlayerVehicle_TrueOnlyForCurrentVehicle()
    {
        var local = new Vehicle();
        local.SetCoid(1, true);
        var other = new Vehicle();
        other.SetCoid(2, true);
        var self = new Character();
        self.SetCurrentVehicleForTests(local);

        Assert.IsTrue(SectorMap.IsLocalPlayerVehicle(local, self));
        Assert.IsFalse(SectorMap.IsLocalPlayerVehicle(other, self));
        Assert.IsFalse(SectorMap.IsLocalPlayerVehicle(local, null));
        Assert.IsFalse(SectorMap.IsLocalPlayerVehicle(null, self));
    }

    [TestMethod]
    public void PerformScopeQuery_ScopeGlobalVehiclesFalse_DoesNotScopeLocalPlayerVehicle()
    {
        const int vehicleCbid = 650_003;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var localVehicle = new Vehicle { Position = new Vector3(0, 0, 0) };
        localVehicle.SetCoid(MapNpcIdentity.CoidBase + 99, true);
        localVehicle.LoadCloneBase(vehicleCbid);
        localVehicle.SetupCBFields();
        localVehicle.SetMap(map);
        localVehicle.CreateGhost();

        var npc = new Vehicle { Position = new Vector3(10, 0, 0) };
        npc.SetCoid(MapNpcIdentity.CoidBase + 100, true);
        npc.LoadCloneBase(vehicleCbid);
        npc.SetupCBFields();
        npc.SetMap(map);
        npc.CreateGhost();

        var self = new Character { Position = new Vector3(0, 0, 0) };
        self.SetCurrentVehicleForTests(localVehicle);

        // Put both into selected scope buffer
        var selected = (List<ClonedObjectBase>)typeof(SectorMap)
            .GetField("_scopeSelected", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(map)!;
        selected.Add(localVehicle);
        selected.Add(npc);

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        SectorMap.ScopeGlobalVehicles = false;
        map.PerformScopeQuery(null, self, connection);

        Assert.IsNull(localVehicle.Ghost.GetFirstObjectRef(),
            "CreateVehicleExtended owns the local vehicle state; GhostVehicle must never overwrite it.");
        Assert.IsNull(npc.Ghost.GetFirstObjectRef(), "Foreign NPC must be skipped");
    }

    private static (SectorMap Map, Vehicle Npc, Character Self, TNLConnection Connection, List<BasePacket> Packets)
        ArrangeScopedNpc()
    {
        const int vehicleCbid = 650_002;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 18_135;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle
        {
            Position = new Vector3(25f, 2.349889f, 0f),
            Rotation = new Quaternion(0f, 0.00005f, 0f, 1f),
        };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();

        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        return (map, npcVehicle, self, connection, packets);
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 707,
            MapFileName = "sec_f_h_map_tut_j2_arkbaytutorial",
            DisplayName = "Hestia Ark Bay 313",
            IsTown = false,
        };

        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        InitializeScopeBuffer(map, "_scopeNearby");
        InitializeScopeBuffer(map, "_scopeMissionGivers");
        InitializeScopeBuffer(map, "_scopeSelected");
        return map;
    }

    private static void InitializeScopeBuffer(SectorMap map, string fieldName)
    {
        typeof(SectorMap).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, new List<ClonedObjectBase>());
    }
}

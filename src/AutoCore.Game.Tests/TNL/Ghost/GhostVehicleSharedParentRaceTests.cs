using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using global::TNL.Entities;
using global::TNL.Structures;
using global::TNL.Utils;

/// <summary>
/// Regression coverage for the "Missing parent for GhostVehicle!" crash that only reproduces with
/// two or more connections ghosting the same shared <see cref="GhostVehicle"/> at once. A single
/// <see cref="ClonedObjectBase"/> has exactly one <see cref="GhostObject"/> which can be tracked by
/// multiple <see cref="GhostConnection"/>s simultaneously; <see cref="ClonedObjectBase.ClearGhost"/>
/// must not sever that shared <c>Parent</c> link while another connection still expects to
/// <c>PackUpdate</c> it. See also the still-pinned-forever companion defect covered below
/// (<see cref="GhostConnection.ObjectLocalScopeAlways"/> without a matching
/// <see cref="GhostConnection.ObjectLocalClearAlways"/> call for path vehicles that stop qualifying).
/// </summary>
[TestClass]
public class GhostVehicleSharedParentRaceTests
{
    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        // Deterministic tests: two post-create TryAllow calls, no wall-clock wait.
        TNLConnection.ForeignGhostScopeHoldQueries = 2;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 0;
        TNLConnection.ForceForeignCreateReapply = false;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NetObject.PIsInitialUpdate = false;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    #region ClearGhost shared-Parent race

    [TestMethod]
    public void ClearGhost_WhileForeignConnectionStillGhosted_KeepsParentAlive_NoThrowOnPack()
    {
        // One Vehicle, two independent connections both holding its GhostVehicle in scope —
        // mirrors "own connection" A whose player is about to disconnect, and "foreign observer" B
        // (e.g. a second player still watching this vehicle as a foreign object).
        var vehicle = new Vehicle();
        vehicle.SetCoid(30_001, true);
        vehicle.CreateGhost();
        var ghost = vehicle.Ghost;

        var connA = new TNLConnection();
        connA.SetGhostFrom(true);
        connA.SetGhostTo(false);
        connA.ActivateGhosting();
        var connB = new TNLConnection();
        connB.SetGhostFrom(true);
        connB.SetGhostTo(false);
        connB.ActivateGhosting();

        connA.ObjectInScope(ghost);
        connB.ObjectInScope(ghost);

        // Act: A's owning character disconnects — this is the exact call EndCharacterSession makes.
        vehicle.ClearGhost();

        Assert.IsNull(vehicle.Ghost,
            "the entity's own Ghost reference should still drop so a future CreateGhost() starts fresh");
        Assert.IsNotNull(GetParent(ghost),
            "Parent must survive while another connection (B) still ghosts this exact instance");

        // B's next PackUpdate must not throw.
        var stream = new BitStream(new byte[256], 256);
        NetObject.PIsInitialUpdate = false;
        ghost.PackUpdate(connB, GhostObject.PositionMask, stream);
    }

    [TestMethod]
    public void ClearGhost_NoOtherConnectionGhosting_NullsParentImmediately()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(30_002, true);
        vehicle.CreateGhost();
        var ghost = vehicle.Ghost;

        vehicle.ClearGhost();

        Assert.IsNull(vehicle.Ghost);
        Assert.IsNull(GetParent(ghost),
            "with no other connection observing it, ClearGhost must still null Parent immediately");
    }

    private static ClonedObjectBase GetParent(GhostObject ghost) =>
        (ClonedObjectBase)typeof(GhostObject)
            .GetProperty("Parent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(ghost);

    #endregion

    #region ScopeLocalAlways unpin

    [TestMethod]
    public void PerformScopeQuery_PathVehicleStopsQualifying_UnpinsScopeLocalAlways()
    {
        var (map, npcVehicle, self, connection) = ArrangePathVehicle();
        npcVehicle.CoidCurrentPath = 42;

        // Drive through create-hold + ghost-hold queries so the vehicle is actually ghosted (and,
        // being a path vehicle, pinned) — ForeignGhostScopeHoldQueries = 2 per SetUp.
        map.PerformScopeQuery(null, self, connection); // create
        map.PerformScopeQuery(null, self, connection); // hold query 1
        map.PerformScopeQuery(null, self, connection); // ghost + pin

        var coid = npcVehicle.ObjectId.Coid;
        var ghostInfo = npcVehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "vehicle must be ghosted before a pin can apply");
        Assert.AreNotEqual(0u, ghostInfo.Flags & (uint)GhostInfoFlags.ScopeLocalAlways,
            "pathing foreign vehicle must be pinned into permanent scope");
        Assert.IsTrue(connection.PinnedPathVehicles.ContainsKey(coid));

        // Path ends: the vehicle no longer qualifies for pinning.
        npcVehicle.CoidCurrentPath = 0;
        map.PerformScopeQuery(null, self, connection);

        Assert.IsFalse(connection.PinnedPathVehicles.ContainsKey(coid),
            "the unpin sweep must forget the coid once it stops qualifying");
        var ghostInfoAfter = npcVehicle.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfoAfter);
        Assert.AreEqual(0u, ghostInfoAfter.Flags & (uint)GhostInfoFlags.ScopeLocalAlways,
            "ObjectLocalClearAlways must clear the pin bit so TNL's normal InScope-clearing/DetachObject flow can reclaim it");
    }

    private static (SectorMap Map, Vehicle Npc, Character Self, TNLConnection Connection) ArrangePathVehicle()
    {
        const int vehicleCbid = 650_500;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 30_500;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var continent = new ContinentObject
        {
            Id = 708,
            MapFileName = "tm_gv_pin_708",
            DisplayName = "test",
            IsTown = false,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        InitializeScopeBuffer(map, "_scopeNearby");
        InitializeScopeBuffer(map, "_scopeMissionGivers");
        InitializeScopeBuffer(map, "_scopeSelected");

        var npcVehicle = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
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

        return (map, npcVehicle, self, connection);
    }

    private static void InitializeScopeBuffer(SectorMap map, string fieldName)
    {
        typeof(SectorMap).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, new List<ClonedObjectBase>());
    }

    #endregion
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.Map;
using AutoCore.Game.TNL;

/// <summary>
/// Target-frame Cur/Max for NPC vehicles requires client <c>vehicle+0xAC</c> (driver) set via
/// <c>CreateVehiclePacket.CoidCurrentOwner</c> → <c>Creature::SetVehicle</c>. Attach has no retry
/// if the driver is missing when CreateVehicle first applies. IsItemLink re-apply causes tooltips
/// and does not fix numbers; recovery is delayed DestroyObject + CreateVehicle (IsItemLink=0).
/// See NPC.md §14.4.
/// </summary>
[TestClass]
public class ForeignOwnerAttachReapplyTests
{
    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    [TestMethod]
    public void Schedule_ThenConsume_BeforeDelay_ReturnsFalse()
    {
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 10_000;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 40_001;

        conn.ScheduleForeignOwnerAttachReapply(coid);
        Assert.IsFalse(conn.TryConsumeForeignOwnerAttachReapply(coid),
            "Must not fire before the delay elapses.");
        Assert.IsTrue(conn.HasPendingForeignOwnerAttachReapplyForTests(coid),
            "Pending entry remains until the delay expires.");
    }

    [TestMethod]
    public void Schedule_ThenConsume_AfterDelay_ReturnsTrueOnce()
    {
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 50;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 40_002;

        conn.ScheduleForeignOwnerAttachReapply(coid);
        conn.DebugAgeForeignOwnerAttachReapplyForTests(coid, 100);

        Assert.IsTrue(conn.TryConsumeForeignOwnerAttachReapply(coid),
            "After delay, first consume fires the CreateVehicle re-send.");
        Assert.IsFalse(conn.TryConsumeForeignOwnerAttachReapply(coid),
            "One-shot: second consume must not re-fire.");
        Assert.IsFalse(conn.HasPendingForeignOwnerAttachReapplyForTests(coid));
    }

    [TestMethod]
    public void Schedule_Overwrite_KeepsLatestDeadline()
    {
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 5_000;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 40_003;

        conn.ScheduleForeignOwnerAttachReapply(coid);
        conn.DebugAgeForeignOwnerAttachReapplyForTests(coid, 4_000);
        // Re-schedule extends/replaces — not yet due under full delay from now.
        conn.ScheduleForeignOwnerAttachReapply(coid);
        Assert.IsFalse(conn.TryConsumeForeignOwnerAttachReapply(coid),
            "Fresh schedule restarts the delay window.");
    }

    [TestMethod]
    public void ClearGlobalVehicleCreateTracking_DropsPendingOwnerAttach()
    {
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 10_000;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 40_004;

        conn.ScheduleForeignOwnerAttachReapply(coid);
        conn.ClearGlobalVehicleCreateTracking();
        Assert.IsFalse(conn.HasPendingForeignOwnerAttachReapplyForTests(coid));
        Assert.IsFalse(conn.TryConsumeForeignOwnerAttachReapply(coid));
    }

    [TestMethod]
    public void ShouldScheduleForeignOwnerAttachReapply_RequiresCreatureOwner()
    {
        Assert.IsFalse(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(null, hasCreatureOwner: false));
        Assert.IsFalse(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(null, hasCreatureOwner: true),
            "Null connection never schedules.");
        var conn = new TNLConnection();
        Assert.IsFalse(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(conn, hasCreatureOwner: false));
        Assert.IsTrue(TNLConnection.ShouldScheduleForeignOwnerAttachReapply(conn, hasCreatureOwner: true));
    }

    [TestMethod]
    public void Schedule_ZeroDelay_IsImmediatelyConsumable()
    {
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 0;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 40_005;
        conn.ScheduleForeignOwnerAttachReapply(coid);
        Assert.IsTrue(conn.TryConsumeForeignOwnerAttachReapply(coid));
        Assert.IsFalse(conn.TryConsumeForeignOwnerAttachReapply(coid));
    }

    [TestMethod]
    public void TryConsume_UnknownCoid_ReturnsFalse()
    {
        var conn = new TNLConnection();
        Assert.IsFalse(conn.TryConsumeForeignOwnerAttachReapply(MapNpcIdentity.CoidBase + 40_999));
    }

    [TestMethod]
    public void DebugAge_UnknownCoid_IsNoOp()
    {
        var conn = new TNLConnection();
        conn.DebugAgeForeignOwnerAttachReapplyForTests(MapNpcIdentity.CoidBase + 40_998, 5000);
        Assert.IsFalse(conn.HasPendingForeignOwnerAttachReapplyForTests(MapNpcIdentity.CoidBase + 40_998));
    }

    [TestMethod]
    public void ClearGlobalVehicleCreateTracking_AfterConsume_StaysClear()
    {
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 10;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 40_006;
        conn.ScheduleForeignOwnerAttachReapply(coid);
        conn.DebugAgeForeignOwnerAttachReapplyForTests(coid, 100);
        Assert.IsTrue(conn.TryConsumeForeignOwnerAttachReapply(coid));
        conn.ClearGlobalVehicleCreateTracking();
        Assert.IsFalse(conn.HasPendingForeignOwnerAttachReapplyForTests(coid));
    }
}

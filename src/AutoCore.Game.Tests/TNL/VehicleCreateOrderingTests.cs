using AutoCore.Game.Map;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// PDB Pass 5 sequence locks.
/// Local: Client_CreateVehicleObjectApply then Client_RecvCreateCharacter; ActivateGhosting
/// after Creates (Pass 4). Foreign: FUN_008078B0 applies ghosts before game packets, and
/// FUN_005F5AD0 synthesizes a CreateVehicle with wheel CBID 0 — so GhostVehicle must not
/// beat CreateVehicle.
/// </summary>
[TestClass]
public class VehicleCreateOrderingTests
{
    private int _savedHoldMs;
    private int _savedHoldQueries;
    private int _savedStaleMs;

    [TestInitialize]
    public void Init()
    {
        _savedHoldMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        _savedHoldQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        _savedStaleMs = TNLConnection.ForeignCreateHoldStaleGraceMilliseconds;
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.ForeignGhostScopeHoldMilliseconds = _savedHoldMs;
        TNLConnection.ForeignGhostScopeHoldQueries = _savedHoldQueries;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = _savedStaleMs;
    }

    [TestMethod]
    public void ForeignHold_BlocksGhostScopeUntilCreateHasHadAPump()
    {
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 500;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 15000;

        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 60_001;

        conn.NoteForeignVehicleCreateSent(coid);

        Assert.IsTrue(conn.HasActiveForeignCreateHold(coid),
            "CreateVehicle send must open the hold before ObjectInScope.");
        Assert.IsFalse(conn.TryAllowForeignVehicleGhostScope(coid),
            "Same-tick / first post-create query must not ObjectInScope: FUN_008078B0 drains ghosts first.");
    }

    [TestMethod]
    public void ForeignHold_ReleasesAfterRequiredQueryAndElapsedMs()
    {
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForeignGhostScopeHoldQueries = 1;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 15000;

        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 60_002;
        conn.NoteForeignVehicleCreateSent(coid);

        Assert.IsTrue(conn.TryAllowForeignVehicleGhostScope(coid),
            "With holdMs=0, the first counted query is the required extra pump.");
    }

    [TestMethod]
    public void UnknownCoid_IsNotHeld()
    {
        var conn = new TNLConnection();
        Assert.IsTrue(conn.TryAllowForeignVehicleGhostScope(MapNpcIdentity.CoidBase + 60_003),
            "Coids that never received CreateVehicle are allowed immediately (create lever off).");
    }
}

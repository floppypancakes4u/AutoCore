using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Database.World.Models;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>P2: first foreign ghost without owner, then descope/rescope for owner initial.</summary>
[TestClass]
public class ForeignReghostOwnerTests
{
    [TestCleanup]
    public void TearDown()
    {
        GhostVehicle.EnableForeignReghostOwner = false;
        GhostVehicle.EnableMinimalForeignInitialProfile = false;
        GhostVehicle.EnableMinimalForeignOwnerBlock = false;
        GhostVehicle.EnableDeferredForeignPose = false;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
        NetObject.PIsInitialUpdate = false;
        WireDiag.ResetForTests();
    }

    [TestMethod]
    public void Reghost_StateMachine_SuppressesThenDescopesThenAllowsOwner()
    {
        GhostVehicle.EnableForeignReghostOwner = true;
        TNLConnection.ForeignReghostDelayMilliseconds = 100;
        var conn = new TNLConnection();
        const long coid = MapNpcIdentity.CoidBase + 30_001;

        Assert.IsTrue(conn.ShouldSuppressForeignOwnerOnPack(coid),
            "Before first scope, suppress owner (no Rescoped phase yet).");
        Assert.IsFalse(conn.ShouldSkipForeignObjectInScopeForReghost(coid));

        conn.NoteForeignVehicleGhostScoped(coid);
        Assert.AreEqual(TNLConnection.ForeignReghostPhase.FirstScopedNoOwner,
            conn.GetForeignReghostPhaseForTests(coid));
        Assert.IsTrue(conn.ShouldSuppressForeignOwnerOnPack(coid));

        Assert.IsFalse(conn.ShouldSkipForeignObjectInScopeForReghost(coid),
            "Delay not elapsed — stay in scope.");

        conn.DebugAgeForeignReghostFirstScopeForTests(coid, 200);
        Assert.IsTrue(conn.ShouldSkipForeignObjectInScopeForReghost(coid),
            "After delay, skip ObjectInScope once (descope).");
        Assert.AreEqual(TNLConnection.ForeignReghostPhase.Descoped,
            conn.GetForeignReghostPhaseForTests(coid));
        Assert.IsFalse(conn.ShouldSkipForeignObjectInScopeForReghost(coid),
            "Only one descope skip.");

        conn.NoteForeignVehicleGhostScoped(coid);
        Assert.AreEqual(TNLConnection.ForeignReghostPhase.RescopedWithOwner,
            conn.GetForeignReghostPhaseForTests(coid));
        Assert.IsFalse(conn.ShouldSuppressForeignOwnerOnPack(coid),
            "Second initial may pack owner.");
    }

    [TestMethod]
    public void PackInitial_ForeignReghost_FirstScope_OmitsOwnerDespiteOwnerLever()
    {
        var vehicle = CreateForeignVehicle(MapNpcIdentity.CoidBase + 30_010);
        var driver = new Creature();
        driver.SetCoid(MapNpcIdentity.CoidBase + 30_011, true);
        vehicle.SetOwner(driver);

        GhostVehicle.EnableForeignReghostOwner = true;
        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        GhostVehicle.EnableMinimalForeignOwnerBlock = true;

        var conn = new TNLConnection();
        // First scope note without rescope → suppress
        conn.NoteForeignVehicleGhostScoped(vehicle.ObjectId.Coid);

        var stream = new BitStream(new byte[8192], 8192);
        NetObject.PIsInitialUpdate = true;
        try
        {
            vehicle.Ghost.PackUpdate(conn, ulong.MaxValue, stream);
        }
        finally
        {
            NetObject.PIsInitialUpdate = false;
        }

        stream.SetBitPosition(0);
        SkipToOwnerFlag(stream);
        Assert.IsFalse(stream.ReadFlag(), "First reghost scope must withhold owner");
    }

    [TestMethod]
    public void PackInitial_ForeignReghost_AfterRescope_PacksOwner()
    {
        var vehicle = CreateForeignVehicle(MapNpcIdentity.CoidBase + 30_020);
        var driver = new Creature();
        driver.SetCoid(MapNpcIdentity.CoidBase + 30_021, true);
        driver.Level = 4;
        vehicle.SetOwner(driver);

        GhostVehicle.EnableForeignReghostOwner = true;
        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        GhostVehicle.EnableMinimalForeignOwnerBlock = true;

        var conn = new TNLConnection();
        conn.NoteForeignVehicleGhostScoped(vehicle.ObjectId.Coid);
        conn.DebugAgeForeignReghostFirstScopeForTests(vehicle.ObjectId.Coid, 10_000);
        Assert.IsTrue(conn.ShouldSkipForeignObjectInScopeForReghost(vehicle.ObjectId.Coid));
        conn.NoteForeignVehicleGhostScoped(vehicle.ObjectId.Coid);

        var stream = new BitStream(new byte[8192], 8192);
        NetObject.PIsInitialUpdate = true;
        try
        {
            vehicle.Ghost.PackUpdate(conn, ulong.MaxValue, stream);
        }
        finally
        {
            NetObject.PIsInitialUpdate = false;
        }

        stream.SetBitPosition(0);
        SkipToOwnerFlag(stream);
        Assert.IsTrue(stream.ReadFlag(), "Rescoped initial must pack owner");
        stream.Read(out long ownerCoid);
        Assert.AreEqual(driver.ObjectId.Coid, ownerCoid);
    }

    private static Vehicle CreateForeignVehicle(long coid)
    {
        var continent = new ContinentObject
        {
            Id = unchecked((int)(coid & 0x7FFF)),
            MapFileName = "tm_test",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.SetMap(map);
        vehicle.CreateGhost();
        return vehicle;
    }

    private static void SkipToOwnerFlag(BitStream stream)
    {
        stream.Read(out long _);
        stream.ReadFlag();
        stream.ReadInt(20);
        stream.ReadInt(18);
        stream.ReadInt(16);
        stream.ReadInt(16);
        stream.Read(out uint _);
        stream.Read(out uint _);
        stream.ReadFlag();
        stream.Read(out byte _);
        for (var i = 0; i < 7; ++i)
            stream.ReadFlag();
        stream.ReadFlag(); // path
        stream.ReadFlag(); // template
        stream.ReadFlag(); // spawn
        stream.ReadInt(8); // tricks
        stream.ReadFlag(); // trailer
    }
}

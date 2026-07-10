using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Stage 4: GhostVehicle.PackUpdate wires real path/template/spawn-owner/driver-AI-state fields
/// onto the wire in the client's fixed field order (VehicleNet_UnpackGhostVehicle contract).
/// </summary>
[TestClass]
public class GhostVehicleWireTests
{
    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
        GhostVehicle.EnableAiStateWire = true;
    }

    [TestMethod]
    public void PackInitial_WithPath_WritesPathBlockInOrder()
    {
        var vehicle = CreateVehicleWithMap(9101);
        vehicle.CoidCurrentPath = 555;
        vehicle.ExtraPathId = 7;
        vehicle.PathReversing = true;
        vehicle.PathIsRoad = true;
        vehicle.PatrolDistance = 12.5f;

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsTrue(stream.ReadFlag(), "path flag must be set when CoidCurrentPath > 0");
        Assert.AreEqual(555u, stream.ReadInt(18));
        stream.Read(out int extraPathId);
        Assert.AreEqual(7, extraPathId);
        Assert.IsTrue(stream.ReadFlag(), "PathReversing");
        Assert.IsTrue(stream.ReadFlag(), "PathIsRoad");
        stream.Read(out float patrolDistance);
        Assert.AreEqual(12.5f, patrolDistance);
    }

    [TestMethod]
    public void PackInitial_NoPath_WritesFlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9102);

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag(), "path flag must be false when CoidCurrentPath <= 0");
    }

    [TestMethod]
    public void PackInitial_TemplateAndSpawnOwner_Gated()
    {
        var withValues = CreateVehicleWithMap(9103);
        withValues.TemplateId = 42;
        withValues.SpawnOwnerCoid = 4321;

        var stream = PackInitial(withValues);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag()); // no path
        Assert.IsTrue(stream.ReadFlag(), "TemplateId flag");
        Assert.AreEqual(42u, stream.ReadInt(20));
        Assert.IsTrue(stream.ReadFlag(), "SpawnOwner flag");
        Assert.AreEqual(4321u, stream.ReadInt(20));

        var withoutValues = CreateVehicleWithMap(9104);
        var stream2 = PackInitial(withoutValues);
        SkipToPathBlock(stream2);

        Assert.IsFalse(stream2.ReadFlag()); // no path
        Assert.IsFalse(stream2.ReadFlag(), "TemplateId flag false when TemplateId == -1");
        Assert.IsFalse(stream2.ReadFlag(), "SpawnOwner flag false when SpawnOwnerCoid == -1");
    }

    [TestMethod]
    public void PackInitial_CreatureOwner_WritesDriverLevel()
    {
        var vehicle = CreateVehicleWithMap(9105);
        var driver = new Creature();
        driver.SetCoid(9106, false);
        driver.Level = 7;
        vehicle.SetOwner(driver);

        var stream = PackInitial(vehicle);
        SkipToPathBlock(stream);

        Assert.IsFalse(stream.ReadFlag()); // no path
        Assert.IsFalse(stream.ReadFlag()); // no template
        Assert.IsFalse(stream.ReadFlag()); // no spawn owner

        stream.ReadInt(8); // trick count

        Assert.IsFalse(stream.ReadFlag()); // IsTrailer

        Assert.IsTrue(stream.ReadFlag(), "CurrentOwner present");
        stream.Read(out long _); // owner coid
        stream.ReadFlag(); // owner global
        stream.ReadInt(20); // owner CBID

        Assert.IsFalse(stream.ReadFlag(), "characterOwner == null branch (driver is a Creature)");

        Assert.IsFalse(stream.ReadFlag()); // EnhancementID
        Assert.IsFalse(stream.ReadFlag()); // CoidOnUseTrigger
        Assert.IsFalse(stream.ReadFlag()); // CoidOnUseReaction
        Assert.IsFalse(stream.ReadFlag()); // CreatureSummoner
        Assert.IsFalse(stream.ReadFlag()); // DoesntCountAsSummon
        Assert.AreEqual(7u, stream.ReadInt(8), "Level must be the driver creature's level");
        Assert.IsFalse(stream.ReadFlag()); // IsElite
    }

    [TestMethod]
    public void PackUpdate_StateMask_WritesDriverCombatState()
    {
        var vehicle = CreateVehicleWithMap(9107);
        var driver = new Creature();
        driver.SetCoid(9108, false);
        driver.AiCombatState = 3;
        vehicle.SetOwner(driver);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsTrue(stream.ReadFlag(), "StateMask flag must be set when a driver creature is present");
        stream.Read(out byte state);
        Assert.AreEqual((byte)3, state);
    }

    [TestMethod]
    public void PackUpdate_StateMask_NoDriver_FlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9109);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsFalse(stream.ReadFlag(), "StateMask flag must stay false without a driver creature");
    }

    [TestMethod]
    public void PackUpdate_StateMask_LeverDisabled_FlagFalse()
    {
        var vehicle = CreateVehicleWithMap(9110);
        var driver = new Creature();
        driver.SetCoid(9111, false);
        driver.AiCombatState = 9;
        vehicle.SetOwner(driver);

        GhostVehicle.EnableAiStateWire = false;

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        SkipNonInitialFlagsBeforeStateMask(stream);

        Assert.IsFalse(stream.ReadFlag(), "EnableAiStateWire = false must suppress the AI-state block");
    }

    private static Vehicle CreateVehicleWithMap(long coid)
    {
        var map = CreateTestMap((int)coid);
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.SetMap(map);
        vehicle.CreateGhost();
        return vehicle;
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_ghost_vehicle_wire_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };

        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static BitStream PackInitial(Vehicle vehicle)
    {
        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = true;
        vehicle.Ghost.PackUpdate(null, GhostObject.InitialMask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static BitStream PackUpdateNonInitial(Vehicle vehicle, ulong updateMask)
    {
        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = false;
        vehicle.Ghost.PackUpdate(null, updateMask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    /// <summary>Consumes PackCommon + the fixed multiplier flags, leaving the stream positioned
    /// right before the path-block flag (GhostVehicle.cs initial block).</summary>
    private static void SkipToPathBlock(BitStream stream)
    {
        stream.Read(out long _); // PackCommon: coid
        stream.ReadFlag();       // PackCommon: global
        stream.ReadInt(20);      // PackCommon: CBID
        stream.ReadInt(18);      // PackCommon: MaxHP
        stream.ReadInt(16);      // PackCommon: faction
        stream.ReadInt(16);      // PackCommon: bareTeamFaction

        stream.Read(out uint _); // PrimaryColor
        stream.Read(out uint _); // SecondaryColor
        stream.ReadFlag();       // IsActive
        stream.Read(out byte _); // Trim

        for (var i = 0; i < 7; ++i)
            stream.ReadFlag(); // SpeedAdd .. AVDNormalSpinDampeningMultiplier (always false)
    }

    /// <summary>Consumes the non-initial update flags that precede the StateMask block.</summary>
    private static void SkipNonInitialFlagsBeforeStateMask(BitStream stream)
    {
        for (var i = 0; i < 14; ++i)
            stream.ReadFlag();
    }
}

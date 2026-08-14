using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// PDB Pass 5. Reads a packed GhostVehicle bitstream in the order
/// VehicleNet_UnpackGhostVehicle (RVA 0x005F7720) consumes on initial + a
/// transform-only delta. Mask *names* are AutoCore constants; bit numbers below
/// are those constants' values, verified against the unpacker comment block and
/// the existing GhostVehicleWireTests harness.
/// </summary>
[TestClass]
public class GhostVehicleClientBitTests
{
    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
        GhostVehicle.EnableAiStateWire = true;
        GhostVehicle.EnablePathWire = true;
        GhostVehicle.EnableOwnerWire = true;
        GhostVehicle.EnableTemplateSpawnWire = true;
        GhostVehicle.EnableMinimalForeignInitialProfile = false;
        GhostVehicle.EnableInitialHardpointPack = false;
        GhostVehicle.EnableDeferredForeignPose = false;
    }

    [TestMethod]
    public void InitialUnpackOrder_ColorsTrimMultipliersPathTemplateOwner()
    {
        var vehicle = CreateVehicle(9201);
        vehicle.CoidCurrentPath = 0;
        vehicle.TemplateId = -1;
        vehicle.SpawnOwnerCoid = -1;

        var stream = Pack(vehicle, GhostObject.InitialMask, initial: true);

        stream.Read(out long coid);
        Assert.AreEqual(9201L, coid);
        Assert.IsTrue(stream.ReadFlag()); // global
        stream.ReadInt(20); // CBID
        stream.ReadInt(18); // MaxHP
        stream.ReadInt(16);
        stream.ReadInt(16);

        stream.Read(out uint _);
        stream.Read(out uint _);
        stream.ReadFlag(); // IsActive
        stream.Read(out byte _);

        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag(), $"multiplier {i} must be default-false");

        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn owner
        Assert.AreEqual(0u, stream.ReadInt(8)); // trick count
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag()); // owner
    }

    [TestMethod]
    public void TransformOnlyDelta_ReadsPoseThenFiringThenVehicleFlags()
    {
        var vehicle = CreateVehicle(9202);
        vehicle.Position = new Vector3(1.5f, 2.5f, 3.5f);
        vehicle.Rotation = new Quaternion(0, 0, 0, 1);
        vehicle.Firing = 0x05;
        vehicle.VehicleFlags = VehicleMovedFlags.Handbreak;

        var stream = Pack(vehicle, GhostObject.PositionMask, initial: false);

        // Non-initial: skills flag first, then equipment flags, then pose.
        Assert.IsFalse(stream.ReadFlag()); // SkillsMask
        Assert.IsFalse(stream.ReadFlag()); // WheelSet single flag
        Assert.IsFalse(stream.ReadFlag()); // Front
        Assert.IsFalse(stream.ReadFlag()); // Turret
        Assert.IsFalse(stream.ReadFlag()); // Rear
        Assert.IsFalse(stream.ReadFlag()); // Melee
        Assert.IsFalse(stream.ReadFlag()); // Ornament
        Assert.IsFalse(stream.ReadFlag()); // Armor
        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsFalse(stream.ReadFlag()); // Murderer
        Assert.IsFalse(stream.ReadFlag()); // Health
        Assert.IsFalse(stream.ReadFlag()); // HealthMax
        Assert.IsFalse(stream.ReadFlag()); // State
        Assert.IsTrue(stream.ReadFlag(), "PositionMask");

        stream.Read(out float x);
        stream.Read(out float y);
        stream.Read(out float z);
        Assert.AreEqual(1.5f, x);
        Assert.AreEqual(2.5f, y);
        Assert.AreEqual(3.5f, z);

        stream.Read(out float qx);
        stream.Read(out float qy);
        stream.Read(out float qz);
        stream.Read(out float qw);
        Assert.AreEqual(0f, qx);
        Assert.AreEqual(1f, qw);

        stream.Read(out float _);
        stream.Read(out float _);
        stream.Read(out float _); // vel
        stream.Read(out float _);
        stream.Read(out float _);
        stream.Read(out float _); // ang vel

        stream.Read(out byte firing);
        stream.Read(out byte flags);
        Assert.AreEqual((byte)0x05, firing, "unpack reads first pose flag-byte as Firing");
        Assert.AreEqual((byte)VehicleMovedFlags.Handbreak, flags,
            "second pose flag-byte is VehicleFlags; swapped order brakes path NPCs");
    }

    [TestMethod]
    public void MaskBitNumbers_MatchClientCommentBlock()
    {
        // GhostObject bits used by VehicleNet_UnpackGhostVehicle after the initial body.
        Assert.AreEqual(0x002ul, GhostObject.PositionMask);
        Assert.AreEqual(0x004ul, GhostObject.TargetMask);
        Assert.AreEqual(0x008ul, GhostObject.HealthMask);
        Assert.AreEqual(0x040ul, GhostObject.HealthMaxMask);
        Assert.AreEqual(0x080ul, GhostObject.SkillsMask);

        // Vehicle-specific; unpack comment: Heat 0x20000000, ShieldMax 0x2000000,
        // Shield 0x4000000, Power 0x8000000.
        Assert.AreEqual(0x0020000000ul, GhostVehicle.HeatMask);
        Assert.AreEqual(0x0002000000ul, GhostVehicle.ShieldMaxMask);
        Assert.AreEqual(0x0004000000ul, GhostVehicle.ShieldMask);
        Assert.AreEqual(0x0008000000ul, GhostVehicle.PowerMask);
        Assert.AreEqual(0x0100000000ul, GhostVehicle.WheelSetMask);
        Assert.AreEqual(0x0400000000ul, GhostVehicle.FrontWeaponMask);
        Assert.AreEqual(0x0800000000ul, GhostVehicle.TurretWeaponMask);
        Assert.AreEqual(0x1000000000ul, GhostVehicle.RearWeaponMask);
        Assert.AreEqual(0x2000000000ul, GhostVehicle.MeleeWeaponMask);
        Assert.AreEqual(0x4000000000ul, GhostVehicle.OrnamentMask);
        Assert.AreEqual(0x0040000000ul, GhostVehicle.ChangeArmor);
    }

    [TestMethod]
    public void LocalOwnerInitial_OmitsEquipment_PacksCombatPools()
    {
        var vehicle = CreateVehicle(9203);
        var character = new Character();
        character.SetCoid(9204, true);
        vehicle.SetOwner(character);
        character.SetCurrentVehicleForTests(vehicle);

        var conn = new TNLConnection();
        conn.CurrentCharacter = character;
        character.SetOwningConnection(conn);

        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = true;
        vehicle.Ghost.PackUpdate(conn, ~0ul, stream);
        stream.SetBitPosition(0);

        // Combat-only initial must not emit wheel hardpoint (would re-run create-buffer
        // +0x45C and can clear live +0x258).
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
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn
        stream.ReadInt(8);
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag(), "owner control initial omits CurrentOwner block");
        Assert.IsFalse(stream.ReadFlag(), "no WheelSet hardpoint on owner-combat initial");
    }

    private static Vehicle CreateVehicle(long coid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(coid % 100000),
            MapFileName = $"tm_ghost_client_bit_{coid}",
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

    private static BitStream Pack(Vehicle vehicle, ulong mask, bool initial)
    {
        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = initial;
        vehicle.Ghost.PackUpdate(null, mask, stream);
        stream.SetBitPosition(0);
        return stream;
    }
}

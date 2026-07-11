using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;
using TNL.Utils;

namespace AutoCore.Game.Tests.TNL.Ghost;

using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Heavy regression tests for the Invalid-Packet / AV 0x005F8FED wire fixes.
/// Encodes live failure modes: null-owner GM/AI, initial equipment skip after CreateVehicle.
/// </summary>
[TestClass]
public class GhostVehicleWireRegressionTests
{
    private const ulong AllEquipmentMasks =
        GhostVehicle.WheelSetMask
        | GhostVehicle.FrontWeaponMask
        | GhostVehicle.TurretWeaponMask
        | GhostVehicle.RearWeaponMask
        | GhostVehicle.MeleeWeaponMask
        | GhostVehicle.OrnamentMask
        | GhostVehicle.ChangeArmor;

    private const ulong FullDirtyMask = ulong.MaxValue;

    [TestCleanup]
    public void TearDown()
    {
        NetObject.PIsInitialUpdate = false;
        GhostVehicle.EnableAiStateWire = true;
        GhostVehicle.EnablePathWire = true;
        GhostVehicle.EnableOwnerWire = true;
        GhostVehicle.EnableTemplateSpawnWire = true;
        GhostVehicle.EnableMinimalForeignInitialProfile = false;
        GhostVehicle.EnableMinimalForeignPathBlock = false;
        GhostVehicle.EnableMinimalForeignTemplateSpawnBlock = false;
        GhostVehicle.EnableMinimalForeignOwnerBlock = false;
        WireDiag.ResetForTests();
    }

    #region Crash-recipe regressions

    [TestMethod]
    public void PackInitial_FullNpcRecipe_SkipsEquipment_PacksPathOwnerTemplateSpawnAndAi()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_001);
        vehicle.CoidCurrentPath = 555;
        vehicle.ExtraPathId = 3;
        vehicle.PathReversing = true;
        vehicle.PathIsRoad = false;
        vehicle.PatrolDistance = 9.5f;
        vehicle.TemplateId = 42;
        vehicle.SpawnOwnerCoid = 18097;

        var driver = new Creature();
        driver.SetCoid(MapNpcIdentity.CoidBase + 20_002, true);
        driver.Level = 7;
        driver.AiCombatState = 2;
        vehicle.SetOwner(driver);

        var armor = new Armor();
        armor.SetCoid(20_003, false);
        vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);
        var weapon = new Weapon();
        weapon.SetCoid(20_004, false);
        vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _);

        WireDiag.Enabled = true;
        var stream = PackInitial(vehicle, FullDirtyMask);

        // Initial body: path + template + spawn + creature owner
        SkipPackCommonAndMultipliers(stream);
        Assert.IsTrue(stream.ReadFlag(), "path");
        Assert.AreEqual(555u, stream.ReadInt(18));
        stream.Read(out int extra);
        Assert.AreEqual(3, extra);
        Assert.IsTrue(stream.ReadFlag()); // reversing
        Assert.IsFalse(stream.ReadFlag()); // road
        stream.Read(out float patrol);
        Assert.AreEqual(9.5f, patrol);

        Assert.IsTrue(stream.ReadFlag(), "template");
        Assert.AreEqual(42u, stream.ReadInt(20));
        Assert.IsTrue(stream.ReadFlag(), "spawn");
        Assert.AreEqual(18097u, stream.ReadInt(20));
        Assert.AreEqual(0u, stream.ReadInt(8)); // tricks
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsTrue(stream.ReadFlag(), "owner");
        stream.Read(out long ownerCoid);
        Assert.AreEqual(MapNpcIdentity.CoidBase + 20_002, ownerCoid);
        Assert.IsTrue(stream.ReadFlag()); // owner global
        stream.ReadInt(20); // CBID
        Assert.IsFalse(stream.ReadFlag()); // not character
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.AreEqual(7u, stream.ReadInt(8)); // driver level
        Assert.IsFalse(stream.ReadFlag()); // elite

        // Equipment must NOT be on initial (CreateVehicle already sent it)
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag(), $"equipment lead flag {i} must be false on initial");

        Assert.IsFalse(stream.ReadFlag()); // GM (creature owner)
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsTrue(stream.ReadFlag()); // Murderer (full mask)
        stream.Read(out long murderer);
        Assert.AreEqual(0L, murderer);
        Assert.IsTrue(stream.ReadFlag()); // Health
        stream.ReadInt(18);
        stream.ReadFlag(); // corpse
        Assert.IsTrue(stream.ReadFlag()); // HealthMax
        stream.ReadInt(18);
        Assert.IsTrue(stream.ReadFlag(), "AI StateMask for creature driver with client owner");
        stream.Read(out byte ai);
        Assert.AreEqual((byte)2, ai);

        var diag = WireDiag.Snapshot().Single();
        StringAssert.Contains(diag.Detail, "clientOwner=1");
        StringAssert.Contains(diag.Detail, "equip=0");
        StringAssert.Contains(diag.Detail, "path=1/1");
        StringAssert.Contains(diag.Detail, "owner=1/1");
        StringAssert.Contains(diag.Detail, "tmpl=1/1");
        StringAssert.Contains(diag.Detail, "spawn=1/1");
    }

    [TestMethod]
    public void PackInitial_OwnerWireOff_FullMask_NeverEmitsGmAiOrEquipmentPayload()
    {
        var vehicle = CreateVehicleWithMap(20_010);
        var character = CreateTestCharacter(20_011);
        character.GMLevel = 5;
        vehicle.SetOwner(character);
        var armor = new Armor();
        armor.SetCoid(20_012, false);
        vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);

        GhostVehicle.EnableOwnerWire = false;
        var stream = PackInitial(vehicle, FullDirtyMask);

        SkipPackCommonAndMultipliers(stream);
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn
        stream.ReadInt(8);
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag()); // owner omitted
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // no equipment
        Assert.IsFalse(stream.ReadFlag(), "GM suppressed without client owner");
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag()); // Murderer
        stream.Read(out long _);
        Assert.IsTrue(stream.ReadFlag()); // Health
        stream.ReadInt(18);
        stream.ReadFlag();
        Assert.IsTrue(stream.ReadFlag()); // HealthMax
        stream.ReadInt(18);
        Assert.IsFalse(stream.ReadFlag(), "AI suppressed without client owner");
    }

    [TestMethod]
    public void PackInitial_MissingParent_Throws()
    {
        var ghost = new GhostVehicle();
        var stream = new BitStream(new byte[256], 256);
        NetObject.PIsInitialUpdate = true;
        Assert.ThrowsException<Exception>(() => ghost.PackUpdate(null, GhostObject.InitialMask, stream));
    }

    [TestMethod]
    public void GetClassRep_AfterBootstrap_ReturnsRegisteredRep()
    {
        var ghost = new GhostVehicle();
        var rep = ghost.GetClassRep();
        Assert.IsNotNull(rep, "TestBootstrap registers GhostVehicle net class reps");
    }

    [TestMethod]
    public void SetParent_NullOrNonVehicle_ReturnsEarlyWithoutThrow()
    {
        var ghost = new GhostVehicle();
        ghost.SetParent(null);

        var creature = new Creature();
        creature.SetCoid(20_099, false);
        ghost.SetParent(creature);
    }

    [TestMethod]
    public void PackUpdate_WithTNLConnectionAndWireDiag_RecordsPlayerCoidFromConnection()
    {
        var vehicle = CreateVehicleWithMap(20_100);
        WireDiag.Enabled = true;
        var connection = new AutoCore.Game.TNL.TNLConnection();

        var stream = new BitStream(new byte[4096], 4096);
        NetObject.PIsInitialUpdate = true;
        vehicle.Ghost.PackUpdate(connection, GhostObject.InitialMask, stream);

        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(WireDiagKind.GhostPack, entry.Kind);
        Assert.AreEqual("GhostVehicle", entry.Name);
        Assert.IsTrue(entry.Initial);
        StringAssert.Contains(entry.Detail, "clientOwner=0");
        StringAssert.Contains(entry.Detail, "equip=0");
    }

    [TestMethod]
    public void PackUpdate_WireDiagDisabled_DoesNotRecordGhostPack()
    {
        var vehicle = CreateVehicleWithMap(20_101);
        WireDiag.Enabled = false;
        PackInitial(vehicle);
        Assert.AreEqual(0, WireDiag.Snapshot().Count);
    }

    [TestMethod]
    public void PackInitial_CharacterOwner_PacksNameAndAppearanceSlots()
    {
        var vehicle = CreateVehicleWithMap(20_102);
        var character = CreateTestCharacter(20_103);
        character.GMLevel = 2;
        vehicle.SetOwner(character);

        var stream = PackInitial(vehicle, GhostObject.InitialMask | GhostVehicle.GMMask | GhostVehicle.AttributeMask);
        SkipPackCommonAndMultipliers(stream);
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn
        stream.ReadInt(8);
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsTrue(stream.ReadFlag(), "owner");
        stream.Read(out long ownerCoid);
        Assert.AreEqual(20_103L, ownerCoid);
        stream.ReadFlag(); // global
        stream.ReadInt(20); // CBID
        Assert.IsTrue(stream.ReadFlag(), "is character");
        stream.ReadString(out string name);
        Assert.AreEqual("Tester", name);
        stream.ReadString(out string _); // clan
        stream.Read(out byte level);
        Assert.AreEqual((byte)1, level);
        Assert.IsFalse(stream.ReadFlag()); // possess
        stream.ReadString(out string _); // vehicle name
        for (var i = 0; i < 8; ++i)
            stream.ReadInt(16);

        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // equipment skipped on initial
        Assert.IsTrue(stream.ReadFlag(), "GM packs when character owner on initial");
        Assert.AreEqual(2u, stream.ReadInt(4));
    }

    [TestMethod]
    public void PackInitial_AllLeversOff_OmitsOptionalBlocks()
    {
        var vehicle = CreateVehicleWithMap(20_104);
        vehicle.CoidCurrentPath = 99;
        vehicle.TemplateId = 5;
        vehicle.SpawnOwnerCoid = 6;
        var driver = new Creature();
        driver.SetCoid(20_105, false);
        driver.Level = 3;
        driver.AiCombatState = 1;
        vehicle.SetOwner(driver);

        GhostVehicle.EnablePathWire = false;
        GhostVehicle.EnableOwnerWire = false;
        GhostVehicle.EnableTemplateSpawnWire = false;
        GhostVehicle.EnableAiStateWire = false;

        WireDiag.Enabled = true;
        var stream = PackInitial(vehicle, FullDirtyMask);
        SkipPackCommonAndMultipliers(stream);
        Assert.IsFalse(stream.ReadFlag(), "path lever off");
        Assert.IsFalse(stream.ReadFlag(), "template lever off");
        Assert.IsFalse(stream.ReadFlag(), "spawn lever off");
        stream.ReadInt(8);
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag(), "owner lever off");

        var diag = WireDiag.Snapshot().Single();
        StringAssert.Contains(diag.Detail, "path=0/1");
        StringAssert.Contains(diag.Detail, "owner=0/1");
        StringAssert.Contains(diag.Detail, "tmpl=0/1");
        StringAssert.Contains(diag.Detail, "spawn=0/1");
        StringAssert.Contains(diag.Detail, "clientOwner=0");
        StringAssert.Contains(diag.Detail, "aiWire=0");
    }

    [TestMethod]
    public void PackUpdate_NonInitial_NoOwner_SuppressesGmAiAttributes()
    {
        var vehicle = CreateVehicleWithMap(20_106);
        var mask = GhostVehicle.GMMask | GhostVehicle.StateMask | GhostVehicle.AttributeMask;
        var stream = PackUpdateNonInitial(vehicle, mask);

        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // equip
        Assert.IsFalse(stream.ReadFlag(), "GM without owner");
        Assert.IsFalse(stream.ReadFlag()); // clan
        Assert.IsFalse(stream.ReadFlag()); // pet
        Assert.IsFalse(stream.ReadFlag()); // murderer
        Assert.IsFalse(stream.ReadFlag()); // health
        Assert.IsFalse(stream.ReadFlag()); // health max
        Assert.IsFalse(stream.ReadFlag(), "AI without owner");
        Assert.IsFalse(stream.ReadFlag()); // position
        Assert.IsFalse(stream.ReadFlag()); // target
        Assert.IsFalse(stream.ReadFlag(), "attributes without character owner");
    }

    [TestMethod]
    public void PackUpdate_NonInitial_CreatureDriver_AiStateRequiresOwnerPresent()
    {
        var vehicle = CreateVehicleWithMap(20_107);
        var driver = new Creature();
        driver.SetCoid(20_108, false);
        driver.AiCombatState = 4;
        vehicle.SetOwner(driver);

        // Owner block sent at initial → delta AI state gating is satisfied.
        PackInitial(vehicle);

        GhostVehicle.EnableAiStateWire = true;
        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag(), "AI on delta with creature owner");
        stream.Read(out byte ai);
        Assert.AreEqual((byte)4, ai);

        GhostVehicle.EnableAiStateWire = false;
        var stream2 = PackUpdateNonInitial(vehicle, GhostVehicle.StateMask);
        Assert.IsFalse(stream2.ReadFlag());
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag());
        Assert.IsFalse(stream2.ReadFlag(), "AI lever off");
    }

    [TestMethod]
    public void PackUpdate_NonInitial_OrnamentAndWheelSet_PackWhenEquipped()
    {
        var vehicle = CreateVehicleWithMap(20_109);
        var wheel = new WheelSet();
        wheel.SetCoid(20_111, false);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.WheelSet, wheel, out _));

        var ornament = new SimpleObject(GraphicsObjectType.Graphics);
        ornament.SetCoid(20_110, false);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Ornament, ornament, out _));

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.WheelSetMask | GhostVehicle.OrnamentMask);
        Assert.IsFalse(stream.ReadFlag()); // Skills
        Assert.IsTrue(stream.ReadFlag()); // Wheel mask
        Assert.IsTrue(stream.ReadFlag()); // present
        stream.ReadInt(20);
        stream.Read(out long wheelCoid);
        Assert.AreEqual(20_111L, wheelCoid);
        stream.ReadFlag();
        Assert.IsFalse(stream.ReadFlag()); // Front
        Assert.IsFalse(stream.ReadFlag()); // Turret
        Assert.IsFalse(stream.ReadFlag()); // Rear
        Assert.IsFalse(stream.ReadFlag()); // Melee
        Assert.IsTrue(stream.ReadFlag()); // Ornament mask
        Assert.IsTrue(stream.ReadFlag()); // present
        stream.ReadInt(20);
        stream.Read(out long ornamentCoid);
        Assert.AreEqual(20_110L, ornamentCoid);
        stream.ReadFlag();
    }

    #endregion

    #region Path contract

    [TestMethod]
    public void PackInitial_PathBlock_UsesEighteenBitPathId()
    {
        var vehicle = CreateVehicleWithMap(20_020);
        vehicle.CoidCurrentPath = (1 << 18) - 1; // max 18-bit value
        vehicle.ExtraPathId = -1;
        vehicle.PathReversing = false;
        vehicle.PathIsRoad = true;
        vehicle.PatrolDistance = 0f;

        var stream = PackInitial(vehicle);
        SkipPackCommonAndMultipliers(stream);
        Assert.IsTrue(stream.ReadFlag());
        Assert.AreEqual((1u << 18) - 1, stream.ReadInt(18));
        stream.Read(out int extra);
        Assert.AreEqual(-1, extra);
        Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag());
        stream.Read(out float patrol);
        Assert.AreEqual(0f, patrol);
    }

    [TestMethod]
    public void PackInitial_PathOverflow_DoesNotThrow_WritesTruncatedEighteenBits()
    {
        var vehicle = CreateVehicleWithMap(20_021);
        vehicle.CoidCurrentPath = 1L << 18; // requires 19 bits — will truncate on wire
        vehicle.ExtraPathId = 0;

        var stream = PackInitial(vehicle);
        SkipPackCommonAndMultipliers(stream);
        Assert.IsTrue(stream.ReadFlag());
        // Truncated to low 18 bits → 0
        Assert.AreEqual(0u, stream.ReadInt(18));
        stream.Read(out int _);
        stream.ReadFlag();
        stream.ReadFlag();
        stream.Read(out float _);
    }

    #endregion

    #region Hardpoints + armor

    [TestMethod]
    public void PackUpdate_NonInitial_AllHardpoints_NullPresentFlags()
    {
        var vehicle = CreateVehicleWithMap(20_030);
        var mask = AllEquipmentMasks;
        var stream = PackUpdateNonInitial(vehicle, mask);

        Assert.IsFalse(stream.ReadFlag()); // Skills
        // Each equipment mask set, item null → mask true, present false
        for (var i = 0; i < 6; ++i)
        {
            Assert.IsTrue(stream.ReadFlag(), $"hardpoint mask {i}");
            Assert.IsFalse(stream.ReadFlag(), $"hardpoint present {i}");
        }

        Assert.IsTrue(stream.ReadFlag()); // armor mask
        Assert.IsFalse(stream.ReadFlag()); // armor present
    }

    [TestMethod]
    public void PackUpdate_NonInitial_Armor_WritesSixShortResistances()
    {
        var vehicle = CreateVehicleWithMap(20_031);
        var armor = new Armor();
        armor.SetCoid(20_032, false);
        Assert.IsTrue(vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _));

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.ChangeArmor);
        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 6; ++i)
            Assert.IsFalse(stream.ReadFlag()); // other hardpoints
        Assert.IsTrue(stream.ReadFlag()); // armor mask
        Assert.IsTrue(stream.ReadFlag()); // present
        stream.ReadInt(20);
        stream.Read(out long coid);
        Assert.AreEqual(20_032L, coid);
        stream.ReadFlag();
        // Six 16-bit shorts (null CloneBase → zeros)
        for (var i = 0; i < 6; ++i)
        {
            stream.Read(out short r);
            Assert.AreEqual((short)0, r);
        }
    }

    [TestMethod]
    public void PackUpdate_NonInitial_EachWeaponSlot_PacksCoid()
    {
        var vehicle = CreateVehicleWithMap(20_040);
        var slots = new (VehicleEquipmentSlot Slot, ulong Mask, long Coid)[]
        {
            (VehicleEquipmentSlot.WeaponFront, GhostVehicle.FrontWeaponMask, 20_041),
            (VehicleEquipmentSlot.WeaponTurret, GhostVehicle.TurretWeaponMask, 20_042),
            (VehicleEquipmentSlot.WeaponRear, GhostVehicle.RearWeaponMask, 20_043),
            (VehicleEquipmentSlot.WeaponMelee, GhostVehicle.MeleeWeaponMask, 20_044),
        };

        foreach (var (slot, mask, coid) in slots)
        {
            var w = new Weapon();
            w.SetCoid(coid, false);
            Assert.IsTrue(vehicle.TryEquipItem(slot, w, out _));

            var stream = PackUpdateNonInitial(vehicle, mask);
            Assert.IsFalse(stream.ReadFlag()); // Skills
            Assert.IsFalse(stream.ReadFlag()); // Wheel
            // walk hardpoint flags until we find a true mask
            var found = false;
            for (var i = 0; i < 6 && !found; ++i)
            {
                if (!stream.ReadFlag())
                    continue;
                Assert.IsTrue(stream.ReadFlag());
                stream.ReadInt(20);
                stream.Read(out long wireCoid);
                Assert.AreEqual(coid, wireCoid);
                stream.ReadFlag();
                found = true;
            }

            Assert.IsTrue(found, $"slot {slot}");
            vehicle = CreateVehicleWithMap(20_040 + (int)slot + 100);
        }
    }

    #endregion

    #region Mask tails (coverage)

    [TestMethod]
    public void PackUpdate_NonInitial_PositionHealthTargetTokenShields()
    {
        var vehicle = CreateVehicleWithMap(20_050);
        vehicle.Position = new Vector3(1, 2, 3);
        vehicle.Rotation = new Quaternion(0, 0, 0, 1);
        vehicle.Firing = 1;
        vehicle.Acceleration = 0.5f;
        vehicle.Steering = -0.25f;
        vehicle.WantedTurretDirection = 1.5f;

        var target = new Creature();
        target.SetCoid(20_051, true);
        vehicle.SetTargetObject(target);

        var mask = GhostObject.PositionMask
                   | GhostObject.HealthMask
                   | GhostObject.HealthMaxMask
                   | GhostObject.TargetMask
                   | GhostObject.TokenMask
                   | GhostVehicle.HeatMask
                   | GhostVehicle.ShieldMaxMask
                   | GhostVehicle.ShieldMask
                   | GhostVehicle.PowerMask
                   | GhostObject.MurdererMask;

        var stream = PackUpdateNonInitial(vehicle, mask);
        // Skills through State: all false for this mask set (except murderer/health/etc.)
        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag()); // equipment
        Assert.IsFalse(stream.ReadFlag()); // GM
        Assert.IsFalse(stream.ReadFlag()); // Clan
        Assert.IsFalse(stream.ReadFlag()); // Pet
        Assert.IsTrue(stream.ReadFlag()); // Murderer
        stream.Read(out long _);
        Assert.IsTrue(stream.ReadFlag()); // Health
        stream.ReadInt(18);
        stream.ReadFlag();
        Assert.IsTrue(stream.ReadFlag()); // HealthMax
        stream.ReadInt(18);
        Assert.IsFalse(stream.ReadFlag()); // State/AI
        Assert.IsTrue(stream.ReadFlag()); // Position
        stream.Read(out float x);
        stream.Read(out float y);
        stream.Read(out float z);
        Assert.AreEqual(1f, x);
        Assert.AreEqual(2f, y);
        Assert.AreEqual(3f, z);
        for (var i = 0; i < 4; ++i)
            stream.Read(out float _); // rot
        for (var i = 0; i < 6; ++i)
            stream.Read(out float _); // vel + ang
        stream.Read(out byte _); // flags
        stream.Read(out byte firing);
        Assert.AreEqual((byte)1, firing);
        stream.ReadSignedFloat(6); // accel
        stream.ReadSignedFloat(6); // steering
        stream.Read(out float turret);
        Assert.AreEqual(1.5f, turret);
        Assert.IsTrue(stream.ReadFlag()); // Target
        stream.Read(out long tCoid);
        Assert.AreEqual(20_051L, tCoid);
        Assert.IsTrue(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag()); // Attribute
        Assert.IsTrue(stream.ReadFlag()); // Heat
        stream.Read(out uint _);
        Assert.IsTrue(stream.ReadFlag()); // ShieldMax
        stream.Read(out uint _);
        Assert.IsTrue(stream.ReadFlag()); // Shield
        stream.Read(out uint _);
        Assert.IsTrue(stream.ReadFlag()); // Power
        stream.Read(out uint _);
        Assert.IsTrue(stream.ReadFlag()); // Token
        Assert.IsFalse(stream.ReadFlag()); // GivesToken
    }

    [TestMethod]
    public void PackUpdate_NonInitial_TargetNull_WritesZeroCoid()
    {
        var vehicle = CreateVehicleWithMap(20_060);
        vehicle.SetTargetObject(null);
        var stream = PackUpdateNonInitial(vehicle, GhostObject.TargetMask);
        // Skills + 7 equip + GM + Clan + Pet + Murderer + Health + HealthMax + State + Position = 16
        for (var i = 0; i < 16; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag()); // Target
        stream.Read(out long coid);
        Assert.AreEqual(0L, coid);
        Assert.IsFalse(stream.ReadFlag());
    }

    [TestMethod]
    public void PackUpdate_NonInitial_SkillsMask_WritesEmptySkillCounts()
    {
        var vehicle = CreateVehicleWithMap(20_061);
        var stream = PackUpdateNonInitial(vehicle, GhostObject.SkillsMask);
        Assert.IsTrue(stream.ReadFlag()); // Skills
        Assert.IsFalse(stream.ReadFlag()); // owner skills
        Assert.AreEqual(0u, stream.ReadInt(8)); // vehicle skill count
    }

    [TestMethod]
    public void PackInitial_ForeignVehicle_ConnectionMaskOverrideIsRecordedAsEffectiveMask()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_070);
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 20_070, true);
        var connection = new TNLConnection
        {
            ForeignVehicleInitialMaskOverrideForTests = GhostObject.PositionMask,
        };
        var ghost = new GhostVehicle();
        ghost.SetParent(vehicle);
        var stream = new BitStream(new byte[8192], 8192);

        WireDiag.Enabled = true;
        NetObject.PIsInitialUpdate = true;
        ghost.PackUpdate(connection, ulong.MaxValue, stream);

        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(GhostObject.PositionMask, entry.Mask,
            "A controlled foreign-vehicle experiment must use the connection's initial mask profile.");
        StringAssert.Contains(entry.Detail, "sourceMask=FFFFFFFFFFFFFFFF");
    }

    [TestMethod]
    public void PackInitial_ForeignVehicle_MinimalProfile_OmitsOptionalStateAndKeepsPose()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_071);
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 20_071, true);
        vehicle.CoidCurrentPath = 555;
        vehicle.TemplateId = 42;
        vehicle.SpawnOwnerCoid = 18_097;
        var driver = new Creature();
        driver.SetCoid(MapNpcIdentity.CoidBase + 20_072, true);
        vehicle.SetOwner(driver);

        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        WireDiag.Enabled = true;
        var stream = PackInitial(vehicle, ulong.MaxValue);

        SkipPackCommonAndMultipliers(stream);
        Assert.IsFalse(stream.ReadFlag(), "minimal profile omits path");
        Assert.IsFalse(stream.ReadFlag(), "minimal profile omits template");
        Assert.IsFalse(stream.ReadFlag(), "minimal profile omits spawn owner");
        stream.ReadInt(8); // trick count
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag(), "minimal profile omits owner");

        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(GhostObject.PositionMask, entry.Mask);
        StringAssert.Contains(entry.Detail, "profile=minimal");
    }

    [TestMethod]
    public void PackDelta_ForeignVehicle_MinimalProfile_RestrictsToPoseMask()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_073);
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 20_073, true);

        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        WireDiag.Enabled = true;
        var stream = PackUpdateNonInitial(vehicle, ulong.MaxValue);

        Assert.IsTrue(stream.ReadFlag() == false, "Skills must be withheld by the pose-only delta profile.");
        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(GhostObject.PositionMask, entry.Mask);
        StringAssert.Contains(entry.Detail, "profile=minimal");
    }

    [TestMethod]
    public void PackInitial_ForeignVehicle_MinimalProfile_PathLeverAddsOnlyPathBlock()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_074);
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 20_074, true);
        vehicle.CoidCurrentPath = 555;
        vehicle.ExtraPathId = 3;
        vehicle.PathReversing = true;
        vehicle.PathIsRoad = false;
        vehicle.PatrolDistance = 9.5f;

        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        GhostVehicle.EnableMinimalForeignPathBlock = true;
        var stream = PackInitial(vehicle, ulong.MaxValue);

        SkipPackCommonAndMultipliers(stream);
        Assert.IsTrue(stream.ReadFlag(), "path is the only optional initial block admitted by this lever");
        Assert.AreEqual(555u, stream.ReadInt(18));
        stream.Read(out int extraPathId);
        Assert.AreEqual(3, extraPathId);
        Assert.IsTrue(stream.ReadFlag());
        Assert.IsFalse(stream.ReadFlag());
        stream.Read(out float patrolDistance);
        Assert.AreEqual(9.5f, patrolDistance);
        Assert.IsFalse(stream.ReadFlag(), "template remains suppressed");
        Assert.IsFalse(stream.ReadFlag(), "spawn owner remains suppressed");
    }

    [TestMethod]
    public void PackInitial_ForeignVehicle_MinimalProfile_TemplateSpawnLeverAddsOnlyTemplateBlocks()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_075);
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 20_075, true);
        vehicle.TemplateId = 42;
        vehicle.SpawnOwnerCoid = 18_097;

        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        GhostVehicle.EnableMinimalForeignTemplateSpawnBlock = true;
        var stream = PackInitial(vehicle, ulong.MaxValue);

        SkipPackCommonAndMultipliers(stream);
        Assert.IsFalse(stream.ReadFlag(), "path remains separately gated");
        Assert.IsTrue(stream.ReadFlag());
        Assert.AreEqual(42u, stream.ReadInt(20));
        Assert.IsTrue(stream.ReadFlag());
        Assert.AreEqual(18_097u, stream.ReadInt(20));
        stream.ReadInt(8); // trick count
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsFalse(stream.ReadFlag(), "owner remains suppressed");
    }

    [TestMethod]
    public void PackInitial_ForeignVehicle_MinimalProfile_OwnerLeverAddsOnlyOwnerBlock()
    {
        var vehicle = CreateVehicleWithMap(MapNpcIdentity.CoidBase + 20_076);
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 20_076, true);
        var driver = new Creature();
        driver.SetCoid(MapNpcIdentity.CoidBase + 20_077, true);
        driver.Level = 7;
        vehicle.SetOwner(driver);

        GhostVehicle.EnableMinimalForeignInitialProfile = true;
        GhostVehicle.EnableMinimalForeignOwnerBlock = true;
        var stream = PackInitial(vehicle, ulong.MaxValue);

        SkipPackCommonAndMultipliers(stream);
        Assert.IsFalse(stream.ReadFlag()); // path
        Assert.IsFalse(stream.ReadFlag()); // template
        Assert.IsFalse(stream.ReadFlag()); // spawn owner
        stream.ReadInt(8); // trick count
        Assert.IsFalse(stream.ReadFlag()); // trailer
        Assert.IsTrue(stream.ReadFlag(), "owner is the only newly admitted block");
        stream.Read(out long ownerCoid);
        Assert.AreEqual(driver.ObjectId.Coid, ownerCoid);
        Assert.IsTrue(stream.ReadFlag()); // global
        stream.ReadInt(20); // CBID
        Assert.IsFalse(stream.ReadFlag()); // creature owner
        for (var i = 0; i < 5; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.AreEqual(7u, stream.ReadInt(8));
        Assert.IsFalse(stream.ReadFlag());
    }

    [TestMethod]
    public void PackUpdate_NonInitial_AttributeMask_RequiresCharacterOwner()
    {
        var vehicle = CreateVehicleWithMap(20_062);
        var character = CreateTestCharacter(20_063);
        vehicle.SetOwner(character);

        // Owner block sent at initial → delta attribute gating is satisfied.
        PackInitial(vehicle);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.AttributeMask);
        // through Target (17 flags), then Attribute
        for (var i = 0; i < 17; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag(), "Attribute packs for character owner on delta");
        for (var i = 0; i < 4; ++i)
            stream.Read(out uint _);
    }

    [TestMethod]
    public void PackUpdate_NonInitial_GmMask_CharacterOwner()
    {
        var vehicle = CreateVehicleWithMap(20_064);
        var character = CreateTestCharacter(20_065);
        character.GMLevel = 4;
        vehicle.SetOwner(character);

        // Owner block sent at initial → delta GM gating is satisfied.
        PackInitial(vehicle);

        var stream = PackUpdateNonInitial(vehicle, GhostVehicle.GMMask);
        Assert.IsFalse(stream.ReadFlag()); // Skills
        for (var i = 0; i < 7; ++i)
            Assert.IsFalse(stream.ReadFlag());
        Assert.IsTrue(stream.ReadFlag());
        Assert.AreEqual(4u, stream.ReadInt(4));
    }

    #endregion

    #region Helpers

    private static Vehicle CreateVehicleWithMap(long coid)
    {
        var map = CreateTestMap(unchecked((int)(coid & 0x7FFFFFFF)));
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid, true);
        vehicle.SetMap(map);
        vehicle.CreateGhost();
        return vehicle;
    }

    private static Character CreateTestCharacter(long coid)
    {
        var character = new Character();
        character.SetCoid(coid, true);
        var dbData = new CharacterData { Coid = coid, Name = "Tester", Level = 1 };
        typeof(Character)
            .GetProperty("DBData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, dbData);
        return character;
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_gv_reg_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static BitStream PackInitial(Vehicle vehicle, ulong updateMask = GhostObject.InitialMask)
    {
        var stream = new BitStream(new byte[8192], 8192);
        NetObject.PIsInitialUpdate = true;
        vehicle.Ghost.PackUpdate(null, updateMask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static BitStream PackUpdateNonInitial(Vehicle vehicle, ulong updateMask)
    {
        var stream = new BitStream(new byte[8192], 8192);
        NetObject.PIsInitialUpdate = false;
        vehicle.Ghost.PackUpdate(null, updateMask, stream);
        stream.SetBitPosition(0);
        return stream;
    }

    private static void SkipPackCommonAndMultipliers(BitStream stream)
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
    }

    #endregion
}

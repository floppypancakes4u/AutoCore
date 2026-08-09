using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using System.Runtime.CompilerServices;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>
/// SS-45/SS-49 slot selection, exercised directly so every branch of the arc test and the
/// fallback chain is pinned — the integration path can only reach the bearing branches, because
/// TickCombat/TickEngage always hold a non-null target.
/// </summary>
[TestClass]
public class NpcFiringSlotSelectionTests
{
    private const float NarrowArc = 0.7f;   // ~±45° cone
    private const float WideArc = -1f;      // 360°

    [TestMethod]
    public void NonVehicleEntity_SelectsNothing()
    {
        var creature = new Creature();
        creature.SetCoid(50_001, false);

        var (bit, weapon) = NpcCombatAi.SelectFiringWeapon(creature, creature);

        Assert.AreEqual((byte)0, bit, "foot NPCs have no vehicle weapon slots");
        Assert.IsNull(weapon);
    }

    [TestMethod]
    public void NullEntity_SelectsNothing()
    {
        var (bit, weapon) = NpcCombatAi.SelectFiringWeapon(null, null);
        Assert.AreEqual((byte)0, bit);
        Assert.IsNull(weapon);
    }

    [TestMethod]
    public void NoWeapons_SelectsNothing()
    {
        var vehicle = CreateVehicle();
        var target = CreateTargetAt(0f, 20f);

        var (bit, weapon) = NpcCombatAi.SelectFiringWeapon(vehicle, target);

        Assert.AreEqual((byte)0, bit);
        Assert.IsNull(weapon);
    }

    [TestMethod]
    public void FrontBearsOnTarget_SelectsFront()
    {
        var vehicle = CreateVehicle();
        Equip(vehicle, VehicleEquipmentSlot.WeaponFront, NarrowArc);
        Equip(vehicle, VehicleEquipmentSlot.WeaponTurret, WideArc);

        var (bit, _) = NpcCombatAi.SelectFiringWeapon(vehicle, CreateTargetAt(0f, 20f)); // dead ahead

        Assert.AreEqual((byte)1, bit);
    }

    [TestMethod]
    public void FrontOutOfArc_FallsToTurret()
    {
        var vehicle = CreateVehicle();
        Equip(vehicle, VehicleEquipmentSlot.WeaponFront, NarrowArc);
        Equip(vehicle, VehicleEquipmentSlot.WeaponTurret, WideArc);

        var (bit, _) = NpcCombatAi.SelectFiringWeapon(vehicle, CreateTargetAt(20f, 0f)); // 90° off bow

        Assert.AreEqual((byte)2, bit, "the turret tracks the target, so it always bears");
    }

    [TestMethod]
    public void NoTurret_RearBearsOnTargetBehind_SelectsRear()
    {
        var vehicle = CreateVehicle();
        Equip(vehicle, VehicleEquipmentSlot.WeaponFront, NarrowArc);
        Equip(vehicle, VehicleEquipmentSlot.WeaponRear, NarrowArc);

        var (bit, _) = NpcCombatAi.SelectFiringWeapon(vehicle, CreateTargetAt(0f, -20f)); // behind

        Assert.AreEqual((byte)4, bit, "a rear gun bears on a target astern");
    }

    [TestMethod]
    public void NothingBears_FallsBackToFirableFront()
    {
        var vehicle = CreateVehicle();
        Equip(vehicle, VehicleEquipmentSlot.WeaponFront, NarrowArc);
        Equip(vehicle, VehicleEquipmentSlot.WeaponRear, NarrowArc);

        // Abeam: outside both the forward and the rear cone.
        var (bit, weapon) = NpcCombatAi.SelectFiringWeapon(vehicle, CreateTargetAt(20f, 0f));

        Assert.AreEqual((byte)1, bit, "fallback keeps aim/range behaviour on a firable slot");
        Assert.IsNotNull(weapon);
    }

    [TestMethod]
    public void NullTarget_FallsBackThroughFirableSlots()
    {
        var frontOnly = CreateVehicle();
        Equip(frontOnly, VehicleEquipmentSlot.WeaponFront, NarrowArc);
        Assert.AreEqual((byte)1, NpcCombatAi.SelectFiringWeapon(frontOnly, null).Bit);

        var turretOnly = CreateVehicle();
        Equip(turretOnly, VehicleEquipmentSlot.WeaponTurret, NarrowArc);
        Assert.AreEqual((byte)2, NpcCombatAi.SelectFiringWeapon(turretOnly, null).Bit);

        var rearOnly = CreateVehicle();
        Equip(rearOnly, VehicleEquipmentSlot.WeaponRear, NarrowArc);
        Assert.AreEqual((byte)4, NpcCombatAi.SelectFiringWeapon(rearOnly, null).Bit);
    }

    /// <summary>
    /// SS-49: an unfirable slot (no clonebase) must never shadow a firable one — TryFireSlot
    /// refuses it, so selecting it raises a firing bit that produces nothing.
    /// </summary>
    [TestMethod]
    public void UnfirableSlots_NeverShadowFirableOnes()
    {
        var turretRescue = CreateVehicle();
        EquipClonebaseless(turretRescue, VehicleEquipmentSlot.WeaponFront);
        Equip(turretRescue, VehicleEquipmentSlot.WeaponTurret, NarrowArc);
        Assert.AreEqual((byte)2, NpcCombatAi.SelectFiringWeapon(turretRescue, CreateTargetAt(0f, 20f)).Bit,
            "an unfirable front must not shadow a firable turret");

        var rearRescue = CreateVehicle();
        EquipClonebaseless(rearRescue, VehicleEquipmentSlot.WeaponFront);
        EquipClonebaseless(rearRescue, VehicleEquipmentSlot.WeaponTurret);
        Equip(rearRescue, VehicleEquipmentSlot.WeaponRear, NarrowArc);
        Assert.AreEqual((byte)4, NpcCombatAi.SelectFiringWeapon(rearRescue, CreateTargetAt(20f, 0f)).Bit,
            "fallback must skip unfirable slots to reach the firable rear");
    }

    /// <summary>Nothing firable at all: degrade to the equipped slot so aim/range are unchanged.</summary>
    [TestMethod]
    public void NoFirableSlots_DegradesToEquippedSlot()
    {
        var front = CreateVehicle();
        EquipClonebaseless(front, VehicleEquipmentSlot.WeaponFront);
        Assert.AreEqual((byte)1, NpcCombatAi.SelectFiringWeapon(front, CreateTargetAt(0f, 20f)).Bit);

        var turret = CreateVehicle();
        EquipClonebaseless(turret, VehicleEquipmentSlot.WeaponTurret);
        Assert.AreEqual((byte)2, NpcCombatAi.SelectFiringWeapon(turret, CreateTargetAt(0f, 20f)).Bit);

        var rear = CreateVehicle();
        EquipClonebaseless(rear, VehicleEquipmentSlot.WeaponRear);
        Assert.AreEqual((byte)4, NpcCombatAi.SelectFiringWeapon(rear, CreateTargetAt(0f, 20f)).Bit);
    }

    // ----- helpers -------------------------------------------------------------------------

    private static Vehicle CreateVehicle()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(50_100 + _nextCoid++, false);
        vehicle.Position = new Vector3(0f, 0f, 0f);
        vehicle.Rotation = new Quaternion(0f, 0f, 0f, 1f); // yaw 0 → forward +Z
        return vehicle;
    }

    private static int _nextCoid;

    private static Creature CreateTargetAt(float x, float z)
    {
        var target = new Creature();
        target.SetCoid(50_500 + _nextCoid++, false);
        target.Position = new Vector3(x, 0f, z);
        return target;
    }

    private static void Equip(Vehicle vehicle, VehicleEquipmentSlot slot, float validArc)
    {
        var spec = new WeaponSpecific
        {
            RangeMin = 0f,
            RangeMax = 50f,
            RechargeTime = 1,
            ValidArc = validArc,
            MinMin = DamageSpecific.CreateEmpty(),
            MaxMax = DamageSpecific.CreateEmpty(),
        };
        var cloneBase = (CloneBaseWeapon)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseWeapon));
        cloneBase.WeaponSpecific = spec;
        cloneBase.SimpleObjectSpecific = new SimpleObjectSpecific();
        cloneBase.CloneBaseSpecific = new CloneBaseSpecific
        {
            CloneBaseId = 50_900,
            Type = (int)CloneBaseObjectType.Weapon,
        };

        var weapon = new Weapon();
        weapon.SetCoid(50_700 + _nextCoid++, false);
        typeof(ClonedObjectBase).GetProperty(nameof(ClonedObjectBase.CloneBaseObject))!
            .SetValue(weapon, cloneBase);
        Assert.IsTrue(vehicle.TryEquipItem(slot, weapon, out _), $"failed to equip {slot}");
    }

    private static void EquipClonebaseless(Vehicle vehicle, VehicleEquipmentSlot slot)
    {
        var weapon = new Weapon();
        weapon.SetCoid(50_800 + _nextCoid++, false);
        Assert.IsTrue(vehicle.TryEquipItem(slot, weapon, out _), $"failed to equip {slot}");
        Assert.IsNull(weapon.CloneBaseWeapon, "precondition: unfirable slot");
    }
}

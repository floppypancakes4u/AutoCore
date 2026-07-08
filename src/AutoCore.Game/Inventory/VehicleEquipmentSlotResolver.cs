namespace AutoCore.Game.Inventory;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;

/// <summary>
/// Resolves which vehicle hardpoint a cargo item should equip into.
/// Weapon front/turret/rear come from WeaponSpecific.Flags bits observed in the
/// client unequip/equip helpers (0x02 front, 0x10 turret, 0x04 rear).
/// </summary>
public static class VehicleEquipmentSlotResolver
{
    public const byte WeaponFlagFront = 0x02;
    public const byte WeaponFlagRear = 0x04;
    public const byte WeaponFlagTurret = 0x10;

    public static bool TryResolve(
        CloneBaseObjectType type,
        CloneBase cloneBase,
        byte dropPositionX,
        out VehicleEquipmentSlot slot)
    {
        switch (type)
        {
            case CloneBaseObjectType.Armor:
                slot = VehicleEquipmentSlot.Armor;
                return true;

            case CloneBaseObjectType.PowerPlant:
                slot = VehicleEquipmentSlot.PowerPlant;
                return true;

            case CloneBaseObjectType.WheelSet:
                slot = VehicleEquipmentSlot.WheelSet;
                return true;

            case CloneBaseObjectType.Ornament:
                slot = VehicleEquipmentSlot.Ornament;
                return true;

            case CloneBaseObjectType.RaceItem:
                slot = VehicleEquipmentSlot.RaceItem;
                return true;

            case CloneBaseObjectType.Weapon:
                return TryResolveWeaponSlot(cloneBase as CloneBaseWeapon, dropPositionX, out slot);

            default:
                slot = default;
                return false;
        }
    }

    public static bool TryResolveWeaponSlot(CloneBaseWeapon weapon, byte dropPositionX, out VehicleEquipmentSlot slot)
    {
        if (weapon == null)
        {
            slot = default;
            return false;
        }

        // Melee subtype 9 attaches via a dedicated client path (vehicle+0x264 melee).
        if (weapon.WeaponSpecific.SubType == 9)
        {
            slot = VehicleEquipmentSlot.WeaponMelee;
            return true;
        }

        var flags = weapon.WeaponSpecific.Flags;
        if ((flags & WeaponFlagFront) != 0)
        {
            slot = VehicleEquipmentSlot.WeaponFront;
            return true;
        }

        if ((flags & WeaponFlagTurret) != 0)
        {
            slot = VehicleEquipmentSlot.WeaponTurret;
            return true;
        }

        if ((flags & WeaponFlagRear) != 0)
        {
            slot = VehicleEquipmentSlot.WeaponRear;
            return true;
        }

        // Fallback: client hardpoint drop X as 0/1/2 front/turret/rear.
        slot = dropPositionX switch
        {
            0 => VehicleEquipmentSlot.WeaponFront,
            1 => VehicleEquipmentSlot.WeaponTurret,
            2 => VehicleEquipmentSlot.WeaponRear,
            _ => default
        };

        return dropPositionX <= 2;
    }
}

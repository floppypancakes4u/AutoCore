namespace AutoCore.Game.Inventory;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;

/// <summary>
/// Resolves which vehicle hardpoint a cargo item should equip into.
/// Weapon front/turret/rear come from WeaponSpecific.Flags bits observed in the
/// client unequip/equip helpers (0x02 front, 0x10 turret, 0x04 rear).
///
/// Ghidra (FUN_00502e90 / FUN_00502460 / FUN_004fe620):
/// clonebase type 6 (Item) uses SimpleObjectSpecific.SubType:
///   0x0A (10) → Ornament hardpoint (vehicle+0x26c)
///   0x0B (11) → RaceItem / hazard-shield hardpoint (vehicle+0x270)
/// Retail hazard kits (e.g. CBID 5782) are type Item, not RaceItem=70.
/// </summary>
public static class VehicleEquipmentSlotResolver
{
    public const byte WeaponFlagFront = 0x02;
    public const byte WeaponFlagRear = 0x04;
    public const byte WeaponFlagTurret = 0x10;

    /// <summary>Client Item SubType for ornament hardpoint (FUN_004fe620).</summary>
    public const short ItemSubTypeOrnament = 10;

    /// <summary>Client Item SubType for race-item / hazard hardpoint (FUN_00502460).</summary>
    public const short ItemSubTypeRaceItem = 11;

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

            case CloneBaseObjectType.Item:
                return TryResolveItemSlot(cloneBase, out slot);

            case CloneBaseObjectType.Weapon:
                return TryResolveWeaponSlot(cloneBase as CloneBaseWeapon, dropPositionX, out slot);

            default:
                slot = default;
                return false;
        }
    }

    public static bool TryResolveItemSlot(CloneBase cloneBase, out VehicleEquipmentSlot slot)
    {
        // Prefer SubType when clonebase specifics are loaded (matches client).
        if (cloneBase is CloneBaseObject itemBase)
        {
            switch (itemBase.SimpleObjectSpecific.SubType)
            {
                case ItemSubTypeOrnament:
                    slot = VehicleEquipmentSlot.Ornament;
                    return true;

                case ItemSubTypeRaceItem:
                    slot = VehicleEquipmentSlot.RaceItem;
                    return true;
            }
        }

        // Fallback: retail starter hazard kits are Item-typed RaceItem modules.
        // Unequip→cargo keeps Type=Item; without this, re-equip fails when SubType
        // is unavailable on the clonebase instance passed into TryResolve.
        slot = VehicleEquipmentSlot.RaceItem;
        return true;
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

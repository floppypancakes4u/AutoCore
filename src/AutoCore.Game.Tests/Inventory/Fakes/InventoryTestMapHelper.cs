using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Inventory.Fakes;

public static class InventoryTestMapHelper
{
    public static SectorMap CreateMap(long localCoidCounter = 1000)
    {
        var map = (SectorMap)RuntimeHelpers.GetUninitializedObject(typeof(SectorMap));
        map.LocalCoidCounter = localCoidCounter;
        return map;
    }

    public static void AttachMap(Character character, long localCoidCounter = 1000)
    {
        var map = CreateMap(localCoidCounter);
        typeof(ClonedObjectBase)
            .GetProperty(nameof(ClonedObjectBase.Map), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(character, map);
    }
}

public static class AssetManagerTestHelper
{
    private static readonly Dictionary<int, CloneBase> Registered = new();

    public static void RegisterCloneBase(int cbid, CloneBaseObjectType type)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)type, CloneBaseId = cbid };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    /// <summary>Registers a fake <see cref="CloneBaseVehicle"/> for spawn-flow tests.</summary>
    public static void RegisterVehicleCloneBase(
        int cbid,
        int defaultDriverCbid = 0,
        int defaultWheelsetCbid = 0,
        int faction = 0,
        short maxHitPoint = 100,
        short armorAdd = 0,
        short inventorySlots = 1)
    {
        var clone = (CloneBaseVehicle)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseVehicle));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Vehicle, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            Faction = faction,
            MaxHitPoint = maxHitPoint,
        };
        clone.VehicleSpecific = new VehicleSpecific
        {
            DefaultDriver = defaultDriverCbid,
            DefaultWheelset = defaultWheelsetCbid,
            ArmorAdd = armorAdd,
            InventorySlots = inventorySlots,
        };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    /// <summary>Registers a fake <see cref="CloneBaseArmor"/> with an ArmorFactor for HP tests.</summary>
    public static void RegisterArmorCloneBase(int cbid, short armorFactor = 10, short defenseBonus = 0)
    {
        var clone = (CloneBaseArmor)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseArmor));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Armor, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { MaxHitPoint = 1 };
        clone.ArmorSpecific = new ArmorSpecific
        {
            ArmorFactor = armorFactor,
            DefenseBonus = defenseBonus,
            DeflectionModifier = 0f,
            Resistances = default,
        };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    /// <summary>Registers a fake <see cref="CloneBaseCharacter"/> with race/class for HP formula tests.</summary>
    public static void RegisterCharacterCloneBase(int cbid, byte race = 0, byte classId = 0)
    {
        var clone = (CloneBaseCharacter)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseCharacter));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Character, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { MaxHitPoint = 100 };
        clone.CharacterSpecific = new CharacterSpecific
        {
            Race = race,
            Class = classId,
            HPStart = 100,
            HPFactor = 1,
        };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    /// <summary>Registers a fake <see cref="CloneBaseCreature"/> (with AIBehavior/BaseLevel) for spawn-flow tests.</summary>
    public static void RegisterCreatureCloneBase(
        int cbid,
        int aiBehaviorId = 0,
        short baseLevel = 1,
        int faction = 0,
        short maxHitPoint = 100)
    {
        var clone = (CloneBaseCreature)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseCreature));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Creature, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            Faction = faction,
            MaxHitPoint = maxHitPoint,
        };
        clone.CreatureSpecific = new CreatureSpecific { AIBehavior = aiBehaviorId, BaseLevel = baseLevel };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    /// <summary>
    /// Registers a fake <see cref="CloneBasePowerPlant"/> so <see cref="Entities.PowerPlant.WriteToPacket"/>
    /// can resolve PowerPlantSpecific / Mass without a WAD.
    /// </summary>
    public static void RegisterPowerPlantCloneBase(int cbid, float mass = 1.0f)
    {
        var clone = (CloneBasePowerPlant)RuntimeHelpers.GetUninitializedObject(typeof(CloneBasePowerPlant));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.PowerPlant, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { Mass = mass };
        clone.PowerPlantSpecific = new PowerPlantSpecific
        {
            HeatMaximum = 100,
            PowerMaximum = 100,
            PowerRegenRate = 10,
            CoolRate = 10,
        };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    public static void ClearRegisteredCloneBases()
    {
        var dictionary = GetCloneBasesDictionary();
        foreach (var cbid in Registered.Keys)
            dictionary.Remove(cbid);

        Registered.Clear();
    }

    private static Dictionary<int, CloneBase> GetCloneBasesDictionary()
    {
        var wadLoader = typeof(AssetManager)
            .GetProperty("WADLoader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(AssetManager.Instance);

        return (Dictionary<int, CloneBase>)wadLoader!
            .GetType()
            .GetProperty(nameof(WADLoader.CloneBases))!
            .GetValue(wadLoader)!;
    }
}

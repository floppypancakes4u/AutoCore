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
    public static void RegisterVehicleCloneBase(int cbid, int defaultDriverCbid = 0, int defaultWheelsetCbid = 0)
    {
        var clone = (CloneBaseVehicle)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseVehicle));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Vehicle, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific();
        clone.VehicleSpecific = new VehicleSpecific
        {
            DefaultDriver = defaultDriverCbid,
            DefaultWheelset = defaultWheelsetCbid,
        };
        Registered[cbid] = clone;
        GetCloneBasesDictionary()[cbid] = clone;
    }

    /// <summary>Registers a fake <see cref="CloneBaseCreature"/> (with AIBehavior/BaseLevel) for spawn-flow tests.</summary>
    public static void RegisterCreatureCloneBase(int cbid, int aiBehaviorId = 0, short baseLevel = 1)
    {
        var clone = (CloneBaseCreature)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseCreature));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Creature, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific();
        clone.CreatureSpecific = new CreatureSpecific { AIBehavior = aiBehaviorId, BaseLevel = baseLevel };
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

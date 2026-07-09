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

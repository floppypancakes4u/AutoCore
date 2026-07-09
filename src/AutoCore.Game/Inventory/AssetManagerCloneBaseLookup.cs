namespace AutoCore.Game.Inventory;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Managers;
using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
public sealed class AssetManagerCloneBaseLookup : ICloneBaseLookup
{
    public static AssetManagerCloneBaseLookup Instance { get; } = new();

    public CloneBase GetCloneBase(int cbid) => AssetManager.Instance.GetCloneBase(cbid);
}

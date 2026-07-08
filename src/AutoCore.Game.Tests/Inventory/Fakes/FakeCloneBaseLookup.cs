using AutoCore.Game.CloneBases;
using AutoCore.Game.Inventory;

namespace AutoCore.Game.Tests.Inventory.Fakes;

public sealed class FakeCloneBaseLookup : ICloneBaseLookup
{
    private readonly Dictionary<int, CloneBase> _cloneBases = new();

    public void Register(int cbid, CloneBase cloneBase) => _cloneBases[cbid] = cloneBase;

    public CloneBase GetCloneBase(int cbid) =>
        _cloneBases.TryGetValue(cbid, out var cloneBase) ? cloneBase : null;
}

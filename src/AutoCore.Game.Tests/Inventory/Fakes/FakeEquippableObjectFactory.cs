using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;

namespace AutoCore.Game.Tests.Inventory.Fakes;

public sealed class FakeEquippableObjectFactory : IEquippableObjectFactory
{
    private readonly Dictionary<int, Func<long, bool, SimpleObject>> _creators = new();

    public void Register(int cbid, Func<long, bool, SimpleObject> creator) =>
        _creators[cbid] = creator;

    public void Register(int cbid, SimpleObject template)
    {
        _creators[cbid] = (coid, global) =>
        {
            template.SetCoid(coid, global);
            return template;
        };
    }

    public SimpleObject Create(int cbid, CloneBaseObjectType type, long coid, bool global)
    {
        if (!_creators.TryGetValue(cbid, out var creator))
            return null;

        return creator(coid, global);
    }
}

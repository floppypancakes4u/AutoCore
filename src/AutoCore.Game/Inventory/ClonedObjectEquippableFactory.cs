namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
public sealed class ClonedObjectEquippableFactory : IEquippableObjectFactory
{
    public static ClonedObjectEquippableFactory Instance { get; } = new();

    public SimpleObject Create(int cbid, CloneBaseObjectType type, long coid, bool global)
    {
        var created = ClonedObjectBase.AllocateNewObjectFromCBID(cbid);
        if (created is not SimpleObject simpleObject)
            return null;

        simpleObject.SetCoid(coid, global);
        simpleObject.LoadCloneBase(cbid);
        return simpleObject;
    }
}

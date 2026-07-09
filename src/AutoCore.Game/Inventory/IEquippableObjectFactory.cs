namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;

public interface IEquippableObjectFactory
{
    SimpleObject Create(int cbid, CloneBaseObjectType type, long coid, bool global);
}

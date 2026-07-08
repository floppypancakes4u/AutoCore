namespace AutoCore.Game.Inventory;

using AutoCore.Game.CloneBases;

public interface ICloneBaseLookup
{
    CloneBase GetCloneBase(int cbid);
}

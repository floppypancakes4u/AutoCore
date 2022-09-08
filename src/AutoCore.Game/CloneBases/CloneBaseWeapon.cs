namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseWeapon : CloneBaseObject
{
    public WeaponSpecific WeaponSpecific;

    public CloneBaseWeapon(BinaryReader reader)
        : base(reader)
    {
        WeaponSpecific = WeaponSpecific.ReadNew(reader);
    }
}

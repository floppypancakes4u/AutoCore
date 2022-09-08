namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseArmor : CloneBaseObject
{
    public ArmorSpecific ArmorSpecific { get; set; }

    public CloneBaseArmor(BinaryReader reader)
        : base(reader)
    {
        ArmorSpecific = ArmorSpecific.ReadNew(reader);
    }
}

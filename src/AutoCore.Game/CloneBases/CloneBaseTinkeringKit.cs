namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseTinkeringKit : CloneBaseObject
{
    public TinkeringKitSpecific TinkeringKitSpecific;

    public CloneBaseTinkeringKit(BinaryReader reader)
        : base(reader)
    {
        TinkeringKitSpecific = TinkeringKitSpecific.ReadNew(reader);
    }
}

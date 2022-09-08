namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseGadget : CloneBaseObject
{
    public GadgetSpecific GadgetSpecific;

    public CloneBaseGadget(BinaryReader reader)
        : base(reader)
    {
        GadgetSpecific = GadgetSpecific.ReadNew(reader);
    }
}

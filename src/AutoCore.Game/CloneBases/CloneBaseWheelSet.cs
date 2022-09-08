namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseWheelSet : CloneBaseObject
{
    public WheelSetSpecific WheelSetSpecific;

    public CloneBaseWheelSet(BinaryReader reader)
        : base(reader)
    {
        WheelSetSpecific = WheelSetSpecific.ReadNew(reader);
    }
}

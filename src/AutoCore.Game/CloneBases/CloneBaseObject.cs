namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseObject : CloneBase
{
    public SimpleObjectSpecific SimpleObjectSpecific;

    public CloneBaseObject(BinaryReader reader)
        : base(reader)
    {
        SimpleObjectSpecific = SimpleObjectSpecific.ReadNew(reader);
    }
}

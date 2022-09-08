namespace AutoCore.Game.CloneBases;

using AutoCore.Game.Constants;
using AutoCore.Game.CloneBases.Specifics;

public class CloneBase
{
    public CloneBaseSpecific CloneBaseSpecific { get; set; }

    public CloneBase(BinaryReader reader)
    {
        CloneBaseSpecific = CloneBaseSpecific.ReadNew(reader);
    }

    public CloneBaseObjectType Type => (CloneBaseObjectType)CloneBaseSpecific.Type;
}

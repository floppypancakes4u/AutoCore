namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseCreature : CloneBaseObject
{
    public CreatureSpecific CreatureSpecific;

    public CloneBaseCreature(BinaryReader reader)
        : base(reader)
    {
        CreatureSpecific = CreatureSpecific.ReadNew(reader);
    }
}

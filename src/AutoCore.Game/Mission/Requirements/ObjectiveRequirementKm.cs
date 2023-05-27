using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public enum KmMode
{
    KeepTrack = 0,
    Player = 1,
    Vehicle = 2
}

public class ObjectiveRequirementKm : ObjectiveRequirement
{
    public float DistanceNeeded { get; set; }
    public KmMode Mode { get; set; }

    public ObjectiveRequirementKm(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Km;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var distNeeded = elem.Element("DistanceNeeded");
        if (distNeeded != null && !distNeeded.IsEmpty)
            DistanceNeeded = (float)distNeeded;

        var kmMode = elem.Element("Mode");
        if (kmMode != null && !kmMode.IsEmpty)
            Mode = (KmMode)(int)kmMode;
    }
}

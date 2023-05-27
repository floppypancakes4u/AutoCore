using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementStunt : ObjectiveRequirement
{
    public float Height { get; set; }
    public float Distance { get; set; }
    public float Time { get; set; }

    public ObjectiveRequirementStunt(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Stunt;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var height = elem.Element("Height");
        if (height != null && !height.IsEmpty)
            Height = (float)height;

        var distance = elem.Element("MaxEscortDistance");
        if (distance != null && !distance.IsEmpty)
            Distance = (float)distance;

        var time = elem.Element("MaxEscortDistance");
        if (time != null && !time.IsEmpty)
            Time = (float)time;
    }
}

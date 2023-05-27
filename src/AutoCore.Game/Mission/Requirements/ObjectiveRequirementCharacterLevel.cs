using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementCharacterLevel : ObjectiveRequirement
{
    public int RequiredLevel { get; set; }

    public ObjectiveRequirementCharacterLevel(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.CharacterLevel;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var reqLevel = elem.Element("CharacterLevel");
        if (reqLevel != null && !reqLevel.IsEmpty)
            RequiredLevel = (int)reqLevel;
    }
}

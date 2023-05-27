using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementMission : ObjectiveRequirement
{
    public List<int> MissionIds { get; } = new();
    public int CountNeeded { get; set; }
    public bool IdsAreMedals { get; set; }

    public ObjectiveRequirementMission(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Mission;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var ids = elem.Element("IDs");
        if (ids != null && !ids.IsEmpty)
        {
            var str = (string)ids;
            foreach (var id in str.Split(new[] { '|' }).Where(id => !string.IsNullOrWhiteSpace(id)))
                MissionIds.Add(int.Parse(id));
        }

        var countNeeded = elem.Element("CountNeeded");
        if (countNeeded != null && !countNeeded.IsEmpty)
            CountNeeded = (int)countNeeded;

        var idsAreMedals = elem.Element("IDsAreMedals");
        if (idsAreMedals != null && !idsAreMedals.IsEmpty)
            IdsAreMedals = (int)idsAreMedals != 0;
    }
}

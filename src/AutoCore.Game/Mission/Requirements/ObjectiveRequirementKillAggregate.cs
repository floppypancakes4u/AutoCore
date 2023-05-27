using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementKillAggregate : ObjectiveRequirement
{
    public List<int> Targets { get; } = new();
    public List<int> TemplateTargets { get; } = new();
    public int AllowedType { get; set; } = -1;
    public bool TrackDamage { get; set; }
    public bool TargetIsFaction { get; set; }
    public int ContinentId { get; set; } = -1;
    public int NumToKill { get; set; }
    public bool NegativeKill { get; set; }
    public string ShortDescription { get; set; }

    public ObjectiveRequirementKillAggregate(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.KillAggregate;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var contCBID = elem.Element("ContinentCBID");
        if (contCBID != null && !contCBID.IsEmpty)
            ContinentId = (int)contCBID;

        var cbid = elem.Element("CBID");
        if (cbid != null && !cbid.IsEmpty)
        {
            var str = (string)cbid;
            foreach (var id in str.Split(new[] { '|' }).Where(id => !string.IsNullOrWhiteSpace(id)))
                Targets.Add(int.Parse(id));
        }

        var templId = elem.Element("TEMPLATEID");
        if (templId != null && !templId.IsEmpty)
        {
            var str = (string)templId;
            foreach (var id in str.Split(new[] { '|' }).Where(id => !string.IsNullOrWhiteSpace(id)))
                TemplateTargets.Add(int.Parse(id));
        }

        var negativeKill = elem.Element("NegativeKill");
        if (negativeKill != null && !negativeKill.IsEmpty)
            NegativeKill = (int)negativeKill != 0;

        var numToKill = elem.Element("NumToKill");
        if (numToKill != null && !numToKill.IsEmpty)
            NumToKill = (int)numToKill;

        var tIsFaction = elem.Element("TargetIsFaction");
        if (tIsFaction != null && !tIsFaction.IsEmpty)
            TargetIsFaction = (int)tIsFaction != 0;

        var allowedType = elem.Element("AllowedType");
        if (allowedType != null && !allowedType.IsEmpty)
            AllowedType = (int)allowedType;

        var shortDescr = elem.Element("ShortDescription");
        if (shortDescr != null && !shortDescr.IsEmpty)
            ShortDescription = (string)shortDescr;
    }
}

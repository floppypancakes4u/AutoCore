using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementEscort : ObjectiveRequirement
{
    public int SkillId { get; set; }
    public int SkillLevel { get; set; }
    public bool FailOnSummonDeath { get; set; }
    public float FailDistance { get; set; }
    public int ContinentId { get; set; } = -1;
    public long CompletionPatrol { get; set; } = -1;
    public float CompletionDistance { get; set; }
    public long FailPatrol { get; set; } = -1;
    public float FailPatrolDistance { get; set; }
    public bool StartEscort { get; set; } = true;
    public bool EndEscort { get; set; } = true;
    public int CachedCreatureId { get; set; } = -1;

    public ObjectiveRequirementEscort(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Escort;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var skillId = elem.Element("SkillID");
        if (skillId != null && !skillId.IsEmpty)
            SkillId = (int)skillId;

        var skillLvl = elem.Element("SkillLevel");
        if (skillLvl != null && !skillLvl.IsEmpty)
            SkillLevel = (int)skillLvl;

        var failOnSummDeath = elem.Element("FailOnDeath");
        if (failOnSummDeath != null && !failOnSummDeath.IsEmpty)
            FailOnSummonDeath = (int)failOnSummDeath != 0;

        var failDist = elem.Element("MaxDistance");
        if (failDist != null && !failDist.IsEmpty)
            FailDistance = (float)failDist;

        var contCBID = elem.Element("ContinentCBID");
        if (contCBID != null && !contCBID.IsEmpty)
            ContinentId = (int)contCBID;

        var compPatrol = elem.Element("CompletionCOID");
        if (compPatrol != null && !compPatrol.IsEmpty)
            CompletionPatrol = (long)compPatrol;

        var compPatrolDist = elem.Element("CompletionPatrolDistance");
        if (compPatrolDist != null && !compPatrolDist.IsEmpty)
            CompletionDistance = (float)compPatrolDist;

        var failPatrol = elem.Element("FailCOID");
        if (failPatrol != null && !failPatrol.IsEmpty)
            FailPatrol = (long)failPatrol;

        var failPatrolDist = elem.Element("FailPatrolDistance");
        if (failPatrolDist != null && !failPatrolDist.IsEmpty)
            FailPatrolDistance = (float)failPatrolDist;

        var startEscort = elem.Element("StartEscort");
        if (startEscort != null && !startEscort.IsEmpty)
            StartEscort = (int)startEscort != 0;

        var endEscort = elem.Element("EndEscort");
        if (endEscort != null && !endEscort.IsEmpty)
            EndEscort = (int)endEscort != 0;
    }
}

using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementPatrol : ObjectiveRequirement
{
    public bool AutoComplete { get; set; }
    public float AutoCompleteDistance { get; set; }
    public bool AutoFail { get; set; }
    public float AutoFailDistance { get; set; }
    public int ContinentId { get; set; } = -1;
    public int TargetCount { get; set; }
    public long[] GenericTargets { get; } = new long[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };
    public int Laps { get; set; } = 1;
    public bool Sequential { get; set; } = true;

    public ObjectiveRequirementPatrol(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Patrol;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var autoComp = elem.Element("AutoComplete");
        if (autoComp != null && !autoComp.IsEmpty)
            AutoComplete = (int)autoComp != 0;

        var autoCompDist = elem.Element("AutoCompleteDistance");
        if (autoCompDist != null && !autoCompDist.IsEmpty)
            AutoCompleteDistance = (float)autoCompDist;

        var autoFail = elem.Element("AutoFail");
        if (autoFail != null && !autoFail.IsEmpty)
            AutoFail = (int)autoFail != 0;

        var autoFailDist = elem.Element("AutoFailDistance");
        if (autoFailDist != null && !autoFailDist.IsEmpty)
            AutoFailDistance = (float)autoFailDist;

        var contId = elem.Element("ContinentCBID");
        if (contId != null && !contId.IsEmpty)
            ContinentId = (int)contId;

        var laps = elem.Element("Laps");
        if (laps != null && !laps.IsEmpty)
            Laps = (int)laps;

        foreach (var el in elem.Elements("GenericTargetCOID"))
            if (TargetCount < 10)
                GenericTargets[TargetCount++] = (long)el;
    }
}

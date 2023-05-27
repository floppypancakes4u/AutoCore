using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementKill : ObjectiveRequirement
{
    public bool TargetIsTemplateVehicle { get; set; }
    public bool TrackDamage { get; set; }
    public int NumToKill { get; set; }
    public bool NegativeKill { get; set; }
    public bool TargetIsPlayer { get; set; }
    public bool LevelRestriction { get; set; }
    public bool TargetIsFaction { get; set; }
    public int TargetCBID { get; set; } = -1;
    public int ContinentId { get; set; } = -1;
    public int AllowedType { get; set; } = -1;
    public int AllowedClass { get; set; } = -1;
    public int MinLevel { get; set; } = -1;
    public int MaxLevel { get; set; } = -1;
    public float MaxEscortDistance { get; set; }

    public ObjectiveRequirementKill(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Kill;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var levelMin = elem.Element("ReqireLevelMin");
        if (levelMin != null && !levelMin.IsEmpty)
        {
            MinLevel = (int)levelMin;
            LevelRestriction = true;
        }

        var levelMax = elem.Element("RequireLevelMax");
        if (levelMax != null && !levelMax.IsEmpty)
        {
            MaxLevel = (int)levelMax;
            LevelRestriction = true;
        }

        var allowedClass = elem.Element("AllowedClass");
        if (allowedClass != null && !allowedClass.IsEmpty)
            AllowedClass = (int)allowedClass;

        var allowedType = elem.Element("AllowedType");
        if (allowedType != null && !allowedType.IsEmpty)
            AllowedType = (int)allowedType;

        var trackDamage = elem.Element("TrackDamage");
        if (trackDamage != null && !trackDamage.IsEmpty)
            TrackDamage = (int)trackDamage != 0;

        var tIsTemplVeh = elem.Element("TargetIsTemplateVehicle");
        if (tIsTemplVeh != null && !tIsTemplVeh.IsEmpty)
            TargetIsTemplateVehicle = (int)tIsTemplVeh != 0;

        var tIsFaction = elem.Element("TargetIsFaction");
        if (tIsFaction != null && !tIsFaction.IsEmpty)
            TargetIsFaction = (int)tIsFaction != 0;

        var tIsPlayer = elem.Element("TargetIsPlayer");
        if (tIsPlayer != null && !tIsPlayer.IsEmpty)
            TargetIsPlayer = (int)tIsPlayer != 0;

        var maxEscortDist = elem.Element("MaxEscortDistance");
        if (maxEscortDist != null && !maxEscortDist.IsEmpty)
            MaxEscortDistance = (float)maxEscortDist;

        var negativeKill = elem.Element("NegativeKill");
        if (negativeKill != null && !negativeKill.IsEmpty)
            NegativeKill = (int)negativeKill != 0;

        var numToKill = elem.Element("NumToKill");
        if (numToKill != null && !numToKill.IsEmpty)
            NumToKill = (int)numToKill;

        var cbid = elem.Element("CBID");
        if (cbid != null && !cbid.IsEmpty)
            TargetCBID = (int)cbid;

        var contCBID = elem.Element("ContinentCBID");
        if (contCBID != null && !contCBID.IsEmpty)
            ContinentId = (int)contCBID;
    }
}

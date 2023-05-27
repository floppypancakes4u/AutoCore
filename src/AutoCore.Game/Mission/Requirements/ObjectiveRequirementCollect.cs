using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementCollect : ObjectiveRequirement
{
    public int ItemCBID { get; set; } = -1;
    public int ContinentId { get; set; } = -1;
    public int AllowedType { get; set; } = -1;
    public int AllowedClass { get; set; } = -1;
    public int MinLevel { get; set; } = -1;
    public int MaxLevel { get; set; } = -1;
    public bool TargetIsPlayer { get; set; }
    public bool TargetIsTemplateVehicle { get; set; }
    public bool LevelRestriction { get; set; }
    public bool TakeItems { get; set; }
    public bool GiveToAllConvoyMembers { get; set; }
    public int TargetCount { get; set; } = 0;
    public int NumToCollect { get; set; }
    public float OptionalDropPercent { get; set; }
    public int[] OptinonalTargets { get; set; } = new[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1 };

    public ObjectiveRequirementCollect(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Collect;
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

        var cbid = elem.Element("CBID");
        if (cbid != null && !cbid.IsEmpty)
            ItemCBID = (int)cbid;

        var contCBID = elem.Element("ContinentCBID");
        if (contCBID != null && !contCBID.IsEmpty)
            ContinentId = (int)contCBID;

        var tIsTemplVeh = elem.Element("TargetIsTemplateVehicle");
        if (tIsTemplVeh != null && !tIsTemplVeh.IsEmpty)
            TargetIsTemplateVehicle = (int)tIsTemplVeh != 0;

        var tIsPlayer = elem.Element("TargetIsPlayer");
        if (tIsPlayer != null && !tIsPlayer.IsEmpty)
            TargetIsPlayer = (int)tIsPlayer != 0;

        var numToCollect = elem.Element("NumToCollect");
        if (numToCollect != null && !numToCollect.IsEmpty)
            NumToCollect = (int)numToCollect;

        var optionalDropPct = elem.Element("OptionalDropPercent");
        if (optionalDropPct != null && !optionalDropPct.IsEmpty)
            OptionalDropPercent = (float)optionalDropPct;

        var takeItems = elem.Element("TakeAllItems");
        if (takeItems != null && !takeItems.IsEmpty)
            TakeItems = (int)takeItems != 0;

        var giveToConvMems = elem.Element("GiveToAllConvoyMembers");
        if (giveToConvMems != null && !giveToConvMems.IsEmpty)
            GiveToAllConvoyMembers = (int)giveToConvMems != 0;

        foreach (var el in elem.Elements("OptionalTargetCBID"))
            if (TargetCount < 10)
                OptinonalTargets[TargetCount++] = (int)el;
    }
}

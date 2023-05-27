using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementDeliver : ObjectiveRequirement
{
    public int ItemCBID { get; set; } = -1;
    public int NumToDeliver { get; set; } = 0;
    public int NPCTargetCBID { get; set; } = -1;
    public int NPCContinentId { get; set; } = -1;
    public bool GiveItemOnStart { get; set; } = true;
    public bool TakeItemAtEnd { get; set; } = true;
    public bool NPCTargetCompletes { get; set; } = true;
    public bool RequireItemToComplete { get; set; } = true;

    public ObjectiveRequirementDeliver(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Deliver;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var cbid = elem.Element("CBIDItem");
        if (cbid != null && !cbid.IsEmpty)
        {
            ItemCBID = (int)cbid;
            RequireItemToComplete = ItemCBID == -1;
        }

        var contCBID = elem.Element("ContinentID");
        if (contCBID != null && !contCBID.IsEmpty)
            NPCContinentId = (int)contCBID;

        var numToDeliver = elem.Element("NumToDeliver");
        if (numToDeliver != null && !numToDeliver.IsEmpty)
            NumToDeliver = (int)numToDeliver;

        var targetCBID = elem.Element("TargetNPCCBID");
        if (targetCBID != null && !targetCBID.IsEmpty)
            NPCTargetCBID = (int)targetCBID;

        var giveItemAtStart = elem.Element("GiveItemAtStart");
        if (giveItemAtStart != null && !giveItemAtStart.IsEmpty)
            GiveItemOnStart = (int)giveItemAtStart != 0;

        var takeItemAtEnd = elem.Element("TakeItemAtEnd");
        if (takeItemAtEnd != null && !takeItemAtEnd.IsEmpty)
            TakeItemAtEnd = (int)takeItemAtEnd != 0;

        var targetCompletes = elem.Element("NPCTargetCompletes");
        if (targetCompletes != null && !targetCompletes.IsEmpty)
            NPCTargetCompletes = (int)targetCompletes != 0;
    }
}

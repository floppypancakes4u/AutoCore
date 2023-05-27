using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementUseItem : ObjectiveRequirement
{
    public long PrimaryItem { get; set; } = -1L;
    public int PrimaryCBID { get; set; } = -1;
    public bool PrimaryDestroy { get; set; }
    public bool PrimaryInWorld { get; set; }
    public string PrimaryUseText { get; set; }
    public bool PrimaryGiveAtStart { get; set; }
    public bool PrimaryMultipleUse { get; set; }
    public bool PrimaryExplode { get; set; }
    public int PrimaryCompletedItem { get; set; } = -1;
    public int SecondaryCBID { get; set; } = -1;
    public bool SecondaryDestroy { get; set; }
    public bool SecondaryGiveAtStart { get; set; }
    public bool SecondaryMultipleUse { get; set; }
    public int ProgressTime { get; set; }
    public string ProgressText { get; set; }
    public bool ProgressInterruptable { get; set; }
    public string ProgressInterruptText { get; set; }
    public string CompleteText { get; set; }
    public int CompletedItem { get; set; } = -1;
    public int CompletedMission { get; set; } = -1;
    public int RepeatCount { get; set; } = 1;
    public int ContinentID { get; set; } = -1;

    public ObjectiveRequirementUseItem(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.UseItem;
    }

    public override void UnSerialize(XElement elem)
    {
        FirstStateSlot = (byte)(int)elem.Attribute("slot");

        var primItem = elem.Element("PrimaryCOID");
        if (primItem != null && !primItem.IsEmpty)
            PrimaryItem = (long)primItem;

        var primCBID = elem.Element("PrimaryCBID");
        if (primCBID != null && !primCBID.IsEmpty)
            PrimaryCBID = (int)primCBID;

        var primDestroy = elem.Element("TargetIsPlayer");
        if (primDestroy != null && !primDestroy.IsEmpty)
            PrimaryDestroy = (int)primDestroy != 0;

        var primInWorld = elem.Element("TargetIsPlayer");
        if (primInWorld != null && !primInWorld.IsEmpty)
            PrimaryInWorld = (int)primInWorld != 0;

        var primUseText = elem.Element("PrimaryUseText");
        if (primUseText != null && !primUseText.IsEmpty)
            PrimaryUseText = (string)primUseText;

        var primGiveStart = elem.Element("PrimaryGiveAtStart");
        if (primGiveStart != null && !primGiveStart.IsEmpty)
            PrimaryGiveAtStart = (int)primGiveStart != 0;

        var primMultiUse = elem.Element("PrimaryMultipleUse");
        if (primMultiUse != null && !primMultiUse.IsEmpty)
            PrimaryMultipleUse = (int)primMultiUse != 0;

        var primExplode = elem.Element("PrimaryExplode");
        if (primExplode != null && !primExplode.IsEmpty)
            PrimaryExplode = (int)primExplode != 0;

        var primCompItem = elem.Element("PrimaryCompletedItem");
        if (primCompItem != null && !primCompItem.IsEmpty)
            PrimaryCompletedItem = (int)primCompItem;

        var secItem = elem.Element("SecondaryCBID");
        if (secItem != null && !secItem.IsEmpty)
            SecondaryCBID = (int)secItem;

        var secDestroy = elem.Element("SecondaryDestroy");
        if (secDestroy != null && !secDestroy.IsEmpty)
            PrimaryDestroy = (int)secDestroy != 0;

        var secGiveStart = elem.Element("SecondaryGiveAtStart");
        if (secGiveStart != null && !secGiveStart.IsEmpty)
            PrimaryDestroy = (int)secGiveStart != 0;

        var secMultiUse = elem.Element("SecondaryMultipleUse");
        if (secMultiUse != null && !secMultiUse.IsEmpty)
            PrimaryDestroy = (int)secMultiUse != 0;

        var progTime = elem.Element("ProgressTime");
        if (progTime != null && !progTime.IsEmpty)
            ProgressTime = (int)progTime;

        var progText = elem.Element("ProgressText");
        if (progText != null && !progText.IsEmpty)
            ProgressText = (string)progText;

        var progInterruptable = elem.Element("ProgressInterruptable");
        if (progInterruptable != null && !progInterruptable.IsEmpty)
            PrimaryDestroy = (int)progInterruptable != 0;

        var progInterrText = elem.Element("ProgressInterruptText");
        if (progInterrText != null && !progInterrText.IsEmpty)
            ProgressInterruptText = (string)progInterrText;

        var completeText = elem.Element("CompleteText");
        if (completeText != null && !completeText.IsEmpty)
            CompleteText = (string)completeText;

        var compItem = elem.Element("CompleteItem");
        if (compItem != null && !compItem.IsEmpty)
            CompletedItem = (int)compItem;

        var compMission = elem.Element("CompletedMission");
        if (compMission != null && !compMission.IsEmpty)
            CompletedMission = (int)compMission;

        var repCount = elem.Element("RepeatCount");
        if (repCount != null && !repCount.IsEmpty)
            RepeatCount = (int)repCount;

        var contId = elem.Element("ContinentID");
        if (contId != null && !contId.IsEmpty)
            ContinentID = (int)contId;
    }
}

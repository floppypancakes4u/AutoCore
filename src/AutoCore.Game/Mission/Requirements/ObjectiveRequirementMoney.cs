using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementMoney : ObjectiveRequirement
{
    public uint MoneyNeeded { get; set; }

    public ObjectiveRequirementMoney(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.Money;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var moneyNeeded = elem.Element("MoneyNeeded");
        if (moneyNeeded != null && !moneyNeeded.IsEmpty)
            MoneyNeeded = (uint)moneyNeeded;
    }
}

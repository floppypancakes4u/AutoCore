using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

using AutoCore.Game.Structures;

public class ObjectiveRequirementCrazyTaxi : ObjectiveRequirement
{
    public List<long> Targets { get; } = new ();
    public List<Reward> MoneyRewards { get; } = new();
    public List<Reward> ExpRewards { get; } = new();
    public List<TimeCurve> TimeLimits { get; } = new();

    public int ContinentId { get; set; } = -1;
    public int TargetCount { get; set; }
    public int FinishMissionCount { get; set; }
    public float VehicleMaxVecAtStop { get; set; }
    public bool GiveExpReward { get; set; }
    public bool GiveMoneyReward { get; set; }
    public bool FinishOnMissionCount { get; set; }
    public float RadiusForTrigger { get; set; }
    public int ReachedTargetCount { get; set; }
    public float Timer { get; set; }

    public ObjectiveRequirementCrazyTaxi(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.CrazyTaxi;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var contCBID = elem.Element("ContinentCBID");
        if (contCBID != null && !contCBID.IsEmpty)
            ContinentId = (int)contCBID;

        var vehMaxVec = elem.Element("VehicleMaxVec");
        if (vehMaxVec != null && !vehMaxVec.IsEmpty)
            VehicleMaxVecAtStop = (float)vehMaxVec;

        var radAtStop = elem.Element("RadiusOfStop");
        if (radAtStop != null && !radAtStop.IsEmpty)
            RadiusForTrigger = (float)radAtStop;

        var missStopLim = elem.Element("MissionStopLimit");
        if (missStopLim != null && !missStopLim.IsEmpty)
            FinishOnMissionCount = (int)missStopLim != 0;

        var missStopCount = elem.Element("MissionStopCount");
        if (missStopCount != null && !missStopCount.IsEmpty)
            FinishMissionCount = (int)missStopCount;

        var giveMoney = elem.Element("GiveMoney");
        if (giveMoney != null && !giveMoney.IsEmpty)
            GiveMoneyReward = (int)giveMoney != 0;

        var giveExp = elem.Element("GiveExp");
        if (giveExp != null && !giveExp.IsEmpty)
            GiveExpReward = (int)giveExp != 0;

        // TODO: End Implement!
    }
}

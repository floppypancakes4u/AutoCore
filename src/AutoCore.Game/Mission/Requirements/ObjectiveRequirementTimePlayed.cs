using System;
using System.Xml.Linq;

namespace AutoCore.Game.Mission.Requirements;

public class ObjectiveRequirementTimePlayed : ObjectiveRequirement
{
    public int SecondsPlayed { get; set; }
    public bool UseTotal { get; set; }
    public bool FailTimer { get; set; }
    public bool ShowTimer { get; set; }
    public string TimerText { get; set; }

    public ObjectiveRequirementTimePlayed(MissionObjective owner)
        : base(owner)
    {
        RequirementType = RequirementType.TimePlayed;
    }

    public override void UnSerialize(XElement elem)
    {
        base.UnSerialize(elem);

        var secsPlayed = elem.Element("SecondsPlayed");
        if (secsPlayed != null && !secsPlayed.IsEmpty)
            SecondsPlayed = (int)secsPlayed;

        var minsPlayed = elem.Element("MinutesPlayed");
        if (minsPlayed != null && !minsPlayed.IsEmpty)
            SecondsPlayed = (int)minsPlayed * 60;

        var useTotal = elem.Element("UseTotal");
        if (useTotal != null && !useTotal.IsEmpty)
            UseTotal = (int)useTotal != 0;

        var failTimer = elem.Element("FailTimer");
        if (failTimer != null && !failTimer.IsEmpty)
            FailTimer = (int)failTimer != 0;

        var showTimer = elem.Element("ShowTimer");
        if (showTimer != null && !showTimer.IsEmpty)
            ShowTimer = (int)showTimer != 0;

        var timerText = elem.Element("TimerText");
        if (timerText != null && !timerText.IsEmpty)
            TimerText = (string)timerText;
    }
}

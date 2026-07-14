using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission;

using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>
/// Data-driven requirement factory + UnSerialize coverage from mission XML elements.
/// </summary>
[TestClass]
public class ObjectiveRequirementUnserializeTests
{
    private static MissionObjective Owner() => MissionObjective.CreateForTests(1, 0, 100, 1);

    private static ObjectiveRequirement Create(string type, string innerXml)
    {
        var xml = $"<Requirement type=\"{type}\" slot=\"2\">{innerXml}</Requirement>";
        var elem = XElement.Parse(xml);
        return ObjectiveRequirement.Create(Owner(), elem);
    }

    [TestMethod]
    public void Create_UnknownType_ReturnsNull()
    {
        Assert.IsNull(Create("rampage", ""));
        Assert.IsNull(Create("not_a_real_type", ""));
    }

    [TestMethod]
    public void Create_Kill_ParsesAllFields()
    {
        var req = (ObjectiveRequirementKill)Create("kill", @"
            <ReqireLevelMin>5</ReqireLevelMin>
            <RequireLevelMax>20</RequireLevelMax>
            <AllowedClass>1</AllowedClass>
            <AllowedType>2</AllowedType>
            <TrackDamage>1</TrackDamage>
            <TargetIsTemplateVehicle>1</TargetIsTemplateVehicle>
            <TargetIsFaction>1</TargetIsFaction>
            <TargetIsPlayer>0</TargetIsPlayer>
            <MaxEscortDistance>40.5</MaxEscortDistance>
            <NegativeKill>1</NegativeKill>
            <NumToKill>3</NumToKill>
            <CBID>777</CBID>
            <ContinentCBID>707</ContinentCBID>");

        Assert.AreEqual(RequirementType.Kill, req.RequirementType);
        Assert.AreEqual(2, req.FirstStateSlot);
        Assert.IsTrue(req.LevelRestriction);
        Assert.AreEqual(5, req.MinLevel);
        Assert.AreEqual(20, req.MaxLevel);
        Assert.AreEqual(1, req.AllowedClass);
        Assert.AreEqual(2, req.AllowedType);
        Assert.IsTrue(req.TrackDamage);
        Assert.IsTrue(req.TargetIsTemplateVehicle);
        Assert.IsTrue(req.TargetIsFaction);
        Assert.IsFalse(req.TargetIsPlayer);
        Assert.AreEqual(40.5f, req.MaxEscortDistance);
        Assert.IsTrue(req.NegativeKill);
        Assert.AreEqual(3, req.NumToKill);
        Assert.AreEqual(777, req.TargetCBID);
        Assert.AreEqual(707, req.ContinentId);
    }

    [TestMethod]
    public void Create_KillAggregate_ParsesPipeSeparatedTargets()
    {
        var req = (ObjectiveRequirementKillAggregate)Create("kill_aggregate", @"
            <ContinentCBID>10</ContinentCBID>
            <CBID>1|2| 3 </CBID>
            <TEMPLATEID>100|200</TEMPLATEID>
            <NegativeKill>0</NegativeKill>
            <NumToKill>5</NumToKill>
            <TargetIsFaction>1</TargetIsFaction>
            <AllowedType>9</AllowedType>
            <ShortDescription>kill stuff</ShortDescription>");

        Assert.AreEqual(RequirementType.KillAggregate, req.RequirementType);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, req.Targets.ToArray());
        CollectionAssert.AreEqual(new[] { 100, 200 }, req.TemplateTargets.ToArray());
        Assert.AreEqual(5, req.NumToKill);
        Assert.IsTrue(req.TargetIsFaction);
        Assert.AreEqual(9, req.AllowedType);
        Assert.AreEqual("kill stuff", req.ShortDescription);
        Assert.AreEqual(10, req.ContinentId);
    }

    [TestMethod]
    public void Create_Collect_ParsesFieldsAndOptionalTargets()
    {
        var req = (ObjectiveRequirementCollect)Create("collect", @"
            <ReqireLevelMin>1</ReqireLevelMin>
            <RequireLevelMax>10</RequireLevelMax>
            <AllowedClass>2</AllowedClass>
            <AllowedType>3</AllowedType>
            <CBID>2401</CBID>
            <ContinentCBID>707</ContinentCBID>
            <TargetIsTemplateVehicle>0</TargetIsTemplateVehicle>
            <TargetIsPlayer>1</TargetIsPlayer>
            <NumToCollect>4</NumToCollect>
            <OptionalDropPercent>0.25</OptionalDropPercent>
            <TakeAllItems>1</TakeAllItems>
            <GiveToAllConvoyMembers>1</GiveToAllConvoyMembers>
            <OptionalTargetCBID>11</OptionalTargetCBID>
            <OptionalTargetCBID>22</OptionalTargetCBID>");

        Assert.AreEqual(RequirementType.Collect, req.RequirementType);
        Assert.IsTrue(req.LevelRestriction);
        Assert.AreEqual(2401, req.ItemCBID);
        Assert.AreEqual(4, req.NumToCollect);
        Assert.AreEqual(0.25f, req.OptionalDropPercent);
        Assert.IsTrue(req.TakeItems);
        Assert.IsTrue(req.GiveToAllConvoyMembers);
        Assert.IsTrue(req.TargetIsPlayer);
        Assert.AreEqual(2, req.TargetCount);
        Assert.AreEqual(11, req.OptinonalTargets[0]);
        Assert.AreEqual(22, req.OptinonalTargets[1]);
    }

    [TestMethod]
    public void Create_Deliver_ParsesAllFields()
    {
        var req = (ObjectiveRequirementDeliver)Create("deliver", @"
            <CBIDItem>55</CBIDItem>
            <ContinentID>707</ContinentID>
            <NumToDeliver>2</NumToDeliver>
            <TargetNPCCBID>12447</TargetNPCCBID>
            <GiveItemAtStart>0</GiveItemAtStart>
            <TakeItemAtEnd>1</TakeItemAtEnd>
            <NPCTargetCompletes>1</NPCTargetCompletes>");

        Assert.AreEqual(RequirementType.Deliver, req.RequirementType);
        Assert.AreEqual(55, req.ItemCBID);
        Assert.IsFalse(req.RequireItemToComplete); // CBIDItem != -1 clears require
        Assert.AreEqual(707, req.NPCContinentId);
        Assert.AreEqual(2, req.NumToDeliver);
        Assert.AreEqual(12447, req.NPCTargetCBID);
        Assert.IsFalse(req.GiveItemOnStart);
        Assert.IsTrue(req.TakeItemAtEnd);
        Assert.IsTrue(req.NPCTargetCompletes);
    }

    [TestMethod]
    public void Create_Money_Mission_Km_Stunt_CharacterLevel()
    {
        var money = (ObjectiveRequirementMoney)Create("money", "<MoneyNeeded>500</MoneyNeeded>");
        Assert.AreEqual(500u, money.MoneyNeeded);

        var mission = (ObjectiveRequirementMission)Create("mission",
            "<IDs>10|20</IDs><CountNeeded>2</CountNeeded><IDsAreMedals>1</IDsAreMedals>");
        CollectionAssert.AreEqual(new[] { 10, 20 }, mission.MissionIds.ToArray());
        Assert.AreEqual(2, mission.CountNeeded);
        Assert.IsTrue(mission.IdsAreMedals);

        var km = (ObjectiveRequirementKm)Create("km",
            "<DistanceNeeded>12.5</DistanceNeeded><Mode>2</Mode>");
        Assert.AreEqual(12.5f, km.DistanceNeeded);
        Assert.AreEqual(KmMode.Vehicle, km.Mode);

        var stunt = (ObjectiveRequirementStunt)Create("stunt",
            "<Height>3.5</Height><MaxEscortDistance>9.0</MaxEscortDistance>");
        Assert.AreEqual(3.5f, stunt.Height);
        Assert.AreEqual(9.0f, stunt.Distance);
        Assert.AreEqual(9.0f, stunt.Time);

        var level = (ObjectiveRequirementCharacterLevel)Create("characterlevel",
            "<CharacterLevel>15</CharacterLevel>");
        Assert.AreEqual(15, level.RequiredLevel);
    }

    [TestMethod]
    public void Create_TimePlayed_ParsesMinutesAndFlags()
    {
        var req = (ObjectiveRequirementTimePlayed)Create("timeplayed", @"
            <SecondsPlayed>30</SecondsPlayed>
            <MinutesPlayed>2</MinutesPlayed>
            <UseTotal>1</UseTotal>
            <FailTimer>1</FailTimer>
            <ShowTimer>1</ShowTimer>
            <TimerText>hurry</TimerText>");

        // MinutesPlayed overwrites SecondsPlayed
        Assert.AreEqual(120, req.SecondsPlayed);
        Assert.IsTrue(req.UseTotal);
        Assert.IsTrue(req.FailTimer);
        Assert.IsTrue(req.ShowTimer);
        Assert.AreEqual("hurry", req.TimerText);
    }

    [TestMethod]
    public void Create_Patrol_ParsesTargetsAndFlags()
    {
        var req = (ObjectiveRequirementPatrol)Create("patrol", @"
            <AutoComplete>1</AutoComplete>
            <AutoCompleteDistance>25.0</AutoCompleteDistance>
            <AutoFail>1</AutoFail>
            <AutoFailDistance>100.0</AutoFailDistance>
            <ContinentCBID>707</ContinentCBID>
            <Laps>2</Laps>
            <GenericTargetCOID>16279</GenericTargetCOID>
            <GenericTargetCOID>16280</GenericTargetCOID>");

        Assert.IsTrue(req.AutoComplete);
        Assert.AreEqual(25f, req.AutoCompleteDistance);
        Assert.IsTrue(req.AutoFail);
        Assert.AreEqual(100f, req.AutoFailDistance);
        Assert.AreEqual(707, req.ContinentId);
        Assert.AreEqual(2, req.Laps);
        Assert.AreEqual(2, req.TargetCount);
        Assert.AreEqual(16279L, req.GenericTargets[0]);
        Assert.AreEqual(16280L, req.GenericTargets[1]);
    }

    [TestMethod]
    public void Create_Patrol_LoadsMoreThanTenGenericTargets()
    {
        // Track This class: 15 waypoint pads — old UnSerialize capped at 10.
        var targets = string.Join("\n", Enumerable.Range(10310, 15).Select(id =>
            $"<GenericTargetCOID>{id}</GenericTargetCOID>"));
        var req = (ObjectiveRequirementPatrol)Create("patrol", $@"
            <AutoComplete>1</AutoComplete>
            <AutoCompleteDistance>25</AutoCompleteDistance>
            {targets}
            <Laps>1</Laps>");

        Assert.AreEqual(15, req.TargetCount);
        Assert.AreEqual(10310L, req.GenericTargets[0]);
        Assert.AreEqual(10324L, req.GenericTargets[14]);
    }

    [TestMethod]
    public void Create_UseItem_ParsesPrimaryAndProgressFields()
    {
        // Real missions.glm tags (not TargetIsPlayer). Secondary* must not clobber PrimaryDestroy.
        var req = (ObjectiveRequirementUseItem)Create("useitem", @"
            <PrimaryCOID>999</PrimaryCOID>
            <PrimaryCBID>15461</PrimaryCBID>
            <PrimaryDestroy>0</PrimaryDestroy>
            <PrimaryInWorld>1</PrimaryInWorld>
            <PrimaryUseText>use</PrimaryUseText>
            <PrimaryGiveAtStart>1</PrimaryGiveAtStart>
            <PrimaryMultipleUse>1</PrimaryMultipleUse>
            <PrimaryExplode>1</PrimaryExplode>
            <PrimaryCompletedItem>5</PrimaryCompletedItem>
            <SecondaryCBID>6</SecondaryCBID>
            <SecondaryDestroy>1</SecondaryDestroy>
            <SecondaryGiveAtStart>1</SecondaryGiveAtStart>
            <SecondaryMultipleUse>1</SecondaryMultipleUse>
            <ProgressTime>3</ProgressTime>
            <ProgressText>wait</ProgressText>
            <ProgressInterruptable>1</ProgressInterruptable>
            <ProgressInterruptText>stop</ProgressInterruptText>
            <CompleteText>done</CompleteText>
            <CompleteItem>7</CompleteItem>
            <CompletedMission>8</CompletedMission>
            <RepeatCount>2</RepeatCount>
            <ContinentID>707</ContinentID>");

        Assert.AreEqual(999L, req.PrimaryItem);
        Assert.AreEqual(15461, req.PrimaryCBID);
        Assert.IsFalse(req.PrimaryDestroy);
        Assert.IsTrue(req.PrimaryInWorld);
        Assert.AreEqual("use", req.PrimaryUseText);
        Assert.IsTrue(req.PrimaryGiveAtStart);
        Assert.IsTrue(req.PrimaryMultipleUse);
        Assert.IsTrue(req.PrimaryExplode);
        Assert.AreEqual(5, req.PrimaryCompletedItem);
        Assert.AreEqual(6, req.SecondaryCBID);
        Assert.IsTrue(req.SecondaryDestroy);
        Assert.IsTrue(req.SecondaryGiveAtStart);
        Assert.IsTrue(req.SecondaryMultipleUse);
        Assert.AreEqual(3, req.ProgressTime);
        Assert.AreEqual("wait", req.ProgressText);
        Assert.IsTrue(req.ProgressInterruptable);
        Assert.AreEqual("stop", req.ProgressInterruptText);
        Assert.AreEqual("done", req.CompleteText);
        Assert.AreEqual(7, req.CompletedItem);
        Assert.AreEqual(8, req.CompletedMission);
        Assert.AreEqual(2, req.RepeatCount);
        Assert.AreEqual(707, req.ContinentID);
    }

    [TestMethod]
    public void Create_UseItem_PrimaryDestroyAlone_DoesNotRequireSecondaryFlags()
    {
        var req = (ObjectiveRequirementUseItem)Create("useitem", @"
            <PrimaryCOID>-1</PrimaryCOID>
            <PrimaryCBID>12100</PrimaryCBID>
            <PrimaryDestroy>1</PrimaryDestroy>
            <PrimaryInWorld>1</PrimaryInWorld>
            <SecondaryCBID>11800</SecondaryCBID>
            <SecondaryDestroy>0</SecondaryDestroy>
            <SecondaryGiveAtStart>1</SecondaryGiveAtStart>
            <SecondaryMultipleUse>1</SecondaryMultipleUse>
            <ProgressInterruptable>0</ProgressInterruptable>
            <RepeatCount>1</RepeatCount>");

        Assert.IsTrue(req.PrimaryDestroy);
        Assert.IsTrue(req.PrimaryInWorld);
        Assert.IsFalse(req.SecondaryDestroy);
        Assert.IsTrue(req.SecondaryGiveAtStart);
        Assert.IsTrue(req.SecondaryMultipleUse);
        Assert.IsFalse(req.ProgressInterruptable);
    }

    [TestMethod]
    public void Create_Escort_ParsesPatrolEndpoints()
    {
        var req = (ObjectiveRequirementEscort)Create("escort", @"
            <SkillID>12</SkillID>
            <SkillLevel>3</SkillLevel>
            <FailOnDeath>1</FailOnDeath>
            <MaxDistance>50.0</MaxDistance>
            <ContinentCBID>707</ContinentCBID>
            <CompletionCOID>100</CompletionCOID>
            <CompletionPatrolDistance>10.0</CompletionPatrolDistance>
            <FailCOID>200</FailCOID>
            <FailPatrolDistance>15.0</FailPatrolDistance>
            <StartEscort>0</StartEscort>
            <EndEscort>0</EndEscort>");

        Assert.AreEqual(12, req.SkillId);
        Assert.AreEqual(3, req.SkillLevel);
        Assert.IsTrue(req.FailOnSummonDeath);
        Assert.AreEqual(50f, req.FailDistance);
        Assert.AreEqual(100L, req.CompletionPatrol);
        Assert.AreEqual(10f, req.CompletionDistance);
        Assert.AreEqual(200L, req.FailPatrol);
        Assert.AreEqual(15f, req.FailPatrolDistance);
        Assert.IsFalse(req.StartEscort);
        Assert.IsFalse(req.EndEscort);
    }

    [TestMethod]
    public void Create_CrazyTaxi_ParsesStopLimits()
    {
        var req = (ObjectiveRequirementCrazyTaxi)Create("crazytaxi", @"
            <ContinentCBID>707</ContinentCBID>
            <VehicleMaxVec>5.5</VehicleMaxVec>
            <RadiusOfStop>12.0</RadiusOfStop>
            <MissionStopLimit>1</MissionStopLimit>
            <MissionStopCount>4</MissionStopCount>
            <GiveMoney>1</GiveMoney>
            <GiveExp>1</GiveExp>");

        Assert.AreEqual(707, req.ContinentId);
        Assert.AreEqual(5.5f, req.VehicleMaxVecAtStop);
        Assert.AreEqual(12f, req.RadiusForTrigger);
        Assert.IsTrue(req.FinishOnMissionCount);
        Assert.AreEqual(4, req.FinishMissionCount);
        Assert.IsTrue(req.GiveMoneyReward);
        Assert.IsTrue(req.GiveExpReward);
    }

    [TestMethod]
    public void Create_EmptyElements_LeavesDefaults()
    {
        var kill = (ObjectiveRequirementKill)Create("kill", "");
        Assert.AreEqual(-1, kill.TargetCBID);
        Assert.AreEqual(0, kill.NumToKill);

        var deliver = (ObjectiveRequirementDeliver)Create("deliver", "");
        Assert.AreEqual(-1, deliver.NPCTargetCBID);
        Assert.IsTrue(deliver.NPCTargetCompletes);
    }
}

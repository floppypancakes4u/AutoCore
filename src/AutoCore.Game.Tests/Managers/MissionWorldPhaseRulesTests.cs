using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>Full coverage for pure mission-phase decision rules.</summary>
[TestClass]
public class MissionWorldPhaseRulesTests
{
    [TestMethod]
    public void HasAlternateFormDeliver_Matrix()
    {
        Assert.IsFalse(MissionWorldPhaseRules.HasAlternateFormDeliver(null, 1));
        Assert.IsFalse(MissionWorldPhaseRules.HasAlternateFormDeliver(
            MissionObjective.CreateForTests(1, 0, 1, 1), 0));

        var same = MissionObjective.CreateForTests(1, 0, 10, 1);
        same.Requirements.Add(new ObjectiveRequirementDeliver(same)
        {
            NPCTargetCBID = 5,
            NPCTargetCompletes = true,
        });
        Assert.IsFalse(MissionWorldPhaseRules.HasAlternateFormDeliver(same, 5));
        Assert.IsTrue(MissionWorldPhaseRules.HasAlternateFormDeliver(same, 9));
    }

    [TestMethod]
    public void CollectPadDeliverCbids_SkipsSameNpcAndInvalid()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 10, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 5,
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 8,
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 0,
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { TargetCBID = 1 });

        var pads = MissionWorldPhaseRules.CollectPadDeliverCbids(obj, giverNpcCbid: 5).ToList();
        CollectionAssert.AreEqual(new[] { 8 }, pads);

        Assert.AreEqual(0, MissionWorldPhaseRules.CollectPadDeliverCbids(null, 1).Count());
    }

    [TestMethod]
    public void CollectActiveCompletingDeliverCbids_IncludesSameNpc_SkipsInvalid()
    {
        Assert.AreEqual(0, MissionWorldPhaseRules.CollectActiveCompletingDeliverCbids(null).Count());

        var obj = MissionObjective.CreateForTests(1, 0, 10, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 5,
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 8,
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 9,
            NPCTargetCompletes = false,
        });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 0,
            NPCTargetCompletes = true,
        });
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { TargetCBID = 1 });

        // Same-NPC (5) is included — unlike CollectPadDeliverCbids.
        var delivers = MissionWorldPhaseRules.CollectActiveCompletingDeliverCbids(obj).ToList();
        CollectionAssert.AreEqual(new[] { 5, 8 }, delivers);
    }

    [TestMethod]
    public void ExcludeActiveDeliverFromGiverSuppress_ActiveDeliverWins()
    {
        var suppress = new HashSet<int> { 11786, 11155, 99 };
        MissionWorldPhaseRules.ExcludeActiveDeliverFromGiverSuppress(suppress, new[] { 11786, 0, -1 });
        Assert.IsFalse(suppress.Contains(11786), "Active deliver CBID must not stay in suppress set");
        Assert.IsTrue(suppress.Contains(11155));
        Assert.IsTrue(suppress.Contains(99));

        MissionWorldPhaseRules.ExcludeActiveDeliverFromGiverSuppress(null, new[] { 1 });
        MissionWorldPhaseRules.ExcludeActiveDeliverFromGiverSuppress(suppress, null);
        Assert.AreEqual(2, suppress.Count);
    }

    [TestMethod]
    public void MeetsRaceClassRequirements_FullMatrix()
    {
        // Unrestricted — body not required.
        Assert.IsTrue(MissionWorldPhaseRules.MeetsRaceClassRequirements(-1, -1, false, 0, 0));
        Assert.IsTrue(MissionWorldPhaseRules.MeetsRaceClassRequirements(-1, -1, true, 9, 9));

        // Restricted without body → fail.
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, -1, false, 0, 0));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(-1, 1, false, 0, 1));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, 1, false, 0, 1));

        // Race only.
        Assert.IsTrue(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, -1, true, 0, 3));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, -1, true, 1, 3));

        // Class only (0 = Commando is a real class).
        Assert.IsTrue(MissionWorldPhaseRules.MeetsRaceClassRequirements(-1, 0, true, 2, 0));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(-1, 0, true, 2, 1));

        // Both.
        Assert.IsTrue(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, 2, true, 0, 2));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, 2, true, 0, 1));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsRaceClassRequirements(0, 2, true, 1, 2));
    }

    [TestMethod]
    public void IsCompletedNonPadAlternateFormGiver_Matrix()
    {
        Assert.IsFalse(MissionWorldPhaseRules.IsCompletedNonPadAlternateFormGiver(
            false, false, 10, false, true));
        Assert.IsFalse(MissionWorldPhaseRules.IsCompletedNonPadAlternateFormGiver(
            true, true, 10, false, true), "Repeatable completed must not use sticky-suppress path");
        Assert.IsFalse(MissionWorldPhaseRules.IsCompletedNonPadAlternateFormGiver(
            true, false, 0, false, true));
        Assert.IsFalse(MissionWorldPhaseRules.IsCompletedNonPadAlternateFormGiver(
            true, false, 10, true, true), "Pad-class Final Exam must not count as non-pad");
        Assert.IsFalse(MissionWorldPhaseRules.IsCompletedNonPadAlternateFormGiver(
            true, false, 10, false, false), "Same-NPC / no alt deliver");
        Assert.IsTrue(MissionWorldPhaseRules.IsCompletedNonPadAlternateFormGiver(
            true, false, 11786, false, true), "Track This / class-report giver");
    }

    [TestMethod]
    public void CollectKillSpawnTypes_KillAndAggregate()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 10, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = 580,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        });
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = 1,
            NegativeKill = true,
        });
        var agg = new ObjectiveRequirementKillAggregate(obj) { NumToKill = 2 };
        agg.Targets.Add(10);
        agg.Targets.Add(0);
        agg.Targets.Add(10);
        obj.Requirements.Add(agg);

        var types = MissionWorldPhaseRules.CollectKillSpawnTypes(obj).ToList();
        CollectionAssert.Contains(types, 580);
        CollectionAssert.Contains(types, 10);
        Assert.AreEqual(0, MissionWorldPhaseRules.CollectKillSpawnTypes(null).Count());
    }

    [TestMethod]
    public void MeetsMissionPrerequisites_And_RequiresAll()
    {
        var completed = new HashSet<int> { 10 };
        Assert.IsFalse(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            new[] { 10, 20, 30, -1 }, requirementsOred: 0, completed));
        completed.Add(20);
        completed.Add(30);
        Assert.IsTrue(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            new[] { 10, 20, 30, -1 }, requirementsOred: 0, completed));
    }

    [TestMethod]
    public void MeetsMissionPrerequisites_Ored_AnyOneClassReport()
    {
        // Retail Freelancer/Shields Up: RequirementsOred=-1, four mutually exclusive class reports.
        var reqs = new[] { 2945, 2939, 2941, 2943 };
        Assert.IsFalse(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            reqs, requirementsOred: -1, completedMissionIds: new HashSet<int>()));
        Assert.IsTrue(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            reqs, requirementsOred: -1, completedMissionIds: new HashSet<int> { 2945 }));
        Assert.IsTrue(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            reqs, requirementsOred: 1, completedMissionIds: new HashSet<int> { 2941 }));
        Assert.IsFalse(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            reqs, requirementsOred: 0, completedMissionIds: new HashSet<int> { 2945 }));
    }

    [TestMethod]
    public void MeetsMissionPrerequisites_EmptyOrNull_Passes()
    {
        Assert.IsTrue(MissionWorldPhaseRules.MeetsMissionPrerequisites(null, 0, null));
        Assert.IsTrue(MissionWorldPhaseRules.MeetsMissionPrerequisites(
            new[] { -1, -1, -1, -1 }, 0, new HashSet<int>()));
    }

    [TestMethod]
    public void HasBlockingDeliverSibling_And_ForceComplete()
    {
        Assert.IsFalse(MissionWorldPhaseRules.HasBlockingDeliverSibling(null, RequirementType.Patrol));
        Assert.IsFalse(MissionWorldPhaseRules.NeedsForceClientCompleteAfterDeliver(null));

        var solo = MissionObjective.CreateForTests(1, 0, 1, 1);
        solo.Requirements.Add(new ObjectiveRequirementDeliver(solo)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
        });
        Assert.IsFalse(MissionWorldPhaseRules.HasBlockingDeliverSibling(solo, RequirementType.Patrol));
        Assert.IsFalse(MissionWorldPhaseRules.NeedsForceClientCompleteAfterDeliver(solo));

        var multi = MissionObjective.CreateForTests(2, 0, 1, 1);
        var patrol = new ObjectiveRequirementPatrol(multi) { AutoComplete = true };
        multi.Requirements.Add(patrol);
        multi.Requirements.Add(new ObjectiveRequirementDeliver(multi)
        {
            NPCTargetCBID = 2,
            NPCTargetCompletes = true,
        });
        Assert.IsTrue(MissionWorldPhaseRules.HasBlockingDeliverSibling(multi, RequirementType.Patrol));
        Assert.IsTrue(MissionWorldPhaseRules.NeedsForceClientCompleteAfterDeliver(multi));

        // Deliver without NPCTargetCompletes counts as "other", not deliver.
        var incompleteDeliver = MissionObjective.CreateForTests(3, 0, 1, 1);
        incompleteDeliver.Requirements.Add(new ObjectiveRequirementDeliver(incompleteDeliver)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = false,
        });
        incompleteDeliver.Requirements.Add(new ObjectiveRequirementPatrol(incompleteDeliver));
        Assert.IsFalse(MissionWorldPhaseRules.NeedsForceClientCompleteAfterDeliver(incompleteDeliver));
        Assert.IsFalse(MissionWorldPhaseRules.HasBlockingDeliverSibling(
            incompleteDeliver, RequirementType.Patrol));
    }
}

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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>Pure multi-waypoint patrol progress encoding (no sector map).</summary>
[TestClass]
public class MissionPatrolProgressTests
{
    [TestMethod]
    public void CountListedTargets_UsesTargetCountAndPositiveSlots()
    {
        Assert.AreEqual(0, MissionPatrolProgress.CountListedTargets(null));

        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = new ObjectiveRequirementPatrol(obj)
        {
            TargetCount = 3,
        };
        patrol.GenericTargets[0] = 10;
        patrol.GenericTargets[1] = 11;
        patrol.GenericTargets[2] = -1; // not positive
        Assert.AreEqual(2, MissionPatrolProgress.CountListedTargets(patrol));

        var auto = new ObjectiveRequirementPatrol(obj) { TargetCount = 0 };
        auto.GenericTargets[0] = 5;
        auto.GenericTargets[1] = 6;
        Assert.AreEqual(2, MissionPatrolProgress.CountListedTargets(auto));
    }

    [TestMethod]
    public void NeededCount_MultipliesTargetsByLaps()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: true, laps: 2, 100, 200);
        Assert.AreEqual(4, MissionPatrolProgress.NeededCount(patrol));

        patrol.Laps = 0;
        Assert.AreEqual(2, MissionPatrolProgress.NeededCount(patrol), "Laps<=0 treated as 1");
    }

    [TestMethod]
    public void Sequential_FirstPadPartial_ThenCompleteInOrder()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: true, laps: 1, 10, 20, 30);

        var a = MissionPatrolProgress.TryApplyHit(patrol, 0, 10);
        Assert.IsTrue(a.Accepted);
        Assert.IsFalse(a.IsComplete);
        Assert.AreEqual(1, a.NewProgress);
        Assert.AreEqual(1, a.DisplayProgress);
        Assert.AreEqual(3, a.Needed);

        var rehit = MissionPatrolProgress.TryApplyHit(patrol, a.NewProgress, 10);
        Assert.IsFalse(rehit.Accepted);
        Assert.AreEqual(1, rehit.NewProgress);

        // Client-ahead catch-up: later pad is accepted and jumps progress to that index+1.
        var skip = MissionPatrolProgress.TryApplyHit(patrol, a.NewProgress, 30);
        Assert.IsTrue(skip.Accepted);
        Assert.IsTrue(skip.IsComplete);
        Assert.AreEqual(3, skip.NewProgress);

        // Fresh route: strict middle then last.
        var b = MissionPatrolProgress.TryApplyHit(patrol, 1, 20);
        Assert.IsTrue(b.Accepted);
        Assert.IsFalse(b.IsComplete);
        Assert.AreEqual(2, b.NewProgress);

        var c = MissionPatrolProgress.TryApplyHit(patrol, b.NewProgress, 30);
        Assert.IsTrue(c.Accepted);
        Assert.IsTrue(c.IsComplete);
        Assert.AreEqual(3, c.NewProgress);
        Assert.AreEqual(3, c.DisplayProgress);
    }

    [TestMethod]
    public void Sequential_EarlierPadAfterProgress_Rejected()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: true, laps: 1, 10, 20, 30);
        var back = MissionPatrolProgress.TryApplyHit(patrol, currentProgress: 2, targetCoid: 10);
        Assert.IsFalse(back.Accepted);
        Assert.AreEqual(2, back.NewProgress);
    }

    [TestMethod]
    public void Sequential_TwoLaps_RequiresFullSecondCircuit()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: true, laps: 2, 10, 20);

        var p = 0;
        foreach (var coid in new long[] { 10, 20, 10 })
        {
            var hit = MissionPatrolProgress.TryApplyHit(patrol, p, coid);
            Assert.IsTrue(hit.Accepted, $"should accept {coid}");
            Assert.IsFalse(hit.IsComplete, $"mid-route should not complete on {coid}");
            p = hit.NewProgress;
        }

        var last = MissionPatrolProgress.TryApplyHit(patrol, p, 20);
        Assert.IsTrue(last.Accepted);
        Assert.IsTrue(last.IsComplete);
        Assert.AreEqual(4, last.Needed);
        Assert.AreEqual(4, last.DisplayProgress);
    }

    [TestMethod]
    public void NonSequential_AnyOrder_IgnoresRehit_CompletesWhenAllVisited()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: false, laps: 1, 10, 20, 30);

        var a = MissionPatrolProgress.TryApplyHit(patrol, 0, 30);
        Assert.IsTrue(a.Accepted);
        Assert.IsFalse(a.IsComplete);
        Assert.AreEqual(1, a.DisplayProgress);

        var rehit = MissionPatrolProgress.TryApplyHit(patrol, a.NewProgress, 30);
        Assert.IsFalse(rehit.Accepted);

        var b = MissionPatrolProgress.TryApplyHit(patrol, a.NewProgress, 10);
        Assert.IsTrue(b.Accepted);
        Assert.IsFalse(b.IsComplete);
        Assert.AreEqual(2, b.DisplayProgress);

        var c = MissionPatrolProgress.TryApplyHit(patrol, b.NewProgress, 20);
        Assert.IsTrue(c.Accepted);
        Assert.IsTrue(c.IsComplete);
        Assert.AreEqual(3, c.DisplayProgress);
        Assert.AreEqual(3, c.Needed);
    }

    [TestMethod]
    public void NonSequential_TwoLaps_ClearsMaskBetweenLaps()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: false, laps: 2, 10, 20);

        var p = 0;
        foreach (var coid in new long[] { 20, 10 })
        {
            var hit = MissionPatrolProgress.TryApplyHit(patrol, p, coid);
            Assert.IsTrue(hit.Accepted);
            Assert.IsFalse(hit.IsComplete);
            p = hit.NewProgress;
        }

        // Lap 1 done → mask cleared; may re-hit same pads.
        Assert.AreEqual(1, p >> MissionPatrolProgress.NonSequentialLapShift);
        Assert.AreEqual(0, p & MissionPatrolProgress.NonSequentialMaskBits);

        var mid = MissionPatrolProgress.TryApplyHit(patrol, p, 10);
        Assert.IsTrue(mid.Accepted);
        Assert.IsFalse(mid.IsComplete);

        var done = MissionPatrolProgress.TryApplyHit(patrol, mid.NewProgress, 20);
        Assert.IsTrue(done.Accepted);
        Assert.IsTrue(done.IsComplete);
    }

    [TestMethod]
    public void TryApplyHit_NullOrInvalid_Rejected()
    {
        var miss = MissionPatrolProgress.TryApplyHit(null, 0, 1);
        Assert.IsFalse(miss.Accepted);

        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var patrol = MakePatrol(obj, sequential: true, laps: 1, 10);
        Assert.IsFalse(MissionPatrolProgress.TryApplyHit(patrol, 0, 0).Accepted);
        Assert.IsFalse(MissionPatrolProgress.TryApplyHit(patrol, 0, 99).Accepted);
    }

    private static ObjectiveRequirementPatrol MakePatrol(
        MissionObjective owner,
        bool sequential,
        int laps,
        params long[] targets)
    {
        var patrol = new ObjectiveRequirementPatrol(owner)
        {
            AutoComplete = true,
            Sequential = sequential,
            Laps = laps,
            TargetCount = targets.Length,
        };
        for (var i = 0; i < targets.Length; i++)
            patrol.GenericTargets[i] = targets[i];
        return patrol;
    }
}

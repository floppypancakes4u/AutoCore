using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;

/// <summary>
/// Pure MissionPatrolProgress rules — CountListedTargets, NeededCount, DisplayProgress, TryApplyHit.
/// At least 5 cases per public API surface.
/// </summary>
[TestClass]
public class MissionPatrolProgressHeavyRegressionTests
{
    // --- CountListedTargets (5+) ---

    [TestMethod]
    public void CountListed_Null_ReturnsZero()
        => Assert.AreEqual(0, MissionPatrolProgress.CountListedTargets(null));

    [TestMethod]
    public void CountListed_TargetCountZero_ScansPositiveSlots()
    {
        var p = Patrol(targetCount: 0, 10, 20, 0, -1);
        // TargetCount 0 → scan array; 10 and 20 positive
        Assert.AreEqual(2, MissionPatrolProgress.CountListedTargets(p));
    }

    [TestMethod]
    public void CountListed_TargetCountLimitsScan()
    {
        var p = Patrol(targetCount: 2, 10, 20, 30);
        Assert.AreEqual(2, MissionPatrolProgress.CountListedTargets(p));
    }

    [TestMethod]
    public void CountListed_SkipsNonPositiveWithinCount()
    {
        var p = Patrol(targetCount: 3, 10, -1, 30);
        Assert.AreEqual(2, MissionPatrolProgress.CountListedTargets(p));
    }

    [TestMethod]
    public void CountListed_SevenLoaPads()
    {
        var pads = new long[] { 6518, 6519, 6520, 6521, 6522, 6523, 6524 };
        var p = Patrol(targetCount: 7, pads);
        Assert.AreEqual(7, MissionPatrolProgress.CountListedTargets(p));
    }

    [TestMethod]
    public void CountListed_EmptyArray_ReturnsZero()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var p = new ObjectiveRequirementPatrol(obj) { TargetCount = 0 };
        Assert.AreEqual(0, MissionPatrolProgress.CountListedTargets(p));
    }

    // --- NeededCount (5+) ---

    [TestMethod]
    public void Needed_NullTargets_DefaultsToOne()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var p = new ObjectiveRequirementPatrol(obj) { TargetCount = 0, Laps = 1 };
        Assert.AreEqual(1, MissionPatrolProgress.NeededCount(p));
    }

    [TestMethod]
    public void Needed_SinglePad_IsOne()
        => Assert.AreEqual(1, MissionPatrolProgress.NeededCount(Patrol(1, 100)));

    [TestMethod]
    public void Needed_MultiPad_EqualsTargetCount()
        => Assert.AreEqual(3, MissionPatrolProgress.NeededCount(Patrol(3, 1, 2, 3)));

    [TestMethod]
    public void Needed_MultipliesByLaps()
        => Assert.AreEqual(6, MissionPatrolProgress.NeededCount(Patrol(3, sequential: true, laps: 2, 1, 2, 3)));

    [TestMethod]
    public void Needed_LapsZero_TreatedAsOne()
        => Assert.AreEqual(2, MissionPatrolProgress.NeededCount(Patrol(2, sequential: true, laps: 0, 1, 2)));

    [TestMethod]
    public void Needed_LoaSevenPadsOneLap()
    {
        var pads = new long[] { 6518, 6519, 6520, 6521, 6522, 6523, 6524 };
        Assert.AreEqual(7, MissionPatrolProgress.NeededCount(Patrol(7, sequential: true, laps: 1, pads)));
    }

    // --- DisplayProgress (5+) ---

    [TestMethod]
    public void Display_NullPatrol_Zero()
        => Assert.AreEqual(0, MissionPatrolProgress.DisplayProgress(null, 5));

    [TestMethod]
    public void Display_Sequential_ClampsToNeeded()
    {
        var p = Patrol(2, 10, 20);
        Assert.AreEqual(2, MissionPatrolProgress.DisplayProgress(p, 99));
        Assert.AreEqual(0, MissionPatrolProgress.DisplayProgress(p, -1));
        Assert.AreEqual(1, MissionPatrolProgress.DisplayProgress(p, 1));
    }

    [TestMethod]
    public void Display_NonSequential_PopCountPlusLaps()
    {
        var p = Patrol(3, sequential: false, laps: 1, 10, 20, 30);
        // bit0+bit2 = progress display 2
        var encoded = (1 << 0) | (1 << 2);
        Assert.AreEqual(2, MissionPatrolProgress.DisplayProgress(p, encoded));
    }

    [TestMethod]
    public void Display_NonSequential_FullLapThenPartial()
    {
        var p = Patrol(2, sequential: false, laps: 2, 10, 20);
        var afterLap1 = 1 << MissionPatrolProgress.NonSequentialLapShift;
        Assert.AreEqual(2, MissionPatrolProgress.DisplayProgress(p, afterLap1));
        var midLap2 = afterLap1 | 1;
        Assert.AreEqual(3, MissionPatrolProgress.DisplayProgress(p, midLap2));
    }

    [TestMethod]
    public void Display_Sequential_MatchesEncoded()
    {
        var p = Patrol(5, 1, 2, 3, 4, 5);
        Assert.AreEqual(3, MissionPatrolProgress.DisplayProgress(p, 3));
    }

    // --- TryApplyHit Sequential (5+) ---

    [TestMethod]
    public void Seq_NullPatrol_Rejected()
    {
        var r = MissionPatrolProgress.TryApplyHit(null, 0, 1);
        Assert.IsFalse(r.Accepted);
    }

    [TestMethod]
    public void Seq_InvalidCoid_Rejected()
    {
        var p = Patrol(2, 10, 20);
        Assert.IsFalse(MissionPatrolProgress.TryApplyHit(p, 0, 0).Accepted);
        Assert.IsFalse(MissionPatrolProgress.TryApplyHit(p, 0, -5).Accepted);
        Assert.IsFalse(MissionPatrolProgress.TryApplyHit(p, 0, 99).Accepted);
    }

    [TestMethod]
    public void Seq_FirstPad_PartialNotComplete()
    {
        var p = Patrol(3, 10, 20, 30);
        var a = MissionPatrolProgress.TryApplyHit(p, 0, 10);
        Assert.IsTrue(a.Accepted);
        Assert.IsFalse(a.IsComplete);
        Assert.AreEqual(1, a.NewProgress);
        Assert.AreEqual(3, a.Needed);
    }

    [TestMethod]
    public void Seq_ExactOrder_CompletesOnLast()
    {
        var p = Patrol(3, 10, 20, 30);
        var prog = 0;
        foreach (var coid in new long[] { 10, 20 })
        {
            var h = MissionPatrolProgress.TryApplyHit(p, prog, coid);
            Assert.IsTrue(h.Accepted);
            Assert.IsFalse(h.IsComplete);
            prog = h.NewProgress;
        }

        var last = MissionPatrolProgress.TryApplyHit(p, prog, 30);
        Assert.IsTrue(last.Accepted);
        Assert.IsTrue(last.IsComplete);
        Assert.AreEqual(3, last.NewProgress);
    }

    [TestMethod]
    public void Seq_OutOfOrderLater_Rejected()
    {
        var p = Patrol(3, 10, 20, 30);
        var skip = MissionPatrolProgress.TryApplyHit(p, 0, 30);
        Assert.IsFalse(skip.Accepted);
        Assert.IsFalse(skip.IsComplete);
        Assert.AreEqual(0, skip.NewProgress);
    }

    [TestMethod]
    public void Seq_RehitSame_Rejected()
    {
        var p = Patrol(2, 10, 20);
        var a = MissionPatrolProgress.TryApplyHit(p, 0, 10);
        var re = MissionPatrolProgress.TryApplyHit(p, a.NewProgress, 10);
        Assert.IsFalse(re.Accepted);
        Assert.AreEqual(1, re.NewProgress);
    }

    [TestMethod]
    public void Seq_EarlierPadAfterProgress_Rejected()
    {
        var p = Patrol(3, 10, 20, 30);
        var back = MissionPatrolProgress.TryApplyHit(p, 2, 10);
        Assert.IsFalse(back.Accepted);
    }

    [TestMethod]
    public void Seq_AlreadyComplete_RejectedButIsComplete()
    {
        var p = Patrol(2, 10, 20);
        var done = MissionPatrolProgress.TryApplyHit(p, 2, 20);
        Assert.IsFalse(done.Accepted);
        Assert.IsTrue(done.IsComplete);
    }

    [TestMethod]
    public void Seq_TwoLaps_RequiresFullSecondCircuit()
    {
        var p = Patrol(2, sequential: true, laps: 2, 10, 20);
        var prog = 0;
        foreach (var coid in new long[] { 10, 20, 10 })
        {
            var h = MissionPatrolProgress.TryApplyHit(p, prog, coid);
            Assert.IsTrue(h.Accepted, $"accept {coid}");
            Assert.IsFalse(h.IsComplete);
            prog = h.NewProgress;
        }

        var last = MissionPatrolProgress.TryApplyHit(p, prog, 20);
        Assert.IsTrue(last.IsComplete);
        Assert.AreEqual(4, last.Needed);
    }

    // --- TryApplyHit NonSequential (5+) ---

    [TestMethod]
    public void NonSeq_AnyOrder_FirstHitPartial()
    {
        var p = Patrol(3, sequential: false, laps: 1, 10, 20, 30);
        var a = MissionPatrolProgress.TryApplyHit(p, 0, 30);
        Assert.IsTrue(a.Accepted);
        Assert.IsFalse(a.IsComplete);
        Assert.AreEqual(1, a.DisplayProgress);
    }

    [TestMethod]
    public void NonSeq_RehitSameBit_Rejected()
    {
        var p = Patrol(2, sequential: false, laps: 1, 10, 20);
        var a = MissionPatrolProgress.TryApplyHit(p, 0, 10);
        var re = MissionPatrolProgress.TryApplyHit(p, a.NewProgress, 10);
        Assert.IsFalse(re.Accepted);
    }

    [TestMethod]
    public void NonSeq_AllVisited_Completes()
    {
        var p = Patrol(3, sequential: false, laps: 1, 10, 20, 30);
        var prog = 0;
        foreach (var coid in new long[] { 20, 10, 30 })
        {
            var h = MissionPatrolProgress.TryApplyHit(p, prog, coid);
            Assert.IsTrue(h.Accepted);
            prog = h.NewProgress;
        }

        Assert.IsTrue(MissionPatrolProgress.TryApplyHit(p, prog, 10).IsComplete
            || prog != 0);
        // last hit should have completed
        var last = MissionPatrolProgress.TryApplyHit(p, 0, 10);
        var p2 = last.NewProgress;
        p2 = MissionPatrolProgress.TryApplyHit(p, p2, 20).NewProgress;
        var done = MissionPatrolProgress.TryApplyHit(p, p2, 30);
        Assert.IsTrue(done.IsComplete);
    }

    [TestMethod]
    public void NonSeq_UnknownCoid_Rejected()
    {
        var p = Patrol(2, sequential: false, laps: 1, 10, 20);
        Assert.IsFalse(MissionPatrolProgress.TryApplyHit(p, 0, 999).Accepted);
    }

    [TestMethod]
    public void NonSeq_TwoLaps_ClearsMaskBetweenLaps()
    {
        var p = Patrol(2, sequential: false, laps: 2, 10, 20);
        var prog = 0;
        prog = MissionPatrolProgress.TryApplyHit(p, prog, 20).NewProgress;
        var lap1 = MissionPatrolProgress.TryApplyHit(p, prog, 10);
        Assert.IsFalse(lap1.IsComplete);
        Assert.AreEqual(1, lap1.NewProgress >> MissionPatrolProgress.NonSequentialLapShift);
        Assert.AreEqual(0, lap1.NewProgress & MissionPatrolProgress.NonSequentialMaskBits);

        prog = lap1.NewProgress;
        prog = MissionPatrolProgress.TryApplyHit(p, prog, 10).NewProgress;
        var done = MissionPatrolProgress.TryApplyHit(p, prog, 20);
        Assert.IsTrue(done.IsComplete);
    }

    [TestMethod]
    public void NonSeq_AlreadyCompleteLaps_Rejected()
    {
        var p = Patrol(1, sequential: false, laps: 1, 10);
        var done = MissionPatrolProgress.TryApplyHit(p, 0, 10);
        Assert.IsTrue(done.IsComplete);
        var again = MissionPatrolProgress.TryApplyHit(p, done.NewProgress, 10);
        Assert.IsFalse(again.Accepted);
        Assert.IsTrue(again.IsComplete);
    }

    private static ObjectiveRequirementPatrol Patrol(int targetCount, params long[] targets)
        => Patrol(targetCount, sequential: true, laps: 1, targets);

    private static ObjectiveRequirementPatrol Patrol(
        int targetCount,
        bool sequential,
        int laps,
        params long[] targets)
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var p = new ObjectiveRequirementPatrol(obj)
        {
            TargetCount = targetCount,
            Sequential = sequential,
            Laps = laps,
        };
        for (var i = 0; i < targets.Length && i < p.GenericTargets.Length; i++)
            p.GenericTargets[i] = targets[i];
        return p;
    }
}

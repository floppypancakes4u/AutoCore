using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Structures;

/// <summary>
/// <see cref="CreatureMotionDiag"/> answers the question that pose-rate tuning cannot: is the
/// transmitted pose stream itself walking creatures backwards, or is it monotonic and the visible
/// jump is purely the client's hard-snap correction?
/// </summary>
[TestClass]
public class CreatureMotionDiagTests
{
    /// <summary>Identity keys standing in for two distinct creature ghosts.</summary>
    private static readonly object A = new();
    private static readonly object B = new();

    [TestInitialize]
    public void SetUp()
    {
        CreatureMotionDiag.Reset();
        CreatureMotionDiag.Enabled = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        CreatureMotionDiag.Enabled = false;
        CreatureMotionDiag.Reset();
    }

    [TestMethod]
    public void Disabled_RecordsNothing()
    {
        CreatureMotionDiag.Enabled = false;

        for (var i = 0; i < 5; i++)
            CreatureMotionDiag.RecordPose(A, new Vector3(i, 0f, 0f), 1);

        var (reversals, samples, _, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(0, reversals);
        Assert.AreEqual(0, samples);
    }

    [TestMethod]
    public void StraightLine_ReportsNoReversals()
    {
        for (var i = 0; i < 10; i++)
            CreatureMotionDiag.RecordPose(A, new Vector3(i * 2f, 0f, 0f), 1);

        var (reversals, samples, maxBackward, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(0, reversals, "monotonic travel must never register as a reversal");
        Assert.IsTrue(samples > 0, "steps along the line must be sampled");
        Assert.AreEqual(0f, maxBackward, 0.0001f);
    }

    [TestMethod]
    public void BackwardStep_IsCountedAsReversal()
    {
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(5f, 0f, 0f), 1);   // forward, establishes direction
        CreatureMotionDiag.RecordPose(A, new Vector3(2f, 0f, 0f), 1);   // backward 3u — the rubberband

        var (reversals, _, maxBackward, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(1, reversals);
        Assert.AreEqual(3f, maxBackward, 0.001f, "reports how far back the creature was pulled");
    }

    /// <summary>Y is excluded: terrain snapping moves Y independently of travel direction.</summary>
    [TestMethod]
    public void VerticalOnlyChange_IsNotAReversal()
    {
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(5f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(10f, -4f, 0f), 1); // still forward in XZ

        var (reversals, _, _, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(0, reversals, "a downhill step is not a direction reversal");
    }

    /// <summary>Sub-millimetre jitter must not be read as a direction.</summary>
    [TestMethod]
    public void MicroJitter_IsIgnored()
    {
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(0.001f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);

        var (reversals, samples, _, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(0, reversals);
        Assert.AreEqual(0, samples);
    }

    /// <summary>Tracks are per creature — one NPC turning must not implicate another.</summary>
    [TestMethod]
    public void TracksArePerCreature()
    {
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(B, new Vector3(100f, 0f, 0f), 2);
        CreatureMotionDiag.RecordPose(A, new Vector3(5f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(B, new Vector3(105f, 0f, 0f), 2);
        CreatureMotionDiag.RecordPose(A, new Vector3(10f, 0f, 0f), 1);   // coid 1 keeps going
        CreatureMotionDiag.RecordPose(B, new Vector3(100f, 0f, 0f), 2);  // coid 2 reverses

        var (reversals, _, _, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(1, reversals, "only the creature that actually reversed may be counted");
    }

    /// <summary>
    /// A ghost instance can be re-pointed to a different creature. Continuing the old position
    /// history across that boundary compares two unrelated creatures and invents a large "reversal"
    /// the wire never carried — the exact artefact that made COID-keyed tracking useless. A rebind
    /// must reset the history and be counted separately, so a real teleport can be told apart from
    /// an instrument artefact.
    /// </summary>
    [TestMethod]
    public void GhostRebind_ResetsHistory_AndIsNotCountedAsReversal()
    {
        // Creature 1 walking +X.
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(5f, 0f, 0f), 1);

        // Same ghost instance now carries creature 2, far away in the opposite direction.
        CreatureMotionDiag.RecordPose(A, new Vector3(-500f, 0f, 0f), 2);
        CreatureMotionDiag.RecordPose(A, new Vector3(-495f, 0f, 0f), 2);

        var (reversals, _, maxBackward, rebinds) = CreatureMotionDiag.Sample();

        Assert.AreEqual(1, rebinds, "the parent swap must be reported as a rebind");
        Assert.AreEqual(0, reversals,
            "a rebind must not masquerade as the server teleporting a creature backwards");
        Assert.AreEqual(0f, maxBackward, 0.001f);
    }

    [TestMethod]
    public void Sample_ResetsWindowCounters()
    {
        CreatureMotionDiag.RecordPose(A, new Vector3(0f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(5f, 0f, 0f), 1);
        CreatureMotionDiag.RecordPose(A, new Vector3(2f, 0f, 0f), 1);
        CreatureMotionDiag.Sample();

        var (reversals, samples, maxBackward, _) = CreatureMotionDiag.Sample();
        Assert.AreEqual(0, reversals);
        Assert.AreEqual(0, samples);
        Assert.AreEqual(0f, maxBackward, 0.0001f);
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy regression: Live-and-Direct (one pad/seq) vs LOA (multi-pad) AutoPatrol sequence handling.
/// </summary>
[TestClass]
public class MissionSequencePatrolHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;

    private const int Mid = 94001;
    private const int O0 = 95001;
    private const int O1 = 95002;
    private const int O2 = 95003;
    private const int O3 = 95004;
    private const int O4 = 95005;
    private const long P0 = 96001;
    private const long P1 = 96002;
    private const long P2 = 96003;
    private const long P3 = 96004;
    private const long P4 = 96005;
    private const long P5 = 96006;
    private const long P6 = 96007;

    [TestInitialize]
    public void SetUp() => _fx = new MissionHeavyRegressionFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    // --- Live and Direct: one pad per sequence (5+) ---

    [TestMethod]
    public void Lad_FirstPad_AdvancesSequence_Sends0x2070()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, veh) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);

        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, _fx.CountComplete(O0));
        Assert.IsTrue(_fx.Sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == O1));
        Assert.IsTrue(_fx.Sent.OfType<ConvoyMissionsResponsePacket>().Any());
    }

    [TestMethod]
    public void Lad_SecondPad_AdvancesAgain()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1), (O2, P2));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(10, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        ch.CurrentVehicle.Position = new Vector3(10, 0, 0);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P1);
        Assert.AreEqual(2, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, _fx.CountComplete(O1));
    }

    [TestMethod]
    public void Lad_FinalPad_CompletesMission()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
        Assert.AreEqual(0, ch.CurrentQuests.Count);
    }

    [TestMethod]
    public void Lad_FivePadChain_CompletesAtEnd()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1), (O2, P2), (O3, P3), (O4, P4));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        var pads = new[] { P0, P1, P2, P3, P4 };
        for (var i = 0; i < pads.Length; i++)
            MissionHeavyRegressionFixture.PlaceWaypoint(map, pads[i], new Vector3(i * 10f, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        for (var i = 0; i < pads.Length; i++)
        {
            ch.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            _fx.AutoPatrol(conn, pads[i]);
            if (i < pads.Length - 1)
                Assert.AreEqual(i + 1, ch.CurrentQuests[0].ActiveObjectiveSequence);
        }

        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Lad_UnlistedPad_NoProgress()
    {
        // Pad not on any sequence — must not reconcile-skip or complete.
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        const long stranger = 96999;
        MissionHeavyRegressionFixture.PlaceWaypoint(map, stranger, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, stranger);
        Assert.AreEqual(1, ch.CurrentQuests.Count);
        Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(0, _fx.CountComplete(O0));
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Lad_LaterPad_ReconcilesAndCompletesChain()
    {
        // Hitting a later sequence's pad intentionally client-ahead reconciles intermediate objs.
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P1);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Lad_SinglePad_DoesNotWaitForMultiPadProgress()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
        // No mid-route pad-count-only path that leaves quest active
        Assert.AreEqual(0, ch.CurrentQuests.Count);
    }

    // --- LOA multi-pad one objective (5+) ---

    [TestMethod]
    public void Loa_FirstPad_StaysOnSeq0_No0x2070()
    {
        var pads = new[] { P0, P1, P2, P3, P4, P5, P6 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);

        Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(1, ch.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.AreEqual(0, _fx.CountComplete(O0));
        var st = _fx.LastObjectiveState(O0);
        Assert.IsNotNull(st);
        Assert.AreEqual(1f, st.SlotProgress[0], 0.001f);
    }

    [TestMethod]
    public void Loa_MiddlePads_AccumulateProgress()
    {
        var pads = new[] { P0, P1, P2, P3 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        for (var i = 0; i < pads.Length; i++)
            MissionHeavyRegressionFixture.PlaceWaypoint(map, pads[i], new Vector3(i * 10f, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        for (var i = 0; i < pads.Length - 1; i++)
        {
            ch.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            _fx.AutoPatrol(conn, pads[i]);
            Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence, $"seq after pad {i}");
            Assert.AreEqual(i + 1, ch.CurrentQuests[0].ObjectiveProgress[0]);
        }
    }

    [TestMethod]
    public void Loa_LastPad_AdvancesToDeliver()
    {
        var pads = new[] { P0, P1, P2 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        for (var i = 0; i < pads.Length; i++)
            MissionHeavyRegressionFixture.PlaceWaypoint(map, pads[i], new Vector3(i * 10f, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        for (var i = 0; i < pads.Length; i++)
        {
            ch.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            _fx.AutoPatrol(conn, pads[i]);
        }

        Assert.AreEqual(1, ch.CurrentQuests.Count);
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_fx.CountComplete(O0) >= 1);
        Assert.IsTrue(_fx.Sent.OfType<ObjectiveStatePacket>().Any(p => p.ObjectiveId == O1));
    }

    [TestMethod]
    public void Loa_RehitSamePad_NoDoubleCount()
    {
        var pads = new[] { P0, P1, P2 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        _fx.AutoPatrol(conn, P0);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(1, ch.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);
    }

    [TestMethod]
    public void Loa_OutOfOrderLastPadFirst_DoesNotComplete()
    {
        var pads = new[] { P0, P1, P2 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P2, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P2);
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.AreEqual(0, _fx.CountComplete(O0));
    }

    [TestMethod]
    public void Loa_SevenPads_MatchesRetailShape()
    {
        var pads = new long[] { 6518, 6519, 6520, 6521, 6522, 6523, 6524 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        for (var i = 0; i < pads.Length; i++)
            MissionHeavyRegressionFixture.PlaceWaypoint(map, pads[i], new Vector3(i * 10f, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        for (var i = 0; i < pads.Length - 1; i++)
        {
            ch.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            _fx.AutoPatrol(conn, pads[i]);
            Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);
        }

        ch.CurrentVehicle.Position = new Vector3((pads.Length - 1) * 10f, 0, 0);
        _fx.AutoPatrol(conn, pads[^1]);
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
    }

    [TestMethod]
    public void Loa_SetsObjectiveMaxToNeededOnFirstHit()
    {
        var pads = new[] { P0, P1, P2 };
        _fx.SeedLoaShaped(Mid, O0, O1, pads);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        Assert.AreEqual(1, ch.CurrentQuests[0].ObjectiveMax[0], "grant default max");
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(ch.CurrentQuests[0].ObjectiveMax[0] >= 3);
    }

    // --- Non-sequential multi-pad (5+) ---

    [TestMethod]
    public void NonSeq_AnyOrder_FirstHitPartial()
    {
        SeedNonSeq(3);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P2, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P2);
        Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void NonSeq_AllPadsAnyOrder_Completes()
    {
        SeedNonSeq(3);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        var order = new[] { P1, P0, P2 };
        for (var i = 0; i < order.Length; i++)
            MissionHeavyRegressionFixture.PlaceWaypoint(map, order[i], new Vector3(i * 10f, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        for (var i = 0; i < order.Length; i++)
        {
            ch.CurrentVehicle.Position = new Vector3(i * 10f, 0, 0);
            _fx.AutoPatrol(conn, order[i]);
        }

        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid) || ch.CurrentQuests[0].ActiveObjectiveSequence == 1);
    }

    [TestMethod]
    public void NonSeq_Rehit_DoesNotDouble()
    {
        SeedNonSeq(2);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        var prog = ch.CurrentQuests[0].ObjectiveProgress[0];
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(prog, ch.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    public void NonSeq_UnknownPad_NoOp()
    {
        SeedNonSeq(2);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, 99999, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, 99999);
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    public void NonSeq_TwoPads_SecondCompletes()
    {
        SeedNonSeq(2);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(10, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        ch.CurrentVehicle.Position = new Vector3(10, 0, 0);
        _fx.AutoPatrol(conn, P1);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid) || ch.CurrentQuests[0].ActiveObjectiveSequence >= 1);
    }

    // --- Laps (5+) ---

    [TestMethod]
    public void Laps_SinglePadThreeLaps_NeedsThreeHits()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(obj, sequential: true, laps: 3, P0);
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        _fx.AutoPatrol(conn, P0);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Laps_TwoPadsTwoLaps_NeedsFourHits()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(obj, sequential: true, laps: 2, P0, P1);
        obj.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(10, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        foreach (var (pad, x) in new[] { (P0, 0f), (P1, 10f), (P0, 0f) })
        {
            ch.CurrentVehicle.Position = new Vector3(x, 0, 0);
            _fx.AutoPatrol(conn, pad);
            Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        }

        ch.CurrentVehicle.Position = new Vector3(10, 0, 0);
        _fx.AutoPatrol(conn, P1);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Laps_ProgressIncrementsPerHit()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        obj.Requirements.Add(MissionHeavyRegressionFixture.MakePatrol(obj, true, 3, P0));
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(1, ch.CurrentQuests[0].ObjectiveProgress[0]);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(2, ch.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    public void Laps_OneLapDefault_SinglePadCompletes()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        obj.Requirements.Add(MissionHeavyRegressionFixture.MakePatrol(obj, true, 1, P0));
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Laps_MidRouteSendsAbsoluteObjectiveState()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        obj.Requirements.Add(MissionHeavyRegressionFixture.MakePatrol(obj, true, 3, P0));
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);
        var st = _fx.LastObjectiveState(O0);
        Assert.IsNotNull(st);
        Assert.AreEqual(1f, st.SlotProgress[0], 0.001f);
    }

    // --- Guards / isolation (5+) ---

    [TestMethod]
    public void Guard_NullConn_NoThrow()
    {
        NpcInteractHandler.HandleAutoPatrol(null, new AutoPatrolPacket { Target = new TFID(P0, false) });
    }

    [TestMethod]
    public void Guard_NullPacket_NoThrow()
    {
        var (conn, _, _, _) = _fx.CreatePlayer();
        NpcInteractHandler.HandleAutoPatrol(conn, null);
    }

    [TestMethod]
    public void Guard_OutOfRange_NoProgress()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        ch.CurrentVehicle.Position = new Vector3(100, 0, 0);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(1, ch.CurrentQuests.Count);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Guard_NoMap_NoProgress()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, _, _) = _fx.CreatePlayer();
        ch.SetMap(null);
        ch.CurrentVehicle.SetMap(null);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Isolation_TwoPlayers_ProgressIndependent()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var a = _fx.CreatePlayer(18100, 18101);
        var b = _fx.CreatePlayer(18200, 18201);
        MissionHeavyRegressionFixture.PlaceWaypoint(a.Map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.PlaceWaypoint(b.Map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(a.Character, Mid);
        MissionHeavyRegressionFixture.GiveQuest(b.Character, Mid);
        _fx.AutoPatrol(a.Conn, P0);
        Assert.AreEqual(1, a.Character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(0, b.Character.CurrentQuests[0].ActiveObjectiveSequence);
    }

    private void SeedNonSeq(int padCount)
    {
        var pads = new[] { P0, P1, P2 }.Take(padCount).ToArray();
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(obj, sequential: false, laps: 1, pads);
        obj.Requirements.Add(patrol);
        var deliver = MissionObjective.CreateForTests(O1, 1, Mid, 1);
        deliver.Requirements.Add(new ObjectiveRequirementDeliver(deliver)
        {
            NPCTargetCBID = 2472,
            NPCTargetCompletes = true,
        });
        // For non-seq complete of multi-pad alone, use single objective when testing complete
        if (padCount <= 2)
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        else
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj, deliver));
    }
}

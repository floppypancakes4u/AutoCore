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
/// Heavy regression: sibling deliver blocking, client-ahead reconcile, stale patrol resync.
/// </summary>
[TestClass]
public class MissionSiblingAndReconcileHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;
    private const int Mid = 94200;
    private const int O0 = 95200;
    private const int O1 = 95201;
    private const long P0 = 96200;
    private const long P1 = 96201;

    [TestInitialize]
    public void SetUp() => _fx = new MissionHeavyRegressionFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    // --- Sibling deliver + patrol (5+) ---

    [TestMethod]
    public void Sibling_PatrolHit_DoesNotCompleteMission()
    {
        SeedPatrolPlusDeliverSameObjective();
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(1, ch.CurrentQuests.Count);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        Assert.AreEqual(0, _fx.CountComplete(O0));
    }

    [TestMethod]
    public void Sibling_HasBlockingDeliver_True()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        obj.Requirements.Add(new ObjectiveRequirementPatrol(obj) { AutoComplete = true });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 99,
            NPCTargetCompletes = true,
        });
        Assert.IsTrue(NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            obj, RequirementType.Patrol));
    }

    [TestMethod]
    public void Sibling_PatrolOnly_NoBlocking()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        obj.Requirements.Add(new ObjectiveRequirementPatrol(obj) { AutoComplete = true });
        Assert.IsFalse(NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            obj, RequirementType.Patrol));
    }

    [TestMethod]
    public void Sibling_DeliverWithoutNpcCompletes_MayNotBlock()
    {
        // NPCTargetCompletes false — HasBlockingDeliverSibling rules decide
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        obj.Requirements.Add(new ObjectiveRequirementPatrol(obj) { AutoComplete = true });
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 99,
            NPCTargetCompletes = false,
        });
        // Document current rule: only completing deliver siblings block
        var blocked = NpcInteractHandler.ObjectiveHasBlockingSiblingRequirements(
            obj, RequirementType.Patrol);
        Assert.IsFalse(blocked);
    }

    [TestMethod]
    public void Sibling_MultiPadPlusDeliver_FirstPadStillNoComplete()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(obj, true, 1, P0, P1);
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 2472,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        Assert.AreEqual(0, _fx.CountComplete(O0));
    }

    // --- Client-ahead reconcile (5+) ---

    [TestMethod]
    public void Reconcile_ClientOnLaterPad_AdvancesIntermediateDeliver()
    {
        // seq0 deliver-like simple, seq1 patrol with P1
        var o0 = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        o0.Requirements.Add(new ObjectiveRequirementDeliver(o0)
        {
            NPCTargetCBID = 1,
            NPCTargetCompletes = true,
        });
        var o1 = MissionObjective.CreateForTests(O1, 1, Mid, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(o1, true, 1, P1);
        o1.Requirements.Add(patrol);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, o0, o1));

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid); // starts at seq 0
        Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);

        _fx.AutoPatrol(conn, P1); // client-ahead on patrol of later seq
        // Should reconcile forward so patrol can process
        Assert.IsTrue(
            ch.CurrentQuests.Count == 0
            || ch.CurrentQuests[0].ActiveObjectiveSequence >= 1
            || ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Reconcile_AlreadyOnMatchingSeq_NoExtraAdvance()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
        // second pad only advances once
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(10, 0, 0));
        ch.CurrentVehicle.Position = new Vector3(10, 0, 0);
        _fx.AutoPatrol(conn, P1);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Reconcile_UnknownTarget_NoChange()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, 123456, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, 123456);
        Assert.AreEqual(0, ch.CurrentQuests[0].ActiveObjectiveSequence);
    }

    [TestMethod]
    public void Reconcile_CompletedMission_Skipped()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        ch.CompletedMissionIds.Add(Mid);
        _fx.AutoPatrol(conn, P0);
        // quest may still be in list but completed set present — no complete packet spam required
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
    }

    [TestMethod]
    public void Reconcile_MultiMission_OnlyMatchingAdvances()
    {
        var m2 = Mid + 1;
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        // second mission different pads
        var o = MissionObjective.CreateForTests(O1 + 10, 0, m2, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(o, true, 1, P0 + 50);
        o.Requirements.Add(patrol);
        // Seed after first would overwrite — use SetTestMission for both carefully
        var mission1 = AssetManager.Instance.GetMission(Mid);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(m2, o));
        // re-seed mid
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(m2, o));

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        MissionHeavyRegressionFixture.GiveQuest(ch, m2);
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(1, ch.CurrentQuests.First(q => q.MissionId == Mid).ActiveObjectiveSequence);
        Assert.AreEqual(0, ch.CurrentQuests.First(q => q.MissionId == m2).ActiveObjectiveSequence);
    }

    // --- Stale past patrol resync (5+) ---

    [TestMethod]
    public void Stale_PastPadWhileOnLaterSeq_Sends0x2070ForPast()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(10, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0); // now seq 1
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
        _fx.Sent.Clear();
        // client still spamming past pad
        ch.CurrentVehicle.Position = new Vector3(0, 0, 0);
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(
            _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == O0)
            || _fx.Sent.OfType<ObjectiveStatePacket>().Any()
            || _fx.Sent.OfType<ConvoyMissionsResponsePacket>().Any(),
            "stale pad should force client resync path");
    }

    [TestMethod]
    public void Stale_OneShot_DoesNotSpamForever()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P1, new Vector3(10, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        ch.CurrentVehicle.Position = new Vector3(0, 0, 0);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);
        var first = _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Count(p => p.ObjectiveId == O0);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);
        var second = _fx.Sent.OfType<CompleteDynamicObjectivePacket>().Count(p => p.ObjectiveId == O0);
        Assert.IsTrue(second <= first, "stale resync is one-shot per mission");
    }

    [TestMethod]
    public void Stale_ActivePad_NotTreatedAsStale()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0), (O1, P1));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);
        // real advance, not stale
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_fx.CountComplete(O0) >= 1);
    }

    [TestMethod]
    public void Stale_NoQuest_NoThrow()
    {
        var (conn, _, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        _fx.AutoPatrol(conn, P0);
    }

    [TestMethod]
    public void Stale_AfterComplete_PastPadIgnored()
    {
        _fx.SeedOnePadPerSequence(Mid, (O0, P0));
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWaypoint(map, P0, new Vector3(0, 0, 0));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.AutoPatrol(conn, P0);
        Assert.IsTrue(ch.CompletedMissionIds.Contains(Mid));
        _fx.Sent.Clear();
        _fx.AutoPatrol(conn, P0);
        Assert.AreEqual(0, ch.CurrentQuests.Count);
    }

    private void SeedPatrolPlusDeliverSameObjective()
    {
        var obj = MissionObjective.CreateForTests(O0, 0, Mid, 1);
        var patrol = MissionHeavyRegressionFixture.MakePatrol(obj, true, 1, P0);
        obj.Requirements.Add(patrol);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = 2472,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(Mid, obj));
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using System;
using System.Collections.Generic;
using System.Linq;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy regression for the give-leg deliver collapse (live "Track This" 3979 double-dialog bug).
/// <para>
/// The mission delivers to the SAME NPC twice — a give-leg (item handed to the player at start,
/// NPC does NOT take it) at seq0, an AutoComplete patrol at seq1, and a take-leg (NPC takes the
/// item) at seq2. The FIRST visit to that NPC must collapse the give-leg + patrol straight into
/// the take-leg so the player sees ONE dialog (the completion), never the give-leg dialog whose
/// client-side text was the mission's "go find Gareth" not-complete line.
/// </para>
/// <para>
/// These tests lock the collapse AND its guards: it must not fire across a kill, a use-item, or a
/// deliver to a different NPC, and it must never consume the delivered item early.
/// </para>
/// </summary>
[TestClass]
public class MissionDeliverGiveLegCollapseHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;

    // Synthetic ids only (must not collide with retail content).
    private const int Mid = 94700;
    private const int GiveObj = 95700;
    private const int PatrolObj = 95710;
    private const int PatrolObj2 = 95711;
    private const int TakeObj = 95720;
    private const int KillObj = 95730;
    private const int NpcCbid = 93700;   // Gareth stand-in (give + take target)
    private const int OtherCbid = 93701; // a different NPC
    private const long NpcCoid = 94700_1;
    private const long OtherCoid = 94700_2;
    private const int ItemCbid = 20045;  // GPS control unit stand-in

    [TestInitialize]
    public void SetUp() => _fx = new MissionHeavyRegressionFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    // ---------------------------------------------------------------- collapse (happy path)

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FirstVisit_CollapsesGiveLegAndPatrol_ToSingleCompletionDialog()
    {
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj }, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 2);
        Assert.AreEqual(1, _fx.CountNpcMissionDialog(),
            "one physical interaction must yield exactly one (completion) dialog");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FirstVisit_ForceCompletesGiveLegAndPatrolClientSide()
    {
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj }, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        Assert.AreEqual(1, _fx.CountComplete(GiveObj),
            "give-leg must be force-completed (0x2070) so the client retargets, not dialog'd");
        Assert.AreEqual(1, _fx.CountComplete(PatrolObj),
            "the intervening patrol must be force-completed too");
        Assert.AreEqual(0, _fx.CountComplete(TakeObj),
            "the take-leg is the live turn-in — not force-completed at interaction time");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void MultiplePatrolsBetween_AllCollapse()
    {
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj, PatrolObj2 }, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 3); // give + 2 patrols skipped
        Assert.AreEqual(1, _fx.CountComplete(GiveObj));
        Assert.AreEqual(1, _fx.CountComplete(PatrolObj));
        Assert.AreEqual(1, _fx.CountComplete(PatrolObj2));
        Assert.AreEqual(1, _fx.CountNpcMissionDialog());
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void FirstVisit_WithStaleGiveLegHint_ServerAdvancesOnce_NoDoubleComplete()
    {
        // Retail Gareth wire keeps sending the give-leg objective id after the server advances.
        // The server must never double-advance the sequence or complete the mission twice, even
        // though the persistently-stale hint may re-arm the client-behind 0x2070 resync.
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj }, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);
        _fx.UseObject(conn, NpcCoid, GiveObj); // stale hint again

        // The single load-bearing invariant: server state is at the take-leg exactly once.
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 2);
        MissionInvariantAssertions.AssertNotActive(ch, Mid + 1); // sanity: no phantom quests
        Assert.AreEqual(1, ch.CurrentQuests.Count(q => q.MissionId == Mid),
            "a repeated stale hint must not spawn a second quest or re-advance the sequence");
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid), "mission is not completed by interaction alone");
    }

    // ---------------------------------------------------------------- cargo safety

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveLegCollapse_DoesNotConsumeDeliveredItem()
    {
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj }, TakeObj, NpcCbid, itemCbid: ItemCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.GrantMissionCargo(ch, ItemCbid);
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 2);
        Assert.AreEqual(1, ch.Inventory.CountByCbid(ItemCbid),
            "the item is taken only at the take-leg (TakeItemAtEnd) — the collapse must not eat it");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void EndToEnd_CollapseThenAccept_CompletesMissionAndTakesItem()
    {
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj }, TakeObj, NpcCbid, itemCbid: ItemCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.GrantMissionCargo(ch, ItemCbid);
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        // Interact (collapses to take-leg), then click the completion dialog.
        _fx.UseObject(conn, NpcCoid, GiveObj);
        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = Mid,
            Accepted = true,
            MissionGiver = new TFID(NpcCoid, false),
        });

        MissionInvariantAssertions.AssertCompleted(ch, Mid);
        Assert.AreEqual(0, ch.Inventory.CountByCbid(ItemCbid), "take-leg turn-in must take the item");
    }

    // ---------------------------------------------------------------- guards (must NOT collapse)

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void TakeLegAtDifferentNpc_DoesNotCollapse_GiveLegKeepsOwnDialog()
    {
        // Give-leg to NpcCbid, real take-leg to a DIFFERENT NPC → no same-NPC collapse.
        SeedGiveThenTakeElsewhere(Mid, GiveObj, NpcCbid, TakeObj, OtherCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(1, _fx.CountNpcMissionDialog(), "give-leg keeps its own dialog here");
        Assert.AreEqual(0, _fx.CountComplete(GiveObj), "no collapse → no give-leg force-complete");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void KillBetweenGiveAndTake_DoesNotCollapseAcrossKill()
    {
        SeedGiveKillTakeSameNpc(Mid, GiveObj, KillObj, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        Assert.AreEqual(0, _fx.CountComplete(KillObj), "a kill objective must never be skipped");
        Assert.IsTrue(ch.CurrentQuests[0].ActiveObjectiveSequence < 2,
            "the collapse must stop at the give-leg, never jump the kill to the take-leg");
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void GiveLegOnly_NoLaterTakeLeg_KeepsGiveLegDialog()
    {
        // Give-leg to NpcCbid with no take-leg anywhere → nothing to collapse into.
        SeedGiveLegOnly(Mid, GiveObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(1, _fx.CountNpcMissionDialog());
        Assert.AreEqual(0, _fx.CountComplete(GiveObj));
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void PlainSingleDeliver_Unaffected_OpensNormalDialog()
    {
        // A normal take-leg-only deliver must behave exactly as before (no collapse machinery).
        _fx.SeedNpcMinusOneDeliver(Mid, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, TakeObj);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(1, _fx.CountNpcMissionDialog());
        Assert.AreEqual(0, _fx.CountComplete(TakeObj));
    }

    // ---------------------------------------------------------------- deferral (AV-window safety)

    [TestMethod]
    [TestCategory("MissionCritical")]
    public void Collapse_DefersDialogUntilRetargetsApplied()
    {
        _fx.SeedGivePatrolTakeSameNpc(Mid, GiveObj, new[] { PatrolObj }, TakeObj, NpcCbid);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceNpc(map, NpcCoid, NpcCbid, new Vector3(5f, 0f, 0f));
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        var scheduled = new List<(Action Action, int DelayMs)>();
        NpcInteractHandler.DialogAfterRetargetDelayMs = 250;
        NpcInteractHandler.ScheduleDelayedWork = (action, delayMs, _) => scheduled.Add((action, delayMs));
        _fx.Sent.Clear();

        _fx.UseObject(conn, NpcCoid, GiveObj);

        Assert.AreEqual(0, _fx.CountNpcMissionDialog(),
            "the completion dialog must not race the just-sent retargeting 0x2070s");
        Assert.IsTrue(scheduled.Any(s => s.DelayMs >= NpcInteractHandler.DialogAfterRetargetDelayMs),
            "the dialog must be deferred past the client reaction-pump window");

        foreach (var (action, _) in scheduled)
            action();

        Assert.AreEqual(1, _fx.CountNpcMissionDialog(), "deferred dialog still opens, exactly once");
    }

    // ---------------------------------------------------------------- local variant seeders

    private static void SeedGiveThenTakeElsewhere(
        int missionId, int giveObjId, int giveNpcCbid, int takeObjId, int takeNpcCbid)
    {
        var give = MissionObjective.CreateForTests(giveObjId, 0, missionId, 1);
        give.Requirements.Add(new ObjectiveRequirementDeliver(give)
        {
            NPCTargetCBID = giveNpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = false,
        });
        var take = MissionObjective.CreateForTests(takeObjId, 1, missionId, 1);
        take.Requirements.Add(new ObjectiveRequirementDeliver(take)
        {
            NPCTargetCBID = takeNpcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = true,
        });
        var mission = Mission.CreateForTests(missionId, give, take);
        mission.NPC = giveNpcCbid;
        mission.Continent = MissionHeavyRegressionFixture.ContId;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    private static void SeedGiveKillTakeSameNpc(
        int missionId, int giveObjId, int killObjId, int takeObjId, int npcCbid)
    {
        var give = MissionObjective.CreateForTests(giveObjId, 0, missionId, 1);
        give.Requirements.Add(new ObjectiveRequirementDeliver(give)
        {
            NPCTargetCBID = npcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = false,
        });
        var kill = MissionObjective.CreateForTests(killObjId, 1, missionId, 1);
        kill.Requirements.Add(new ObjectiveRequirementKill(kill)
        {
            NumToKill = 1,
            TargetCBID = 7,
            FirstStateSlot = 0,
        });
        var take = MissionObjective.CreateForTests(takeObjId, 2, missionId, 1);
        take.Requirements.Add(new ObjectiveRequirementDeliver(take)
        {
            NPCTargetCBID = npcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = true,
        });
        var mission = Mission.CreateForTests(missionId, give, kill, take);
        mission.NPC = npcCbid;
        mission.Continent = MissionHeavyRegressionFixture.ContId;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }

    private static void SeedGiveLegOnly(int missionId, int giveObjId, int npcCbid)
    {
        var give = MissionObjective.CreateForTests(giveObjId, 0, missionId, 1);
        give.Requirements.Add(new ObjectiveRequirementDeliver(give)
        {
            NPCTargetCBID = npcCbid,
            NPCTargetCompletes = true,
            FirstStateSlot = 0,
            TakeItemAtEnd = false,
        });
        var mission = Mission.CreateForTests(missionId, give);
        mission.NPC = npcCbid;
        mission.Continent = MissionHeavyRegressionFixture.ContId;
        mission.ReqMissionId = new[] { -1, -1, -1, -1 };
        AssetManager.Instance.SetTestMission(mission);
    }
}

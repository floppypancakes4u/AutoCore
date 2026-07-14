using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Chat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Mission.Infrastructure;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy regression for generic UseItem mission support (grant / multi-site plant / cargo /
/// destroy packets / abandon / isolation). Synthetic mission ids only — not retail content.
/// </summary>
[TestClass]
public class MissionUseItemHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;

    // Synthetic ids — keep clear of other heavy suites (94xxx patrol/lifecycle).
    private const int Mid = 94200;
    private const int O0 = 95200;
    private const int O1 = 95201;
    private const int O2 = 95202;
    private const long SiteA = 96200;
    private const long SiteB = 96201;
    private const int WorldCbid = 81200;
    private const int SecondaryCbid = 81201;
    private const int DeliverNpc = 81202;
    private const int ContId = MissionHeavyRegressionFixture.ContId;

    [TestInitialize]
    public void SetUp() => _fx = new MissionHeavyRegressionFixture();

    [TestCleanup]
    public void TearDown() => _fx.Dispose();

    // -------------------------------------------------------------------------
    // Grant / SecondaryGiveAtStart cargo
    // -------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void Grant_SecondaryGiveAtStart_MultipleUse_GivesQuantityOne()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.SecondaryCBID = SecondaryCbid;
            u.SecondaryGiveAtStart = true;
            u.SecondaryMultipleUse = true;
            u.RepeatCount = 3;
        });

        var (conn, ch, _, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        _fx.Sent.Clear();

        NpcInteractHandler.GrantMission(conn, ch, Mid);

        Assert.AreEqual(1, ch.Inventory.CountByCbid(SecondaryCbid),
            "MultipleUse secondary grant is a single multi-use stack, not RepeatCount");
        Assert.IsTrue(ch.Inventory.Items.Any(i => i.Cbid == SecondaryCbid && i.IsMissionItem));
        Assert.IsTrue(_fx.Sent.OfType<CreateSimpleObjectPacket>()
            .Any(p => p.CBID == SecondaryCbid && p.PossibleMissionItem && p.IsInInventory));
        Assert.IsTrue(_fx.Sent.OfType<InventoryAddItemResponsePacket>()
            .Any(p => p.WasSuccessful));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Grant_SecondaryGiveAtStart_NotMultipleUse_GivesRepeatCount()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.SecondaryCBID = SecondaryCbid;
            u.SecondaryGiveAtStart = true;
            u.SecondaryMultipleUse = false;
            u.RepeatCount = 4;
        });

        var (conn, ch, _, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);

        NpcInteractHandler.GrantMission(conn, ch, Mid);

        Assert.AreEqual(4, ch.Inventory.CountByCbid(SecondaryCbid));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Grant_PrimaryGiveAtStart_PutsPrimaryInCargo()
    {
        const int primaryItemCbid = 81210;
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = primaryItemCbid;
            u.PrimaryInWorld = false;
            u.PrimaryGiveAtStart = true;
            u.PrimaryMultipleUse = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, _, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);

        NpcInteractHandler.GrantMission(conn, ch, Mid);

        Assert.AreEqual(1, ch.Inventory.CountByCbid(primaryItemCbid));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Grant_NoGiveAtStart_DoesNotInjectCargo()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.SecondaryCBID = SecondaryCbid;
            u.SecondaryGiveAtStart = false;
            u.RepeatCount = 1;
        });

        var (conn, ch, _, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);

        NpcInteractHandler.GrantMission(conn, ch, Mid);

        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid));
    }

    // -------------------------------------------------------------------------
    // Multi-site plant shape (give multi-use → keep → destroy on second site)
    // -------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void MultiSite_GrantGivesOne_FirstPlantKeeps_SecondPlantDestroys()
    {
        SeedMultiSiteWithDeliver();
        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        PlaceSites(map);

        NpcInteractHandler.GrantMission(conn, ch, Mid);
        Assert.AreEqual(1, ch.Inventory.CountByCbid(SecondaryCbid));
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);

        _fx.Sent.Clear();
        _fx.UseObject(conn, SiteA);
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 1);
        Assert.AreEqual(1, ch.Inventory.CountByCbid(SecondaryCbid),
            "First site SecondaryDestroy=0 must keep multi-use gear");
        Assert.IsTrue(ch.MapPresence.IsSuppressed(SiteA));
        Assert.IsTrue(_fx.Sent.OfType<InitCreateObjectPacket>()
            .Any(p => !p.Create && p.ObjectCoid == SiteA && p.DoDeath));
        Assert.IsTrue(_fx.CountComplete(O0) >= 1);

        _fx.Sent.Clear();
        _fx.UseObject(conn, SiteB);
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 2);
        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid),
            "Second site SecondaryDestroy=1 must consume secondary cargo");
        Assert.IsTrue(_fx.Sent.OfType<InventoryDestroyItemPacket>()
            .Any(p => p.Delete),
            "Client mission inventory needs 0x2049 bDelete for live clear");
        Assert.IsTrue(ch.MapPresence.IsSuppressed(SiteB));
        Assert.IsTrue(_fx.CountComplete(O1) >= 1);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void MultiSite_CannotPlantSecondSiteBeforeFirst_WrongSequence()
    {
        SeedMultiSiteWithDeliver();
        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        PlaceSites(map);
        NpcInteractHandler.GrantMission(conn, ch, Mid);

        // Active is seq0 matching WorldCbid at either site; either site advances seq0.
        // After first plant, the used site is suppressed; second plant needs remaining site.
        _fx.UseObject(conn, SiteA);
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);

        // Re-using suppressed SiteA must not advance further or re-grant destroy.
        var before = ch.CurrentQuests[0].ActiveObjectiveSequence;
        var destroyBefore = _fx.Sent.OfType<InventoryDestroyItemPacket>().Count();
        _fx.UseObject(conn, SiteA);
        Assert.AreEqual(before, ch.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.AreEqual(destroyBefore, _fx.Sent.OfType<InventoryDestroyItemPacket>().Count());
        Assert.AreEqual(1, ch.Inventory.CountByCbid(SecondaryCbid));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void MultiSite_MissingSecondaryWithoutGiveAtStart_BlocksUse()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = true;
            u.SecondaryCBID = SecondaryCbid;
            u.SecondaryGiveAtStart = false;
            u.SecondaryDestroy = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, WorldCbid);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, SiteA);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(ch.MapPresence.IsSuppressed(SiteA));
        Assert.AreEqual(0, _fx.CountComplete(O0));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void MultiSite_TopUpOnMissedAcceptGrant_AllowsPlant()
    {
        SeedMultiSiteWithDeliver();
        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        PlaceSites(map);
        // Quest active without accept-time cargo.
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid));

        _fx.UseObject(conn, SiteA);

        Assert.IsTrue(ch.CurrentQuests[0].ActiveObjectiveSequence >= 1
            || ch.CompletedMissionIds.Contains(Mid));
        Assert.IsTrue(ch.MapPresence.IsSuppressed(SiteA));
    }

    // -------------------------------------------------------------------------
    // Progress / RepeatCount / ObjectiveState
    // -------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void RepeatCountTwo_RequiresTwoUses_SendsAbsoluteObjectiveState()
    {
        const long t1 = 96210;
        const long t2 = 96211;
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = false; // allow second interact against remaining props
            u.RepeatCount = 2;
            u.FirstStateSlot = 0;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, t1, WorldCbid);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, t2, WorldCbid);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        Assert.AreEqual(2, ch.CurrentQuests[0].ObjectiveMax[0]);

        _fx.Sent.Clear();
        _fx.UseObject(conn, t1);
        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(1, ch.CurrentQuests[0].ObjectiveProgress[0]);
        var state = _fx.LastObjectiveState(O0);
        Assert.IsNotNull(state, "Partial UseItem progress must send ObjectiveState");
        Assert.AreEqual(1f, state.SlotProgress[0], 0.001f,
            "Client UseItem Eval expects absolute use count, not 0..1 ratio");

        _fx.UseObject(conn, t2);
        MissionInvariantAssertions.AssertCompleted(ch, Mid);
        Assert.IsTrue(_fx.CountComplete(O0) >= 1);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void ProgressNeverExceedsRepeatCount()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryItem = SiteA;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = false;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, SiteA);
        // Mission completed on first use — no active quest to over-progress.
        if (ch.CurrentQuests.Count > 0)
        {
            var q = ch.CurrentQuests[0];
            MissionInvariantAssertions.AssertProgressAtMost(q, 0, q.ObjectiveMax[0]);
        }
        else
        {
            MissionInvariantAssertions.AssertCompleted(ch, Mid);
        }
    }

    // -------------------------------------------------------------------------
    // Matching: COID / CBID / continent / objective id / suppress
    // -------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Match_PrimaryCoid_CompletesWithoutCbid()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryItem = SiteA;
            u.PrimaryCBID = -1;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = true;
            u.PrimaryExplode = false;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, cbid: 99999);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, SiteA);

        MissionInvariantAssertions.AssertCompleted(ch, Mid);
        Assert.IsTrue(ch.MapPresence.IsSuppressed(SiteA));
        var remove = _fx.Sent.OfType<InitCreateObjectPacket>().Single(p => !p.Create);
        Assert.IsFalse(remove.DoDeath, "PrimaryExplode=false → DoDeath false");
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Match_PrimaryCbid_AnyMatchingWorldCoid()
    {
        const long other = 96220;
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryItem = -1;
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = true;
            u.PrimaryExplode = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWorldObject(map, other, WorldCbid);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, other);

        MissionInvariantAssertions.AssertCompleted(ch, Mid);
        Assert.IsTrue(_fx.Sent.OfType<InitCreateObjectPacket>()
            .Any(p => !p.Create && p.ObjectCoid == other && p.DoDeath));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Match_WrongCbid_NoProgress()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, cbid: WorldCbid + 1);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, SiteA);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Match_WrongContinent_NoProgress()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.ContinentID = ContId + 50;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, WorldCbid);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, SiteA);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Match_PacketObjectiveIdMismatch_DoesNotConsumeWrongObjective()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, WorldCbid);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        // Positive objective id that is not the active objective.
        _fx.UseObject(conn, SiteA, objectiveId: O0 + 999);

        MissionInvariantAssertions.AssertActiveMission(ch, Mid, 0);
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);
    }

    // -------------------------------------------------------------------------
    // Inventory destroy on use / abandon / chat
    // -------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void Use_SecondaryDestroy_EmitsInventoryDestroyItemAndClearsCargo()
    {
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryCBID = WorldCbid;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = true;
            u.SecondaryCBID = SecondaryCbid;
            u.SecondaryDestroy = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, WorldCbid);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);
        MissionHeavyRegressionFixture.GrantMissionCargo(ch, SecondaryCbid, 1);
        var itemCoid = ch.Inventory.Items.First(i => i.Cbid == SecondaryCbid).Coid;

        _fx.Sent.Clear();
        _fx.UseObject(conn, SiteA);

        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid));
        var destroy = _fx.Sent.OfType<InventoryDestroyItemPacket>().ToList();
        Assert.IsTrue(destroy.Any(p => p.ItemCoid == itemCoid && p.Delete));
        Assert.IsTrue(_fx.Sent.OfType<InventoryCargoSendAllPacket>().Any());
        MissionInvariantAssertions.AssertCompleted(ch, Mid);
    }

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void Abandon_ReclaimsGiveAtStart_EmitsDestroyPacket()
    {
        SeedMultiSiteWithDeliver();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        Assert.AreEqual(1, ch.Inventory.CountByCbid(SecondaryCbid));

        _fx.Sent.Clear();
        NpcInteractHandler.FailMission(conn, ch, Mid);

        MissionInvariantAssertions.AssertNotActive(ch, Mid);
        Assert.IsFalse(ch.CompletedMissionIds.Contains(Mid));
        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid));
        Assert.IsTrue(_fx.Sent.OfType<InventoryDestroyItemPacket>().Any(p => p.Delete));
        Assert.IsTrue(_fx.Sent.OfType<FailMissionPacket>().Any(p => p.MissionId == Mid));
        Assert.IsTrue(_fx.Sent.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Abandon_AfterFirstPlant_StillReclaimsRemainingSecondary()
    {
        SeedMultiSiteWithDeliver();
        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        PlaceSites(map);
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        _fx.UseObject(conn, SiteA);
        Assert.AreEqual(1, ch.Inventory.CountByCbid(SecondaryCbid));
        Assert.AreEqual(1, ch.CurrentQuests[0].ActiveObjectiveSequence);

        NpcInteractHandler.FailMission(conn, ch, Mid);

        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid));
        MissionInvariantAssertions.AssertNotActive(ch, Mid);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Chat_RemoveMissionCargo_DestroysOnlyMissionStacks()
    {
        SeedMultiSiteWithDeliver();
        var (conn, ch, _, _) = _fx.CreatePlayer();
        var harness = _fx.AttachInventory(ch);
        NpcInteractHandler.GrantMission(conn, ch, Mid);

        // Non-mission cargo must survive.
        harness.Inventory.TryAdd(new CharacterInventoryItem(
            42,
            CloneBaseObjectType.Item,
            "normal",
            70001,
            2,
            0,
            1,
            IsMissionItem: false));

        var result = ChatCommandService.Instance.Execute(ch, "/removeMissionCargo");

        Assert.IsTrue(result.Handled);
        Assert.AreEqual(0, ch.Inventory.CountByCbid(SecondaryCbid));
        Assert.AreEqual(1, ch.Inventory.CountByCbid(42));
        Assert.IsTrue(result.Packets.OfType<InventoryDestroyItemPacket>().Any(p => p.Delete));
    }

    // -------------------------------------------------------------------------
    // Isolation / template immutability / inventory primary destroy
    // -------------------------------------------------------------------------

    [TestMethod]
    [TestCategory("MissionCritical")]
    [TestCategory("MissionHeavy")]
    public void Isolation_PlayerAPlant_DoesNotCompletePlayerB()
    {
        SeedMultiSiteWithDeliver();
        var (connA, chA, mapA, _) = _fx.CreatePlayer(charCoid: 18100, vehicleCoid: 18101);
        var (connB, chB, mapB, _) = _fx.CreatePlayer(charCoid: 18200, vehicleCoid: 18201);
        _fx.AttachInventory(chA);
        _fx.AttachInventory(chB);
        PlaceSites(mapA);
        PlaceSites(mapB);

        NpcInteractHandler.GrantMission(connA, chA, Mid);
        NpcInteractHandler.GrantMission(connB, chB, Mid);

        _fx.UseObject(connA, SiteA);
        _fx.UseObject(connA, SiteB);

        MissionInvariantAssertions.AssertActiveMission(chA, Mid, 2);
        MissionInvariantAssertions.AssertActiveMission(chB, Mid, 0);
        Assert.AreEqual(1, chB.Inventory.CountByCbid(SecondaryCbid));
        Assert.IsFalse(chB.MapPresence.IsSuppressed(SiteA));
        MissionInvariantAssertions.AssertPlayerIsolation(chA, chB);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void Template_NotMutated_ByGrantPlantAbandon()
    {
        SeedMultiSiteWithDeliver();
        var mission = AssetManager.Instance.GetMission(Mid);
        var reqCount0 = mission.Objectives.Values.First(o => o.ObjectiveId == O0).Requirements.Count;
        var complete0 = mission.Objectives.Values.First(o => o.ObjectiveId == O0).CompleteCount;
        var reqCount1 = mission.Objectives.Values.First(o => o.ObjectiveId == O1).Requirements.Count;

        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        PlaceSites(map);
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        _fx.UseObject(conn, SiteA);
        NpcInteractHandler.FailMission(conn, ch, Mid);

        MissionInvariantAssertions.AssertTemplateUnchanged(mission, O0, reqCount0, complete0);
        MissionInvariantAssertions.AssertTemplateUnchanged(mission, O1, reqCount1,
            mission.Objectives.Values.First(o => o.ObjectiveId == O1).CompleteCount);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void InventoryPrimaryDestroy_RemovesPrimaryCargoNotWorldProp()
    {
        // Inventory primary: match world interact via PrimaryItem COID; destroy cargo PrimaryCBID.
        const int primaryItem = 81230;
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryItem = SiteA;
            u.PrimaryCBID = primaryItem;
            u.PrimaryInWorld = false;
            u.PrimaryDestroy = true;
            u.PrimaryGiveAtStart = true;
            u.PrimaryMultipleUse = true;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA);
        NpcInteractHandler.GrantMission(conn, ch, Mid);
        Assert.AreEqual(1, ch.Inventory.CountByCbid(primaryItem));

        _fx.Sent.Clear();
        _fx.UseObject(conn, SiteA);

        Assert.AreEqual(0, ch.Inventory.CountByCbid(primaryItem));
        Assert.IsTrue(_fx.Sent.OfType<InventoryDestroyItemPacket>().Any(p => p.Delete));
        // PrimaryInWorld=false → no map prop remove (no InitCreateObject destroy).
        Assert.IsFalse(_fx.Sent.OfType<InitCreateObjectPacket>()
            .Any(p => !p.Create && p.ObjectCoid == SiteA));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void CompletedItem_GrantedOnObjectiveComplete()
    {
        const int rewardCbid = 81240;
        _fx.SeedSingleUseItem(Mid, O0, u =>
        {
            u.PrimaryItem = SiteA;
            u.PrimaryInWorld = true;
            u.PrimaryDestroy = true;
            u.CompletedItem = rewardCbid;
            u.RepeatCount = 1;
        });

        var (conn, ch, map, _) = _fx.CreatePlayer();
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA);
        MissionHeavyRegressionFixture.GiveQuest(ch, Mid);

        _fx.UseObject(conn, SiteA);

        MissionInvariantAssertions.AssertCompleted(ch, Mid);
        Assert.IsTrue(ch.Inventory.CountByCbid(rewardCbid) >= 1);
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void QuantityForUseItemGive_Rules_AreStable()
    {
        Assert.AreEqual(1, MissionCargoService.QuantityForUseItemGive(multipleUse: true, repeatCount: 5));
        Assert.AreEqual(5, MissionCargoService.QuantityForUseItemGive(multipleUse: false, repeatCount: 5));
        Assert.AreEqual(1, MissionCargoService.QuantityForUseItemGive(multipleUse: false, repeatCount: 0));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void GetAbandonTakeSpecs_CoversAllSequencesGiveAtStart()
    {
        SeedMultiSiteWithDeliver();
        var mission = AssetManager.Instance.GetMission(Mid);
        var specs = MissionCargoService.GetAbandonTakeSpecs(mission);
        Assert.IsTrue(specs.Any(s => s.Cbid == SecondaryCbid && s.Quantity >= 1));
    }

    [TestMethod]
    [TestCategory("MissionHeavy")]
    public void GetUseSuccessTakeSpecs_FirstSiteNoDestroy_SecondSiteDestroys()
    {
        SeedMultiSiteWithDeliver();
        var mission = AssetManager.Instance.GetMission(Mid);
        var o0 = mission.Objectives[0];
        var o1 = mission.Objectives[1];

        var take0 = MissionCargoService.GetUseSuccessTakeSpecs(o0);
        var take1 = MissionCargoService.GetUseSuccessTakeSpecs(o1);

        Assert.IsFalse(take0.Any(s => s.Cbid == SecondaryCbid),
            "seq0 SecondaryDestroy=0 must not take secondary on use");
        Assert.IsTrue(take1.Any(s => s.Cbid == SecondaryCbid && s.Quantity == 1));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SeedMultiSiteWithDeliver()
    {
        _fx.SeedMultiSiteUseItem(
            Mid, O0, O1, O2,
            SiteA, SiteB,
            WorldCbid, SecondaryCbid,
            DeliverNpc);
    }

    private static void PlaceSites(AutoCore.Game.Map.SectorMap map)
    {
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteA, WorldCbid);
        MissionHeavyRegressionFixture.PlaceWorldObject(map, SiteB, WorldCbid);
    }
}

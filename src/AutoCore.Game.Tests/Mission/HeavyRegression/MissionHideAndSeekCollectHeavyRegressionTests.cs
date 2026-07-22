using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Mission.HeavyRegression;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Heavy regression for Hide-and-Seek-shaped collect kill-to-loot
/// (retail 3668: OptionalDropPercent 35%, Marsh Alligrake → Alligrake Hide ×2 → Jake turn-in).
/// Synthetic ids only.
/// </summary>
[TestClass]
public class MissionHideAndSeekCollectHeavyRegressionTests
{
    private MissionHeavyRegressionFixture _fx = null!;

    private const int MissionId = 943668;
    private const int ObjectiveId = 946948;
    private const int HideCbid = 944172;
    private const int AlligrakeCbid = 9412685;
    private const int GiverNpcCbid = 942545;
    private const long GiverCoid = 952545;

    [TestInitialize]
    public void SetUp()
    {
        _fx = new MissionHeavyRegressionFixture();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCloneBase(HideCbid, CloneBaseObjectType.Item);
        MissionCollectProgress.ResetDropRollForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        MissionCollectProgress.ResetDropRollForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        _fx.Dispose();
    }

    [TestMethod]
    public void HideAndSeek_KillWithForcedDrop_SpawnsMissionHide()
    {
        SeedHideAndSeek(continentId: MissionHeavyRegressionFixture.ContId);
        MissionCollectProgress.NextDropRoll01 = () => 0.0;

        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, MissionId);
        _fx.AttachInventory(ch);

        KillAlligrake(map, ch, victimCoid: 96001);

        var loot = map.Objects.Values
            .OfType<SimpleObject>()
            .FirstOrDefault(o => o.CBID == HideCbid);
        Assert.IsNotNull(loot);
        Assert.IsTrue(loot.PossibleMissionItem);
        Assert.AreEqual(1, ch.CurrentQuests.Count, "Collect-only must not auto-complete on drop");
    }

    [TestMethod]
    public void HideAndSeek_MissedRoll_NoDrop()
    {
        SeedHideAndSeek(continentId: MissionHeavyRegressionFixture.ContId);
        MissionCollectProgress.NextDropRoll01 = () => 0.99; // 99 >= 35

        var (_, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, MissionId);

        KillAlligrake(map, ch, victimCoid: 96002);

        Assert.IsFalse(map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == HideCbid));
    }

    [TestMethod]
    public void HideAndSeek_PickupTwo_ThenGiverTurnIn_Completes()
    {
        SeedHideAndSeek(continentId: MissionHeavyRegressionFixture.ContId);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, MissionId);
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceNpc(map, GiverCoid, GiverNpcCbid, new Vector3(5, 0, 0));

        ch.Inventory.TryAdd(new CharacterInventoryItem(
            HideCbid, CloneBaseObjectType.Item, "hide", 97001, 0, 0, 2, true));
        MissionCollectProgress.SyncProgressFromInventory(ch, HideCbid);

        Assert.AreEqual(2, ch.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsTrue(NpcInteractHandler.IsCollectTurnInReady(
            ch,
            ch.CurrentQuests[0],
            AssetManager.Instance.GetMission(MissionId).Objectives[0],
            GiverNpcCbid));

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = false,
            MissionGiver = new TFID(GiverCoid, false),
        });

        Assert.IsTrue(ch.CompletedMissionIds.Contains(MissionId));
        Assert.AreEqual(0, ch.CurrentQuests.Count);
        Assert.AreEqual(0, ch.Inventory.CountByCbid(HideCbid), "Turn-in must take collected hides");
    }

    /// <summary>
    /// Live Hide and Seek (3668): client Collect_Eval uses cargo; server ObjectiveProgress can stay 0
    /// if pickup sync missed. Dialog OK must still complete and take hides (not "already active").
    /// </summary>
    [TestMethod]
    public void HideAndSeek_TurnInWithHidesButStaleProgress_CompletesAndTakesCargo()
    {
        SeedHideAndSeek(continentId: MissionHeavyRegressionFixture.ContId);
        var (conn, ch, map, _) = _fx.CreatePlayer();
        MissionHeavyRegressionFixture.GiveQuest(ch, MissionId);
        _fx.AttachInventory(ch);
        MissionHeavyRegressionFixture.PlaceNpc(map, GiverCoid, GiverNpcCbid, new Vector3(5, 0, 0));

        ch.Inventory.TryAdd(new CharacterInventoryItem(
            HideCbid, CloneBaseObjectType.Item, "hide", 97002, 0, 0, 2, true));
        // Intentionally do NOT call SyncProgressFromInventory — stale 0 progress.
        Assert.AreEqual(0, ch.CurrentQuests[0].ObjectiveProgress[0]);

        NpcInteractHandler.HandleMissionDialogResponse(conn, new MissionDialogResponsePacket
        {
            MissionId = MissionId,
            Accepted = true,
            MissionGiver = new TFID(GiverCoid, false),
        });

        Assert.IsTrue(ch.CompletedMissionIds.Contains(MissionId),
            "Collect turn-in must complete from cargo even when ObjectiveProgress was stale");
        Assert.AreEqual(0, ch.CurrentQuests.Count);
        Assert.AreEqual(0, ch.Inventory.CountByCbid(HideCbid), "Turn-in must take collected hides");
    }

    private static void SeedHideAndSeek(int continentId)
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var collect = new ObjectiveRequirementCollect(obj)
        {
            ItemCBID = HideCbid,
            NumToCollect = 2,
            OptionalDropPercent = 35f,
            ContinentId = continentId,
            TargetCount = 1,
            FirstStateSlot = 0,
            TakeItems = false,
        };
        collect.OptinonalTargets[0] = AlligrakeCbid;
        obj.Requirements.Add(collect);

        var mission = Mission.CreateForTests(MissionId, obj);
        mission.NPC = GiverNpcCbid;
        AssetManager.Instance.SetTestMission(mission);
    }

    private static void KillAlligrake(SectorMap map, Character killer, long victimCoid)
    {
        var victim = new GraphicsObject(GraphicsObjectType.Graphics);
        victim.InitializeHealthForTests(5);
        victim.SetCbidForTests(AlligrakeCbid);
        victim.SetCoid(victimCoid, false);
        victim.Position = new Vector3(1, 0, 1);
        victim.SetMap(map);
        victim.SetMurderer(killer.CurrentVehicle);
        victim.OnDeath(DeathType.Silent);
    }
}

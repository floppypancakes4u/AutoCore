using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Unit coverage for collect kill-to-loot: match, OptionalDropPercent roll, pickup progress, turn-in.
/// </summary>
[TestClass]
public class MissionCollectProgressUnitTests
{
    private const int MissionId = 3668;
    private const int ObjectiveId = 6948;
    private const int HideCbid = 4172;
    private const int AlligrakeCbid = 12685;
    private const int GiverNpcCbid = 2545;
    private const int ContinentId = 426;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        MissionCollectProgress.ResetDropRollForTests();
        IncompleteHandlerLog.TestSink = null;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        MissionCollectProgress.ResetDropRollForTests();
    }

    [TestMethod]
    public void CollectMatches_TargetCbidAndContinent()
    {
        var collect = SeedCollectReq(dropPercent: 35f, targetCbid: AlligrakeCbid, continentId: ContinentId);

        Assert.IsTrue(MissionCollectProgress.CollectMatches(
            collect, AlligrakeCbid, ContinentId, victimTemplateId: -1, victimIsPlayer: false));
        Assert.IsFalse(MissionCollectProgress.CollectMatches(
            collect, AlligrakeCbid, continentId: 999, victimTemplateId: -1, victimIsPlayer: false));
        Assert.IsFalse(MissionCollectProgress.CollectMatches(
            collect, victimCbid: 1, ContinentId, victimTemplateId: -1, victimIsPlayer: false));
    }

    [TestMethod]
    public void CollectMatches_ZeroDropPercent_False()
    {
        var collect = SeedCollectReq(dropPercent: 0f, targetCbid: AlligrakeCbid, continentId: -1);
        Assert.IsFalse(MissionCollectProgress.CollectMatches(
            collect, AlligrakeCbid, 1, -1, false));
    }

    [TestMethod]
    public void ShouldDrop_RollsAgainstOptionalDropPercent()
    {
        var collect = SeedCollectReq(dropPercent: 35f, targetCbid: AlligrakeCbid, continentId: -1);

        MissionCollectProgress.NextDropRoll01 = () => 0.34; // 34 < 35
        Assert.IsTrue(MissionCollectProgress.ShouldDrop(collect));

        MissionCollectProgress.NextDropRoll01 = () => 0.35; // 35 not < 35
        Assert.IsFalse(MissionCollectProgress.ShouldDrop(collect));

        collect.OptionalDropPercent = 100f;
        MissionCollectProgress.NextDropRoll01 = () => 0.999;
        Assert.IsTrue(MissionCollectProgress.ShouldDrop(collect));
    }

    [TestMethod]
    public void NotifyObjectKilled_MatchingVictim_SpawnsMissionLoot()
    {
        AssetManagerTestHelper.RegisterCloneBase(HideCbid, CloneBaseObjectType.Item);
        SeedHideAndSeekMission();
        MissionCollectProgress.NextDropRoll01 = () => 0.0; // always drop

        var (conn, character, map) = CreatePlayer(ContinentId);
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var victim = new GraphicsObject(GraphicsObjectType.Graphics);
        victim.InitializeHealthForTests(5);
        victim.SetCbidForTests(AlligrakeCbid);
        victim.SetCoid(91001, false);
        victim.Position = new Vector3(1, 0, 1);
        victim.SetMap(map);
        victim.SetMurderer(character.CurrentVehicle);

        MissionCollectProgress.NotifyObjectKilled(victim);

        var loot = map.Objects.Values
            .OfType<SimpleObject>()
            .FirstOrDefault(o => o.CBID == HideCbid && o.ObjectId.Coid != victim.ObjectId.Coid);
        Assert.IsNotNull(loot, "Collect drop must spawn Alligrake Hide as ground loot");
        Assert.IsTrue(loot.PossibleMissionItem, "Quest collect loot must flag PossibleMissionItem");
    }

    [TestMethod]
    public void NotifyObjectKilled_WrongVictim_NoSpawn()
    {
        AssetManagerTestHelper.RegisterCloneBase(HideCbid, CloneBaseObjectType.Item);
        SeedHideAndSeekMission();
        MissionCollectProgress.NextDropRoll01 = () => 0.0;

        var (_, character, map) = CreatePlayer(ContinentId);
        character.CurrentQuests.Add(new CharacterQuest(MissionId, 0));
        character.CurrentQuests[0].PopulateFromAssets();

        var victim = new GraphicsObject(GraphicsObjectType.Graphics);
        victim.InitializeHealthForTests(5);
        victim.SetCbidForTests(99999);
        victim.SetCoid(91002, false);
        victim.SetMap(map);
        victim.SetMurderer(character.CurrentVehicle);

        MissionCollectProgress.NotifyObjectKilled(victim);

        Assert.IsFalse(map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == HideCbid));
    }

    [TestMethod]
    public void NotifyObjectKilled_AlreadyHasEnough_NoSpawn()
    {
        AssetManagerTestHelper.RegisterCloneBase(HideCbid, CloneBaseObjectType.Item);
        SeedHideAndSeekMission();
        MissionCollectProgress.NextDropRoll01 = () => 0.0;

        var (_, character, map) = CreatePlayer(ContinentId);
        character.CurrentQuests.Add(new CharacterQuest(MissionId, 0));
        character.CurrentQuests[0].PopulateFromAssets();
        character.Inventory.TryAdd(new CharacterInventoryItem(
            HideCbid, CloneBaseObjectType.Item, "hide", 70001, 0, 0, 2, true));

        var victim = new GraphicsObject(GraphicsObjectType.Graphics);
        victim.InitializeHealthForTests(5);
        victim.SetCbidForTests(AlligrakeCbid);
        victim.SetCoid(91003, false);
        victim.SetMap(map);
        victim.SetMurderer(character.CurrentVehicle);

        MissionCollectProgress.NotifyObjectKilled(victim);

        Assert.IsFalse(map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == HideCbid));
    }

    [TestMethod]
    public void SyncProgressFromInventory_UpdatesAbsoluteState()
    {
        SeedHideAndSeekMission();
        var (conn, character, _) = CreatePlayer(ContinentId);
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        character.Inventory.TryAdd(new CharacterInventoryItem(
            HideCbid, CloneBaseObjectType.Item, "hide", 70002, 0, 0, 1, true));

        MissionCollectProgress.SyncProgressFromInventory(character, HideCbid);

        Assert.AreEqual(1, quest.ObjectiveProgress[0]);
        Assert.AreEqual(2, quest.ObjectiveMax[0]);
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any(p =>
            p.ObjectiveId == ObjectiveId && p.SlotProgress[0] == 1f));
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void IsCollectTurnInReady_RequiresFullCountAtGiver()
    {
        SeedHideAndSeekMission(giverNpc: GiverNpcCbid);
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        quest.ObjectiveProgress[0] = 1;
        quest.ObjectiveMax[0] = 2;

        var objective = AssetManager.Instance.GetMission(MissionId).Objectives[0];
        Assert.IsFalse(NpcInteractHandler.IsCollectTurnInReady(quest, objective, GiverNpcCbid));

        quest.ObjectiveProgress[0] = 2;
        Assert.IsTrue(NpcInteractHandler.IsCollectTurnInReady(quest, objective, GiverNpcCbid));
        Assert.IsFalse(NpcInteractHandler.IsCollectTurnInReady(quest, objective, npcCbid: 1));
    }

    private static ObjectiveRequirementCollect SeedCollectReq(
        float dropPercent,
        int targetCbid,
        int continentId)
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1, 1);
        var collect = new ObjectiveRequirementCollect(obj)
        {
            ItemCBID = HideCbid,
            NumToCollect = 2,
            OptionalDropPercent = dropPercent,
            ContinentId = continentId,
            TargetCount = 1,
        };
        collect.OptinonalTargets[0] = targetCbid;
        return collect;
    }

    private void SeedHideAndSeekMission(int giverNpc = GiverNpcCbid)
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var collect = new ObjectiveRequirementCollect(obj)
        {
            ItemCBID = HideCbid,
            NumToCollect = 2,
            OptionalDropPercent = 35f,
            ContinentId = ContinentId,
            TargetCount = 1,
            FirstStateSlot = 0,
            TakeItems = false,
        };
        collect.OptinonalTargets[0] = AlligrakeCbid;
        obj.Requirements.Add(collect);

        var mission = Mission.CreateForTests(MissionId, obj);
        mission.NPC = giverNpc;
        AssetManager.Instance.SetTestMission(mission);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_collect_{continentId}",
            DisplayName = "collect-test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        return CreatePlayerOnMap(map);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayerOnMap(SectorMap map)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(200, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(201, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        vehicle.Position = new Vector3(0, 0, 0);
        character.Position = new Vector3(0, 0, 0);
        return (connection, character, map);
    }
}

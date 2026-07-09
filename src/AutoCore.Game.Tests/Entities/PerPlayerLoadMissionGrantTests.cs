using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Tests for map PerPlayerLoad on-load mission grant (FireOnLoadPlayerMissions + GiveMission + 0x206C).
/// </summary>
[TestClass]
public class PerPlayerLoadMissionGrantTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, packet) => _sent.Add(packet);
        AssetManager.Instance.ClearTestMissions();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        _sent.Clear();
    }

    [TestMethod]
    public void Character_DoesNotPreSeedMission554()
    {
        Assert.AreEqual(0, new Character().CurrentQuests.Count);
    }

    [TestMethod]
    public void GiveMission_TracksQuestOnServer()
    {
        SeedMission(554, (714, 0, 1));
        var (character, vehicle, map) = CreatePlayer();
        PlaceReaction(map, 14137, ReactionType.GiveMission, genericVar1: 554);

        Assert.IsTrue((map.GetObjectByCoid(14137) as Reaction).TriggerIfPossible(character));
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(554, character.CurrentQuests[0].MissionId);
    }

    [TestMethod]
    public void TryGetPerPlayerLoadTrigger_WhenFindable_ReturnsTriggerWithReactions()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.SetEventTriggerCoidsForTests(perPlayerLoad: 16217);
        PlaceEventTrigger(map, triggerCoid: 16217, reactionCoid: 14137, ReactionType.GiveMission, genericVar1: 554);

        Assert.IsTrue(map.TryGetPerPlayerLoadTrigger(out var trigger));
        Assert.IsNotNull(trigger);
        Assert.AreEqual(16217, trigger.ObjectId.Coid);
        Assert.AreEqual(1, trigger.Template.Reactions.Count);
        Assert.AreEqual(14137, trigger.Template.Reactions[0]);
    }

    [TestMethod]
    public void TryGetPerPlayerLoadTrigger_WhenMissing_ReturnsFalse()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.SetEventTriggerCoidsForTests(perPlayerLoad: 16217);
        // Trigger COID set in header but not placed on map / templates.

        Assert.IsFalse(map.TryGetPerPlayerLoadTrigger(out var trigger));
        Assert.IsNull(trigger);
    }

    [TestMethod]
    public void TryGetPerPlayerLoadTrigger_WhenHeaderDisabled_ReturnsFalse()
    {
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.SetEventTriggerCoidsForTests(perPlayerLoad: -1);

        Assert.IsFalse(map.TryGetPerPlayerLoadTrigger(out _));
    }

    [TestMethod]
    public void FireOnLoadPlayerMissions_WhenTriggerFindable_GrantsMissionViaGiveMissionReaction()
    {
        // Retail design: client DoPlayerOnLoad looks up m_coidPerPlayerLoadTrigger and fires it.
        // Server mirrors: only if findable, run that trigger's reaction list (GiveMission 554 on 707).
        SeedMission(554, (714, 0, 1));
        var (character, vehicle, map) = CreatePlayer();

        map.MapData.SetEventTriggerCoidsForTests(perPlayerLoad: 16217);
        PlaceEventTrigger(map, triggerCoid: 16217, reactionCoid: 14137, ReactionType.GiveMission, genericVar1: 554);

        var fired = map.FireOnLoadPlayerMissions(character);

        Assert.IsTrue(fired);
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(554, character.CurrentQuests[0].MissionId);
        Assert.IsTrue(_sent.OfType<GroupReactionCallPacket>().Any());
        Assert.AreEqual(1, _sent.OfType<GroupReactionCallPacket>().First().Count);
    }

    [TestMethod]
    public void FireOnLoadPlayerMissions_WhenTriggerNotFindable_DoesNotGrantOrSend()
    {
        SeedMission(554, (714, 0, 1));
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.SetEventTriggerCoidsForTests(perPlayerLoad: 16217);
        // Header names 16217 but trigger is not on the map → not findable.

        var fired = map.FireOnLoadPlayerMissions(character);

        Assert.IsFalse(fired);
        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count());
    }

    [TestMethod]
    public void FireOnLoadPlayerMissions_WhenNoPerPlayerLoad_DoesNotGrant()
    {
        SeedMission(554, (714, 0, 1));
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.SetEventTriggerCoidsForTests(perPlayerLoad: -1);
        PlaceEventTrigger(map, triggerCoid: 16217, reactionCoid: 14137, ReactionType.GiveMission, genericVar1: 554);

        Assert.IsFalse(map.FireOnLoadPlayerMissions(character));
        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void TriggerReactions_VehicleActivator_ResolvesToCharacterAndSends()
    {
        var (character, vehicle, map) = CreatePlayer();
        PlaceReaction(map, 500, ReactionType.Activate, genericVar1: 0);

        map.TriggerReactions(vehicle, new List<long> { 500 });

        Assert.AreEqual(1, _sent.OfType<GroupReactionCallPacket>().Count());
    }

    [TestMethod]
    public void SetActiveObjective_UpdatesSequenceWhenQuestPresent()
    {
        SeedMission(554, (714, 0, 1));
        var (character, vehicle, map) = CreatePlayer();
        var quest = new CharacterQuest(554, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        PlaceReaction(map, 17935, ReactionType.SetActiveObjective, genericVar1: 714);
        Assert.IsTrue((map.GetObjectByCoid(17935) as Reaction).TriggerIfPossible(character));
        Assert.AreEqual(0, character.CurrentQuests[0].ActiveObjectiveSequence);
    }

    private static void SeedMission(int missionId, params (int ObjectiveId, byte Sequence, int CompleteCount)[] objectives)
    {
        var objs = objectives
            .Select(o => MissionObjective.CreateForTests(o.ObjectiveId, o.Sequence, missionId, o.CompleteCount))
            .ToArray();
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, objs));
    }

    private static SectorMap CreateMap(int continentId = 707)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_mission_{continentId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var map = CreateMap();
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (character, vehicle, map);
    }

    private static void PlaceReaction(SectorMap map, int coid, ReactionType type, int genericVar1)
    {
        var template = new ReactionTemplate
        {
            COID = coid,
            ReactionType = type,
            GenericVar1 = genericVar1,
        };
        var reaction = new Reaction(template);
        reaction.SetCoid(coid, false);
        reaction.SetMap(map);
    }

    private static void PlaceEventTrigger(
        SectorMap map,
        int triggerCoid,
        int reactionCoid,
        ReactionType reactionType,
        int genericVar1)
    {
        PlaceReaction(map, reactionCoid, reactionType, genericVar1);

        var triggerTemplate = new TriggerTemplate
        {
            COID = triggerCoid,
            TargetType = TriggerTargetType.Players,
            Scale = 10f,
        };
        triggerTemplate.Reactions.Add(reactionCoid);
        var trigger = new Trigger(triggerTemplate);
        trigger.SetCoid(triggerCoid, false);
        trigger.SetMap(map);
    }
}

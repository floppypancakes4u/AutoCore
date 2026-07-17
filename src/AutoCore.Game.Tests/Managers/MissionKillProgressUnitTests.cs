using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Unit coverage for <see cref="MissionKillProgress"/> matching and edge paths.
/// </summary>
[TestClass]
public class MissionKillProgressUnitTests
{
    private const int MissionId = 94001;
    private const int ObjectiveId = 94002;
    private const int ContId = 822;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        IncompleteHandlerLog.TestSink = null;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestMethod]
    public void NotifyObjectKilled_NullVictim_NoOp()
    {
        MissionKillProgress.NotifyObjectKilled(null);
    }

    [TestMethod]
    public void ResolveKiller_NullOrEmptyMurderer_ReturnsNull()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(5);
        Assert.IsNull(MissionKillProgress.ResolveKillerCharacter(prop));
        Assert.IsNull(MissionKillProgress.ResolveKillerCharacter(null));
    }

    [TestMethod]
    public void KillMatches_NegativeKill_False()
    {
        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetCBID = 10,
            NegativeKill = true,
        };
        Assert.IsFalse(MissionKillProgress.KillMatches(kill, 10, 0, 1));
    }

    [TestMethod]
    public void KillMatches_ContinentMismatch_False()
    {
        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetCBID = 10,
            ContinentId = 5,
        };
        Assert.IsFalse(MissionKillProgress.KillMatches(kill, 10, 0, continentId: 9));
        Assert.IsTrue(MissionKillProgress.KillMatches(kill, 10, 0, continentId: 5));
    }

    [TestMethod]
    public void KillMatches_TargetIsPlayer_False()
    {
        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetIsPlayer = true,
            TargetCBID = 10,
        };
        Assert.IsFalse(MissionKillProgress.KillMatches(kill, 10, 0, 1));
    }

    [TestMethod]
    public void KillMatches_FactionTarget()
    {
        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetIsFaction = true,
            TargetCBID = 42,
        };
        Assert.IsTrue(MissionKillProgress.KillMatches(kill, victimCbid: 999, victimFaction: 42, continentId: 1));
        Assert.IsFalse(MissionKillProgress.KillMatches(kill, 999, victimFaction: 7, continentId: 1));
    }

    [TestMethod]
    public void KillMatches_NullKill_False()
    {
        Assert.IsFalse(MissionKillProgress.KillMatches(null, 1, 1, 1));
    }

    [TestMethod]
    public void KillAggregateMatches_CbidListAndFactionAndNegative()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1);
        var agg = new ObjectiveRequirementKillAggregate(obj) { NumToKill = 1 };
        agg.Targets.Add(55);
        Assert.IsTrue(MissionKillProgress.KillAggregateMatches(agg, 55, 1, 0, 1));
        Assert.IsFalse(MissionKillProgress.KillAggregateMatches(agg, 56, 1, 0, 1));

        agg.NegativeKill = true;
        Assert.IsFalse(MissionKillProgress.KillAggregateMatches(agg, 55, 1, 0, 1));

        Assert.IsFalse(MissionKillProgress.KillAggregateMatches(null, 1, 1, 1, 1));
    }

    [TestMethod]
    public void KillAggregateMatches_ContinentAndFactionList()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1);
        var agg = new ObjectiveRequirementKillAggregate(obj) { ContinentId = 3, TargetIsFaction = true };
        agg.Targets.Add(9);
        Assert.IsFalse(MissionKillProgress.KillAggregateMatches(agg, 1, 1, victimFaction: 9, continentId: 2));
        Assert.IsTrue(MissionKillProgress.KillAggregateMatches(agg, 1, 1, victimFaction: 9, continentId: 3));
    }

    [TestMethod]
    public void TryMatchKillRequirement_EmptyOrNoMatch()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1);
        Assert.IsFalse(MissionKillProgress.TryMatchKillRequirement(obj, 1, 1, 1, 1, out _));
        Assert.IsFalse(MissionKillProgress.TryMatchKillRequirement(null, 1, 1, 1, 1, out _));

        obj.Requirements.Add(new ObjectiveRequirementCollect(obj));
        Assert.IsFalse(MissionKillProgress.TryMatchKillRequirement(obj, 1, 1, 1, 1, out _));
    }

    [TestMethod]
    public void EnsureProgressCapacity_ExpandsArrays()
    {
        var quest = new CharacterQuest(1, 0);
        Assert.AreEqual(8, quest.ObjectiveProgress.Length);
        MissionKillProgress.EnsureProgressCapacity(quest, 12);
        Assert.IsTrue(quest.ObjectiveProgress.Length >= 13);
        Assert.IsTrue(quest.ObjectiveMax.Length >= 13);
        MissionKillProgress.EnsureProgressCapacity(quest, 2); // no-op
        MissionKillProgress.EnsureProgressCapacity(null, 99);
    }

    [TestMethod]
    public void KillProp_FactionMatch_Completes()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var kill = new ObjectiveRequirementKill(obj)
        {
            TargetIsFaction = true,
            TargetCBID = 77,
            NumToKill = 1,
        };
        obj.Requirements.Add(kill);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(3);
        prop.SetCbidForTests(1);
        prop.Faction = 77;
        prop.SetCoid(5001, false);
        prop.SetMap(map);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        // Final kill-only: full progress, still active (giver turn-in).
        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any());
    }

    [TestMethod]
    public void KillProp_AggregateCbidList_CreditsProgress()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var agg = new ObjectiveRequirementKillAggregate(obj) { NumToKill = 1 };
        agg.Targets.Add(12001);
        agg.Targets.Add(12002);
        obj.Requirements.Add(agg);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(2);
        prop.SetCbidForTests(12002);
        prop.SetCoid(6001, false);
        prop.SetMap(map);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void NotifyObjectKilled_NoOwningConnection_NoOp()
    {
        SeedKillCbid(10);
        var map = CreateMapBare();
        var character = new Character();
        character.SetCoid(300, true);
        // no connection
        var vehicle = new Vehicle();
        vehicle.SetCoid(301, true);
        character.SetCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(1);
        prop.SetCbidForTests(10);
        prop.SetCoid(302, false);
        prop.SetMap(map);
        prop.SetMurderer(vehicle);
        MissionKillProgress.NotifyObjectKilled(prop);
        Assert.AreEqual(1, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void NotifyObjectKilled_MissingMissionAsset_SkipsQuest()
    {
        // Quest references unknown mission id
        var (conn, character, map) = CreatePlayer();
        character.CurrentQuests.Add(new CharacterQuest(999999, 0));
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(1);
        prop.SetCbidForTests(1);
        prop.SetCoid(303, false);
        prop.SetMap(map);
        prop.SetMurderer(character.CurrentVehicle);
        MissionKillProgress.NotifyObjectKilled(prop);
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    [TestMethod]
    public void KillAggregate_EmptyFilters_NoMatch()
    {
        var obj = MissionObjective.CreateForTests(1, 0, 1);
        var agg = new ObjectiveRequirementKillAggregate(obj);
        Assert.IsFalse(MissionKillProgress.KillAggregateMatches(agg, 1, 1, 1, 1));
    }

    [TestMethod]
    public void KillMatches_TargetCbidZeroOrNegative_FalseUnlessFaction()
    {
        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetCBID = -1,
        };
        Assert.IsFalse(MissionKillProgress.KillMatches(kill, 5, 0, 1));
    }

    [TestMethod]
    public void KillMatches_TargetIsTemplateVehicle_MatchesTemplateIdNotCbid()
    {
        // Retail Final Exam: <CBID>580</CBID><TargetIsTemplateVehicle>1</TargetIsTemplateVehicle>
        // Victim vehicle CBID is 12425; TemplateId is 580.
        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetCBID = 580,
            TargetIsTemplateVehicle = true,
            ContinentId = ContId,
            NumToKill = 1,
        };

        Assert.IsFalse(
            MissionKillProgress.KillMatches(kill, victimCbid: 1, victimFaction: 0, continentId: ContId, victimTemplateId: -1),
            "Must not match arbitrary CBID without template id or template chassis lookup");
        Assert.IsFalse(
            MissionKillProgress.KillMatches(kill, 12425, 0, continentId: ContId, victimTemplateId: 999),
            "Wrong template id must not match");
        Assert.IsTrue(
            MissionKillProgress.KillMatches(kill, 12425, 0, continentId: ContId, victimTemplateId: 580),
            "Template id 580 must match Final Exam kill requirement");
    }

    [TestMethod]
    public void KillMatches_TargetIsTemplateVehicle_FallbackMatchesTemplateChassisCbid()
    {
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate { Id = 580, VehicleCbid = 12425 },
        });

        var kill = new ObjectiveRequirementKill(MissionObjective.CreateForTests(1, 0, 1))
        {
            TargetCBID = 580,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
        };

        Assert.IsTrue(
            MissionKillProgress.KillMatches(kill, victimCbid: 12425, victimFaction: 0, continentId: ContId, victimTemplateId: -1),
            "When TemplateId is missing, chassis CBID from vehicle template row must still match");
    }

    [TestMethod]
    public void NotifyObjectKilled_TemplateVehicle_CreditsKillOnlyObjective()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj)
        {
            TargetCBID = 580,
            TargetIsTemplateVehicle = true,
            NumToKill = 1,
            ContinentId = ContId,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var npcVehicle = new Vehicle();
        npcVehicle.SetCoid(7001, true);
        npcVehicle.SetCbidForTests(12425);
        npcVehicle.TemplateId = 580;
        npcVehicle.SetMap(map);
        npcVehicle.SetMurderer(character.CurrentVehicle);
        npcVehicle.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0],
            "Template-vehicle kill must credit the kill-only objective");
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId),
            "Final kill-only waits for giver turn-in");
    }

    private void SeedKillCbid(int cbid)
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementKill(obj) { TargetCBID = cbid, NumToKill = 1 });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
    }

    private static SectorMap CreateMapBare()
    {
        var continent = new ContinentObject
        {
            Id = ContId + 50,
            MapFileName = "tm_kill_bare",
            DisplayName = "t",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    [TestMethod]
    public void KillProp_CompletedMissionSkipped()
    {
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var kill = new ObjectiveRequirementKill(obj) { TargetCBID = 5, NumToKill = 1 };
        obj.Requirements.Add(kill);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        character.CompletedMissionIds.Add(MissionId);
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);

        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.InitializeHealthForTests(2);
        prop.SetCbidForTests(5);
        prop.SetCoid(7001, false);
        prop.SetMap(map);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_killu_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
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
        return (connection, character, map);
    }
}

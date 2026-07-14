using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;

/// <summary>
/// Destroying map props / NPCs must progress kill objectives generically (CBID / template COID).
/// </summary>
[TestClass]
public class MissionKillProgressTests
{
    private const int MissionId = 93041;
    private const int ObjectiveId = 95446;
    private const int NextObjectiveId = 95447;
    private const int TargetCbid = 12001;
    private const long PropCoid = 9301;
    private const int ContId = 811;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        MapPropCorpseDespawn.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        MapPropCorpseDespawn.ResetForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void KillProp_MatchingCbid_CompletesSingleObjectiveMission()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId));
        Assert.IsTrue(_sent.OfType<ConvoyMissionsResponsePacket>().Any());
        // Map props leave a corpse for a delay before despawn — still present until flush.
        Assert.IsNotNull(map.GetObjectByCoid(PropCoid));
        Assert.IsTrue(MapPropCorpseDespawn.PendingCountForTests >= 1);
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(PropCoid));
    }

    [TestMethod]
    public void KillProp_MatchingCbid_AdvancesToNextObjective()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: NextObjectiveId);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ActiveObjectiveSequence);
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId));
        Assert.IsTrue(_sent.OfType<ObjectiveStatePacket>().Any());
    }

    [TestMethod]
    public void KillProp_WrongCbid_DoesNotAdvance()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, cbid: 99999);
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    [TestMethod]
    public void KillProp_NumToKillTwo_RequiresSecondKill()
    {
        SeedKillMission(TargetCbid, numToKill: 2, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop1 = PlaceProp(map, PropCoid, TargetCbid);
        prop1.SetMurderer(character.CurrentVehicle);
        prop1.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(1, character.CurrentQuests[0].ObjectiveProgress[0]);
        Assert.IsFalse(character.CompletedMissionIds.Contains(MissionId));

        var prop2 = PlaceProp(map, PropCoid + 1, TargetCbid);
        prop2.SetMurderer(character.CurrentVehicle);
        prop2.OnDeath(DeathType.Silent);

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void KillProp_TemplateIdMatch_Advances()
    {
        // kill_aggregate with TEMPLATEID list (map COID) rather than CBID
        var obj = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, 1);
        var kill = new ObjectiveRequirementKillAggregate(obj) { NumToKill = 1 };
        kill.TemplateTargets.Add((int)PropCoid);
        obj.Requirements.Add(kill);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));

        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, cbid: 1); // CBID irrelevant
        prop.SetMurderer(character.CurrentVehicle);
        prop.OnDeath(DeathType.Silent);

        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
    }

    [TestMethod]
    public void KillProp_NoMurderer_DoesNotAdvance()
    {
        SeedKillMission(TargetCbid, numToKill: 1, nextObjectiveId: null);
        var (conn, character, map) = CreatePlayer();
        GiveQuest(character);

        var prop = PlaceProp(map, PropCoid, TargetCbid);
        // no SetMurderer
        prop.OnDeath(DeathType.Silent);

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.IsFalse(_sent.OfType<CompleteDynamicObjectivePacket>().Any());
    }

    private void SeedKillMission(int targetCbid, int numToKill, int? nextObjectiveId)
    {
        var objA = MissionObjective.CreateForTests(ObjectiveId, 0, MissionId, completeCount: Math.Max(1, numToKill));
        var kill = new ObjectiveRequirementKill(objA)
        {
            TargetCBID = targetCbid,
            NumToKill = numToKill,
        };
        objA.Requirements.Add(kill);

        if (nextObjectiveId.HasValue)
        {
            var objB = MissionObjective.CreateForTests(nextObjectiveId.Value, 1, MissionId, 1);
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA, objB));
        }
        else
        {
            AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, objA));
        }
    }

    private static void GiveQuest(Character character)
    {
        var quest = new CharacterQuest(MissionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static GraphicsObject PlaceProp(SectorMap map, long coid, int cbid)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.InitializeHealthForTests(15);
        prop.SetCbidForTests(cbid);
        prop.SetCoid(coid, false);
        prop.SetInvincible(false);
        prop.SetMap(map);
        return prop;
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_kill_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(18248, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(18249, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (connection, character, map);
    }
}

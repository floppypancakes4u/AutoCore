using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

using AutoCore.Database.World.Models;
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
/// UseObject on non-NPC world objects for UseItem mission requirements.
/// </summary>
[TestClass]
public class UseObjectUseItemTests
{
    private const int MissionId = 91300;
    private const int ObjectiveId = 92300;
    private const long WorldObjectCoid = 9301;
    private const int ContId = 707;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        TriggerManager.Instance.ClearAllForTests();
        _sent.Clear();
    }

    [TestMethod]
    public void HandleUseObject_WorldObject_CompletesUseItemObjective()
    {
        SeedUseItemMission(MissionId, ObjectiveId, WorldObjectCoid, primaryCbid: -1);
        var (conn, character, map) = CreatePlayer();
        PlaceWorldObject(map, WorldObjectCoid, new Vector3(5, 0, 0));
        character.CurrentVehicle.Position = new Vector3(5, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(WorldObjectCoid, false),
            ObjectiveId = ObjectiveId,
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
        Assert.IsTrue(character.CompletedMissionIds.Contains(MissionId));
        Assert.IsTrue(_sent.OfType<CompleteDynamicObjectivePacket>().Any(p => p.ObjectiveId == ObjectiveId));
    }

    [TestMethod]
    public void HandleUseObject_WorldObject_WrongObjectiveIdHint_NoProgress()
    {
        SeedUseItemMission(MissionId, ObjectiveId, WorldObjectCoid, primaryCbid: -1);
        var (conn, character, map) = CreatePlayer();
        PlaceWorldObject(map, WorldObjectCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(WorldObjectCoid, false),
            ObjectiveId = ObjectiveId + 999, // client hint does not match
        });

        Assert.AreEqual(1, character.CurrentQuests.Count);
        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    [TestMethod]
    public void HandleUseObject_WorldObject_MatchesByCbid()
    {
        const int cbid = 4401;
        SeedUseItemMission(MissionId, ObjectiveId, primaryCoid: -1, primaryCbid: cbid);
        var (conn, character, map) = CreatePlayer();
        PlaceWorldObject(map, WorldObjectCoid, new Vector3(0, 0, 0), cbid);
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);
        GiveQuest(character, MissionId);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(WorldObjectCoid, false),
            ObjectiveId = -1, // no objective hint
        });

        Assert.AreEqual(0, character.CurrentQuests.Count);
    }

    [TestMethod]
    public void HandleUseObject_WorldObject_NoMatchingObjective_DoesNotThrow()
    {
        var (conn, character, map) = CreatePlayer();
        PlaceWorldObject(map, WorldObjectCoid, new Vector3(0, 0, 0));
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        NpcInteractHandler.HandleUseObject(conn, new UseObjectPacket
        {
            Target = new TFID(WorldObjectCoid, false),
            ObjectiveId = ObjectiveId,
        });

        Assert.AreEqual(0, _sent.OfType<CompleteDynamicObjectivePacket>().Count());
    }

    private static void SeedUseItemMission(int missionId, int objectiveId, long primaryCoid, int primaryCbid)
    {
        var objective = MissionObjective.CreateForTests(objectiveId, 0, missionId, 1);
        var useItem = new ObjectiveRequirementUseItem(objective)
        {
            PrimaryItem = primaryCoid,
            PrimaryCBID = primaryCbid,
            FirstStateSlot = 0,
        };
        objective.Requirements.Add(useItem);
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(missionId, objective));
    }

    private static void GiveQuest(Character character, int missionId)
    {
        var quest = new CharacterQuest(missionId, 0);
        quest.PopulateFromAssets();
        character.CurrentQuests.Add(quest);
    }

    private static void PlaceWorldObject(SectorMap map, long coid, Vector3 position, int cbid = 0)
    {
        var obj = new SimpleObject(GraphicsObjectType.Graphics);
        obj.SetCoid(coid, false);
        if (cbid > 0)
            obj.SetCbidForTests(cbid);
        obj.Position = position;
        obj.SetMap(map);
    }

    private (TNLConnection Conn, Character Character, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_mission_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(150, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(151, true);
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (connection, character, map);
    }
}

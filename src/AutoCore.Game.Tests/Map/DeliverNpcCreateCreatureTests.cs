using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
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
/// Deliver-phase pad NPCs must be client-visible: CreateCreature (0x2013) before/with ghost scope,
/// not ObjectInScope alone on a global MapNpcIdentity TFID.
/// </summary>
[TestClass]
public class DeliverNpcCreateCreatureTests
{
    private const int ContId = 8820;
    private const int MissionId = 98200;
    private const int DeliverCbid = 12448;
    private const long PadSpawnCoid = 25820;
    private const long CreatePadRx = 25819;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCreatureCloneBase(DeliverCbid, maxHitPoint: 50);
        NpcInteractHandler.InvalidateMissionIndex();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManager.Instance.ClearTestMissions();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        NpcInteractHandler.InvalidateMissionIndex();
        _sent.Clear();
    }

    [TestMethod]
    public void ReplayDeliverPhase_SendsCreateCreatureForPadNpc()
    {
        SeedDeliverMission();
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[PadSpawnCoid] = MakePadSpawnTemplate();
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlacePadMarker(map);

        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);

        _sent.Clear();
        map.ReplayMissionWorldSetup(vehicle);

        Assert.IsTrue(
            _sent.OfType<CreateCreaturePacket>().Any(p => p.CBID == DeliverCbid),
            "Deliver-phase pad NPC must CreateCreature so client object table has the TFID. Got: "
            + string.Join(", ", _sent.Select(p => p.GetType().Name)));
    }

    [TestMethod]
    public void EnsureDeliverTurnInNpc_SecondCall_IsNoOpNoPackets()
    {
        // AutoPatrol is client-spammed every tick in the pad volume — must not re-Create.
        SeedDeliverMission();
        var (character, vehicle, map) = CreatePlayer();
        map.MapData.Templates[PadSpawnCoid] = MakePadSpawnTemplate();
        PlaceCreateReaction(map, CreatePadRx, PadSpawnCoid);
        PlacePadMarker(map);
        character.CurrentQuests.Add(MakeQuest());
        character.SetMap(map);
        vehicle.SetMap(map);

        map.EnsureDeliverTurnInNpc(vehicle, DeliverCbid);
        Assert.IsTrue(character.MapPresence.IsDeliverTurnInReady(DeliverCbid)
            || map.MapHasPresentEntityWithCbidForTests(character, DeliverCbid));

        _sent.Clear();
        for (var i = 0; i < 20; i++)
            map.EnsureDeliverTurnInNpc(vehicle, DeliverCbid);

        Assert.AreEqual(0, _sent.OfType<CreateCreaturePacket>().Count(),
            "Repeated Ensure must not spam CreateCreature");
        Assert.AreEqual(0, _sent.OfType<GroupReactionCallPacket>().Count(),
            "Repeated Ensure must not spam GroupReactionCall Create");
    }

    [TestMethod]
    public void PerformScopeQuery_GlobalStandingNpc_SendsCreateCreature()
    {
        var map = CreateScopeTestMap();
        var creature = new Creature { Position = new Vector3(25f, 0f, 0f), Level = 5 };
        var counter = map.LocalCoidCounter;
        SpawnPoint.AssignMapNpcIdentity(creature, ref counter);
        map.LocalCoidCounter = counter;
        creature.LoadCloneBase(DeliverCbid);
        creature.SetupCBFields();
        creature.IsMissionGiver = true;
        creature.CreateGhost();
        creature.SetMap(map);

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, self, connection);

        Assert.IsTrue(
            packets.OfType<CreateCreaturePacket>().Any(p => p.CBID == DeliverCbid),
            "Global standing/mission NPC must CreateCreature on scope (not ghost-only)");
    }

    private static SectorMap CreateScopeTestMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId + 1,
            MapFileName = $"tm_scope_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
            IsTown = false,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }

        return map;
    }

    private void SeedDeliverMission()
    {
        var obj = MissionObjective.CreateForTests(98201, 0, MissionId, 1);
        obj.Requirements.Add(new ObjectiveRequirementDeliver(obj)
        {
            NPCTargetCBID = DeliverCbid,
            NPCTargetCompletes = true,
        });
        AssetManager.Instance.SetTestMission(Mission.CreateForTests(MissionId, obj));
        NpcInteractHandler.InvalidateMissionIndex();
    }

    private static CharacterQuest MakeQuest()
    {
        var q = new CharacterQuest(MissionId, 0);
        q.PopulateFromAssets();
        return q;
    }

    private static SpawnPointTemplate MakePadSpawnTemplate()
    {
        var tpl = new SpawnPointTemplate
        {
            COID = (int)PadSpawnCoid,
            IsActive = false,
            OriginalIsActive = false,
            CBID = 0,
            Location = new Vector4(10, 0, 10, 0),
        };
        tpl.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = DeliverCbid,
            IsTemplate = false,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        return tpl;
    }

    private static void PlaceCreateReaction(SectorMap map, long rxCoid, long targetSpawn)
    {
        var tpl = new ReactionTemplate
        {
            COID = (int)rxCoid,
            ReactionType = ReactionType.Create,
        };
        tpl.Objects.Add(targetSpawn);
        var rx = new Reaction(tpl);
        rx.SetCoid(rxCoid, false);
        rx.SetMap(map);
    }

    private static void PlacePadMarker(SectorMap map)
    {
        var tpl = (SpawnPointTemplate)map.MapData.Templates[PadSpawnCoid];
        var spawn = new SpawnPoint(tpl) { Position = new Vector3(10, 0, 10) };
        spawn.SetCoid(PadSpawnCoid, false);
        spawn.SetMap(map);
    }

    private (Character Character, Vehicle Vehicle, SectorMap Map) CreatePlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_deliver_{ContId}",
            DisplayName = "test",
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4());
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.ActivateGhosting();

        var character = new Character();
        character.SetCoid(600, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle { Position = new Vector3(10, 0, 10) };
        vehicle.SetCoid(601, true);
        character.SetCurrentVehicleForTests(vehicle);
        return (character, vehicle, map);
    }
}

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Ground loot must obey interest management like everything else on the map.
///
/// Regression: <c>LootManager.BroadcastPacketToMap</c> sent every ground-loot
/// <c>CreateSimpleObject</c> to every player on the map with no distance check at all, so players
/// watched items appear across the entire continent from fights they were nowhere near. The map
/// already runs an interest query (<see cref="InterestSelector"/>) for ghosted entities; ground
/// loot carries no ghost, so it needs the same radius policy applied on its own create path —
/// including a catch-up delivery when a player later walks into range, because the spawn-time
/// create is otherwise the only one ever sent.
/// </summary>
[TestClass]
public class LootScopeDeliveryTests
{
    private const int LootCbid = 7750;

    private readonly List<(TNLConnection Connection, BasePacket Packet)> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (c, p) => _sent.Add((c, p));
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManagerTestHelper.RegisterCloneBase(LootCbid, CloneBaseObjectType.Item);
        LootManager.Instance.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
    }

    [TestMethod]
    public void SpawnLoot_SendsCreateOnlyToPlayersInRange()
    {
        var map = CreateMap(11000);
        var near = CreateCharacterOnMap(map, 6100, new Vector3(10f, 0f, 10f));
        var far = CreateCharacterOnMap(map, 6200, new Vector3(5000f, 0f, 5000f));
        near.OwningConnection.BeginGhostingForTests();
        far.OwningConnection.BeginGhostingForTests();

        LootManager.Instance.SpawnLootItem(
            LootCbid, new Vector3(0f, 0f, 0f), Quaternion.Default, map);

        Assert.AreEqual(1, CreatesFor(near), "a player standing next to the drop must see it");
        Assert.AreEqual(
            0,
            CreatesFor(far),
            "a player on the far side of the map must not be told about this drop");
    }

    [TestMethod]
    public void PlayerApproachingLater_ReceivesGroundLootCreateOnce()
    {
        var map = CreateMap(11100);
        var player = CreateCharacterOnMap(map, 6300, new Vector3(5000f, 0f, 5000f));
        var connection = player.OwningConnection;
        connection.BeginGhostingForTests();

        LootManager.Instance.SpawnLootItem(
            LootCbid, new Vector3(0f, 0f, 0f), Quaternion.Default, map);
        Assert.AreEqual(0, CreatesFor(player), "out of range at spawn time");

        // Player drives over to the drop.
        MovePlayer(player, new Vector3(5f, 0f, 5f));
        map.PerformScopeQuery(null, player, connection);

        Assert.AreEqual(
            1,
            CreatesFor(player),
            "walking into range must deliver the create — the spawn-time send is the only other one");

        // Steady state: the scope query runs every ~100ms and must not re-send.
        for (var i = 0; i < 20; i++)
            map.PerformScopeQuery(null, player, connection);

        Assert.AreEqual(
            1,
            CreatesFor(player),
            "ground loot already delivered to this player must not be re-created every query");
    }

    [TestMethod]
    public void ScopeQuery_DoesNotDeliverGroundLootStillOutOfRange()
    {
        var map = CreateMap(11200);
        var player = CreateCharacterOnMap(map, 6400, new Vector3(5000f, 0f, 5000f));
        var connection = player.OwningConnection;
        connection.BeginGhostingForTests();

        LootManager.Instance.SpawnLootItem(
            LootCbid, new Vector3(0f, 0f, 0f), Quaternion.Default, map);

        for (var i = 0; i < 20; i++)
            map.PerformScopeQuery(null, player, connection);

        Assert.AreEqual(0, CreatesFor(player), "distance gate must hold across repeated queries");
    }

    [TestMethod]
    public void NearbyPlayerStillLoadingTheMap_GetsCreateOnceGhostingIsLive()
    {
        var map = CreateMap(11300);
        var player = CreateCharacterOnMap(map, 6500, new Vector3(10f, 0f, 10f));

        // Ghosting not started yet: the client is still loading the map.
        LootManager.Instance.SpawnLootItem(
            LootCbid, new Vector3(0f, 0f, 0f), Quaternion.Default, map);
        Assert.AreEqual(0, CreatesFor(player), "nothing can reach a client that is not ghosting");

        player.OwningConnection.BeginGhostingForTests();
        map.PerformScopeQuery(null, player, player.OwningConnection);

        Assert.AreEqual(
            1,
            CreatesFor(player),
            "the drop must not be lost just because it landed during the client's map load");
    }

    // ----------------------------------------------------------------- helpers

    private int CreatesFor(Character character) => _sent.Count(e =>
        ReferenceEquals(e.Connection, character.OwningConnection)
        && e.Packet is CreateSimpleObjectPacket { IsInInventory: false } p
        && p.CBID == LootCbid);

    private static void MovePlayer(Character character, Vector3 position)
    {
        character.Position = position;
        if (character.CurrentVehicle != null)
            character.CurrentVehicle.Position = position;
        // Grid buckets are keyed by position; re-bucket so the radius query sees the new spot.
        character.Map?.Grid.RebucketSweep();
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(localCoid % 10000),
            MapFileName = $"tm_loot_scope_{localCoid}",
            DisplayName = "lootscope",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        map.LocalCoidCounter = localCoid;

        // CreateForTests builds via GetUninitializedObject, which skips field initializers.
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }

        return map;
    }

    private static Character CreateCharacterOnMap(SectorMap map, long characterCoid, Vector3 position)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character { Position = position };
        character.SetCoid(characterCoid, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var inventory = new InventoryManager();
        character.AttachInventoryForTests(inventory);

        var vehicle = new Vehicle { Position = position };
        vehicle.SetCoid(characterCoid + 1, true);
        character.AttachCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        return character;
    }
}

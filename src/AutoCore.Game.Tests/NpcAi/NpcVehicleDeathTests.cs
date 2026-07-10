using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using System.Linq;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Stage 10: a dead NPC vehicle rolls loot from its <c>tVehicleTemplate</c> (LootTableId /
/// LootChance / LootRolls), leaves the sector map, and broadcasts a destroy so clients remove
/// the wreck — mirroring <see cref="Creature.OnDeath"/> but sourced from the vehicle template.
/// </summary>
[TestClass]
public class NpcVehicleDeathTests
{
    private const int ContId = 851;
    private const int TemplateId = 6100;
    private const int LootTableId = 6200;
    private const int LootItemCbid = 6300;
    private const long VehicleCoid = 851_001;

    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        AssetManager.Instance.ClearTestNpcData();
        LootManager.Instance.ResetForTests();
    }

    [TestMethod]
    public void NpcVehicleDeath_RollsTemplateLoot_RemovesFromMap_Broadcasts()
    {
        var map = CreateFieldMapWithPlayer();

        // Deterministic single-item loot table: only Item type, only rarity 0, at the NPC's level.
        AssetManagerTestHelper.RegisterCloneBase(LootItemCbid, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, rarity: 0, cbid: LootItemCbid, requiredLevel: 1);
        AssetManager.Instance.SetTestLootTables(new[]
        {
            new LootTable { Id = LootTableId, ChanceOther = 1, ChanceRarity0 = 1, DropLevelOffset = 0f, MaxLevelOffset = 0 },
        });
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate { Id = TemplateId, LootTableId = LootTableId, LootChance = 255, LootRolls = 1 },
        });

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.Position = new Vector3(15f, 0f, 15f);
        vehicle.TemplateId = TemplateId;
        vehicle.NpcAi = new NpcAiState();
        vehicle.InitializeHealthForTests(50);
        vehicle.SetMap(map);

        vehicle.OnDeath(DeathType.Silent);

        Assert.IsTrue(vehicle.IsCorpse, "death must mark the vehicle a corpse");
        Assert.IsNull(map.GetObjectByCoid(VehicleCoid), "dead NPC vehicle must leave the sector map");
        Assert.IsTrue(
            _sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == VehicleCoid),
            "clients need a DestroyObject broadcast so the wreck disappears");
        Assert.IsTrue(
            _sent.OfType<CreateSimpleObjectPacket>().Any(p => p.CBID == LootItemCbid),
            "template loot must be rolled and spawned; sent=" + string.Join(',', _sent.Select(p => p.Opcode)));
    }

    [TestMethod]
    public void NpcVehicleDeath_BroadcastSendFailure_DoesNotThrow()
    {
        var map = CreateFieldMapWithPlayer();

        var vehicle = new Vehicle();
        vehicle.SetCoid(VehicleCoid, true);
        vehicle.Position = new Vector3(15f, 0f, 15f);
        vehicle.TemplateId = -1; // no template loot; isolate the destroy broadcast
        vehicle.NpcAi = new NpcAiState();
        vehicle.InitializeHealthForTests(50);
        vehicle.SetMap(map);

        try
        {
            // Route the destroy broadcast through GraphicsObject.BroadcastDestroy's forced-failure
            // path; a single failed send must not abort death handling.
            GraphicsObject.ForceNetworkHelperFailureForTests = true;
            vehicle.OnDeath(DeathType.Silent);
        }
        finally
        {
            GraphicsObject.ForceNetworkHelperFailureForTests = false;
        }

        Assert.IsTrue(vehicle.IsCorpse, "death must still mark the vehicle a corpse despite send failure");
        Assert.IsNull(map.GetObjectByCoid(VehicleCoid), "dead NPC vehicle must still leave the sector map");
    }

    private static SectorMap CreateFieldMapWithPlayer()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_death_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));

        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(851_900, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;
        character.SetMap(map);

        return map;
    }
}

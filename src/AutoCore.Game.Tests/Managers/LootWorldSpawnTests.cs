using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// World ground-loot contract: no GhostObject, CreateSimpleObject wire shape, random Item picks.
/// </summary>
[TestClass]
public class LootWorldSpawnTests
{
    [TestCleanup]
    public void Cleanup()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
    }

    [TestMethod]
    public void TrySpawnLootItem_DoesNotCreateGhost()
    {
        AssetManagerTestHelper.RegisterCloneBase(5010, CloneBaseObjectType.Item);
        var map = CreateMap(localCoid: 2000);

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            5010,
            new Vector3(10, 1, 20),
            Quaternion.Default,
            map,
            out var coid));

        var item = map.GetObjectByCoid(coid);
        Assert.IsNotNull(item);
        Assert.IsNull(item.Ghost, "World loot must not create a plain GhostObject (AV 0x005B0EFF).");
        Assert.IsFalse(item.ObjectId.Global);
    }

    [TestMethod]
    public void TrySpawnLootItem_Armor_UsesCreateSimpleObjectNotCreateArmor()
    {
        AssetManagerTestHelper.RegisterArmorCloneBase(5020);
        var so = (SimpleObject)ClonedObjectBase.AllocateNewObjectFromCBID(5020)!;
        so.SetCoid(99, false);
        so.LoadCloneBase(5020);

        var packet = LootManager.BuildGroundLootCreatePacket(so);

        Assert.IsNotNull(packet);
        Assert.AreEqual(GameOpcode.CreateSimpleObject, packet.Opcode);
        Assert.IsFalse(packet is CreateArmorPacket);
        Assert.IsFalse(packet.IsBound);
        Assert.IsFalse(packet.IsInInventory);
        Assert.IsTrue(packet.IsIdentified);
        Assert.AreEqual(5020, packet.CBID);
        Assert.AreEqual(99L, packet.ObjectId.Coid);
    }

    [TestMethod]
    public void TryPickRandomGroundLootCbid_OnlyReturnsNonAutoLootTypes()
    {
        // Item = ground-pickable; Armor requires auto-loot / not for /loot rand
        AssetManagerTestHelper.RegisterCloneBase(6001, CloneBaseObjectType.Item);
        SetGeneratable(6001, 1);
        AssetManagerTestHelper.RegisterArmorCloneBase(6002);
        SetGeneratable(6002, 1);

        LootManager.Instance.ResetForTests();
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, rarity: 0, cbid: 6001, requiredLevel: 1);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Armor, rarity: 0, cbid: 6002, requiredLevel: 1);

        for (var i = 0; i < 20; i++)
        {
            Assert.IsTrue(LootManager.Instance.TryPickRandomGroundLootCbid(out var cbid));
            Assert.AreEqual(6001, cbid);
            Assert.IsFalse(LootManager.Instance.RequiresAutoLoot(cbid));
        }
    }

    [TestMethod]
    public void TryPickRandomGroundLootCbid_FailsWhenNoGroundItems()
    {
        LootManager.Instance.ResetForTests();
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Armor, rarity: 0, cbid: 7001, requiredLevel: 1);

        Assert.IsFalse(LootManager.Instance.TryPickRandomGroundLootCbid(out _));
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = 8801,
            MapFileName = "tm_loot_test",
            DisplayName = "loot",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        map.LocalCoidCounter = localCoid;
        return map;
    }

    private static void SetGeneratable(int cbid, uint value)
    {
        var cb = AssetManager.Instance.GetCloneBase(cbid);
        Assert.IsNotNull(cb);
        var specific = cb.CloneBaseSpecific;
        specific.IsGeneratable = value;
        cb.CloneBaseSpecific = specific;
    }
}

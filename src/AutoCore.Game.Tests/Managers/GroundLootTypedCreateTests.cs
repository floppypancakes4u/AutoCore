using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AutoCore.Database.World.Models;

namespace AutoCore.Game.Tests.Managers;

/// <summary>
/// Ground loot must use the same typed create opcodes as inventory items.
///
/// Client ground truth (autoassault.exe): <c>SMSG_Sector_CreateArmor</c> is 344 bytes — the first
/// 216 are byte-identical to <c>SMSG_Sector_CreateSimpleObject</c>, followed by
/// <c>sArmorData</c> (216), <c>fMass</c> (236) and <c>char strName[100]</c> (240).
/// <c>Process_EMSG_Sector_CreateArmor</c> @0x00812320 funnels into the same
/// <c>ProcessSectorCreate</c>, which allocates the concrete class from the CBID — so an Armor CBID
/// always becomes a CVOGArmor regardless of which opcode carried it.
///
/// Sending the plain 216-byte SimpleObject payload for an Armor CBID therefore leaves
/// <c>strName</c> untransmitted, and <c>CVOGClonedObjectBase::AddItemText</c> @0x005149D0 — which
/// draws the floating item name over ground loot — renders that uninitialized buffer as garbage
/// ("=-1jh^7&amp; fj/;"). Pickup looked correct because the inventory UI resolves the name from the
/// clonebase by CBID instead.
///
/// <see cref="AutoCore.Game.Inventory.InventoryItemCreator.CreatePacketFor"/> already documents
/// this ("a plain CreateSimpleObject for a weapon CBID makes the client mis-parse the object");
/// ground loot was the remaining path that ignored it. Pickability is unaffected:
/// <c>IsPickupable</c> @0x005130E0 switches on the clonebase type, not the opcode.
/// </summary>
[TestClass]
public class GroundLootTypedCreateTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
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
    public void ArmorGroundLoot_UsesTypedCreateArmorOpcode()
    {
        const int armorCbid = 7810;
        AssetManagerTestHelper.RegisterArmorCloneBase(armorCbid);

        var map = CreateMap(13000);
        var player = CreateCharacterOnMap(map, 6700, new Vector3(0f, 0f, 0f));
        player.OwningConnection.BeginGhostingForTests();

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            armorCbid, new Vector3(1f, 0f, 1f), Quaternion.Default, map, out _));

        var create = _sent.OfType<CreateSimpleObjectPacket>().SingleOrDefault(p => p.CBID == armorCbid);
        Assert.IsNotNull(create, "armor ground loot must be created on the client");
        Assert.AreEqual(
            GameOpcode.CreateArmor,
            create.Opcode,
            "a plain CreateSimpleObject leaves strName[100] untransmitted, so the floating item "
            + "name renders uninitialized memory");
        Assert.IsInstanceOfType(create, typeof(CreateArmorPacket));
        Assert.IsNotNull(
            ((CreateArmorPacket)create).Name,
            "the typed name field must be written (empty is fine — the client falls back to the "
            + "clonebase name); leaving it unsent is what produced garbage text");
    }

    [TestMethod]
    public void PlainItemGroundLoot_StaysSimpleObject()
    {
        const int itemCbid = 7820;
        AssetManagerTestHelper.RegisterCloneBase(itemCbid, CloneBaseObjectType.Item);

        var map = CreateMap(13100);
        var player = CreateCharacterOnMap(map, 6800, new Vector3(0f, 0f, 0f));
        player.OwningConnection.BeginGhostingForTests();

        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            itemCbid, new Vector3(1f, 0f, 1f), Quaternion.Default, map, out _));

        var create = _sent.OfType<CreateSimpleObjectPacket>().SingleOrDefault(p => p.CBID == itemCbid);
        Assert.IsNotNull(create);
        Assert.AreEqual(
            GameOpcode.CreateSimpleObject,
            create.Opcode,
            "a plain Item really is a simple object; only typed gear carries trailing stats");
    }

    [TestMethod]
    public void ArmorGroundLootPacket_SerializesToTypedLength()
    {
        const int armorCbid = 7830;
        AssetManagerTestHelper.RegisterArmorCloneBase(armorCbid);

        var map = CreateMap(13200);
        Assert.IsTrue(LootManager.Instance.TrySpawnLootItem(
            armorCbid, new Vector3(1f, 0f, 1f), Quaternion.Default, map, out var coid));

        var loot = (SimpleObject)map.GetObjectByCoid(coid);
        var packet = LootManager.BuildGroundLootCreatePacket(loot);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        packet.Write(writer);

        // Client SMSG_Sector_CreateArmor (344 bytes) minus its 4-byte leading _padding_ gives a
        // 340-byte body, whose final 2 bytes are trailing alignment the server never materializes:
        //   base 212 (208 written + 4 alignment, which materializes once the armor block follows)
        // + sArmorData 20 + fMass 4 + strName 100 + iVarianceDefensiveBonus 2 = 338.
        // The plain SimpleObject body is 208, so a typed armor create carries 130 more bytes —
        // including the 100-byte strName the client was previously reading as uninitialized memory.
        Assert.AreEqual(
            338,
            stream.Length,
            "the armor create body must match the client's SMSG_Sector_CreateArmor layout");
    }

    private static SectorMap CreateMap(long localCoid)
    {
        var continent = new ContinentObject
        {
            Id = (int)(localCoid % 10000),
            MapFileName = $"tm_loot_typed_{localCoid}",
            DisplayName = "loottyped",
            IsTown = false,
            IsPersistent = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        map.LocalCoidCounter = localCoid;
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

        var vehicle = new Vehicle { Position = position };
        vehicle.SetCoid(characterCoid + 1, true);
        character.AttachCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        return character;
    }
}

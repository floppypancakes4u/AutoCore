using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Inventory;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

/// <summary>
/// Sector packet handlers on <see cref="TNLConnection"/> via reflection +
/// <see cref="TNLConnection.TestPacketSink"/> (no live UDP).
/// </summary>
[TestClass]
public class TNLConnectionSectorHandlerTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void Init()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
    }

    [TestCleanup]
    public void Cleanup()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
    }

    private static TNLConnection CreateClient(Character character = null)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);
        connection.SetNetAddress(new IPEndPoint(IPAddress.Loopback, 0));
        connection.SetInterface(new TNLInterface(doGhosting: false, skipNetworkBind: true));
        if (character != null)
        {
            connection.CurrentCharacter = character;
            character.SetOwningConnection(connection);
        }
        return connection;
    }

    private static void InvokeHandler(TNLConnection connection, string methodName, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Missing handler {methodName}");
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        method.Invoke(connection, new object[] { reader });
    }

    private static void InvokeHandlerWithOpcode(TNLConnection connection, string methodName, GameOpcode opcode, byte[] body)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((uint)opcode);
            writer.Write(body);
            writer.Flush();
        }

        stream.Position = sizeof(uint);
        var method = typeof(TNLConnection).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Missing handler {methodName}");
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        method.Invoke(connection, new object[] { reader });
    }

    private static Character CreateCharacterOnMap(out SectorMap map, long coid = 8001)
    {
        var continent = new ContinentObject
        {
            Id = 88,
            MapFileName = "tm_sector_handler",
            DisplayName = "sector-handler",
            IsTown = false,
            IsPersistent = true,
        };
        map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        map.LocalCoidCounter = 9000;

        var character = new Character();
        character.SetCoid(coid, true);
        var inventory = new InventoryManager();
        character.AttachInventoryForTests(inventory);
        var vehicle = new Vehicle();
        vehicle.SetCoid(coid + 1, true);
        vehicle.Position = new Vector3(0, 0, 0);
        character.AttachCurrentVehicleForTests(vehicle);
        character.SetMap(map);
        vehicle.SetMap(map);
        return character;
    }

    private static CloneBaseObject MakeCharacterCloneBase()
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific
        {
            CloneBaseId = 1,
            Type = (int)CloneBaseObjectType.Character,
            BaseValue = 0,
        };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MaxHitPoint = 100,
            MaxUses = 0,
        };
        return clone;
    }

    // --- Skill / attribute / quickbar / cast ---

    [TestMethod]
    public void HandleSkillIncrement_ShortBody_DoesNotThrowOrSend()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);

        InvokeHandler(client, "HandleSkillIncrementPacket", new byte[] { 0x01, 0x02 });

        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleSkillIncrement_UnknownSkill_DoesNotSendLevelPacket()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);

        // 4-byte skill id at end of body
        InvokeHandler(client, "HandleSkillIncrementPacket", BitConverter.GetBytes(999001));

        Assert.IsFalse(_sent.OfType<CharacterLevelPacket>().Any());
    }

    [TestMethod]
    public void HandleAttributeIncrement_NullCharacter_NoSend()
    {
        var client = CreateClient();
        InvokeHandler(client, "HandleAttributeIncrementPacket", BitConverter.GetBytes(1u));
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleAttributeIncrement_RejectedMask_NoSend()
    {
        var character = CreateCharacterOnMap(out _);
        character.SetAttributePoints(0);
        var client = CreateClient(character);

        InvokeHandler(client, "HandleAttributeIncrementPacket", BitConverter.GetBytes(1u));

        Assert.IsFalse(_sent.OfType<CharacterLevelPacket>().Any());
    }

    [TestMethod]
    public void HandleRequestCastSkill_NullCharacter_NoResponse()
    {
        var client = CreateClient();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0);
            writer.Write(1L);
            writer.Write(true);
            writer.Write(new byte[7]);
            writer.Write(2103);
            writer.Write(1f);
            writer.Write(2f);
            writer.Write(3f);
        }

        InvokeHandler(client, "HandleRequestCastSkillPacket", stream.ToArray());
        Assert.IsFalse(_sent.OfType<SkillStatusEffectPacket>().Any());
    }

    [TestMethod]
    public void HandleQuickBarUpdate_NullCharacter_NoThrow()
    {
        var client = CreateClient();
        // slot + isItem + value (long) layout used by QuickBarUpdatePacket
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); // pad-ish
            writer.Write((byte)0);   // slot
            writer.Write((byte)0);   // isItem
            writer.Write(0L);        // value
            writer.Write(0);         // trailing
        }

        InvokeHandler(client, "HandleQuickBarUpdatePacket", stream.ToArray());
        Assert.AreEqual(0, _sent.Count);
    }

    // --- Transfer stage 2 ---

    [TestMethod]
    public void HandleTransferFromGlobalStage2_UnknownCharacter_Disconnects()
    {
        var client = CreateClient();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0xABCDu);
            writer.Write(999_999_999L);
        }

        InvokeHandler(client, "HandleTransferFromGlobalStage2Packet", stream.ToArray());
        // Disconnect path; no TransferFromGlobalStage3Packet
        Assert.IsFalse(_sent.OfType<TransferFromGlobalStage3Packet>().Any());
    }

    // --- Store / mission / patrol soft fails ---

    [TestMethod]
    public void HandleStoreClose_NullCharacter_DoesNotThrow()
    {
        var client = CreateClient();
        InvokeHandler(client, "HandleStoreClosePacket", Array.Empty<byte>());
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleStoreClose_WithCharacter_ClearsSessionQuietly()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleStoreClosePacket", new byte[] { 0x01 });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleUseObject_ShortBody_ParseFail_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleUseObjectPacket", new byte[] { 0x01 });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleStoreTransaction_ShortBody_DoesNotThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        // Short body is accepted as soft parse; VendorStoreService may reject without session.
        InvokeHandler(client, "HandleStoreTransactionRequestPacket", new byte[] { 0x01 });
    }

    [TestMethod]
    public void HandleAutoPatrol_ShortBody_ParseFail_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleAutoPatrolPacket", new byte[] { 0x01 });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleFailMission_ShortBody_ParseFail_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleFailMissionPacket", new byte[] { 0x01 });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleMissionDialogResponse_ShortBody_ParseFail_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleMissionDialogResponse", new byte[] { 0x01 });
        Assert.AreEqual(0, _sent.Count);
    }

    // --- RequestObject ---

    [TestMethod]
    public void HandleRequestObject_NullCharacter_NoSend()
    {
        var client = CreateClient();
        InvokeHandler(client, "HandleRequestObjectPacket", BuildRequestObjectBody(1, 42, true));
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleRequestObject_MissingObject_NoSend()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleRequestObjectPacket", BuildRequestObjectBody(1, 424242, true));
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleRequestObject_Graphics_ResendsCreateSimpleObject()
    {
        var character = CreateCharacterOnMap(out var map);
        var client = CreateClient(character);

        var graphics = new GraphicsObject(GraphicsObjectType.Graphics);
        graphics.SetCoid(9101, false);
        graphics.Position = new Vector3(1, 0, 1);
        graphics.SetMap(map);

        InvokeHandler(client, "HandleRequestObjectPacket", BuildRequestObjectBody(1, 9101, false));

        Assert.IsTrue(
            _sent.OfType<CreateSimpleObjectPacket>().Any(),
            "GraphicsObject path should resend CreateSimpleObject");
    }

    [TestMethod]
    public void HandleRequestObject_Creature_WithoutCloneBase_SwallowsWriteError()
    {
        var character = CreateCharacterOnMap(out var map);
        var client = CreateClient(character);

        var creature = new Creature();
        creature.SetCoid(9102, true);
        creature.Position = new Vector3(2, 0, 2);
        creature.SetMap(map);

        // WriteToPacket throws without clonebase; ResendObjectCreate catches and logs.
        InvokeHandler(client, "HandleRequestObjectPacket", BuildRequestObjectBody(1, 9102, true));
        Assert.IsFalse(_sent.OfType<CreateCreaturePacket>().Any());
    }

    [TestMethod]
    public void HandleRequestObject_ZeroCoid_Skipped()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleRequestObjectPacket", BuildRequestObjectBody(1, 0, true));
        Assert.AreEqual(0, _sent.Count);
    }

    /// <summary>
    /// PDB Pass 7. Client <c>FUN_008078B0</c> RequestObject recovery for a character
    /// ghost applies <c>Client_RecvCreateCharacter</c> (<c>0x2015</c>, size <c>0x1A8</c>).
    /// <see cref="Character"/> is a <see cref="Creature"/> subclass, so a Creature-first
    /// switch sends <c>0x2013</c> and the character createFromPacket reads past the
    /// creature tail.
    /// </summary>
    [TestMethod]
    public void HandleRequestObject_Character_ResendsCreateCharacter_NotCreateCreature()
    {
        var viewer = CreateCharacterOnMap(out var map, coid: 8001);
        var client = CreateClient(viewer);

        var other = new Character();
        other.SetCoid(9201, true);
        other.AttachTestDataForTests("OtherPilot");
        other.AssignCloneBaseForTests(MakeCharacterCloneBase());
        other.Position = new Vector3(3, 0, 3);
        other.SetMap(map);

        InvokeHandler(client, "HandleRequestObjectPacket", BuildRequestObjectBody(1, 9201, true));

        Assert.IsFalse(_sent.OfType<CreateCreaturePacket>().Any(),
            "Character RequestObject must not use CreateCreature 0x2013.");
        var packet = _sent.OfType<CreateCharacterPacket>().SingleOrDefault();
        Assert.IsNotNull(packet, "FUN_008078B0 character recovery expects 0x2015.");
        Assert.IsFalse(packet is CreateCharacterExtendedPacket,
            "RequestObject recovery is base CreateCharacter, not Extended.");
        Assert.AreEqual(GameOpcode.CreateCharacter, packet.Opcode);
        Assert.AreEqual(9201L, packet.ObjectId.Coid);
        Assert.AreEqual("OtherPilot", packet.Name);
    }

    private static byte[] BuildRequestObjectBody(byte count, long coid, bool global)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(count);
        writer.Write(new byte[3]); // pad
        writer.WriteTFID(coid, global);
        writer.Flush();
        return stream.ToArray();
    }

    // --- Firing ---

    [TestMethod]
    public void HandleFiring_NoVehicle_NoThrow()
    {
        var client = CreateClient();
        InvokeHandler(client, "HandleFiringPacket", new byte[] { 1 });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleFiring_SetsFiringFlag()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);

        InvokeHandler(client, "HandleFiringPacket", new byte[] { 3 });

        Assert.AreEqual(3, character.CurrentVehicle.Firing);
    }

    [TestMethod]
    public void HandleFiring_WithTarget_SetsTargetObject()
    {
        var character = CreateCharacterOnMap(out var map);
        var client = CreateClient(character);

        var target = new Creature();
        target.SetCoid(9200, true);
        target.SetMap(map);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)1);
            writer.Write((ushort)0);
            writer.WriteTFID(9200, true);
        }

        InvokeHandler(client, "HandleFiringPacket", stream.ToArray());
        Assert.AreEqual(1, character.CurrentVehicle.Firing);
        Assert.IsNotNull(character.CurrentVehicle.Target);
        Assert.AreEqual(9200L, character.CurrentVehicle.Target.ObjectId.Coid);
    }

    [TestMethod]
    public void HandleFiring_EmptyBody_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleFiringPacket", Array.Empty<byte>());
        Assert.AreEqual(0, character.CurrentVehicle.Firing);
    }

    // --- Item pickup ---

    [TestMethod]
    public void HandleItemPickup_NullCharacter_NoThrow()
    {
        var client = CreateClient();
        InvokeHandler(client, "HandleItemPickupPacket", BuildItemPickupBody(1));
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleItemPickup_ItemNotOnMap_NoSend()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandler(client, "HandleItemPickupPacket", BuildItemPickupBody(55555));
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandleItemPickup_CharacterNotSimplePickup_StillSimpleObjectSubclass()
    {
        // Character : Creature : SimpleObject — pickup path treats it as SimpleObject but
        // distance/claim should fail without a real ground item clonebase. Just ensure no throw.
        var character = CreateCharacterOnMap(out var map);
        var client = CreateClient(character);
        character.AttachTestDataForTests("pickup-char");

        InvokeHandler(client, "HandleItemPickupPacket", BuildItemPickupBody(character.ObjectId.Coid));
        // Character is on map as SimpleObject-derived; may attempt claim and fail.
    }

    [TestMethod]
    public void HandleItemPickup_TooFar_NoSend()
    {
        var character = CreateCharacterOnMap(out var map);
        var client = CreateClient(character);
        character.CurrentVehicle.Position = new Vector3(0, 0, 0);

        var item = new SimpleObject(GraphicsObjectType.Graphics);
        item.SetCoid(9302, false);
        item.Position = new Vector3(100, 0, 100); // far
        item.SetMap(map);

        InvokeHandler(client, "HandleItemPickupPacket", BuildItemPickupBody(9302));
        Assert.AreEqual(0, _sent.Count);
        Assert.IsNotNull(map.GetObjectByCoid(9302));
    }

    [TestMethod]
    public void HandleItemPickup_CargoFull_DoesNotAllocatePlaceholderCoid()
    {
        // SS-31 leak guard: cargo has no free 1x1 slot and the picked-up cbid is non-stackable
        // (default footprint), so PickupWorldItem's claim would fail. The guard must reject
        // before allocating the persistent coid, or a placeholder simple_object row leaks for
        // an item that is never actually placed in cargo.
        const int cbid = 8620;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        try
        {
            AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Item);

            var character = CreateCharacterOnMap(out var map);
            character.AttachTestDataForTests("pickup-char");
            character.Inventory.SetCapacity(1, 1);
            character.Inventory.TryAdd(new CharacterInventoryItem(99, CloneBaseObjectType.Item, "Filler", 5000, 0, 0, 1));
            var client = CreateClient(character);

            var item = new SimpleObject(GraphicsObjectType.Graphics);
            item.SetCoid(9410, false);
            item.LoadCloneBase(cbid);
            item.Position = new Vector3(0, 0, 0);
            item.SetMap(map);

            var saved = InventoryRuntime.AllocatePersistentCoid;
            var allocations = 0;
            try
            {
                InventoryRuntime.AllocatePersistentCoid = () => { allocations++; return 9500 + allocations; };
                InvokeHandler(client, "HandleItemPickupPacket", BuildItemPickupBody(9410));
            }
            finally
            {
                InventoryRuntime.AllocatePersistentCoid = saved;
            }

            Assert.AreEqual(0, allocations, "cargo-full pickup must not allocate a persistent coid for an item that cannot be placed (SS-31 leak guard)");
            Assert.AreEqual(0, _sent.Count, "no packets should be sent for a rejected pickup");
            Assert.IsNotNull(map.GetObjectByCoid(9410), "item stays on the map when the pickup is rejected");
        }
        finally
        {
            AssetManagerTestHelper.ClearRegisteredCloneBases();
        }
    }

    private static byte[] BuildItemPickupBody(long coid)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0); // unknown
        writer.WriteTFID(coid, false);
        writer.Flush();
        return stream.ToArray();
    }

    // --- Inventory handlers (null character → failure packets) ---

    [TestMethod]
    public void HandleItemDrop_NullCharacter_SendsFailure()
    {
        var client = CreateClient();
        InvokeHandlerWithOpcode(client, "HandleItemDropPacket", GameOpcode.ItemDrop, new byte[0x2C]);
        Assert.IsTrue(_sent.Count >= 1);
    }

    [TestMethod]
    public void HandleInventoryGrab_NullCharacter_SendsFailure()
    {
        var client = CreateClient();
        InvokeHandlerWithOpcode(client, "HandleInventoryGrabPacket", GameOpcode.InventoryGrab, new byte[0x30]);
        Assert.IsTrue(_sent.Count >= 1);
    }

    [TestMethod]
    public void HandleInventoryDrop_NullCharacter_SendsFailure()
    {
        var client = CreateClient();
        InvokeHandlerWithOpcode(client, "HandleInventoryDropPacket", GameOpcode.InventoryDrop, new byte[0x30]);
        Assert.IsTrue(_sent.Count >= 1);
    }

    [TestMethod]
    public void HandleInventoryGrabMM_NullCharacter_SendsFailure()
    {
        var client = CreateClient();
        InvokeHandlerWithOpcode(client, "HandleInventoryGrabMMPacket", GameOpcode.InventoryGrabMM, new byte[0x30]);
        Assert.IsTrue(_sent.Count >= 1);
    }

    [TestMethod]
    public void HandleInventoryDropMM_NullCharacter_SendsFailure()
    {
        var client = CreateClient();
        InvokeHandlerWithOpcode(client, "HandleInventoryDropMMPacket", GameOpcode.InventoryDropMM, new byte[0x30]);
        Assert.IsTrue(_sent.Count >= 1);
    }

    [TestMethod]
    public void HandleInventoryDestroyItem_LogOnlyStub_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandlerWithOpcode(client, "HandleInventoryDestroyItemPacket", GameOpcode.InventoryDestroyItem, new byte[0x20]);
        Assert.AreEqual(0, _sent.Count);
    }

    // --- RestoreMissionCargoAfterLogin ---

    [TestMethod]
    public void RestoreMissionCargoAfterLogin_EmptyQuests_NoThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        var method = typeof(TNLConnection).GetMethod(
            "RestoreMissionCargoAfterLogin",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(client, new object[] { character });
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void RestoreMissionCargoAfterLogin_NullCharacter_NoThrow()
    {
        var client = CreateClient();
        var method = typeof(TNLConnection).GetMethod(
            "RestoreMissionCargoAfterLogin",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(client, new object[] { null });
    }

    // --- HandlePacket dispatch soft paths ---

    [TestMethod]
    public void HandlePacket_UnknownOpcode_DoesNotThrow()
    {
        var client = CreateClient();
        InvokeHandlePacket(client, 0xDEADBEEFu, Array.Empty<byte>());
    }

    [TestMethod]
    public void HandlePacket_DamageOpcode_NoOp()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandlePacket(client, (uint)GameOpcode.Damage, Array.Empty<byte>());
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void HandlePacket_StoreClose_Dispatches()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        InvokeHandlePacket(client, (uint)GameOpcode.StoreClose, Array.Empty<byte>());
    }

    [TestMethod]
    public void HandlePacket_SocialStubs_DoNotThrow()
    {
        var character = CreateCharacterOnMap(out _);
        var client = CreateClient(character);
        foreach (var op in new[]
                 {
                     GameOpcode.GetFriends, GameOpcode.GetEnemies, GameOpcode.GetIgnored,
                 })
        {
            InvokeHandlePacket(client, (uint)op, Array.Empty<byte>());
        }
    }

    private static void InvokeHandlePacket(TNLConnection connection, uint opcode, byte[] body)
    {
        var method = typeof(TNLConnection).GetMethod(
            "HandlePacket",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(opcode);
            writer.Write(body);
            writer.Flush();
        }

        var buffer = new global::TNL.Utils.ByteBuffer(stream.ToArray(), (uint)stream.Length);
        method.Invoke(connection, new object[] { buffer });
    }
}

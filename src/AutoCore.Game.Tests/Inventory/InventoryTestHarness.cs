using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace AutoCore.Game.Tests.Inventory;

public sealed class InventoryTestHarness
{
    public RecordingInventoryPersistence Persistence { get; }
    public FakeCloneBaseLookup CloneBases { get; }
    public FakeEquippableObjectFactory EquipFactory { get; }
    public InventoryManager Inventory { get; }
    public Character Character { get; }
    public Vehicle Vehicle { get; }

    public InventoryTestHarness(long characterCoid = 5001, long vehicleCoid = 9001)
    {
        Persistence = new RecordingInventoryPersistence();
        CloneBases = new FakeCloneBaseLookup();
        EquipFactory = new FakeEquippableObjectFactory();
        Inventory = new InventoryManager(Persistence, CloneBases, EquipFactory);

        Character = new Character();
        Character.SetCoid(characterCoid, true);
        Character.AttachTestDataForTests();

        Vehicle = new Vehicle();
        Vehicle.SetCoid(vehicleCoid, true);
        Character.AttachCurrentVehicleForTests(Vehicle);
        Character.AttachInventoryForTests(Inventory);
    }

    public void RegisterWeapon(int cbid, byte flags, byte subType = 0)
    {
        var weaponBase = CreateWeaponCloneBase(cbid, flags, subType);
        CloneBases.Register(cbid, weaponBase);
        EquipFactory.Register(cbid, (coid, global) =>
        {
            var weapon = new Weapon();
            weapon.SetCoid(coid, global);
            return weapon;
        });
    }

    public void RegisterArmor(int cbid)
    {
        CloneBases.Register(cbid, CreateObjectCloneBase(cbid, CloneBaseObjectType.Armor));
        EquipFactory.Register(cbid, (coid, global) =>
        {
            var armor = new Armor();
            armor.SetCoid(coid, global);
            return armor;
        });
    }

    public void RegisterPowerPlant(int cbid)
    {
        CloneBases.Register(cbid, CreateObjectCloneBase(cbid, CloneBaseObjectType.PowerPlant));
        EquipFactory.Register(cbid, (coid, global) =>
        {
            var powerPlant = new PowerPlant();
            powerPlant.SetCoid(coid, global);
            return powerPlant;
        });
    }

    public void RegisterRaceItem(int cbid, short itemSubType = VehicleEquipmentSlotResolver.ItemSubTypeRaceItem)
    {
        CloneBases.Register(cbid, CreateItemCloneBase(cbid, itemSubType));
        EquipFactory.Register(cbid, (coid, global) =>
        {
            var item = new SimpleObject(GraphicsObjectType.Graphics);
            item.SetCoid(coid, global);
            return item;
        });
    }

    public Weapon EquipWeapon(VehicleEquipmentSlot slot, int cbid, long coid)
    {
        var weapon = new Weapon();
        weapon.SetCoid(coid, true);
        AttachCloneBase(weapon, cbid, CloneBaseObjectType.Weapon);
        Vehicle.TryEquipItem(slot, weapon, out _);
        return weapon;
    }

    public Armor EquipArmor(int cbid, long coid)
    {
        var armor = new Armor();
        armor.SetCoid(coid, true);
        AttachCloneBase(armor, cbid, CloneBaseObjectType.Armor);
        Vehicle.TryEquipItem(VehicleEquipmentSlot.Armor, armor, out _);
        return armor;
    }

    public static InventoryGrabPacket CreateGrabPacket(
        long itemCoid,
        byte inventoryType = 1,
        bool itemGlobal = true,
        int equipmentCbid = -1,
        int equipmentSlotHint = -1,
        int quantity = 1)
    {
        var bytes = new byte[0x30];
        BitConverter.GetBytes((uint)GameOpcode.InventoryGrab).CopyTo(bytes, 0);
        BitConverter.GetBytes(itemCoid).CopyTo(bytes, 8);
        bytes[0x10] = (byte)(itemGlobal ? 1 : 0);
        bytes[0x18] = inventoryType;
        if (inventoryType == 2)
        {
            if (equipmentCbid > 0)
                BitConverter.GetBytes(equipmentCbid).CopyTo(bytes, 4);
            if (equipmentSlotHint >= 0)
                BitConverter.GetBytes(equipmentSlotHint).CopyTo(bytes, 0x14);
        }
        else if (quantity > 1)
        {
            BitConverter.GetBytes(quantity).CopyTo(bytes, 0x1c);
        }

        return ReadPacket<InventoryGrabPacket>(bytes);
    }

    public static InventoryDropPacket CreateDropPacket(
        long itemCoid,
        byte x,
        byte y,
        byte inventoryType = 1,
        bool itemGlobal = true)
    {
        var bytes = new byte[0x20];
        BitConverter.GetBytes((uint)GameOpcode.InventoryDrop).CopyTo(bytes, 0);
        BitConverter.GetBytes(itemCoid).CopyTo(bytes, 8);
        bytes[0x10] = (byte)(itemGlobal ? 1 : 0);
        bytes[0x18] = x;
        bytes[0x19] = y;
        bytes[0x1a] = inventoryType;
        return ReadPacket<InventoryDropPacket>(bytes);
    }

    private static T ReadPacket<T>(byte[] bytes) where T : BasePacket, new()
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        _ = reader.ReadUInt32();
        var packet = new T();
        packet.Read(reader);
        return packet;
    }

    private static CloneBaseWeapon CreateWeaponCloneBase(int cbid, byte flags, byte subType)
    {
        var clone = (CloneBaseWeapon)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseWeapon));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Weapon, CloneBaseId = cbid };
        clone.WeaponSpecific = new WeaponSpecific { Flags = flags, SubType = subType };
        return clone;
    }

    private static CloneBaseObject CreateObjectCloneBase(int cbid, CloneBaseObjectType type)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)type, CloneBaseId = cbid };
        return clone;
    }

    private static CloneBaseObject CreateItemCloneBase(int cbid, short subType)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { SubType = subType };
        return clone;
    }

    private static void AttachCloneBase(SimpleObject item, int cbid, CloneBaseObjectType type)
    {
        typeof(ClonedObjectBase)
            .GetProperty(nameof(ClonedObjectBase.CloneBaseObject), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(item, CreateObjectCloneBase(cbid, type));
    }
}

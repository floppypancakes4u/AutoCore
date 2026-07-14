namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
public sealed class InventoryItemCreator : IInventoryItemCreator
{
    public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
    {
        var item = ClonedObjectBase.AllocateNewObjectFromCBID(entry.Cbid);
        if (item == null)
            return InventoryItemCreateResult.Unsupported($"item type {entry.Type} is not supported by the server create factory yet.");

        var cloneBase = AssetManager.Instance.GetCloneBase(entry.Cbid);
        if (cloneBase == null)
            return InventoryItemCreateResult.Unsupported("clonebase is not loaded.");

        var packet = CreatePacketFor(entry.Type);

        item.SetCoid(coid, true);
        item.LoadCloneBase(entry.Cbid);
        item.WriteToPacket(packet);

        packet.IsInInventory = true;
        packet.InventoryPositionX = x;
        packet.InventoryPositionY = y;
        packet.Quantity = 1;
        packet.IsBound = false;
        packet.IsIdentified = true;
        // Quest/mission clonebase types open Mission Inventory UI on the client.
        if (entry.Type is CloneBaseObjectType.QuestObject or CloneBaseObjectType.MissionObject)
            packet.PossibleMissionItem = true;

        var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? cloneBase.CloneBaseSpecific.UniqueName
            : entry.DisplayName;

        return InventoryItemCreateResult.Success(packet, displayName);
    }

    /// <summary>
    /// Must match LootManager / vehicle create: weapons/armor/powerplants/wheelsets
    /// write typed opcodes and trailing stats. A plain CreateSimpleObject for a weapon
    /// CBID makes the client mis-parse the object and show jumbled stats on relog.
    /// </summary>
    public static CreateSimpleObjectPacket CreatePacketFor(CloneBaseObjectType type)
    {
        return type switch
        {
            CloneBaseObjectType.Armor => new CreateArmorPacket(),
            CloneBaseObjectType.Weapon => new CreateWeaponPacket(),
            CloneBaseObjectType.PowerPlant => new CreatePowerPlantPacket(),
            CloneBaseObjectType.WheelSet => new CreateWheelSetPacket(),
            _ => new CreateSimpleObjectPacket()
        };
    }
}

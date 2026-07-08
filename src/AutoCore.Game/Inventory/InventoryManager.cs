namespace AutoCore.Game.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;

public sealed class InventoryManager
{
    public const int CargoWidth = 24;
    public const int CargoPageCount = 13;
    public const int CargoSlotCount = 312;

    private readonly List<CharacterInventoryItem> _items = new();

    public IReadOnlyList<CharacterInventoryItem> Items => _items;

    public bool IsFull => _items.Count >= CargoSlotCount || !TryGetFirstFreeCargoSlot(out _, out _);

    public bool TryGetFirstFreeCargoSlot(out byte x, out byte y)
    {
        var occupied = new bool[CargoSlotCount];
        foreach (var item in _items)
        {
            var slot = item.InventoryPositionY * CargoWidth + item.InventoryPositionX;
            if (slot >= 0 && slot < CargoSlotCount)
                occupied[slot] = true;
        }

        for (var slot = 0; slot < CargoSlotCount; slot++)
        {
            if (occupied[slot])
                continue;

            x = (byte)(slot % CargoWidth);
            y = (byte)(slot / CargoWidth);
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    public bool TryAdd(CharacterInventoryItem item)
    {
        if (!CanAdd(item))
            return false;

        _items.Add(item);
        return true;
    }

    public CharacterInventoryItem FindByCoid(long coid)
    {
        return _items.LastOrDefault(i => i.Coid == coid);
    }

    public bool TryMove(long coid, byte x, byte y, out CharacterInventoryItem movedItem)
    {
        if (!IsValidCargoSlot(x, y))
        {
            movedItem = null;
            return false;
        }

        var index = _items.FindLastIndex(i => i.Coid == coid);
        if (index < 0)
        {
            movedItem = null;
            return false;
        }

        if (_items.Any(i => i.Coid != coid && i.InventoryPositionX == x && i.InventoryPositionY == y))
        {
            movedItem = null;
            return false;
        }

        movedItem = _items[index] with
        {
            InventoryPositionX = x,
            InventoryPositionY = y
        };
        _items[index] = movedItem;
        return true;
    }

    private static bool IsValidCargoSlot(byte x, byte y)
    {
        return x < CargoWidth && y < CargoPageCount;
    }

    private bool CanAdd(CharacterInventoryItem item)
    {
        if (!IsValidCargoSlot(item.InventoryPositionX, item.InventoryPositionY))
            return false;

        if (_items.Any(i => i.InventoryPositionX == item.InventoryPositionX && i.InventoryPositionY == item.InventoryPositionY))
            return false;

        if (_items.Any(i => i.Coid == item.Coid))
            return false;

        return true;
    }

    public InventoryCommandResult AddItem(InventoryCatalogEntry entry, IInventoryItemCreator itemCreator, long coid)
    {
        if (!TryGetFirstFreeCargoSlot(out var x, out var y))
            return new InventoryCommandResult($"Cargo inventory is full ({CargoSlotCount}/{CargoSlotCount}).");

        var createResult = itemCreator.Create(entry, coid, x, y);
        if (!createResult.WasSuccessful)
            return new InventoryCommandResult($"Cannot add CBID {entry.Cbid}: {createResult.Error}");

        var item = new CharacterInventoryItem(entry.Cbid, entry.Type, createResult.DisplayName, coid, x, y, 1);
        if (!TryAdd(item))
            return new InventoryCommandResult($"Cargo inventory is full ({CargoSlotCount}/{CargoSlotCount}).");

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            createResult.Packet,
            InventoryPacketFactory.CreateCargoSendAll(this),
            new InventoryAddItemResponsePacket
            {
                ItemCoid = coid,
                InventoryPositionX = x,
                InventoryPositionY = y,
                AddToExistingItem = false,
                Quantity = 1,
                WasSuccessful = true
            }
        };

        return new InventoryCommandResult($"Added {createResult.DisplayName} ({entry.Cbid}) to cargo slot {x},{y}.", packets, item);
    }

    public IReadOnlyList<BasePacket> CreateItemObjectPackets(InventoryCatalog catalog, IInventoryItemCreator itemCreator)
    {
        var packets = new List<BasePacket>();
        foreach (var item in Items.OrderBy(i => i.InventoryPositionY).ThenBy(i => i.InventoryPositionX))
        {
            var entry = catalog.FindAny(item.Cbid);
            if (entry == null || !InventoryItemTypePolicy.IsInventoryCapable(entry.Type))
                continue;

            var createResult = itemCreator.Create(entry, item.Coid, item.InventoryPositionX, item.InventoryPositionY);
            if (!createResult.WasSuccessful)
                continue;

            createResult.Packet.Quantity = item.Quantity;
            packets.Add(createResult.Packet);
        }

        return packets;
    }

    public InventoryOperationResult Grab(InventoryGrabPacket packet, Character character)
    {
        if (character == null || character.Map == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                "HandleInventoryGrabPacket: Character or Map is null");
        }

        var existingInventoryItem = Items.LastOrDefault(i => i.Coid == packet.ItemCoid);
        if (existingInventoryItem != null)
        {
            var grabQuantity = Math.Max(1, Math.Min(existingInventoryItem.Quantity, packet.Quantity));
            return InventoryOperationResult.SinglePacket(
                new InventoryGrabResponsePacket
                {
                    ItemCoid = existingInventoryItem.Coid,
                    ItemGlobal = packet.ItemGlobal,
                    InventoryType = packet.InventoryType,
                    Quantity = grabQuantity,
                    AddToExistingItem = false,
                    InventoryPositionX = existingInventoryItem.InventoryPositionX,
                    InventoryPositionY = existingInventoryItem.InventoryPositionY,
                    WasSuccessful = true
                },
                $"HandleInventoryGrabPacket: Player {character.Name} grabbing existing inventory item {existingInventoryItem.Coid} (CBID: {existingInventoryItem.Cbid}) from slot {existingInventoryItem.InventoryPositionX},{existingInventoryItem.InventoryPositionY}");
        }

        var map = character.Map;
        if (!TryResolveInventoryGrabSource(packet, character, map, out var source))
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: Item {packet.ItemCoid} not found in map or clonebase");
        }

        if (source.MapObject != null && source.MapObject is not SimpleObject)
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: Item {packet.ItemCoid} is not a SimpleObject");
        }

        if (!InventoryItemTypePolicy.IsInventoryCapable(source.Type))
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: CBID {source.Cbid} ({source.Type}) is not inventory-capable");
        }

        if (source.MapObject != null)
        {
            var distance = character.CurrentVehicle.Position.DistSq(source.MapObject.Position);
            const float maxGrabDistanceSq = 100.0f;
            if (distance > maxGrabDistanceSq)
            {
                return InventoryOperationResult.SinglePacket(
                    CreateGrabFailure(packet),
                    $"HandleInventoryGrabPacket: Item {source.SourceCoid} too far away (distance: {Math.Sqrt(distance):F2})");
            }
        }

        if (!TryChooseInventorySlot(packet, out var x, out var y))
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: Cargo inventory is full for {character.Name}");
        }

        var inventoryItem = new CharacterInventoryItem(
            source.Cbid,
            source.Type,
            source.DisplayName,
            source.SourceCoid,
            x,
            y,
            Math.Max(1, packet.Quantity));

        if (!TryAdd(inventoryItem))
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: Cargo slot {x},{y} is unavailable for {character.Name}");
        }

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            InventoryPacketFactory.CreateCargoSendAll(this),
            new InventoryGrabResponsePacket
            {
                ItemCoid = source.SourceCoid,
                ItemGlobal = source.SourceGlobal,
                InventoryType = packet.InventoryType,
                Quantity = inventoryItem.Quantity,
                AddToExistingItem = true,
                InventoryPositionX = x,
                InventoryPositionY = y,
                WasSuccessful = true
            }
        };

        return new InventoryOperationResult(
            packets,
            $"HandleInventoryGrabPacket: Player {character.Name} grabbing item {source.SourceCoid} (CBID: {source.Cbid}, source={source.SourceKind}) into slot {x},{y}",
            source.MapObject);
    }

    public InventoryOperationResult Drop(InventoryDropPacket packet, Character character)
    {
        if (character == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                "HandleInventoryDropPacket: Character is null");
        }

        if (packet.InventoryType != 1)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Inventory type {packet.InventoryType} is not supported yet");
        }

        if (packet.InventoryPositionX >= CargoWidth || packet.InventoryPositionY >= CargoPageCount)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Invalid cargo slot {packet.InventoryPositionX},{packet.InventoryPositionY}");
        }

        var item = FindByCoid(packet.ItemCoid);
        if (item == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Item {packet.ItemCoid} not found in inventory");
        }

        if (!TryMove(packet.ItemCoid, packet.InventoryPositionX, packet.InventoryPositionY, out var movedItem))
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Could not move item {packet.ItemCoid} to slot {packet.InventoryPositionX},{packet.InventoryPositionY}");
        }

        return InventoryOperationResult.SinglePacket(
            new InventoryDropResponsePacket
            {
                ItemCoid = movedItem.Coid,
                ItemGlobal = packet.ItemGlobal,
                InventoryPositionX = movedItem.InventoryPositionX,
                InventoryPositionY = movedItem.InventoryPositionY,
                InventoryType = packet.InventoryType,
                WasSuccessful = true,
                HasSwappedOrConcatenatedItem = false
            },
            $"HandleInventoryDropPacket: Player {character.Name} moved item {movedItem.Coid} (CBID: {movedItem.Cbid}) to slot {movedItem.InventoryPositionX},{movedItem.InventoryPositionY}");
    }

    public static InventoryGrabResponsePacket CreateGrabFailure(InventoryGrabPacket packet)
    {
        return new InventoryGrabResponsePacket
        {
            ItemCoid = packet.ItemCoid,
            ItemGlobal = packet.ItemGlobal,
            InventoryType = packet.InventoryType,
            Quantity = Math.Max(1, packet.Quantity),
            WasSuccessful = false
        };
    }

    public static InventoryDropResponsePacket CreateDropFailure(InventoryDropPacket packet)
    {
        return new InventoryDropResponsePacket
        {
            ItemCoid = packet.ItemCoid,
            ItemGlobal = packet.ItemGlobal,
            InventoryPositionX = packet.InventoryPositionX,
            InventoryPositionY = packet.InventoryPositionY,
            InventoryType = packet.InventoryType,
            WasSuccessful = false,
            HasSwappedOrConcatenatedItem = false
        };
    }

    private bool TryChooseInventorySlot(InventoryGrabPacket packet, out byte x, out byte y)
    {
        if (packet.HasRequestedInventoryPosition)
        {
            x = packet.RequestedInventoryPositionX;
            y = packet.RequestedInventoryPositionY;
            return true;
        }

        return TryGetFirstFreeCargoSlot(out x, out y);
    }

    private static bool TryResolveInventoryGrabSource(
        InventoryGrabPacket packet,
        Character character,
        SectorMap map,
        out InventoryGrabSource source)
    {
        if (TryCreateMapSource(map.GetObjectByCoid(packet.ItemCoid), "parsed-coid", out source))
            return true;

        foreach (var candidate in packet.EnumerateInt64Candidates().Distinct())
        {
            if (TryCreateMapSource(map.GetObjectByCoid(candidate), $"i64:{candidate}", out source))
                return true;
        }

        foreach (var cbid in packet.EnumerateInt32Candidates().Distinct())
        {
            var objectByCbid = map.Objects.Values
                .OfType<SimpleObject>()
                .Where(o => o.CBID == cbid)
                .OrderBy(o => character.CurrentVehicle.Position.DistSq(o.Position))
                .FirstOrDefault();

            if (TryCreateMapSource(objectByCbid, $"cbid-map:{cbid}", out source))
                return true;
        }

        foreach (var cbid in packet.EnumerateInt32Candidates().Distinct())
        {
            var cloneBase = AssetManager.Instance.GetCloneBase(cbid);
            if (cloneBase == null)
                continue;

            source = new InventoryGrabSource(
                null,
                packet.ItemCoid,
                packet.ItemGlobal,
                cbid,
                cloneBase.Type,
                string.IsNullOrWhiteSpace(cloneBase.CloneBaseSpecific.UniqueName)
                    ? $"CBID {cbid}"
                    : cloneBase.CloneBaseSpecific.UniqueName,
                $"cbid-only:{cbid}");
            return true;
        }

        source = default;
        return false;
    }

    private static bool TryCreateMapSource(ClonedObjectBase mapObject, string sourceKind, out InventoryGrabSource source)
    {
        if (mapObject != null)
        {
            source = new InventoryGrabSource(
                mapObject,
                mapObject.ObjectId.Coid,
                mapObject.ObjectId.Global,
                mapObject.CBID,
                mapObject.Type,
                mapObject.CloneBaseObject?.CloneBaseSpecific.UniqueName ?? $"CBID {mapObject.CBID}",
                sourceKind);
            return true;
        }

        source = default;
        return false;
    }

    private readonly record struct InventoryGrabSource(
        ClonedObjectBase MapObject,
        long SourceCoid,
        bool SourceGlobal,
        int Cbid,
        CloneBaseObjectType Type,
        string DisplayName,
        string SourceKind);

}

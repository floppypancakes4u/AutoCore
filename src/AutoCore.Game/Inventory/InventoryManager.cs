namespace AutoCore.Game.Inventory;

using System.Diagnostics.CodeAnalysis;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

public sealed class InventoryManager
{
    public const int DefaultCargoWidth = 24;
    public const int DefaultCargoPageCount = 13;
    public const int DefaultCargoSlotCount = DefaultCargoWidth * DefaultCargoPageCount;
    public const int MaxCargoSlotCount = 512;

    /// <summary>Legacy alias for default width; prefer <see cref="Width"/> on instances.</summary>
    public const int CargoWidth = DefaultCargoWidth;

    /// <summary>Legacy alias for default page count; prefer <see cref="PageCount"/> on instances.</summary>
    public const int CargoPageCount = DefaultCargoPageCount;

    /// <summary>Legacy alias for default slot count; prefer <see cref="SlotCount"/> on instances.</summary>
    public const int CargoSlotCount = DefaultCargoSlotCount;

    private readonly List<CharacterInventoryItem> _items = new();
    private readonly Dictionary<long, PendingEquippedItemDrag> _pendingEquippedItemDrags = new();
    private readonly IInventoryPersistence _persistence;
    private readonly ICloneBaseLookup _cloneBases;
    private readonly IEquippableObjectFactory _equipFactory;

    public InventoryManager(
        IInventoryPersistence persistence = null,
        ICloneBaseLookup cloneBases = null,
        IEquippableObjectFactory equipFactory = null)
    {
        _persistence = persistence;
        _cloneBases = cloneBases ?? AssetManagerCloneBaseLookup.Instance;
        _equipFactory = equipFactory ?? ClonedObjectEquippableFactory.Instance;
    }

    public int Width { get; private set; } = DefaultCargoWidth;
    public int PageCount { get; private set; } = DefaultCargoPageCount;
    public int SlotCount => Width * PageCount;

    public IReadOnlyList<CharacterInventoryItem> Items => _items;

    public bool IsFull => _items.Count >= SlotCount || !TryGetFirstFreeCargoSlot(out _, out _);

    public void SetCapacity(int width, int pageCount)
    {
        if (width < 1)
            width = 1;
        if (pageCount < 1)
            pageCount = 1;

        while (width * pageCount > MaxCargoSlotCount && pageCount > 1)
            pageCount--;
        while (width * pageCount > MaxCargoSlotCount && width > 1)
            width--;

        Width = width;
        PageCount = pageCount;
    }

    public void LoadItems(IEnumerable<CharacterInventoryItem> items)
    {
        _items.Clear();
        if (items == null)
            return;

        var occupied = new HashSet<int>();
        foreach (var item in items)
        {
            if (item == null || !IsValidCargoSlot(item.InventoryPositionX, item.InventoryPositionY))
                continue;

            var slot = item.InventoryPositionY * Width + item.InventoryPositionX;
            if (!occupied.Add(slot))
                continue;

            _items.Add(item);
        }
    }

    public int GetOccupiedSlotCount()
    {
        var occupied = new HashSet<int>();
        foreach (var item in _items)
        {
            var slot = item.InventoryPositionY * Width + item.InventoryPositionX;
            if (slot >= 0 && slot < SlotCount)
                occupied.Add(slot);
        }

        return occupied.Count;
    }

    public InventoryCommandResult ClearCargo(long characterCoid)
    {
        var removed = _items.Count;
        _items.Clear();

        if (characterCoid != 0)
            PersistCargoClear(characterCoid);

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            InventoryPacketFactory.CreateCargoSendAll(this)
        };

        return new InventoryCommandResult(
            $"Cleared {removed} cargo item(s). Capacity is {Width}x{PageCount} ({SlotCount} slots).",
            packets);
    }

    public string DescribeCargoStatus()
    {
        var occupied = GetOccupiedSlotCount();
        return $"{_items.Count} item(s) loaded, {occupied}/{SlotCount} slots occupied, capacity {Width}x{PageCount}.";
    }

    public bool TryGetFirstFreeCargoSlot(out byte x, out byte y)
    {
        var occupied = new bool[SlotCount];
        foreach (var item in _items)
        {
            var slot = item.InventoryPositionY * Width + item.InventoryPositionX;
            if (slot >= 0 && slot < SlotCount)
                occupied[slot] = true;
        }

        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (occupied[slot])
                continue;

            x = (byte)(slot % Width);
            y = (byte)(slot / Width);
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

    private bool IsValidCargoSlot(byte x, byte y)
    {
        return x < Width && y < PageCount;
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

    public InventoryCommandResult AddItem(InventoryCatalogEntry entry, IInventoryItemCreator itemCreator, long coid, long characterCoid = 0, int quantity = 1)
    {
        if (!TryGetFirstFreeCargoSlot(out var x, out var y))
            return new InventoryCommandResult(BuildCargoFullMessage());

        var createResult = itemCreator.Create(entry, coid, x, y);
        if (!createResult.WasSuccessful)
            return new InventoryCommandResult($"Cannot add CBID {entry.Cbid}: {createResult.Error}");

        // New stacks take quantity from the create packet; send AddItemResponse next
        // so the client places the object before CargoSendAll.
        createResult.Packet.Quantity = quantity;
        var item = new CharacterInventoryItem(entry.Cbid, entry.Type, createResult.DisplayName, coid, x, y, quantity);
        if (!TryAdd(item))
            return new InventoryCommandResult(BuildCargoAddRejectedMessage(coid, x, y));

        if (characterCoid != 0)
            PersistCargoUpsert(characterCoid, item);

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            createResult.Packet,
            new InventoryAddItemResponsePacket
            {
                ItemCoid = coid,
                InventoryPositionX = x,
                InventoryPositionY = y,
                AddToExistingItem = false,
                Quantity = quantity,
                WasSuccessful = true
            },
            InventoryPacketFactory.CreateCargoSendAll(this)
        };

        var quantitySuffix = quantity > 1 ? $" x{quantity}" : string.Empty;
        return new InventoryCommandResult($"Added {createResult.DisplayName} ({entry.Cbid}){quantitySuffix} to cargo slot {x},{y}.", packets, item);
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
        if (character == null)
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

        if (packet.InventoryType == 2)
            return GrabEquippedVehicleItem(packet, character);

        if (character.Map == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                "HandleInventoryGrabPacket: Character or Map is null");
        }

        return GrabFromSectorMap(packet, character);
    }

    [ExcludeFromCodeCoverage]
    private InventoryOperationResult GrabFromSectorMap(InventoryGrabPacket packet, Character character)
    {
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

        PersistCargoUpsert(character.ObjectId.Coid, inventoryItem);

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

        // HARDPOINT (2): equip from cargo / pending drag onto a vehicle slot.
        if (packet.InventoryType == 2)
            return DropToHardpoint(packet, character);

        if (packet.InventoryType != 1)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Inventory type {packet.InventoryType} is not supported yet");
        }

        if (packet.InventoryPositionX >= Width || packet.InventoryPositionY >= PageCount)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Invalid cargo slot {packet.InventoryPositionX},{packet.InventoryPositionY}");
        }

        var item = FindByCoid(packet.ItemCoid);
        if (item == null)
        {
            if (_pendingEquippedItemDrags.TryGetValue(packet.ItemCoid, out var pendingEquippedItem))
                return DropPendingEquippedItem(packet, character, pendingEquippedItem);

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

        PersistCargoMove(character.ObjectId.Coid, movedItem);

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

    private InventoryOperationResult DropToHardpoint(InventoryDropPacket packet, Character character)
    {
        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                "HandleInventoryDropPacket: No current vehicle for hardpoint equip");
        }

        CharacterInventoryItem cargoItem = null;
        PendingEquippedItemDrag? pending = null;
        int cbid;
        CloneBaseObjectType type;
        string displayName;
        bool itemGlobal;
        byte sourceInventoryType;

        if (_pendingEquippedItemDrags.TryGetValue(packet.ItemCoid, out var pendingDrag))
        {
            pending = pendingDrag;
            cbid = pendingDrag.Cbid;
            type = pendingDrag.Type;
            displayName = pendingDrag.DisplayName;
            itemGlobal = pendingDrag.Global;
            sourceInventoryType = 2;
        }
        else
        {
            cargoItem = FindByCoid(packet.ItemCoid);
            if (cargoItem == null)
            {
                return InventoryOperationResult.SinglePacket(
                    CreateDropFailure(packet),
                    $"HandleInventoryDropPacket: Item {packet.ItemCoid} not found for hardpoint equip");
            }

            cbid = cargoItem.Cbid;
            type = cargoItem.Type;
            displayName = cargoItem.DisplayName;
            itemGlobal = packet.ItemGlobal;
            sourceInventoryType = 1;
        }

        var cloneBase = _cloneBases.GetCloneBase(cbid);
        if (cloneBase == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: CloneBase {cbid} not loaded for equip");
        }

        if (!VehicleEquipmentSlotResolver.TryResolve(type, cloneBase, packet.InventoryPositionX, out var slot))
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Cannot resolve hardpoint slot for CBID {cbid} (type={type}, dropX={packet.InventoryPositionX})");
        }

        var equipObject = _equipFactory.Create(cbid, type, packet.ItemCoid, itemGlobal);
        if (equipObject == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Cannot create equippable object for CBID {cbid}");
        }

        if (!vehicle.TryEquipItem(slot, equipObject, out var previousItem))
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Failed to equip CBID {cbid} into slot {slot}");
        }

        if (cargoItem != null)
        {
            _items.Remove(cargoItem);
            PersistCargoDelete(character.ObjectId.Coid, cargoItem.Coid);
        }

        if (pending.HasValue)
            _pendingEquippedItemDrags.Remove(packet.ItemCoid);

        // Swapped previous hardpoint item goes into cargo (prefer the drop's cargo coords if valid).
        CharacterInventoryItem swappedCargoItem = null;
        if (previousItem != null)
        {
            byte swapX = 0;
            byte swapY = 0;
            if (!TryGetFirstFreeCargoSlot(out swapX, out swapY))
            {
                // Roll back equip if cargo is full — put previous back.
                vehicle.TryEquipItem(slot, previousItem, out _);
                if (cargoItem != null)
                {
                    _items.Add(cargoItem);
                    PersistCargoUpsert(character.ObjectId.Coid, cargoItem);
                }
                if (pending.HasValue)
                    _pendingEquippedItemDrags[packet.ItemCoid] = pending.Value;

                return InventoryOperationResult.SinglePacket(
                    CreateDropFailure(packet),
                    $"HandleInventoryDropPacket: Cargo full; cannot unequip previous item from {slot}");
            }

            swappedCargoItem = new CharacterInventoryItem(
                previousItem.CBID,
                previousItem.Type,
                previousItem.CloneBaseObject?.CloneBaseSpecific.UniqueName ?? $"CBID {previousItem.CBID}",
                previousItem.ObjectId.Coid,
                swapX,
                swapY,
                1);
            _items.Add(swappedCargoItem);
            PersistCargoUpsert(character.ObjectId.Coid, swappedCargoItem);
        }

        PersistEquip(vehicle, equipObject);

        // Always refresh cargo: item left cargo, and a swap may have added the previous hardpoint item.
        var packets = BuildHardpointEquipPackets(
            this,
            vehicle,
            new TFID(packet.ItemCoid, itemGlobal),
            previousItem?.ObjectId,
            sourceInventoryType);

        return new InventoryOperationResult(
            packets,
            $"HandleInventoryDropPacket: Player {character.Name} equipped {displayName} (CBID {cbid}, coid={packet.ItemCoid}) into {slot}" +
            (previousItem != null ? $" (swapped out coid={previousItem.ObjectId.Coid})" : string.Empty));
    }

    /// <summary>
    /// Hardpoint equip ack: InventoryEquip (0x203C) only, then CargoSendAll.
    /// Do NOT send InventoryDropResponse with type HARDPOINT — Client_RecvInventoryDrop
    /// rejects type 2 with "Called Drop on invalid inventory object".
    /// PutInHand=true matches ghost-synthesized equips and clears the drag cursor
    /// via client FUN_007fc270.
    /// </summary>
    public static IReadOnlyList<BasePacket> BuildHardpointEquipPackets(
        InventoryManager inventory,
        Vehicle vehicle,
        TFID newItemId,
        TFID oldItemId,
        byte sourceInventoryType)
    {
        var hasOldItem = oldItemId is { Coid: > 0 };
        var packets = new List<BasePacket>
        {
            new InventoryEquipPacket
            {
                ItemId = new TFID(newItemId.Coid, newItemId.Global),
                VehicleId = new TFID(vehicle.ObjectId.Coid, vehicle.ObjectId.Global),
                OldItemId = hasOldItem
                    ? new TFID(oldItemId.Coid, oldItemId.Global)
                    : new TFID(-1, false),
                PutInHand = true,
                InventoryPositionX = 0,
                InventoryPositionY = 0,
                InventoryTypeFrom = sourceInventoryType,
            }
        };

        if (inventory != null)
            packets.Add(InventoryPacketFactory.CreateCargoSendAll(inventory));

        return packets;
    }

    private InventoryOperationResult GrabEquippedVehicleItem(InventoryGrabPacket packet, Character character)
    {
        var vehicle = character.CurrentVehicle;
        if (vehicle == null || !vehicle.TryFindEquippedItem(packet.ItemCoid, packet.EquipmentCbid, out var slot, out var item))
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: Equipped item {packet.ItemCoid} (CBID fallback {packet.EquipmentCbid}, slot hint {packet.EquipmentSlotHint}) not found on vehicle");
        }

        var itemCoid = item.ObjectId.Coid;
        var itemGlobal = item.ObjectId.Global;
        var itemTfid = new TFID(itemCoid, itemGlobal);
        var cbid = item.CBID;
        var itemType = item.Type;
        var displayName = item.CloneBaseObject?.CloneBaseSpecific.UniqueName ?? $"CBID {cbid}";

        // Clear the hardpoint immediately so the client icon leaves the slot (0x203E),
        // then acknowledge the grab. Cargo creation still happens on drop.
        if (!vehicle.TryUnequipItem(itemCoid, out slot, out _))
        {
            return InventoryOperationResult.SinglePacket(
                CreateGrabFailure(packet),
                $"HandleInventoryGrabPacket: Failed to unequip vehicle item {itemCoid} from slot {slot}");
        }

        PersistUnequip(vehicle);

        _pendingEquippedItemDrags[itemCoid] = new PendingEquippedItemDrag(
            vehicle,
            slot,
            cbid,
            itemType,
            displayName,
            itemGlobal,
            AlreadyUnequipped: true);

        return new InventoryOperationResult(
            BuildEquippedGrabPackets(itemTfid, vehicle.ObjectId, itemGlobal, packet.InventoryType),
            $"HandleInventoryGrabPacket: Player {character.Name} grabbing equipped vehicle item {itemCoid} (requestCoid={packet.ItemCoid}, CBID: {cbid}, slot={slot}, slotHint={packet.EquipmentSlotHint})");
    }

    /// <summary>
    /// Packet order for hardpoint grab: InventoryUnequip (0x203E) first, then GrabResponse.
    /// Unequip-first matches the client path that clears the equipped icon before the drag
    /// cursor is acknowledged.
    /// </summary>
    public static IReadOnlyList<BasePacket> BuildEquippedGrabPackets(
        TFID itemId,
        TFID vehicleId,
        bool itemGlobal,
        byte inventoryType)
    {
        return new BasePacket[]
        {
            new InventoryUnequipPacket
            {
                ItemId = new TFID(itemId.Coid, itemId.Global),
                VehicleId = new TFID(vehicleId.Coid, vehicleId.Global),
                InventoryPositionX = 0,
                InventoryPositionY = 0,
                InventoryType = 2,
            },
            new InventoryGrabResponsePacket
            {
                ItemCoid = itemId.Coid,
                ItemGlobal = itemGlobal,
                InventoryType = inventoryType,
                Quantity = 1,
                AddToExistingItem = false,
                WasSuccessful = true
            }
        };
    }

    private InventoryOperationResult DropPendingEquippedItem(
        InventoryDropPacket packet,
        Character character,
        PendingEquippedItemDrag pendingEquippedItem)
    {
        if (pendingEquippedItem.Vehicle != character.CurrentVehicle)
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Pending equipped item {packet.ItemCoid} belongs to another vehicle");
        }

        var inventoryItem = new CharacterInventoryItem(
            pendingEquippedItem.Cbid,
            pendingEquippedItem.Type,
            pendingEquippedItem.DisplayName,
            packet.ItemCoid,
            packet.InventoryPositionX,
            packet.InventoryPositionY,
            1);

        if (!CanAdd(inventoryItem))
        {
            return InventoryOperationResult.SinglePacket(
                CreateDropFailure(packet),
                $"HandleInventoryDropPacket: Cargo slot {packet.InventoryPositionX},{packet.InventoryPositionY} is unavailable for equipped item {packet.ItemCoid}");
        }

        // Grab already cleared the hardpoint + sent 0x203E. Drop only creates cargo.
        var slot = pendingEquippedItem.Slot;
        if (!pendingEquippedItem.AlreadyUnequipped)
        {
            if (!pendingEquippedItem.Vehicle.TryUnequipItem(packet.ItemCoid, out slot, out _))
            {
                return InventoryOperationResult.SinglePacket(
                    CreateDropFailure(packet),
                    $"HandleInventoryDropPacket: Equipped item {packet.ItemCoid} not found on vehicle during drop");
            }
        }

        _items.Add(inventoryItem);
        _pendingEquippedItemDrags.Remove(packet.ItemCoid);
        PersistCargoUpsert(character.ObjectId.Coid, inventoryItem);
        if (!pendingEquippedItem.AlreadyUnequipped)
            PersistUnequip(pendingEquippedItem.Vehicle);

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            InventoryPacketFactory.CreateCargoSendAll(this),
            new InventoryDropResponsePacket
            {
                ItemCoid = inventoryItem.Coid,
                ItemGlobal = packet.ItemGlobal,
                InventoryPositionX = inventoryItem.InventoryPositionX,
                InventoryPositionY = inventoryItem.InventoryPositionY,
                InventoryType = packet.InventoryType,
                WasSuccessful = true,
                HasSwappedOrConcatenatedItem = false
            }
        };

        return new InventoryOperationResult(
            packets,
            $"HandleInventoryDropPacket: Player {character.Name} unequipped item {inventoryItem.Coid} (CBID: {inventoryItem.Cbid}, slot={slot}) to cargo slot {inventoryItem.InventoryPositionX},{inventoryItem.InventoryPositionY}");
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

    [ExcludeFromCodeCoverage]
    private bool TryResolveInventoryGrabSource(
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
            var cloneBase = _cloneBases.GetCloneBase(cbid);
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

    [ExcludeFromCodeCoverage]
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

    public void SaveCapacity(long characterCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        _persistence.SaveCharacterCargoCapacity(characterCoid, Width, PageCount);
    }

    public void ReloadCargo(long characterCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        LoadItems(_persistence.LoadCargo(characterCoid));
    }

    private void PersistCargoUpsert(long characterCoid, CharacterInventoryItem item)
    {
        if (_persistence == null || characterCoid == 0 || item == null)
            return;

        _persistence.UpsertCargo(characterCoid, item);
    }

    private void PersistCargoMove(long characterCoid, CharacterInventoryItem item)
    {
        if (_persistence == null || characterCoid == 0 || item == null)
            return;

        _persistence.MoveCargo(characterCoid, item);
    }

    private void PersistCargoDelete(long characterCoid, long itemCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        _persistence.DeleteCargo(characterCoid, itemCoid);
    }

    private void PersistCargoClear(long characterCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        _persistence.ClearCargo(characterCoid);
    }

    private string BuildCargoFullMessage()
    {
        return $"Cargo inventory is full ({GetOccupiedSlotCount()}/{SlotCount} slots used, {_items.Count} item(s) loaded). Try /cargoinfo or /clearcargo.";
    }

    private string BuildCargoAddRejectedMessage(long coid, byte x, byte y)
    {
        if (_items.Any(i => i.Coid == coid))
        {
            return $"Cannot add item: COID {coid} is already in cargo. {DescribeCargoStatus()}";
        }

        if (_items.Any(i => i.InventoryPositionX == x && i.InventoryPositionY == y))
        {
            return $"Cannot add item: cargo slot {x},{y} is already occupied. {DescribeCargoStatus()}";
        }

        return $"Cannot add item to cargo slot {x},{y}. {DescribeCargoStatus()}";
    }

    private void PersistEquip(Vehicle vehicle, SimpleObject equippedItem)
    {
        if (_persistence == null || vehicle == null)
            return;

        if (equippedItem != null)
            _persistence.EnsureSimpleObject(equippedItem.ObjectId.Coid, (byte)equippedItem.Type, equippedItem.CBID);

        _persistence.SaveVehicleEquipment(vehicle.ObjectId.Coid, vehicle.CreateEquipmentSnapshot());
    }

    private void PersistUnequip(Vehicle vehicle)
    {
        if (_persistence == null || vehicle == null)
            return;

        _persistence.SaveVehicleEquipment(vehicle.ObjectId.Coid, vehicle.CreateEquipmentSnapshot());
    }

    private readonly record struct InventoryGrabSource(
        ClonedObjectBase MapObject,
        long SourceCoid,
        bool SourceGlobal,
        int Cbid,
        CloneBaseObjectType Type,
        string DisplayName,
        string SourceKind);

    private readonly record struct PendingEquippedItemDrag(
        Vehicle Vehicle,
        VehicleEquipmentSlot Slot,
        int Cbid,
        CloneBaseObjectType Type,
        string DisplayName,
        bool Global,
        bool AlreadyUnequipped = false);

}

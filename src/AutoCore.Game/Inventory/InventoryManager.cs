namespace AutoCore.Game.Inventory;

using System.Diagnostics.CodeAnalysis;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.AgentDebug;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;

public sealed class InventoryManager
{
    /// <summary>
    /// Default column count. Retail cargo width is 6 (<see cref="VehicleCargoCapacity.GridWidth"/>);
    /// kept as aliases for older tests that still expect the constant names.
    /// </summary>
    public const int DefaultCargoWidth = VehicleCargoCapacity.GridWidth;

    /// <summary>
    /// Default grid height (rows). One retail cargo page is 13 rows; starter Callisto is 1 page.
    /// </summary>
    public const int DefaultCargoPageCount = VehicleCargoCapacity.RowsPerPage;

    public const int DefaultCargoSlotCount = DefaultCargoWidth * DefaultCargoPageCount; // 78 (1 page)
    public const int MaxCargoSlotCount = VehicleCargoCapacity.MaxWireSlotCount;

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

    /// <summary>True when no free 1×1 cell remains (not "no free multi-cell footprint").</summary>
    public bool IsFull => !TryFindFirstFreeCargoSlot(1, 1, out _, out _);

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

    /// <summary>
    /// Load cargo rows. Places each item at its stored origin when the footprint fits;
    /// otherwise first-fit reassigns origin (client-parity). Items that cannot fit or have
    /// no legal size are skipped. Returns whether any origin was changed or item dropped
    /// (caller may persist).
    /// </summary>
    public bool LoadItems(IEnumerable<CharacterInventoryItem> items)
    {
        _items.Clear();
        if (items == null)
            return false;

        // Stable order: stored Y, X, then COID so re-pack is deterministic.
        var ordered = items
            .Where(i => i != null)
            .OrderBy(i => i.InventoryPositionY)
            .ThenBy(i => i.InventoryPositionX)
            .ThenBy(i => i.Coid)
            .ToList();

        var occupied = new HashSet<(byte X, byte Y)>();
        var changed = false;

        foreach (var item in ordered)
        {
            if (_items.Any(i => i.Coid == item.Coid))
            {
                changed = true;
                continue;
            }

            ResolveFootprintOrDefault(item.Cbid, out var sizeX, out var sizeY);

            var placed = item;
            if (!InventoryGridPlacement.CanPlace(
                    Width, PageCount, VehicleCargoCapacity.RowsPerPage,
                    occupied, item.InventoryPositionX, item.InventoryPositionY, sizeX, sizeY))
            {
                if (!InventoryGridPlacement.TryFindFirstFree(
                        Width, PageCount, VehicleCargoCapacity.RowsPerPage,
                        occupied, sizeX, sizeY, out var fx, out var fy))
                {
                    Logger.WriteLog(
                        LogType.Error,
                        "LoadItems: dropped coid={0} cbid={1} — no free {2}x{3} footprint",
                        item.Coid, item.Cbid, sizeX, sizeY);
                    changed = true;
                    continue;
                }

                placed = item with { InventoryPositionX = fx, InventoryPositionY = fy };
                changed = true;
            }

            _items.Add(placed);
            foreach (var cell in InventoryGridPlacement.EnumerateCells(
                         placed.InventoryPositionX, placed.InventoryPositionY, sizeX, sizeY))
                occupied.Add((cell.X, cell.Y));
        }

        return changed;
    }

    public int GetOccupiedSlotCount()
    {
        return BuildOccupiedCells(ignoreCoid: null).Count;
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

    /// <summary>
    /// Remove cargo stacks flagged as mission gear (<see cref="CharacterInventoryItem.IsMissionItem"/>).
    /// Optional <paramref name="cbidFilter"/> limits to one CBID (still mission-only).
    /// Sends InventoryDestroyItem (0x2049 bDelete) per stack so client mission inventory updates live.
    /// </summary>
    public InventoryCommandResult RemoveMissionCargo(long characterCoid, int cbidFilter = 0)
    {
        var removedStacks = 0;
        var removedQty = 0;
        var packets = new List<BasePacket>();

        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (!item.IsMissionItem)
                continue;
            if (cbidFilter > 0 && item.Cbid != cbidFilter)
                continue;

            removedStacks++;
            var qty = Math.Max(1, item.Quantity);
            removedQty += qty;
            packets.Add(new InventoryDestroyItemPacket(item.Coid, qty, delete: true, itemGlobal: true));
            _items.RemoveAt(i);
            if (characterCoid != 0)
                PersistCargoDelete(characterCoid, item.Coid);
        }

        packets.Add(InventoryPacketFactory.CreateCargoSendAll(this));

        if (removedStacks == 0)
        {
            var scope = cbidFilter > 0 ? $" for CBID {cbidFilter}" : string.Empty;
            return new InventoryCommandResult(
                $"No mission cargo{scope} to remove.",
                packets);
        }

        var filterNote = cbidFilter > 0 ? $" (CBID {cbidFilter})" : string.Empty;
        return new InventoryCommandResult(
            $"Removed {removedQty} mission cargo unit(s) in {removedStacks} stack(s){filterNote}.",
            packets);
    }

    public string DescribeCargoStatus()
    {
        var occupied = GetOccupiedSlotCount();
        return $"{_items.Count} item(s) loaded, {occupied}/{SlotCount} slots occupied, capacity {Width}x{PageCount}.";
    }

    /// <summary>
    /// First free 1×1 cell (legacy helpers / IsFull). Prefer
    /// <see cref="TryFindFirstFreeCargoSlot(byte, byte, out byte, out byte)"/> for multi-cell items.
    /// </summary>
    public bool TryGetFirstFreeCargoSlot(out byte x, out byte y) =>
        TryFindFirstFreeCargoSlot(1, 1, out x, out y);

    /// <summary>
    /// Client-parity first-fit for a footprint (<c>FUN_005713a0</c>): Y then X.
    /// </summary>
    public bool TryFindFirstFreeCargoSlot(byte sizeX, byte sizeY, out byte x, out byte y)
    {
        var occupied = BuildOccupiedCells(ignoreCoid: null);
        return InventoryGridPlacement.TryFindFirstFree(
            Width, PageCount, VehicleCargoCapacity.RowsPerPage,
            occupied, sizeX, sizeY, out x, out y);
    }

    /// <summary>
    /// First-fit using clonebase footprint for <paramref name="cbid"/>.
    /// Fails when size cannot be resolved (zero/missing InvSize).
    /// </summary>
    public bool TryFindFirstFreeCargoSlotForCbid(int cbid, out byte x, out byte y)
    {
        x = 0;
        y = 0;
        if (!InventoryFootprintPolicy.TryResolve(_cloneBases, cbid, out var sizeX, out var sizeY))
            return false;

        return TryFindFirstFreeCargoSlot(sizeX, sizeY, out x, out y);
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
        movedItem = null;
        var index = _items.FindLastIndex(i => i.Coid == coid);
        if (index < 0)
            return false;

        var item = _items[index];
        ResolveFootprintOrDefault(item.Cbid, out var sizeX, out var sizeY);

        if (!InventoryGridPlacement.CanPlace(
                Width, PageCount, VehicleCargoCapacity.RowsPerPage,
                BuildOccupiedCells(ignoreCoid: coid),
                x, y, sizeX, sizeY))
            return false;

        movedItem = item with
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
        if (item == null)
            return false;

        if (_items.Any(i => i.Coid == item.Coid))
            return false;

        ResolveFootprintOrDefault(item.Cbid, out var sizeX, out var sizeY);

        return InventoryGridPlacement.CanPlace(
            Width, PageCount, VehicleCargoCapacity.RowsPerPage,
            BuildOccupiedCells(ignoreCoid: null),
            item.InventoryPositionX, item.InventoryPositionY, sizeX, sizeY);
    }

    /// <summary>
    /// Occupied cells from item footprints. Unknown/zero size falls back to 1×1 at origin
    /// so legacy loads and tests without clonebase still behave; acquisition paths must
    /// reject unresolved sizes explicitly (see AddItem).
    /// </summary>
    private HashSet<(byte X, byte Y)> BuildOccupiedCells(long? ignoreCoid)
    {
        var occupied = new HashSet<(byte X, byte Y)>();
        foreach (var item in _items)
        {
            if (ignoreCoid.HasValue && item.Coid == ignoreCoid.Value)
                continue;

            ResolveFootprintOrDefault(item.Cbid, out var sizeX, out var sizeY);
            foreach (var cell in InventoryGridPlacement.EnumerateCells(
                         item.InventoryPositionX, item.InventoryPositionY, sizeX, sizeY))
            {
                if (cell.X < Width && cell.Y < PageCount)
                    occupied.Add((cell.X, cell.Y));
            }
        }

        return occupied;
    }

    private void ResolveFootprintOrDefault(int cbid, out byte sizeX, out byte sizeY)
    {
        if (InventoryFootprintPolicy.TryResolve(_cloneBases, cbid, out sizeX, out sizeY))
            return;

        sizeX = 1;
        sizeY = 1;
    }

    /// <summary>
    /// Acquisition footprint: use clonebase InvSize when known and positive.
    /// Reject when clonebase exists but size is invalid (0×0 / non-object).
    /// Unknown CBID (no clonebase) falls back to 1×1 for tests/legacy; live AssetManager
    /// should always provide SimpleObjectSpecific for inventory-capable items.
    /// </summary>
    private bool TryResolveFootprintForAcquisition(CloneBase cloneBase, int cbid, out byte sizeX, out byte sizeY)
    {
        sizeX = 0;
        sizeY = 0;

        if (cloneBase != null)
            return InventoryFootprintPolicy.TryResolve(cloneBase, out sizeX, out sizeY);

        if (InventoryFootprintPolicy.TryResolve(_cloneBases, cbid, out sizeX, out sizeY))
            return true;

        sizeX = 1;
        sizeY = 1;
        return true;
    }

    public InventoryCommandResult AddItem(
        InventoryCatalogEntry entry,
        IInventoryItemCreator itemCreator,
        long coid,
        long characterCoid = 0,
        int quantity = 1,
        Func<long> allocateAdditionalCoid = null)
    {
        return AddItemInternal(entry, itemCreator, coid, characterCoid, quantity, false, allocateAdditionalCoid, "Added");
    }

    private InventoryCommandResult AddItemInternal(
        InventoryCatalogEntry entry,
        IInventoryItemCreator itemCreator,
        long firstCoid,
        long characterCoid,
        int quantity,
        bool isMissionItem,
        Func<long> allocateAdditionalCoid,
        string action)
    {
        if (entry == null || itemCreator == null || quantity < 1)
            return new InventoryCommandResult("Cannot add item: invalid acquisition request.", remainingQuantity: Math.Max(0, quantity));

        var cloneBase = _cloneBases.GetCloneBase(entry.Cbid);
        // Runtime acquisition paths have clonebase metadata. Preserve legacy cargo for
        // unknown records (for example, old persisted test data) rather than splitting
        // an item whose client stack cap cannot be determined.
        var (maxStack, stackable) = cloneBase == null
            ? (int.MaxValue, true)
            : InventoryStackPolicy.GetLimits(cloneBase);
        if (_items.Any(item => item.Coid == firstCoid))
            return new InventoryCommandResult(BuildCargoAddRejectedMessage(firstCoid, 0, 0), remainingQuantity: quantity);
        var remaining = quantity;
        var updates = new List<CharacterInventoryItem>();
        if (stackable)
        {
            foreach (var current in _items
                         .Where(item => item.Cbid == entry.Cbid && item.Quantity < maxStack)
                         .OrderBy(item => item.InventoryPositionY)
                         .ThenBy(item => item.InventoryPositionX))
            {
                var merge = InventoryStackPolicy.ComputeMergeAmount(current.Quantity, remaining, maxStack);
                if (merge <= 0)
                    continue;

                updates.Add(current with { Quantity = current.Quantity + merge });
                remaining -= merge;
                if (remaining == 0)
                    break;
            }
        }

        if (!TryResolveFootprintForAcquisition(cloneBase, entry.Cbid, out var footprintX, out var footprintY))
        {
            // Clonebase present but InvSizeX/Y is zero or non-object — cannot place.
            return new InventoryCommandResult(
                $"Cannot add CBID {entry.Cbid}: inventory footprint is missing or zero (InvSizeX/Y).",
                remainingQuantity: quantity);
        }

        var plannedAdds = new List<(CharacterInventoryItem Item, InventoryItemCreateResult Create)>();
        var usedCoids = _items.Select(item => item.Coid).ToHashSet();
        var occupiedCells = BuildOccupiedCells(ignoreCoid: null);
        // Account for quantity merges already applied in-memory only after loop — occupancy
        // of existing stacks is unchanged; new stack origins need free footprints.
        var nextCoid = firstCoid;
        while (remaining > 0
               && InventoryGridPlacement.TryFindFirstFree(
                   Width, PageCount, VehicleCargoCapacity.RowsPerPage,
                   occupiedCells, footprintX, footprintY, out var x, out var y))
        {
            if (nextCoid <= 0 || !usedCoids.Add(nextCoid))
                break;

            var stackQuantity = stackable ? Math.Min(remaining, maxStack) : 1;
            var create = itemCreator.Create(entry, nextCoid, x, y);
            if (!create.WasSuccessful)
            {
                if (updates.Count == 0 && plannedAdds.Count == 0)
                    return new InventoryCommandResult($"Cannot add CBID {entry.Cbid}: {create.Error}", remainingQuantity: quantity);
                break;
            }

            create.Packet.Quantity = stackQuantity;
            create.Packet.IsInInventory = true;
            create.Packet.PossibleMissionItem = isMissionItem;
            var item = new CharacterInventoryItem(entry.Cbid, entry.Type, create.DisplayName, nextCoid, x, y, stackQuantity, isMissionItem);
            plannedAdds.Add((item, create));
            foreach (var cell in InventoryGridPlacement.EnumerateCells(x, y, footprintX, footprintY))
                occupiedCells.Add((cell.X, cell.Y));
            remaining -= stackQuantity;
            if (remaining > 0)
                nextCoid = allocateAdditionalCoid?.Invoke() ?? 0;
        }

        var packets = new List<BasePacket>();
        foreach (var updated in updates)
        {
            var index = _items.FindIndex(item => item.Coid == updated.Coid);
            _items[index] = updated;
            if (characterCoid != 0)
                PersistCargoUpsert(characterCoid, updated);
            packets.Add(new InventoryAddItemResponsePacket
            {
                ItemCoid = updated.Coid,
                InventoryPositionX = updated.InventoryPositionX,
                InventoryPositionY = updated.InventoryPositionY,
                AddToExistingItem = true,
                Quantity = updated.Quantity,
                WasSuccessful = true
            });
        }

        foreach (var (item, create) in plannedAdds)
        {
            if (!TryAdd(item))
                throw new InvalidOperationException("Planned cargo slot was no longer available.");
            if (characterCoid != 0)
                PersistCargoUpsert(characterCoid, item);
            packets.Add(create.Packet);
            packets.Add(new InventoryAddItemResponsePacket
            {
                ItemCoid = item.Coid,
                InventoryPositionX = item.InventoryPositionX,
                InventoryPositionY = item.InventoryPositionY,
                AddToExistingItem = false,
                Quantity = item.Quantity,
                WasSuccessful = true
            });
        }

        var accepted = quantity - remaining;
        if (accepted == 0)
            return new InventoryCommandResult(BuildCargoFullMessage(), remainingQuantity: quantity);

        packets.Add(InventoryPacketFactory.CreateCargoSendAll(this));
        var representative = plannedAdds.FirstOrDefault().Item ?? updates.FirstOrDefault();
        var quantitySuffix = quantity > 1 ? $" x{accepted}" : string.Empty;
        var remainderSuffix = remaining > 0 ? $" ({remaining} could not fit)" : string.Empty;
        return new InventoryCommandResult(
            $"{action} {representative.DisplayName} ({entry.Cbid}){quantitySuffix} to cargo slot {representative.InventoryPositionX},{representative.InventoryPositionY}.{remainderSuffix}",
            packets,
            representative,
            accepted,
            remaining,
            plannedAdds.Select(add => add.Item).ToArray(),
            updates);
    }

    /// <summary>
    /// Claims world ground loot into cargo with the same client wire sequence as /addItem:
    /// Create (IsInInventory + global TFID) → InventoryAddItemResponse → CargoSendAll.
    /// Caller must allocate a fresh inventory coid and destroy the world entity separately —
    /// reusing the local world TFID with only 0x2047 does not place the item in client cargo.
    /// </summary>
    public InventoryCommandResult PickupWorldItem(
        int cbid,
        CloneBaseObjectType type,
        string displayName,
        long inventoryCoid,
        IInventoryItemCreator itemCreator,
        long characterCoid = 0,
        int quantity = 1)
    {
        if (cbid <= 0)
            return new InventoryCommandResult("Invalid loot CBID.");

        if (itemCreator == null)
            return new InventoryCommandResult("Item creator is required.");

        if (quantity < 1)
            return new InventoryCommandResult("Quantity must be at least 1.");

        if (!InventoryItemTypePolicy.IsInventoryCapable(type))
            return new InventoryCommandResult($"CBID {cbid} ({type}) is not an inventory item.");

        var name = string.IsNullOrWhiteSpace(displayName) ? $"CBID {cbid}" : displayName;
        var entry = new InventoryCatalogEntry(cbid, type, name);
        return AddItem(entry, itemCreator, inventoryCoid, characterCoid, quantity);
    }

    /// <summary>
    /// Total quantity of cargo stacks matching <paramref name="cbid"/>.
    /// </summary>
    public int CountByCbid(int cbid)
    {
        if (cbid <= 0)
            return 0;

        var total = 0;
        foreach (var item in _items)
        {
            if (item.Cbid == cbid)
                total += Math.Max(0, item.Quantity);
        }

        return total;
    }

    /// <summary>
    /// Server-authoritative mission-gear grant into cargo: place first, then Create + 0x2047.
    /// Marks the stack as mission inventory gear for relog PossibleMissionItem restore.
    /// </summary>
    public InventoryCommandResult GrantMissionCargoItem(
        int cbid,
        CloneBaseObjectType type,
        string displayName,
        long coid,
        long characterCoid,
        int quantity = 1,
        IInventoryItemCreator itemCreator = null,
        Func<long> allocateAdditionalCoid = null)
    {
        if (cbid <= 0)
            return new InventoryCommandResult($"Cannot grant mission cargo: invalid CBID {cbid}.");
        if (quantity < 1)
            return new InventoryCommandResult("Cannot grant mission cargo: quantity must be at least 1.");

        return AddItemInternal(
            new InventoryCatalogEntry(
                cbid,
                InventoryItemTypePolicy.IsInventoryCapable(type) ? type : CloneBaseObjectType.Item,
                string.IsNullOrWhiteSpace(displayName) ? $"CBID {cbid}" : displayName),
            itemCreator ?? FallbackInventoryItemCreator.Instance,
            coid,
            characterCoid,
            quantity,
            isMissionItem: true,
            allocateAdditionalCoid,
            "Granted mission cargo");

        // Persist before client packets so a DB failure is logged with the grant attempt.
        // Still deliver live packets if persist fails — login ensure will re-grant.
        #if false
        else
        {
            persistOk = false;
            Logger.WriteLog(LogType.Error,
                "GrantMissionCargoItem: characterCoid=0 — cargo not persisted (cbid={0} itemCoid={1})",
                cbid,
                coid);
        }

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            createPacket,
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
        var persistNote = persistOk ? string.Empty : " (NOT PERSISTED)";
        return new InventoryCommandResult(
            $"Granted mission cargo {name} ({cbid}){quantitySuffix} at {x},{y}.{persistNote}",
            packets,
            item);
        #endif
    }

    /// <summary>
    /// Removes up to <paramref name="quantity"/> of cargo stacks matching <paramref name="cbid"/>.
    /// Prefers mission-item stacks, then any remaining matching CBID.
    /// Emits S2C <see cref="InventoryDestroyItemPacket"/> (0x2049, bDelete) per removed stack so
    /// mission inventory / object UI clears without relog.
    /// </summary>
    public InventoryCommandResult RemoveCargoByCbid(long characterCoid, int cbid, int quantity)
    {
        if (cbid <= 0 || quantity < 1)
        {
            return new InventoryCommandResult(
                "No cargo removed.",
                new BasePacket[] { InventoryPacketFactory.CreateCargoSendAll(this) });
        }

        var remaining = quantity;
        var removedCoids = new List<long>();
        var removedQtys = new List<int>();

        // Prefer mission gear stacks first (deliver TakeItemAtEnd).
        remaining = RemoveMatchingStacks(
            characterCoid, cbid, remaining, missionOnly: true, removedCoids, removedQtys);
        if (remaining > 0)
        {
            remaining = RemoveMatchingStacks(
                characterCoid, cbid, remaining, missionOnly: false, removedCoids, removedQtys);
        }

        var removedQty = quantity - remaining;
        var packets = new List<BasePacket>();
        for (var i = 0; i < removedCoids.Count; i++)
        {
            packets.Add(new InventoryDestroyItemPacket(
                removedCoids[i],
                quantity: removedQtys[i],
                delete: true,
                itemGlobal: true));
        }

        packets.Add(InventoryPacketFactory.CreateCargoSendAll(this));

        return new InventoryCommandResult(
            $"Removed {removedQty} of CBID {cbid} from cargo ({removedCoids.Count} stack(s)).",
            packets);
    }

    private sealed class FallbackInventoryItemCreator : IInventoryItemCreator
    {
        public static FallbackInventoryItemCreator Instance { get; } = new();

        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
        {
            var packet = InventoryItemCreator.CreatePacketFor(entry.Type);
            packet.CBID = entry.Cbid;
            packet.ObjectId = new TFID { Coid = coid, Global = true };
            packet.InventoryPositionX = x;
            packet.InventoryPositionY = y;
            packet.IsInInventory = true;
            packet.IsIdentified = true;
            packet.IsBound = false;
            return InventoryItemCreateResult.Success(packet, entry.DisplayName);
        }
    }

    private int RemoveMatchingStacks(
        long characterCoid,
        int cbid,
        int remaining,
        bool missionOnly,
        List<long> removedCoids,
        List<int> removedQtys = null)
    {
        if (remaining < 1)
            return remaining;

        for (var i = _items.Count - 1; i >= 0 && remaining > 0; i--)
        {
            var item = _items[i];
            if (item.Cbid != cbid)
                continue;
            if (missionOnly && !item.IsMissionItem)
                continue;
            if (!missionOnly && item.IsMissionItem)
                continue;

            if (item.Quantity > remaining)
            {
                var reduced = item with { Quantity = item.Quantity - remaining };
                _items[i] = reduced;
                if (characterCoid != 0)
                    PersistCargoUpsert(characterCoid, reduced);
                // Partial stack reduce: still notify client destroy qty for removed amount.
                removedCoids.Add(item.Coid);
                removedQtys?.Add(remaining);
                remaining = 0;
                break;
            }

            var stackQty = Math.Max(1, item.Quantity);
            remaining -= stackQty;
            removedCoids.Add(item.Coid);
            removedQtys?.Add(stackQty);
            _items.RemoveAt(i);
            if (characterCoid != 0)
                PersistCargoDelete(characterCoid, item.Coid);
        }

        return remaining;
    }

    public IReadOnlyList<BasePacket> CreateItemObjectPackets(InventoryCatalog catalog, IInventoryItemCreator itemCreator)
    {
        var packets = new List<BasePacket>();
        foreach (var item in Items.OrderBy(i => i.InventoryPositionY).ThenBy(i => i.InventoryPositionX))
        {
            var entry = catalog?.FindAny(item.Cbid);
            var type = entry?.Type ?? item.Type;
            if (entry == null)
            {
                if (!InventoryItemTypePolicy.IsInventoryCapable(item.Type) && !item.IsMissionItem)
                    continue;

                entry = new InventoryCatalogEntry(item.Cbid, type, item.DisplayName);
            }
            else if (!InventoryItemTypePolicy.IsInventoryCapable(entry.Type) && !item.IsMissionItem)
            {
                continue;
            }

            CreateSimpleObjectPacket createPacket = null;
            if (itemCreator != null)
            {
                var createResult = itemCreator.Create(entry, item.Coid, item.InventoryPositionX, item.InventoryPositionY);
                if (createResult.WasSuccessful)
                    createPacket = createResult.Packet;
            }

            // Login restore must not drop cargo when the typed factory lacks a clonebase path
            // (historical QuestObject gap). Mission gear and plain items get a minimal create.
            createPacket ??= BuildFallbackInventoryCreatePacket(item, type);

            createPacket.Quantity = item.Quantity;
            createPacket.IsInInventory = true;
            createPacket.IsIdentified = true;
            if (item.IsMissionItem)
                createPacket.PossibleMissionItem = true;
            packets.Add(createPacket);
        }

        return packets;
    }

    /// <summary>
    /// Minimal CreateSimpleObject for cargo login restore when AllocateNewObjectFromCBID fails.
    /// </summary>
    private static CreateSimpleObjectPacket BuildFallbackInventoryCreatePacket(
        CharacterInventoryItem item,
        CloneBaseObjectType type)
    {
        var packet = InventoryItemCreator.CreatePacketFor(type);
        packet.CBID = item.Cbid;
        packet.ObjectId = new TFID { Coid = item.Coid, Global = true };
        packet.InventoryPositionX = item.InventoryPositionX;
        packet.InventoryPositionY = item.InventoryPositionY;
        packet.Quantity = item.Quantity;
        packet.IsInInventory = true;
        packet.IsIdentified = true;
        packet.IsBound = false;
        packet.PossibleMissionItem = item.IsMissionItem;
        return packet;
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

        return new InventoryOperationResult(
            new BasePacket[]
            {
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
                InventoryPacketFactory.CreateCargoSendAll(this)
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

    public InventoryOperationResult TossToWorld(ItemDropPacket packet, Character character)
    {
        if (character == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateItemDropFailure(packet),
                "HandleItemDropPacket: Character is null");
        }

        if (packet.RawBytes.Length < ItemDropPacket.MinimumLength)
        {
            return InventoryOperationResult.SinglePacket(
                CreateItemDropFailure(packet),
                $"HandleItemDropPacket: Packet too short ({packet.RawBytes.Length} bytes, expected {ItemDropPacket.MinimumLength})");
        }

        var map = character.Map;
        if (map == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateItemDropFailure(packet),
                "HandleItemDropPacket: Character has no map");
        }

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
        {
            return InventoryOperationResult.SinglePacket(
                CreateItemDropFailure(packet),
                "HandleItemDropPacket: Character has no vehicle");
        }

        var cargoItem = FindByCoid(packet.ItemCoid);
        if (cargoItem == null)
        {
            if (_pendingEquippedItemDrags.TryGetValue(packet.ItemCoid, out var pendingEquipped))
                return TossPendingEquippedItem(packet, character, pendingEquipped);

            return InventoryOperationResult.SinglePacket(
                CreateItemDropFailure(packet),
                $"HandleItemDropPacket: Cargo item {packet.ItemCoid} not found");
        }

        _items.Remove(cargoItem);
        PersistCargoDelete(character.ObjectId.Coid, cargoItem.Coid);

        // #region agent log
        TossDebugLogger.Log(
            "H6",
            "InventoryManager.TossToWorld:inventory-only",
            "toss deletes cargo only; no world spawn",
            new { cargoCbid = cargoItem.Cbid, cargoCoid = packet.ItemCoid, cbid = cargoItem.Cbid },
            "post-fix");
        // #endregion

        var packets = new List<BasePacket>
        {
            new ItemDropResponsePacket
            {
                SourceObjectId = packet.SourceObjectId,
                ItemCoid = packet.ItemCoid,
                DropPosition = packet.DropPosition,
                TailValue = packet.TailValue,
                WasSuccessful = true
            },
            InventoryPacketFactory.CreateCargoSendAll(this)
        };

        return new InventoryOperationResult(
            packets,
            $"HandleItemDropPacket: Player {character.Name} tossed {cargoItem.DisplayName} (CBID {cargoItem.Cbid}, cargo coid={cargoItem.Coid}) — removed from cargo (no world spawn)");
    }

    private InventoryOperationResult TossPendingEquippedItem(
        ItemDropPacket packet,
        Character character,
        PendingEquippedItemDrag pendingEquipped)
    {
        if (pendingEquipped.Vehicle != character.CurrentVehicle)
        {
            return InventoryOperationResult.SinglePacket(
                CreateItemDropFailure(packet),
                $"HandleItemDropPacket: Pending equipped item {packet.ItemCoid} belongs to another vehicle");
        }

        if (!pendingEquipped.AlreadyUnequipped)
        {
            if (!pendingEquipped.Vehicle.TryUnequipItem(packet.ItemCoid, out _, out _))
            {
                return InventoryOperationResult.SinglePacket(
                    CreateItemDropFailure(packet),
                    $"HandleItemDropPacket: Equipped item {packet.ItemCoid} not found on vehicle during toss");
            }

            PersistUnequip(pendingEquipped.Vehicle);
        }

        _pendingEquippedItemDrags.Remove(packet.ItemCoid);

        // #region agent log
        TossDebugLogger.Log(
            "H7",
            "InventoryManager.TossPendingEquippedItem",
            "equipped module toss deleted pending drag",
            new
            {
                itemCoid = packet.ItemCoid,
                cbid = pendingEquipped.Cbid,
                slot = pendingEquipped.Slot.ToString(),
                sourceObjectId = packet.SourceObjectId
            },
            "post-fix");
        // #endregion

        return new InventoryOperationResult(
            new List<BasePacket>
            {
                new ItemDropResponsePacket
                {
                    SourceObjectId = packet.SourceObjectId,
                    ItemCoid = packet.ItemCoid,
                    DropPosition = packet.DropPosition,
                    TailValue = packet.TailValue,
                    WasSuccessful = true
                }
            },
            $"HandleItemDropPacket: Player {character.Name} tossed equipped {pendingEquipped.DisplayName} (CBID {pendingEquipped.Cbid}, coid={packet.ItemCoid}, slot={pendingEquipped.Slot}) — deleted (no world spawn)");
    }

    public static ItemDropResponsePacket CreateItemDropFailure(ItemDropPacket packet)
    {
        return new ItemDropResponsePacket
        {
            SourceObjectId = packet.SourceObjectId,
            ItemCoid = packet.ItemCoid,
            DropPosition = packet.DropPosition,
            TailValue = packet.TailValue,
            WasSuccessful = false
        };
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

    /// <summary>
    /// Apply a signed credit delta for a character, persist absolute balance, and build a
    /// client <see cref="GiveCreditsPacket"/> for the applied delta (0x205E is additive on the client).
    /// Negative deltas floor at zero unless <paramref name="allowDebt"/> is true.
    /// </summary>
    public AddCreditsResult AddCredits(Character character, long amount, bool allowDebt = false) =>
        CurrencySync.AddCredits(_persistence, character, amount, allowDebt);

    /// <summary>Set absolute credits, persist, and return a CharacterLevel-friendly absolute value.</summary>
    public long SetCreditsAbsolute(Character character, long absoluteCredits, bool allowDebt = false) =>
        CurrencySync.SetCreditsAbsolute(_persistence, character, absoluteCredits, allowDebt);

    public void ReloadCargo(long characterCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        var changed = LoadItems(_persistence.LoadCargo(characterCoid));
        if (changed)
            PersistRepackedCargo(characterCoid);
    }

    /// <summary>
    /// After load-time re-pack, rewrite all cargo rows to match runtime origins
    /// (and drop rows for items that could not fit).
    /// </summary>
    public void PersistRepackedCargo(long characterCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        try
        {
            _persistence.ClearCargo(characterCoid);
            foreach (var item in _items)
                _persistence.UpsertCargo(characterCoid, item);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "PersistRepackedCargo failed char={0}: {1}",
                characterCoid,
                ex.Message);
        }
    }

    private void PersistCargoUpsert(long characterCoid, CharacterInventoryItem item)
    {
        if (_persistence == null || characterCoid == 0 || item == null)
            return;

        try
        {
            _persistence.UpsertCargo(characterCoid, item);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "PersistCargoUpsert failed char={0} coid={1} cbid={2}: {3}",
                characterCoid,
                item.Coid,
                item.Cbid,
                ex.Message);
        }
    }

    private void PersistCargoMove(long characterCoid, CharacterInventoryItem item)
    {
        if (_persistence == null || characterCoid == 0 || item == null)
            return;

        try
        {
            _persistence.MoveCargo(characterCoid, item);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "PersistCargoMove failed char={0} coid={1}: {2}",
                characterCoid,
                item.Coid,
                ex.Message);
        }
    }

    private void PersistCargoDelete(long characterCoid, long itemCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        try
        {
            _persistence.DeleteCargo(characterCoid, itemCoid);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "PersistCargoDelete failed char={0} coid={1}: {2}",
                characterCoid,
                itemCoid,
                ex.Message);
        }
    }

    private void PersistCargoClear(long characterCoid)
    {
        if (_persistence == null || characterCoid == 0)
            return;

        try
        {
            _persistence.ClearCargo(characterCoid);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "PersistCargoClear failed char={0}: {1}",
                characterCoid,
                ex.Message);
        }
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

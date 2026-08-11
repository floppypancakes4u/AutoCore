using System.Diagnostics.CodeAnalysis;

namespace AutoCore.Game.Inventory;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Utils;
using AutoCore.Utils.Logging;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF-backed inventory persistence. Opens a short-lived <see cref="CharContext"/> per call.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "EF CharContext I/O; unit tests inject IInventoryPersistence fakes (RecordingInventoryPersistence).")]
public sealed class InventoryPersistence : IInventoryPersistence
{
    public static InventoryPersistence Instance { get; } = new();

    /// <summary>Context factory (tests inject InMemory).</summary>
    internal static Func<CharContext> CreateContext { get; set; } = static () => new CharContext();

    /// <summary>Restore the default context factory after tests.</summary>
    internal static void ResetForTests()
    {
        CreateContext = static () => new CharContext();
        Interlocked.Exchange(ref _ss31OverwriteRefusedCount, 0);
    }

    private static long _ss31OverwriteRefusedCount;

    /// <summary>Times EnsureSimpleObject refused a cross-category coid overwrite (SS-31).</summary>
    public static long Ss31OverwriteRefusedCount => Interlocked.Read(ref _ss31OverwriteRefusedCount);

    private const byte InventoryTypeCargo = 1;
    private const byte InventoryTypeLocker = 3;

    public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) =>
        LoadByInventoryType(characterCoid, InventoryTypeCargo);

    public IReadOnlyList<CharacterInventoryItem> LoadLocker(long characterCoid) =>
        LoadByInventoryType(characterCoid, InventoryTypeLocker);

    private static IReadOnlyList<CharacterInventoryItem> LoadByInventoryType(long characterCoid, byte inventoryType)
    {
        using var context = CreateContext();
        // Ensure optional columns exist before materializing (older DBs).
        context.EnsureInventorySchema();
        return context.CharacterInventories
            .AsNoTracking()
            .Where(i => i.CharacterCoid == characterCoid && i.InventoryType == inventoryType)
            .OrderBy(i => i.SlotY)
            .ThenBy(i => i.SlotX)
            .Select(i => new CharacterInventoryItem(
                i.Cbid,
                (CloneBaseObjectType)i.Type,
                $"CBID {i.Cbid}",
                i.ItemCoid,
                i.SlotX,
                i.SlotY,
                i.Quantity,
                i.IsMissionItem))
            .ToList();
    }

    public void UpsertCargo(long characterCoid, CharacterInventoryItem item) =>
        UpsertInventoryItem(characterCoid, item, InventoryTypeCargo, "UpsertCargo");

    public void UpsertLocker(long characterCoid, CharacterInventoryItem item) =>
        UpsertInventoryItem(characterCoid, item, InventoryTypeLocker, "UpsertLocker");

    private void UpsertInventoryItem(
        long characterCoid,
        CharacterInventoryItem item,
        byte inventoryType,
        string operation)
    {
        if (item == null)
            return;

        using var context = CreateContext();
        context.EnsureInventorySchema();
        var row = context.CharacterInventories.FirstOrDefault(i => i.ItemCoid == item.Coid);
        if (row == null)
        {
            context.CharacterInventories.Add(new CharacterInventoryData
            {
                CharacterCoid = characterCoid,
                ItemCoid = item.Coid,
                Cbid = item.Cbid,
                Type = (byte)item.Type,
                SlotX = item.InventoryPositionX,
                SlotY = item.InventoryPositionY,
                Quantity = Math.Max(1, item.Quantity),
                InventoryType = inventoryType,
                IsMissionItem = item.IsMissionItem
            });
        }
        else
        {
            row.CharacterCoid = characterCoid;
            row.Cbid = item.Cbid;
            row.Type = (byte)item.Type;
            row.SlotX = item.InventoryPositionX;
            row.SlotY = item.InventoryPositionY;
            row.Quantity = Math.Max(1, item.Quantity);
            row.InventoryType = inventoryType;
            row.IsMissionItem = item.IsMissionItem;
        }

        EnsureSimpleObjectInternal(context, item.Coid, (byte)item.Type, item.Cbid);
        Save(context, $"{operation} character={characterCoid} item={item.Coid}");
    }

    public void MoveCargo(long characterCoid, CharacterInventoryItem item) =>
        MoveInventoryItem(characterCoid, item, InventoryTypeCargo, "MoveCargo");

    public void MoveLocker(long characterCoid, CharacterInventoryItem item) =>
        MoveInventoryItem(characterCoid, item, InventoryTypeLocker, "MoveLocker");

    private void MoveInventoryItem(
        long characterCoid,
        CharacterInventoryItem item,
        byte inventoryType,
        string operation)
    {
        if (item == null)
            return;

        using var context = CreateContext();
        context.EnsureInventorySchema();
        var row = context.CharacterInventories.FirstOrDefault(i => i.ItemCoid == item.Coid);
        if (row == null)
        {
            UpsertInventoryItem(characterCoid, item, inventoryType, operation);
            return;
        }

        row.SlotX = item.InventoryPositionX;
        row.SlotY = item.InventoryPositionY;
        row.Quantity = Math.Max(1, item.Quantity);
        row.InventoryType = inventoryType;
        Save(context, $"{operation} character={characterCoid} item={item.Coid}");
    }

    public void DeleteCargo(long characterCoid, long itemCoid) =>
        DeleteInventoryItem(characterCoid, itemCoid, InventoryTypeCargo, "DeleteCargo");

    public void DeleteLocker(long characterCoid, long itemCoid) =>
        DeleteInventoryItem(characterCoid, itemCoid, InventoryTypeLocker, "DeleteLocker");

    private static void DeleteInventoryItem(
        long characterCoid,
        long itemCoid,
        byte inventoryType,
        string operation)
    {
        using var context = CreateContext();
        context.EnsureInventorySchema();
        var rows = context.CharacterInventories
            .Where(i => i.CharacterCoid == characterCoid
                        && i.ItemCoid == itemCoid
                        && i.InventoryType == inventoryType)
            .ToList();
        if (rows.Count == 0)
            return;

        context.CharacterInventories.RemoveRange(rows);
        Save(context, $"{operation} character={characterCoid} item={itemCoid}");
    }

    public void ClearCargo(long characterCoid)
    {
        using var context = CreateContext();
        context.EnsureInventorySchema();
        var rows = context.CharacterInventories
            .Where(i => i.CharacterCoid == characterCoid && i.InventoryType == InventoryTypeCargo)
            .ToList();
        if (rows.Count == 0)
            return;

        context.CharacterInventories.RemoveRange(rows);
        Save(context, $"ClearCargo character={characterCoid} count={rows.Count}");
    }

    public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0)
    {
        using var context = CreateContext();
        EnsureSimpleObjectInternal(context, itemCoid, type, cbid, faction, teamFaction);
        Save(context, $"EnsureSimpleObject item={itemCoid}");
    }

    public void ReleaseUnusedPlaceholder(long coid)
    {
        using var context = CreateContext();
        var existing = context.SimpleObjects.FirstOrDefault(so => so.Coid == coid);
        if (existing == null)
            return;

        // Only ever remove a still-unfilled allocator placeholder. A row with a real identity
        // (Type/CBID already set) must never be touched here — that would be a live object.
        if (existing.Type != 0 || existing.CBID != 0)
            return;

        context.SimpleObjects.Remove(existing);
        Save(context, $"ReleaseUnusedPlaceholder coid={coid}");
    }

    public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot)
    {
        using var context = CreateContext();
        var vehicle = context.Vehicles.FirstOrDefault(v => v.Coid == vehicleCoid);
        if (vehicle == null)
        {
            Logger.WriteLog(LogType.Error, $"SaveVehicleEquipment: vehicle {vehicleCoid} not found");
            return;
        }

        vehicle.Ornament = snapshot.Ornament;
        vehicle.RaceItem = snapshot.RaceItem;
        vehicle.PowerPlant = snapshot.PowerPlant;
        vehicle.Wheelset = snapshot.Wheelset;
        vehicle.Armor = snapshot.Armor;
        vehicle.MeleeWeapon = snapshot.MeleeWeapon;
        vehicle.Front = snapshot.Front;
        vehicle.Turret = snapshot.Turret;
        vehicle.Rear = snapshot.Rear;
        Save(context, $"SaveVehicleEquipment vehicle={vehicleCoid}");
    }

    public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount)
    {
        using var context = CreateContext();
        var character = context.Characters.FirstOrDefault(c => c.Coid == characterCoid);
        if (character == null)
        {
            Logger.WriteLog(LogType.Error, $"SaveCharacterCargoCapacity: character {characterCoid} not found");
            return;
        }

        character.CargoWidth = width;
        character.CargoPageCount = pageCount;
        Save(context, $"SaveCharacterCargoCapacity character={characterCoid} {width}x{pageCount}");
    }

    public long LoadCredits(long characterCoid)
    {
        using var context = CreateContext();
        var character = context.Characters.AsNoTracking().FirstOrDefault(c => c.Coid == characterCoid);
        return character?.Credits ?? 0L;
    }

    public void SaveCredits(long characterCoid, long credits)
    {
        using var context = CreateContext();
        var character = context.Characters.FirstOrDefault(c => c.Coid == characterCoid);
        if (character == null)
        {
            // Fail loud so /currency surfaces a failure instead of silently dropping balance.
            throw new InvalidOperationException(
                $"SaveCredits: character {characterCoid} not found; balance not saved");
        }

        character.Credits = credits;
        Save(context, $"SaveCredits character={characterCoid} credits={credits}");
    }

    private static void EnsureSimpleObjectInternal(
        CharContext context,
        long itemCoid,
        byte type,
        int cbid,
        int faction = 0,
        int teamFaction = 0)
    {
        var existing = context.SimpleObjects.FirstOrDefault(so => so.Coid == itemCoid);
        if (existing != null)
        {
            // SS-31: only fill an allocator placeholder (Type == 0) or refresh the same identity
            // (same Type and CBID) at this coid. No production path legitimately rewrites CBID
            // or object category under an existing coid; an allocator that minted a colliding id
            // would otherwise corrupt that row (client AVs at character select). Refuse loudly.
            var isPlaceholder = existing.Type == 0;
            var isSameIdentity = existing.Type == type && existing.CBID == cbid;
            if (!isPlaceholder && !isSameIdentity)
            {
                Interlocked.Increment(ref _ss31OverwriteRefusedCount);
                throw new InvalidOperationException(
                    $"EnsureSimpleObject: coid {itemCoid} already belongs to object type {existing.Type} " +
                    $"(cbid {existing.CBID}); refusing to overwrite with type {type} (cbid {cbid}) — SS-31 coid collision.");
            }

            existing.Type = type;
            existing.CBID = cbid;
            if (faction != 0)
                existing.Faction = faction;
            if (teamFaction != 0)
                existing.TeamFaction = teamFaction;
            return;
        }

        // simple_object.Coid is identity-generated for character creation; cargo/equip
        // items reuse server-allocated coids, so insert with an explicit primary key.
        context.Database.ExecuteSqlRaw(
            """
            INSERT INTO `simple_object` (`Coid`, `Type`, `CBID`, `Faction`, `TeamFaction`)
            VALUES ({0}, {1}, {2}, {3}, {4})
            """,
            itemCoid,
            type,
            cbid,
            faction,
            teamFaction);
    }

    private static void Save(CharContext context, string operation)
    {
        try
        {
            DbOperationTiming.Run(operation, () => context.SaveChanges());
        }
        catch (Exception ex)
        {
            Logger.WriteException(LogType.Error, $"{operation}", ex);
            throw;
        }
    }
}

namespace AutoCore.Game.Inventory;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Utils;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF-backed inventory persistence. Opens a short-lived <see cref="CharContext"/> per call.
/// </summary>
public sealed class InventoryPersistence : IInventoryPersistence
{
    public static InventoryPersistence Instance { get; } = new();

    public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid)
    {
        using var context = new CharContext();
        return context.CharacterInventories
            .AsNoTracking()
            .Where(i => i.CharacterCoid == characterCoid)
            .OrderBy(i => i.SlotY)
            .ThenBy(i => i.SlotX)
            .Select(i => new CharacterInventoryItem(
                i.Cbid,
                (CloneBaseObjectType)i.Type,
                $"CBID {i.Cbid}",
                i.ItemCoid,
                i.SlotX,
                i.SlotY,
                i.Quantity))
            .ToList();
    }

    public void UpsertCargo(long characterCoid, CharacterInventoryItem item)
    {
        if (item == null)
            return;

        using var context = new CharContext();
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
                Quantity = Math.Max(1, item.Quantity)
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
        }

        EnsureSimpleObjectInternal(context, item.Coid, (byte)item.Type, item.Cbid);
        Save(context, $"UpsertCargo character={characterCoid} item={item.Coid}");
    }

    public void MoveCargo(long characterCoid, CharacterInventoryItem item)
    {
        if (item == null)
            return;

        using var context = new CharContext();
        var row = context.CharacterInventories.FirstOrDefault(i => i.ItemCoid == item.Coid);
        if (row == null)
        {
            UpsertCargo(characterCoid, item);
            return;
        }

        row.SlotX = item.InventoryPositionX;
        row.SlotY = item.InventoryPositionY;
        row.Quantity = Math.Max(1, item.Quantity);
        Save(context, $"MoveCargo character={characterCoid} item={item.Coid}");
    }

    public void DeleteCargo(long characterCoid, long itemCoid)
    {
        using var context = new CharContext();
        var rows = context.CharacterInventories
            .Where(i => i.CharacterCoid == characterCoid && i.ItemCoid == itemCoid)
            .ToList();
        if (rows.Count == 0)
            return;

        context.CharacterInventories.RemoveRange(rows);
        Save(context, $"DeleteCargo character={characterCoid} item={itemCoid}");
    }

    public void ClearCargo(long characterCoid)
    {
        using var context = new CharContext();
        var rows = context.CharacterInventories
            .Where(i => i.CharacterCoid == characterCoid)
            .ToList();
        if (rows.Count == 0)
            return;

        context.CharacterInventories.RemoveRange(rows);
        Save(context, $"ClearCargo character={characterCoid} count={rows.Count}");
    }

    public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0)
    {
        using var context = new CharContext();
        EnsureSimpleObjectInternal(context, itemCoid, type, cbid, faction, teamFaction);
        Save(context, $"EnsureSimpleObject item={itemCoid}");
    }

    public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot)
    {
        using var context = new CharContext();
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
        using var context = new CharContext();
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
        using var context = new CharContext();
        var character = context.Characters.AsNoTracking().FirstOrDefault(c => c.Coid == characterCoid);
        return character?.Credits ?? 0L;
    }

    public void SaveCredits(long characterCoid, long credits)
    {
        using var context = new CharContext();
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
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"{operation} failed: {ex.Message}");
            throw;
        }
    }
}

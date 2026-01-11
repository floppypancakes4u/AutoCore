namespace AutoCore.Game.Managers;

using System;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Login;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils.Memory;

public class CharacterSelectionManager : Singleton<CharacterSelectionManager>
{
    public static (bool, long) CreateNewCharacter(TNLConnection client, LoginNewCharacterPacket packet)
    {
        using var context = new CharContext();

        // Normalize the input names (trim and convert to lowercase for comparison)
        var normalizedCharacterName = packet.CharacterName?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedVehicleName = packet.VehicleName?.Trim().ToLowerInvariant() ?? string.Empty;

        // Log what we're checking
        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Checking character name '{packet.CharacterName}' (normalized: '{normalizedCharacterName}') for account {client.Account.Id}");

        // Get all existing character names (non-deleted) for debugging
        var existingCharacterNames = context.Characters
            .Where(c => !c.Deleted)
            .Select(c => c.Name)
            .ToList();
        
        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Found {existingCharacterNames.Count} non-deleted characters. Names: [{string.Join(", ", existingCharacterNames)}]");

        // Check for existing character name (case-insensitive, excluding deleted characters)
        // Use ToList() first to ensure we're doing the comparison in memory, not in SQL
        var matchingCharacter = context.Characters
            .Where(c => !c.Deleted)
            .ToList()
            .FirstOrDefault(c => c.Name.Trim().ToLowerInvariant() == normalizedCharacterName);
        
        if (matchingCharacter != null)
        {
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Character name '{packet.CharacterName}' conflicts with existing character '{matchingCharacter.Name}' (Coid: {matchingCharacter.Coid}, AccountId: {matchingCharacter.AccountId}, Deleted: {matchingCharacter.Deleted})");
            return (false, -1);
        }

        // Check for existing vehicle name (case-insensitive)
        // Note: Vehicles don't have a Deleted flag, but we check against vehicles from non-deleted characters
        var existingVehicleNames = context.Vehicles
            .Join(context.Characters.Where(c => !c.Deleted), 
                v => v.CharacterCoid, 
                c => c.Coid, 
                (v, c) => v.Name)
            .ToList();
        
        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Found {existingVehicleNames.Count} vehicle names from non-deleted characters. Names: [{string.Join(", ", existingVehicleNames)}]");

        var existingVehicle = context.Vehicles
            .Join(context.Characters.Where(c => !c.Deleted), 
                v => v.CharacterCoid, 
                c => c.Coid, 
                (v, c) => v)
            .ToList()
            .FirstOrDefault(v => v.Name.Trim().ToLowerInvariant() == normalizedVehicleName);
        
        if (existingVehicle != null)
        {
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Vehicle name '{packet.VehicleName}' conflicts with existing vehicle '{existingVehicle.Name}' (Coid: {existingVehicle.Coid}, CharacterCoid: {existingVehicle.CharacterCoid})");
            return (false, -1);
        }

        var cloneBaseCharacter = AssetManager.Instance.GetCloneBase<CloneBaseCharacter>(packet.CBID);
        ConfigNewCharacter configNewCharacter = null;
        
        if (cloneBaseCharacter == null)
        {
            // Get available character CBIDs for debugging
            var availableCharacterCBIDs = AssetManager.Instance.GetAllCharacterCBIDs();
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Invalid CBID {packet.CBID} - cloneBaseCharacter not found");
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Available character CBIDs: [{string.Join(", ", availableCharacterCBIDs)}]");
            
            // Check if bypass is enabled
            if (!AssetManager.Instance.AllowMissingCBID)
            {
                return (false, -1);
            }
            
            // Bypass enabled - use default race/class values
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Bypassing CBID validation (AllowMissingCBID=true). Using CBID {packet.CBID} with default values.");
            
            // Try to find any valid config - try common race/class combinations
            configNewCharacter = AssetManager.Instance.GetConfigNewCharacterFor(0, 0) ??  // Human Berserker
                                AssetManager.Instance.GetConfigNewCharacterFor(1, 0) ??  // Mutant Berserker
                                AssetManager.Instance.GetConfigNewCharacterFor(0, 1) ??  // Human Biker
                                AssetManager.Instance.GetConfigNewCharacterFor(1, 1) ??  // Mutant Biker
                                AssetManager.Instance.GetConfigNewCharacterFor(2, 0) ??  // Biomek Berserker
                                AssetManager.Instance.GetConfigNewCharacterFor(2, 1);    // Biomek Biker
            
            if (configNewCharacter == null)
            {
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, "CreateNewCharacter: Cannot bypass - no default character config found in database");
                return (false, -1);
            }
            
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Using default config - Race: {configNewCharacter.Race}, Class: {configNewCharacter.Class}");
        }
        else
        {
            configNewCharacter = AssetManager.Instance.GetConfigNewCharacterFor(cloneBaseCharacter.CharacterSpecific.Race, cloneBaseCharacter.CharacterSpecific.Class);
            if (configNewCharacter == null)
            {
                var availableConfigs = AssetManager.Instance.GetAllAvailableConfigs();
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: No config found for Race {cloneBaseCharacter.CharacterSpecific.Race}, Class {cloneBaseCharacter.CharacterSpecific.Class}");
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Available configs in database: [{string.Join(", ", availableConfigs)}]");
                
                // Try to find a fallback config
                configNewCharacter = AssetManager.Instance.GetConfigNewCharacterFallback(cloneBaseCharacter.CharacterSpecific.Race, cloneBaseCharacter.CharacterSpecific.Class);
                if (configNewCharacter != null)
                {
                    AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Using fallback config - Race: {configNewCharacter.Race}, Class: {configNewCharacter.Class} (requested Race: {cloneBaseCharacter.CharacterSpecific.Race}, Class: {cloneBaseCharacter.CharacterSpecific.Class})");
                }
                else
                {
                    AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, "CreateNewCharacter: No fallback config available - database appears to be empty");
                    AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, "CreateNewCharacter: Attempting to generate config from game data...");
                    
                    // Try to generate a config from game data
                    configNewCharacter = AssetManager.Instance.GenerateConfigFromGameData(cloneBaseCharacter.CharacterSpecific.Race, cloneBaseCharacter.CharacterSpecific.Class);
                    if (configNewCharacter != null)
                    {
                        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Successfully generated config from game data - Race: {configNewCharacter.Race}, Class: {configNewCharacter.Class}");
                    }
                    else
                    {
                        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, "CreateNewCharacter: Failed to generate config from game data");
                        return (false, -1);
                    }
                }
            }
        }

        var starterMapData = AssetManager.Instance.GetMapData(configNewCharacter.StartTown);
        if (starterMapData == null)
        {
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: StartTown {configNewCharacter.StartTown} map data not found");
            return (false, -1);
        }

        var characterSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Character,
            CBID = packet.CBID
        };

        var vehicleSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Vehicle,
            CBID = configNewCharacter.Vehicle
        };

        var wheelSetSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.WheelSet,
            CBID = packet.WheelsetCBID
        };

        var powerPlantSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.PowerPlant,
            CBID = configNewCharacter.PowerPlant
        };

        var armorSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Armor,
            CBID = configNewCharacter.Armor
        };

        var raceItemSimpleObject = new SimpleObjectData
        {
            // In `wad.xml` (tConfigNewCharacters), CBIDRaceItem points to a CloneBase with `intType=6` (Item),
            // e.g. CBID 5782 (Human) is `intType=6`. So we store it as an Item simpleobject.
            Type = (byte)CloneBaseObjectType.Item,
            CBID = configNewCharacter.RaceItem
        };

        var turretSimpleObject = new SimpleObjectData
        {
            Type = (byte)CloneBaseObjectType.Weapon,
            CBID = configNewCharacter.Weapon
        };

        context.SimpleObjects.Add(characterSimpleObject);
        context.SimpleObjects.Add(vehicleSimpleObject);
        context.SimpleObjects.Add(wheelSetSimpleObject);
        context.SimpleObjects.Add(powerPlantSimpleObject);
        context.SimpleObjects.Add(armorSimpleObject);
        context.SimpleObjects.Add(raceItemSimpleObject);
        context.SimpleObjects.Add(turretSimpleObject);
        context.SaveChanges();

        try
        {
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Creating character with Coid {characterSimpleObject.Coid}, Name '{packet.CharacterName}' (Length: {packet.CharacterName?.Length ?? 0})");
            
            var character = new CharacterData
            {
                Coid = characterSimpleObject.Coid,
                AccountId = client.Account.Id,
                Name = packet.CharacterName ?? string.Empty,
                HeadId = packet.HeadId,
                BodyId = packet.BodyId,
                HeadDetail1 = packet.HeadDetail1,
                HeadDetail2 = packet.HeadDetail2,
                HelmetId = packet.HelmetId,
                EyesId = packet.EyesId,
                MouthId = packet.MouthId,
                HairId = packet.HairId,
                PrimaryColor = packet.PrimaryColor,
                SecondaryColor = packet.SecondaryColor,
                EyesColor = packet.EyesColor,
                HairColor = packet.HairColor,
                SkinColor = packet.SkinColor,
                SpecialityColor = packet.SpecialityColor,
                ScaleOffset = packet.ScaleOffset,
                PositionX = 0.0f,
                PositionY = 0.0f,
                PositionZ = 0.0f,
                RotationX = 0.0f,
                RotationY = 0.0f,
                RotationZ = 0.0f,
                RotationW = 1.0f
            };
            context.Characters.Add(character);
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Character.Name before SaveChanges: '{character.Name}' (Length: {character.Name?.Length ?? 0})");

            var vehicle = new VehicleData
            {
                Coid = vehicleSimpleObject.Coid,
                CharacterCoid = characterSimpleObject.Coid,
                Name = packet.VehicleName ?? string.Empty,
                PositionX = 0.0f,
                PositionY = 0.0f,
                PositionZ = 0.0f,
                RotationX = 0.0f,
                RotationY = 0.0f,
                RotationZ = 0.0f,
                RotationW = 1.0f,
                PrimaryColor = packet.VehiclePrimaryColor,
                SecondaryColor = packet.VehicleSecondaryColor,
                Trim = packet.VehicleTrim,
                Wheelset = wheelSetSimpleObject.Coid,
                PowerPlant = powerPlantSimpleObject.Coid,
                Armor = armorSimpleObject.Coid,
                RaceItem = raceItemSimpleObject.Coid,
                Turret = turretSimpleObject.Coid
            };
            context.Vehicles.Add(vehicle);
            context.SaveChanges();
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: After SaveChanges, reloading character from DB...");
            
            // Reload to verify what was saved
            context.Entry(character).Reload();
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Character.Name after reload: '{character.Name}' (Length: {character.Name?.Length ?? 0})");

            character.ActiveVehicleCoid = vehicleSimpleObject.Coid;
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Exception during character creation: {ex.Message}");
            if (ex.InnerException != null)
            {
                AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Inner exception: {ex.InnerException.Message}");
                if (ex.InnerException.InnerException != null)
                {
                    AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Inner inner exception: {ex.InnerException.InnerException.Message}");
                }
            }
            AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Error, $"CreateNewCharacter: Stack trace: {ex.StackTrace}");
            
            context.SimpleObjects.Remove(characterSimpleObject);
            context.SimpleObjects.Remove(vehicleSimpleObject);
            context.SimpleObjects.Remove(wheelSetSimpleObject);
            context.SimpleObjects.Remove(powerPlantSimpleObject);
            context.SimpleObjects.Remove(armorSimpleObject);
            context.SimpleObjects.Remove(raceItemSimpleObject);
            context.SimpleObjects.Remove(turretSimpleObject);
            context.SaveChanges();

            return (false, -1);
        }

        AutoCore.Utils.Logger.WriteLog(AutoCore.Utils.LogType.Network, $"CreateNewCharacter: Successfully created character '{packet.CharacterName}' with Coid {characterSimpleObject.Coid}");
        return (true, characterSimpleObject.Coid);
    }

    public static void DeleteCharacter(TNLConnection client, long coid)
    {
        using var context = new CharContext();

        var characterData = context.Characters.FirstOrDefault(c => c.AccountId == client.Account.Id && c.Coid == coid && c.Deleted == false);
        if (characterData != null)
        {
            characterData.Deleted = true;
            context.SaveChanges();
        }
    }

    public static void SendCharacterList(TNLConnection client)
    {
        using var context = new CharContext();

        var coids = context.Characters.Where(c => c.AccountId == client.Account.Id && c.Deleted == false).Select(c => c.Coid).ToList();

        foreach (var coid in coids)
            SendCharacter(client, context, coid);
    }

    public static void ExtendCharacterList(TNLConnection client, long coid)
    {
        using var context = new CharContext();

        SendCharacter(client, context, coid);
    }

    private static void SendCharacter(TNLConnection client, CharContext context, long coid)
    {
        var character = ObjectManager.LoadCharacterForSelection(coid, context);
        if (character == null)
            return;

        var createCharPacket = new CreateCharacterPacket();
        var createVehiclePacket = new CreateVehiclePacket();

        character.WriteToPacket(createCharPacket);
        character.CurrentVehicle.WriteToPacket(createVehiclePacket);

        client.SendGamePacket(createCharPacket);
        client.SendGamePacket(createVehiclePacket);
    }
}

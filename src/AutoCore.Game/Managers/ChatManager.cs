namespace AutoCore.Game.Managers;

using System.Linq;
using System.Text;
using AutoCore.Game.Chat;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

public class ChatManager : Singleton<ChatManager>
{
    /// <summary>
    /// Delivers a private message to <paramref name="target"/> when online.
    /// Returns false when the target is missing or has no <see cref="Character.OwningConnection"/> (SS-04).
    /// Extracted for unit testing without full chat packet / ObjectManager plumbing.
    /// </summary>
    public static bool TryDeliverPrivateMessage(Character target, Action deliver)
    {
        if (target?.OwningConnection == null)
            return false;

        deliver?.Invoke();
        return true;
    }

    public void HandleChatPacket(TNLConnection connection, BinaryReader reader)
    {
        var packet = new ChatPacket();
        packet.Read(reader);

        if (packet.Message.StartsWith('/'))
        {
            HandleChatCommand(connection, packet.Message);
            return;
        }

        switch (packet.ChatType)
        {
            case ChatType.ConvoyMessage:
                ConvoyManager.Instance.BroadcastPacket(connection.CurrentCharacter, packet);
                break;

            case ChatType.ClanMessage:
                ClanManager.Instance.BroadcastPacket(connection.CurrentCharacter, packet);
                break;

            case ChatType.PrivateMessage:
                var target = ObjectManager.Instance.GetCharacterByName(packet.PrivateRecipientName);
                if (target == null)
                    break;

                connection.SendGamePacket(packet);
                TryDeliverPrivateMessage(target, () => target.OwningConnection.SendGamePacket(packet));
                break;

            default:
                Logger.WriteLog(LogType.Error, $"Unhandled ChatType {packet.ChatType} in HandleChat!");
                break;
        }

        // TODO: later: handle chat commands and send the chat packet to the proper recipient(s)
    }

    public void HandleBroadcastPacket(TNLConnection connection, BinaryReader reader)
    {
        var packet = new BroadcastPacket();
        packet.Read(reader);

        if (packet.Message.StartsWith('/'))
        {
            HandleChatCommand(connection, packet.Message);
            return;
        }

        connection.SendGamePacket(packet);

        // TODO: later: handle chat commands and send the chat packet to the proper recipient(s)
    }

    private void HandleChatCommand(TNLConnection connection, string command)
    {
        Logger.WriteLog(LogType.Debug, $"Conn {connection.Account.Name} sent chat command: {command}");

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return;

        if (parts[0].StartsWith("//"))
        {
            Logger.WriteLog(LogType.Debug, $"Ignoring client chat control command: {parts[0]}");
            return;
        }

        var respPacket = new BroadcastPacket
        {
            IsGM = false,
            Sender = "System",
            ChatType = ChatType.SystemMessage,
            Message = ""
        };

        var character = connection.CurrentCharacter;

        var commandResult = ChatCommandService.Instance.Execute(character, command);
        if (commandResult.Handled)
        {
            foreach (var packet in commandResult.Packets)
                connection.SendGamePacket(packet);

            respPacket.Message = commandResult.Message;
            SendChatCommandResponse(connection, respPacket);
            return;
        }

        // Client chat is not always lowercase; match commands case-insensitively.
        switch (parts[0].ToLowerInvariant())
        {
            case "/loot":
                if (parts.Length < 2)
                {
                    respPacket.Message = $"Invalid loot command! Specify a cbid!";
                    break;
                }

                if (int.TryParse(parts[1], out var cbid))
                {
                    var item = ClonedObjectBase.AllocateNewObjectFromCBID(cbid);
                    if (item == null)
                    {
                        respPacket.Message = $"Unable to create item {cbid}!";
                        break;
                    }

                    item.SetCoid(character.Map.LocalCoidCounter++, false);
                    item.LoadCloneBase(cbid);
                    item.Faction = character.Faction;
                    item.Position = character.CurrentVehicle.Position;
                    item.Rotation = character.CurrentVehicle.Rotation;

                    CreateSimpleObjectPacket createPacket;
                    switch (item)
                    {
                        case WheelSet:
                            createPacket = new CreateWheelSetPacket();
                            break;

                        default:
                            createPacket = null;
                            break;
                    }

                    if (createPacket is not null)
                    {
                        item.WriteToPacket(createPacket);

                        connection.SendGamePacket(createPacket);
                    }
                }
                break;

            case "/getcbid":
                if (character?.CurrentVehicle == null)
                {
                    respPacket.Message = "You are not in a vehicle!";
                    break;
                }

                var vehicleCBID = character.CurrentVehicle.CBID;
                if (vehicleCBID == -1)
                {
                    respPacket.Message = "Unable to get vehicle CBID (vehicle not properly loaded)!";
                    break;
                }

                respPacket.Message = $"Your current vehicle CBID: {vehicleCBID}";
                break;

            case "/equippedItems":
            case "/equippeditems":
                if (character?.CurrentVehicle == null)
                {
                    respPacket.Message = "You are not in a vehicle!";
                    break;
                }

                var equippedItems = character.CurrentVehicle
                    .EnumerateEquippedItems()
                    .Where(e => e.Item != null)
                    .ToList();

                if (equippedItems.Count == 0)
                {
                    respPacket.Message = "No equipped vehicle items found.";
                    Logger.WriteLog(LogType.Debug, $"Equipped items for {character.Name}: none");
                    break;
                }

                var equippedList = new StringBuilder();
                equippedList.Append($"Equipped items ({equippedItems.Count}):");

                Logger.WriteLog(LogType.Debug, $"Equipped items for {character.Name}:");
                foreach (var (slot, item) in equippedItems)
                {
                    var line = $"{slot}: COID={item.ObjectId.Coid} Global={item.ObjectId.Global} CBID={item.CBID} Type={item.GetType().Name} CloneType={item.Type}";
                    Logger.WriteLog(LogType.Debug, $"  {line}");
                    equippedList.Append('\n').Append(line);
                }

                respPacket.Message = equippedList.ToString();
                break;

            case "/getNearbyCBIDs":
                if (character?.CurrentVehicle == null)
                {
                    respPacket.Message = "You are not in a vehicle!";
                    break;
                }

                if (character.Map == null)
                {
                    respPacket.Message = "You are not in a map!";
                    break;
                }

                // Parse optional distance parameter (default 50 units)
                float maxDistance = 50.0f;
                if (parts.Length >= 2 && float.TryParse(parts[1], out var distance))
                {
                    maxDistance = distance;
                }

                var characterPosition = character.CurrentVehicle.Position;
                var nearbyObjects = new List<(int CBID, string Type, float Distance)>();

                foreach (var kvp in character.Map.Objects)
                {
                    var obj = kvp.Value;
                    if (obj == null || obj == character || obj == character.CurrentVehicle)
                        continue;

                    var distanceSq = characterPosition.DistSq(obj.Position);
                    var distanceValue = (float)Math.Sqrt(distanceSq);

                    if (distanceValue <= maxDistance)
                    {
                        var objCBID = obj.CBID;
                        var objType = obj.GetType().Name;
                        nearbyObjects.Add((objCBID, objType, distanceValue));
                    }
                }

                if (nearbyObjects.Count == 0)
                {
                    respPacket.Message = $"No objects found within {maxDistance} units.";
                }
                else
                {
                    // Sort by distance
                    nearbyObjects.Sort((a, b) => a.Distance.CompareTo(b.Distance));

                    // Format the message
                    var cbidList = new System.Text.StringBuilder();
                    cbidList.Append($"Nearby objects ({nearbyObjects.Count} within {maxDistance} units):\n");

                    // Limit to first 20 to avoid message length issues
                    var displayCount = Math.Min(nearbyObjects.Count, 20);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var (objCBID, type, dist) = nearbyObjects[i];
                        cbidList.Append($"  CBID: {objCBID} ({type}) - {dist:F1} units away\n");
                    }

                    if (nearbyObjects.Count > 20)
                    {
                        cbidList.Append($"  ... and {nearbyObjects.Count - 20} more");
                    }

                    respPacket.Message = cbidList.ToString().TrimEnd();
                }
                break;

            case "/maps":
                if (character?.Map == null)
                {
                    respPacket.Message = "You are not in a map!";
                    break;
                }

                var currentMapId = character.Map.ContinentId;
                var allMaps = AssetManager.Instance.GetContinentObjects().OrderBy(m => m.Id).ToList();

                var mapList = new System.Text.StringBuilder();
                mapList.Append($"Current Map ID: {currentMapId}\n");
                
                if (allMaps.Count > 0)
                {
                    mapList.Append($"\nAll Available Maps ({allMaps.Count} total):\n");
                    
                    foreach (var map in allMaps)
                    {
                        var isCurrent = map.Id == currentMapId ? " [CURRENT]" : "";
                        var mapType = "";
                        if (map.IsTown) mapType = " [Town]";
                        else if (map.IsArena) mapType = " [Arena]";
                        
                        var displayName = string.IsNullOrWhiteSpace(map.DisplayName) ? "Unnamed" : map.DisplayName;
                        mapList.Append($"  ID: {map.Id} - {displayName}{mapType}{isCurrent}\n");
                    }
                }
                else
                {
                    mapList.Append("\nNo maps available.");
                }

                respPacket.Message = mapList.ToString().TrimEnd();
                break;

            case "/warp":
                if (character?.Map == null)
                {
                    respPacket.Message = "You are not in a map!";
                    break;
                }

                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid warp command! Usage: /warp <mapid>";
                    break;
                }

                if (!int.TryParse(parts[1], out var targetMapId))
                {
                    respPacket.Message = $"Invalid map ID: {parts[1]}. Map ID must be a number.";
                    break;
                }

                // Check if map exists
                var targetMap = AssetManager.Instance.GetContinentObject(targetMapId);
                if (targetMap == null)
                {
                    respPacket.Message = $"Map ID {targetMapId} does not exist. Use /maps to see available maps.";
                    break;
                }

                // Check if we're already on that map
                if (character.Map.ContinentId == targetMapId)
                {
                    respPacket.Message = $"You are already on map {targetMapId}!";
                    break;
                }

                // Attempt to transfer
                var transferSuccess = MapManager.Instance.TransferCharacterToMap(character, targetMapId);
                if (transferSuccess)
                {
                    var mapName = string.IsNullOrWhiteSpace(targetMap.DisplayName) ? "Unnamed" : targetMap.DisplayName;
                    respPacket.Message = $"Warped to map {targetMapId} ({mapName})";
                }
                else
                {
                    respPacket.Message = $"Failed to warp to map {targetMapId}. The map may not be loaded or available.";
                }
                break;

            case "/kill":
                if (character?.CurrentVehicle == null)
                {
                    respPacket.Message = "You are not in a vehicle!";
                    break;
                }

                var vehicle = character.CurrentVehicle;
                if (vehicle.Target == null)
                {
                    respPacket.Message = "You have no target!";
                    break;
                }

                var target = vehicle.Target;
                if (target.IsCorpse || target.IsInvincible)
                {
                    respPacket.Message = "Cannot damage target (corpse or invincible)!";
                    break;
                }

                // Deal 10000 damage
                const int killDamage = 10000;
                var actualDamage = target.TakeDamage(killDamage);

                try
                {
                    var dmg = new DamagePacket { Source = vehicle.ObjectId };
                    dmg.AddHit(target.ObjectId, actualDamage);
                    connection.SendGamePacket(dmg, skipOpcode: true);
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send damage packet: {ex.Message}");
                }

                // Check if target died
                if (target.GetCurrentHP() <= 0)
                {
                    // Set the murderer for loot attribution
                    target.SetMurderer(character.CurrentVehicle ?? (ClonedObjectBase)character);
                    target.OnDeath(DeathType.Silent);
                    respPacket.Message = $"Killed {target.GetType().Name}#{target.ObjectId.Coid} with {actualDamage} damage!";
                }
                else
                {
                    respPacket.Message = $"Dealt {actualDamage} damage to {target.GetType().Name}#{target.ObjectId.Coid} (HP: {target.GetCurrentHP()}/{target.GetMaximumHP()})";
                }
                break;

            case "/xp":
            case "/experience":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid XP command! Usage: /xp <amount>";
                    break;
                }

                if (!int.TryParse(parts[1], out var xpAmount))
                {
                    respPacket.Message = $"Invalid XP amount: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        Experience = xpAmount
                    });
                    respPacket.Message = $"Set Experience to {xpAmount}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send XP packet: {ex.Message}");
                    respPacket.Message = $"Failed to set XP: {ex.Message}";
                }
                break;

            case "/level":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid level command! Usage: /level <level>";
                    break;
                }

                if (!byte.TryParse(parts[1], out var level) || level < 1 || level > 255)
                {
                    respPacket.Message = $"Invalid level: {parts[1]}. Must be a number between 1 and 255.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = level
                    });
                    respPacket.Message = $"Set level to {level}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send level packet: {ex.Message}");
                    respPacket.Message = $"Failed to set level: {ex.Message}";
                }
                break;

            case "/currency":
            {
                // Absolute set only: CharacterLevel (0x2017) + server persist.
                var currency = CurrencySync.TryApplyCurrencyCommand(character, parts);
                if (currency.Success && currency.Packet != null)
                    connection.SendGamePacket(currency.Packet);
                respPacket.Message = currency.Message;
                break;
            }

            case "/mana":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid mana command! Usage: /mana <current> [max]";
                    break;
                }

                if (!short.TryParse(parts[1], out var currentMana))
                {
                    respPacket.Message = $"Invalid current mana: {parts[1]}. Must be a number.";
                    break;
                }

                short maxMana = currentMana;
                if (parts.Length >= 3 && !short.TryParse(parts[2], out maxMana))
                {
                    respPacket.Message = $"Invalid max mana: {parts[2]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        CurrentMana = currentMana,
                        MaxMana = maxMana
                    });
                    respPacket.Message = $"Set mana to {currentMana}/{maxMana}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send mana packet: {ex.Message}");
                    respPacket.Message = $"Failed to set mana: {ex.Message}";
                }
                break;

            case "/tech":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid tech command! Usage: /tech <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var tech))
                {
                    respPacket.Message = $"Invalid tech value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        AttributeTech = tech
                    });
                    respPacket.Message = $"Set Tech to {tech}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send tech packet: {ex.Message}");
                    respPacket.Message = $"Failed to set tech: {ex.Message}";
                }
                break;

            case "/combat":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid combat command! Usage: /combat <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var combat))
                {
                    respPacket.Message = $"Invalid combat value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        AttributeCombat = combat
                    });
                    respPacket.Message = $"Set Combat to {combat}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send combat packet: {ex.Message}");
                    respPacket.Message = $"Failed to set combat: {ex.Message}";
                }
                break;

            case "/theory":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid theory command! Usage: /theory <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var theory))
                {
                    respPacket.Message = $"Invalid theory value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        AttributeTheory = theory
                    });
                    respPacket.Message = $"Set Theory to {theory}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send theory packet: {ex.Message}");
                    respPacket.Message = $"Failed to set theory: {ex.Message}";
                }
                break;

            case "/perception":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid perception command! Usage: /perception <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var perception))
                {
                    respPacket.Message = $"Invalid perception value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        AttributePerception = perception
                    });
                    respPacket.Message = $"Set Perception to {perception}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send perception packet: {ex.Message}");
                    respPacket.Message = $"Failed to set perception: {ex.Message}";
                }
                break;

            case "/attrpoints":
            case "/attributepoints":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid attribute points command! Usage: /attrpoints <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var attrPoints))
                {
                    respPacket.Message = $"Invalid attribute points value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        AttributePoints = attrPoints
                    });
                    respPacket.Message = $"Set Attribute Points to {attrPoints}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send attribute points packet: {ex.Message}");
                    respPacket.Message = $"Failed to set attribute points: {ex.Message}";
                }
                break;

            case "/skillpoints":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid skill points command! Usage: /skillpoints <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var skillPoints))
                {
                    respPacket.Message = $"Invalid skill points value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        SkillPoints = skillPoints
                    });
                    respPacket.Message = $"Set Skill Points to {skillPoints}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send skill points packet: {ex.Message}");
                    respPacket.Message = $"Failed to set skill points: {ex.Message}";
                }
                break;

            case "/research":
            case "/researchpoints":
                if (parts.Length < 2)
                {
                    respPacket.Message = "Invalid research points command! Usage: /research <value>";
                    break;
                }

                if (!short.TryParse(parts[1], out var researchPoints))
                {
                    respPacket.Message = $"Invalid research points value: {parts[1]}. Must be a number.";
                    break;
                }

                try
                {
                    connection.SendGamePacket(new CharacterLevelPacket
                    {
                        CharacterId = character.ObjectId,
                        Level = character.Level,
                        ResearchPoints = researchPoints
                    });
                    respPacket.Message = $"Set Research Points to {researchPoints}!";
                }
                catch (System.Exception ex)
                {
                    Logger.WriteLog(LogType.Error, $"Failed to send research points packet: {ex.Message}");
                    respPacket.Message = $"Failed to set research points: {ex.Message}";
                }
                break;

            case "/combattext":
            case "/ct":
                respPacket.Message = HandleCombatTextTest(connection, character, parts);
                break;

            default:
                Logger.WriteLog(LogType.Debug, $"Unhandled chat command: {parts[0]}");
                return;
        }

        SendChatCommandResponse(connection, respPacket);
    }

    private static string HandleCombatTextTest(TNLConnection connection, Character character, string[] parts)
    {
        var vehicle = character?.CurrentVehicle;
        var result = CombatTextCommand.Execute(parts, new CombatTextCommand.Context
        {
            HasVehicle = vehicle != null,
            Source = vehicle?.ObjectId ?? new TFID(),
            Target = vehicle?.Target?.ObjectId ?? vehicle?.ObjectId ?? new TFID(),
            HasExplicitTarget = vehicle?.Target != null,
            TargetTypeName = vehicle?.Target?.GetType().Name ?? "Vehicle"
        });

        foreach (var send in result.Packets)
        {
            try
            {
                connection.SendGamePacket(send.Packet, skipOpcode: send.SkipOpcode);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"combattext send failed: {ex}");
                return $"combattext failed: {ex.Message}";
            }
        }

        return result.Message;
    }

    private static void SendChatCommandResponse(TNLConnection connection, BroadcastPacket respPacket)
    {
        if (string.IsNullOrEmpty(respPacket.Message))
            return;

        respPacket.MessageLength = (short)(Encoding.UTF8.GetByteCount(respPacket.Message) + 1);
        if (respPacket.SenderCoid == 0)
            respPacket.SenderCoid = unchecked((ulong)-1L);
        connection.SendGamePacket(respPacket);
    }
}

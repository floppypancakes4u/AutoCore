namespace AutoCore.Game.Managers;

using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Global;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

public class ChatManager : Singleton<ChatManager>
{
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
                target.OwningConnection.SendGamePacket(packet);
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

        var respPacket = new BroadcastPacket
        {
            IsGM = false,
            Sender = "System",
            ChatType = ChatType.SystemMessage,
            Message = ""
        };

        var character = connection.CurrentCharacter;

        switch (parts[0])
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

            default:
                Logger.WriteLog(LogType.Debug, $"Unhandled chat command: {parts[0]}");
                break;
        }

        respPacket.MessageLength = (short)respPacket.Message.Length;

        connection.SendGamePacket(respPacket);
    }
}

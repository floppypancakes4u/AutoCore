using System.Text;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;

namespace AutoCore.Game.Chat;

public sealed class ChatCommandService
{
    public static ChatCommandService Instance { get; } = new();

    public ChatCommandExecutionResult Execute(Character character, string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new ChatCommandExecutionResult(false, string.Empty);

        switch (parts[0])
        {
            case "/listItems":
            case "/listitems":
                return new ChatCommandExecutionResult(
                    true,
                    InventoryCommandService.Instance.ListItems(parts));

            case "/addItem":
            case "/additem":
                var addItemResult = InventoryCommandService.Instance.AddItem(
                    character == null ? null : new InventoryRuntime(character),
                    parts);

                return new ChatCommandExecutionResult(
                    true,
                    addItemResult.Message,
                    addItemResult.Packets,
                    addItemResult.AddedItem);

            case "/setcargo":
            case "/setCargo":
                return SetCargo(character, parts);

            case "/clearcargo":
            case "/clearCargo":
                return ClearCargo(character);

            case "/cargoinfo":
            case "/cargoInfo":
                return CargoInfo(character);

            case "/sectorTick":
            case "/sectortick":
            case "/sector.tick":
                return SectorTick(parts);

            case "/showMissions":
            case "/showmissions":
                return ShowMissions(character);

            case "/clearAllMissions":
            case "/clearallmissions":
                return ClearAllMissions(character);

            case "/removeCurrentMission":
            case "/removecurrentmission":
                return RemoveCurrentMission(character);

            case "/giveMission":
            case "/givemission":
                return GiveMission(character, parts);

            case "/completeMission":
            case "/completemission":
                return CompleteMission(character, parts);

            case "/setHP":
            case "/sethp":
            case "/hp":
                return SetHP(character, parts);

            case "/setMaxHP":
            case "/setmaxhp":
            case "/mhp":
                return SetMaxHP(character, parts);

            case "/shield":
            case "/setShield":
            case "/setshield":
                return SetShield(character, parts);

            case "/mshield":
            case "/setMaxShield":
            case "/setmaxshield":
                return SetMaxShield(character, parts);

            case "/power":
            case "/setPower":
            case "/setpower":
                return SetPower(character, parts);

            case "/mpower":
            case "/setMaxPower":
            case "/setmaxpower":
                return SetMaxPower(character, parts);

            default:
                // Case-insensitive account-create aliases (client steals bare /player for //playerrename).
                var cmd = parts[0].ToLowerInvariant();
                if (cmd is "/addplayer" or "/newaccount" or "/player")
                    return CreatePlayer(parts);

                return new ChatCommandExecutionResult(false, string.Empty);
        }
    }

    /// <summary>
    /// Create an auth login account. Prefer <c>/addplayer</c> — the client intercepts <c>/player</c>
    /// as the GM <c>//playerrename</c> command ("not allowed to choose a new name for yourself").
    /// Email is auto-generated as <c>{user}@autocore.local</c>. Char account is created on first login.
    /// </summary>
    private static ChatCommandExecutionResult CreatePlayer(string[] parts)
    {
        if (parts.Length < 3)
            return new ChatCommandExecutionResult(true, "Usage: /addplayer <user> <pass>  (aliases: /newaccount, /player)");

        var result = PlayerAccountService.Instance.Create(parts[1], parts[2]);
        return new ChatCommandExecutionResult(true, result.Message);
    }

    /// <summary>
    /// Live-tune sector main loop period (ms). Usage: <c>/sectorTick 100</c> or <c>/sectorTick</c> to query.
    /// </summary>
    private static ChatCommandExecutionResult SectorTick(string[] parts)
    {
        if (parts.Length < 2)
        {
            var current = SectorLoopControl.CurrentMilliseconds;
            return new ChatCommandExecutionResult(
                true,
                current.HasValue
                    ? $"Sector tick is {current.Value}ms. Usage: /sectorTick <ms>  (e.g. /sectorTick 50, /sectorTick 10)"
                    : "Sector loop control is not available (sector server not running).");
        }

        if (!int.TryParse(parts[1], out var ms))
            return new ChatCommandExecutionResult(true, "Usage: /sectorTick <ms>  (integer 1-5000)");

        if (!SectorLoopControl.TrySet(ms, out var message))
            return new ChatCommandExecutionResult(true, message);

        return new ChatCommandExecutionResult(true, message);
    }

    /// <summary>
    /// Report this character's server-side mission state: completed mission ids and active quests
    /// (with active objective sequence + progress). Diagnostic for mission persistence.
    /// </summary>
    private static ChatCommandExecutionResult ShowMissions(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var completed = character.CompletedMissionIds.OrderBy(x => x).ToList();

        var sb = new StringBuilder();
        sb.Append($"Completed ({completed.Count}): ");
        sb.Append(completed.Count == 0 ? "none" : string.Join(", ", completed));

        sb.Append($" | Active ({character.CurrentQuests.Count}): ");
        if (character.CurrentQuests.Count == 0)
        {
            sb.Append("none");
        }
        else
        {
            sb.Append(string.Join("; ", character.CurrentQuests.Select(q =>
            {
                var progress = q.ObjectiveProgress != null && q.ActiveObjectiveSequence < q.ObjectiveProgress.Length
                    ? q.ObjectiveProgress[q.ActiveObjectiveSequence]
                    : 0;
                var max = q.ObjectiveMax != null && q.ActiveObjectiveSequence < q.ObjectiveMax.Length
                    ? q.ObjectiveMax[q.ActiveObjectiveSequence]
                    : 0;
                return $"mission {q.MissionId} (seq {q.ActiveObjectiveSequence}, {progress}/{max})";
            })));
        }

        return new ChatCommandExecutionResult(true, sb.ToString());
    }

    /// <summary>
    /// Wipe this character's mission state (active + completed) from memory AND the char DB.
    /// The client keeps its current journal until the next relog, when the (now empty) create
    /// packet resets it. Diagnostic / test reset for mission persistence.
    /// </summary>
    private static ChatCommandExecutionResult ClearAllMissions(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var coid = character.ObjectId.Coid;
        var activeCount = character.CurrentQuests.Count;
        var completedCount = character.CompletedMissionIds.Count;

        character.CurrentQuests.Clear();
        character.CompletedMissionIds.Clear();
        MissionPersistence.Instance.DeleteAllForCharacter(coid);

        return new ChatCommandExecutionResult(
            true,
            $"Cleared {activeCount} active and {completedCount} completed mission(s) for coid {coid} (memory + DB). Relog to reset the client journal.");
    }

    /// <summary>
    /// Remove this character's active missions from memory AND the char DB, preserving completed
    /// missions. Client journal updates on the next relog via the create packet.
    /// </summary>
    private static ChatCommandExecutionResult RemoveCurrentMission(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var coid = character.ObjectId.Coid;
        var activeCount = character.CurrentQuests.Count;

        character.CurrentQuests.Clear();
        MissionPersistence.Instance.DeleteActiveForCharacter(coid);

        return new ChatCommandExecutionResult(
            true,
            $"Removed {activeCount} active mission(s) for coid {coid} (memory + DB). Completed missions preserved. Relog to reset the client journal.");
    }

    /// <summary>
    /// Force-grant a mission by id onto this character's active list and push journal/objective
    /// state to the client. Uses the same path as NPC dialog acceptance.
    /// </summary>
    private static ChatCommandExecutionResult GiveMission(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2 || !int.TryParse(parts[1], out var missionId) || missionId <= 0)
            return new ChatCommandExecutionResult(true, "Usage: /giveMission <id>");

        if (AssetManager.Instance.GetMission(missionId) == null)
            return new ChatCommandExecutionResult(true, $"Unknown mission id {missionId}.");

        var alreadyActive = character.CurrentQuests.Any(q => q.MissionId == missionId);
        NpcInteractHandler.GrantMission(character.OwningConnection, character, missionId);

        return new ChatCommandExecutionResult(
            true,
            alreadyActive
                ? $"Mission {missionId} already active; resent to client."
                : $"Granted mission {missionId} (active + client sync).");
    }

    /// <summary>
    /// Force-complete an active mission by id: move to completed, persist, and push client
    /// complete + journal packets.
    /// </summary>
    private static ChatCommandExecutionResult CompleteMission(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2 || !int.TryParse(parts[1], out var missionId) || missionId <= 0)
            return new ChatCommandExecutionResult(true, "Usage: /completeMission <id>");

        if (character.CurrentQuests.All(q => q.MissionId != missionId))
        {
            if (character.CompletedMissionIds.Contains(missionId))
                return new ChatCommandExecutionResult(true, $"Mission {missionId} is already completed.");

            return new ChatCommandExecutionResult(true, $"Mission {missionId} is not active.");
        }

        NpcInteractHandler.ForceCompleteMission(character.OwningConnection, character, missionId);

        return new ChatCommandExecutionResult(
            true,
            $"Completed mission {missionId} (removed from active + client sync).");
    }

    private static ChatCommandExecutionResult SetHP(Character character, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var hp))
            return new ChatCommandExecutionResult(true, "Usage: /setHP <value> (alias /hp). Example: /hp 250");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        vehicle.SetCurrentHP(hp);
        return new ChatCommandExecutionResult(
            true,
            $"HP set to {vehicle.GetCurrentHP()}/{vehicle.GetMaximumHP()}.");
    }

    private static ChatCommandExecutionResult SetMaxHP(Character character, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var maxHp))
            return new ChatCommandExecutionResult(true, "Usage: /setMaxHP <value> (alias /mhp). Example: /mhp 2000");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        vehicle.SetMaximumHP(maxHp);
        return new ChatCommandExecutionResult(
            true,
            $"Max HP set to {vehicle.GetCurrentHP()}/{vehicle.GetMaximumHP()}.");
    }

    private static ChatCommandExecutionResult SetShield(Character character, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var shield))
            return new ChatCommandExecutionResult(true, "Usage: /shield <value>. Example: /shield 250");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        vehicle.SetCurrentShield(shield);
        return new ChatCommandExecutionResult(
            true,
            $"Shield set to {vehicle.CurrentShield}/{vehicle.MaxShield}.");
    }

    private static ChatCommandExecutionResult SetMaxShield(Character character, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var maxShield))
            return new ChatCommandExecutionResult(true, "Usage: /mshield <value>. Example: /mshield 500");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        vehicle.SetMaximumShield(maxShield);
        return new ChatCommandExecutionResult(
            true,
            $"Max shield set to {vehicle.CurrentShield}/{vehicle.MaxShield}.");
    }

    private static ChatCommandExecutionResult SetPower(Character character, string[] parts)
    {
        if (parts.Length < 2 || !short.TryParse(parts[1], out var power))
            return new ChatCommandExecutionResult(true, "Usage: /power <value>. Example: /power 50");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        // sendPacket: false — ChatManager delivers via ChatCommandExecutionResult.Packets.
        var packet = CharacterLevelManager.Instance.SetCurrentMana(character, power, sendPacket: false);
        return new ChatCommandExecutionResult(
            true,
            $"Power set to {packet.CurrentMana}/{packet.MaxMana}.",
            new BasePacket[] { packet });
    }

    private static ChatCommandExecutionResult SetMaxPower(Character character, string[] parts)
    {
        if (parts.Length < 2 || !short.TryParse(parts[1], out var maxPower))
            return new ChatCommandExecutionResult(true, "Usage: /mpower <value>. Example: /mpower 200");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var packet = CharacterLevelManager.Instance.SetMaxMana(character, maxPower, sendPacket: false);
        return new ChatCommandExecutionResult(
            true,
            $"Max power set to {packet.CurrentMana}/{packet.MaxMana}.",
            new BasePacket[] { packet });
    }

    private static ChatCommandExecutionResult SetCargo(Character character, string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out var pageCount) || pageCount < 1)
            return new ChatCommandExecutionResult(true, "Usage: /setcargo <pages> [width]. Example: /setcargo 13 24");

        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var width = character.Inventory.Width;
        if (parts.Length >= 3)
        {
            if (!int.TryParse(parts[2], out width) || width < 1)
                return new ChatCommandExecutionResult(true, "Width must be a positive integer.");
        }

        character.Inventory.SetCapacity(width, pageCount);
        character.Inventory.SaveCapacity(character.ObjectId.Coid);
        character.Inventory.ReloadCargo(character.ObjectId.Coid);

        IReadOnlyList<BasePacket> packets = new BasePacket[]
        {
            InventoryPacketFactory.CreateCargoSendAll(character.Inventory)
        };

        return new ChatCommandExecutionResult(
            true,
            $"Cargo capacity set to {character.Inventory.Width}x{character.Inventory.PageCount} ({character.Inventory.SlotCount} slots).",
            packets);
    }

    private static ChatCommandExecutionResult ClearCargo(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var result = character.Inventory.ClearCargo(character.ObjectId.Coid);
        return new ChatCommandExecutionResult(true, result.Message, result.Packets);
    }

    private static ChatCommandExecutionResult CargoInfo(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        return new ChatCommandExecutionResult(true, character.Inventory.DescribeCargoStatus());
    }
}

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

            default:
                return new ChatCommandExecutionResult(false, string.Empty);
        }
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

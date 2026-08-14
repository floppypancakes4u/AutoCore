using System.Text;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Mission;
using AutoCore.Game.Mission.Requirements;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Skills;
using AutoCore.Game.Structures;
using AutoCore.Utils;

namespace AutoCore.Game.Chat;

public sealed class ChatCommandService
{
    public static ChatCommandService Instance { get; } = new();

    public ChatCommandExecutionResult Execute(Character character, string command)
    {
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new ChatCommandExecutionResult(false, string.Empty);

        // SS-28: gate mutating commands before any state change.
        if (!ChatAdminGate.Authorize(character, parts[0]))
            return new ChatCommandExecutionResult(true, "Permission denied (GM required).");

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

            case "/removeMissionCargo":
            case "/removemissioncargo":
                return RemoveMissionCargo(character, parts);

            case "/cargoinfo":
            case "/cargoInfo":
                return CargoInfo(character);

            case "/sectorTick":
            case "/sectortick":
            case "/sector.tick":
                return SectorTick(parts);

            case "/clone":
            case "/unclone":
                return ToggleClone(character, parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null);

            case "/clonetrim":
            case "/cloneTrim":
                return TrimClone(character, parts.Length > 1 ? parts[1] : null);

            case "/clonefollowdist":
            case "/cloneFollowDist":
                return CloneFollowDist(character, parts.Length > 1 ? parts[1] : null);

            case "/clonestop":
            case "/cloneStop":
                return CloneHold(character, hold: true);

            case "/clonefollow":
            case "/cloneFollow":
                return CloneHold(character, hold: false);

            case "/cloneteleport":
            case "/cloneTeleport":
            case "/clonetp":
                return CloneTeleport(character);

            case "/clonestartpath":
            case "/cloneStartPath":
                return CloneStartPath(character);

            case "/clonepathspeed":
            case "/clonePathSpeed":
                return ClonePathSpeed(character, parts.Length > 1 ? parts[1] : null);

            case "/showMissions":
            case "/showmissions":
                return ShowMissions(character);

            case "/mission":
                return MissionInfo(character, parts);

            case "/reportbug":
            case "/bug":
            case "/bugreport":
                return ReportBug(character, command, parts);

            case "/clearAllMissions":
            case "/clearallmissions":
                return ClearAllMissions(character);

            case "/removeCurrentMission":
            case "/removecurrentmission":
                return RemoveCurrentMission(character);

            case "/removeMission":
            case "/removemission":
                return RemoveMission(character, parts);

            case "/giveMission":
            case "/givemission":
            case "/addMission":
            case "/addmission":
                return GiveMission(character, parts);

            case "/completeMission":
            case "/completemission":
                return CompleteMission(character, parts);

            case "/completeMissionTree":
            case "/completemissiontree":
                return CompleteMissionTree(character, parts);

            case "/seedCompleted":
            case "/seedcompleted":
                return SeedCompleted(character, parts);

            case "/getpos":
            case "/GetPos":
                return GetPos(character);

            case "/teleporttopos":
                return TeleportToPos(character, parts);

            case "/tptonpc":
                return TpToNpc(character, parts);

            case "/tptowaypoint":
            case "/tpToWaypoint":
            case "/tpwaypoint":
                return TpToWaypoint(character);

            case "/portto":
            case "/portTo":
                return PortTo(character, parts);

            case "/porttome":
            case "/portToMe":
                return PortToMe(character, parts);

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

            case "/skills":
                return Skills(character, parts);

            case "/resetSkills":
            case "/resetskills":
                return ResetSkills(character);

            case "/kick":
                return Kick(character, parts);

            case "/ban":
                return Ban(character, parts);

            case "/unban":
                return Unban(character, parts);

            case "/listplayers":
            case "/listPlayers":
                return ListPlayers();

            default:
            {
                // Case-insensitive aliases.
                var cmd = parts[0].ToLowerInvariant();
                if (cmd is "/skillpoints")
                    return SkillPoints(character, parts);

                if (cmd is "/mission")
                    return MissionInfo(character, parts);

                // Client steals bare /player for //playerrename — prefer /addplayer.
                if (cmd is "/addplayer" or "/newaccount" or "/player")
                    return CreatePlayer(parts);

                if (cmd is "/listplayers")
                    return ListPlayers();

                if (cmd is "/kick")
                    return Kick(character, parts);

                if (cmd is "/ban")
                    return Ban(character, parts);

                if (cmd is "/unban")
                    return Unban(character, parts);

                if (cmd is "/getpos")
                    return GetPos(character);

                return new ChatCommandExecutionResult(false, string.Empty);
            }
        }
    }

    private static ChatCommandExecutionResult ListPlayers()
        => new(true, PlayerModerationService.Instance.ListPlayers());

    private static ChatCommandExecutionResult Kick(Character character, string[] parts)
    {
        var query = parts.Length >= 2 ? string.Join(' ', parts.Skip(1)) : null;
        return new ChatCommandExecutionResult(true, PlayerModerationService.Instance.Kick(query, character));
    }

    private static ChatCommandExecutionResult Ban(Character character, string[] parts)
    {
        var query = parts.Length >= 2 ? string.Join(' ', parts.Skip(1)) : null;
        return new ChatCommandExecutionResult(true, PlayerModerationService.Instance.Ban(query, character));
    }

    private static ChatCommandExecutionResult Unban(Character character, string[] parts)
    {
        var query = parts.Length >= 2 ? string.Join(' ', parts.Skip(1)) : null;
        return new ChatCommandExecutionResult(true, PlayerModerationService.Instance.Unban(query, character));
    }

    /// <summary>
    /// GM: teleport the caller to a fuzzy-matched online player's map and location.
    /// Usage: <c>/portto &lt;player&gt;</c> (alias: <c>/portTo</c>).
    /// </summary>
    private static ChatCommandExecutionResult PortTo(Character character, string[] parts)
    {
        var query = parts.Length >= 2 ? string.Join(' ', parts.Skip(1)) : null;
        var result = PlayerPortService.Instance.PortTo(query, character);
        return new ChatCommandExecutionResult(true, result.Message, result.Packets);
    }

    /// <summary>
    /// GM: teleport a fuzzy-matched online player to the caller's map and location.
    /// Usage: <c>/porttome &lt;player&gt;</c> (alias: <c>/portToMe</c>).
    /// </summary>
    private static ChatCommandExecutionResult PortToMe(Character character, string[] parts)
    {
        var query = parts.Length >= 2 ? string.Join(' ', parts.Skip(1)) : null;
        var result = PlayerPortService.Instance.PortToMe(query, character);
        return new ChatCommandExecutionResult(true, result.Message, result.Packets);
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
    /// Toggle a simulated clone of the character's vehicle. The actual behavior lives in
    /// AutoCore.Sim; the Sector host wires <see cref="CloneCommandControl.TryToggleClone"/>.
    /// </summary>
    private static ChatCommandExecutionResult ToggleClone(Character character, string countArg)
    {
        var toggle = CloneCommandControl.TryToggleClone;
        if (toggle == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, toggle(character, countArg));
    }

    /// <summary>/clonetrim &lt;metres&gt; — live clone height trim; see CloneCommandControl.</summary>
    private static ChatCommandExecutionResult TrimClone(Character character, string arg)
    {
        var trim = CloneCommandControl.TryTrimClone;
        if (trim == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, trim(character, arg));
    }

    /// <summary>/clonepathspeed &lt;m/s|default&gt; — live path cruise speed.</summary>
    private static ChatCommandExecutionResult ClonePathSpeed(Character character, string arg)
    {
        var setter = CloneCommandControl.TrySetPathSpeed;
        if (setter == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, setter(character, arg));
    }

    /// <summary>/clonestartpath — clone navigates the nearest map path.</summary>
    private static ChatCommandExecutionResult CloneStartPath(Character character)
    {
        var startPath = CloneCommandControl.TryStartPath;
        if (startPath == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, startPath(character));
    }

    /// <summary>/cloneteleport — jump the caller's clone to them.</summary>
    private static ChatCommandExecutionResult CloneTeleport(Character character)
    {
        var teleport = CloneCommandControl.TryTeleportClone;
        if (teleport == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, teleport(character));
    }

    /// <summary>/clonestop and /clonefollow — park / resume the caller's clone.</summary>
    private static ChatCommandExecutionResult CloneHold(Character character, bool hold)
    {
        var setter = CloneCommandControl.TrySetHold;
        if (setter == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, setter(character, hold));
    }

    /// <summary>/clonefollowdist &lt;metres|default&gt; — live clone follow distance.</summary>
    private static ChatCommandExecutionResult CloneFollowDist(Character character, string arg)
    {
        var setter = CloneCommandControl.TrySetFollowDistance;
        if (setter == null)
            return new ChatCommandExecutionResult(true, "Clone simulation is unavailable on this server.");

        return new ChatCommandExecutionResult(true, setter(character, arg));
    }

    private static ChatCommandExecutionResult Skills(Character character, string[] parts)
    {
        if (character == null) return new ChatCommandExecutionResult(true, "No character loaded.");
        if (parts.Length == 1) return new ChatCommandExecutionResult(true, $"Skill points available: {character.SkillPoints}.");
        if (parts.Length != 3 || !string.Equals(parts[1], "set", StringComparison.OrdinalIgnoreCase) ||
            !short.TryParse(parts[2], out var points) || points < 0)
            return new ChatCommandExecutionResult(true, "Usage: /skills or /skills set <0-32767>");
        CharacterSkillService.Instance.SetPoints(character, points);
        return new ChatCommandExecutionResult(true, $"Skill points set to {points}.", new BasePacket[] { CharacterLevelManager.Instance.BuildPacket(character) });
    }

    /// <summary>
    /// Debug: grant or set unspent skill points. Usage:
    /// <c>/skillPoints</c> (query), <c>/skillPoints 50</c> (set), <c>/skillPoints add 10</c> (add).
    /// </summary>
    private static ChatCommandExecutionResult SkillPoints(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length == 1)
            return new ChatCommandExecutionResult(true, $"Skill points available: {character.SkillPoints}. Usage: /skillPoints <n> | /skillPoints add <n>");

        short points;
        if (parts.Length >= 3 && string.Equals(parts[1], "add", StringComparison.OrdinalIgnoreCase))
        {
            if (!short.TryParse(parts[2], out var delta))
                return new ChatCommandExecutionResult(true, "Usage: /skillPoints add <amount>");
            var sum = character.SkillPoints + delta;
            if (sum < 0)
                sum = 0;
            if (sum > short.MaxValue)
                sum = short.MaxValue;
            points = (short)sum;
        }
        else if (parts.Length >= 2 && short.TryParse(parts[1], out points) && points >= 0)
        {
            // absolute set
        }
        else
        {
            return new ChatCommandExecutionResult(true, "Usage: /skillPoints <0-32767> | /skillPoints add <amount>");
        }

        CharacterSkillService.Instance.SetPoints(character, points);
        return new ChatCommandExecutionResult(
            true,
            $"Skill points set to {points}.",
            new BasePacket[] { CharacterLevelManager.Instance.BuildPacket(character) });
    }

    private static ChatCommandExecutionResult ResetSkills(Character character)
    {
        if (character == null) return new ChatCommandExecutionResult(true, "No character loaded.");
        var count = character.LearnedSkills.Count;
        CharacterSkillService.Instance.Reset(character);
        return new ChatCommandExecutionResult(true, $"Removed {count} learned skill(s) without refunding points. Relog to refresh the skill tree.");
    }

    /// <summary>
    /// Player bug report: packs description + mission journal + last N action events into a zip
    /// and uploads via <see cref="BugReportUploadBridge"/> (Discord when Launcher wired it).
    /// Open to all players (not GM-gated). Usage: <c>/reportbug your text here</c>
    /// </summary>
    private static ChatCommandExecutionResult ReportBug(Character character, string fullCommand, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        // Everything after the command token is free text (may contain spaces).
        var description = string.Empty;
        if (!string.IsNullOrEmpty(fullCommand))
        {
            var firstSpace = fullCommand.IndexOf(' ');
            if (firstSpace >= 0 && firstSpace + 1 < fullCommand.Length)
                description = fullCommand[(firstSpace + 1)..].Trim();
        }

        if (string.IsNullOrWhiteSpace(description) && parts.Length < 2)
        {
            return new ChatCommandExecutionResult(true,
                "Usage: /reportbug <what went wrong>  — attaches mission journal + recent actions and posts to the team.");
        }

        if (string.IsNullOrWhiteSpace(description) && parts.Length >= 2)
            description = string.Join(' ', parts.Skip(1));

        var result = BugReportService.Submit(character, description);
        return new ChatCommandExecutionResult(true, result.PlayerMessage);
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
    /// GM diagnostic: print a mission's display name and accept dialog text by id.
    /// Prefers GLM <c>Title</c> / <c>OnLineAccept</c>, falling back to WAD <c>Name</c> and
    /// <c>Description</c> when the accept line is missing.
    /// Usage: <c>/mission &lt;id&gt;</c>
    /// </summary>
    private static ChatCommandExecutionResult MissionInfo(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2 || !int.TryParse(parts[1], out var missionId) || missionId <= 0)
            return new ChatCommandExecutionResult(true, "Usage: /mission <id>");

        var mission = AssetManager.Instance.GetMission(missionId);
        if (mission == null)
            return new ChatCommandExecutionResult(true, $"Unknown mission id {missionId}.");

        var name = !string.IsNullOrWhiteSpace(mission.Title)
            ? mission.Title
            : !string.IsNullOrWhiteSpace(mission.Name)
                ? mission.Name
                : $"(unnamed mission {missionId})";

        var acceptText = !string.IsNullOrWhiteSpace(mission.OnLineAccept)
            ? mission.OnLineAccept
            : !string.IsNullOrWhiteSpace(mission.Description)
                ? mission.Description
                : "(no accept text)";

        return new ChatCommandExecutionResult(
            true,
            $"Mission {missionId}: {name}\nAccept: {acceptText}");
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
    /// Abandon an active mission by id (FailMission path) and/or erase it from completed.
    /// Full wipe for that mission id: active + completed memory and DB rows.
    /// Usage: <c>/removeMission &lt;id&gt;</c>
    /// </summary>
    private static ChatCommandExecutionResult RemoveMission(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2 || !int.TryParse(parts[1], out var missionId) || missionId <= 0)
            return new ChatCommandExecutionResult(true, "Usage: /removeMission <id>");

        var wasActive = character.CurrentQuests.Any(q => q.MissionId == missionId);
        var wasCompleted = character.CompletedMissionIds.Contains(missionId);

        if (!wasActive && !wasCompleted)
            return new ChatCommandExecutionResult(true, $"Mission {missionId} not found (not active or completed).");

        if (wasActive)
            NpcInteractHandler.FailMission(character.OwningConnection, character, missionId);

        if (wasCompleted)
            character.CompletedMissionIds.Remove(missionId);

        // Ensure active + completed DB rows are dropped even when only completed (FailMission no-ops).
        MissionPersistence.Instance.OnMissionRemoved(character.ObjectId.Coid, missionId);

        var partsDesc = (wasActive, wasCompleted) switch
        {
            (true, true) => "active + completed",
            (true, false) => "active",
            _ => "completed",
        };

        // The retail client keeps completed missions in an in-session hash with no removal
        // wire message — re-running the mission before a relog makes the client reject
        // turn-in dialogs (stale NotCompleteText).
        var relogWarning = wasCompleted
            ? " WARNING: the client keeps completed missions in-session — relog before re-running it."
            : string.Empty;

        return new ChatCommandExecutionResult(
            true,
            $"Removed mission {missionId} ({partsDesc}; memory + DB).{relogWarning}");
    }

    /// <summary>
    /// Force-grant a mission by id onto this character's active list. ObjectiveState can resync
    /// an existing client objective, but retail has no generic live-add packet for an arbitrary
    /// GM grant; a new grant therefore becomes visible through the next create snapshot.
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
                : $"Granted mission {missionId} (active + persisted); relog to load it into the client journal.");
    }

    /// <summary>
    /// Force-complete an active mission by id: move to completed, persist, and push the client
    /// CompleteDynamicObjective packet.
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

    /// <summary>
    /// GM: print the caller's current map name, continent id, and world X Y Z
    /// to game chat and the server console.
    /// Usage: <c>/getpos</c>.
    /// </summary>
    private static ChatCommandExecutionResult GetPos(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (character.Map == null)
            return new ChatCommandExecutionResult(true, "You are not in a map!");

        var pos = character.CurrentVehicle?.Position ?? character.Position;
        var map = character.Map;
        var mapName = map.ContinentObject?.DisplayName;
        if (string.IsNullOrWhiteSpace(mapName))
            mapName = map.ContinentObject?.MapFileName;
        if (string.IsNullOrWhiteSpace(mapName))
            mapName = "Unnamed";

        var message =
            $"Map: {mapName}  Id: {map.ContinentId}  X: {pos.X:F2}  Y: {pos.Y:F2}  Z: {pos.Z:F2}";
        Logger.WriteLog(LogType.Command, message);
        return new ChatCommandExecutionResult(true, message);
    }

    /// <summary>
    /// GM: same-map teleport to absolute world coordinates.
    /// Usage: <c>/teleporttopos &lt;x&gt; &lt;y&gt; &lt;z&gt;</c>.
    /// </summary>
    private static ChatCommandExecutionResult TeleportToPos(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        if (character.Map == null)
            return new ChatCommandExecutionResult(true, "You are not in a map!");

        if (parts.Length < 4
            || !float.TryParse(parts[1], out var x)
            || !float.TryParse(parts[2], out var y)
            || !float.TryParse(parts[3], out var z))
        {
            return new ChatCommandExecutionResult(true, "Usage: /teleporttopos <x> <y> <z>");
        }

        var position = new Vector3(x, y, z);
        return ApplySameMapTeleport(character, vehicle, position,
            $"Teleported to ({position.X:F1}, {position.Y:F1}, {position.Z:F1}).");
    }

    /// <summary>
    /// GM: teleport to the first live object with the given clonebase id.
    /// Searches the current map first, then every loaded map; cross-map hits transfer
    /// via <see cref="MapManager.TransferCharacterToMap(Character, SectorMap, Vector3, Quaternion)"/>
    /// (same path as <c>/tptowaypoint</c>).
    /// Usage: <c>/tptonpc &lt;cbid&gt;</c>.
    /// </summary>
    private static ChatCommandExecutionResult TpToNpc(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        if (character.Map == null)
            return new ChatCommandExecutionResult(true, "You are not in a map!");

        if (parts.Length < 2 || !int.TryParse(parts[1], out var cbid) || cbid <= 0)
            return new ChatCommandExecutionResult(true, "Usage: /tptonpc <cbid>");

        if (!TryFindNpcByCbid(character, cbid, out var foundCoid, out var position, out var foundOnMap))
        {
            return new ChatCommandExecutionResult(
                true,
                $"NPC cbid {cbid} not found on any loaded map.");
        }

        // Prefer the caller's instance of an instanced continent (identical local poses).
        var destinationMap = TryResolveContinentMap(character, foundOnMap.ContinentId) ?? foundOnMap;
        var poseLabel =
            $"NPC cbid {cbid} (coid {foundCoid}) " +
            $"({position.X:F1}, {position.Y:F1}, {position.Z:F1})";

        if (ReferenceEquals(character.Map, destinationMap))
        {
            return ApplySameMapTeleport(character, vehicle, position, $"Teleported to {poseLabel}.");
        }

        var rotation = vehicle.Rotation;
        if (!MapManager.Instance.TransferCharacterToMap(character, destinationMap, position, rotation))
        {
            return new ChatCommandExecutionResult(
                true,
                $"Failed to transfer to map {destinationMap.ContinentId} for NPC cbid {cbid}.");
        }

        return new ChatCommandExecutionResult(
            true,
            $"Transferred to map {destinationMap.ContinentId} {poseLabel}.");
    }

    /// <summary>
    /// Current map first, then every registered map (shared + instances).
    /// </summary>
    private static bool TryFindNpcByCbid(
        Character character,
        int cbid,
        out long foundCoid,
        out Vector3 position,
        out SectorMap foundOnMap)
    {
        foundCoid = 0;
        position = default;
        foundOnMap = null;

        if (TryFindNpcByCbidOnMap(character.Map, cbid, out foundCoid, out position))
        {
            foundOnMap = character.Map;
            return true;
        }

        foreach (var map in MapManager.Instance.AllMaps())
        {
            if (map == null || ReferenceEquals(map, character.Map))
                continue;

            if (!TryFindNpcByCbidOnMap(map, cbid, out foundCoid, out position))
                continue;

            foundOnMap = map;
            return true;
        }

        return false;
    }

    private static bool TryFindNpcByCbidOnMap(
        SectorMap map,
        int cbid,
        out long foundCoid,
        out Vector3 position)
    {
        foundCoid = 0;
        position = default;
        if (map?.Objects == null)
            return false;

        foreach (var obj in map.Objects.Values)
        {
            if (obj == null || obj.CBID != cbid)
                continue;

            foundCoid = obj.ObjectId.Coid;
            position = obj.Position;
            return true;
        }

        return false;
    }

    private static ChatCommandExecutionResult ApplySameMapTeleport(
        Character character,
        Vehicle vehicle,
        Vector3 position,
        string message)
    {
        var rotation = vehicle.Rotation;
        character.Position = position;
        character.Rotation = rotation;
        vehicle.ClearPhysicsInstance();
        vehicle.SetPosition(position);
        vehicle.Rotation = rotation;

        var teleport = new TeleportCharacterPacket { Position = position };
        return new ChatCommandExecutionResult(true, message, new BasePacket[] { teleport });
    }

    /// <summary>
    /// GM: mark every mission in the transitive <see cref="Mission.ReqMissionId"/> closure of
    /// <c>missionId</c> as completed (seed only — no rewards). Does not complete the target itself.
    /// Usage: <c>/completemissiontree &lt;missionId&gt;</c>.
    /// </summary>
    private static ChatCommandExecutionResult CompleteMissionTree(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2 || !int.TryParse(parts[1], out var missionId) || missionId <= 0)
            return new ChatCommandExecutionResult(true, "Usage: /completemissiontree <missionId>");

        var mission = AssetManager.Instance.GetMission(missionId);
        if (mission == null)
            return new ChatCommandExecutionResult(true, $"Unknown mission id {missionId}.");

        var toSeed = CollectPrerequisiteMissionIds(missionId);
        if (toSeed.Count == 0)
        {
            return new ChatCommandExecutionResult(
                true,
                $"Mission {missionId} has no prerequisite missions to seed.");
        }

        var seeded = NpcInteractHandler.MarkMissionsCompletedForSeed(
            character.OwningConnection,
            character,
            toSeed);

        var list = seeded.Count == 0 ? "none (already complete)" : string.Join(", ", seeded);
        return new ChatCommandExecutionResult(
            true,
            $"Seeded {seeded.Count} completed: {list}");
    }

    /// <summary>
    /// GM: mark one or more mission ids completed without rewards (harness seed).
    /// Usage: <c>/seedcompleted &lt;id&gt; [id...]</c>.
    /// </summary>
    private static ChatCommandExecutionResult SeedCompleted(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2)
            return new ChatCommandExecutionResult(true, "Usage: /seedcompleted <id> [id...]");

        var ids = new List<int>();
        for (var i = 1; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var id) || id <= 0)
                return new ChatCommandExecutionResult(true, $"Invalid mission id '{parts[i]}'.");
            ids.Add(id);
        }

        var seeded = NpcInteractHandler.MarkMissionsCompletedForSeed(
            character.OwningConnection,
            character,
            ids);
        var list = seeded.Count == 0 ? "none (already complete)" : string.Join(", ", seeded);
        return new ChatCommandExecutionResult(true, $"Seeded {seeded.Count} completed: {list}");
    }

    /// <summary>
    /// Transitive closure of <see cref="Mission.ReqMissionId"/> entries (&gt; 0), excluding
    /// <paramref name="rootMissionId"/>. Cycle-safe; includes every OR-branch id so either
    /// RequirementsOred style still passes.
    /// </summary>
    internal static List<int> CollectPrerequisiteMissionIds(int rootMissionId)
    {
        var result = new List<int>();
        var seen = new HashSet<int> { rootMissionId };
        var queue = new Queue<int>();
        queue.Enqueue(rootMissionId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var current = AssetManager.Instance.GetMission(currentId);
            if (current?.ReqMissionId == null)
                continue;

            foreach (var reqId in current.ReqMissionId)
            {
                if (reqId <= 0 || !seen.Add(reqId))
                    continue;

                result.Add(reqId);
                queue.Enqueue(reqId);
            }
        }

        return result;
    }

    /// <summary>
    /// GM: teleport the caller to the HUD/GPS primary waypoint for their active mission
    /// objective (map <see cref="VisualWaypoint"/>), with patrol-pad / WorldPosition / deliver
    /// fallbacks. Same-map client snap is <see cref="TeleportCharacterPacket"/> (0x8058 →
    /// <c>CVOGReaction_TeleportTarget</c>). Off-map targets use
    /// <see cref="MapManager.TransferCharacterToMap(Character, SectorMap, Vector3, Quaternion)"/>
    /// (same path as /warp + /portto) at the resolved pose.
    /// Usage: <c>/tptowaypoint</c> (aliases: <c>/tpToWaypoint</c>, <c>/tpwaypoint</c>).
    /// </summary>
    private static ChatCommandExecutionResult TpToWaypoint(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
            return new ChatCommandExecutionResult(true, "You are not in a vehicle!");

        if (character.Map == null)
            return new ChatCommandExecutionResult(true, "You are not in a map!");

        if (character.CurrentQuests.Count == 0)
            return new ChatCommandExecutionResult(true, "No active mission.");

        // First active quest matches /showMissions ordering for "current".
        var quest = character.CurrentQuests[0];
        var mission = AssetManager.Instance.GetMission(quest.MissionId);
        if (mission == null
            || !mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
            || objective == null)
        {
            return new ChatCommandExecutionResult(
                true,
                $"Active mission {quest.MissionId} has no objective at sequence {quest.ActiveObjectiveSequence}.");
        }

        if (!TryResolveMissionWaypoint(
                character,
                quest,
                objective,
                out var targetCoid,
                out var position,
                out var destinationMap,
                out var source,
                out var detail))
        {
            return new ChatCommandExecutionResult(true, detail);
        }

        var rotation = vehicle.Rotation;
        var sameMap = ReferenceEquals(character.Map, destinationMap);
        if (!sameMap)
        {
            // Cross-map: MapInfo + ghost re-establish already place the client at spawnPos.
            if (!MapManager.Instance.TransferCharacterToMap(character, destinationMap, position, rotation))
            {
                return new ChatCommandExecutionResult(
                    true,
                    $"Failed to transfer to map {destinationMap.ContinentId} for mission waypoint.");
            }

            NpcInteractHandler.TryCreditAutoCompletePatrolAtPlayer(character.OwningConnection, targetCoid);
            return new ChatCommandExecutionResult(
                true,
                $"Transferred to map {destinationMap.ContinentId} mission {quest.MissionId} GPS waypoint " +
                $"({source} {targetCoid}) ({position.X:F1}, {position.Y:F1}, {position.Z:F1}).");
        }

        // Same-map discontinuous pose + living client snap (0x8058).
        character.Position = position;
        character.Rotation = rotation;
        vehicle.ClearPhysicsInstance();
        vehicle.SetPosition(position);
        vehicle.Rotation = rotation;

        NpcInteractHandler.TryCreditAutoCompletePatrolAtPlayer(character.OwningConnection, targetCoid);

        var teleport = new TeleportCharacterPacket { Position = position };

        return new ChatCommandExecutionResult(
            true,
            $"Teleported to mission {quest.MissionId} GPS waypoint ({source} {targetCoid}) " +
            $"({position.X:F1}, {position.Y:F1}, {position.Z:F1}).",
            new BasePacket[] { teleport });
    }

    /// <summary>
    /// Prefer current-map VisualWaypoint / patrol / WorldPosition / deliver / UseItem; if that
    /// fails and the objective names another continent, resolve on that map instead.
    /// </summary>
    private static bool TryResolveMissionWaypoint(
        Character character,
        CharacterQuest quest,
        MissionObjective objective,
        out long targetCoid,
        out Vector3 position,
        out SectorMap destinationMap,
        out string source,
        out string failureMessage)
    {
        targetCoid = 0;
        position = default;
        destinationMap = null;
        source = "waypoint";
        failureMessage = "No mission waypoint on the active objective.";

        var currentMap = character.Map;
        if (currentMap == null)
        {
            failureMessage = "You are not in a map!";
            return false;
        }

        if (TryResolveMissionWaypointOnMap(
                currentMap, quest, objective, out targetCoid, out position, out source, out failureMessage))
        {
            destinationMap = currentMap;
            return true;
        }

        var localFailure = failureMessage;
        var continentHint = ResolveObjectiveContinentHint(objective);
        if (continentHint <= 0 || continentHint == currentMap.ContinentId)
        {
            failureMessage = localFailure;
            return false;
        }

        var remoteMap = TryResolveContinentMap(character, continentHint);
        if (remoteMap == null)
        {
            failureMessage = $"Could not load continent {continentHint} for mission waypoint.";
            return false;
        }

        if (TryResolveMissionWaypointOnMap(
                remoteMap, quest, objective, out targetCoid, out position, out source, out failureMessage))
        {
            destinationMap = remoteMap;
            return true;
        }

        // Prefer the local failure when remote also misses — it names the same COID/CBID.
        if (!string.IsNullOrEmpty(localFailure))
            failureMessage = localFailure;
        return false;
    }

    /// <summary>
    /// Continent authored on patrol / deliver / use-item requirements (0 when unset).
    /// </summary>
    private static int ResolveObjectiveContinentHint(MissionObjective objective)
    {
        if (objective?.Requirements == null)
            return 0;

        var patrol = objective.Requirements.OfType<ObjectiveRequirementPatrol>().FirstOrDefault();
        if (patrol != null && patrol.ContinentId > 0)
            return patrol.ContinentId;

        var deliver = objective.Requirements.OfType<ObjectiveRequirementDeliver>()
            .FirstOrDefault(d => d.NPCContinentId > 0);
        if (deliver != null)
            return deliver.NPCContinentId;

        var useItem = objective.Requirements.OfType<ObjectiveRequirementUseItem>()
            .FirstOrDefault(u => u.ContinentID > 0);
        if (useItem != null)
            return useItem.ContinentID;

        return 0;
    }

    private static SectorMap TryResolveContinentMap(Character character, int continentId)
    {
        if (character == null || continentId <= 0)
            return null;

        try
        {
            if (MapManager.Instance.ResolveMapForTests != null)
                return MapManager.Instance.ResolveMapForTests(continentId);

            return MapManager.Instance.GetMapForCharacter(continentId, character);
        }
        catch (Exception ex)
        {
            // Boundary: unknown/unloadable continent is a command reject, not a server fault.
            Logger.WriteLog(
                LogType.Debug,
                $"TpToWaypoint: continent {continentId} resolve failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Prefer map VisualWaypoint for the active objective (HUD/GPS marker), then next patrol pad,
    /// objective WorldPosition COID, then a live deliver NPC, then UseItem world PrimaryItem.
    /// </summary>
    private static bool TryResolveMissionWaypointOnMap(
        SectorMap map,
        CharacterQuest quest,
        MissionObjective objective,
        out long targetCoid,
        out Vector3 position,
        out string source,
        out string failureMessage)
    {
        targetCoid = 0;
        position = default;
        source = "waypoint";
        failureMessage = "No mission waypoint on the active objective.";

        if (map == null)
        {
            failureMessage = "You are not in a map!";
            return false;
        }

        var patrol = objective.Requirements?.OfType<ObjectiveRequirementPatrol>().FirstOrDefault();
        var multiPad = patrol != null && MissionPatrolProgress.CountListedTargets(patrol) > 1;

        // Multi-pad (Crater Run): next GenericTarget, not the single objective VisualWaypoint.
        // Single-pad (Live and Direct): GPS VisualWaypoint stays first.
        if (!multiPad
            && TryResolveVisualWaypoint(map, objective.ObjectiveId, out targetCoid, out position))
        {
            source = "visual";
            return true;
        }

        if (patrol != null && MissionPatrolProgress.CountListedTargets(patrol) > 0)
        {
            var progress = quest.ActiveObjectiveSequence < quest.ObjectiveProgress.Length
                ? quest.ObjectiveProgress[quest.ActiveObjectiveSequence]
                : 0;
            targetCoid = ResolveNextPatrolTargetCoid(patrol, progress);
            if (targetCoid > 0)
            {
                if (NpcInteractHandler.TryGetWorldPosition(map, targetCoid, out position))
                {
                    source = "patrol";
                    return true;
                }

                failureMessage =
                    $"Could not resolve world position for waypoint coid {targetCoid} on this map.";
                return false;
            }
        }

        if (objective.WorldPosition > 0)
        {
            targetCoid = objective.WorldPosition;
            if (NpcInteractHandler.TryGetWorldPosition(map, targetCoid, out position))
            {
                source = "worldpos";
                return true;
            }

            // WorldPosition may also be a VisualWaypoint id (not a live COID).
            if (map.MapData?.VisualWaypoints != null
                && map.MapData.VisualWaypoints.TryGetValue(objective.WorldPosition, out var byWorldPos))
            {
                targetCoid = byWorldPos.Id;
                position = byWorldPos.Position;
                source = "worldpos-visual";
                return true;
            }

            failureMessage =
                $"Could not resolve world position for waypoint coid {targetCoid} on this map.";
            return false;
        }

        var deliver = objective.Requirements?.OfType<ObjectiveRequirementDeliver>()
            .FirstOrDefault(d => d.NPCTargetCBID > 0);
        if (deliver != null)
        {
            var cbid = deliver.NPCTargetCBID;
            foreach (var obj in map.Objects.Values)
            {
                if (obj == null || obj.CBID != cbid)
                    continue;

                targetCoid = obj.ObjectId.Coid;
                position = obj.Position;
                source = "deliver";
                return true;
            }

            failureMessage = $"Deliver NPC cbid {cbid} is not present on this map.";
            return false;
        }

        var useItem = objective.Requirements?.OfType<ObjectiveRequirementUseItem>()
            .FirstOrDefault(u => u.PrimaryItem > 0);
        if (useItem != null)
        {
            targetCoid = useItem.PrimaryItem;
            if (NpcInteractHandler.TryGetWorldPosition(map, targetCoid, out position))
            {
                source = "useitem";
                return true;
            }

            failureMessage =
                $"Could not resolve world position for use-item coid {targetCoid} on this map.";
            return false;
        }

        failureMessage = "No mission waypoint on the active objective.";
        return false;
    }

    /// <summary>
    /// HUD GPS marker for an objective: first map VisualWaypoint whose Objectives list contains
    /// <paramref name="objectiveId"/>. When the marker binds an <see cref="VisualWaypoint.ObjectCoid"/>,
    /// prefer that live/template world position; otherwise use the authored marker position.
    /// </summary>
    private static bool TryResolveVisualWaypoint(
        SectorMap map,
        int objectiveId,
        out long targetCoid,
        out Vector3 position)
    {
        targetCoid = 0;
        position = default;
        if (map?.MapData?.VisualWaypoints == null || objectiveId <= 0)
            return false;

        VisualWaypoint best = null;
        foreach (var wp in map.MapData.VisualWaypoints.Values)
        {
            if (wp?.Objectives == null)
                continue;

            var hit = false;
            for (var i = 0; i < wp.Objectives.Length; i++)
            {
                if (wp.Objectives[i] == objectiveId)
                {
                    hit = true;
                    break;
                }
            }

            if (!hit)
                continue;

            // Prefer lower id when multiple markers share an objective (stable primary).
            if (best == null || wp.Id < best.Id)
                best = wp;
        }

        if (best == null)
            return false;

        targetCoid = best.ObjectCoid > 0 ? best.ObjectCoid : best.Id;
        if (best.ObjectCoid > 0
            && NpcInteractHandler.TryGetWorldPosition(map, best.ObjectCoid, out var livePos))
        {
            position = livePos;
            return true;
        }

        position = best.Position;
        return true;
    }

    /// <summary>
    /// Next pad the player still needs for the active patrol (sequential index or first unvisited).
    /// </summary>
    private static long ResolveNextPatrolTargetCoid(ObjectiveRequirementPatrol patrol, int encodedProgress)
    {
        var targets = MissionPatrolProgress.CountListedTargets(patrol);
        if (targets <= 0)
            return 0;

        var listed = new long[targets];
        var n = 0;
        var count = Math.Max(patrol.TargetCount, 0);
        if (count == 0)
            count = patrol.GenericTargets.Length;
        for (var i = 0; i < count && i < patrol.GenericTargets.Length && n < targets; i++)
        {
            if (patrol.GenericTargets[i] > 0)
                listed[n++] = patrol.GenericTargets[i];
        }

        if (n == 0)
            return 0;

        if (patrol.Sequential)
        {
            var progress = Math.Max(0, encodedProgress);
            var needed = MissionPatrolProgress.NeededCount(patrol);
            if (progress >= needed)
                progress = Math.Max(0, needed - 1);
            var index = progress % n;
            return listed[index];
        }

        // Non-sequential: first pad not yet visited this lap.
        var maskWidth = Math.Min(n, MissionPatrolProgress.MaxTrackableTargets);
        var mask = encodedProgress & ((1 << maskWidth) - 1);
        for (var i = 0; i < maskWidth; i++)
        {
            if ((mask & (1 << i)) == 0)
                return listed[i];
        }

        return listed[0];
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

        // Ghost dirty; CharacterLevel via Packets (ChatManager) — same as /power sendPacket:false.
        vehicle.SetCurrentHP(hp, triggerGhostUpdate: true, notifyOwnerHud: false);
        var packet = CharacterLevelManager.Instance.SyncOwnedCombatHud(character, sendPacket: false);
        return new ChatCommandExecutionResult(
            true,
            $"HP set to {vehicle.GetCurrentHP()}/{vehicle.GetMaximumHP()}.",
            new BasePacket[] { packet });
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

        vehicle.SetMaximumHP(maxHp, triggerGhostUpdate: true, notifyOwnerHud: false);
        var packet = CharacterLevelManager.Instance.SyncOwnedCombatHud(character, sendPacket: false);
        return new ChatCommandExecutionResult(
            true,
            $"Max HP set to {vehicle.GetCurrentHP()}/{vehicle.GetMaximumHP()}.",
            new BasePacket[] { packet });
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

        // Ghost ShieldMask + owner MultipleStatUpdate (0x2010 type=1 → Vehicle_SetCurrentShield).
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
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        if (parts.Length < 2)
        {
            var powerState = CharacterLevelManager.Instance.GetPower(character.ObjectId.Coid);
            return new ChatCommandExecutionResult(true, $"Server power: {powerState.Current}/{powerState.Maximum}.");
        }

        if (!short.TryParse(parts[1], out var power))
            return new ChatCommandExecutionResult(true, "Usage: /power <value>. Example: /power 50");

        // sendPacket: false — ChatManager delivers via ChatCommandExecutionResult.Packets.
        var packet = CharacterLevelManager.Instance.SetPower(character, power, sendPacket: false);
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

    /// <summary>
    /// Remove mission-inventory cargo stacks (IsMissionItem). Optional CBID filter.
    /// Usage: <c>/removeMissionCargo</c> or <c>/removeMissionCargo &lt;cbid&gt;</c>
    /// </summary>
    private static ChatCommandExecutionResult RemoveMissionCargo(Character character, string[] parts)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        var cbidFilter = 0;
        if (parts.Length >= 2)
        {
            if (!int.TryParse(parts[1], out cbidFilter) || cbidFilter <= 0)
                return new ChatCommandExecutionResult(true, "Usage: /removeMissionCargo [cbid]");
        }

        var result = character.Inventory.RemoveMissionCargo(character.ObjectId.Coid, cbidFilter);
        return new ChatCommandExecutionResult(true, result.Message, result.Packets);
    }

    private static ChatCommandExecutionResult CargoInfo(Character character)
    {
        if (character == null)
            return new ChatCommandExecutionResult(true, "No character loaded.");

        return new ChatCommandExecutionResult(true, character.Inventory.DescribeCargoStatus());
    }
}

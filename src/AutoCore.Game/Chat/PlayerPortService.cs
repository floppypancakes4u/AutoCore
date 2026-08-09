using System.Text;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils.Logging;

namespace AutoCore.Game.Chat;

/// <summary>
/// GM /portto and /porttome: fuzzy online-player match, then map+pose teleport.
/// Same-map uses <see cref="TeleportCharacterPacket"/> (0x8058). Cross-map joins the
/// destination player's live <see cref="SectorMap"/> instance (not a fresh continent copy).
/// </summary>
public sealed class PlayerPortService
{
    public static PlayerPortService Instance { get; } = new();

    /// <summary>Online players on this process. Tests inject a fixed list.</summary>
    internal Func<IReadOnlyList<OnlinePlayerSnapshot>> ListOnline { get; set; } = DefaultListOnline;

    internal void ResetForTests()
    {
        ListOnline = DefaultListOnline;
    }

    /// <summary>Admin → target map and location.</summary>
    public PlayerPortResult PortTo(string query, Character admin)
    {
        if (string.IsNullOrWhiteSpace(query))
            return PlayerPortResult.MessageOnly("Usage: /portto <player>");

        if (!TryResolveTarget(query, admin, out var target, out var error))
            return PlayerPortResult.MessageOnly(error);

        // Mover is admin: ChatManager delivers result packets on the admin connection.
        return PortCharacterTo(admin, target, directionLabel: "to", deliverSnapViaResultPackets: true);
    }

    /// <summary>Target → admin map and location.</summary>
    public PlayerPortResult PortToMe(string query, Character admin)
    {
        if (string.IsNullOrWhiteSpace(query))
            return PlayerPortResult.MessageOnly("Usage: /porttome <player>");

        if (!TryResolveTarget(query, admin, out var target, out var error))
            return PlayerPortResult.MessageOnly(error);

        // Mover is the target: ChatManager only replies to the admin, so same-map snap
        // must be sent on the target connection directly.
        return PortCharacterTo(target, admin, directionLabel: "to you from", deliverSnapViaResultPackets: false);
    }

    private bool TryResolveTarget(string query, Character admin, out Character target, out string error)
    {
        target = null;
        error = null;

        var online = ListOnline() ?? Array.Empty<OnlinePlayerSnapshot>();
        var candidates = online.Select(ToCandidate).ToList();
        var match = PlayerNameMatcher.Resolve(query, candidates);
        if (match.Kind == PlayerNameMatchKind.None)
        {
            error = $"No player matching '{query.Trim()}'.";
            return false;
        }

        if (match.Kind == PlayerNameMatchKind.Ambiguous)
        {
            error = FormatAmbiguous(match);
            return false;
        }

        var best = match.Best;
        if (admin != null && best.CharacterCoid == admin.ObjectId.Coid)
        {
            error = "Cannot port yourself.";
            return false;
        }

        // Prefer live character from the snapshot connection; fall back to ObjectManager.
        target = online
            .Where(o => o.CharacterCoid == best.CharacterCoid)
            .Select(o => o.Connection?.CurrentCharacter)
            .FirstOrDefault(c => c != null && c.ObjectId.Coid == best.CharacterCoid);

        if (target == null)
            target = ObjectManager.Instance.GetCharacter(best.CharacterCoid);

        if (target == null)
        {
            error = $"Player '{best.CharacterName}' is no longer online.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Move <paramref name="mover"/> to <paramref name="anchor"/>'s map and pose.
    /// Same-map: server pose + TeleportCharacter (via result packets and/or mover connection).
    /// Cross-map: <see cref="MapManager.TransferCharacterToMap(Character, SectorMap, Vector3, Quaternion)"/>.
    /// </summary>
    private static PlayerPortResult PortCharacterTo(
        Character mover,
        Character anchor,
        string directionLabel,
        bool deliverSnapViaResultPackets)
    {
        if (mover == null)
            return PlayerPortResult.MessageOnly("No character loaded.");
        if (anchor == null)
            return PlayerPortResult.MessageOnly("Target is offline.");

        var moverVehicle = mover.CurrentVehicle;
        if (moverVehicle == null)
            return PlayerPortResult.MessageOnly($"{Label(mover)} is not in a vehicle.");

        if (mover.OwningConnection == null)
            return PlayerPortResult.MessageOnly($"{Label(mover)} has no connection.");

        var destMap = anchor.Map;
        if (destMap == null)
            return PlayerPortResult.MessageOnly($"{Label(anchor)} is not in a map.");

        // Anchor pose: prefer vehicle (world body), else character.
        var destPos = anchor.CurrentVehicle?.Position ?? anchor.Position;
        var destRot = anchor.CurrentVehicle?.Rotation ?? anchor.Rotation;

        BasePacket[] packets = Array.Empty<BasePacket>();
        var sameMap = mover.Map != null && ReferenceEquals(mover.Map, destMap);
        if (sameMap)
        {
            ApplyPose(mover, moverVehicle, destPos, destRot);
            var teleport = new TeleportCharacterPacket { Position = destPos };
            if (deliverSnapViaResultPackets)
            {
                // ChatManager forwards result.Packets to the issuing connection (admin).
                packets = new BasePacket[] { teleport };
            }
            else
            {
                // Living client snap on the moved player's connection.
                mover.OwningConnection.SendGamePacket(teleport);
            }
        }
        else
        {
            if (!MapManager.Instance.TransferCharacterToMap(mover, destMap, destPos, destRot))
                return PlayerPortResult.MessageOnly($"Failed to transfer {Label(mover)} to {Label(anchor)}'s map.");
        }

        GameLog.Audit("PlayerPorted",
            ("MoverCharacterId", mover.ObjectId.Coid),
            ("AnchorCharacterId", anchor.ObjectId.Coid),
            ("MapId", destMap.ContinentId),
            ("SameMap", sameMap),
            ("X", destPos.X),
            ("Y", destPos.Y),
            ("Z", destPos.Z));

        var message =
            $"Ported {Label(mover)} {directionLabel} {Label(anchor)} " +
            $"(map {destMap.ContinentId} @ {destPos.X:F1}, {destPos.Y:F1}, {destPos.Z:F1}).";
        return new PlayerPortResult(message, packets);
    }

    private static void ApplyPose(Character character, Vehicle vehicle, Vector3 position, Quaternion rotation)
    {
        character.Position = position;
        character.Rotation = rotation;
        vehicle.ClearPhysicsInstance();
        vehicle.SetPosition(position);
        vehicle.Rotation = rotation;
    }

    private static string Label(Character c)
    {
        if (c == null)
            return "?";
        return string.IsNullOrEmpty(c.Name) ? c.ObjectId.Coid.ToString() : c.Name;
    }

    private static string FormatAmbiguous(PlayerNameMatchResult match)
    {
        var sb = new StringBuilder("Ambiguous:");
        foreach (var m in match.Matches.Take(8))
        {
            sb.Append(' ')
                .Append(m.AccountId).Append('/').Append(string.IsNullOrEmpty(m.AccountName) ? "?" : m.AccountName)
                .Append(' ')
                .Append(m.CharacterCoid).Append('/').Append(string.IsNullOrEmpty(m.CharacterName) ? "?" : m.CharacterName)
                .Append(';');
        }

        return sb.ToString().TrimEnd(';');
    }

    private static PlayerNameCandidate ToCandidate(OnlinePlayerSnapshot o)
        => new(o.AccountId, o.AccountName, o.CharacterCoid, o.CharacterName);

    private static IReadOnlyList<OnlinePlayerSnapshot> DefaultListOnline()
    {
        // Share the same process-local online enumeration as moderation.
        var list = new List<OnlinePlayerSnapshot>();
        foreach (var character in ObjectManager.Instance.GetOnlineCharacters())
        {
            var conn = character.OwningConnection;
            if (conn == null)
                continue;

            var acct = conn.Account;
            list.Add(new OnlinePlayerSnapshot(
                acct?.Id ?? character.AccountId,
                acct?.Name ?? string.Empty,
                character.ObjectId.Coid,
                character.Name ?? string.Empty,
                conn));
        }

        return list;
    }
}

/// <summary>Outcome of a GM player-port command (message + optional S2C packets for the issuer).</summary>
public sealed class PlayerPortResult
{
    public PlayerPortResult(string message, IReadOnlyList<BasePacket> packets = null)
    {
        Message = message ?? string.Empty;
        Packets = packets ?? Array.Empty<BasePacket>();
    }

    public string Message { get; }
    public IReadOnlyList<BasePacket> Packets { get; }

    public static PlayerPortResult MessageOnly(string message) => new(message);
}

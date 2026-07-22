namespace AutoCore.Game.Combat;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Utils;

/// <summary>
/// Gating helpers for weapon-fire combat debug logs controlled by <see cref="ServerConfig"/>.
/// Log-only — does not change hit resolution, HP, or DamagePacket delivery.
/// </summary>
public static class CombatDebugLog
{
    /// <summary>
    /// Player-owned combat chassis: vehicle with a character owner and no NPC AI.
    /// Weapon targets are never bare <see cref="Character"/> bodies.
    /// </summary>
    public static bool IsPlayerOwnedCombatTarget(ClonedObjectBase target)
    {
        if (target is not Vehicle vehicle)
            return false;
        if (vehicle.NpcAi != null)
            return false;
        return vehicle.Owner?.GetAsCharacter() != null;
    }

    /// <summary>True when neither side is a player-owned combat chassis.</summary>
    public static bool IsNpcToNpc(ClonedObjectBase attacker, ClonedObjectBase target)
        => !IsPlayerOwnedCombatTarget(attacker) && !IsPlayerOwnedCombatTarget(target);

    /// <summary>
    /// Whether weapon damage to <paramref name="target"/> should emit a debug log line.
    /// NPC-vs-NPC requires <see cref="ServerConfig.LogNpcToNpc"/>.
    /// </summary>
    public static bool ShouldLogDamage(ClonedObjectBase attacker, ClonedObjectBase target)
    {
        if (target == null)
            return false;
        if (IsNpcToNpc(attacker, target) && !ServerConfig.LogNpcToNpc)
            return false;
        if (IsPlayerOwnedCombatTarget(target))
            return ServerConfig.LogDamageToPlayers;
        return ServerConfig.LogDamageToNpcs;
    }

    /// <summary>
    /// Whether each fire-cycle hit-chance roll should emit a debug log line.
    /// NPC-vs-NPC requires <see cref="ServerConfig.LogNpcToNpc"/>.
    /// </summary>
    public static bool ShouldLogHitChanceRoll(ClonedObjectBase attacker, ClonedObjectBase target)
    {
        if (!ServerConfig.LogHitChanceRolls)
            return false;
        if (IsNpcToNpc(attacker, target) && !ServerConfig.LogNpcToNpc)
            return false;
        return true;
    }

    /// <summary>Log a hit-chance roll when <see cref="ShouldLogHitChanceRoll"/> is true.</summary>
    public static void LogHitChanceRoll(
        ClonedObjectBase attacker,
        ClonedObjectBase target,
        float hitChance,
        double roll,
        bool hit)
    {
        if (!ShouldLogHitChanceRoll(attacker, target))
            return;

        Logger.WriteLog(
            LogType.Debug,
            "CombatHitChance: attacker={0} target={1} chance={2:F3} roll={3:F3} {4}",
            attacker?.ObjectId?.Coid ?? 0,
            target?.ObjectId?.Coid ?? 0,
            hitChance,
            roll,
            hit ? "hit" : "miss");
    }

    /// <summary>Log applied weapon damage when <see cref="ShouldLogDamage"/> is true.</summary>
    public static void LogWeaponDamage(
        ClonedObjectBase attacker,
        ClonedObjectBase target,
        int actualDamage,
        bool isCrit)
    {
        if (!ShouldLogDamage(attacker, target))
            return;

        var kind = IsPlayerOwnedCombatTarget(target) ? "player" : "npc";
        Logger.WriteLog(
            LogType.Debug,
            "CombatDamage: attacker={0} target={1} kind={2} dealt={3} crit={4}",
            attacker?.ObjectId?.Coid ?? 0,
            target.ObjectId?.Coid ?? 0,
            kind,
            actualDamage,
            isCrit ? 1 : 0);
    }
}

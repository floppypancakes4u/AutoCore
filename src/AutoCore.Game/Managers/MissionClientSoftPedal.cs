namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;

/// <summary>
/// Soft-pedals client-facing mission UI traffic after dialog deliver turn-in.
///
/// Ghidra (client):
/// - Interact icon state 7 = <c>interact_npc_available_new_mission_core</c> (FUN_0091b8d0).
/// - Set when an offerable mission has CoreMission flag (mission+0x169) and prereqs pass
///   (FUN_004d5aa0 / FUN_004d7640 → states 6=non-core, 7=core).
/// - FX load: FUN_004a61b0 → <c>../scripts/&lt;name&gt;.xml</c> tag NDSpecialFX → FUN_007999c0
///   → FUN_007b6c70 COM Release (crash PC 0x007B6DB0).
///
/// Completing a mission client-side already unlocks next core offers; stacking GroupReactionCall
/// (0x206C) / journal during that window races MSXML. This type arms a per-character suppress
/// window for 0x206C sends while server reactions still execute.
/// </summary>
public static class MissionClientSoftPedal
{
    /// <summary>How long to suppress GroupReactionCall S2C after dialog deliver (ms).</summary>
    public static int GroupReactionSuppressMs { get; set; } = 500;

    private static readonly ConcurrentDictionary<long, long> SuppressGroupReactionUntilUnixMs = new();

    /// <summary>Arm suppress for this character after dialog turn-in (extends if already armed).</summary>
    public static void ArmAfterDialogTurnIn(long characterCoid)
    {
        if (characterCoid == 0)
            return;

        var until = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Math.Max(0, GroupReactionSuppressMs);
        SuppressGroupReactionUntilUnixMs.AddOrUpdate(
            characterCoid,
            until,
            (_, existing) => Math.Max(existing, until));
    }

    /// <summary>
    /// True while GroupReactionCall packets should be held for this character.
    /// Expired entries are removed.
    /// </summary>
    public static bool ShouldSuppressGroupReactionCall(long characterCoid)
    {
        if (characterCoid == 0)
            return false;

        if (!SuppressGroupReactionUntilUnixMs.TryGetValue(characterCoid, out var until))
            return false;

        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < until)
            return true;

        SuppressGroupReactionUntilUnixMs.TryRemove(characterCoid, out _);
        return false;
    }

    /// <summary>Test helper: force the suppress deadline into the past.</summary>
    internal static void DebugExpireForTests(long characterCoid)
    {
        if (SuppressGroupReactionUntilUnixMs.ContainsKey(characterCoid))
            SuppressGroupReactionUntilUnixMs[characterCoid] =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1;
    }

    internal static bool HasPendingSuppressForTests(long characterCoid) =>
        SuppressGroupReactionUntilUnixMs.ContainsKey(characterCoid);

    /// <summary>Clear all suppress state and restore defaults (unit tests).</summary>
    public static void ResetForTests()
    {
        GroupReactionSuppressMs = 500;
        SuppressGroupReactionUntilUnixMs.Clear();
    }
}

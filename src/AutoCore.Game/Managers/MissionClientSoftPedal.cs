namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL;
using AutoCore.Utils;

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
///
/// Suppressed 0x206C packets are <b>queued</b> and flushed when the window ends so gate
/// Create/Delete (Biomek Dunlap, Human door) still reach the client. Dropping them left
/// ActivationCount one-shot triggers fired server-side with no client notify — permanent stuck gates.
/// </summary>
public static class MissionClientSoftPedal
{
    /// <summary>How long to suppress GroupReactionCall S2C after dialog deliver (ms).</summary>
    public static int GroupReactionSuppressMs { get; set; } = 500;

    private static readonly ConcurrentDictionary<long, long> SuppressGroupReactionUntilUnixMs = new();

    private static readonly ConcurrentDictionary<long, ConcurrentQueue<PendingGroupReaction>>
        PendingGroupReactions = new();

    private static readonly ConcurrentDictionary<long, byte> FlushScheduled = new();

    /// <summary>
    /// Schedules delayed flush work. Default: sync when delay≤0, else Task.Delay.
    /// Tests may replace (same pattern as NpcInteractHandler.ScheduleDelayedWork).
    /// </summary>
    internal static Action<Action, int> ScheduleDelayedWork { get; set; } = DefaultScheduleDelayedWork;

    private sealed class PendingGroupReaction
    {
        public TNLConnection Connection { get; init; }
        public GroupReactionCallPacket Packet { get; init; }
    }

    private static void DefaultScheduleDelayedWork(Action action, int delayMs)
    {
        if (action == null)
            return;

        if (delayMs <= 0)
        {
            action();
            return;
        }

        _ = RunDelayedAsync(action, delayMs);
    }

    private static async Task RunDelayedAsync(Action action, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            action();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "MissionClientSoftPedal: delayed flush failed: {0}",
                ex.Message);
        }
    }

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

        EnsureFlushScheduled(characterCoid);
    }

    /// <summary>
    /// True while GroupReactionCall packets should be held for this character.
    /// Expired entries are removed (does not flush — use <see cref="FlushPendingGroupReactions"/>).
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

    /// <summary>
    /// Hold a GroupReactionCall for later send. Server reaction effects already applied.
    /// Flushed when soft-pedal expires (or immediately if not suppressing).
    /// </summary>
    public static void EnqueueSuppressedGroupReaction(
        long characterCoid,
        TNLConnection connection,
        GroupReactionCallPacket packet)
    {
        if (characterCoid == 0 || connection == null || packet == null || packet.Count == 0)
            return;

        var queue = PendingGroupReactions.GetOrAdd(characterCoid, _ => new ConcurrentQueue<PendingGroupReaction>());
        queue.Enqueue(new PendingGroupReaction { Connection = connection, Packet = packet });

        Logger.WriteLog(LogType.Debug,
            "GroupReactionCall queued (mission UI soft-pedal) charCoid={0} entries={1} pendingBatches={2}",
            characterCoid,
            packet.Count,
            queue.Count);

        EnsureFlushScheduled(characterCoid);
    }

    /// <summary>
    /// Send any queued 0x206C batches if soft-pedal has expired for this character.
    /// Safe to call when still suppressed (no-op until window ends).
    /// </summary>
    public static void FlushPendingGroupReactions(long characterCoid)
    {
        if (characterCoid == 0)
            return;

        if (ShouldSuppressGroupReactionCall(characterCoid))
        {
            EnsureFlushScheduled(characterCoid);
            return;
        }

        if (!PendingGroupReactions.TryRemove(characterCoid, out var queue))
        {
            FlushScheduled.TryRemove(characterCoid, out _);
            return;
        }

        FlushScheduled.TryRemove(characterCoid, out _);

        var sent = 0;
        while (queue.TryDequeue(out var pending))
        {
            if (pending?.Connection == null || pending.Packet == null || pending.Packet.Count == 0)
                continue;

            pending.Connection.SendGamePacket(pending.Packet, skipOpcode: true);
            sent++;
        }

        if (sent > 0)
        {
            Logger.WriteLog(LogType.Debug,
                "GroupReactionCall flushed after soft-pedal charCoid={0} batches={1}",
                characterCoid,
                sent);
        }
    }

    private static void EnsureFlushScheduled(long characterCoid)
    {
        if (!FlushScheduled.TryAdd(characterCoid, 0))
            return;

        var delayMs = Math.Max(0, GroupReactionSuppressMs);
        if (SuppressGroupReactionUntilUnixMs.TryGetValue(characterCoid, out var until))
        {
            var remaining = (int)(until - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (remaining > delayMs)
                delayMs = remaining;
            // Small pad so we clear after the until timestamp, not on the same ms.
            delayMs = Math.Max(0, delayMs) + 1;
        }

        var schedule = ScheduleDelayedWork ?? DefaultScheduleDelayedWork;
        schedule(
            () =>
            {
                FlushScheduled.TryRemove(characterCoid, out _);
                FlushPendingGroupReactions(characterCoid);
            },
            delayMs);
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

    internal static int PendingBatchCountForTests(long characterCoid) =>
        PendingGroupReactions.TryGetValue(characterCoid, out var q) ? q.Count : 0;

    /// <summary>Clear all suppress state and restore defaults (unit tests).</summary>
    public static void ResetForTests()
    {
        GroupReactionSuppressMs = 500;
        ScheduleDelayedWork = DefaultScheduleDelayedWork;
        SuppressGroupReactionUntilUnixMs.Clear();
        PendingGroupReactions.Clear();
        FlushScheduled.Clear();
    }
}

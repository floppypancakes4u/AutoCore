namespace AutoCore.Game.Managers;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Game.Entities;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

/// <summary>
/// Re-sends the owner combat-HUD snapshot (<see cref="CharacterLevelManager.SyncOwnedCombatHud"/>,
/// CharacterLevelPacket 0x2017) on a repeating cadence for a window after respawn.
/// <para>
/// Ghidra: the client only applies vehicle Health/HealthMax from CharacterLevelPacket while it is
/// "in-world" — <c>CVOGCharacter_ApplyCharacterLevelPacket</c> @ 0x00531E90 gates the HP write on
/// the world-ready byte <c>gameCtx+0xF5</c> (and a possessed vehicle at <c>char+0x250</c>). After
/// an INC airlift respawn that byte stays 0 for the duration of the multi-second airlift cinematic
/// (<c>ClientSpecialEvent_Respawn_Update</c> @ 0x00979730, ~12–17s), so a single resync sent at
/// revive time — or on the first movement packet, which the client emits <i>during</i> the
/// cinematic while being teleported — is dropped. There is no server-observable "in-world" signal
/// (<c>+0xF5</c> is pure client state), so we cover the window by re-sending periodically. This is
/// what <c>/hp</c> does implicitly: it works only because the player types it after the cinematic.
/// The HP-bar HUD refresh also lives only on the CharacterLevelPacket path, so the periodic resync
/// keeps the bar correct as repair pads heal even if the ghost HealthMask path does not refresh it.
/// </para>
/// </summary>
public static class PostRespawnHudResync
{
    /// <summary>Covers the airlift cinematic (~12–17s) plus margin.</summary>
    public const int WindowMs = 20_000;

    /// <summary>Cadence between resyncs; small enough that one lands soon after the cinematic ends.</summary>
    public const int IntervalMs = 2_000;

    private static readonly object Gate = new();
    private static readonly List<PendingEntry> Pending = new();

    private sealed class PendingEntry
    {
        public Character Character;
        public long EndTickMs;
        public long NextSendTickMs;
    }

    /// <summary>
    /// Schedule (or restart) the periodic HUD resync for a freshly revived character. Replaces any
    /// existing schedule for the same character COID (e.g. a second death during the window).
    /// </summary>
    public static void Schedule(Character character, int windowMs = WindowMs, int intervalMs = IntervalMs)
    {
        if (character == null)
            return;

        var now = Environment.TickCount64;
        var coid = character.ObjectId.Coid;
        var step = Math.Max(1, intervalMs);

        lock (Gate)
        {
            Pending.RemoveAll(p => p.Character != null && p.Character.ObjectId.Coid == coid);
            Pending.Add(new PendingEntry
            {
                Character = character,
                EndTickMs = now + Math.Max(0, windowMs),
                NextSendTickMs = now, // fire once immediately; harmless if still mid-cinematic
            });
        }

        Logger.WriteLog(LogType.Debug,
            "PostRespawnHudResync: scheduled coid={0} windowMs={1} intervalMs={2}",
            coid, windowMs, step);
    }

    /// <summary>Process due resyncs (call from the sector main loop).</summary>
    public static int Tick(long nowMs = 0)
    {
        if (nowMs == 0)
            nowMs = Environment.TickCount64;

        List<PendingEntry> due;
        lock (Gate)
        {
            // Drop expired schedules and any whose character is no longer sendable.
            Pending.RemoveAll(p =>
                nowMs >= p.EndTickMs ||
                p.Character == null ||
                p.Character.OwningConnection == null ||
                p.Character.CurrentVehicle == null);

            due = Pending.Where(p => nowMs >= p.NextSendTickMs).ToList();
            foreach (var p in due)
                p.NextSendTickMs = nowMs + Math.Max(1, IntervalMs);
        }

        var count = 0;
        foreach (var p in due)
        {
            try
            {
                // Replicate exactly what /hp does, which is the only sequence known to make HP
                // display/repair resume post-respawn: (1) re-dirty the ghost HealthMask/Max delta
                // (SetCurrentHP's side effect) and (2) send the CharacterLevelPacket snapshot.
                var vehicle = p.Character.CurrentVehicle;
                vehicle.EnsureGhostMaskDelivery(GhostObject.HealthMask | GhostObject.HealthMaxMask);
                CharacterLevelManager.Instance.SyncOwnedCombatHud(p.Character);
                count++;

                Logger.WriteLog(LogType.Debug,
                    "PostRespawnHudResync: resync coid={0} hp={1}/{2} corpse={3}",
                    p.Character.ObjectId.Coid,
                    vehicle.GetCurrentHP(),
                    vehicle.GetMaximumHP(),
                    vehicle.GetIsCorpse() ? 1 : 0);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error,
                    "PostRespawnHudResync.Tick failed coid={0}: {1}",
                    p.Character?.ObjectId.Coid ?? 0, ex.Message);
            }
        }

        return count;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
            Pending.Clear();
    }

    internal static int PendingCountForTests
    {
        get
        {
            lock (Gate)
                return Pending.Count;
        }
    }
}

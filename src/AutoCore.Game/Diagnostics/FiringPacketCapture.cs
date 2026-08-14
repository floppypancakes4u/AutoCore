namespace AutoCore.Game.Diagnostics;

using AutoCore.Game.Constants;
using AutoCore.Game.Entities;

/// <summary>
/// Ring buffer of inbound <see cref="GameOpcode.Firing"/> (0x2022) frames.
/// Records bytes as received; does not parse or reinterpret them.
/// Set <see cref="Enabled"/> to false to disable capture.
/// </summary>
public static class FiringPacketCapture
{
    private const int MaxEntries = 32;
    private static readonly object Sync = new();
    private static readonly Queue<FiringPacketCaptureEntry> Entries = new();

    public static bool Enabled { get; set; } = true;

    public static void RecordIncoming(byte[] bytes, Character character = null)
    {
        if (!Enabled || bytes == null || bytes.Length == 0)
            return;

        var coid = character?.ObjectId.Coid ?? 0;
        var map = character?.Map?.ContinentId ?? -1;

        lock (Sync)
        {
            Entries.Enqueue(new FiringPacketCaptureEntry(
                DateTimeOffset.UtcNow,
                (uint)GameOpcode.Firing,
                bytes.Length,
                Convert.ToHexString(bytes),
                coid,
                map));

            while (Entries.Count > MaxEntries)
                Entries.Dequeue();
        }
    }

    public static IReadOnlyList<FiringPacketCaptureEntry> Snapshot()
    {
        lock (Sync)
            return Entries.ToArray();
    }

    public static void Clear()
    {
        lock (Sync)
            Entries.Clear();
    }
}

public sealed record FiringPacketCaptureEntry(
    DateTimeOffset Timestamp,
    uint Opcode,
    int Length,
    string Hex,
    long CharacterCoid,
    int Map);

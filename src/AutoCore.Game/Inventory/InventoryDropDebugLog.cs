namespace AutoCore.Game.Inventory;

using System.Diagnostics.CodeAnalysis;

[ExcludeFromCodeCoverage]
public static class InventoryDropDebugLog
{
    private const int MaxEntries = 32;
    private static readonly object Gate = new();
    private static readonly Queue<InventoryDropDebugEntry> Entries = new();

    public static void RecordIncoming(byte[] bytes)
    {
        Record("incoming", bytes);
    }

    public static void RecordOutgoing(byte[] bytes)
    {
        Record("outgoing", bytes);
    }

    public static IReadOnlyList<InventoryDropDebugEntry> Snapshot()
    {
        lock (Gate)
            return Entries.ToArray();
    }

    public static void Clear()
    {
        lock (Gate)
            Entries.Clear();
    }

    private static void Record(string direction, byte[] bytes)
    {
        lock (Gate)
        {
            while (Entries.Count >= MaxEntries)
                Entries.Dequeue();

            Entries.Enqueue(new InventoryDropDebugEntry(
                DateTimeOffset.UtcNow,
                direction,
                bytes.Length,
                Convert.ToHexString(bytes)));
        }
    }
}

public sealed record InventoryDropDebugEntry(
    DateTimeOffset Timestamp,
    string Direction,
    int Length,
    string Hex);

namespace AutoCore.Game.Inventory;

public static class InventoryGrabDebugLog
{
    private const int MaxEntries = 32;
    private static readonly object Sync = new();
    private static readonly Queue<InventoryGrabDebugEntry> Entries = new();

    public static void RecordIncoming(byte[] bytes)
    {
        Record("incoming", bytes);
    }

    public static void RecordOutgoing(byte[] bytes)
    {
        Record("outgoing", bytes);
    }

    public static IReadOnlyList<InventoryGrabDebugEntry> Snapshot()
    {
        lock (Sync)
            return Entries.ToArray();
    }

    public static void Clear()
    {
        lock (Sync)
            Entries.Clear();
    }

    private static void Record(string direction, byte[] bytes)
    {
        lock (Sync)
        {
            Entries.Enqueue(new InventoryGrabDebugEntry(
                DateTimeOffset.UtcNow,
                direction,
                bytes.Length,
                Convert.ToHexString(bytes)));

            while (Entries.Count > MaxEntries)
                Entries.Dequeue();
        }
    }
}

public sealed record InventoryGrabDebugEntry(
    DateTimeOffset Timestamp,
    string Direction,
    int Length,
    string Hex);

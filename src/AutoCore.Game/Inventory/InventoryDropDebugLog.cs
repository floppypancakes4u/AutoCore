namespace AutoCore.Game.Inventory;

using System.Diagnostics.CodeAnalysis;
using AutoCore.Game.Constants;

[ExcludeFromCodeCoverage]
public static class InventoryDropDebugLog
{
    private const int MaxEntries = 32;
    private static readonly object Gate = new();
    private static readonly Queue<InventoryDropDebugEntry> Entries = new();

    public static bool ShouldRecordIncoming(GameOpcode opcode) =>
        opcode is GameOpcode.InventoryDrop
            or GameOpcode.InventoryDropMM
            or GameOpcode.InventoryGrabMM
            or GameOpcode.InventoryDestroyItem
            or GameOpcode.ItemDrop;

    public static bool ShouldRecordOutgoing(GameOpcode opcode) =>
        opcode is GameOpcode.InventoryDropResponse
            or GameOpcode.InventoryDropMMResponse
            or GameOpcode.ItemDropResponse
            or GameOpcode.CreateSimpleObject
            or GameOpcode.CreateArmor
            or GameOpcode.CreatePowerPlant
            or GameOpcode.CreateWeapon
            or GameOpcode.CreateWheelSet
            or GameOpcode.DestroyObject;

    public static void RecordIncoming(byte[] bytes)
    {
        Record("incoming", bytes);
    }

    public static void RecordOutgoing(byte[] bytes)
    {
        Record("outgoing", bytes);
    }

    public static void RecordIncomingIfTossRelated(GameOpcode opcode, byte[] bytes)
    {
        if (ShouldRecordIncoming(opcode))
            RecordIncoming(bytes);
    }

    public static void RecordOutgoingIfTossRelated(GameOpcode opcode, byte[] bytes)
    {
        if (ShouldRecordOutgoing(opcode))
            RecordOutgoing(bytes);
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

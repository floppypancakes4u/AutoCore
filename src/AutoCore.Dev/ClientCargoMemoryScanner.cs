namespace AutoCore.Dev;

public static class ClientCargoMemoryScanner
{
    public const int CargoSlotSize = 16;
    public const int CargoSlotsToValidate = 16;

    public static int FindCargoBlock(ReadOnlySpan<byte> memory, long firstCoid, long secondCoid)
    {
        var firstBytes = BitConverter.GetBytes(firstCoid);
        var secondBytes = BitConverter.GetBytes(secondCoid);

        for (var offset = 0; offset <= memory.Length - (CargoSlotSize * 3); offset++)
        {
            if (!memory.Slice(offset, 8).SequenceEqual(firstBytes))
                continue;

            if (!IsSlotPosition(memory.Slice(offset + 8, 8), 0, 0))
                continue;

            if (!memory.Slice(offset + CargoSlotSize, 8).SequenceEqual(secondBytes))
                continue;

            if (!IsSlotPosition(memory.Slice(offset + CargoSlotSize + 8, 8), 1, 0))
                continue;

            if (!FollowingSlotsLookEmpty(memory[(offset + CargoSlotSize * 2)..]))
                continue;

            return offset;
        }

        return -1;
    }

    public static int FindFirst(ReadOnlySpan<byte> memory, long coid)
    {
        var bytes = BitConverter.GetBytes(coid);
        for (var offset = 0; offset <= memory.Length - bytes.Length; offset++)
        {
            if (memory.Slice(offset, bytes.Length).SequenceEqual(bytes))
                return offset;
        }

        return -1;
    }

    public static int FindCargoSlot(ReadOnlySpan<byte> memory, long coid, byte x, byte y)
    {
        var bytes = BitConverter.GetBytes(coid);
        for (var offset = 0; offset <= memory.Length - CargoSlotSize; offset++)
        {
            if (!memory.Slice(offset, 8).SequenceEqual(bytes))
                continue;

            if (IsSlotPosition(memory.Slice(offset + 8, 8), x, y))
                return offset;
        }

        return -1;
    }

    private static bool IsSlotPosition(ReadOnlySpan<byte> metadata, byte expectedX, byte expectedY)
    {
        return metadata.Length >= 2
            && metadata[0] == expectedX
            && metadata[1] == expectedY
            && metadata[2..].ToArray().All(b => b == 0);
    }

    private static bool FollowingSlotsLookEmpty(ReadOnlySpan<byte> memory)
    {
        var slots = Math.Min(CargoSlotsToValidate - 2, memory.Length / CargoSlotSize);
        if (slots <= 0)
            return true;

        for (var i = 0; i < slots; i++)
        {
            var slot = memory.Slice(i * CargoSlotSize, CargoSlotSize);
            if (BitConverter.ToInt64(slot[..8]) != -1L)
                return false;

            if (!slot[8..].ToArray().All(b => b == 0))
                return false;
        }

        return true;
    }
}

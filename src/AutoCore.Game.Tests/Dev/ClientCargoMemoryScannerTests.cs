using AutoCore.Dev;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Dev;

[TestClass]
public class ClientCargoMemoryScannerTests
{
    [TestMethod]
    public void FindCargoBlock_FindsExpectedTwoItemFreshCargoLayout()
    {
        var memory = new byte[96];
        WriteSlot(memory, 16, 18136, 0, 0);
        WriteSlot(memory, 32, 18137, 1, 0);

        for (var offset = 48; offset < memory.Length; offset += ClientCargoMemoryScanner.CargoSlotSize)
            WriteSlot(memory, offset, -1, 0, 0);

        var blockOffset = ClientCargoMemoryScanner.FindCargoBlock(memory, 18136, 18137);

        Assert.AreEqual(16, blockOffset);
    }

    [TestMethod]
    public void FindCargoBlock_RejectsWrongSecondSlotPosition()
    {
        var memory = new byte[96];
        WriteSlot(memory, 0, 18136, 0, 0);
        WriteSlot(memory, 16, 18137, 2, 0);

        for (var offset = 32; offset < memory.Length; offset += ClientCargoMemoryScanner.CargoSlotSize)
            WriteSlot(memory, offset, -1, 0, 0);

        var blockOffset = ClientCargoMemoryScanner.FindCargoBlock(memory, 18136, 18137);

        Assert.AreEqual(-1, blockOffset);
    }

    [TestMethod]
    public void FindFirst_FindsCoidBytes()
    {
        var memory = new byte[32];
        BitConverter.GetBytes(18137L).CopyTo(memory, 9);

        Assert.AreEqual(9, ClientCargoMemoryScanner.FindFirst(memory, 18137));
    }

    private static void WriteSlot(byte[] memory, int offset, long coid, byte x, byte y)
    {
        BitConverter.GetBytes(coid).CopyTo(memory, offset);
        memory[offset + 8] = x;
        memory[offset + 9] = y;
    }
}

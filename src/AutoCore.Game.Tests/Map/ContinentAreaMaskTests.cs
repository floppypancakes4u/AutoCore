using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Game.Map;

[TestClass]
public class ContinentAreaMaskTests
{
    [TestMethod]
    public void SampleAreaId_UsesClientGridFormula()
    {
        // 2x2 grid, GridSize=10 → cell (0,0) covers world [5,15) x [5,15) approximately:
        // cellX = (x - 5) / 10
        const float grid = 10f;
        var areaIds = new byte[]
        {
            // index = height * cellX + cellZ; height=2
            // cellX=0,cellZ=0 → 0
            // cellX=0,cellZ=1 → 1
            // cellX=1,cellZ=0 → 2
            // cellX=1,cellZ=1 → 3
            1, 2, 3, 4
        };

        Assert.AreEqual(1, ContinentAreaMask.SampleAreaId(5f, 5f, grid, 2, 2, areaIds));
        Assert.AreEqual(2, ContinentAreaMask.SampleAreaId(5f, 15f, grid, 2, 2, areaIds));
        Assert.AreEqual(3, ContinentAreaMask.SampleAreaId(15f, 5f, grid, 2, 2, areaIds));
        Assert.AreEqual(4, ContinentAreaMask.SampleAreaId(15f, 15f, grid, 2, 2, areaIds));
        Assert.AreEqual(0, ContinentAreaMask.SampleAreaId(-100f, 0f, grid, 2, 2, areaIds));
    }

    [TestMethod]
    public void AreaBit_AndTryAddArea_AreIdempotent()
    {
        Assert.AreEqual(0u, ContinentAreaMask.AreaBit(0));
        Assert.AreEqual(1u, ContinentAreaMask.AreaBit(1));
        Assert.AreEqual(1u << 31, ContinentAreaMask.AreaBit(32));
        Assert.AreEqual(0u, ContinentAreaMask.AreaBit(33));

        uint bits = 0;
        Assert.IsTrue(ContinentAreaMask.TryAddArea(ref bits, 1, out var n1));
        Assert.AreEqual(1u, n1);
        Assert.IsFalse(ContinentAreaMask.TryAddArea(ref bits, 1, out var n2));
        Assert.AreEqual(1u, n2);
        Assert.IsTrue(ContinentAreaMask.TryAddArea(ref bits, 3, out var n3));
        Assert.AreEqual(0b101u, n3);
    }

    [TestMethod]
    public void GetAreaId_OnMaskInstance()
    {
        var mask = new ContinentAreaMask(99, 1, 1, 10f, new byte[] { 7 });
        Assert.AreEqual(7, mask.GetAreaId(5f, 5f));
        Assert.AreEqual(0, mask.GetAreaId(1000f, 1000f));
        Assert.AreEqual(99, mask.ContinentId);
    }
}

using AutoCore.Game.Inventory;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class ItemDropSpawnPositionTests
{
    [TestMethod]
    public void Adjust_DoublesHorizontalDistanceFromVehicle()
    {
        var vehicle = new Vector3(100f, 5f, 200f);
        var clientDrop = new Vector3(103f, 8f, 200f);

        var spawn = ItemDropSpawnPosition.Adjust(vehicle, clientDrop);

        Assert.AreEqual(106f, spawn.X, 0.001f);
        Assert.AreEqual(8f, spawn.Y, 0.001f);
        Assert.AreEqual(200f, spawn.Z, 0.001f);
    }

    [TestMethod]
    public void Adjust_UsesMinHorizDistanceWhenClientDropIsOnVehicle()
    {
        var vehicle = new Vector3(10f, 5f, 20f);
        var clientDrop = new Vector3(10f, 7f, 20f);

        var spawn = ItemDropSpawnPosition.Adjust(vehicle, clientDrop);

        Assert.AreEqual(10f, spawn.X, 0.001f);
        Assert.AreEqual(7f, spawn.Y, 0.001f);
        Assert.AreEqual(24f, spawn.Z, 0.001f);
    }

    [TestMethod]
    public void Adjust_PreservesDirectionOnDiagonalOffset()
    {
        var vehicle = new Vector3(0f, 0f, 0f);
        var clientDrop = new Vector3(3f, 1f, 4f);

        var spawn = ItemDropSpawnPosition.Adjust(vehicle, clientDrop);

        Assert.AreEqual(6f, spawn.X, 0.001f);
        Assert.AreEqual(1f, spawn.Y, 0.001f);
        Assert.AreEqual(8f, spawn.Z, 0.001f);
    }
}

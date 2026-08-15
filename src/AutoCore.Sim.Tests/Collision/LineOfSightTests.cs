using AutoCore.Game.Structures;
using AutoCore.Sim.Collision;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sim.Tests.Collision;

[TestClass]
public class LineOfSightTests
{
    private static ConvexHull Box() =>
        CacheHullParser.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "hulls", "box.cache")));

    [TestMethod]
    public void IsClear_EmptyOrNullWorld_IsClear()
    {
        Assert.IsTrue(LineOfSight.IsClear(null, new Vector3(0, 1, 0), new Vector3(10, 1, 0)));
        var empty = new StaticCollisionWorld();
        empty.Build();
        Assert.IsTrue(LineOfSight.IsClear(empty, new Vector3(0, 1, 0), new Vector3(10, 1, 0)));
    }

    [TestMethod]
    public void TurretMayShoot_NullWorld_DoesNotGrantClearShot()
    {
        Assert.IsFalse(
            LineOfSight.TurretMayShoot(null, new Vector3(0, 1, 0), new Vector3(10, 1, 0)),
            "until the map hull world exists, turrets must not shoot (would go through walls)");
    }

    [TestMethod]
    public void IsClear_WallBetweenPoints_IsBlocked()
    {
        var world = new StaticCollisionWorld();
        world.Add(Box(), new Vector3(0f, 0.5f, 5f), new Quaternion(0, 0, 0, 1), scale: 4f);
        world.Build();

        Assert.IsFalse(
            LineOfSight.IsClear(world, new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 10f)),
            "a hull between the endpoints must block turret LOS");
    }

    [TestMethod]
    public void IsClear_OpenLane_IsClear()
    {
        var world = new StaticCollisionWorld();
        world.Add(Box(), new Vector3(20f, 0.5f, 5f), new Quaternion(0, 0, 0, 1), scale: 4f);
        world.Build();

        Assert.IsTrue(
            LineOfSight.IsClear(world, new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 10f)),
            "a hull beside the segment must not block");
    }
}

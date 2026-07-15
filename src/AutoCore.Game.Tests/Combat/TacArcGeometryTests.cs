using AutoCore.Game.Combat;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

[TestClass]
public class TacArcGeometryTests
{
    private static readonly Vector3 Origin = new(0f, 0f, 0f);

    [TestMethod]
    public void IsInArc_ForwardTarget_InsideNarrowCone()
    {
        // ValidArc 0.987 ≈ cos(9.25°) half-angle
        var aim = TacArcGeometry.AimFromYaw(0f); // +Z forward
        var target = new Vector3(0f, 0f, 10f);

        Assert.IsTrue(TacArcGeometry.IsInArc(Origin, aim, target, validArc: 0.987f));
    }

    [TestMethod]
    public void IsInArc_SideTarget_OutsideNarrowCone()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var target = new Vector3(10f, 0f, 0f); // 90° off

        Assert.IsFalse(TacArcGeometry.IsInArc(Origin, aim, target, validArc: 0.987f));
    }

    [TestMethod]
    public void IsInArc_WideHalfHemisphere_IncludesSide()
    {
        // ValidArc 0 = cos(90°) → 180° full wedge
        var aim = TacArcGeometry.AimFromYaw(0f);
        var target = new Vector3(10f, 0f, 0.1f);
        Assert.IsTrue(TacArcGeometry.IsInArc(Origin, aim, target, 0f));
    }

    [TestMethod]
    public void IsInArc_ValidArcNegOne_AlwaysTrue()
    {
        var aim = TacArcGeometry.AimFromYaw(0f);
        var behind = new Vector3(0f, 0f, -10f);
        Assert.IsTrue(TacArcGeometry.IsInArc(Origin, aim, behind, -1f));
    }

    [TestMethod]
    public void IsInArc_ValidArcOne_StrictDotNeverPassesExceptCoincident()
    {
        // Client uses ValidArc < dot (strict). cos=1 only when collinear; 1 < 1 is false.
        // So ValidArc>=1 never soft-hits except same-cell (zero horizontal separation).
        var aim = TacArcGeometry.AimFromYaw(0f);
        var forward = new Vector3(0f, 0f, 10f);
        var slight = new Vector3(1f, 0f, 10f);
        Assert.IsFalse(TacArcGeometry.IsInArc(Origin, aim, forward, 1f));
        Assert.IsFalse(TacArcGeometry.IsInArc(Origin, aim, slight, 1f));
        Assert.IsTrue(TacArcGeometry.IsInArc(Origin, aim, Origin, 1f));
    }

    [TestMethod]
    public void IsInArc_ElevatedTarget_UsesHorizontalProjection()
    {
        // Client simple path zeros Y — tall target still in-arc if XZ projection is forward.
        var aim = TacArcGeometry.AimFromYaw(0f);
        var elevated = new Vector3(0f, 50f, 10f);
        Assert.IsTrue(TacArcGeometry.IsInArc(Origin, aim, elevated, 0.987f));
    }

    [TestMethod]
    public void AimFromYaw_RearOffsetIsOppositeForward()
    {
        var front = TacArcGeometry.AimFromYaw(0f);
        var rear = TacArcGeometry.AimFromYaw(MathF.PI);
        Assert.AreEqual(-front.X, rear.X, 0.0001f);
        Assert.AreEqual(-front.Z, rear.Z, 0.0001f);
    }

    [TestMethod]
    public void IsInRange_RespectsMinMax()
    {
        Assert.IsTrue(TacArcGeometry.IsInRange(5f, rangeMin: 0f, rangeMax: 10f));
        Assert.IsFalse(TacArcGeometry.IsInRange(15f, rangeMin: 0f, rangeMax: 10f));
        Assert.IsFalse(TacArcGeometry.IsInRange(1f, rangeMin: 5f, rangeMax: 10f));
        // RangeMax <= 0 means no max gate (melee / special)
        Assert.IsTrue(TacArcGeometry.IsInRange(100f, rangeMin: 0f, rangeMax: 0f));
    }

    [TestMethod]
    public void SprayFalloff_PrimaryIsOne_SecondaryUses105MinusDistOverRange()
    {
        Assert.AreEqual(1f, TacArcGeometry.SprayFalloff(isSprayTarget: false, distFromPrimary: 5f, rangeMax: 10f));
        // 1.05 - 5/10 = 0.55
        Assert.AreEqual(0.55f, TacArcGeometry.SprayFalloff(true, 5f, 10f), 0.0001f);
        // point-blank secondary can exceed 1.0
        Assert.AreEqual(1.05f, TacArcGeometry.SprayFalloff(true, 0f, 10f), 0.0001f);
        Assert.AreEqual(0f, TacArcGeometry.SprayFalloff(true, 20f, 10f), 0.0001f);
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

/// <summary>Direct oracles on internal drive helpers — primary mutation killers.</summary>
[TestClass]
public class NpcDriveHelperOracleTests
{
    [TestCleanup]
    public void TearDown()
    {
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 4.712389f;
        NpcVehicleDriveController.MaxAcceleration = 50f;
        NpcVehicleDriveController.MaxBrake = 60f;
        NpcVehicleDriveController.LookAheadDistance = 28f;
        NpcVehicleDriveController.MaxPathDrift = 6f;
    }

    [TestMethod]
    public void Integrate_NoDrift_ExactStep()
    {
        NpcVehicleDriveController.MaxPathDrift = 100f;
        // yaw=π/2 → +X; speed=10, dt=0.25 → +2.5 on X
        var p = NpcVehicleDriveController.IntegrateFacingPosition(
            new Vector3(1, 4, 3), new Vector3(100, 0, 0), MathF.PI * 0.5f, 10f, 0.25f);
        Assert.AreEqual(3.5f, p.X, 1e-4f);
        Assert.AreEqual(4f, p.Y, 1e-5f);
        Assert.AreEqual(3f, p.Z, 1e-4f);
    }

    [TestMethod]
    public void Integrate_Yaw0_StepsAlongPlusZ()
    {
        NpcVehicleDriveController.MaxPathDrift = 100f;
        var p = NpcVehicleDriveController.IntegrateFacingPosition(
            new Vector3(0, 0, 0), new Vector3(0, 0, 100), 0f, 5f, 0.2f);
        Assert.AreEqual(0f, p.X, 1e-4f);
        Assert.AreEqual(1f, p.Z, 1e-4f);
    }

    [TestMethod]
    public void Integrate_ExcessDrift_PullsToMaxPathDrift()
    {
        NpcVehicleDriveController.MaxPathDrift = 2f;
        // Face +Z, hard target on +X far away
        var p = NpcVehicleDriveController.IntegrateFacingPosition(
            new Vector3(0, 0, 0), new Vector3(20, 0, 0), yaw: 0f, speed: 1f, dt: 0.1f);
        // integrated ≈ (0,0,0.1); drift to (20,0,0) ≈ 20; pull to MaxPathDrift=2 from hard
        var dx = 20f - p.X;
        var dz = 0f - p.Z;
        var d = MathF.Sqrt(dx * dx + dz * dz);
        Assert.AreEqual(2f, d, 0.15f);
        Assert.IsTrue(p.X > 15f, $"pulled X={p.X}");
    }

    [TestMethod]
    public void Integrate_NegativeSpeed_NoBackwardStep()
    {
        NpcVehicleDriveController.MaxPathDrift = 100f;
        var p = NpcVehicleDriveController.IntegrateFacingPosition(
            new Vector3(5, 0, 5), new Vector3(5, 0, 5), 0f, speed: -10f, dt: 0.5f);
        Assert.AreEqual(5f, p.X, 1e-4f);
        Assert.AreEqual(5f, p.Z, 1e-4f);
    }

    [TestMethod]
    public void LookAhead_Straight_ExactInterpolation()
    {
        // Floor is Max(LookAheadDistance, 16)
        NpcVehicleDriveController.LookAheadDistance = 20f;
        var path = Path(
            new Vector3(0, 0, 0),
            new Vector3(100, 0, 0),
            new Vector3(200, 0, 0));
        var hard = new PathStepResult { NewIndex = 1 };
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(
            new Vector3(10, 0, 0), hard, path, laneOffset: 0f);
        // from x=10 toward index-1 point at 100, look 20 → x=30
        Assert.AreEqual(30f, aim.X, 0.05f);
        Assert.AreEqual(0f, aim.Z, 0.05f);
    }

    [TestMethod]
    public void LookAhead_MinFloor_Is16()
    {
        NpcVehicleDriveController.LookAheadDistance = 5f; // floored to 16
        var path = Path(new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(200, 0, 0));
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(
            new Vector3(0, 0, 0), new PathStepResult { NewIndex = 1 }, path, 0f);
        Assert.AreEqual(16f, aim.X, 0.05f);
    }

    [TestMethod]
    public void LookAhead_MultiSegment_ConsumesRemaining()
    {
        NpcVehicleDriveController.LookAheadDistance = 30f;
        var path = Path(
            new Vector3(0, 0, 0),
            new Vector3(10, 0, 0),
            new Vector3(10, 0, 40));
        // At origin, index 0: first seg 10, remaining 20 along +Z leg → aim (10,0,20)
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(
            new Vector3(0, 0, 0), new PathStepResult { NewIndex = 0 }, path, 0f);
        Assert.AreEqual(10f, aim.X, 0.1f);
        Assert.AreEqual(20f, aim.Z, 0.1f);
    }

    [TestMethod]
    public void LookAhead_SkipsZeroLengthSegments()
    {
        NpcVehicleDriveController.LookAheadDistance = 12f; // floors to 16
        var path = Path(
            new Vector3(0, 0, 0),
            new Vector3(0, 0, 0), // degenerate
            new Vector3(50, 0, 0));
        var aim = NpcVehicleDriveController.ResolveLookAheadAim(
            new Vector3(0, 0, 0), new PathStepResult { NewIndex = 0 }, path, 0f);
        Assert.AreEqual(16f, aim.X, 0.1f);
    }

    [TestMethod]
    public void LaneOffset_PerpendicularToPath_Exact()
    {
        // Path along +X: left normal is +Z for positive offset? nx=-sz/len=0, nz=sx/len=1 → +Z
        var path = Path(new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(20, 0, 0));
        var p = NpcVehicleDriveController.ApplyLaneOffset(new Vector3(5, 1, 0), index: 1, path, laneOffset: 2f);
        Assert.AreEqual(5f, p.X, 1e-4f);
        Assert.AreEqual(1f, p.Y, 1e-4f);
        Assert.AreEqual(2f, p.Z, 1e-3f);

        var pNeg = NpcVehicleDriveController.ApplyLaneOffset(new Vector3(5, 1, 0), 1, path, -3f);
        Assert.AreEqual(-3f, pNeg.Z, 1e-3f);
    }

    [TestMethod]
    public void LaneOffset_AlongPlusZ_OffsetOnMinusX()
    {
        // next-prev = (0,0,10); nx=-10/10=-1, nz=0 → offset +2 → -X
        var path = Path(new Vector3(0, 0, 0), new Vector3(0, 0, 10), new Vector3(0, 0, 20));
        var p = NpcVehicleDriveController.ApplyLaneOffset(new Vector3(0, 0, 5), 1, path, 2f);
        Assert.AreEqual(-2f, p.X, 1e-3f);
        Assert.AreEqual(5f, p.Z, 1e-3f);
    }

    [TestMethod]
    public void LaneOffset_ZeroOffset_Unchanged()
    {
        var path = Path(new Vector3(0, 0, 0), new Vector3(10, 0, 0));
        var p = NpcVehicleDriveController.ApplyLaneOffset(new Vector3(3, 2, 1), 0, path, 0f);
        Assert.AreEqual(3f, p.X, 1e-5f);
        Assert.AreEqual(2f, p.Y, 1e-5f);
        Assert.AreEqual(1f, p.Z, 1e-5f);
    }

    [TestMethod]
    public void ApproachSpeed_AccelAndBrake_Exact()
    {
        NpcVehicleDriveController.MaxAcceleration = 20f;
        NpcVehicleDriveController.MaxBrake = 40f;
        Assert.AreEqual(2f, NpcVehicleDriveController.ApproachSpeed(0f, 10f, 0.1f), 1e-4f);
        Assert.AreEqual(10f, NpcVehicleDriveController.ApproachSpeed(0f, 10f, 1f), 1e-4f);
        Assert.AreEqual(6f, NpcVehicleDriveController.ApproachSpeed(10f, 0f, 0.1f), 1e-4f);
        Assert.AreEqual(0f, NpcVehicleDriveController.ApproachSpeed(10f, 0f, 1f), 1e-4f);
        Assert.AreEqual(5f, NpcVehicleDriveController.ApproachSpeed(5f, 5f, 0.1f), 1e-4f);
    }

    [TestMethod]
    public void LimitYaw_CapsAndPassthrough()
    {
        NpcVehicleDriveController.MaxYawRateRadiansPerSecond = 2f;
        // delta=1, maxStep=0.2 → clamp
        Assert.AreEqual(0.2f, NpcVehicleDriveController.LimitYaw(0f, 1f, 0.1f), 1e-4f);
        Assert.AreEqual(-0.2f, NpcVehicleDriveController.LimitYaw(0f, -1f, 0.1f), 1e-4f);
        // within step
        Assert.AreEqual(0.1f, NpcVehicleDriveController.LimitYaw(0f, 0.1f, 0.1f), 1e-4f);
    }

    [TestMethod]
    public void NormalizeRadians_WrapsMultipleTurns()
    {
        Assert.AreEqual(0f, NpcVehicleDriveController.NormalizeRadians(0f), 1e-5f);
        Assert.AreEqual(0f, NpcVehicleDriveController.NormalizeRadians(MathF.PI * 2f), 1e-4f);
        Assert.AreEqual(0f, NpcVehicleDriveController.NormalizeRadians(-MathF.PI * 2f), 1e-4f);
        var a = NpcVehicleDriveController.NormalizeRadians(MathF.PI * 3f); // → π
        Assert.AreEqual(MathF.PI, MathF.Abs(a), 1e-3f);
        var b = NpcVehicleDriveController.NormalizeRadians(-MathF.PI * 3f);
        Assert.AreEqual(MathF.PI, MathF.Abs(b), 1e-3f);
        // large multi-wrap
        var c = NpcVehicleDriveController.NormalizeRadians(MathF.PI * 7f);
        Assert.IsTrue(c > -MathF.PI - 0.01f && c <= MathF.PI + 0.01f, $"c={c}");
    }

    [TestMethod]
    public void DesiredYaw_FromAim_HardVel_Fallback()
    {
        var aimYaw = NpcVehicleDriveController.ResolveDesiredYaw(
            new Vector3(0, 0, 0), new Vector3(10, 0, 0),
            new PathStepResult { Velocity = default }, fallbackYaw: 0.5f);
        Assert.AreEqual(MathF.PI * 0.5f, aimYaw, 1e-4f);

        var velYaw = NpcVehicleDriveController.ResolveDesiredYaw(
            new Vector3(0, 0, 0), new Vector3(0, 0, 0),
            new PathStepResult { Velocity = new Vector3(0, 0, 5) }, fallbackYaw: 0.5f);
        Assert.AreEqual(0f, velYaw, 1e-4f);

        var fb = NpcVehicleDriveController.ResolveDesiredYaw(
            new Vector3(0, 0, 0), new Vector3(0, 0, 0),
            new PathStepResult { Velocity = default }, fallbackYaw: 1.23f);
        Assert.AreEqual(1.23f, fb, 1e-5f);
    }

    [TestMethod]
    public void XzSpeed_IgnoresY()
    {
        Assert.AreEqual(5f, NpcVehicleDriveController.XzSpeed(new Vector3(3, 99, 4)), 1e-4f);
        Assert.AreEqual(0f, NpcVehicleDriveController.XzSpeed(new Vector3(0, 7, 0)), 1e-5f);
    }

    [TestMethod]
    public void WheelSamples_AsymmetricHeights_ExactMeans()
    {
        // Front-right high, etc.
        var wheels = new[]
        {
            new Vector3(1f, 0f, 2f),  // FR
            new Vector3(-1f, 0f, 2f), // FL
            new Vector3(1f, 0f, -2f), // RR
            new Vector3(-1f, 0f, -2f),// RL
        };
        Assert.IsTrue(TerrainContactPlane.TryCollectWheelSamples(
            new Vector3(0, 0, 0), fwdX: 0f, fwdZ: 1f, rightX: 1f, rightZ: 0f, wheels,
            sample: (float x, float z, out float y) =>
            {
                // y = 10 + x + 2*z  (distinct per corner)
                y = 10f + x + (2f * z);
                return true;
            },
            out var yC, out var yF, out var yB, out var yR, out var yL, out var hl, out var hw));

        // FR=10+1+4=15, FL=10-1+4=13, RR=10+1-4=7, RL=10-1-4=5
        Assert.AreEqual((15f + 13f + 7f + 5f) / 4f, yC, 1e-4f); // 10
        Assert.AreEqual((15f + 13f) / 2f, yF, 1e-4f); // 14
        Assert.AreEqual((7f + 5f) / 2f, yB, 1e-4f);  // 6
        Assert.AreEqual((15f + 7f) / 2f, yR, 1e-4f);  // 11
        Assert.AreEqual((13f + 5f) / 2f, yL, 1e-4f);  // 9
        Assert.AreEqual(2f, hl, 1e-4f);
        Assert.AreEqual(1f, hw, 1e-4f);
    }

    [TestMethod]
    public void WheelSamples_WorldTransform_UsesYaw()
    {
        // yaw 90°: forward=+X, right=+Z? 
        // fwdX=sin(π/2)=1, fwdZ=0, rightX=cos=0, rightZ=-sin=-1
        // Wait code: rightX=cos(yaw), rightZ=-sin(yaw) → at π/2: right=(0,-1) which is -Z
        var wheels = new[] { new Vector3(0f, 0f, 3f) }; // local forward hardpoint only — nAll=1 fails
        // Need 2 wheels
        wheels = new[]
        {
            new Vector3(0f, 0f, 3f),
            new Vector3(0f, 0f, -3f),
        };
        float? sampledXHigh = null;
        Assert.IsTrue(TerrainContactPlane.TryCollectWheelSamples(
            new Vector3(10, 0, 20), fwdX: 1f, fwdZ: 0f, rightX: 0f, rightZ: -1f, wheels,
            sample: (float x, float z, out float y) =>
            {
                if (x > 12f) sampledXHigh = x;
                y = x; // encode world X in height
                return true;
            },
            out var yC, out var yF, out var yB, out _, out _, out var hl, out _));
        // world for +Z local: wx=10+1*3=13, wz=20+0 → y=13 for front
        Assert.AreEqual(13f, yF, 0.05f);
        Assert.AreEqual(7f, yB, 0.05f); // 10-3
        Assert.AreEqual(10f, yC, 0.05f);
        Assert.AreEqual(3f, hl, 0.01f);
        Assert.IsNotNull(sampledXHigh);
    }

    [TestMethod]
    public void Metrics_Build_TracksAverageRideHeight()
    {
        VehicleGroundMetricsCache.Clear();
        var vsA = new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelRadius = new[] { 0.5f, 0.5f },
            WheelHardPoints = new[] { new Vector3(1, 0, 2), new Vector3(-1, 0, -2) },
        };
        var vsB = new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelRadius = new[] { 0.7f, 0.7f },
            WheelHardPoints = new[] { new Vector3(1, 0, 2), new Vector3(-1, 0, -2) },
        };
        var vsTiny = new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelRadius = new[] { 0.04f }, // below 0.05 → excluded from average
            WheelHardPoints = new[] { new Vector3(1, 0, 1), new Vector3(-1, 0, -1) },
        };
        var a = (AutoCore.Game.CloneBases.CloneBaseVehicle)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(AutoCore.Game.CloneBases.CloneBaseVehicle));
        a.VehicleSpecific = vsA;
        a.CloneBaseSpecific = new AutoCore.Game.CloneBases.Specifics.CloneBaseSpecific { CloneBaseId = 1 };
        var b = (AutoCore.Game.CloneBases.CloneBaseVehicle)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(AutoCore.Game.CloneBases.CloneBaseVehicle));
        b.VehicleSpecific = vsB;
        b.CloneBaseSpecific = new AutoCore.Game.CloneBases.Specifics.CloneBaseSpecific { CloneBaseId = 2 };
        var c = (AutoCore.Game.CloneBases.CloneBaseVehicle)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(AutoCore.Game.CloneBases.CloneBaseVehicle));
        c.VehicleSpecific = vsTiny;
        c.CloneBaseSpecific = new AutoCore.Game.CloneBases.Specifics.CloneBaseSpecific { CloneBaseId = 3 };

        Assert.AreEqual(3, VehicleGroundMetricsCache.BuildFromCloneBases(
            new Dictionary<int, AutoCore.Game.CloneBases.CloneBase> { [1] = a, [2] = b, [3] = c }));
        Assert.AreEqual(2, VehicleGroundMetricsCache.LastWithRadiusCount);
        Assert.AreEqual((0.5f + 0.7f) / 2f, VehicleGroundMetricsCache.LastAverageRideHeight, 1e-4f);
    }

    [TestMethod]
    public void Metrics_Build_NoRadius_ZeroAverage()
    {
        VehicleGroundMetricsCache.Clear();
        var vs = new AutoCore.Game.CloneBases.Specifics.VehicleSpecific
        {
            WheelRadius = new[] { 0f },
            WheelHardPoints = new[] { new Vector3(1, 0, 1) },
        };
        var cv = (AutoCore.Game.CloneBases.CloneBaseVehicle)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(AutoCore.Game.CloneBases.CloneBaseVehicle));
        cv.VehicleSpecific = vs;
        cv.CloneBaseSpecific = new AutoCore.Game.CloneBases.Specifics.CloneBaseSpecific { CloneBaseId = 9 };
        VehicleGroundMetricsCache.BuildFromCloneBases(
            new Dictionary<int, AutoCore.Game.CloneBases.CloneBase> { [9] = cv });
        Assert.AreEqual(0, VehicleGroundMetricsCache.LastWithRadiusCount);
        Assert.AreEqual(0f, VehicleGroundMetricsCache.LastAverageRideHeight, 1e-5f);
    }

    [TestMethod]
    public void PathCurvature_BothTermsNonZero_ExactRadius()
    {
        // Both cross terms non-zero so + vs − on cross product changes |cross|.
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(4, 0, 1);
        var c = new Vector3(1, 0, 3);
        var ab = MathF.Sqrt(16f + 1f);
        var bc = MathF.Sqrt(9f + 4f);
        var ca = MathF.Sqrt(1f + 9f);
        var cross = MathF.Abs((4f * 3f) - (1f * 1f)); // 11
        var expected = (ab * bc * ca) / (2f * cross);
        Assert.AreEqual(expected, PathCurvature.Radius(a, b, c), 1e-4f);
    }

    private static MapPathTemplate Path(params Vector3[] pts)
    {
        var path = new MapPathTemplate();
        foreach (var p in pts)
            path.Points.Add(new MapPathTemplate.MapPathPoint { Position = p, AcceptDistance = 2f });
        return path;
    }
}

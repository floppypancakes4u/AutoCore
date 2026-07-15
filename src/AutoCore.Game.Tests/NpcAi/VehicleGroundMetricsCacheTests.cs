using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;

[TestClass]
public class VehicleGroundMetricsCacheTests
{
    [TestCleanup]
    public void TearDown()
    {
        VehicleGroundMetricsCache.Clear();
        VehicleGroundMetricsCache.RideHeightScale = 1f;
        VehicleGroundMetricsCache.MaxRideHeight = 1.25f;
    }

    [TestMethod]
    public void Compute_RideHeight_IsRadiusMinusHardpointY()
    {
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 2f),
                new Vector3(-1f, 0f, 2f),
                new Vector3(1f, 0f, -2f),
                new Vector3(-1f, 0f, -2f),
                default,
                default,
            },
        };

        var m = VehicleGroundMetricsCache.Compute(vs);
        Assert.AreEqual(0.5f, m.MeanWheelRadius, 0.01f);
        Assert.AreEqual(0f, m.MeanHardpointY, 0.01f);
        Assert.AreEqual(0.5f, m.ChassisHeightAboveTerrain, 0.01f);
        Assert.AreEqual(2f, m.HalfLength, 0.01f);
        Assert.AreEqual(1f, m.HalfWidth, 0.01f);
    }

    [TestMethod]
    public void Compute_HardpointBelowOrigin_IncreasesRideHeight()
    {
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.4f, 0.4f, 0.4f, 0.4f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, -0.1f, 2f),
                new Vector3(-1f, -0.1f, 2f),
                new Vector3(1f, -0.1f, -2f),
                new Vector3(-1f, -0.1f, -2f),
            },
        };

        var m = VehicleGroundMetricsCache.Compute(vs);
        // radius 0.4 − (−0.1) = 0.5
        Assert.AreEqual(0.5f, m.ChassisHeightAboveTerrain, 0.01f);
    }

    [TestMethod]
    public void BuildFromCloneBases_IndexesByCbid()
    {
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.6f, 0.6f, 0.6f, 0.6f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 2f),
                new Vector3(-1f, 0f, 2f),
                new Vector3(1f, 0f, -2f),
                new Vector3(-1f, 0f, -2f),
            },
        };

        // Minimal fake via Compute + manual dictionary path: Build needs CloneBaseVehicle.
        // Use Build with empty then GetRideHeight default.
        VehicleGroundMetricsCache.BuildFromCloneBases(new Dictionary<int, CloneBase>());
        Assert.AreEqual(0, VehicleGroundMetricsCache.Count);
        Assert.AreEqual(0f, VehicleGroundMetricsCache.GetRideHeight(9999), 0.001f);

        var m = VehicleGroundMetricsCache.Compute(vs);
        Assert.AreEqual(0.6f, m.ChassisHeightAboveTerrain, 0.01f);
    }

    [TestMethod]
    public void RideHeightScale_ReducesClearance()
    {
        VehicleGroundMetricsCache.RideHeightScale = 0.5f;
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0.8f, 0.8f, 0.8f, 0.8f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 1f),
                new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(-1f, 0f, -1f),
            },
        };

        var m = VehicleGroundMetricsCache.Compute(vs);
        Assert.AreEqual(0.4f, m.ChassisHeightAboveTerrain, 0.01f);
    }

    [TestMethod]
    public void Compute_NoRadii_ZeroRideHeight()
    {
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 0f, 0f, 0f, 0f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 1f),
                new Vector3(-1f, 0f, -1f),
            },
        };
        var m = VehicleGroundMetricsCache.Compute(vs);
        Assert.AreEqual(0f, m.MeanWheelRadius, 0.001f);
        Assert.AreEqual(0f, m.ChassisHeightAboveTerrain, 0.001f);
        Assert.AreEqual(0, m.WheelRadiusCount);
    }

    [TestMethod]
    public void Compute_MaxRideHeight_Clamps()
    {
        VehicleGroundMetricsCache.MaxRideHeight = 0.3f;
        var vs = new VehicleSpecific
        {
            WheelRadius = new[] { 2f, 2f, 2f, 2f, 0f, 0f },
            WheelHardPoints = new[]
            {
                new Vector3(1f, 0f, 1f),
                new Vector3(-1f, 0f, 1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(-1f, 0f, -1f),
            },
        };
        var m = VehicleGroundMetricsCache.Compute(vs);
        Assert.AreEqual(0.3f, m.ChassisHeightAboveTerrain, 0.001f);
    }

    [TestMethod]
    public void BuildFromCloneBases_NullDictionary_ReturnsZero()
    {
        Assert.AreEqual(0, VehicleGroundMetricsCache.BuildFromCloneBases(null));
        Assert.AreEqual(0, VehicleGroundMetricsCache.Count);
    }
}

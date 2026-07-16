using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

using AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Data-driven goldens for <c>hkDefaultBrake_update</c> @ <c>0x64e6f0</c>
/// (Task C8). Loads <c>Physics/oracles/brake_goldens.json</c>.
/// Asserts against <see cref="HkVehicleBrake"/> bit-exactly where hex siblings are present.
/// </summary>
[TestClass]
public class BrakeOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/brake_goldens.json";
    private const string GoldensFileName = "brake_goldens.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void AllVectors_MatchHkVehicleBrake_BitExact()
    {
        var goldens = LoadGoldens();
        Assert.IsNotNull(goldens.Vectors, "goldens.vectors required");
        Assert.AreEqual(8, goldens.Vectors!.Count, "expected 8 golden vectors");

        foreach (var v in goldens.Vectors)
        {
            Assert.IsNotNull(v.Inputs, $"vector {v.Id} missing inputs");
            Assert.IsNotNull(v.Expected, $"vector {v.Id} missing expected");
            var i = v.Inputs!;
            var exp = v.Expected!;

            AssertHexMatchesDecimal(i.Spin, i.SpinHex, v.Id, "spin");
            AssertHexMatchesDecimal(i.Radius, i.RadiusHex, v.Id, "radius");
            AssertHexMatchesDecimal(i.WheelsMass, i.WheelsMassHex, v.Id, "wheelsMass");
            AssertHexMatchesDecimal(i.InvDt, i.InvDtHex, v.Id, "invDt");
            AssertHexMatchesDecimal(i.Pedal, i.PedalHex, v.Id, "pedal");
            AssertHexMatchesDecimal(i.MaxBreakingTorque, i.MaxBreakingTorqueHex, v.Id, "maxBreakingTorque");
            AssertHexMatchesDecimal(exp.BrakeTorque, exp.BrakeTorqueHex, v.Id, "brakeTorque");

            HkVehicleBrake.UpdateWheel(
                pedalInput: i.Pedal,
                handbrakeActive: i.HandbrakeActive,
                maxBreakingTorque: i.MaxBreakingTorque,
                minPedalInputToBlock: i.MinPedalInputToBlock,
                handbrakeConnected: i.HandbrakeConnected,
                spin: i.Spin,
                radius: i.Radius,
                wheelsMass: i.WheelsMass,
                invDt: i.InvDt,
                out var torque,
                out var blocked);

            var expTorque = ParseHexFloat(exp.BrakeTorqueHex!);
            AssertBitExact(expTorque, torque, v.Id, v.Name, "brakeTorque");
            Assert.AreEqual(exp.IsBlocked, blocked, $"vector {v.Id} ({v.Name}) isBlocked");
        }
    }

    [TestMethod]
    public void PedalDerivation_MatchesThrottleReverseComponent()
    {
        var goldens = LoadGoldens();
        Assert.IsNotNull(goldens.PedalDerivation);
        Assert.IsTrue(goldens.PedalDerivation!.Count >= 5);

        foreach (var row in goldens.PedalDerivation)
        {
            AssertHexMatchesDecimal(row.Throttle, row.ThrottleHex, 0, "throttle");
            AssertHexMatchesDecimal(row.Pedal, row.PedalHex, 0, "pedal");
            var pedal = HkVehicleBrake.DeriveBrakePedal(row.Throttle);
            AssertBitExact(ParseHexFloat(row.PedalHex!), pedal, 0, "pedalDerivation", "pedal");
        }
    }

    [TestMethod]
    public void ServicePeak_MatchesComputeServiceBrakeTorque()
    {
        // Fixture: full pedal × maxT is the peak used by the opposing-spin clamp.
        Assert.AreEqual(
            100f,
            HkVehicleBrake.ComputeServiceBrakeTorque(100f, 1f));
        Assert.AreEqual(
            BitConverter.SingleToInt32Bits(500f),
            BitConverter.SingleToInt32Bits(HkVehicleBrake.ComputeServiceBrakeTorque(1000f, 0.5f)));
    }

    private static float ParseHexFloat(string hex)
    {
        Assert.AreEqual(8, hex.Length, $"hex string '{hex}' must be 8 hex chars (raw LE float32)");
        return BitConverter.ToSingle(Convert.FromHexString(hex), 0);
    }

    private static void AssertHexMatchesDecimal(float decimalValue, string? hex, int id, string field)
    {
        Assert.IsNotNull(hex, $"golden #{id} missing {field}Hex");
        var hexValue = ParseHexFloat(hex!);
        Assert.AreEqual(
            BitConverter.SingleToInt32Bits(decimalValue),
            BitConverter.SingleToInt32Bits(hexValue),
            $"golden #{id} {field}: decimal {decimalValue} bit pattern does not match {field}Hex '{hex}' (decoded {hexValue})");
    }

    private static void AssertBitExact(float expected, float actual, int id, string? name, string field)
    {
        Assert.AreEqual(
            BitConverter.SingleToInt32Bits(expected),
            BitConverter.SingleToInt32Bits(actual),
            $"golden #{id} ({name}) {field}: expected bit-exact {expected} (0x{BitConverter.SingleToInt32Bits(expected):X8}) but got {actual} (0x{BitConverter.SingleToInt32Bits(actual):X8})");
    }

    private static GoldensFile LoadGoldens()
    {
        var path = ResolveGoldensPath();
        if (!File.Exists(path))
            Assert.Fail($"Missing goldens file: {path}");

        var json = File.ReadAllText(path);
        var goldens = JsonSerializer.Deserialize<GoldensFile>(json, JsonOptions);
        Assert.IsNotNull(goldens, "failed to deserialize brake_goldens.json");
        return goldens!;
    }

    private static string ResolveGoldensPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, GoldensRelativePath),
            Path.Combine(Directory.GetCurrentDirectory(), GoldensRelativePath),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "Physics", "oracles", GoldensFileName);
            if (File.Exists(probe))
                return probe;

            var srcProbe = Path.Combine(
                dir.FullName,
                "src", "AutoCore.Game.Tests", "Physics", "oracles", GoldensFileName);
            if (File.Exists(srcProbe))
                return srcProbe;

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, GoldensRelativePath);
    }

    private sealed class GoldensFile
    {
        public List<GoldenVector>? Vectors { get; set; }
        public List<PedalRow>? PedalDerivation { get; set; }
    }

    private sealed class GoldenVector
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public GoldenInputs? Inputs { get; set; }
        public GoldenExpected? Expected { get; set; }
    }

    private sealed class GoldenInputs
    {
        public float Spin { get; set; }
        public string? SpinHex { get; set; }
        public float Radius { get; set; }
        public string? RadiusHex { get; set; }
        public float WheelsMass { get; set; }
        public string? WheelsMassHex { get; set; }
        public float InvDt { get; set; }
        public string? InvDtHex { get; set; }
        public float Pedal { get; set; }
        public string? PedalHex { get; set; }
        public float MaxBreakingTorque { get; set; }
        public string? MaxBreakingTorqueHex { get; set; }
        public float MinPedalInputToBlock { get; set; }
        public bool HandbrakeConnected { get; set; }
        public bool HandbrakeActive { get; set; }
    }

    private sealed class GoldenExpected
    {
        public float BrakeTorque { get; set; }
        public string? BrakeTorqueHex { get; set; }
        public bool IsBlocked { get; set; }
    }

    private sealed class PedalRow
    {
        public float Throttle { get; set; }
        public string? ThrottleHex { get; set; }
        public float Pedal { get; set; }
        public string? PedalHex { get; set; }
    }
}

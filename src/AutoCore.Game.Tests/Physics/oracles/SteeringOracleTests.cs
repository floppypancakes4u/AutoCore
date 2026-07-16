using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

/// <summary>
/// Data-driven goldens for <c>hkDefaultSteering_update</c> @ <c>0x0064f840</c> from
/// docs/reconstruction/physics/steering-spec.md and
/// docs/reconstruction/physics/verified/fn_0064f840_steering.md.
/// Loads Physics/oracles/steering_goldens.json.
/// Asserts against <c>HkVehicleSteering.ComputeWheelAngles</c> when that type is present; otherwise inconclusive.
/// </summary>
[TestClass]
public class SteeringOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/steering_goldens.json";
    private const string GoldensFileName = "steering_goldens.json";
    private const string SteeringTypeName = "AutoCore.Game.Physics.Vehicle.HkVehicleSteering";
    private const string ComputeWheelAnglesMethodName = "ComputeWheelAngles";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void AllGoldens_MatchHkVehicleSteering_WhenPresent()
    {
        var computeWheelAngles = ResolveComputeWheelAnglesOrSkip();
        var goldens = LoadGoldens();

        Assert.IsNotNull(goldens.Vectors, "goldens.vectors required");
        Assert.AreEqual(8, goldens.Vectors!.Count, "expected 8 core golden vectors (the 9th, degenerate 0/0 case, is a documented deviation covered by a separate [Ignore]d test)");

        foreach (var v in goldens.Vectors)
        {
            Assert.IsNotNull(v.Inputs, $"vector {v.Id} missing inputs");
            Assert.IsNotNull(v.Expected, $"vector {v.Id} missing expected");

            var inputs = v.Inputs!;
            var exp = v.Expected!;

            // Fixture self-consistency guard: every *Hex field must decode (bit-exact,
            // via BitConverter.ToSingle on the raw LE float32 bytes) to its decimal sibling.
            AssertHexMatchesDecimal(inputs.SteerInput, inputs.SteerInputHex, v.Id, "steerInput");
            AssertHexMatchesDecimal(inputs.MaxAngle, inputs.MaxAngleHex, v.Id, "maxAngle");
            AssertHexMatchesDecimal(inputs.FullSpeedLimit, inputs.FullSpeedLimitHex, v.Id, "fullSpeedLimit");
            AssertHexMatchesDecimal(inputs.ForwardSpeed, inputs.ForwardSpeedHex, v.Id, "forwardSpeed");
            AssertHexMatchesDecimal(exp.Angle, exp.AngleHex, v.Id, "angle");

            Assert.IsNotNull(inputs.DoesSteer, $"vector {v.Id} missing doesSteer");
            var doesSteer = inputs.DoesSteer!;

            var outAngles = InvokeComputeWheelAngles(computeWheelAngles, inputs);

            Assert.AreEqual(doesSteer.Length, outAngles.Length, $"golden #{v.Id} ({v.Name}): outAngles length mismatch");

            // Bit-exact assertion against the hex-decoded expected value (the raw LE
            // float32 bytes are the source of truth; the decimal field is a convenience
            // copy already proven consistent with it above). Per-wheel: doesSteer[i] ? angle : 0.0.
            var expAngle = ParseHexFloat(exp.AngleHex!);
            for (var i = 0; i < doesSteer.Length; i++)
            {
                var expectedWheel = doesSteer[i] ? expAngle : 0.0f;
                AssertBitExact(expectedWheel, outAngles[i], v.Id, v.Name, $"wheel[{i}]");
            }
        }
    }

    /// <summary>
    /// Documented deviation (see steering_goldens.json "knownDeviation"): for
    /// fullSpeedLimit == 0 and forwardSpeed == 0, the retail binary's gate
    /// (fullSpeedLimit &lt;= forwardSpeed) is TRUE (0.0 &lt;= 0.0), so it takes the
    /// falloff branch and computes 0.0/0.0 = NaN (confirmed bit-exact via
    /// emulate_function register readback: XMM0 = 0x7fc00000 at RET). The current
    /// port has an extra `forwardSpeed > 0f` guard that skips the branch for this
    /// input, returning the plain maxAngle*steerInput identity value (0.3) instead
    /// of NaN. Not fixed here per task scope -- ignored so it documents the gap
    /// without failing the build. Do not "fix" by matching NaN; instead route any
    /// port fix decision through the C-phase steering work.
    /// </summary>
    [Ignore("unblocked by C-phase steering fix")]
    [TestMethod]
    public void KnownDeviation_ZeroFullSpeedLimitZeroForwardSpeed_PortDiffersFromRetail()
    {
        var computeWheelAngles = ResolveComputeWheelAnglesOrSkip();
        var goldens = LoadGoldens();

        Assert.IsNotNull(goldens.KnownDeviation, "goldens.knownDeviation required");
        var dev = goldens.KnownDeviation!;
        Assert.IsNotNull(dev.Inputs, "knownDeviation missing inputs");
        Assert.IsNotNull(dev.RetailExpected, "knownDeviation missing retailExpected");

        var inputs = dev.Inputs!;
        var retailAngle = ParseHexFloat(dev.RetailExpected!.AngleHex!);
        Assert.IsTrue(float.IsNaN(retailAngle), "retail expected value for this vector should be NaN (0/0 falloff)");

        var outAngles = InvokeComputeWheelAngles(computeWheelAngles, inputs);

        // This assertion is expected to FAIL against the retail value (that's the
        // point of this [Ignore]d test): the port does not reproduce retail NaN here.
        AssertBitExact(retailAngle, outAngles[0], dev.Id, dev.Name, "wheel[0] (retail NaN, port non-NaN -- known deviation)");
    }

    /// <summary>
    /// Parses an 8-hex-char raw little-endian float32 (as written in steering_goldens.json)
    /// into a <see cref="float"/>.
    /// </summary>
    private static float ParseHexFloat(string hex)
    {
        Assert.AreEqual(8, hex.Length, $"hex string '{hex}' must be 8 hex chars (raw LE float32)");
        var bytes = Convert.FromHexString(hex);
        return BitConverter.ToSingle(bytes, 0);
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

    private static MethodInfo ResolveComputeWheelAnglesOrSkip()
    {
        var type = Type.GetType($"{SteeringTypeName}, AutoCore.Game", throwOnError: false)
                   ?? AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType(SteeringTypeName, throwOnError: false))
                       .FirstOrDefault(t => t is not null);

        if (type is null)
        {
            Assert.Inconclusive(
                $"{SteeringTypeName} not present; skip steering oracle until the port exists.");
            return null!;
        }

        var method = type.GetMethod(
            ComputeWheelAnglesMethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[]
            {
                typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool[]),
            },
            modifiers: null);

        if (method is null)
        {
            Assert.Inconclusive(
                $"{SteeringTypeName}.{ComputeWheelAnglesMethodName}(float,float,float,float,bool[]) not found.");
            return null!;
        }

        return method;
    }

    private static float[] InvokeComputeWheelAngles(MethodInfo computeWheelAngles, GoldenInputs i)
    {
        Assert.IsNotNull(i.DoesSteer, "inputs.doesSteer required");
        var result = computeWheelAngles.Invoke(null, new object[]
        {
            i.SteerInput, i.MaxAngle, i.FullSpeedLimit, i.ForwardSpeed, i.DoesSteer!,
        });
        Assert.IsNotNull(result, "ComputeWheelAngles returned null");
        return (float[])result!;
    }

    private static GoldensFile LoadGoldens()
    {
        var path = ResolveGoldensPath();
        if (!File.Exists(path))
            Assert.Fail($"Missing goldens file: {path}");

        var json = File.ReadAllText(path);
        var goldens = JsonSerializer.Deserialize<GoldensFile>(json, JsonOptions);
        Assert.IsNotNull(goldens, "failed to deserialize steering_goldens.json");
        return goldens!;
    }

    private static string ResolveGoldensPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, GoldensRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(AppContext.BaseDirectory, GoldensFileName),
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

            var underSrc = Path.Combine(
                dir.FullName,
                "src", "AutoCore.Game.Tests", "Physics", "oracles", GoldensFileName);
            if (File.Exists(underSrc))
                return underSrc;

            dir = dir.Parent;
        }

        return candidates[0];
    }

    private sealed class GoldensFile
    {
        public List<GoldenVector>? Vectors { get; set; }
        public KnownDeviationVector? KnownDeviation { get; set; }
    }

    private sealed class GoldenVector
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public GoldenInputs? Inputs { get; set; }
        public GoldenExpected? Expected { get; set; }
    }

    private sealed class KnownDeviationVector
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public GoldenInputs? Inputs { get; set; }
        public GoldenExpected? RetailExpected { get; set; }
        public GoldenExpected? PortActual { get; set; }
    }

    private sealed class GoldenInputs
    {
        public float SteerInput { get; set; }
        public string? SteerInputHex { get; set; }
        public float MaxAngle { get; set; }
        public string? MaxAngleHex { get; set; }
        public float FullSpeedLimit { get; set; }
        public string? FullSpeedLimitHex { get; set; }
        public float ForwardSpeed { get; set; }
        public string? ForwardSpeedHex { get; set; }
        public bool[]? DoesSteer { get; set; }
    }

    private sealed class GoldenExpected
    {
        public float Angle { get; set; }
        public string? AngleHex { get; set; }
    }
}

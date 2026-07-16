using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

/// <summary>
/// Data-driven goldens for <c>hkDefaultAerodynamics::update</c> @ <c>0x0064dae0</c>
/// from docs/reconstruction/physics/0.6-aerodynamics.md and
/// docs/reconstruction/physics/verified/fn_0064dae0_aero.md.
/// Loads Physics/oracles/aero_goldens.json.
/// Asserts against <c>HkVehicleAerodynamics.ComputeForce</c> when that type is present; otherwise inconclusive.
/// </summary>
[TestClass]
public class AeroOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/aero_goldens.json";
    private const string GoldensFileName = "aero_goldens.json";
    private const string AeroTypeName = "AutoCore.Game.Physics.Vehicle.HkVehicleAerodynamics";
    private const string ComputeForceMethodName = "ComputeForce";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void AllGoldens_MatchHkVehicleAerodynamics_WhenPresent()
    {
        var computeForce = ResolveComputeForceOrSkip();
        var goldens = LoadGoldens();

        Assert.IsNotNull(goldens.Vectors, "goldens.vectors required");
        Assert.AreEqual(8, goldens.Vectors!.Count, "expected 8 golden vectors from 0.6-aerodynamics.md");

        foreach (var v in goldens.Vectors)
        {
            Assert.IsNotNull(v.Inputs, $"vector {v.Id} missing inputs");
            Assert.IsNotNull(v.Expected, $"vector {v.Id} missing expected");

            var inputs = v.Inputs!;
            var exp = v.Expected!;

            // Fixture self-consistency guard: every *Hex field must decode (bit-exact,
            // via BitConverter.ToSingle on the raw LE float32 bytes) to its decimal sibling.
            AssertHexMatchesDecimal(inputs.Rho, inputs.RhoHex, v.Id, "rho");
            AssertHexMatchesDecimal(inputs.FrontalArea, inputs.FrontalAreaHex, v.Id, "frontalArea");
            AssertHexMatchesDecimal(inputs.DragCoefficient, inputs.DragCoefficientHex, v.Id, "dragCoefficient");
            AssertHexMatchesDecimal(inputs.LiftCoefficient, inputs.LiftCoefficientHex, v.Id, "liftCoefficient");
            AssertHexMatchesDecimal(inputs.ExtraGx, inputs.ExtraGxHex, v.Id, "extraGx");
            AssertHexMatchesDecimal(inputs.ExtraGy, inputs.ExtraGyHex, v.Id, "extraGy");
            AssertHexMatchesDecimal(inputs.ExtraGz, inputs.ExtraGzHex, v.Id, "extraGz");
            AssertHexMatchesDecimal(inputs.WorldFrontX, inputs.WorldFrontXHex, v.Id, "worldFrontX");
            AssertHexMatchesDecimal(inputs.WorldFrontY, inputs.WorldFrontYHex, v.Id, "worldFrontY");
            AssertHexMatchesDecimal(inputs.WorldFrontZ, inputs.WorldFrontZHex, v.Id, "worldFrontZ");
            AssertHexMatchesDecimal(inputs.WorldUpX, inputs.WorldUpXHex, v.Id, "worldUpX");
            AssertHexMatchesDecimal(inputs.WorldUpY, inputs.WorldUpYHex, v.Id, "worldUpY");
            AssertHexMatchesDecimal(inputs.WorldUpZ, inputs.WorldUpZHex, v.Id, "worldUpZ");
            AssertHexMatchesDecimal(inputs.LinVelX, inputs.LinVelXHex, v.Id, "linVelX");
            AssertHexMatchesDecimal(inputs.LinVelY, inputs.LinVelYHex, v.Id, "linVelY");
            AssertHexMatchesDecimal(inputs.LinVelZ, inputs.LinVelZHex, v.Id, "linVelZ");
            AssertHexMatchesDecimal(inputs.Mass, inputs.MassHex, v.Id, "mass");
            AssertHexMatchesDecimal(exp.Fx, exp.FxHex, v.Id, "fx");
            AssertHexMatchesDecimal(exp.Fy, exp.FyHex, v.Id, "fy");
            AssertHexMatchesDecimal(exp.Fz, exp.FzHex, v.Id, "fz");

            var (fx, fy, fz) = InvokeComputeForce(computeForce, inputs);

            // Bit-exact assertion against the hex-decoded expected value (the raw LE
            // float32 bytes are the source of truth; the decimal field is a convenience
            // copy already proven consistent with it above).
            var expFx = ParseHexFloat(exp.FxHex!);
            var expFy = ParseHexFloat(exp.FyHex!);
            var expFz = ParseHexFloat(exp.FzHex!);

            AssertBitExact(expFx, fx, v.Id, v.Name, "fx");
            AssertBitExact(expFy, fy, v.Id, v.Name, "fy");
            AssertBitExact(expFz, fz, v.Id, v.Name, "fz");
        }
    }

    /// <summary>
    /// Parses an 8-hex-char raw little-endian float32 (as written in aero_goldens.json)
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

    private static MethodInfo ResolveComputeForceOrSkip()
    {
        var type = Type.GetType($"{AeroTypeName}, AutoCore.Game", throwOnError: false)
                   ?? AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType(AeroTypeName, throwOnError: false))
                       .FirstOrDefault(t => t is not null);

        if (type is null)
        {
            Assert.Inconclusive(
                $"{AeroTypeName} not present; skip aero oracle until the port exists.");
            return null!;
        }

        var method = type.GetMethod(
            ComputeForceMethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[]
            {
                typeof(float), typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float),
                typeof(float), typeof(float), typeof(float),
                typeof(float),
            },
            modifiers: null);

        if (method is null)
        {
            Assert.Inconclusive(
                $"{AeroTypeName}.{ComputeForceMethodName}(17 floats) not found.");
            return null!;
        }

        return method;
    }

    private static (float fx, float fy, float fz) InvokeComputeForce(
        MethodInfo computeForce,
        GoldenInputs i)
    {
        var result = computeForce.Invoke(null, new object[]
        {
            i.Rho, i.FrontalArea, i.DragCoefficient, i.LiftCoefficient,
            i.ExtraGx, i.ExtraGy, i.ExtraGz,
            i.WorldFrontX, i.WorldFrontY, i.WorldFrontZ,
            i.WorldUpX, i.WorldUpY, i.WorldUpZ,
            i.LinVelX, i.LinVelY, i.LinVelZ,
            i.Mass,
        });
        Assert.IsNotNull(result, "ComputeForce returned null");

        if (result is ValueTuple<float, float, float> t)
            return t;

        var type = result.GetType();
        if (type.IsGenericType && type.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
        {
            var fields = type.GetFields();
            if (fields.Length >= 3)
            {
                return (
                    Convert.ToSingle(fields[0].GetValue(result)),
                    Convert.ToSingle(fields[1].GetValue(result)),
                    Convert.ToSingle(fields[2].GetValue(result)));
            }
        }

        Assert.Fail($"ComputeForce return type {type.FullName} not recognized; expected (float fx, float fy, float fz).");
        return default;
    }

    private static GoldensFile LoadGoldens()
    {
        var path = ResolveGoldensPath();
        if (!File.Exists(path))
            Assert.Fail($"Missing goldens file: {path}");

        var json = File.ReadAllText(path);
        var goldens = JsonSerializer.Deserialize<GoldensFile>(json, JsonOptions);
        Assert.IsNotNull(goldens, "failed to deserialize aero_goldens.json");
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
        public float Rho { get; set; }
        public string? RhoHex { get; set; }
        public float FrontalArea { get; set; }
        public string? FrontalAreaHex { get; set; }
        public float DragCoefficient { get; set; }
        public string? DragCoefficientHex { get; set; }
        public float LiftCoefficient { get; set; }
        public string? LiftCoefficientHex { get; set; }
        public float ExtraGx { get; set; }
        public string? ExtraGxHex { get; set; }
        public float ExtraGy { get; set; }
        public string? ExtraGyHex { get; set; }
        public float ExtraGz { get; set; }
        public string? ExtraGzHex { get; set; }
        public float WorldFrontX { get; set; }
        public string? WorldFrontXHex { get; set; }
        public float WorldFrontY { get; set; }
        public string? WorldFrontYHex { get; set; }
        public float WorldFrontZ { get; set; }
        public string? WorldFrontZHex { get; set; }
        public float WorldUpX { get; set; }
        public string? WorldUpXHex { get; set; }
        public float WorldUpY { get; set; }
        public string? WorldUpYHex { get; set; }
        public float WorldUpZ { get; set; }
        public string? WorldUpZHex { get; set; }
        public float LinVelX { get; set; }
        public string? LinVelXHex { get; set; }
        public float LinVelY { get; set; }
        public string? LinVelYHex { get; set; }
        public float LinVelZ { get; set; }
        public string? LinVelZHex { get; set; }
        public float Mass { get; set; }
        public string? MassHex { get; set; }
    }

    private sealed class GoldenExpected
    {
        public float Fx { get; set; }
        public string? FxHex { get; set; }
        public float Fy { get; set; }
        public string? FyHex { get; set; }
        public float Fz { get; set; }
        public string? FzHex { get; set; }
    }
}

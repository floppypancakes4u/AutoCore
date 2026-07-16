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
    private const float Tolerance = 1e-4f;

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
            var (fx, fy, fz) = InvokeComputeForce(computeForce, inputs);

            var exp = v.Expected!;
            Assert.AreEqual(exp.Fx, fx, Tolerance, $"golden #{v.Id} ({v.Name}) fx");
            Assert.AreEqual(exp.Fy, fy, Tolerance, $"golden #{v.Id} ({v.Name}) fy");
            Assert.AreEqual(exp.Fz, fz, Tolerance, $"golden #{v.Id} ({v.Name}) fz");
        }
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
        public float FrontalArea { get; set; }
        public float DragCoefficient { get; set; }
        public float LiftCoefficient { get; set; }
        public float ExtraGx { get; set; }
        public float ExtraGy { get; set; }
        public float ExtraGz { get; set; }
        public float WorldFrontX { get; set; }
        public float WorldFrontY { get; set; }
        public float WorldFrontZ { get; set; }
        public float WorldUpX { get; set; }
        public float WorldUpY { get; set; }
        public float WorldUpZ { get; set; }
        public float LinVelX { get; set; }
        public float LinVelY { get; set; }
        public float LinVelZ { get; set; }
        public float Mass { get; set; }
    }

    private sealed class GoldenExpected
    {
        public float Fx { get; set; }
        public float Fy { get; set; }
        public float Fz { get; set; }
    }
}

using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

using AutoCore.Game.Structures;

/// <summary>
/// Data-driven goldens for <c>CVOGVehicle::MoveToTarget3DPoint</c> @ <c>0x004fc650</c>
/// from docs/reconstruction/physics/drive-controller-spec.md §5.
/// Loads Physics/oracles/driveController_goldens.json.
/// Asserts against <c>VehicleDriveController.ComputeAxes</c> when that type is present; otherwise inconclusive.
/// </summary>
[TestClass]
public class DriveControllerOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/driveController_goldens.json";
    private const string GoldensFileName = "driveController_goldens.json";
    private const string ControllerTypeName = "AutoCore.Game.Physics.Vehicle.VehicleDriveController";
    private const string ComputeAxesMethodName = "ComputeAxes";
    private const float Tolerance = 1e-3f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void AllGoldens_MatchVehicleDriveController_WhenPresent()
    {
        var computeAxes = ResolveComputeAxesOrSkip();
        var goldens = LoadGoldens();

        Assert.IsNotNull(goldens.Config, "goldens.config required");
        Assert.IsNotNull(goldens.Vectors, "goldens.vectors required");
        Assert.AreEqual(4, goldens.Vectors.Count, "expected 4 golden vectors from drive-controller-spec.md §5");

        var cfg = goldens.Config!;
        var position = ToVector3(cfg.Position, "config.position");
        var right = ToVector3(cfg.Right, "config.right");
        var forward = ToVector3(cfg.Forward, "config.forward");

        foreach (var v in goldens.Vectors!)
        {
            Assert.IsNotNull(v.Inputs, $"vector {v.Id} missing inputs");
            Assert.IsNotNull(v.Expected, $"vector {v.Id} missing expected");

            var aim = ToVector3(v.Inputs!.Aim, $"vector {v.Id} aim");
            var velocity = ToVector3(v.Inputs.Velocity, $"vector {v.Id} velocity");

            var (throttle, steer, sharp) = InvokeComputeAxes(
                computeAxes,
                position,
                right,
                forward,
                velocity,
                aim,
                (float)cfg.AcceptDist,
                (float)cfg.CruiseScale,
                cfg.AllowReverse);

            var exp = v.Expected!;
            Assert.AreEqual(
                (float)exp.Throttle,
                throttle,
                Tolerance,
                $"golden #{v.Id} ({v.Name}) throttle");
            Assert.AreEqual(
                (float)exp.Steer,
                steer,
                Tolerance,
                $"golden #{v.Id} ({v.Name}) steer");
            Assert.AreEqual(
                (byte)exp.Sharp,
                sharp,
                $"golden #{v.Id} ({v.Name}) sharp");
        }
    }

    private static MethodInfo ResolveComputeAxesOrSkip()
    {
        var type = Type.GetType($"{ControllerTypeName}, AutoCore.Game", throwOnError: false)
                   ?? AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType(ControllerTypeName, throwOnError: false))
                       .FirstOrDefault(t => t is not null);

        if (type is null)
        {
            Assert.Inconclusive(
                $"{ControllerTypeName} not present; skip drive-controller oracle until the port exists.");
            return null!;
        }

        // Prefer full signature with optional alwaysDrive (default false in production).
        var withAlwaysDrive = new[]
        {
            typeof(Vector3), typeof(Vector3), typeof(Vector3),
            typeof(Vector3), typeof(Vector3),
            typeof(float), typeof(float), typeof(bool), typeof(bool),
        };
        var withoutAlwaysDrive = new[]
        {
            typeof(Vector3), typeof(Vector3), typeof(Vector3),
            typeof(Vector3), typeof(Vector3),
            typeof(float), typeof(float), typeof(bool),
        };

        var method = type.GetMethod(
                ComputeAxesMethodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: withAlwaysDrive,
                modifiers: null)
            ?? type.GetMethod(
                ComputeAxesMethodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: withoutAlwaysDrive,
                modifiers: null)
            ?? type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == ComputeAxesMethodName);

        if (method is null)
        {
            Assert.Inconclusive(
                $"{ControllerTypeName}.{ComputeAxesMethodName} not found.");
            return null!;
        }

        return method;
    }

    private static (float throttle, float steer, byte sharp) InvokeComputeAxes(
        MethodInfo computeAxes,
        Vector3 position,
        Vector3 right,
        Vector3 forward,
        Vector3 velocity,
        Vector3 aim,
        float acceptDist,
        float cruiseScale,
        bool allowReverse)
    {
        object? result;
        try
        {
            var paramCount = computeAxes.GetParameters().Length;
            object[] args = paramCount >= 9
                ? new object[]
                {
                    position, right, forward, velocity, aim,
                    acceptDist, cruiseScale, allowReverse,
                    false, // alwaysDrive — goldens use normal arrival gate
                }
                : new object[]
                {
                    position, right, forward, velocity, aim,
                    acceptDist, cruiseScale, allowReverse,
                };

            result = computeAxes.Invoke(null, args);
        }
        catch (TargetParameterCountException)
        {
            Assert.Inconclusive(
                $"{ControllerTypeName}.{ComputeAxesMethodName} signature mismatch; expected " +
                "(Vector3 pos, right, forward, velocity, aim, float acceptDist, float cruiseScale, bool allowReverse[, bool alwaysDrive]).");
            return default;
        }
        catch (ArgumentException)
        {
            Assert.Inconclusive(
                $"{ControllerTypeName}.{ComputeAxesMethodName} argument types mismatch.");
            return default;
        }

        Assert.IsNotNull(result, "ComputeAxes returned null");

        // Support (float, float, byte) and (float, float, int).
        if (result is ValueTuple<float, float, byte> tByte)
            return tByte;
        if (result is ValueTuple<float, float, int> tInt)
            return (tInt.Item1, tInt.Item2, (byte)tInt.Item3);

        // Reflection ValueTuple boxes as ITuple when System.Runtime.CompilerServices.ITuple is available.
        var type = result.GetType();
        if (type.IsGenericType && type.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
        {
            var fields = type.GetFields();
            if (fields.Length >= 3)
            {
                var thr = Convert.ToSingle(fields[0].GetValue(result));
                var st = Convert.ToSingle(fields[1].GetValue(result));
                var sh = Convert.ToByte(fields[2].GetValue(result));
                return (thr, st, sh);
            }
        }

        Assert.Fail(
            $"ComputeAxes return type {type.FullName} not recognized; expected (float throttle, float steer, byte|int sharp).");
        return default;
    }

    private static Vector3 ToVector3(float[]? arr, string label)
    {
        Assert.IsNotNull(arr, $"{label} required");
        Assert.AreEqual(3, arr!.Length, $"{label} must be [x,y,z]");
        return new Vector3(arr[0], arr[1], arr[2]);
    }

    private static GoldensFile LoadGoldens()
    {
        var path = ResolveGoldensPath();
        if (!File.Exists(path))
            Assert.Fail($"Missing goldens file: {path}");

        var json = File.ReadAllText(path);
        var goldens = JsonSerializer.Deserialize<GoldensFile>(json, JsonOptions);
        Assert.IsNotNull(goldens, "failed to deserialize driveController_goldens.json");
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
        public GoldenConfig? Config { get; set; }
        public List<GoldenVector>? Vectors { get; set; }
    }

    private sealed class GoldenConfig
    {
        public float[]? Position { get; set; }
        public float[]? Right { get; set; }
        public float[]? Forward { get; set; }
        public double AcceptDist { get; set; }
        public double CruiseScale { get; set; }
        public bool AllowReverse { get; set; }
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
        public float[]? Aim { get; set; }
        public float[]? Velocity { get; set; }
    }

    private sealed class GoldenExpected
    {
        public double Throttle { get; set; }
        public double Steer { get; set; }
        public int Sharp { get; set; }
    }
}

using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

/// <summary>
/// Data-driven goldens for <c>VehicleEngine::torqueCurve2D</c> @ <c>0x4a9750</c>
/// from docs/reconstruction/physics/engine-torque-spec.md §2.
/// Loads Physics/oracles/torqueCurve2D_goldens.json. Skips if TorqueCurve2D is absent.
/// </summary>
[TestClass]
public class TorqueCurve2DOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/torqueCurve2D_goldens.json";
    private const string TorqueCurve2DTypeName = "AutoCore.Game.Physics.Vehicle.TorqueCurve2D";
    private const string EvaluateMethodName = "Evaluate";
    private const float Tolerance = 1e-6f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void AllGoldens_MatchTorqueCurve2D_WhenPresent()
    {
        var evaluate = ResolveEvaluateOrSkip();
        var goldens = LoadGoldens();

        Assert.IsNotNull(goldens.Config, "goldens.config required");
        Assert.IsNotNull(goldens.Vectors, "goldens.vectors required");
        Assert.AreEqual(6, goldens.Vectors.Count, "expected 6 golden vectors from engine-torque-spec.md §2");

        var cfg = goldens.Config;
        Assert.IsNotNull(cfg.Factors, "goldens.config.factors required");
        Assert.IsNotNull(cfg.Lut, "goldens.config.lut required");

        var factors = cfg.Factors.Select(f => (float)f).ToArray();
        var lut = cfg.Lut.Select(b => (byte)b).ToArray();

        foreach (var v in goldens.Vectors)
        {
            Assert.IsNotNull(v.Inputs, $"vector {v.Id} missing inputs");
            var actual = InvokeEvaluate(
                evaluate,
                v.Inputs.Enabled,
                cfg.Rows,
                cfg.Cols,
                (float)cfg.RangeScale,
                factors,
                lut,
                (float)v.Inputs.Rpm,
                (float)v.Inputs.Throttle);

            Assert.AreEqual(
                (float)v.Expected,
                actual,
                Tolerance,
                $"golden #{v.Id} ({v.Name}): rpm={v.Inputs.Rpm} thr={v.Inputs.Throttle} enabled={v.Inputs.Enabled}");
        }
    }

    private static MethodInfo ResolveEvaluateOrSkip()
    {
        var type = Type.GetType($"{TorqueCurve2DTypeName}, AutoCore.Game", throwOnError: false)
                   ?? AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType(TorqueCurve2DTypeName, throwOnError: false))
                       .FirstOrDefault(t => t != null);

        if (type == null)
        {
            Assert.Inconclusive(
                $"{TorqueCurve2DTypeName} not present; skip torqueCurve2D oracle until the port exists.");
            return null;
        }

        var method = type.GetMethod(
            EvaluateMethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[]
            {
                typeof(bool), typeof(int), typeof(int), typeof(float),
                typeof(float[]), typeof(byte[]), typeof(float), typeof(float),
            },
            modifiers: null);

        if (method == null || method.ReturnType != typeof(float))
        {
            Assert.Inconclusive(
                $"{TorqueCurve2DTypeName}.{EvaluateMethodName}(bool,int,int,float,float[],byte[],float,float) not found.");
            return null;
        }

        return method;
    }

    private static float InvokeEvaluate(
        MethodInfo evaluate,
        bool enabled,
        int rows,
        int cols,
        float rangeScale,
        float[] factors,
        byte[] lut,
        float rpm,
        float throttle)
    {
        var result = evaluate.Invoke(null, new object[]
        {
            enabled, rows, cols, rangeScale, factors, lut, rpm, throttle,
        });
        Assert.IsNotNull(result);
        return (float)result;
    }

    private static GoldensFile LoadGoldens()
    {
        var path = ResolveGoldensPath();
        if (!File.Exists(path))
            Assert.Fail($"Missing goldens file: {path}");

        var json = File.ReadAllText(path);
        var goldens = JsonSerializer.Deserialize<GoldensFile>(json, JsonOptions);
        Assert.IsNotNull(goldens, "failed to deserialize torqueCurve2D_goldens.json");
        return goldens;
    }

    private static string ResolveGoldensPath()
    {
        // Prefer output-dir copy (csproj CopyToOutputDirectory), then walk up for source tree.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, GoldensRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(AppContext.BaseDirectory, "torqueCurve2D_goldens.json"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var probe = Path.Combine(
                dir.FullName,
                "Physics", "oracles", "torqueCurve2D_goldens.json");
            if (File.Exists(probe))
                return probe;

            var underSrc = Path.Combine(
                dir.FullName,
                "src", "AutoCore.Game.Tests", "Physics", "oracles", "torqueCurve2D_goldens.json");
            if (File.Exists(underSrc))
                return underSrc;

            dir = dir.Parent;
        }

        return candidates[0];
    }

    private sealed class GoldensFile
    {
        public GoldenConfig Config { get; set; }
        public List<GoldenVector> Vectors { get; set; }
    }

    private sealed class GoldenConfig
    {
        public int Rows { get; set; }
        public int Cols { get; set; }
        public double RangeScale { get; set; }
        public double[] Factors { get; set; }
        public int[] Lut { get; set; }
    }

    private sealed class GoldenVector
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public GoldenInputs Inputs { get; set; }
        public double Expected { get; set; }
    }

    private sealed class GoldenInputs
    {
        public bool Enabled { get; set; }
        public double Rpm { get; set; }
        public double Throttle { get; set; }
    }
}

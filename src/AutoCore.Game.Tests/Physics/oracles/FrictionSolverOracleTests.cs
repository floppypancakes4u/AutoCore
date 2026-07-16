using System;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

/// <summary>
/// Live-captured goldens for <c>hkVehicleFrictionSolver_solve</c> @ <c>0x6c4450</c>
/// (called from <c>postTickApplyForces</c> @ <c>0x64bc70</c>), from Task B1.
/// Loads <c>Physics/oracles/frictionSolver_goldens.json</c> — 5 driving scenarios
/// (rest / launch / cruise / turn / slide) captured from <c>autoassault.exe</c> at the
/// return of the solver call, with the raw <c>setup</c>/<c>cb</c>/<c>out</c> struct blobs.
///
/// <para>See docs/reconstruction/physics/0.3-friction-solver.md §"Live capture (B1)".</para>
///
/// <para><b>Status:</b> the active tests assert fixture self-consistency (decoded decimals
/// decode bit-exactly from the raw LE blobs) and the retail behavioural contract
/// (friction-circle writebacks are zero under grip and non-zero only when saturated).
/// The bit-exact <b>port comparison</b> is <c>[Ignore]</c>d until C4 rebuilds
/// <c>HkVehicleFrictionSolver.Solve</c> onto retail's <c>cb</c>-based layout — the current
/// simplified port cannot reproduce these blobs and is expected to differ.</para>
/// </summary>
[TestClass]
public class FrictionSolverOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/frictionSolver_goldens.json";
    private const string GoldensFileName = "frictionSolver_goldens.json";

    private static readonly string[] Scenarios = { "rest", "launch", "cruise", "turn", "slide" };

    private static JsonDocument LoadGoldens()
    {
        var path = ResolveGoldensPath();
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static float DecodeLeFloat(string hex, int floatIndex)
    {
        var bytes = Convert.FromHexString(hex);
        return BitConverter.ToSingle(bytes, floatIndex * 4);
    }

    [TestMethod]
    public void AllScenarios_PresentWithRawBlobs()
    {
        using var doc = LoadGoldens();
        var scenarios = doc.RootElement.GetProperty("scenarios");
        foreach (var name in Scenarios)
        {
            Assert.IsTrue(scenarios.TryGetProperty(name, out var s), $"missing scenario '{name}'");
            var raw = s.GetProperty("raw");
            // setup = 0xC8 (2 axles x 0x64), cb = 0x130, out = 0x38 (2 axles x 0x1c) → hex length = 2x bytes.
            Assert.AreEqual(0xC8 * 2, raw.GetProperty("setupHex").GetString()!.Length, $"{name} setup size");
            Assert.AreEqual(0x130 * 2, raw.GetProperty("cbHex").GetString()!.Length, $"{name} cb size");
            Assert.AreEqual(0x38 * 2, raw.GetProperty("outHex").GetString()!.Length, $"{name} out size");
        }
    }

    [TestMethod]
    public void DecodedFields_DecodeBitExactFromRawBlobs()
    {
        using var doc = LoadGoldens();
        var scenarios = doc.RootElement.GetProperty("scenarios");
        foreach (var name in Scenarios)
        {
            var s = scenarios.GetProperty(name);
            var raw = s.GetProperty("raw");
            var dec = s.GetProperty("decoded");
            var cbHex = raw.GetProperty("cbHex").GetString()!;
            var outHex = raw.GetProperty("outHex").GetString()!;

            // cb linear impulse (+0xc0) and angular impulse (+0xd0): 4 floats each, decoded
            // decimals must equal the raw cb blob at those byte offsets, bit-exact.
            AssertVecMatchesBlob(cbHex, 0xc0, dec.GetProperty("cb_linImpulse"), $"{name}.cb_linImpulse");
            AssertVecMatchesBlob(cbHex, 0xd0, dec.GetProperty("cb_angImpulse"), $"{name}.cb_angImpulse");
            AssertVecMatchesBlob(cbHex, 0xe0, dec.GetProperty("cb_invInertiaMass"), $"{name}.cb_invInertiaMass");
            AssertVecMatchesBlob(outHex, 0x00, dec.GetProperty("out_axle0"), $"{name}.out_axle0");
            AssertVecMatchesBlob(outHex, 0x1c, dec.GetProperty("out_axle1"), $"{name}.out_axle1");

            // *Hex convenience fields must round-trip to their decimal siblings.
            AssertHexPairs(dec.GetProperty("cb_linImpulse"), dec.GetProperty("cb_linImpulseHex"), $"{name}.linImp");
            AssertHexPairs(dec.GetProperty("out_axle0"), dec.GetProperty("out_axle0Hex"), $"{name}.out0");
        }
    }

    /// <summary>
    /// Retail behavioural contract (the C4 acceptance target): the per-axle friction
    /// writebacks <c>out[0]</c> (lateral, WHEEL+0x9c) and <c>out[2]</c> (longitudinal,
    /// WHEEL+0x94) are <b>zero while the tyre grips</b> and become <b>non-zero only when the
    /// friction circle saturates</b> (the handbrake slide). This is the discriminating
    /// signature C4's solver must reproduce.
    /// </summary>
    [TestMethod]
    public void FrictionCircleWriteback_ZeroUnderGrip_ActiveOnlyWhenSaturated()
    {
        using var doc = LoadGoldens();
        var scenarios = doc.RootElement.GetProperty("scenarios");

        foreach (var grip in new[] { "rest", "launch", "cruise", "turn" })
        {
            var outHex = scenarios.GetProperty(grip).GetProperty("raw").GetProperty("outHex").GetString()!;
            Assert.AreEqual(0f, DecodeLeFloat(outHex, 0), $"{grip}: out[0] must be 0 under grip");
            Assert.AreEqual(0f, DecodeLeFloat(outHex, 2), $"{grip}: out[2] must be 0 under grip");
        }

        var slideOut = scenarios.GetProperty("slide").GetProperty("raw").GetProperty("outHex").GetString()!;
        Assert.AreNotEqual(0f, DecodeLeFloat(slideOut, 0), "slide: out[0] (lateral) must be active when saturated");
        Assert.AreNotEqual(0f, DecodeLeFloat(slideOut, 2), "slide: out[2] (longitudinal) must be active when saturated");
    }

    [TestMethod]
    [Ignore("unblocked by C4: current HkVehicleFrictionSolver.Solve uses a simplified 2-axle " +
            "layout and cannot reproduce retail's cb-based solve. Un-ignore once C4 rebuilds the " +
            "solver to the retail cb/out layout and feeds these captured inputs.")]
    public void PortSolve_ReproducesRetailImpulses_BitExact()
    {
        // C4 target: parse each scenario's cb (jacobians, invMass/invInertia at cb+0xe0/+0x100,
        // per-axle friction params, drive pack) + static setup block, run the ported solver, and
        // assert the resulting chassis impulse (cb+0xc0 linear, cb+0xd0 angular) and per-axle out
        // block match these goldens bit-exact via BitConverter.SingleToInt32Bits.
        Assert.Inconclusive("C4 not yet implemented.");
    }

    private static void AssertVecMatchesBlob(string blobHex, int byteOffset, JsonElement decoded, string ctx)
    {
        var arr = decoded;
        for (var i = 0; i < arr.GetArrayLength(); i++)
        {
            var expected = DecodeLeFloat(blobHex, byteOffset / 4 + i);
            var actual = (float)arr[i].GetDouble();
            Assert.AreEqual(
                BitConverter.SingleToInt32Bits(expected),
                BitConverter.SingleToInt32Bits(actual),
                $"{ctx}[{i}] decoded decimal must match raw blob bit-exact");
        }
    }

    private static void AssertHexPairs(JsonElement decimals, JsonElement hex, string ctx)
    {
        var h = hex.GetString()!;
        for (var i = 0; i < decimals.GetArrayLength(); i++)
        {
            var fromHex = DecodeLeFloat(h, i);
            var dec = (float)decimals[i].GetDouble();
            Assert.AreEqual(
                BitConverter.SingleToInt32Bits(fromHex),
                BitConverter.SingleToInt32Bits(dec),
                $"{ctx}[{i}] hex must round-trip to decimal bit-exact");
        }
    }

    private static string ResolveGoldensPath()
    {
        foreach (var baseDir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var p = Path.Combine(baseDir, GoldensRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p))
                return p;
        }

        // Walk up to the test project root and look under Physics/oracles.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Physics", "oracles", GoldensFileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {GoldensFileName}");
    }
}

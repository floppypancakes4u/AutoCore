using System;
using System.IO;
using System.Text.Json;
using AutoCore.Game.Physics.Vehicle;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Physics.Oracles;

/// <summary>
/// Bit-exact goldens for <c>hkDefaultSuspension::update</c> @ <c>0x64de50</c> (Task B2).
/// Loads <c>Physics/oracles/suspension_goldens.json</c>. Expected values are
/// <b>decompile-derived</b> — computed independently from the disassembled force formula in
/// float32 op-order, not from the port under test — matching the B6 aero-oracle methodology.
///
/// <para>Formula: <c>force = ((restLength-currentLength)*strength*scalingFactor
/// − damp*closingSpeed) * gScale</c>, <c>gScale = (RB+0x2c==0) ? 0 : 1/(RB+0x2c)</c>,
/// <c>damp = closingSpeed &gt;= 0 ? extension : compression</c>, airborne → 0.
/// B4 live-confirmed <c>RB+0x2c = invMass</c>, so <c>gScale = 1/invMass = mass</c>.</para>
///
/// <para>Each vector carries both <c>expectedRetail</c> (exact unclamped retail value) and
/// <c>expectedPortClamped</c> (the current port, which adds a non-retail
/// <see cref="HkPhysicsConstants.MaxSuspensionForce"/> clamp flagged for C2/C7 removal). Grounded
/// unit-mass vectors match retail bit-exact; the realistic-mass vector shows the clamp deviation.</para>
/// </summary>
[TestClass]
public class SuspensionOracleTests
{
    private const string GoldensRelativePath = "Physics/oracles/suspension_goldens.json";
    private const string GoldensFileName = "suspension_goldens.json";

    private static JsonDocument LoadGoldens() => JsonDocument.Parse(File.ReadAllText(ResolveGoldensPath()));

    private static float Hex(JsonElement obj, string hexProp)
        => BitConverter.ToSingle(Convert.FromHexString(obj.GetProperty(hexProp).GetString()!), 0);

    private static float Dec(JsonElement obj, string prop) => (float)obj.GetProperty(prop).GetDouble();

    private static void AssertBitExact(float expected, float actual, string ctx)
        => Assert.AreEqual(
            BitConverter.SingleToInt32Bits(expected),
            BitConverter.SingleToInt32Bits(actual),
            $"{ctx}: expected {expected:R} got {actual:R}");

    [TestMethod]
    public void AllGoldens_HexRoundTripsToDecimal()
    {
        using var doc = LoadGoldens();
        var vectors = doc.RootElement.GetProperty("vectors");
        Assert.AreEqual(8, vectors.GetArrayLength(), "expected 8 golden vectors");

        foreach (var v in vectors.EnumerateArray())
        {
            var inp = v.GetProperty("inputs");
            var id = v.GetProperty("id").GetString();
            foreach (var name in new[] { "restLength", "strength", "dampCompression", "dampExtension",
                                         "currentLength", "scalingFactor", "closingSpeed", "invMass" })
                AssertBitExact(Dec(inp, name), Hex(inp, name + "Hex"), $"{id}.{name}");
            AssertBitExact(Dec(v, "expectedRetail"), Hex(v, "expectedRetailHex"), $"{id}.expectedRetail");
            AssertBitExact(Dec(v, "expectedPortClamped"), Hex(v, "expectedPortClampedHex"), $"{id}.expectedPortClamped");
        }
    }

    [TestMethod]
    public void PortComputeForce_MatchesRetail_WhenClampInactive()
    {
        using var doc = LoadGoldens();
        var vectors = doc.RootElement.GetProperty("vectors");

        var checkedNonClamp = 0;
        foreach (var v in vectors.EnumerateArray())
        {
            if (v.GetProperty("clampActive").GetBoolean())
                continue;

            var inp = v.GetProperty("inputs");
            var actual = HkVehicleSuspension.ComputeForce(
                inContact: inp.GetProperty("inContact").GetBoolean(),
                restLength: Hex(inp, "restLengthHex"),
                strength: Hex(inp, "strengthHex"),
                dampCompression: Hex(inp, "dampCompressionHex"),
                dampExtension: Hex(inp, "dampExtensionHex"),
                currentLength: Hex(inp, "currentLengthHex"),
                scalingFactor: Hex(inp, "scalingFactorHex"),
                closingSpeed: Hex(inp, "closingSpeedHex"),
                invMass: Hex(inp, "invMassHex"));

            // Clamp inactive → port must reproduce the retail value bit-exact.
            AssertBitExact(Hex(v, "expectedRetailHex"), actual, $"{v.GetProperty("id").GetString()} (retail)");
            checkedNonClamp++;
        }

        Assert.AreEqual(7, checkedNonClamp, "expected 7 clamp-inactive vectors");
    }

    /// <summary>
    /// The realistic-mass vector: retail produces a large force (gScale = mass), but the current port
    /// clamps it to <see cref="HkPhysicsConstants.MaxSuspensionForce"/>. This asserts the current
    /// (clamped) behaviour and pins the retail value the C2 clamp-removal must restore.
    /// </summary>
    [TestMethod]
    public void PortComputeForce_ClampedVector_MatchesClamp_AndRetailIsC2Target()
    {
        using var doc = LoadGoldens();
        var vectors = doc.RootElement.GetProperty("vectors");

        var found = false;
        foreach (var v in vectors.EnumerateArray())
        {
            if (!v.GetProperty("clampActive").GetBoolean())
                continue;
            found = true;

            var inp = v.GetProperty("inputs");
            var actual = HkVehicleSuspension.ComputeForce(
                inContact: inp.GetProperty("inContact").GetBoolean(),
                restLength: Hex(inp, "restLengthHex"),
                strength: Hex(inp, "strengthHex"),
                dampCompression: Hex(inp, "dampCompressionHex"),
                dampExtension: Hex(inp, "dampExtensionHex"),
                currentLength: Hex(inp, "currentLengthHex"),
                scalingFactor: Hex(inp, "scalingFactorHex"),
                closingSpeed: Hex(inp, "closingSpeedHex"),
                invMass: Hex(inp, "invMassHex"));

            // Current port clamps to MaxSuspensionForce.
            AssertBitExact(Hex(v, "expectedPortClampedHex"), actual, $"{v.GetProperty("id").GetString()} (clamped)");
            Assert.AreEqual(HkPhysicsConstants.MaxSuspensionForce, actual, "clamp ceiling");
            // Retail (unclamped) is materially larger — the value C2 must restore once the clamp is removed.
            Assert.IsTrue(Math.Abs(Hex(v, "expectedRetailHex")) > HkPhysicsConstants.MaxSuspensionForce,
                "retail force exceeds the clamp (this is the deviation C2 removes)");
        }

        Assert.IsTrue(found, "expected a clamp-active vector");
    }

    private static string ResolveGoldensPath()
    {
        var p = Path.Combine(AppContext.BaseDirectory, GoldensRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(p))
            return p;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "Physics", "oracles", GoldensFileName);
            if (File.Exists(c))
                return c;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {GoldensFileName}");
    }
}

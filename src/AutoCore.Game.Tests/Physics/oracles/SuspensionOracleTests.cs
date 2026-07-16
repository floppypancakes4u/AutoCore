using System;
using System.IO;
using System.Text.Json;
using AutoCore.Game.Diagnostics;
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
/// <c>expectedPortClamped</c> (the pre-C2 port output with the non-retail
/// <see cref="HkPhysicsConstants.MaxSuspensionForce"/> clamp). Since C2, retail (unclamped) is the
/// default; the clamp survives only behind <see cref="ServerConfig.SuspensionForceClampEnabled"/>
/// as an opt-in stability lever, pinned by the flag-enabled test below.</para>
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
    /// The realistic-mass vector: retail produces a large force (gScale = mass). With the C2
    /// clamp removal, the default port path (safety flag OFF) must reproduce the retail value
    /// bit-exact — no <see cref="HkPhysicsConstants.MaxSuspensionForce"/> saturation.
    /// </summary>
    [TestMethod]
    public void PortComputeForce_ClampActiveVector_MatchesRetailUnclamped_ByDefault()
    {
        Assert.IsFalse(ServerConfig.SuspensionForceClampEnabled,
            "retail behaviour requires the suspension clamp flag to default OFF");

        using var doc = LoadGoldens();
        var vectors = doc.RootElement.GetProperty("vectors");

        var found = false;
        foreach (var v in vectors.EnumerateArray())
        {
            if (!v.GetProperty("clampActive").GetBoolean())
                continue;
            found = true;

            var actual = ComputeForceFromVector(v);

            // Default path is retail: bit-exact, materially above the old clamp ceiling.
            AssertBitExact(Hex(v, "expectedRetailHex"), actual, $"{v.GetProperty("id").GetString()} (retail)");
            Assert.IsTrue(Math.Abs(actual) > HkPhysicsConstants.MaxSuspensionForce,
                "retail force exceeds the old clamp ceiling (the C2-removed deviation)");
        }

        Assert.IsTrue(found, "expected a clamp-active vector");
    }

    /// <summary>
    /// Opt-in safety lever: with <see cref="ServerConfig.SuspensionForceClampEnabled"/> ON the old
    /// clamp behaviour is preserved (output saturates at <see cref="HkPhysicsConstants.MaxSuspensionForce"/>).
    /// </summary>
    [TestMethod]
    public void PortComputeForce_ClampFlagEnabled_ClampsToMaxSuspensionForce()
    {
        using var doc = LoadGoldens();
        var vectors = doc.RootElement.GetProperty("vectors");

        try
        {
            ServerConfig.SuspensionForceClampEnabled = true;

            var found = false;
            foreach (var v in vectors.EnumerateArray())
            {
                if (!v.GetProperty("clampActive").GetBoolean())
                    continue;
                found = true;

                var actual = ComputeForceFromVector(v);

                AssertBitExact(Hex(v, "expectedPortClampedHex"), actual, $"{v.GetProperty("id").GetString()} (clamped)");
                Assert.AreEqual(HkPhysicsConstants.MaxSuspensionForce, actual, "clamp ceiling");
            }

            Assert.IsTrue(found, "expected a clamp-active vector");
        }
        finally
        {
            ServerConfig.SuspensionForceClampEnabled = ServerConfig.DefaultSuspensionForceClampEnabled;
        }
    }

    /// <summary>
    /// Flag ON must not disturb non-clamping vectors (|force| under the ceiling passes through).
    /// </summary>
    [TestMethod]
    public void PortComputeForce_ClampFlagEnabled_NonClampVectors_StillMatchRetail()
    {
        using var doc = LoadGoldens();
        var vectors = doc.RootElement.GetProperty("vectors");

        try
        {
            ServerConfig.SuspensionForceClampEnabled = true;

            var checkedNonClamp = 0;
            foreach (var v in vectors.EnumerateArray())
            {
                if (v.GetProperty("clampActive").GetBoolean())
                    continue;

                var actual = ComputeForceFromVector(v);
                AssertBitExact(Hex(v, "expectedRetailHex"), actual, $"{v.GetProperty("id").GetString()} (retail, flag on)");
                checkedNonClamp++;
            }

            Assert.AreEqual(7, checkedNonClamp, "expected 7 clamp-inactive vectors");
        }
        finally
        {
            ServerConfig.SuspensionForceClampEnabled = ServerConfig.DefaultSuspensionForceClampEnabled;
        }
    }

    private static float ComputeForceFromVector(JsonElement v)
    {
        var inp = v.GetProperty("inputs");
        return HkVehicleSuspension.ComputeForce(
            inContact: inp.GetProperty("inContact").GetBoolean(),
            restLength: Hex(inp, "restLengthHex"),
            strength: Hex(inp, "strengthHex"),
            dampCompression: Hex(inp, "dampCompressionHex"),
            dampExtension: Hex(inp, "dampExtensionHex"),
            currentLength: Hex(inp, "currentLengthHex"),
            scalingFactor: Hex(inp, "scalingFactorHex"),
            closingSpeed: Hex(inp, "closingSpeedHex"),
            invMass: Hex(inp, "invMassHex"));
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

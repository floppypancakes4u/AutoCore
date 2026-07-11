using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Map;
using AutoCore.Game.TNL.Ghost;

[TestClass]
public class WireIsolationLeversTests
{
    [TestInitialize]
    public void SetUp()
    {
        WireIsolationLevers.ResetToDefaults();
        WireDiag.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        WireIsolationLevers.ResetToDefaults();
        WireDiag.ResetForTests();
    }

    [TestMethod]
    public void ResetToDefaults_ProductionSafe()
    {
        SectorMap.ScopeGlobalVehicles = false;
        GhostVehicle.EnablePathWire = false;
        WireDiag.Enabled = true;

        WireIsolationLevers.ResetToDefaults();

        Assert.IsTrue(SectorMap.ScopeGlobalVehicles);
        Assert.IsTrue(SectorMap.ScopeGlobalVehicleCreate);
        Assert.IsFalse(SectorMap.ScopeGlobalVehicleGhost,
            "Foreign GhostVehicle updates crash the retail client; normal CreateVehicle remains enabled.");
        Assert.IsTrue(SectorMap.SendGroupReactionCall);
        Assert.IsTrue(GhostVehicle.EnableAiStateWire);
        Assert.IsTrue(GhostVehicle.EnablePathWire);
        Assert.IsTrue(GhostVehicle.EnableOwnerWire);
        Assert.IsTrue(GhostVehicle.EnableTemplateSpawnWire);
        Assert.IsFalse(GhostVehicle.EnableInitialHardpointPack);
        Assert.IsFalse(GhostVehicle.EnableDeferredForeignPose);
        Assert.IsFalse(GhostVehicle.EnableForeignReghostOwner);
        Assert.IsTrue(GhostVehicle.EnableForeignVehiclePosePriorityBoost);
        Assert.IsFalse(WireDiag.Enabled);
    }

    [TestMethod]
    public void TrySet_KnownLever_UpdatesBackingFlag()
    {
        Assert.IsTrue(WireIsolationLevers.TrySet("ScopeGlobalVehicles", false, out _));
        Assert.IsFalse(SectorMap.ScopeGlobalVehicles);

        Assert.IsTrue(WireIsolationLevers.TrySet("EnablePathWire", false, out _));
        Assert.IsFalse(GhostVehicle.EnablePathWire);

        Assert.IsTrue(WireIsolationLevers.TrySet("WireDiag", true, out _));
        Assert.IsTrue(WireDiag.Enabled);
    }

    [TestMethod]
    public void TrySet_UnknownLever_Fails()
    {
        Assert.IsFalse(WireIsolationLevers.TrySet("NotARealLever", false, out var error));
        StringAssert.Contains(error, "Unknown");
    }

    [TestMethod]
    public void TrySet_CaseInsensitiveNames()
    {
        Assert.IsTrue(WireIsolationLevers.TrySet("scopeglobalvehicles", false, out _));
        Assert.IsFalse(SectorMap.ScopeGlobalVehicles);
    }

    [TestMethod]
    public void Snapshot_ListsAllLevers()
    {
        var snap = WireIsolationLevers.Snapshot();
        Assert.IsTrue(snap.Count >= 9);
        Assert.IsTrue(snap.Any(e => e.Name == "ScopeGlobalVehicles"));
        Assert.IsTrue(snap.Any(e => e.Name == "EnablePathWire"));
        Assert.IsTrue(snap.Any(e => e.Name == "WireDiag"));
    }

    [TestMethod]
    public void ApplyFromDictionary_OverridesOnlyPresentKeys()
    {
        WireIsolationLevers.ResetToDefaults();
        WireIsolationLevers.ApplyFromDictionary(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScopeGlobalVehicleGhost"] = false,
            ["EnableAiStateWire"] = false,
        });

        Assert.IsTrue(SectorMap.ScopeGlobalVehicles, "unmentioned lever stays default");
        Assert.IsFalse(SectorMap.ScopeGlobalVehicleGhost);
        Assert.IsFalse(GhostVehicle.EnableAiStateWire);
    }

    [TestMethod]
    public void ApplyFromDictionary_EnablesMinimalForeignVehicleInitialProfile()
    {
        WireIsolationLevers.ResetToDefaults();
        WireIsolationLevers.ApplyFromDictionary(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnableMinimalForeignInitialProfile"] = true,
        });

        Assert.IsTrue(GhostVehicle.EnableMinimalForeignInitialProfile);
    }

    [TestMethod]
    public void ApplyFromJson_ParsesBooleans()
    {
        const string json = """
            {
              "WireDiag": true,
              "ScopeGlobalVehicles": false,
              "EnablePathWire": false
            }
            """;

        var applied = WireIsolationLevers.ApplyFromJson(json, out var error);
        Assert.IsTrue(applied, error);
        Assert.IsTrue(WireDiag.Enabled);
        Assert.IsFalse(SectorMap.ScopeGlobalVehicles);
        Assert.IsFalse(GhostVehicle.EnablePathWire);
    }

    [TestMethod]
    public void ParseBool_AcceptsCommonTokens()
    {
        Assert.IsTrue(WireIsolationLevers.TryParseBool("1", out var a) && a);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("true", out var b) && b);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("yes", out var c) && c);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("0", out var d) && !d);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("false", out var e) && !e);
        Assert.IsFalse(WireIsolationLevers.TryParseBool("maybe", out _));
    }

    [TestMethod]
    public void FormatStatus_ContainsLeverNames()
    {
        var text = WireIsolationLevers.FormatStatus();
        StringAssert.Contains(text, "ScopeGlobalVehicles");
        StringAssert.Contains(text, "EnablePathWire");
        StringAssert.Contains(text, "WireDiag");
    }

    [TestMethod]
    public void TryGet_KnownAndUnknown()
    {
        Assert.IsTrue(WireIsolationLevers.TryGet("WireDiag", out var v));
        Assert.IsFalse(v);
        Assert.IsFalse(WireIsolationLevers.TryGet("Nope", out _));
    }

    [TestMethod]
    public void TrySet_EmptyName_Fails()
    {
        Assert.IsFalse(WireIsolationLevers.TrySet("", true, out var err));
        StringAssert.Contains(err, "required");
    }

    [TestMethod]
    public void TrySet_ByEnvSuffix_Works()
    {
        Assert.IsTrue(WireIsolationLevers.TrySet("PATH", false, out _));
        Assert.IsFalse(GhostVehicle.EnablePathWire);
        Assert.IsTrue(WireIsolationLevers.TrySet("AUTOCORE_WIRE_OWNER", false, out _));
        Assert.IsFalse(GhostVehicle.EnableOwnerWire);
    }

    [TestMethod]
    public void ApplyFromJson_InvalidCases()
    {
        Assert.IsFalse(WireIsolationLevers.ApplyFromJson("", out _));
        Assert.IsFalse(WireIsolationLevers.ApplyFromJson("[]", out var errArr));
        StringAssert.Contains(errArr, "object");
        Assert.IsFalse(WireIsolationLevers.ApplyFromJson("{\"WireDiag\": 1}", out var errType));
        StringAssert.Contains(errType, "boolean");
        Assert.IsFalse(WireIsolationLevers.ApplyFromJson("{", out _));
    }

    [TestMethod]
    public void ApplyFromJson_StringBoolTokens()
    {
        Assert.IsTrue(WireIsolationLevers.ApplyFromJson("{\"EnablePathWire\": \"off\"}", out var error), error);
        Assert.IsFalse(GhostVehicle.EnablePathWire);
    }

    [TestMethod]
    public void ApplyFromEnvironmentVariables_HonorsPrefix()
    {
        var key = WireIsolationLevers.EnvPrefix + "SCOPE_GLOBAL_VEHICLE_GHOST";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "0");
            WireIsolationLevers.ResetToDefaults();
            WireIsolationLevers.ApplyFromEnvironmentVariables();
            Assert.IsFalse(SectorMap.ScopeGlobalVehicleGhost);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
            WireIsolationLevers.ResetToDefaults();
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_LoadsTempJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "wire-levers-test-" + Guid.NewGuid().ToString("N") + ".json");
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        try
        {
            File.WriteAllText(path, """{"WireDiag": true, "EnableAiStateWire": false}""");
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, path);
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles();
            Assert.IsTrue(WireDiag.Enabled);
            Assert.IsFalse(GhostVehicle.EnableAiStateWire);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            WireDiag.ResetForTests();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void HandleConsoleCommand_SetDiagResetListHelpAndUnknown()
    {
        // list / status (no args subcommand defaults to list)
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire" });
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "list" });
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "status" });
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "help" });
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "?" });
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "notacommand" });

        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "diag", "on" });
        Assert.IsTrue(WireDiag.Enabled);
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "diag", "off" });
        Assert.IsFalse(WireDiag.Enabled);
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "diag" }); // usage

        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "set", "EnablePathWire", "false" });
        Assert.IsFalse(GhostVehicle.EnablePathWire);
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "set", "nope", "true" });
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "set", "EnablePathWire" }); // usage

        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "reset" });
        Assert.IsTrue(GhostVehicle.EnablePathWire);
        Assert.IsFalse(WireDiag.Enabled);
    }

    [TestMethod]
    public void ApplyFromDictionary_Null_IsNoOp()
    {
        WireIsolationLevers.ApplyFromDictionary(null);
        Assert.IsTrue(SectorMap.ScopeGlobalVehicles);
    }

    [TestMethod]
    public void ParseBool_WhitespaceAndOnOff()
    {
        Assert.IsFalse(WireIsolationLevers.TryParseBool("  ", out _));
        Assert.IsTrue(WireIsolationLevers.TryParseBool(" ON ", out var on) && on);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("no", out var no) && !no);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("FALSE", out var f) && !f);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("False", out var f2) && !f2);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("NO", out var n) && !n);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("OFF", out var o) && !o);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("TRUE", out var t) && t);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("True", out var t2) && t2);
        Assert.IsTrue(WireIsolationLevers.TryParseBool("YES", out var y) && y);
    }

    [TestMethod]
    public void ApplyFromDictionary_UnknownKey_IsIgnored()
    {
        WireIsolationLevers.ApplyFromDictionary(new Dictionary<string, bool>
        {
            ["NotALever"] = true,
            ["WireDiag"] = true,
        });
        Assert.IsTrue(WireDiag.Enabled);
    }

    [TestMethod]
    public void ApplyFromEnvironmentVariables_InvalidToken_LeavesDefault()
    {
        var key = WireIsolationLevers.EnvPrefix + "PATH";
        var prev = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "maybe-not-bool");
            WireIsolationLevers.ResetToDefaults();
            WireIsolationLevers.ApplyFromEnvironmentVariables();
            Assert.IsTrue(GhostVehicle.EnablePathWire, "invalid env must not flip lever");
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, prev);
            WireIsolationLevers.ResetToDefaults();
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_InvalidJson_LogsAndContinues()
    {
        var path = Path.Combine(Path.GetTempPath(), "wire-levers-bad-" + Guid.NewGuid().ToString("N") + ".json");
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        try
        {
            File.WriteAllText(path, "{ not valid json");
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, path);
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles();
            // Defaults remain after failed JSON (reset then failed apply).
            Assert.IsFalse(WireDiag.Enabled);
            Assert.IsTrue(SectorMap.ScopeGlobalVehicles);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_UnreadablePath_DoesNotThrow()
    {
        // Hold an exclusive lock so File.ReadAllText throws IOException inside the loader.
        var path = Path.Combine(Path.GetTempPath(), "wire-levers-locked-" + Guid.NewGuid().ToString("N") + ".json");
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        FileStream exclusive = null;
        try
        {
            File.WriteAllText(path, """{"WireDiag": true}""");
            exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, path);
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles();
            // Read failed; defaults from ResetToDefaults remain (WireDiag off).
            Assert.IsFalse(WireDiag.Enabled);
            Assert.IsTrue(SectorMap.ScopeGlobalVehicles);
        }
        finally
        {
            exclusive?.Dispose();
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_ContentRoot_LoadsFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "wire-levers-root-" + Guid.NewGuid().ToString("N"));
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, WireIsolationLevers.DefaultConfigFileName),
                """{"EnableOwnerWire": false}""");
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, null);
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles(contentRoot: root);
            Assert.IsFalse(GhostVehicle.EnableOwnerWire);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_CwdFallback_LoadsFile()
    {
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        var prevCwd = Environment.CurrentDirectory;
        var tempCwd = Path.Combine(Path.GetTempPath(), "wire-levers-cwd-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempCwd);
            File.WriteAllText(
                Path.Combine(tempCwd, WireIsolationLevers.DefaultConfigFileName),
                """{"EnableTemplateSpawnWire": false}""");
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, null);
            Environment.CurrentDirectory = tempCwd;
            // contentRoot without file so ResolveConfigPath falls through to cwd
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles(contentRoot: Path.GetTempPath());
            Assert.IsFalse(GhostVehicle.EnableTemplateSpawnWire);
        }
        finally
        {
            Environment.CurrentDirectory = prevCwd;
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            try { Directory.Delete(tempCwd, recursive: true); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_BaseDirectoryFallback_LoadsFile()
    {
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        var prevCwd = Environment.CurrentDirectory;
        var tempCwd = Path.Combine(Path.GetTempPath(), "wire-levers-empty-cwd-" + Guid.NewGuid().ToString("N"));
        var baseFile = Path.Combine(AppContext.BaseDirectory, WireIsolationLevers.DefaultConfigFileName);
        var hadBaseFile = File.Exists(baseFile);
        var previousBaseContent = hadBaseFile ? File.ReadAllText(baseFile) : null;
        try
        {
            Directory.CreateDirectory(tempCwd);
            File.WriteAllText(baseFile, """{"EnablePathWire": false}""");
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, null);
            Environment.CurrentDirectory = tempCwd; // no levers file in cwd
            // contentRoot that has no levers file
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles(contentRoot: tempCwd);
            Assert.IsFalse(GhostVehicle.EnablePathWire);
        }
        finally
        {
            Environment.CurrentDirectory = prevCwd;
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            try
            {
                if (hadBaseFile)
                    File.WriteAllText(baseFile, previousBaseContent!);
                else
                    File.Delete(baseFile);
            }
            catch { /* ignore */ }
            try { Directory.Delete(tempCwd, recursive: true); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void ApplyFromEnvironmentAndConfigFiles_NoConfigAnywhere_KeepsDefaults()
    {
        var prevFile = Environment.GetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar);
        var prevCwd = Environment.CurrentDirectory;
        var tempCwd = Path.Combine(Path.GetTempPath(), "wire-levers-none-" + Guid.NewGuid().ToString("N"));
        var baseFile = Path.Combine(AppContext.BaseDirectory, WireIsolationLevers.DefaultConfigFileName);
        var hadBaseFile = File.Exists(baseFile);
        var previousBaseContent = hadBaseFile ? File.ReadAllText(baseFile) : null;
        try
        {
            Directory.CreateDirectory(tempCwd);
            if (hadBaseFile)
                File.Delete(baseFile);
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, null);
            Environment.CurrentDirectory = tempCwd;
            WireIsolationLevers.ApplyFromEnvironmentAndConfigFiles(contentRoot: tempCwd);
            Assert.IsTrue(SectorMap.ScopeGlobalVehicles);
            Assert.IsFalse(WireDiag.Enabled);
        }
        finally
        {
            Environment.CurrentDirectory = prevCwd;
            Environment.SetEnvironmentVariable(WireIsolationLevers.ConfigFileEnvVar, prevFile);
            WireIsolationLevers.ResetToDefaults();
            try
            {
                if (hadBaseFile)
                    File.WriteAllText(baseFile, previousBaseContent!);
            }
            catch { /* ignore */ }
            try { Directory.Delete(tempCwd, recursive: true); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void HandleConsoleCommand_ShowAndDefaultsAliases()
    {
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "show" });
        WireIsolationLevers.TrySet("EnablePathWire", false, out _);
        WireIsolationLevers.HandleConsoleCommand(new[] { "wire", "defaults" });
        Assert.IsTrue(GhostVehicle.EnablePathWire);
    }
}

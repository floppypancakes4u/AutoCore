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
        Assert.IsTrue(SectorMap.ScopeGlobalVehicleGhost);
        Assert.IsTrue(SectorMap.SendGroupReactionCall);
        Assert.IsTrue(GhostVehicle.EnableAiStateWire);
        Assert.IsTrue(GhostVehicle.EnablePathWire);
        Assert.IsTrue(GhostVehicle.EnableOwnerWire);
        Assert.IsTrue(GhostVehicle.EnableTemplateSpawnWire);
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
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;

/// <summary>
/// Precedence for the two diag flags is compiled default &lt; log.filters.json &lt; environment.
/// <para>
/// Live 2026-08-13 capture: the operator set <c>AUTOCORE_WIRE_DIAG=1</c>, startup logged
/// <c>WireDiag = true</c> from the wire levers, and then <c>LogFilters.ApplyFromConfigFiles</c> —
/// which runs afterwards so a checked-in file can quiet the spam without a rebuild — read
/// <c>"WireDiag": false</c> and turned it straight back off. The env var was silently dead and the
/// capture produced nothing. The file must still win over the compiled default; it must not win
/// over an explicit per-run environment override.
/// </para>
/// </summary>
[TestClass]
public class DiagEnvOverridePrecedenceTests
{
    private string _previousWire;
    private string _previousGhost;

    [TestInitialize]
    public void SetUp()
    {
        _previousWire = Environment.GetEnvironmentVariable(WireDiag.EnvironmentVariableName);
        _previousGhost = Environment.GetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName);
        WireDiag.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(WireDiag.EnvironmentVariableName, _previousWire);
        Environment.SetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName, _previousGhost);
        WireDiag.ResetForTests();
        GhostObjectDiag.Enabled = false;
    }

    [TestMethod]
    public void EnvOverride_ReEnablesWireDiag_AfterLogFiltersTurnedItOff()
    {
        Environment.SetEnvironmentVariable(WireDiag.EnvironmentVariableName, "1");
        // Stand-in for LogFilters having just applied "WireDiag": false from the config file.
        WireDiag.Enabled = false;

        WireIsolationLevers.ApplyEnvironmentDiagOverrides();

        Assert.IsTrue(WireDiag.Enabled,
            "an explicit AUTOCORE_WIRE_DIAG=1 must survive the log.filters.json pass");
    }

    [TestMethod]
    public void EnvOverride_ReEnablesGhostObjectDiag_AfterLogFiltersTurnedItOff()
    {
        Environment.SetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName, "1");
        GhostObjectDiag.Enabled = false;

        WireIsolationLevers.ApplyEnvironmentDiagOverrides();

        Assert.IsTrue(GhostObjectDiag.Enabled);
    }

    /// <summary>
    /// The whole point of the file running last was that it can quiet diag spam without a rebuild.
    /// With no env var set, that must still hold.
    /// </summary>
    [TestMethod]
    public void NoEnvVar_LeavesLogFiltersValueIntact()
    {
        Environment.SetEnvironmentVariable(WireDiag.EnvironmentVariableName, null);
        Environment.SetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName, null);
        WireDiag.Enabled = false;
        GhostObjectDiag.Enabled = false;

        WireIsolationLevers.ApplyEnvironmentDiagOverrides();

        Assert.IsFalse(WireDiag.Enabled, "no override set — the config file's value stands");
        Assert.IsFalse(GhostObjectDiag.Enabled);
    }

    /// <summary>An explicit off must also win, not just an explicit on.</summary>
    [TestMethod]
    public void EnvOverride_CanForceWireDiagOff_OverAnEnabledValue()
    {
        Environment.SetEnvironmentVariable(WireDiag.EnvironmentVariableName, "0");
        WireDiag.Enabled = true;

        WireIsolationLevers.ApplyEnvironmentDiagOverrides();

        Assert.IsFalse(WireDiag.Enabled);
    }
}

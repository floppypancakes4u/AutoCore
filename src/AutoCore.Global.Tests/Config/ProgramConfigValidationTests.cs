using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Global.Tests.Config;

using AutoCore.Global.Config;

[TestClass]
public class ProgramConfigValidationTests
{
    [TestMethod]
    public void TryValidate_ValidConfig_ReturnsTrueWithNoErrors()
    {
        var config = CreateValidConfig();

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsTrue(ok);
        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void TryValidate_NullConfig_ReturnsFalse()
    {
        var ok = GlobalConfigValidator.TryValidate(null, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("GlobalConfig", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_MissingConnectionStrings_ReportsBoth()
    {
        var config = CreateValidConfig();
        config.CharDatabaseConnectionString = " ";
        config.WorldDatabaseConnectionString = "";

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("CharDatabaseConnectionString", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("WorldDatabaseConnectionString", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_MissingGamePath_ReportsError()
    {
        var config = CreateValidConfig();
        config.GamePath = "";

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("GamePath", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_InvalidCommunicatorEndpoint_ReportsErrors()
    {
        var config = CreateValidConfig();
        config.CommunicatorAddress = "not-an-ip";
        config.CommunicatorPort = 0;

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("CommunicatorAddress", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("CommunicatorPort", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_InvalidGamePortAndPublicAddress_ReportsErrors()
    {
        var config = CreateValidConfig();
        config.GameConfig.Port = 70000;
        config.GameConfig.PublicAddress = "hostname.local";

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("GameConfig.Port", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("GameConfig.PublicAddress", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_NegativeExpectedVersion_ReportsError()
    {
        var config = CreateValidConfig();
        config.GameConfig.ExpectedVersion = -1;

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("ExpectedVersion", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_MissingServerPasswordOrMaxPlayers_ReportsErrors()
    {
        var config = CreateValidConfig();
        config.ServerInfoConfig.Password = "";
        config.ServerInfoConfig.MaxPlayers = 0;

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("Password", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("MaxPlayers", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryValidate_NullNestedConfigs_ReportsErrors()
    {
        var config = CreateValidConfig();
        config.GameConfig = null!;
        config.ServerInfoConfig = null!;

        var ok = GlobalConfigValidator.TryValidate(config, out var errors);

        Assert.IsFalse(ok);
        Assert.IsTrue(errors.Any(e => e.Contains("GameConfig is required", StringComparison.Ordinal)));
        Assert.IsTrue(errors.Any(e => e.Contains("ServerInfoConfig is required", StringComparison.Ordinal)));
    }

    private static GlobalConfig CreateValidConfig() => new()
    {
        CharDatabaseConnectionString = "Server=localhost;Database=char;",
        WorldDatabaseConnectionString = "Server=localhost;Database=world;",
        GamePath = @"C:\Games\AutoAssault",
        CommunicatorAddress = "127.0.0.1",
        CommunicatorPort = 2107,
        GameConfig = new GameConfig
        {
            PublicAddress = "127.0.0.1",
            Port = 26880,
            ExpectedVersion = 175
        },
        ServerInfoConfig = new ServerInfoConfig
        {
            Id = 1,
            Password = "test",
            MaxPlayers = 1000
        }
    };
}

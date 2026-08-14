using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Global.Tests.Config;

using AutoCore.Global.Config;

[TestClass]
public class GameConfigTests
{
    [TestMethod]
    public void Defaults_WhenConstructed_MatchDocumentedValues()
    {
        var config = new GameConfig();

        Assert.IsFalse(config.AllowVersionMismatch);
        Assert.AreEqual(0, config.ExpectedVersion);
        Assert.IsFalse(config.AllowMissingCBID);
        Assert.AreEqual(0, config.Port);
        Assert.IsNull(config.PublicAddress);
    }

    [TestMethod]
    public void Bind_FromTempJson_PopulatesRequiredFields()
    {
        var json = """
            {
              "PublicAddress": "10.0.0.5",
              "Port": 26880,
              "AllowVersionMismatch": true,
              "ExpectedVersion": 175,
              "AllowMissingCBID": true
            }
            """;

        var config = BindFromTempJson(json);

        Assert.AreEqual("10.0.0.5", config.PublicAddress);
        Assert.AreEqual(26880, config.Port);
        Assert.IsTrue(config.AllowVersionMismatch);
        Assert.AreEqual(175, config.ExpectedVersion);
        Assert.IsTrue(config.AllowMissingCBID);
    }

    [TestMethod]
    public void Bind_PartialJson_KeepsDefaultsForOmittedFields()
    {
        var json = """
            {
              "PublicAddress": "127.0.0.1",
              "Port": 1
            }
            """;

        var config = BindFromTempJson(json);

        Assert.AreEqual("127.0.0.1", config.PublicAddress);
        Assert.AreEqual(1, config.Port);
        Assert.IsFalse(config.AllowVersionMismatch);
        Assert.AreEqual(0, config.ExpectedVersion);
        Assert.IsFalse(config.AllowMissingCBID);
    }

    [TestMethod]
    public void Bind_InvalidPortValue_StillBindsNumericAsConfigured()
    {
        // Binding itself does not validate; rejection is GlobalConfigValidator's job.
        var json = """
            {
              "PublicAddress": "127.0.0.1",
              "Port": 0
            }
            """;

        var config = BindFromTempJson(json);

        Assert.AreEqual(0, config.Port);
    }

    private static GameConfig BindFromTempJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"game-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var root = new ConfigurationBuilder().AddJsonFile(path).Build();
            var config = new GameConfig();
            root.Bind(config);
            return config;
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

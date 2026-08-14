using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sector.Tests.Config;

using AutoCore.Sector.Config;

[TestClass]
public class SectorConfigTests
{
    [TestMethod]
    public void SectorConfig_Defaults_AreSafeEmpty()
    {
        var config = new SectorConfig();

        Assert.IsNotNull(config.GameConfig);
        Assert.AreEqual(string.Empty, config.CharDatabaseConnectionString);
        Assert.AreEqual(string.Empty, config.WorldDatabaseConnectionString);
        Assert.AreEqual(string.Empty, config.AuthDatabaseConnectionString);
        Assert.AreEqual(string.Empty, config.GamePath);
        Assert.IsNotNull(config.LoggerConfig);
    }

    [TestMethod]
    public void GameConfig_Defaults_MatchHostExpectations()
    {
        var game = new GameConfig();

        Assert.IsNull(game.PublicAddress);
        Assert.AreEqual(0, game.Port);
        Assert.IsFalse(
            game.EnableDevControl,
            "SS-21: the dev-control API exposes unauthenticated /chat-command execution. It must " +
            "be opted into per deployment, not enabled by default.");
        Assert.AreEqual(27999, game.DevControlPort);
        Assert.IsFalse(game.AllowVersionMismatch);
        Assert.AreEqual(0, game.ExpectedVersion);
    }

    [TestMethod]
    public void Bind_FromJson_PopulatesNestedGameConfigAndConnectionStrings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sector-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "GameConfig": {
                    "Port": 27011,
                    "PublicAddress": "10.0.0.5",
                    "AllowVersionMismatch": true,
                    "ExpectedVersion": 175,
                    "EnableDevControl": false,
                    "DevControlPort": 28001
                  },
                  "CharDatabaseConnectionString": "Server=char;",
                  "WorldDatabaseConnectionString": "Server=world;",
                  "AuthDatabaseConnectionString": "Server=auth;",
                  "GamePath": "C:\\Games\\AutoAssault"
                }
                """);

            var root = new ConfigurationBuilder()
                .AddJsonFile(path)
                .Build();

            var config = SectorConfigValidation.Bind(root);

            Assert.AreEqual(27011, config.GameConfig.Port);
            Assert.AreEqual("10.0.0.5", config.GameConfig.PublicAddress);
            Assert.IsTrue(config.GameConfig.AllowVersionMismatch);
            Assert.AreEqual(175, config.GameConfig.ExpectedVersion);
            Assert.IsFalse(config.GameConfig.EnableDevControl);
            Assert.AreEqual(28001, config.GameConfig.DevControlPort);
            Assert.AreEqual("Server=char;", config.CharDatabaseConnectionString);
            Assert.AreEqual("Server=world;", config.WorldDatabaseConnectionString);
            Assert.AreEqual("Server=auth;", config.AuthDatabaseConnectionString);
            Assert.AreEqual(@"C:\Games\AutoAssault", config.GamePath);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [TestMethod]
    public void Bind_NullConfiguration_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => SectorConfigValidation.Bind(null!));
    }
}

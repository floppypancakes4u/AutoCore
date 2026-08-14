using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Global.Tests.Config;

using AutoCore.Global.Config;

[TestClass]
public class GlobalConfigTests
{
    [TestMethod]
    public void Defaults_WhenConstructed_NestedObjectsInitialized()
    {
        var config = new GlobalConfig();

        Assert.IsNotNull(config.GameConfig);
        Assert.IsNotNull(config.ServerInfoConfig);
        Assert.IsNotNull(config.LoggerConfig);
        Assert.AreEqual(string.Empty, config.CommunicatorAddress);
        Assert.AreEqual(0, config.CommunicatorPort);
        Assert.AreEqual(string.Empty, config.CharDatabaseConnectionString);
        Assert.AreEqual(string.Empty, config.WorldDatabaseConnectionString);
        Assert.AreEqual(string.Empty, config.GamePath);
    }

    [TestMethod]
    public void Bind_FromTempJson_PopulatesTopLevelAndNested()
    {
        var json = """
            {
              "CommunicatorAddress": "127.0.0.1",
              "CommunicatorPort": 2107,
              "CharDatabaseConnectionString": "Server=localhost;Database=char;",
              "WorldDatabaseConnectionString": "Server=localhost;Database=world;",
              "GamePath": "C:/Games/AutoAssault",
              "GameConfig": {
                "PublicAddress": "127.0.0.1",
                "Port": 26880,
                "AllowVersionMismatch": true,
                "ExpectedVersion": 175,
                "AllowMissingCBID": true
              },
              "ServerInfoConfig": {
                "Id": 1,
                "Password": "test",
                "AgeLimit": 0,
                "PKFlag": 0,
                "MaxPlayers": 1000
              },
              "LoggerConfig": {
                "IsDebugMode": false,
                "LogToFile": false,
                "LogFilePath": "global.log"
              }
            }
            """;

        var config = BindFromTempJson(json);

        Assert.AreEqual("127.0.0.1", config.CommunicatorAddress);
        Assert.AreEqual(2107, config.CommunicatorPort);
        Assert.AreEqual("Server=localhost;Database=char;", config.CharDatabaseConnectionString);
        Assert.AreEqual("Server=localhost;Database=world;", config.WorldDatabaseConnectionString);
        Assert.AreEqual("C:/Games/AutoAssault", config.GamePath);
        Assert.AreEqual(26880, config.GameConfig.Port);
        Assert.AreEqual("127.0.0.1", config.GameConfig.PublicAddress);
        Assert.IsTrue(config.GameConfig.AllowVersionMismatch);
        Assert.AreEqual(175, config.GameConfig.ExpectedVersion);
        Assert.IsTrue(config.GameConfig.AllowMissingCBID);
        Assert.AreEqual(1, config.ServerInfoConfig.Id);
        Assert.AreEqual("test", config.ServerInfoConfig.Password);
        Assert.AreEqual(1000, config.ServerInfoConfig.MaxPlayers);
        Assert.IsFalse(config.LoggerConfig.IsDebugMode);
        Assert.IsFalse(config.LoggerConfig.LogToFile);
        Assert.AreEqual("global.log", config.LoggerConfig.LogFilePath);
    }

    [TestMethod]
    public void Bind_EmptyObject_KeepsDefaults()
    {
        var config = BindFromTempJson("{}");

        Assert.AreEqual(string.Empty, config.CommunicatorAddress);
        Assert.AreEqual(0, config.CommunicatorPort);
        Assert.IsNotNull(config.GameConfig);
        Assert.IsNotNull(config.ServerInfoConfig);
    }

    private static GlobalConfig BindFromTempJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"global-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var root = new ConfigurationBuilder().AddJsonFile(path).Build();
            var config = new GlobalConfig();
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

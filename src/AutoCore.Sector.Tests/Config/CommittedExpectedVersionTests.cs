using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Sector.Tests.Config;

[TestClass]
public class CommittedExpectedVersionTests
{
    [TestMethod]
    public void StandaloneSectorAppsettings_ExpectedVersion_IsStockClient175()
    {
        AssertJsonExpectedVersion(Path.Combine(SourceRoot(), "AutoCore.Sector", "appsettings.sector.json"));
    }

    [TestMethod]
    public void LauncherSectorAppsettings_ExpectedVersion_IsStockClient175()
    {
        AssertJsonExpectedVersion(Path.Combine(SourceRoot(), "AutoCore.Launcher", "appsettings.sector.json"));
    }

    private static void AssertJsonExpectedVersion(string path)
    {
        Assert.IsTrue(File.Exists(path), path);
        var json = File.ReadAllText(path);
        StringAssert.Contains(json, "\"ExpectedVersion\": 175");
        Assert.AreEqual(175, TNLInterface.Version);
        Assert.IsFalse(json.Contains("\"ExpectedVersion\": 161"), path);
    }

    private static string SourceRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Global.Tests.Config;

[TestClass]
public class CommittedExpectedVersionTests
{
    [TestMethod]
    public void StandaloneGlobalAppsettings_ExpectedVersion_IsStockClient175()
    {
        AssertJsonExpectedVersion(Path.Combine(SourceRoot(), "AutoCore.Global", "appsettings.global.json"));
    }

    [TestMethod]
    public void LauncherGlobalAppsettings_ExpectedVersion_IsStockClient175()
    {
        AssertJsonExpectedVersion(Path.Combine(SourceRoot(), "AutoCore.Launcher", "appsettings.global.json"));
    }

    private static void AssertJsonExpectedVersion(string path)
    {
        Assert.IsTrue(File.Exists(path), path);
        var json = File.ReadAllText(path);
        StringAssert.Contains(json, "\"ExpectedVersion\": 175");
        Assert.AreEqual(175, TNLInterface.Version);
        Assert.IsFalse(json.Contains("\"ExpectedVersion\": 149"), path);
    }

    private static string SourceRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

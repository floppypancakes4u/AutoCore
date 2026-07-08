using AutoCore.Dev;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Live;

[TestClass]
public class LiveInventoryAddTests
{
    [TestMethod]
    [TestCategory("Live")]
    [TestCategory("Manual")]
    [Timeout(120_000)]
    public async Task InventoryAddLive_VerifiesClientCargoMemory()
    {
        if (!IsEnabled())
            Assert.Inconclusive("Live inventory test skipped. Set AUTOCORE_RUN_LIVE_TESTS=1 to run it.");

        var args = new List<string>();
        AddOptionFromEnvironment(args, "AUTOCORE_LIVE_CHARACTER", "--character");
        AddOptionFromEnvironment(args, "AUTOCORE_LIVE_API", "--api");
        AddOptionFromEnvironment(args, "AUTOCORE_LIVE_PROCESS", "--process");
        AddOptionFromEnvironment(args, "AUTOCORE_LIVE_ITEMS", "--items");

        var exitCode = await LiveInventoryAddRunner.RunAsync(LiveInventoryAddOptions.Parse(args.ToArray()));

        Assert.AreEqual(0, exitCode, "Live inventory add runner failed. Check test output for details.");
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("AUTOCORE_RUN_LIVE_TESTS");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddOptionFromEnvironment(List<string> args, string environmentVariable, string optionName)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            return;

        args.Add(optionName);
        args.Add(value);
    }
}

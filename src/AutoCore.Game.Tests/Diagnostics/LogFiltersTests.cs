using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;

[TestClass]
public class LogFiltersTests
{
    [TestInitialize]
    public void SetUp() => LogFilters.ResetToDefaults();

    [TestCleanup]
    public void TearDown() => LogFilters.ResetToDefaults();

    [TestMethod]
    public void Defaults_LootAndRamOn_PacketSpamOff()
    {
        Assert.IsTrue(LogFilters.Loot);
        Assert.IsTrue(LogFilters.MapPropRam);
        Assert.IsFalse(LogFilters.OutgoingPackets);
        Assert.IsFalse(LogFilters.IncomingPackets);
        Assert.IsFalse(LogFilters.PathPoseForce);
        Assert.IsFalse(WireDiag.Enabled);
        Assert.IsFalse(GhostObjectDiag.Enabled);
    }

    [TestMethod]
    public void ApplyFromJson_SetsCategories()
    {
        Assert.IsTrue(LogFilters.ApplyFromJson("""{"OutgoingPackets":true,"Loot":false}""", out var err), err);
        Assert.IsTrue(LogFilters.OutgoingPackets);
        Assert.IsFalse(LogFilters.Loot);
    }

    [TestMethod]
    public void QuietPreset_SilencesWireAndPackets()
    {
        LogFilters.OutgoingPackets = true;
        WireDiag.Enabled = true;
        LogFilters.ApplyQuietPreset();
        Assert.IsFalse(LogFilters.OutgoingPackets);
        Assert.IsFalse(WireDiag.Enabled);
        Assert.IsTrue(LogFilters.Loot);
        Assert.IsTrue(LogFilters.MapPropRam);
    }

    [TestMethod]
    public void TrySet_Unknown_Fails()
    {
        Assert.IsFalse(LogFilters.TrySet("NotARealFilter", true, out var error));
        StringAssert.Contains(error, "Unknown");
    }
}

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
    public void Defaults_LootAndRamAndTakeDamageOn_NoiseOff()
    {
        Assert.IsTrue(LogFilters.Loot);
        Assert.IsTrue(LogFilters.MapPropRam);
        Assert.IsTrue(LogFilters.TakeDamage);
        Assert.IsFalse(LogFilters.OutgoingPackets);
        Assert.IsFalse(LogFilters.IncomingPackets);
        Assert.IsFalse(LogFilters.PathPoseForce);
        Assert.IsFalse(WireDiag.Enabled);
        Assert.IsFalse(GhostObjectDiag.Enabled);
        Assert.IsFalse(LogFilters.OnDeath);
        Assert.IsFalse(LogFilters.RestoreHealth);
        Assert.IsFalse(LogFilters.DeathNet);
        Assert.IsFalse(LogFilters.PlayerDeathGhost);
        Assert.IsFalse(LogFilters.MapPropCorpseDespawn);
        Assert.IsFalse(LogFilters.MapPropDeathLoot);
    }

    [TestMethod]
    public void ApplyFromJson_SetsCategories()
    {
        Assert.IsTrue(LogFilters.ApplyFromJson("""{"OutgoingPackets":true,"Loot":false}""", out var err), err);
        Assert.IsTrue(LogFilters.OutgoingPackets);
        Assert.IsFalse(LogFilters.Loot);
    }

    [TestMethod]
    public void ApplyFromJson_NestedDamageAndProps_SetsLeaves()
    {
        var json = """
            {
              "Damage": {
                "TakeDamage": false,
                "OnDeath": true,
                "DeathNet": true
              },
              "Props": {
                "MapPropCorpseDespawn": true,
                "MapPropDeathLoot": true
              }
            }
            """;

        Assert.IsTrue(LogFilters.ApplyFromJson(json, out var err), err);
        Assert.IsFalse(LogFilters.TakeDamage);
        Assert.IsTrue(LogFilters.OnDeath);
        Assert.IsTrue(LogFilters.DeathNet);
        Assert.IsFalse(LogFilters.RestoreHealth, "unset nested leaves keep defaults");
        Assert.IsTrue(LogFilters.MapPropCorpseDespawn);
        Assert.IsTrue(LogFilters.MapPropDeathLoot);
    }

    [TestMethod]
    public void ApplyFromJson_FlatLegacyNames_StillWork()
    {
        Assert.IsTrue(LogFilters.ApplyFromJson("""{"OnDeath":true,"MapPropCorpseDespawn":true}""", out var err), err);
        Assert.IsTrue(LogFilters.OnDeath);
        Assert.IsTrue(LogFilters.MapPropCorpseDespawn);
    }

    [TestMethod]
    public void QuietPreset_SilencesWireAndPackets()
    {
        LogFilters.OutgoingPackets = true;
        WireDiag.Enabled = true;
        LogFilters.OnDeath = true;
        LogFilters.DeathNet = true;
        LogFilters.MapPropCorpseDespawn = true;
        LogFilters.ApplyQuietPreset();
        Assert.IsFalse(LogFilters.OutgoingPackets);
        Assert.IsFalse(WireDiag.Enabled);
        Assert.IsTrue(LogFilters.Loot);
        Assert.IsTrue(LogFilters.MapPropRam);
        Assert.IsTrue(LogFilters.TakeDamage);
        Assert.IsFalse(LogFilters.OnDeath);
        Assert.IsFalse(LogFilters.DeathNet);
        Assert.IsFalse(LogFilters.MapPropCorpseDespawn);
    }

    [TestMethod]
    public void TrySet_Unknown_Fails()
    {
        Assert.IsFalse(LogFilters.TrySet("NotARealFilter", true, out var error));
        StringAssert.Contains(error, "Unknown");
    }

    [TestMethod]
    public void TrySet_TakeDamage_LiveToggle()
    {
        Assert.IsTrue(LogFilters.TrySet("TakeDamage", false, out var err), err);
        Assert.IsFalse(LogFilters.TakeDamage);
        Assert.IsTrue(LogFilters.TrySet("takedamage", true, out err), err);
        Assert.IsTrue(LogFilters.TakeDamage);
    }
}

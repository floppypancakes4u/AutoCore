using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;

/// <summary>
/// Segment markers exist to make two world entries directly comparable in one capture. The live
/// Back Range freeze reproduces only on a re-entry (after the map's local-world reset), so the
/// diagnosis is a diff of "what did we send the first time" against "what did we send this time" —
/// which needs a boundary in the stream and identical throttling on both sides of it.
/// </summary>
[TestClass]
public class WireDiagSegmentTests
{
    [TestInitialize]
    public void SetUp() => WireDiag.ResetForTests();

    [TestCleanup]
    public void TearDown() => WireDiag.ResetForTests();

    [TestMethod]
    public void Disabled_BeginSegment_RecordsNothing()
    {
        WireDiag.Enabled = false;

        WireDiag.BeginSegment("map=693 resets=0");

        Assert.AreEqual(0, WireDiag.Snapshot().Count);
        Assert.AreEqual(0L, WireDiag.CurrentSeq);
    }

    [TestMethod]
    public void Enabled_BeginSegment_RecordsLabelledMarker()
    {
        WireDiag.Enabled = true;

        WireDiag.BeginSegment("map=693 resets=1");

        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(WireDiagKind.Segment, entry.Kind);
        StringAssert.Contains(entry.Detail, "map=693 resets=1");
    }

    /// <summary>
    /// Without this, the per-COID partial-pack cap is still exhausted from the first entry when the
    /// second begins, so the re-entry logs fewer packs purely as an artefact — the diff would show
    /// phantom differences on every entity.
    /// </summary>
    [TestMethod]
    public void BeginSegment_ResetsPartialGhostPackThrottle()
    {
        WireDiag.Enabled = true;
        WireDiag.MaxPartialGhostPacksPerCoid = 1;

        WireDiag.RecordGhostPack("GhostVehicle", coid: 55, bits: 8, mask: 1, initial: false, playerCoid: 7);
        WireDiag.RecordGhostPack("GhostVehicle", coid: 55, bits: 8, mask: 1, initial: false, playerCoid: 7);
        Assert.AreEqual(1, CountGhostPacks(55), "second pack must be throttled within a segment");

        WireDiag.BeginSegment("next entry");
        WireDiag.RecordGhostPack("GhostVehicle", coid: 55, bits: 8, mask: 1, initial: false, playerCoid: 7);

        Assert.AreEqual(2, CountGhostPacks(55),
            "a new segment must give the same COID its full pack budget again");
    }

    [TestMethod]
    public void BeginSegment_NullOrEmptyLabel_StillRecordsMarker()
    {
        WireDiag.Enabled = true;

        WireDiag.BeginSegment(null);

        Assert.AreEqual(WireDiagKind.Segment, WireDiag.Snapshot().Single().Kind);
    }

    private static int CountGhostPacks(long coid)
        => WireDiag.Snapshot().Count(e => e.Kind == WireDiagKind.GhostPack && e.Coid == coid);
}

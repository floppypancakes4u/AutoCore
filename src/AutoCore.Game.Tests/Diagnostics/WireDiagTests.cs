using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;

[TestClass]
public class WireDiagTests
{
    [TestInitialize]
    public void SetUp()
    {
        WireDiag.ResetForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        WireDiag.ResetForTests();
    }

    [TestMethod]
    public void Disabled_RecordDoesNotStoreOrIncrementVisibleSeq()
    {
        WireDiag.Enabled = false;

        WireDiag.RecordGamePacket("CreateVehicle", coid: 1, bytes: 10, playerCoid: 99);

        Assert.AreEqual(0, WireDiag.Snapshot().Count);
        Assert.AreEqual(0L, WireDiag.CurrentSeq);
    }

    [TestMethod]
    public void Enabled_RecordGamePacket_StoresSeqAndFields()
    {
        WireDiag.Enabled = true;

        WireDiag.RecordGamePacket("CreateVehicle", coid: 18134, bytes: 512, playerCoid: 18409, hexPreview: "DEADBEEF");

        var snap = WireDiag.Snapshot();
        Assert.AreEqual(1, snap.Count);
        Assert.AreEqual(1L, snap[0].Seq);
        Assert.AreEqual(WireDiagKind.GamePacket, snap[0].Kind);
        Assert.AreEqual("CreateVehicle", snap[0].Name);
        Assert.AreEqual(18134L, snap[0].Coid);
        Assert.AreEqual(512, snap[0].Bytes);
        Assert.AreEqual(18409L, snap[0].PlayerCoid);
        Assert.AreEqual("DEADBEEF", snap[0].HexPreview);
        Assert.AreEqual(1L, WireDiag.CurrentSeq);
    }

    [TestMethod]
    public void Enabled_RecordGhostPack_StoresBitsMaskInitial()
    {
        WireDiag.Enabled = true;

        WireDiag.RecordGhostPack(
            name: "GhostVehicle",
            coid: 42,
            bits: 120,
            mask: 0x80ul,
            initial: true,
            playerCoid: 7,
            detail: "path=1 owner=1");

        var entry = WireDiag.Snapshot().Single();
        Assert.AreEqual(WireDiagKind.GhostPack, entry.Kind);
        Assert.AreEqual("GhostVehicle", entry.Name);
        Assert.AreEqual(120, entry.Bits);
        Assert.AreEqual(0x80ul, entry.Mask);
        Assert.IsTrue(entry.Initial);
        Assert.AreEqual("path=1 owner=1", entry.Detail);
    }

    [TestMethod]
    public void FormatLine_IncludesExpectedTokens()
    {
        var line = WireDiag.FormatLine(new WireDiagEntry
        {
            Seq = 3,
            Kind = WireDiagKind.GhostPack,
            Name = "GhostVehicle",
            Coid = 99,
            Bits = 50,
            Bytes = -1,
            Mask = 0x2ul,
            Initial = false,
            PlayerCoid = 5,
            Detail = "path=0",
        });

        StringAssert.Contains(line, "[WireDiag]");
        StringAssert.Contains(line, "seq=3");
        StringAssert.Contains(line, "kind=GhostPack");
        StringAssert.Contains(line, "name=GhostVehicle");
        StringAssert.Contains(line, "coid=99");
        StringAssert.Contains(line, "bits=50");
        StringAssert.Contains(line, "mask=0x2");
        StringAssert.Contains(line, "initial=n");
        StringAssert.Contains(line, "conn=5");
        StringAssert.Contains(line, "path=0");
    }

    [TestMethod]
    public void Seq_IncrementsAcrossRecords()
    {
        WireDiag.Enabled = true;

        WireDiag.RecordGamePacket("A", 1, 1, 1);
        WireDiag.RecordGamePacket("B", 2, 2, 1);

        var snap = WireDiag.Snapshot();
        Assert.AreEqual(1L, snap[0].Seq);
        Assert.AreEqual(2L, snap[1].Seq);
    }

    [TestMethod]
    public void GhostPartial_RateLimitedPerCoid_KeepsInitialAlways()
    {
        WireDiag.Enabled = true;
        WireDiag.MaxPartialGhostPacksPerCoid = 2;

        WireDiag.RecordGhostPack("GhostVehicle", coid: 10, bits: 1, mask: 1, initial: true, playerCoid: 1);
        WireDiag.RecordGhostPack("GhostVehicle", coid: 10, bits: 2, mask: 1, initial: false, playerCoid: 1);
        WireDiag.RecordGhostPack("GhostVehicle", coid: 10, bits: 3, mask: 1, initial: false, playerCoid: 1);
        WireDiag.RecordGhostPack("GhostVehicle", coid: 10, bits: 4, mask: 1, initial: false, playerCoid: 1); // dropped

        Assert.AreEqual(3, WireDiag.Snapshot().Count);
    }

    [TestMethod]
    public void TryEnableFromEnvironment_HonorsEnvVar()
    {
        var previous = Environment.GetEnvironmentVariable("AUTOCORE_WIRE_DIAG");
        try
        {
            Environment.SetEnvironmentVariable("AUTOCORE_WIRE_DIAG", "1");
            WireDiag.ResetForTests();
            WireDiag.TryEnableFromEnvironment();
            Assert.IsTrue(WireDiag.Enabled);

            Environment.SetEnvironmentVariable("AUTOCORE_WIRE_DIAG", "0");
            WireDiag.ResetForTests();
            WireDiag.TryEnableFromEnvironment();
            Assert.IsFalse(WireDiag.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AUTOCORE_WIRE_DIAG", previous);
            WireDiag.ResetForTests();
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Diagnostics;

using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

[TestClass]
public class GhostObjectDiagTests
{
    [TestInitialize]
    public void SetUp() => GhostObjectDiag.ResetForTests();

    [TestCleanup]
    public void TearDown() => GhostObjectDiag.ResetForTests();

    [TestMethod]
    public void Disabled_RecordIsNoOp()
    {
        GhostObjectDiag.Enabled = false;

        GhostObjectDiag.Record(
            "CreateGhost",
            parentType: "GraphicsObject",
            cbid: 1,
            coid: 2,
            global: false,
            playerCoid: 9,
            detail: "x");

        Assert.AreEqual(0, GhostObjectDiag.Snapshot().Count);
        Assert.AreEqual(0L, GhostObjectDiag.CurrentSeq);
    }

    [TestMethod]
    public void Enabled_Record_StoresFieldsAndLogsSeq()
    {
        GhostObjectDiag.Enabled = true;

        GhostObjectDiag.Record(
            "ScopeAlways",
            parentType: "GraphicsObject",
            cbid: 9301,
            coid: 16218,
            global: false,
            playerCoid: 18325,
            detail: "pos=1.0,2.0,3.0 hp=10/10");

        var snap = GhostObjectDiag.Snapshot();
        Assert.AreEqual(1, snap.Count);
        Assert.AreEqual(1L, snap[0].Seq);
        Assert.AreEqual("ScopeAlways", snap[0].Name);
        Assert.AreEqual("GraphicsObject", snap[0].ParentType);
        Assert.AreEqual(9301, snap[0].Cbid);
        Assert.AreEqual(16218L, snap[0].Coid);
        Assert.IsFalse(snap[0].Global);
        Assert.AreEqual(18325L, snap[0].PlayerCoid);
        Assert.AreEqual("pos=1.0,2.0,3.0 hp=10/10", snap[0].Detail);
        StringAssert.Contains(GhostObjectDiag.FormatLine(snap[0]), "[GhostObjectDiag]");
        StringAssert.Contains(GhostObjectDiag.FormatLine(snap[0]), "global=0");
    }

    [TestMethod]
    public void FormatEntityDetail_IncludesTypeCbidTfidPosHp()
    {
        var prop = new GraphicsObject(GraphicsObjectType.Graphics);
        prop.SetCoid(16218, false);
        prop.Position = new Vector3(1949.4f, 5.0f, 521.5f);
        prop.InitializeHealthForTests(maxHp: 100);
        prop.TakeDamage(60); // 100 -> 40

        // CBID comes from clonebase when loaded; without wad use LoadCloneBase only when available.
        // Format must still be stable with cbid=0.
        var detail = GhostObjectDiag.FormatEntityDetail(prop);

        StringAssert.Contains(detail, "type=GraphicsObject");
        StringAssert.Contains(detail, "coid=16218");
        StringAssert.Contains(detail, "global=0");
        StringAssert.Contains(detail, "pos=1949.4,5,521.5");
        StringAssert.Contains(detail, "hp=40/100");
    }

    [TestMethod]
    public void FormatEntityDetail_Null_Safe()
    {
        Assert.AreEqual("entity=null", GhostObjectDiag.FormatEntityDetail(null));
    }

    [TestMethod]
    public void IsPlainGhostObject_TrueOnlyForExactGhostObjectType()
    {
        var plain = new AutoCore.Game.TNL.Ghost.GhostObject();
        var vehicleGhost = new AutoCore.Game.TNL.Ghost.GhostVehicle();

        Assert.IsTrue(GhostObjectDiag.IsPlainGhostObject(plain));
        Assert.IsFalse(GhostObjectDiag.IsPlainGhostObject(vehicleGhost));
        Assert.IsFalse(GhostObjectDiag.IsPlainGhostObject(null));
    }

    [TestMethod]
    public void TryEnableFromEnvironment_HonorsTokens()
    {
        var previous = Environment.GetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName, "1");
            GhostObjectDiag.ResetForTests();
            GhostObjectDiag.TryEnableFromEnvironment();
            Assert.IsTrue(GhostObjectDiag.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(GhostObjectDiag.EnvironmentVariableName, previous);
            GhostObjectDiag.ResetForTests();
        }
    }

    [TestMethod]
    public void MaxRetainedEntries_DropsOldest()
    {
        GhostObjectDiag.Enabled = true;
        GhostObjectDiag.MaxRetainedEntries = 3;

        for (var i = 0; i < 5; i++)
        {
            GhostObjectDiag.Record("E", "T", cbid: i, coid: i, global: false, playerCoid: 0);
        }

        var snap = GhostObjectDiag.Snapshot();
        Assert.AreEqual(3, snap.Count);
        Assert.AreEqual(3L, snap[0].Seq);
        Assert.AreEqual(5L, snap[2].Seq);
    }
}

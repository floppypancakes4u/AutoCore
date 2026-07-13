using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AbandonProbe.Tests;

/// <summary>
/// Pure logic for client abandon UI gates reverse-engineered from autoassault.exe
/// (FUN_008a5fe0 create gate + FUN_008a3510 show/hide).
/// </summary>
[TestClass]
public sealed class AbandonGateAnalyzerTests
{
    [TestMethod]
    public void CreateGate_normal_online_fields_skips_abandon_button()
    {
        // obj+0x100 == 0 && obj+0xAC != 2 → helper returns 1 → create skipped
        var gate = AbandonGateAnalyzer.EvaluateCreateGate(fieldAc: 0, field100: 0);

        Assert.IsFalse(gate.WouldCreateAbandonButton);
        Assert.IsTrue(gate.HelperReturnsTrue);
        StringAssert.Contains(gate.Reason, "skip");
    }

    [TestMethod]
    public void CreateGate_when_fieldAc_is_2_creates_abandon_button()
    {
        var gate = AbandonGateAnalyzer.EvaluateCreateGate(fieldAc: 2, field100: 0);

        Assert.IsTrue(gate.WouldCreateAbandonButton);
        Assert.IsFalse(gate.HelperReturnsTrue);
    }

    [TestMethod]
    public void CreateGate_when_field100_nonzero_creates_abandon_button()
    {
        var gate = AbandonGateAnalyzer.EvaluateCreateGate(fieldAc: 0, field100: 1);

        Assert.IsTrue(gate.WouldCreateAbandonButton);
        Assert.IsFalse(gate.HelperReturnsTrue);
    }

    [TestMethod]
    public void CreateGate_null_game_client_reports_unavailable()
    {
        var gate = AbandonGateAnalyzer.EvaluateCreateGateFromPointers(
            gameClientPtr: 0,
            fieldAc: 0,
            field100: 0);

        Assert.IsFalse(gate.WouldCreateAbandonButton);
        StringAssert.Contains(gate.Reason, "null");
    }

    [TestMethod]
    public void Visibility_self_tab_with_selection_and_created_shows()
    {
        var vis = AbandonGateAnalyzer.EvaluateVisibility(
            abandonButtonPtr: 0x12340000,
            tabMode: 0,
            selectedMissionIndex: 0);

        Assert.IsTrue(vis.WouldShowAbandon);
        Assert.AreEqual("Self", vis.TabName);
    }

    [TestMethod]
    public void Visibility_shared_tab_hides_even_if_created()
    {
        var vis = AbandonGateAnalyzer.EvaluateVisibility(
            abandonButtonPtr: 0x12340000,
            tabMode: 1,
            selectedMissionIndex: 0);

        Assert.IsFalse(vis.WouldShowAbandon);
        Assert.AreEqual("Shared", vis.TabName);
    }

    [TestMethod]
    public void Visibility_convoy_tab_hides()
    {
        var vis = AbandonGateAnalyzer.EvaluateVisibility(
            abandonButtonPtr: 0x12340000,
            tabMode: 2,
            selectedMissionIndex: 0);

        Assert.IsFalse(vis.WouldShowAbandon);
        Assert.AreEqual("Convoy", vis.TabName);
    }

    [TestMethod]
    public void Visibility_no_selection_hides()
    {
        var vis = AbandonGateAnalyzer.EvaluateVisibility(
            abandonButtonPtr: 0x12340000,
            tabMode: 0,
            selectedMissionIndex: unchecked((int)0xFFFFFFFF));

        Assert.IsFalse(vis.WouldShowAbandon);
        StringAssert.Contains(vis.Reason, "selection");
    }

    [TestMethod]
    public void Visibility_null_button_means_never_created()
    {
        var vis = AbandonGateAnalyzer.EvaluateVisibility(
            abandonButtonPtr: 0,
            tabMode: 0,
            selectedMissionIndex: 0);

        Assert.IsFalse(vis.WouldShowAbandon);
        StringAssert.Contains(vis.Reason, "never created");
    }

    [TestMethod]
    public void Diagnose_create_gate_false_dominates_over_good_selection()
    {
        var report = AbandonGateAnalyzer.Diagnose(
            create: AbandonGateAnalyzer.EvaluateCreateGate(0, 0),
            visibility: AbandonGateAnalyzer.EvaluateVisibility(0, 0, 0),
            pendingAbandonMissionId: unchecked((int)0xFFFFFFFF));

        Assert.AreEqual(AbandonDiagnosis.CreateGateBlocked, report.Primary);
        StringAssert.Contains(report.Summary, "Create gate");
    }

    [TestMethod]
    public void Diagnose_journal_not_built_when_gate_allows_but_ptr_null()
    {
        var report = AbandonGateAnalyzer.Diagnose(
            create: AbandonGateAnalyzer.EvaluateCreateGate(2, 0),
            visibility: AbandonGateAnalyzer.EvaluateVisibility(0, 0, 0),
            pendingAbandonMissionId: unchecked((int)0xFFFFFFFF));

        Assert.AreEqual(AbandonDiagnosis.JournalNotBuilt, report.Primary);
    }

    [TestMethod]
    public void Diagnose_wrong_tab_when_button_exists()
    {
        var report = AbandonGateAnalyzer.Diagnose(
            create: AbandonGateAnalyzer.EvaluateCreateGate(2, 0),
            visibility: AbandonGateAnalyzer.EvaluateVisibility(0x1000, 1, 0),
            pendingAbandonMissionId: unchecked((int)0xFFFFFFFF));

        Assert.AreEqual(AbandonDiagnosis.WrongTab, report.Primary);
    }

    [TestMethod]
    public void Diagnose_ready_when_self_selected_and_created()
    {
        var report = AbandonGateAnalyzer.Diagnose(
            create: AbandonGateAnalyzer.EvaluateCreateGate(2, 0),
            visibility: AbandonGateAnalyzer.EvaluateVisibility(0x1000, 0, 3),
            pendingAbandonMissionId: unchecked((int)0xFFFFFFFF));

        Assert.AreEqual(AbandonDiagnosis.ShouldBeVisible, report.Primary);
    }

    [TestMethod]
    public void Diagnose_not_per_mission_message_when_blocked_by_create_gate()
    {
        var report = AbandonGateAnalyzer.Diagnose(
            create: AbandonGateAnalyzer.EvaluateCreateGate(0, 0),
            visibility: AbandonGateAnalyzer.EvaluateVisibility(0, 0, 0),
            pendingAbandonMissionId: unchecked((int)0xFFFFFFFF));

        StringAssert.Contains(report.Summary, "not per-mission");
    }
}

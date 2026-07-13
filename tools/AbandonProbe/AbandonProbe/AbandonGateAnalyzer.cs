namespace AbandonProbe;

/// <summary>
/// Client abandon UI gates from autoassault.exe (image base 0x400000 RE):
/// <list type="bullet">
/// <item><description>Create: FUN_008a5fe0 @ 0x008A674F — call 0x004CE340 on DAT_00D1B644;
/// if AL!=0 skip creating i_d_q_2d_btn_abandon.xml (id 0x9C44 at journal+0x584).</description></item>
/// <item><description>Show: FUN_008a3510 — Self tab (journal+0x50C==0) + valid selection (+0x518!=-1)
/// + non-null abandon button ptr.</description></item>
/// </list>
/// No per-mission abandonable flag participates in either gate.
/// </summary>
public static class AbandonGateAnalyzer
{
    /// <summary>
    /// Mirrors 0x004CE340: returns true (helper AL=1) when field100==0 and fieldAc!=2.
    /// Journal create skips Abandon when helper returns true.
    /// </summary>
    public static AbandonCreateGateResult EvaluateCreateGate(uint fieldAc, byte field100)
    {
        var helperTrue = field100 == 0 && fieldAc != 2;
        if (helperTrue)
        {
            return new AbandonCreateGateResult(
                HelperReturnsTrue: true,
                WouldCreateAbandonButton: false,
                FieldAc: fieldAc,
                Field100: field100,
                Reason:
                "helper returned true (field100==0 && fieldAc!=2) → create path JNZ skips abandon button");
        }

        var why = field100 != 0
            ? $"field100={field100} nonzero"
            : $"fieldAc={fieldAc} == 2";
        return new AbandonCreateGateResult(
            HelperReturnsTrue: false,
            WouldCreateAbandonButton: true,
            FieldAc: fieldAc,
            Field100: field100,
            Reason: $"helper returned false ({why}) → abandon button is created");
    }

    public static AbandonCreateGateResult EvaluateCreateGateFromPointers(
        uint gameClientPtr,
        uint fieldAc,
        byte field100)
    {
        if (gameClientPtr == 0)
        {
            return new AbandonCreateGateResult(
                HelperReturnsTrue: false,
                WouldCreateAbandonButton: false,
                FieldAc: fieldAc,
                Field100: field100,
                Reason: "DAT_00D1B644 game client pointer is null — cannot create journal chrome");
        }

        return EvaluateCreateGate(fieldAc, field100);
    }

    public static AbandonVisibilityResult EvaluateVisibility(
        uint abandonButtonPtr,
        int tabMode,
        int selectedMissionIndex)
    {
        var tabName = tabMode switch
        {
            0 => "Self",
            1 => "Shared",
            2 => "Convoy",
            _ => $"Unknown({tabMode})"
        };

        if (abandonButtonPtr == 0)
        {
            return new AbandonVisibilityResult(
                WouldShowAbandon: false,
                TabMode: tabMode,
                TabName: tabName,
                SelectedMissionIndex: selectedMissionIndex,
                AbandonButtonPtr: abandonButtonPtr,
                Reason: "abandon button ptr is null (never created or journal not built)");
        }

        if (tabMode != 0)
        {
            return new AbandonVisibilityResult(
                WouldShowAbandon: false,
                TabMode: tabMode,
                TabName: tabName,
                SelectedMissionIndex: selectedMissionIndex,
                AbandonButtonPtr: abandonButtonPtr,
                Reason: $"tab is {tabName} — abandon only shown on Self (0)");
        }

        if (selectedMissionIndex == unchecked((int)0xFFFFFFFF))
        {
            return new AbandonVisibilityResult(
                WouldShowAbandon: false,
                TabMode: tabMode,
                TabName: tabName,
                SelectedMissionIndex: selectedMissionIndex,
                AbandonButtonPtr: abandonButtonPtr,
                Reason: "no mission selection (journal+0x518 == -1)");
        }

        return new AbandonVisibilityResult(
            WouldShowAbandon: true,
            TabMode: tabMode,
            TabName: tabName,
            SelectedMissionIndex: selectedMissionIndex,
            AbandonButtonPtr: abandonButtonPtr,
            Reason: "Self tab + valid selection + non-null abandon control → should show");
    }

    public static AbandonDiagnosisReport Diagnose(
        AbandonCreateGateResult create,
        AbandonVisibilityResult visibility,
        int pendingAbandonMissionId)
    {
        AbandonDiagnosis primary;
        string summary;

        if (!create.WouldCreateAbandonButton && create.Reason.Contains("null", StringComparison.OrdinalIgnoreCase))
        {
            primary = AbandonDiagnosis.NoGameClient;
            summary = "Game client pointer null. " + create.Reason;
        }
        else if (!create.WouldCreateAbandonButton)
        {
            primary = AbandonDiagnosis.CreateGateBlocked;
            summary =
                "Create gate blocked Abandon control (not per-mission). " +
                create.Reason +
                " | UI: " + visibility.Reason;
        }
        else if (visibility.AbandonButtonPtr == 0)
        {
            primary = AbandonDiagnosis.JournalNotBuilt;
            summary =
                "Create gate would allow Abandon, but journal abandon ptr is null. " +
                "Open Mission Journal once so FUN_008a5fe0 builds chrome, then re-probe. " +
                visibility.Reason;
        }
        else if (visibility.TabMode != 0)
        {
            primary = AbandonDiagnosis.WrongTab;
            summary = "Abandon exists but tab hides it. " + visibility.Reason;
        }
        else if (visibility.SelectedMissionIndex == unchecked((int)0xFFFFFFFF))
        {
            primary = AbandonDiagnosis.NoSelection;
            summary = "Abandon exists but no mission selected. " + visibility.Reason;
        }
        else if (visibility.WouldShowAbandon)
        {
            primary = AbandonDiagnosis.ShouldBeVisible;
            summary =
                "Logic says Abandon should be visible on Self with a selection. " +
                "If you still cannot see it, check UI skin load (i_d_q_2d_btn_abandon.xml) or layout.";
        }
        else
        {
            primary = AbandonDiagnosis.Unknown;
            summary = create.Reason + " | " + visibility.Reason;
        }

        if (pendingAbandonMissionId != unchecked((int)0xFFFFFFFF))
        {
            summary += $" | pending abandon mission id={pendingAbandonMissionId} (confirm dialog armed)";
        }

        return new AbandonDiagnosisReport(primary, summary, create, visibility, pendingAbandonMissionId);
    }
}

public readonly record struct AbandonCreateGateResult(
    bool HelperReturnsTrue,
    bool WouldCreateAbandonButton,
    uint FieldAc,
    byte Field100,
    string Reason);

public readonly record struct AbandonVisibilityResult(
    bool WouldShowAbandon,
    int TabMode,
    string TabName,
    int SelectedMissionIndex,
    uint AbandonButtonPtr,
    string Reason);

public enum AbandonDiagnosis
{
    CreateGateBlocked,
    JournalNotBuilt,
    WrongTab,
    NoSelection,
    ShouldBeVisible,
    NoGameClient,
    Unknown
}

public readonly record struct AbandonDiagnosisReport(
    AbandonDiagnosis Primary,
    string Summary,
    AbandonCreateGateResult Create,
    AbandonVisibilityResult Visibility,
    int PendingAbandonMissionId);

namespace AbandonProbe;

/// <summary>
/// Live snapshot of abandon UI gates from a running autoassault.exe.
/// Static VAs are Ghidra addresses for the allowlisted build (base 0x400000).
/// </summary>
public sealed class AbandonLiveProbe
{
    // Globals (static VA @ image base 0x400000)
    public const uint VaGameClientPtr = 0x00D1B644;
    public const uint VaLocalCharacter = 0x00D1B6D8;
    /// <summary>Main UI root (DAT_00D1A840). Mission journal lives at +0x107C (FUN_0093E7E0 / FUN_008A5C90).</summary>
    public const uint VaMainUi = 0x00D1A840;
    public const int OffMainUiJournal = 0x107C;
    public const uint VaPendingAbandonMissionId = 0x00D1B4B4;

    // Offsets on DAT_00D1B644 object / continent context object
    public const int OffFieldAc = 0xAC;
    public const int OffField100 = 0x100;

    // Offsets on mission journal UI object
    public const int OffJournalTab = 0x50C;
    public const int OffJournalSelected = 0x518;
    public const int OffSetActiveBtn = 0x57C;
    public const int OffSetActiveAllBtn = 0x580;
    public const int OffAbandonBtn = 0x584;

    // Character meta field for continent/context (layout+char+0xA8)
    public const int OffCharMeta = 0x4;
    public const int OffCharContext = 0xA8;

    public static AbandonLiveSnapshot Capture(ClientMemory mem)
    {
        var gameClientPtrAddr = mem.Resolve(VaGameClientPtr);
        var gameClientPtr = mem.ReadUInt32(gameClientPtrAddr);

        uint fieldAc = 0;
        byte field100 = 0;
        string? gameClientReadError = null;
        if (gameClientPtr != 0)
        {
            try
            {
                fieldAc = mem.ReadUInt32(gameClientPtr + OffFieldAc);
                field100 = mem.ReadByte(gameClientPtr + OffField100);
            }
            catch (Exception ex)
            {
                gameClientReadError = ex.Message;
            }
        }

        var create = AbandonGateAnalyzer.EvaluateCreateGateFromPointers(gameClientPtr, fieldAc, field100);

        uint journalPtr = 0;
        int tab = -1;
        int selected = unchecked((int)0xFFFFFFFF);
        uint setActive = 0, setActiveAll = 0, abandonBtn = 0;
        string? journalReadError = null;
        try
        {
            // Journal object: *(DAT_00D1A840 + 0x107C) from UI init FUN_0093E7E0
            var mainUi = mem.Resolve(VaMainUi);
            journalPtr = mem.ReadUInt32(mainUi + OffMainUiJournal);
            if (journalPtr != 0)
            {
                tab = mem.ReadInt32(journalPtr + OffJournalTab);
                selected = mem.ReadInt32(journalPtr + OffJournalSelected);
                setActive = mem.ReadUInt32(journalPtr + OffSetActiveBtn);
                setActiveAll = mem.ReadUInt32(journalPtr + OffSetActiveAllBtn);
                abandonBtn = mem.ReadUInt32(journalPtr + OffAbandonBtn);
            }
        }
        catch (Exception ex)
        {
            journalReadError = ex.Message;
        }

        var visibility = AbandonGateAnalyzer.EvaluateVisibility(abandonBtn, tab, selected);

        var pending = unchecked((int)0xFFFFFFFF);
        string? pendingError = null;
        try
        {
            pending = mem.ReadInt32(mem.Resolve(VaPendingAbandonMissionId));
        }
        catch (Exception ex)
        {
            pendingError = ex.Message;
        }

        // Character context object (same helper type used for kill-XP / instance-complete)
        uint characterPtr = 0;
        uint contextPtr = 0;
        uint ctxAc = 0;
        byte ctx100 = 0;
        AbandonCreateGateResult? contextGate = null;
        string? charError = null;
        try
        {
            characterPtr = mem.ReadUInt32(mem.Resolve(VaLocalCharacter));
            if (characterPtr != 0)
            {
                // layout = *(*(char+4)+4); field = *(layout + char + 0xA8)
                var meta = mem.ReadUInt32(characterPtr + OffCharMeta);
                if (meta != 0)
                {
                    var layout = mem.ReadUInt32(meta + 4);
                    contextPtr = mem.ReadUInt32(unchecked(layout + characterPtr + OffCharContext));
                    if (contextPtr != 0)
                    {
                        ctxAc = mem.ReadUInt32(contextPtr + OffFieldAc);
                        ctx100 = mem.ReadByte(contextPtr + OffField100);
                        contextGate = AbandonGateAnalyzer.EvaluateCreateGate(ctxAc, ctx100);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            charError = ex.Message;
        }

        var report = AbandonGateAnalyzer.Diagnose(create, visibility, pending);

        return new AbandonLiveSnapshot(
            ModuleBase: mem.ModuleBase,
            ModulePath: mem.ModulePath,
            Pid: mem.Pid,
            GameClientPtr: gameClientPtr,
            GameClientFieldAc: fieldAc,
            GameClientField100: field100,
            GameClientReadError: gameClientReadError,
            CreateGate: create,
            JournalPtr: journalPtr,
            JournalTab: tab,
            JournalSelected: selected,
            SetActiveBtnPtr: setActive,
            SetActiveAllBtnPtr: setActiveAll,
            AbandonBtnPtr: abandonBtn,
            JournalReadError: journalReadError,
            Visibility: visibility,
            PendingAbandonMissionId: pending,
            PendingReadError: pendingError,
            CharacterPtr: characterPtr,
            CharacterContextPtr: contextPtr,
            ContextFieldAc: ctxAc,
            ContextField100: ctx100,
            ContextGate: contextGate,
            CharacterReadError: charError,
            Diagnosis: report);
    }
}

public sealed record AbandonLiveSnapshot(
    uint ModuleBase,
    string? ModulePath,
    int Pid,
    uint GameClientPtr,
    uint GameClientFieldAc,
    byte GameClientField100,
    string? GameClientReadError,
    AbandonCreateGateResult CreateGate,
    uint JournalPtr,
    int JournalTab,
    int JournalSelected,
    uint SetActiveBtnPtr,
    uint SetActiveAllBtnPtr,
    uint AbandonBtnPtr,
    string? JournalReadError,
    AbandonVisibilityResult Visibility,
    int PendingAbandonMissionId,
    string? PendingReadError,
    uint CharacterPtr,
    uint CharacterContextPtr,
    uint ContextFieldAc,
    byte ContextField100,
    AbandonCreateGateResult? ContextGate,
    string? CharacterReadError,
    AbandonDiagnosisReport Diagnosis)
{
    public void WriteReport(TextWriter w)
    {
        w.WriteLine("=== AbandonProbe live snapshot ===");
        w.WriteLine($"pid={Pid}  moduleBase=0x{ModuleBase:X8}");
        if (!string.IsNullOrEmpty(ModulePath))
            w.WriteLine($"module={ModulePath}");
        w.WriteLine();

        w.WriteLine("--- Create gate (DAT_00D1B644 / journal FUN_008a5fe0) ---");
        w.WriteLine($"gameClientPtr = 0x{GameClientPtr:X8}");
        if (GameClientReadError != null)
            w.WriteLine($"READ ERROR: {GameClientReadError}");
        else
        {
            w.WriteLine($"  +0xAC  (fieldAc)  = 0x{GameClientFieldAc:X8} ({GameClientFieldAc})");
            w.WriteLine($"  +0x100 (field100) = 0x{GameClientField100:X2}");
            w.WriteLine($"  helperReturnsTrue = {CreateGate.HelperReturnsTrue}");
            w.WriteLine($"  wouldCreateAbandon = {CreateGate.WouldCreateAbandonButton}");
            w.WriteLine($"  reason: {CreateGate.Reason}");
        }
        w.WriteLine();

        w.WriteLine("--- Character context (char layout+0xA8, same helper offsets) ---");
        w.WriteLine($"characterPtr = 0x{CharacterPtr:X8}");
        w.WriteLine($"contextPtr   = 0x{CharacterContextPtr:X8}");
        if (CharacterReadError != null)
            w.WriteLine($"READ ERROR: {CharacterReadError}");
        else if (ContextGate is { } cg)
        {
            w.WriteLine($"  +0xAC  = 0x{ContextFieldAc:X8}");
            w.WriteLine($"  +0x100 = 0x{ContextField100:X2}");
            w.WriteLine($"  helperReturnsTrue = {cg.HelperReturnsTrue} (compare to create gate)");
            w.WriteLine($"  note: kill-XP / instance-complete use THIS object; journal create uses gameClientPtr");
            if (CharacterContextPtr != 0 && CharacterContextPtr == GameClientPtr)
                w.WriteLine("  contextPtr == gameClientPtr (same object)");
            else if (CharacterContextPtr != 0 && GameClientPtr != 0)
                w.WriteLine("  contextPtr != gameClientPtr (different objects — gate may disagree)");
        }
        w.WriteLine();

        w.WriteLine("--- Journal UI (*(DAT_00D1A840+0x107C)) ---");
        w.WriteLine($"journalPtr = 0x{JournalPtr:X8}");
        if (JournalReadError != null)
            w.WriteLine($"READ ERROR: {JournalReadError}");
        else if (JournalPtr == 0)
            w.WriteLine("  journal null — open Mission Journal once so chrome builds");
        else
        {
            w.WriteLine($"  +0x50C tab       = {JournalTab} ({Visibility.TabName})");
            var selNote = JournalSelected switch
            {
                unchecked((int)0xFFFFFFFF) => " (none)",
                0x9C4A => " (ctor default 0x9C4A — open journal / click a mission)",
                _ => ""
            };
            w.WriteLine($"  +0x518 selected  = {JournalSelected}{selNote}");
            w.WriteLine($"  +0x57C setActive = 0x{SetActiveBtnPtr:X8}" +
                        (SetActiveBtnPtr == 0 ? " (null)" : " (exists)"));
            w.WriteLine($"  +0x580 setAll    = 0x{SetActiveAllBtnPtr:X8}" +
                        (SetActiveAllBtnPtr == 0 ? " (null)" : " (exists)"));
            w.WriteLine($"  +0x584 abandon   = 0x{AbandonBtnPtr:X8}" +
                        (AbandonBtnPtr == 0 ? " (NULL — never created)" : " (EXISTS)"));
            if (SetActiveBtnPtr == 0 && AbandonBtnPtr == 0)
            {
                w.WriteLine(
                    "  note: setActive also null — open Mission Journal in-game so chrome builds, then re-probe.");
                w.WriteLine(
                    "  Even after chrome builds, create gate above still decides whether Abandon is allocated.");
            }
            else if (SetActiveBtnPtr != 0 && AbandonBtnPtr == 0)
            {
                w.WriteLine(
                    "  note: Set Active exists but Abandon does not — pure create-gate skip (not per-mission).");
            }
            w.WriteLine($"  wouldShow        = {Visibility.WouldShowAbandon}");
            w.WriteLine($"  reason: {Visibility.Reason}");
        }
        w.WriteLine();

        w.WriteLine("--- Pending abandon confirm (DAT_00D1B4B4) ---");
        if (PendingReadError != null)
            w.WriteLine($"READ ERROR: {PendingReadError}");
        else
            w.WriteLine(PendingAbandonMissionId == unchecked((int)0xFFFFFFFF)
                ? "  none (-1)"
                : $"  missionId={PendingAbandonMissionId}");
        w.WriteLine();

        w.WriteLine("--- Diagnosis ---");
        w.WriteLine($"PRIMARY: {Diagnosis.Primary}");
        w.WriteLine(Diagnosis.Summary);
        w.WriteLine();
        w.WriteLine("Per-mission abandonability: NOT gated by mission id in this client path.");
        w.WriteLine("If PRIMARY=CreateGateBlocked, every mission lacks Abandon chrome alike.");
        w.WriteLine("If PRIMARY=ShouldBeVisible but you still see no button, suspect UI XML/layout.");
        w.WriteLine("C2S FailMission 0x20B2 still needs server handling for actual abandon to stick.");
    }
}

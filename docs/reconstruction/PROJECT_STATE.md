# PROJECT_STATE — Final status report

**Target:** `autoassault.exe`  
**Ghidra:** AA-decode  
**Updated:** 2026-07-15  
**Status:** **HIGH-PRIORITY STATIC EXHAUSTED** (aligned with WORK_QUEUE)

## Skeptic gap repairs (this pass)

1. **Ghost unpack** — combat apply order heat→shield→max; **owner initial path wired** via live `Ghost_ReadOwnerBlockAndUnpack` → `Ghost_UnpackOwnerForm` (mechanical call-site gate); drive dirty path.
2. **GiveXP floater** — `GiveXpFloaterStack` with `static_assert(offsetof(uStack_8)==0x30)` and `sizeof==0x34`; type 3 at +0x30 in enqueued image (not tail-u32 theater).
3. **Mechanical tests** — `assert_recon_call_site` fails if owner/enqueue only exist as dead definitions; floater asserts `buf[0x30]==3` and `len==0x34`.
4. **State alignment** — SYSTEM_INDEX / WORK_QUEUE / matrix / UF list match wired code after gates pass.
5. **Gate re-verify (task-1)** — `test_reconstructed_logic.py` 27/27 OK; `Ghost_ReadOwnerBlockAndUnpack` live under `DAT_00d1798c`; floater type 3 @+0x30 / size 0x34.

## UF status

| UF | Status |
|----|--------|
| UF-001 | residual: open INC UI from corpse (countdown complete) |
| UF-002 | blocked runtime |
| UF-003 | closed (dialog + store) |
| UF-004 | closed (dispatch map) |
| UF-005 | closed (GiveXP + floater @+0x30) |
| UF-006 | closed (combat + **wired** owner + drive); optional non-combat flag names residual |

## Safety

Binary untouched; raw v1 preserved; no Launcher; reconstruction under `docs/reconstruction/` only.

## Cold-start

`PROJECT_STATE.md` → `RESUME.md` → `ACTIVE_WORK.md` → `WORK_QUEUE.md` → `SYSTEM_INDEX.md`

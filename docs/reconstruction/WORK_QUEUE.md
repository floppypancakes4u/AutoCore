# WORK_QUEUE

**Last updated:** 2026-07-15 (final-review Important fixes: matrix store rows + residual mirror)  
**Exhaustion status:** Claimed for high-priority static work per prompt criterion 5. Residuals are lifecycle-deferred (WQ-017), optional documentation depth (non-combat dirty-flag names; dialog OpenStore/StoreClose/0x206C unpacker), unavailable runtime (UF-002), or residual UI open (UF-001). Mechanical wiring gates re-verified green (`assert_recon_call_site` owner path + floater enqueue; floater 0x34/@+0x30).

| ID | System | Target | Address | Pri | Status | Next action |
|----|--------|--------|---------|-----|--------|-------------|
| WQ-000 | meta | Bootstrap | — | 100 | complete | — |
| WQ-001..015 | various | prior verticals | — | — | complete | quality bar re-checked |
| WQ-016 | comms | PacketDispatch case map | 0x00815710 | 60 | **complete** | raw table + clean helpers |
| WQ-017 | lifecycle | Login callbacks | Client_RecvLogin* | 40 | **deferred** | prompt priority 14 |
| WQ-018 | xp | GiveXP / level / floater | 0x0080ae70 | 55 | **complete** | floater CF filled |
| WQ-019 | respawn | INC countdown | 0x0091ee20 | 85 | **complete** | residual: show INC UI open |
| WQ-020 | dialog | Mission dialog + store | 0x00815070 / 0x0088e180 / 0x00810670 / 0x00860a50 | 70 | **complete** | optional residuals → Residual table |
| WQ-021 | entity | Ghost unpack combat+owner+drive | 0x005f7720 | 75 | **complete** | residual: non-combat flag taxonomy |
| WQ-RT-01 | runtime | differential | — | — | **blocked** | UF-002 |

## Blocked (unavailable evidence only)

| ID | Reason | UF / Doc |
|----|--------|----------|
| WQ-RT-01 | No approved live client/Launcher (live capture for Accepted field, buy slot COID, dual-run) | UF-002; `systems/dialog-vendors.md`, `systems/*` runtime notes |
| WQ-019-open | Corpse/zero-HP → open INC UI not found in symbols after string/function search | UF-001 residual; `systems/death-respawn.md` |

## Residual / optional depth (not eligible high-pri)

These are **out of high-priority scope**. They appear on system plates as depth notes only; they must **not** be treated as eligible queue work without reclassification.

| ID | System | Residual | Reason | Cross-ref |
|----|--------|----------|--------|-----------|
| WQ-020-r1 | dialog (SYS-VENDOR) | OpenStore reaction Apply body (type-specific) | Optional static depth; buy/sell wire path closed via WQ-020 / UF-003 | `systems/dialog-vendors.md` Open; confidence "Full OpenStore reaction type internals" |
| WQ-020-r2 | dialog (SYS-VENDOR) | StoreClose C2S writer (0x202A) | Optional; server session clear documented; client string present, writer not required for closed vertical | `systems/dialog-vendors.md` Vendor open path step 5 |
| WQ-020-r3 | dialog (SYS-VENDOR) | Bitstream unpacker producing 0x28-stride buffer for 0x206C | Optional depth; decoded field map probable; dispatch + apply path enough for vertical | `systems/dialog-vendors.md` 0x206C section |
| WQ-021-r1 | entity (SYS-ENTITY) | Intermediate non-combat dirty-flag name taxonomy | Optional documentation depth; combat+owner+drive complete | SYSTEM_INDEX footnote; `systems/entity-vehicle.md` |
| WQ-016-r1 | comms (SYS-COMMS) | Remaining FUN_* leaf handlers beyond case map | Optional; hub + high-pri cases mapped | `systems/comms-dispatch.md`; matrix `00815710` Open |
| WQ-INPUT-r1 | input (SYS-INPUT) | Full bind-index → action enum / bind table dump | Optional depth; key match + poll vertical complete (static) | `systems/input.md`; matrix `00911030` Open |
| WQ-MOV-r1 | movement (SYS-MOVEMENT) | integrateDt / ghost +0xBC source annotation | Optional; axes + pose apply complete (static) | `systems/movement.md`; matrix pose Open |
| WQ-MISSION-r1 | missions (SYS-MISSION) | Full S2C objective packet variants beyond 0x2070 | Optional depth; GiveMission / CompleteObjective path complete (static) | `systems/missions.md` Open |

## Eligible high-priority remaining

**None.** Residuals above are optional depth, blocked (UF-002), residual UI (UF-001), or lifecycle deferred (WQ-017).

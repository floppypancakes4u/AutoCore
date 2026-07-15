# Function: VehicleEntity_PushDriveAxesToController

| Field | Value |
|-------|-------|
| Canonical name | `VehicleEntity_PushDriveAxesToController` |
| Stable ID | `aa_exe_004fbc10` |
| Module | autoassault.exe |
| Address | `0x004fbc10` |
| Original decompiler name | `VehicleEntity_PushDriveAxesToController` |
| Proposed namespace | `client::movement` |
| System | movement |
| Confidence overall | high |
| Completion status | reconstructed (static); runtime blocked UF-002 |
| Updated | 2026-07-15 |

## Purpose

Push throttle and handbrake into VehicleAction controller.

## Behavioral summary

Steer not written here; speed-cap may zero throttle.

## Signatures

- **Original / decompiler:** see `raw/aa_exe_004fbc10.md`
- **Reconstructed:** see `reconstructed-exact/VehicleEntity_PushDriveAxesToController.cpp` (or nearest)

## Preconditions / postconditions

- Preconditions: valid client/game object; system-specific entity pointers
- Postconditions: documented side effects only; no invented validation

## Inputs / outputs / return

See reconstructed source and raw capture.

## State read / modified / external effects

Documented in reconstructed file header and system doc `systems/movement.md`.

## Known callers / callees

See system doc and raw plate comments.

## Assembly verification notes

Primary evidence: Ghidra decompile (not disassemble_bytes). Control-flow structure matched to prior RE docs where available (motion, respawn). Signedness: integer flag/bit tests use unsigned masks where `& 0xd2` / `& 0x1000` appear.

## Runtime verification notes

Not re-run this session (Launcher approval required). Prior live facts linked from `docs/MOTION_CLIENT_RE.md` / `Documentation/RESPAWN_SYSTEM.md` where applicable.

## Differential-test status

Packet layout pure helpers covered in `experiments/` where practical; full binary path not dual-run.

## Reviewer findings

See `reviews/aa_exe_004fbc10_review.md`.

## Alternate interpretations / open questions

Listed in system doc and UNRESOLVED_FINDINGS.

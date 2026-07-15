# Function: NetworkPoseApply_FUN_0053eec0

| Field | Value |
|-------|-------|
| Canonical name | `NetworkPoseApply_FUN_0053eec0` |
| Stable ID | `aa_exe_0053eec0` |
| Module | autoassault.exe |
| Address | `0x0053eec0` |
| Original decompiler name | `NetworkPoseApply_FUN_0053eec0` |
| Proposed namespace | `client::movement` |
| System | movement |
| Confidence overall | high |
| Completion status | reconstructed (static); runtime blocked UF-002 |
| Updated | 2026-07-15 |

## Purpose

Soft buffer or hard write for network pose.

## Behavioral summary

Soft when physics not ready; teleport if error > 15 units.

## Signatures

- **Original / decompiler:** see `raw/aa_exe_0053eec0.md`
- **Reconstructed:** see `reconstructed-exact/NetworkPoseApply_FUN_0053eec0.cpp` (or nearest)

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

See `reviews/aa_exe_0053eec0_review.md`.

## Alternate interpretations / open questions

Listed in system doc and UNRESOLVED_FINDINGS.

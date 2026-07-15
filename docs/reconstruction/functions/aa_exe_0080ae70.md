# Function: Client_AwardKillExperience

| Field | Value |
|-------|-------|
| Canonical name | `Client_AwardKillExperience` |
| Stable ID | `aa_exe_0080ae70` |
| Module | autoassault.exe |
| Address | `0x0080ae70` |
| Original decompiler name | `Client_AwardKillExperience` |
| Alias note | Handles **all** S2C GiveXP; not kill-only |
| Proposed namespace | `client::progression` |
| System | progression |
| Confidence overall | high |
| Completion status | reconstructed (static) |
| Updated | 2026-07-15 |

## Purpose

S2C `EMSG_Sector_GiveXP` (`0x205F`) handler: apply additive XP, optional level-hint byte, enqueue XP combat floater (type 3).

## Behavioral summary

1. Bail if no local character (`game+0xe98`).
2. `CVOGReaction_AddExperience(char, amount, PacketOrNonKill)`.
3. If `LevelHint != -1`: write `char+0x738` and timestamp `+0x734`.
4. If vehicle present: floater type **3** with amount.

## Wire

| Offset | Field |
|--------|-------|
| `+0x04` | `Amount` int32 |
| `+0x08` | `LevelHint` sbyte (`-1` none) |

## Signatures

- **Raw:** `raw/aa_exe_0080ae70.md`
- **Exact:** `reconstructed-exact/Client_AwardKillExperience.cpp`

## Known callers / callees

- Caller: `Client_PacketDispatch` `0x205f`
- Callees: `CVOGReaction_AddExperience` @ `0x00533c30`; `Client_EnqueueCombatFloater_INFERRED` @ `0x00402620`

## Reviewer findings

See `reviews/aa_exe_0080ae70_review.md`.

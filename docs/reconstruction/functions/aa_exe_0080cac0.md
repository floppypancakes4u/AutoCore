# Function: Client_RecvGiveCredits

| Field | Value |
|-------|-------|
| Canonical name | `Client_RecvGiveCredits` |
| Stable ID | `aa_exe_0080cac0` |
| Module | autoassault.exe |
| Address | `0x0080cac0` |
| Original decompiler name | `Client_RecvGiveCredits` |
| Proposed namespace | `client::progression` |
| System | progression |
| Confidence overall | high |
| Completion status | reconstructed (static) |
| Updated | 2026-07-15 |

## Purpose

S2C `GiveCredits` (`0x205E`): **add** int64 delta to character currency (`+0x720`); optional sound + floater type 4.

## Wire

| Offset | Field |
|--------|-------|
| `+0x04` | pad int32 |
| `+0x08` | amount int64 |

## Signatures

- **Raw:** `raw/aa_exe_0080cac0.md`
- **Exact:** `reconstructed-exact/Client_RecvGiveCredits.cpp`

## Relation

Economy sibling of GiveXP. Absolute money restore is **`0x2017`**, not this packet. Avoid double-count after mission complete paths that already applied credits.

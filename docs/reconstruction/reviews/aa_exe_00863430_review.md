# Review: Client_SendInventoryDrop_Hardpoint (aa_exe_00863430)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00863430.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_SendInventoryDrop_Hardpoint.cpp` |
| Related S2C equip | `docs/reconstruction/raw/aa_exe_00813f40.md` (equip-via-drop note) |
| System doc | `docs/reconstruction/systems/inventory.md` |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x2036` | C2S InventoryDrop |
| Packet size | `0x20` | send arg |
| Drop type byte | packet `+0x1A` = `2` | HARDPOINT (equip-via-drop) |
| XY placeholders | packet `+0x18`/`+0x19` = `0xff` | unused grid cells for this type |
| Item identity | item `[0x58]`/`[0x59]`/`[0x5a]` | packed into `+8`/`+c`/`+0x10` |
| Clone type | `item[0x2a]+0x38` | type `0xe` special town path |
| Town check | `FUN_004ce5f0(vehicle)` | in-town gate |
| Player field | `DAT_00d1b6d8 + 0x6b4` | town-alternate / secondary equip allowance |
| Selection UI | `DAT_00d1b1f8` vfunc `+0x3ac` | selected item |

## Concrete raw ↔ clean matches (3+)

1. **Entry gates** — Both: no player → `0`; null item from `DAT_00d1b1f8` vfunc `+0x3ac` → `0`; `FUN_00862860() == 0` → `0`.
2. **Blocked-item short path** — `FUN_004fabc0(item, 0) != 0` → `FUN_00931db0()` → return `1` (no drop packet).
3. **HARDPOINT packet** — Opcode `0x2036`, size `0x20`, identity from item `[0x58..0x5a]`, `+0x18/+0x19 = 0xff`, `+0x1A = 2` (`kInventoryDropTypeHardpoint`), then `Client_SendSectorPacket`.
4. **Town-only error string** — On blocked paths, both surface `"This item can only be changed in town."` via `FUN_007a6de0` / `FUN_007fdfb0` and return `1` (not `0`).
5. **Type `0xe` branch** — If clone type `== 0xe`: only when in town (`FUN_004ce5f0`) **or** `player+0x6b4 > 0` → `FUN_008012f0` + `FUN_00931440(1)` return `1`; else town error.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: this is a world-drop of cargo, not equip.**  
   **Falsified for this entry point:** drop type is hard-coded to `2` (HARDPOINT). Raw/clean both set `uStack_6 = 2`. Plate on equip recv states equip is requested via Drop HARDPOINT type 2, not C2S `0x203C`.

2. **Hypothesis: town error returns `0` (failed send).**  
   **Falsified:** raw `return 1` after the town error toast. Clean documents the same “binary returns 1 even on error message path.”

3. **Hypothesis: type `0xe` always builds the `0x2036` packet.**  
   **Falsified:** type `0xe` either runs `FUN_008012f0`/`FUN_00931440` in town (no drop packet in that success path) or town-error return — never falls into the `auStack_20` packet build in the shown body.

4. **Hypothesis: secondary town gate is unused when already in town.**  
   **Supported by raw:** non-`0xe` path only re-checks secondary (`vfunc +0x1f0` non-null and `+0x6b4 < 1`) when `FUN_004ce5f0(vehicle) == 0` (not in town). Clean preserves that structure.

## Residual uncertainty (this unit)

- Meaning of clone type `0xe` and of `FUN_008012f0` / `FUN_00931440` (special equip UI vs alternate protocol).
- Exact identity of item dword indices `0x58`/`0x59`/`0x5a` vs TFID layout used elsewhere (`+0x160` style) — same builder pattern as grab but different base.
- Whether other Drop type values share this function or only HARDPOINT entry points here (symbol is Hardpoint-specific).
- Runtime equip-via-drop captures (UF-002).

## Verdict

**Accept** — HARDPOINT `0x2036`/`0x20`/type `2` packet, town gates, type `0xe` fork, and return codes match raw. Semantics of helpers and type `0xe` remain named externs.

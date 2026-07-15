# Review: Client_PacketDispatch (aa_exe_00815710)

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile / map | `docs/reconstruction/raw/aa_exe_00815710.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_PacketDispatch.cpp` (~ pure opcode→handler helpers + known-opcode set) |
| Spot-check handlers | equip `0x203c`, unlock `0x205b`, special event `0x20a9`, mission `0x2070`/`0x2071` |

**Key constants:** switch on `param_2->dwOpcode`; default return 0; fall-through group return 1 without body; high opcodes `0x8063`/`0x9001`/`0x9004`/`0x901c`.

## Concrete raw ↔ clean matches (3+)

1. **`0x20a9` → `Client_RecvSpecialEvent`** in raw table and `PacketDispatch_HandlerName`.
2. **`0x2070` / `0x2071` not swapped** — CompleteDynamicObjective vs ObjectiveState in both.
3. **`0x203c` → equip, `0x205b` → UnlockRegion, `0x205f` → AwardKillExperience** in pure map.
4. Clean documents that full 100+ `FUN_*` bodies are not inlined; raw holds the complete case list.

## Skeptical review

1. **Hypothesis: clean replaces the switch.** Falsified — clean is lookup helpers for high-pri opcodes; binary switch remains authority in raw.
2. **Hypothesis: unlisted opcodes crash.** Falsified — default returns 0.
3. **Hypothesis: 0x2070 is ObjectiveState.** Falsified by raw plate + table.

## Residuals

- Anonymous `FUN_*` handler bodies not reconstructed as separate units.

## Verdict

**Accept** as dispatch map unit (raw full table + clean pure lookup). Not a claim that every callee is fully reconstructed.

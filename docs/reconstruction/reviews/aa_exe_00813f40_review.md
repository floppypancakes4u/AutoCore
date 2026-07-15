# Review: Client_RecvInventoryEquip (aa_exe_00813f40)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00813f40.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_RecvInventoryEquip.cpp` |
| Dispatch map | `docs/reconstruction/raw/aa_exe_00815710.md` (case `0x203c`) |
| System doc | `docs/reconstruction/systems/inventory.md` |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x203C` | S2C InventoryEquip |
| Packet size | `0x40` | plate / enum in clean |
| Item TFID | packet `+8` | resolve / log |
| Vehicle TFID-ish | packet `+0x18` / `+0x1c`, type byte `+0x20` | `FUN_004bafe0` |
| Old item TFID | packet `+0x28` / `+0x2c` | log + empty-hand check |
| putInHand | packet `+0x38` | `1` → hand path |
| srcX / srcY / invTypeFrom | `+0x39` / `+0x3A` / `+0x3B` | grid place / hand inv type |
| Local entity compare | game `+0xe98` vs owner vfunc `+0x1dc` | local equip path |
| UI dirty flags | game `+0x30b4` / `+0x30b5` | set after local apply |
| Foreign type switch | clone `pi[0x2a]+0x38` cases `6, 10, 0xc, 0x10, 0x1c` | equip-by-type |

## Concrete raw ↔ clean matches (3+)

1. **Opcode / size / layout plate** — Clean `kInventoryEquipOpcode = 0x203C`, size `0x40`, and field offsets `+8 / +0x18 / +0x28 / +0x38..+0x3B` match raw header comments and log/uses.
2. **Null-vehicle early path** — Both: `FUN_004bafe0(...)` fails → `Object_ResolveFromTFID(item TFID)` → optional `FUN_009440e0(..., 1, 0, -1, -1)` then return (no foreign switch).
3. **putInHand vs cargo place** — Local path: `packet+0x38 == 1` → resolve-to-hand (`CVOGReaction_ResolveObjectTarget` / `FUN_007fc270(invTypeFrom)`); else grid `FUN_00571620(result, srcX, srcY, 1)`. Empty-hand edge when putInHand and old TFID both `0xffffffff` → `FUN_007fc150`.
4. **Foreign equip type switch** — Both switch on `*(int*)(pi[0x2a] + 0x38)`: case `10` → `Vehicle_EquipPowerPlant`; `0x10` → `FUN_004ff510`; `0x1c` → `FUN_00502180`; default / unhandled → return. Clean documents cases `6` and `0xc` even where bodies are stubbed.
5. **Dispatch wiring** — PacketDispatch maps `0x203c` → `Client_RecvInventoryEquip` (raw map).

## Skeptical review (unit-specific falsification)

1. **Hypothesis: client builds C2S `0x203C` for equip.**  
   **Falsified:** raw plate: “No client C2S 0x203C builder found; equip via Drop type HARDPOINT=2.” Clean plate preserves that. Equip *request* is `0x2036` with type `2` (`aa_exe_00863430`), not this handler.

2. **Hypothesis: foreign (non-local-owner) path still runs hand/grid UI placement.**  
   **Falsified:** hand/grid placement and `+0x30b4` dirtying sit inside the local-owner block (`owner entity id == game+0xe98`). Foreign path uses the clone-type switch + `FUN_0092f120` only.

3. **Hypothesis: clean fully implements the local ownership vfunc check.**  
   **Partially falsified (clean weak here):** raw calls owner vfunc `+0x1dc` and compares to `in_EAX+0xe98`. Clean comments the check but does not execute the vfunc compare (placeholder `(void)localEntity`). Behavior is documented, not bit-exact in clean for that gate.

4. **Hypothesis: type `0xc` always uses one equip helper.**  
   **Falsified by raw:** type `0xc` branches on `short @ +0x3f4 == 9` → `FUN_004fe800` else `FUN_004fe110`. Clean only notes the branch in comments; body is incomplete.

## Residual uncertainty (this unit)

- Full bodies for foreign cases `6` / `0xc` (RTTI cast / weapon mount) not expanded in clean — **type switch incomplete**.
- Exact meaning of packet `+0x10` byte passed into foreign `ResolveObjectTarget` (raw packs with CONCAT31).
- Whether `in_EAX` game object is always the same as dispatch `param_3` — decompiler calling convention residue.
- Runtime equip round-trip (UF-002) not re-run.

## Verdict

**Partial** — packet layout, null-vehicle path, putInHand vs grid, power-plant/default cases, and “no C2S 0x203C” story are solid; local-owner gate and foreign type `0xc`/`6` bodies are not fully reconstructed in clean.

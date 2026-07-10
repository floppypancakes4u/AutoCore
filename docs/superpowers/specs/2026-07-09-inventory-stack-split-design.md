# Inventory Stack Split + Recombine

## Goal

Support cargo **partial stack split** (client shift+LMB / number-pad grab quantity) and **same-CBID recombine on drop**, using client wire semantics validated against Destroyer’s Ghidra notes — implemented cleanly in AutoCore’s `InventoryManager` domain (not a port of Destroyer’s connection-local `_heldItem` model).

## Wire (must match client)

### Grab response `0x2035` (split path)

| Offset | Field | Split meaning |
|--------|--------|----------------|
| `+0x1c` | int32 Count | Amount peeled onto cursor |
| `+0x20` | byte SplitFlag | `1` = partial split |
| `+0x28` | int64 SplitCoid | New COID for peeled stack |
| `+0x38` | byte Success | `1` on success |

Whole-grab path keeps existing AutoCore layout (`SplitFlag=0`, cargo X/Y at `+0x28`/`+0x2c`).

### Drop response `0x2037` (recombine path)

| Offset | Field | Concat meaning |
|--------|--------|----------------|
| `+0x22` | Success | 1 |
| `+0x23` | Swap flag | 1 |
| `+0x28` | int64 occupant COID | Stack receiving the merge |
| `+0x38` | Concat sub-mode | 1 = merge cursor into occupant, destroy cursor |

Normal move path keeps the shorter non-swap layout.

## Domain behavior

1. **`InventoryStackPolicy`** — max stack / stackable from clonebase `StackSize` (reuse `NormalizeStackSize` rules).
2. **Partial grab** when cargo item, stackable, `0 < requested < source.Quantity`:
   - Reduce source quantity (persist)
   - Allocate new COID (`Map.LocalCoidCounter` or safe fallback)
   - Track `PendingCargoDrag` (not in grid)
   - Emit split grab response
3. **Drop of pending cargo drag**:
   - Empty slot → place stack + upsert
   - Occupant same CBID stackable → merge up to max stack; delete pending (and leftover held if partial max-fill)
   - Otherwise fail (no inventing full positional swap for this pass)
4. **Drop of in-grid item onto same-CBID stackable occupant** → recombine (merge + delete mover)

## Non-goals

- CollapseStacks sort
- Locker/trade
- Whole-stack held-item model for all cargo grabs

## Tests

Unit tests for policy, packet layouts, split grab, drop-place, recombine (pending + in-grid), max-stack clamp, non-stackable reject.

# Ghidra RE exports — session combat-pool cleanup

Readable reconstructions for client RE tagged **`session-combat-pool-re`** in
`autoassault.exe`. Scope is limited to vehicle combat-pool (HP / power / heat /
shield), ghost dirty masks, and skill cast-again cooldown dependencies used by
that path.

| File | Topic |
|------|--------|
| `vehicle_combat_pool.md` | Call graph, offsets, masks, function index |
| `vehicle_combat_pool.c` | Cleaned pseudocode (not compilable) |

## Confidence

- **Verified** against decompiler + call sites: periods 3000/5000 ms, pool offsets,
  mask constants, power/cool/shield/HP rate sources, SetMaskBits dirty-list link.
- **INFERRED** (marked in plate comments / type names): full vehicle layout structs
  (`RE_*`, `*_Inferred`), vtable globals, some heat-delta register recovery in
  `Vehicle_AddHeat`, owner type `0xE` = player vehicle.

## Tag

Filter in Ghidra: function tag `session-combat-pool-re` (18 functions).

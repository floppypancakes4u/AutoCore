# Cargo grid footprints (server ↔ client parity)

## Grid geometry

- Width **6**, height **pages × 13** (`VehicleCargoCapacity`, client `FUN_004F3A30` / `InventoryGrid_ctor_Inferred`).
- Item size: clonebase `InvSizeX` / `InvSizeY` (`tinInventorySizeX/Y`, runtime blob +0x406/+0x407).

## Algorithms (implemented)

| Server | Client |
|--------|--------|
| `InventoryFootprintPolicy` | clonebase InvSize fields |
| `InventoryGridPlacement.CanPlace` | `FUN_00570840` |
| `InventoryGridPlacement.TryFindFirstFree` | `FUN_005713a0` (Y outer, X inner) |
| `InventoryManager` multi-cell occupancy | `FUN_00571620` stamps every cell |

Example: 2×2 at origin → cells `(0,0),(1,0),(0,1),(1,1)` → 1-based slots **1, 2, 7, 8** on width 6.

Page rule: `(y % 13) + sizeY <= 13` (no span across cargo pages).

## Acquisition

- Stack merge first (Y then X), then first-fit for new stack origins using item footprint.
- Clonebase present with **0×0** size → **reject** add.
- Unknown CBID (no clonebase) → temporary **1×1** fallback (tests/legacy).

## Load re-pack

`LoadItems` places at stored origin if `CanPlace`, else first-fit. Returns `true` when layout changed → `PersistRepackedCargo` (clear + upsert).

## Wire format

**Origin-only** for `CargoSendAll` / vehicle COID arrays. See `docs/inventory-cargo-wire-re.md` — client `0x2040` dispatch is a no-op; placement is Create + `0x2047` + grab/drop.

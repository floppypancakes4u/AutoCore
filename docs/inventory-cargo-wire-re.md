# Inventory cargo wire format (PR0 RE)

**Date:** 2026-07-14  
**Client:** `autoassault.exe` (Ghidra)  
**Branch:** `research/inventory-grid-size`

## Question

For multi-cell items, should AutoCore write cargo COIDs as:

- **A) Origin-only** — one entry per item at top-left `(x,y)`, client expands via `InvSizeX/Y`, or  
- **B) Fill every footprint cell** — same COID in every linear cell of the rectangle?

## Findings

### `InventoryCargoSendAll` (`0x2040`)

| Fact | Evidence |
|------|----------|
| Layout | `SMSG_Sector_InventoryCargoSendAll` size `0x1388`: `ucInventorySize` (UI pages) + **312** × `SVOGInventoryItem` |
| Per entry | COID (8) + X (1) + Y (1) + pad (6) — see `Documentation/PACKET STRUCTURES.md` |
| Client dispatch | `Client_PacketDispatch` @ `0x00815710` **case `0x2040`** falls through with `0x2041`–`0x2043` to a **no-op** (`return 1`) |

So this client build does **not** rebuild cargo UI from CargoSendAll in the sector packet switch. The packet remains useful as a server-side snapshot / potential other paths / future, but **must not be relied on as the sole client resync**. Visible cargo is driven by **Create** (`IsInInventory`) + **`0x2047` AddItem** (client re-finds slot) + grab/drop responses.

### Grid occupancy on client

| Function | Addr | Behavior |
|----------|------|----------|
| Cell array | `InventoryGrid_AllocateCellArray_Inferred` `0x00570720` | `width*height` cells × 8 bytes; empty = `0xFFFFFFFF` halves |
| Place | `FUN_00571620` `0x00571620` | Stamps item COID into **every** footprint cell |
| Find free | `FUN_005713a0` `0x005713a0` | First-fit Y then X; uses cell occupancy + size |
| Footprint | clonebase `+0x406`/`+0x407` | `InvSizeX` / `InvSizeY` |

Occupancy is **always** multi-cell on the client grid object. That is independent of CargoSendAll wire.

### CreateVehicle inventory COID array

AutoCore fills `CreateVehicleExtendedPacket.InventoryCoids[y*width+x] = originCoid` only at origins (`InventoryPacketFactory.ConfigureVehicleCargo`). Each entry is a bare COID, not X/Y — linear index **is** the origin slot under width-6 row-major indexing used by AutoCore tests.

Client cargo create: `Vehicle_CreateCargoInventoryFromPageCount` @ `0x004F3A30` builds empty grid (`6 × pages×13`); items are placed when objects are created / re-laid via `FUN_00572360` (try keep position with full footprint `CanPlace`, else first-fit).

Filling **every** footprint linear COID in the vehicle array would imply multiple slots share one COID without distinct origins — no client evidence that the receive path expects that; origin-index fill matches existing AutoCore tests and sparse “slot = origin” semantics.

## Recommendation (PR4 wire policy)

| Channel | Policy |
|---------|--------|
| **CargoSendAll / vehicle COID arrays** | **Origin-only** (A). One wire entry per item at `slot = y * 6 + x` with `PositionX/Y = origin`. Do **not** stamp footprint cells. |
| **Server runtime occupancy** | Full rectangle (client `FUN_00571620` parity) — separate from wire. |
| **Client visual after add** | Trust first-fit parity + Create/`0x2047`; treat CargoSendAll as non-authoritative on this client build. |

## Implication for “CargoSendAll after every mutation”

Still do it for:

- Server/debug/DevTool consistency  
- Any non-dispatch consumers  
- Parity with existing AutoCore call sites  

But **do not** assume it fixes client desync by itself. Placement parity on AddItem and correct Create positions matter more.

## Exit criterion

**Origin-only wire** selected for PR4. Runtime multi-cell occupancy is PR1–PR3 scope.

---

## Locker (type 3) — cargo ↔ locker moves

| Fact | Evidence |
|------|----------|
| Enum | `eVOG_INVENTORY_TYPE_LOCKER = 3` (`Documentation/PACKET STRUCTURES.md`) |
| C2S Grab | `Client_SendInventoryGrab_FromGrid` @ `0x00860e20` — same `0x2034` size `0x20`; `ucTypeFrom` from window inventory type |
| C2S Drop | `Client_UI_InventoryDropToGrid` @ `0x00860a50` — same `0x2036` size `0x20`; `ucTypeTo` from target grid; early allows types 1 and 3 |
| S2C DropResponse | `Client_RecvInventoryDropResponse` @ `0x00813730` — **case 3** binds locker grid (`client+0x1034` / character `+0xcbc`) and places via `FUN_00571620` |
| SendAll | `0x2041` LockerSendAll same no-op path as cargo in `Client_PacketDispatch` |
| **Login restore** | `CreateCharacterExtended.InventoryCoids[312]` @ packet `+0x960` — `CVOGCharacter_ApplyCreateFromPacket` @ `0x00534bd0` resolves each global COID and places into locker via `FUN_00571d30`. Create object packets must precede the character create. |

**Server sequence (live move):** Grab (typeFrom 1 or 3) → GrabResponse → Drop (typeTo 1 or 3) → DropResponse. No new opcodes.

**Server sequence (login):** Create locker items (`IsInInventory`) → CreateCharacterExtended with locker COIDs filled → client `FUN_00571d30` stamps locker grid.

# Vendor / Facility UseObject (open-only)

## Summary

`ObjectUseManager` dispatches C2S **UseObject `0x2072`**:

1. Use-item mission progress  
2. Mission dialog (`NpcInteractHandler.TryHandleMissionDialog`)  
3. **Spawn/object TriggerEvents** — `InteractTriggerService` (primary vendor path)  
4. **Vendor store spatial fallback** — `VendorStoreService`  
5. **Facilities** — BodyShop / Garage / Refinery / SkillTrainer / …  
6. Unhandled debug log  

### Primary vendor chain (backrange Ascent dispensary)

```
UseObject → kiosk NPC (SpawnOwner=9837)
  → SpawnPoint.TriggerEvents[2] = trigger 9838 (l1_rem_generalstore_1)
    → Trigger.Reactions = [9839]
      → OpenStore reaction 9839 (GenericVar1 = store COID 9819)
        → GroupReactionCall 0x206C → client opens store UI
```

Store stock COID can be far from the kiosk in world space; client opens by **COID**, not proximity. Spatial OpenStore search is only a fallback.

Kiosk NPC **CBID 12700** (`npc_h_kiosk_generalkisok` / Hestia Equipment Dispensary).

## Opcodes

| Opcode | EMSG | Status |
|--------|------|--------|
| 0x2024 | StoreOpen | unused (open via 0x206C OpenStore) |
| 0x2025 | StoreOpen_Response | unused |
| 0x2026 | StoreList | unused (client local stock) |
| **0x2027** | **StoreTransactionRequest** | **handled** — buy/sell |
| **0x2028** | **StoreTransactionResponse** | **sent** — full 0x30 layout (FUN_00810670) |
| 0x2029 | StoreUpdate | not yet |
| **0x202A** | **StoreClose** | clears session |

### StoreTransactionRequest (0x2027) — client send 0x00860be2

Packet size **0x40** including opcode:

| Offset | Field |
|--------|--------|
| +0x00 | opcode `0x2027` |
| +0x18 | TFID 16B (item; **store-slot COID** on buy, cargo item COID on sell) |
| +0x28 | store COID i64 (present on buy live captures, e.g. 9819) |
| +0x38 | `IsBuy` byte (1=buy, 0=sell) |
| +0x3c | quantity i32 |

Server logs full hex on each transaction for further RE if layout differs by UI path.

### Buy path

Client does **not** send catalog CBID for buy; it sends a store inventory object TFID.

**Buyback (sell → buy same item):** after a successful sell, the client keeps the sold item TFID on the store UI. Live: `itemCoid=11131` after selling that stack. Server lists buyback by that COID (`VendorStore: buyback listed …`) and buy matches it first (`source=buyback`), charging the same unit sell price. Restore uses the **original COID** via `RestoreCargoWithoutCreate` (0x2047 + CargoSendAll only) — never a second `CreateSimpleObject`, which would leave a spare invalid icon.

**Catalog stock:** session map `slotCoid → stock line` + optional `CreateSimpleObject` creates; match by session COID or CBID fallback.

Grant cargo via `AddItem`, charge credits, ack `0x2028` with grant COID @+0x08 and slot TFID @+0x18. StoreClose clears buyback listings.

### StoreTransactionResponse (0x2028) — client FUN_00810670

Size **0x30** including opcode (verified Ghidra `Client_PacketDispatch` → `FUN_00810670`):

| Offset | Field |
|--------|--------|
| +0x00 | opcode `0x2028` |
| +0x04 | pad / buy helper |
| +0x08 | item COID i64 (sold item TFID coid; global forced 1 on client resolve) |
| +0x10 | related COID i64 (buy) |
| +0x18 | related COID i64 (buy) |
| +0x20 | credits i64 (absolute → character+0x720) |
| +0x28 | `bWasSuccessful` |
| +0x29 | `bIsBuy` (1=buy, 0=sell) |
| +0x2c | quantity i32 |

**Sell success path:** resolve item → set credits → destroy cargo (type 4 store UI refresh via `FUN_007fee30`). If cargo destroy misses (item only on cursor), client falls through to **`FUN_007fc150`** (same drag-clear family as equip `PutInHand` / inventory drop).

Do **not** send `DestroyObject` for the sold TFID *before* 0x2028 — that orphans the cursor so resolve fails and the hand never clears.

## Code

- `Managers/ObjectUseManager.cs`  
- `Managers/InteractTriggerService.cs` — spawn TriggerEvents → Trigger → reactions  
- `Managers/VendorStoreService.cs` — spatial OpenStore fallback  
- `Managers/FacilityOpenService.cs`  
- `Reaction.TriggerCore` explicit Open* cases → true (0x206C)  

## Live success logs

```
UseObject: fire trigger coid=9838 from spawn=9837 …
```
followed by GroupReactionCall for OpenStore reaction 9839.  



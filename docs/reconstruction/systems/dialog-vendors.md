# System: Dialog / Vendors / NPC interact

**ID:** SYS-VENDOR  
**Priority:** 9  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15  
**Related UF:** UF-003 **closed** (dialog+store wire path); runtime still UF-002 / WQ-RT-01

## Scope

NPC UseObject → mission dialog / reaction open-store → dialog response → store buy/sell transactions.

## Vertical (end-to-end)

```
Client_SendUseObject (0x2072)                 aa_exe_00916740
        │
        ▼  server ObjectUseManager
   ┌────┴─────────────────────────────┐
   │ Mission dialog path              │ Vendor / facility path
   │ NpcInteractHandler               │ InteractTriggerService / VendorStoreService
   │ S2C 0x206D NpcMissionDialog      │ S2C 0x206C GroupReactionCall (OpenStore reaction)
   └────┬─────────────────────────────┘
        │
        ▼
Client_RecvNpcMissionDialog @ 0x00815070     OR  Client_RecvGroupReactionCall @ 0x008092a0
        │                                              │ reaction vtable+0x2c0 Apply
        ▼                                              ▼
Client_ShowNpcMissionDialogUI                 store UI (CVOGStore session)
Client_NpcDialog_PrepareResponseOpcode        │
  (dialog+0x650 = 0x206E)                        ├─ buy:  FUN_0088e180 → C2S 0x2027 IsBuy=1
        │                                        └─ sell: DropToGrid type4 → C2S 0x2027 IsBuy=0
Client_MissionDialogHandleButton @ 0x008ae7c0
  state0: C2S 0x206F
  state1 offer: local GiveMission + fill 0x206E fields
  state1 turn-in: local CompleteObjective
        │
        ▼
Client_SendMissionDialogResponse FUN_008ab8f0
  send dialog+0x650 size 0x20 (opcode 0x206E)
        │
        ▼
S2C StoreTransactionResponse 0x2028 → FUN_00810670
```

## Entry points

| Symbol | Address | Dir | Opcode | Role |
|--------|---------|-----|--------|------|
| Client_SendUseObject | 0x00916740 | C2S | 0x2072 | Interact / start vertical |
| Client_RecvNpcMissionDialog | 0x00815070 | S2C | **0x206D** | Open NPC mission dialog |
| Client_RecvGroupReactionCall | 0x008092a0 | S2C | **0x206C** | Reaction batch (OpenStore, GiveMission UI, etc.) |
| Client_NpcDialog_PrepareResponseOpcode | 0x008abd70 | local | sets 0x206E | dialog+0x650 |
| Client_MissionDialogHandleButton | 0x008ae7c0 | UI / C2S | 0x206F / prep 0x206E | Button state machine |
| Client_SendMissionDialogResponse (`FUN_008ab8f0`) | 0x008ab8f0 | C2S | **0x206E** | Send response size 0x20 |
| Client_SendStoreTransactionBuy (`FUN_0088e180`) | 0x0088e180 | C2S | **0x2027** | Buy size 0x40 IsBuy=1 |
| Client_UI_InventoryDropToGrid (store branch) | 0x00860a50 | C2S | **0x2027** | Sell size 0x40 IsBuy=0 |
| Client_RecvStoreTransactionResponse (`FUN_00810670`) | 0x00810670 | S2C | **0x2028** | Buy/sell ack size 0x30 |
| FUN_0080ce90 | 0x0080ce90 | S2C | 0x2025 | StoreOpen_Response (legacy/optional) |

## Packet layouts

### 0x206D NpcMissionDialog (S2C)

| Offset | Size | Field |
|--------|------|-------|
| +0x00 | 4 | Opcode 0x206D |
| +0x08 | 16 | NPC TFID |
| +0x18 | 4 | count (client uses **low byte**) |
| +0x20 | 40×N | entries: missionId@+0, 8×itemCoid@+8 |

Handler does **not** filter completed missions; server must offer only eligible ids.

### 0x206E MissionDialogResponse (C2S)

| Offset | Size | Field |
|--------|------|-------|
| +0x00 | 4 | Opcode 0x206E |
| +0x04 | 4 | missionId |
| +0x08 | 4 | accepted/button (often **0 on OK** live) |
| +0x0c | 4 | pad |
| +0x10 | 16 | MissionGiver TFID |
| **Total** | **0x20** | buffer at dialog+0x650 |

### 0x206C GroupReactionCall (S2C)

Wire: bit-packed count + entries (see `GroupReactionCallPacket`).  
Client apply function uses **decoded** stride-0x28 entries → reaction `vt+0x2c0` or `CVOGMap_SetVariable`.

EMSG name note: historical docs confuse `EMSG_Sector_MissionDialog` index with 0x206C; **dispatch table** maps:

| Opcode | Handler symbol |
|--------|----------------|
| 0x206C | Client_RecvGroupReactionCall |
| 0x206D | Client_RecvNpcMissionDialog |
| 0x206E | C2S only (no recv case) |

### 0x2027 StoreTransactionRequest (C2S)

| Offset | Size | Field |
|--------|------|-------|
| +0x00 | 4 | 0x2027 |
| +0x18 | 16 | item TFID (store-slot on buy; cargo/cursor on sell) |
| +0x28 | 16 | store TFID/COID (buy; may be zero on sell) |
| +0x38 | 1 | IsBuy (1 buy / 0 sell) |
| +0x3c | 4 | quantity |
| **Total** | **0x40** | |

### 0x2028 StoreTransactionResponse (S2C)

| Offset | Size | Field |
|--------|------|-------|
| +0x00 | 4 | 0x2028 |
| +0x08 | 8 | item coid |
| +0x10 | 8 | related (buy) |
| +0x18 | 8 | related (buy) |
| +0x20 | 8 | absolute credits → char+0x720 |
| +0x28 | 1 | success |
| +0x29 | 1 | isBuy |
| +0x2c | 4 | quantity |
| **Total** | **0x30** | |

## Dialog button states (`dialog+0x648`)

| State | Behavior |
|-------|----------|
| 0 | C2S **0x206F** size 0x18 |
| 1 | Offer accept / turn-in claim |
| 2 | Abandon confirm UI |
| 3 | Re-show NPC dialog |

Turn-in (`dialog+0x64c != 0`) runs **local** `CVOGReaction_CompleteObjective` — server must **not** also blast 0x2070 for the same completion.

## Vendor open path

1. UseObject on kiosk/NPC.  
2. Server fires spawn TriggerEvents → OpenStore reaction.  
3. **0x206C** delivers reaction COID; client Apply opens store by COID (not proximity).  
4. Buy/sell via **0x2027** / **0x2028**.  
5. StoreClose **0x202A** clears session (server-side; client string present).

Opcodes 0x2024 StoreOpen / 0x2026 StoreList appear unused for primary open (stock is local/clonebase + session creates).

## Evidence

- Ghidra decompiles this pass (`raw/aa_exe_00815070`, `008092a0`, `008ab8f0`, `008ae7c0`, `008abd70`, `00810670`, `0088e180`, `00860a50`)
- Dispatch map `raw/aa_exe_00815710.md`
- Prior topic extraction `docs/topic-extractions/vendor-store-useobject.md`
- AutoCore packets already aligned with layouts above

## Confidence

| Claim | Level |
|-------|-------|
| 0x206D/0x206E/0x206C opcode roles via dispatch + symbols | **confirmed** |
| 0x206E size 0x20 at dialog+0x650 | **confirmed** |
| 0x2027 size 0x40 buy/sell IsBuy | **confirmed** |
| 0x2028 size 0x30 success/isBuy/credits | **confirmed** |
| GroupReactionCall decoded field map | **probable** |
| Full OpenStore reaction type internals | **open** (optional residual WQ-020-r1) |
| Runtime differential | **blocked** UF-002 / WQ-RT-01 |

## Residuals (not eligible high-pri — see WORK_QUEUE Residual table)

Vertical (UseObject → dialog/store open → 0x2027/0x2028) is **closed** under WQ-020 / UF-003. Items below are optional static depth or blocked runtime only:

| ID | Residual | Class |
|----|----------|-------|
| WQ-020-r1 | OpenStore reaction Apply body (type-specific) | optional depth |
| WQ-020-r2 | StoreClose C2S writer (0x202A) | optional depth |
| WQ-020-r3 | Bitstream unpacker for 0x206C → 0x28-stride buffer | optional depth |
| WQ-RT-01 / UF-002 | Live capture for Accepted field and buy slot COID | blocked |

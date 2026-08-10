# Contributions from the Destroyer140 fork - for review

This fork (Destroyer140/AutoCore) is a deep, live-validated reverse-engineering effort against the retail 2007 client (`autoassault.exe`, image_base `0x400000`). A bidirectional comb of the two forks found several places where our implementation is the RE-verified winner or a strict superset. The small self-contained ones are already opened as separate PRs (WheelSet single-flag; LogicVar Type-7 full-on-zero-max; FailMission server-mirror).

The items below are **larger or wire-critical** - better discussed and integrated together than blind-merged. Each cites the client function(s) so you can verify independently in Ghidra. Our implementation paths are given for reference; happy to open focused PRs for any you want to take.


## 1. Damage 0x2023: multi-slot cone/AoE weapon-fire + multi-hit packet as the base (adopt SCAR's Resist/Deflect flags in return)

**What our fork does:** Vehicle.ProcessCombatIfFiring fires every armed slot 0..2 into its own cone (front offset 0, rotating turret, rear offset PI), ValidArc as cosine dot-threshold, SprayTargets multi-target, ExplosionRadius AoE (AcquireExplosionTargets), per-class attacker-level damage table, Theory penetration, mission-kill credit — accumulating ALL hits into one DamagePacket.Hits list with per-hit crit. SCAR's TrySendDamagePacket adds a single AddHit against one hard-selected Target (Vehicle.cs:1475).

**Why (client-verified):** unpackDamage@0x636f00 walks hitCount (readBits 0x10) entries — each crit flag->hit+0x14, damage readBits(0x10)->hit+0x10, coid readBits(0x40)->hit+0x00, target-global->hit+0x08 — so multiple targets in ONE packet is the intended wire shape (ours' Hits list does this; SCAR's single AddHit does not). The client fires slots 0..2 each into its own cone: SetWeaponsFiring@0x5021d0, FireWeaponsPrimary@0x4f50d0, cone-cast FindDistanceToTarget@0x4e9aa0 (acos(ValidArc) half-angle). crit-first ordering is correct (hit+0x14 first flag -> 0x812a60 uStack_f -> notif+0x29 rendered '!' in 0x93ffb0). In return, ours should adopt SCAR's genuinely-correct trailing-flag mapping: IsResist->hit+0x1b->notif+0x2b (s_Resist_), IsDeflect->hit+0x1c->notif+0x2c (s_Deflect), verifiable at 0x812a60 (uStack_d=puVar7[7], uStack_c=puVar7[8]) and 0x93ffb0 case 0.

**Verify in Ghidra:** unpackDamage@0x636f00; Process_EMSG_Sector_Damage@0x812a60; UpdateDamageNotifications@0x93ffb0; SetWeaponsFiring@0x5021d0; FireWeaponsPrimary@0x4f50d0; FindDistanceToTarget@0x4e9aa0.

**Integration / scope:** Medium. Wire format is byte-identical for the shared fields, so the packet struct is compatible; the divergence is purely the server-side fire loop (target acquisition) plus a 2-field addition to WriteHit. Bidirectional PR: SCAR takes our fire model, we take his resist/deflect flags — clean, no schema break.

## 2. Inventory: footprint-aware CargoGrid occupancy (replaces flat 1-cell slot model)

**What our fork does:** CargoGrid.cs models a Columns(6) x (Pages*13) per-cell grid; each item reserves a WxH footprint from InvSizeX/InvSizeY; CanPlace does AABB overlap; TryFindFreeSlot scans footprint-aware; sort re-packs with tail tiebreak. SCAR's InventoryManager uses a flat List with slot=y*Width+x, checks only the single (x,y) cell, and CharacterInventoryItem has no size field.

**Why (client-verified):** CreateGridSpace@0x570720 allocates a cols(+0x8)*rows(+0xc) per-CELL TFID array (8B/cell, init 0xFFFFFFFF) — a per-cell occupancy grid, not a per-item list. AddInventoryItem@0x571620 reads width=*(spec+0x406)/height=*(spec+0x407) (spec via item+0xa8->+0x3c) and stamps the item TFID into EVERY cell of the WxH footprint. FitsInInventoryAtPosition@0x570840 rejects if any covered cell != 0xFFFFFFFF; FindAvailableInventoryPosition@0x5713a0 is footprint-aware; ISortInventory@0x572730 re-packs by footprint + InsertAtTail. Grid dims pinned by CreateInventory@0x4F3A30->CVOGInventory(6, slots*0xD, slots)@0x572650 = SCAR's own VehicleCargoCapacity. Consequence SCAR can reproduce: flat TryGetFirstFreeCargoSlot returns a cell under an existing 2x2 item's footprint, the client's FitsInInventoryAtPosition rejects the add, and the held module is destroyed.

**Verify in Ghidra:** CreateGridSpace@0x570720; AddInventoryItem@0x571620; FitsInInventoryAtPosition@0x570840; FindAvailableInventoryPosition@0x5713a0; ISortInventory@0x572730; CreateInventory@0x4F3A30 / CVOGInventory@0x572650.

**Integration / scope:** Medium. Isolated to the occupancy layer — swap SCAR's LoadItems/CanAdd/TryMove/TryGetFirstFreeCargoSlot/IsFull to delegate to CargoGrid and add a footprint field to CharacterInventoryItem (resolve from clonebase at load). His persistence/currency/catalog scaffolding is untouched and kept.

## 3. CreateVehicle: absolute-offset module/name layout + filled-sub-object padding (fixes name corruption at wire 0xD54)

**What our fork does:** CreateVehiclePacket anchors every module and trailing field to Ghidra-verified absolute offsets (ornament@+0x158, powerplant@+0x308, wheelset@+0x458->veh+0x258, weapons@+0x890/0xA18/0xBA0 stride 0x188, Name@wire 0xD54, body end 0xD78) with NestedFilledUsesPadding=true (+4 on filled sub-objects for the sector Extended path). SCAR writes the same fields sequentially with Name at its natural position after the 3 weapon CBIDs and no filled-padding override.

**Why (client-verified):** The client reads modules at fixed slot offsets (module setters FUN_00504480/FUN_004fedc0 place wheelset->veh+0x258, weapons->veh+0x260[] stride 0x188) and the vehicle custom name at absolute wire 0xD54 (body 0xD78, where CreateVehicleExtended appends NumInventorySlots at param_1[0x35e]). SCAR's sequential Name lands ~6 bytes early (0xD4E) = the exact client-visible name corruption ours' v206 fix documents ('Phoenix'->'*x*'), and omitting the +4 filled-sub-object padding drifts filled modules on the retail sector Extended path.

**Verify in Ghidra:** Module setters FUN_00504480/FUN_004fedc0 (wheelset veh+0x258, weapons veh+0x260 stride 0x188); custom-name read at wire 0xD54; body 0xD78; CreateVehicleExtended NumInventorySlots at param_1[0x35e].

**Integration / scope:** Medium. Contained to CreateVehiclePacket serialization; needs the absolute-offset anchoring + NestedFilledUsesPadding flag. Confidence medium in the RE (AVD offset pairing unverified) but the name/padding drift is client-observable.

## 4. Reaction UnlockContObj/RelockContObj (cases 0x20/0x46): GenericVar1 is the continent id, not an objectiveId

**What our fork does:** HandleUnlockContObj/RelockContObj read GenericVar1 as the CONTINENT id and call ExplorationManager.UnlockContinent/RelockContinent (marks unlocked, persists, sends deterministic 0x205B UnlockRegion). SCAR mis-reads GenericVar1 as an objectiveId, does GetMissionByObjectiveId, and only logs via IncompleteHandlerLog.Warn — no unlock, no persistence.

**Why (client-verified):** Dispatcher@0x57c500 case 0x20 calls CVOGCharacter::UnlockContinentObject(char, *(this+0x25c)) where this+0x25c is GenericVar1; case 0x46 calls RelockContinentObject(char, GenericVar1). GenericVar1 is passed straight as the continent-object id (see UnlockContinentObject@0x531c80 / RelockContinentObject@0x52a1b0), refuting the objectiveId interpretation. The client self-applies via the forwarded 0x206C, so ours is a strict superset (client visual + server authority + relog persistence).

**Verify in Ghidra:** CVOGReaction_ExecuteDispatcher@0x57c500 cases 0x20/0x46; UnlockContinentObject@0x531c80; RelockContinentObject@0x52a1b0.

**Integration / scope:** High / small. Single-handler correction (parameter reinterpretation + wire the existing unlock/persist call). Independent of the rest of the reaction merge.

## 5. Reaction RollFromLootTable (case 0x51): server-authoritative loot grant

**What our fork does:** HandleRollFromLootTable reads GenericVar1 as a tLootTable id, calls LootManager.GenerateLootFromTable, auto-loots or world-spawns to the activating character (how boss VEHICLES get a loot table despite not being Creatures). SCAR is unhandled (falls to default IncompleteHandlerLog).

**Why (client-verified):** Dispatcher@0x57c500 case 0x51 does a client-side CNDDice::RollFloatIndexed + loot presentation only; the client cannot mint inventory, so item grants are server-authoritative with GenericVar1 (this+0x25c) as the roll input. SCAR's unhandled path yields no loot.

**Verify in Ghidra:** CVOGReaction_ExecuteDispatcher@0x57c500 case 0x51 (CNDDice::RollFloatIndexed presentation only).

**Integration / scope:** Medium. Handler is small but depends on our LootManager/GenerateLootFromTable; SCAR would need that or an equivalent. Confidence medium (server gameplay, not directly wire-arbitrable).

## 6. Trigger ActivateDelay deferred-fire + on-death TriggerEvents dispatch (retail server timing)

**What our fork does:** TriggerManager honors TriggerTemplate.ActivateDelay (defers reaction fire N seconds via Timer, re-checks conditions at fire time), FireOnDeathTrigger dispatches ObjectTemplate.TriggerEvents[0] on entity death, plus Map707 airlock handling. SCAR fires all reactions instantly and has no on-death TriggerEvents dispatch or airlock handling.

**Why (client-verified):** Client CVOGEntity_EnterDeathState@0x519d80 reads the per-instance TriggerEvents field and queues the linked trigger (FUN_004d2700/FUN_004d0250); ours mirrors that server-side. SCAR's instant-fire regresses the map-707 airlock: trigger 17903 has ActivateDelay=18, so firing it same-tick re-closes the big door mid-open, and on-death TriggerEvents never run (orphaned FX / missing on-death spawns).

**Verify in Ghidra:** CVOGEntity_EnterDeathState@0x519d80 (per-instance TriggerEvents -> FUN_004d2700/FUN_004d0250); map707 trigger 17903 ActivateDelay=18.

**Integration / scope:** Medium. Server-side scheduling addition to TriggerManager; net-new in ours, no wire change. Confidence medium (retail timing, asserted via code + map data).

## 7. Inventory drop 0x2036: +0x10 is the item subtype byte, not the global flag

**What our fork does:** InventoryDropPacket reads +0x10 as InvSubtype (item+0x168). SCAR reads RawBytes[0x10] as ItemGlobal (bool) and echoes it in the drop response.

**Why (client-verified):** CWndInventoryGrid::DropItem@0x860a50 builds the 0x2036 buffer as opcode@+0, item+0x160@+0x08 (coid low), item+0x164@+0x0c (coid high, which CARRIES the global flag), item+0x168@+0x10 (the inventory-subtype byte, written as uStack_f0), col@+0x18, rowsPerPage*page+row@+0x19, mode@+0x1a. The global flag lives in the coid TFID high dword at +0x0c, NOT at +0x10. Impact is minor (SCAR only echoes ItemGlobal and derives coid from the full i64 at +8) but it is a wire-faithfulness error.

**Verify in Ghidra:** CWndInventoryGrid::DropItem@0x860a50 (item+0x168@+0x10; global in coid-high dword @+0x0c).

**Integration / scope:** High / trivial. One-byte relabel in InventoryDropPacket. Ships naturally alongside the footprint-grid PR.

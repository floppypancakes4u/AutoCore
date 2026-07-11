# Ghidra RE: owner-on × null wheel race (2026-07-11)

Retail client `0.0.14.117.2007.2.1.11`. Live PathA + WireDiag: ghost `owner=1/1` + `EquipFromCreate wheel_cbid=0` + `ActivateEnterWorld` → AV `0x004F5566`.

Server commit baseline (tooltip-free, owner **off**): `74f0d0c`.

---

## 1. `FUN_00812630` — CreateVehicle / object-table apply (`0x00812630`)

### Control flow

```
lookup = FUN_004bb010(packet + 0x90)   // TFID table lookup
if lookup == 0:
    // NEW object
    item = GiveItemByCbid(packet.CBID)
    vfunc apply create (mode from packet)
    if packet[+0xA1] != 0:            // IsItemLink
        clear packet[+0xD8/+0xDC]
        call FUN_008024d0(...)         // UI path (tooltips)
        FUN_009972a0()
    ...
else:
    // EXISTING object
    if packet[+0xA1] != 0:            // IsItemLink
        vfunc re-apply create (0x84 / 0xc4)
        FUN_008024d0(...)             // same UI path
        FUN_009972a0()
    if packet[+0xC0]:
        inventory path only
    // *** NO re-apply when +0xA1 == 0 ***
```

### Contract

| Condition | Behavior |
| --- | --- |
| TFID missing | Full create + nest equip |
| TFID present, **IsItemLink=0** | **No-op on nest** (object kept as-is) |
| TFID present, **IsItemLink≠0** | Re-apply nest **and** runs `FUN_008024d0` (item-link / tooltip UI) |

**Live implication:** post-ghost `CreateVehicle` without IsItemLink cannot repair a ghost zero-nest on an already-tabled object. IsItemLink repairs nest but spams tooltips via `FUN_008024d0`.

**RE follow-up:** map `FUN_008024d0` / `FUN_009972a0` for any flag that re-applies without UI; confirm `+0xA1` is the sole re-apply gate.

---

## 2. `FUN_00504480` — EquipFromCreate (`0x00504480`)

Caller: **`Vehicle_applyCreatePacket` (`0x00505270`)** only (create-packet path).

### Wheelset branch (first hardpoint)

```
// param_2 = create buffer (nested CreateVehicle body)
// wheel nest: opcode at +0x458, CBID at +0x45c

if vehicle not inventory-mode:
  try resolve existing nest object (FUN_004bb950)
  if missing:
    item = GiveItemByCbid(*(param_2 + 0x45c))   // CBID
    if item:
      apply nest blob via vfunc 0xc4 on (param_2+0x458)
  if item ok:
    FUN_004fea90(item)   // SetWheelset → vehicle+0x258
  else:
    log "allocatenewobjectfromcbid failed"   // NO SetWheelset
```

### `FUN_004fea90` SetWheelset

```
vehicle + 0x258 (600 decimal) = wheelset_ptr
// only if param_2 != 0
```

### PathA match

Zero-nest hits: `wheel_cbid=0`, `nest_hex` starts `1B20…` (= opcode `0x201B` LE).  
`GiveItemByCbid(0)` fails → **no SetWheelset** → `v258` stays 0. Function does **not** explicitly null an existing wheelset; it simply never assigns one on this path.

---

## 3. Ghost blob zero nest — `FUN_005F5AD0` (`0x005F5AD0`)

Called at start of **initial** `VehicleNet_UnpackGhostVehicle` when `DAT_00d1798c != 0` (initial):

```
alloc 0xD78 create buffer
memset / word-zero loop
*buf = 0x201D                  // CreateVehicle opcode
buf+0x458 = 0x201B             // nested CreateWheelSet opcode
// +0x45c (wheel CBID) left 0 from zero-fill
// many equipment CBIDs set to -1, but wheel CBID is NOT set to -1
```

**Ghost initial materialize always builds a CreateVehicle-shaped buffer whose nested wheel CBID is 0.**

Hardpoint flags later in unpack (`0x201B` object + `VehicleNet_PostCorrectionEvent`) do **not** call `FUN_00504480` / `FUN_004fea90` immediately. PostCorrection posts deferred events (`0x203C` / `0x203E`) — not a same-call SetWheelset.

Delta hardpoints (non-initial) use a different path that PathA sees as `SetWheelset` — too late if activate already ran.

---

## 4. Owner → activate — `Vehicle_setDrivingInputs` + `FUN_00503F30`

### Unpack end (position present)

When position flag `local_f0` is set during unpack:

```
Vehicle_setDrivingInputs(...)
```

### `Vehicle_setDrivingInputs` (`0x00504C70`)

```
if vehicle+8 != 0:
  write throttle/steer inputs
  if param_9 == 0 && vehicle+0x1A0 == 0:     // no VehicleAction yet
    owner = vehicle+0xB0 (via RTTI layout)
    if owner != 0 && owner identity matches:
      FUN_00503F30()                        // ActivateEnterWorld
```

### `FUN_00503F30` ActivateEnterWorld (`0x00503F30`)

```
// ... owner/graphics setup ...
if (vehicle + 0x1A0 == 0):
  Vehicle_createVehicleAction()             // 0x004FB660
// NO check of vehicle+0x258
```

`Vehicle_createVehicleAction` → Havok build → `FUN_004F5560` reads `*(vehicle+0x258)+0xB0` → **AV if +0x258 is null**.

### Callers of ActivateEnterWorld

- `Vehicle_setDrivingInputs` (ghost unpack position path)
- `VehicleNet_ReconcilePrediction` (`0x005F9F10`)
- (PathA also hits ActivateEnterWorld around crash)

---

## 5. End-to-end race (server owner-on)

```
T0  CreateVehicle (good nest)  ──► game packet queue
T1  hold ends → ObjectInScope
T2  Ghost initial unpack:
      FUN_005F5AD0 → nest wheel CBID = 0
      owner block allocated (creature form)
      hardpoints → PostCorrection only (no SetWheelset)
      position → setDrivingInputs → FUN_00503F30 if owner
                 → createVehicleAction with +0x258 still 0 → AV
T3  CreateVehicle re-apply without IsItemLink → table hit → NO nest repair
T3' CreateVehicle re-apply with IsItemLink → nest repaired + FUN_008024d0 tooltips
T4  WheelSetMask delta SetWheelset  (too late if T2 already AVed)
```

Client tick order (`FUN_008078b0`): **ghost before game packets** on the same frame → even a same-frame post-scope CreateVehicle is processed **after** ghost activate.

---

## 6. What server can / cannot do (from RE)

| Approach | Verdict |
| --- | --- |
| IsItemLink re-apply after ghost | Fixes nest; **tooltips via FUN_008024d0** |
| Create without IsItemLink after ghost | No-op if TFID exists → **crash** |
| Longer hold only | Helps first create; **does not stop ghost zero nest + owner activate** |
| WheelSetMask delta | Sets +0x258 **after** initial; loses race with owner on initial |
| Pack wheel on initial hardpoint | Unpacks to PostCorrection, **not** immediate SetWheelset |
| Withhold owner on initial | Avoids activate; need RE if owner can be sent later on delta |
| Non-zero CBID in ghost `FUN_005F5AD0` buffer | Server does not control that zero-fill; would need client change or different create path |

---

## 7. Follow-up RE (2026-07-11 continued)

### 7.1 Tooltip path — `FUN_008024d0` / `FUN_0084d140`

Called only from create-table apply when **`packet+0xA1 != 0`** (and sibling create paths `0x00811e00`, `0x008120d0`, `0x00997310`).

```
FUN_008024d0:
  alloc UI object via FUN_0084d140(...)
  FUN_0084d140:
    FUN_0084b660("i_d_item.xml")     // item description window
  vfunc show "Getting Data..."
  attach UI to screen list
```

**Conclusion:** IsItemLink is hard-wired to the **item description tooltip UI**. There is no server bit on the create packet that re-applies nest without taking this path. Tooltip spam is not accidental.

### 7.2 Owner on delta? — `VehicleNet_UnpackGhostVehicle`

`DAT_00d1798c != 0` → **initial** branch:

- `FUN_005F5AD0(0,0)` zero create buffer
- colors / IsActive / path / template / spawn / tricks / trailer
- **owner present flag** → `FUN_005F5AD0(1,1)` creature owner or `(1,0)` character owner
- (ends ~line 951 of decompile)

`DAT_00d1798c == 0` → **delta** branch:

- skills / hardpoints via `FUN_005b2800` + `VehicleNet_PostCorrectionEvent`
- health / state / **position** (sets `local_f0`)
- **no owner allocate / no CurrentOwner read**

On delta, only small owner-adjacent fields appear (`+0x127` AI, `+0x12a` GM) when those mask flags are set — **not** the full owner object.

**Conclusion:** CurrentOwner is **initial-only**. You cannot “send owner on the next ghost delta after wheels” with this unpacker. Withholding owner on first initial means **owner never arrives** unless a later full re-ghost initial is forced (TNL does not re-send initial for the same ghost).

### 7.3 PostCorrection `0x203C` / `0x203E` (superseded by §10)

See **§10** for the full three-role map of opcode `0x203C`. Short form:

- Ghost hardpoint **delta** + “unhappy” (`+0x103==0`) → `VehicleNet_PostCorrectionEvent` synthesizes a 64-byte **`0x203C`** blob and **pushes** `FUN_005b2d70` only.
- That path does **not** call `SetWheelset`, `EquipFromCreate`, or `FUN_00813f40`.
- S2C game-packet `InventoryEquip` (`0x203C`) **can** SetWheelset, but only via `Client_PacketDispatch` → `FUN_00813f40` after ghost work on the same tick.

### 7.4 Ordering inside one ghost initial unpack

```
1. Zero create buffer (FUN_005F5AD0; wheel CBID = 0 at +0x45c)
2. Owner object embedded (if flag) — initial only
3. Hardpoint flags:
     INITIAL: write CBID/TFID into create-buffer fields only (no PostCorrection, no SetWheelset)
     DELTA:   FUN_005b2800 + VehicleNet_PostCorrectionEvent (queue 0x203C/0x203E only)
4. … other masks …
5. Position (local_f0) → Vehicle_setDrivingInputs
       → if owner && no action → FUN_00503F30
       → createVehicleAction → need +0x258
```

`local_12c` (vehicle for hardpoints/activate) is resolved from the ghost’s bound game object (`local_138[0x14]` vfunc `0x1d4`). If that object was never equip-nested, `+0x258` is still 0 at step 5.

### 7.5 Race is real (not just “bad create”)

Client tick (`FUN_008078b0`): ghost object-create/update **before** game packet queues (`Client_PacketDispatch`).

| Server intent | Client effect |
| --- | --- |
| CreateVehicle (good nest) then later ObjectInScope | Safe **if** TFID is already tabled before ghost materialize/activate |
| Ghost first (no table hit) with owner+pose | Zero nest materialize + activate → AV |
| CreateVehicle after ghost, IsItemLink=0 | Table hit → no re-apply → zero nest remains |
| CreateVehicle after ghost, IsItemLink=1 | Re-apply nest + **i_d_item.xml** tooltips |
| WheelSetMask / hardpoint delta | Correction / later equip; loses race with activate on initial |
| S2C InventoryEquip `0x203C` same tick as ghost | Runs **after** ghost in `FUN_008078b0` → too late if activate already AVed |

---

## 8. Recommended product path (updated 2026-07-11 evening)

**Baseline (always):** create-before-ghost hold + nest re-apply without IsItemLink + optional `EnableInitialHardpointPack` — commit `74f0d0c` family.

**Owner-on (validated):**

| Path | Lever(s) | Live | Role |
| --- | --- | --- | --- |
| **P2 reghost** | `EnableForeignReghostOwner` + owner block; **defer pose off** | **Pass** | **Feature-complete owner+pose initial** — preferred for max fidelity (§12) |
| **P1 defer pose** | `EnableDeferredForeignPose` + owner block; reghost off | **Pass** | Smoother UX (no descope); pose arrives on delta (`8705611`) |

**Do not** ship owner + pose on the **first** foreign initial without P1 or P2 (crash proven).

Full P2 design + gap list: **§12**. Comparison: **§11.7**.

---

## 9. Suggested next RE sessions

1. ~~Event queue consumer for `0x203C` / `0x203E`~~ — **done** (§10).
2. ~~Non-create S2C SetWheelset paths~~ — **done** (§10.4).
3. ~~`FUN_008078b0` ghost vs game order~~ — **done** (§10.3).
4. ~~Initial hardpoint → create-buffer `+0x45c`~~ — **done** (§11).
5. ~~P1 defer pose~~ — **live pass** (§11.6); commit `8705611`.
6. ~~P2 reghost second initial~~ — **live pass** (§12).
7. **Next product:** expand minimal foreign **delta** masks (AI → health → target → weapons) one family at a time.
8. Optional: PathA evidence for `v258` under P2.
9. Map remaining initial hardpoint slots if full nest seed is needed later.

---

## 10. Deep RE: opcode `0x203C` and the “is retail broken?” question (2026-07-11)

### 10.1 Three distinct uses of `0x203C` (same number, different roles)

| Role | Path | Calls SetWheelset? | When relative to activate |
| --- | --- | --- | --- |
| **A. S2C InventoryEquip game packet** | `Client_PacketDispatch` (`0x00815710`) `case 0x203c` → `FUN_00813f40` → (type `0x10`) `FUN_004ff510` → `FUN_004FEA90` | **Yes** (if item resolves as wheelset) | **After** all ghost work on the tick (`FUN_008078b0`) |
| **B. Ghost PostCorrection synthesis** | Hardpoint **delta** + unhappy → `VehicleNet_PostCorrectionEvent` (`0x005F7360`) builds 64-byte blob `*buf=0x203C`, `FUN_005b2d70` push, `FUN_005a0b30` spatial | **No** | Same unpack as hardpoints; **before** position/activate in unpack order, but **does not equip** |
| **C. C2S UI equip request** | `FUN_00931440` emits `0x203C` (or `0x2053` for type `0xe`) via connection vfunc `+0x18` | N/A (client→server) | Player inventory UI |

String evidence:

- `"EMSG_Sector_InventoryEquip"` / `"Requesting InventoryEquip: char:%I64d Old:%I64d New:%I64d\n"` on role A.
- AutoCore `InventoryEquipPacket` documents the same 64-byte S2C layout as PostCorrection’s synthesized blob (item TFID, vehicle TFID, old item, putInHand, x/y, typeFrom).

`0x203E` is the sibling **InventoryUnequip** game packet (`FUN_00813bf0`) and the PostCorrection “no item / unequip-shaped” synthesis.

### 10.2 PostCorrection in detail (`0x005F7360`)

```
if vehicle ghost object present:
  happy = *(create_related + 0x103)
  if happy == 0:                          // "unhappy" hardpoint object
    if param_2 (item object) != 0:
      // optional first queue entry for item create wrapper
      malloc 0x40; * = 0x203C
      copy item TFID, vehicle TFID (+0x160..), old-item TFID (param_3), flags
      wrap buffer in event; FUN_005b2d70(&event)
      FUN_005a0b30(ghost, ghost+0x40)     // spatial tree insert only
      return
    else:
      // 0x203E 0x30-byte unequip-shaped blob; same queue + spatial
      return
// happy path: operator_delete(param_2) — still no SetWheelset
```

**Callers of `FUN_005b2d70` (complete):** only

1. `VehicleNet_PostCorrectionEvent` (three sites)
2. `FUN_005b2690` (inbound ghost correction blob unpack → push same queue)

There is **no** third consumer that pops this queue and calls `FUN_00813f40` / `FUN_004FEA90`.  
`FUN_005b2830` is the **pack/export** side of correction traffic (bitstream out).  
`FUN_005a0b30` → `FUN_005a3b00` is a **map/spatial tree** insert, not equip.

**Hardpoint unpack split (`VehicleNet_UnpackGhostVehicle`):**

| `DAT_00d1798c` | Wheel hardpoint behavior |
| --- | --- |
| **≠ 0 (initial)** | Store CBID/TFID into create-buffer fields; **no** `FUN_005b2800`, **no** PostCorrection |
| **== 0 (delta)** | `FUN_005b2800` alloc object shaped as `0x201B`, then `VehicleNet_PostCorrectionEvent` |

So packing a “perfect” wheel hardpoint on **initial** does not SetWheelset and does not even synthesize role-B `0x203C`. It only annotates the zeroed create shell. Materialize still depends on `FUN_00812630` / prior game CreateVehicle for real nest equip.

### 10.3 Tick order (`FUN_008078b0`) — why game `0x203C` cannot save activate

Documented control flow:

1. **Ghost list first:** “Creating object from ghost” / “Assigned a ghost to waiting” / “Updating from ghost”; may `FUN_00812630` for opcode `0x201D` create buffer on the ghost; attach via vfunc `0x2b8`.
2. **Then** drain game packet queue → `Client_PacketDispatch` (including `0x203C` InventoryEquip, `0x201D` CreateVehicle, etc.).

Therefore even a same-frame server plan of “ghost hardpoints + S2C InventoryEquip wheels” still runs **equip after** ghost unpack. If unpack already called `Vehicle_setDrivingInputs` → `FUN_00503F30` with null `+0x258`, the process is already crashing (or has AVed) before role-A `0x203C` runs.

### 10.4 Complete `FUN_004FEA90` (SetWheelset) callers

| Caller | Role |
| --- | --- |
| `FUN_00504480` EquipFromCreate | Nested create wheel (game CreateVehicle / ghost materialize buffer) |
| `FUN_004ff510` | Equip/replace wheelset object (InventoryEquip type `0x10`, menu equip, unequip-to-null) |
| `FUN_00503780` | Template spawn equip path |
| `FUN_005252f0` | Vehicle switch / transfer path |
| `FUN_00563ab0` | Clone/spawn with default wheel from vehicle CBID `+0x6f4` |
| `FUN_0090ed80` | GiveItemByCbid vehicle kit path (type `0xe` → default wheel) |
| `FUN_00833680` | **Fallback** if `vehicle+600==0` and template default wheel CBID ≠ −1 |
| `FUN_00833d50` / `FUN_00833e30` / `FUN_008d37d0` | Teardown / UI vehicle select (often SetWheelset(0) or rebind) |

**Not callers:** `VehicleNet_PostCorrectionEvent`, `FUN_005b2d70`, `FUN_005b2690`, `VehicleNet_UnpackGhostVehicle`.

`FUN_00833680` looks like a safety net but its callers are **UI/vehicle-select** (`FUN_0083ab90`, `FUN_0084b210`, `FUN_0088d980`) — **not** the ghost activate path. Retail does **not** auto-default-wheel mid-`setDrivingInputs`.

### 10.5 Is retail “broken”? — deep ruling

**Verdict: No — this is not a retail production bug.** It is a **latent client footgun** that retail’s **send order contract** almost always avoids. AutoCore hits it by violating that contract (owner-bearing initial while `+0x258` is still null).

#### Why a “huge retail bug” is implausible

If ghost initial + owner + pose routinely activated with null wheels, **every driven vehicle** would AV at `0x004F5566` in retail. The game shipped and was playable; that alone makes a always-on retail defect extremely unlikely. RE explains **how** retail stays out of the bad state without needing a magic PostCorrection fix:

| Retail (expected) | AutoCore owner-on failure mode |
| --- | --- |
| `CreateVehicle` (full nest, non-zero wheel CBID) **before** scope/ghost | Ghost initial / materialize with `FUN_005F5AD0` zero nest |
| `FUN_008078b0` finds TFID → “Assigned a ghost to waiting” / “Updating” — **no** re-create with zero nest | Table miss or rebuild → create-from-ghost / EquipFromCreate `wheel_cbid=0` |
| `+0x258` already set when later initial/delta carries owner + pose | Owner initial-only + pose → activate with `+0x258==0` → AV |
| Game `0x203C` used for **player inventory equip acks**, not as spawn wheel bootstrap | Hoping PostCorrection/`0x203C` is the spawn wheel path — **wrong role** |

#### What looks “buggy” but is intentional scaffolding

1. **`FUN_005F5AD0` zero wheel CBID** — ghost builds a CreateVehicle-shaped shell; nest is **not** the authoritative wheel source when a prior game create already equipped the vehicle.
2. **`FUN_00503F30` has no `+0x258` null check** — true client fragility / missing assert; retail relies on ordering, not a guard. That is a **latent footgun**, not evidence retail constantly crashes.
3. **PostCorrection reuses InventoryEquip opcode bytes** — shared **layout** for “item moved onto vehicle hardpoint” signaling in correction export; **not** a synchronous local equip of nest wheels on ghost initial.
4. **Owner is initial-only** — retail can afford that because wheels (from create) are already present before owner-bearing initial is useful.

#### Residual “small possibility” cases (still not “retail is broken”)

| Case | Assessment |
| --- | --- |
| Packet loss / reorder so ghost owner initial arrives without prior create | Possible rare race on bad networks; would be a **sync bug**, not “0x203C is wrong.” Retail still designs for create-before-scope. |
| Client bug if create nest CBID is 0 and owner is set | Server misconfig; client will AV. Not a reason to treat PostCorrection as the fix. |
| AutoCore hold works without owner but fails with owner | Matches RE: hold fixes first create; owner on initial still demands `+0x258` **before** activate; any residual zero-nest materialize still dies. |

#### Disproven hypotheses from this pass

| Hypothesis | Result |
| --- | --- |
| `0x203C` PostCorrection flushes and SetWheelsets before activate | **False** — queue push only; no SetWheelset in call graph |
| S2C InventoryEquip same frame as ghost can pre-empt activate | **False** — game packets after ghost on tick |
| Retail must be broken if AutoCore crashes with owner-on | **False** — AutoCore ordering/owner-on violates create-before-activate invariant |
| Default-wheel `FUN_00833680` saves foreign ghost activates | **False** — UI/select callers only |

### 10.6 Implications for AutoCore

1. **Do not** treat PostCorrection / role-B `0x203C` synthesis as an owner-on fix.
2. **Do not** expect game-packet InventoryEquip to win a race with `Vehicle_setDrivingInputs` on the same tick as ghost.
3. **Owner-on** requires: game object in table with **`+0x258 != 0` before any owner-bearing initial that also carries pose/activate**.
4. **Ship state remains correct:** tooltip-free hold + owner **off** (`74f0d0c`) until that invariant is airtight.
5. **New lever (default off):** `EnableInitialHardpointPack` — see §11.

---

## 11. Initial hardpoint → create-buffer `+0x45c` (materialize equip seed)

### 11.1 Offset identity (confirmed)

Ghost initial unpack (`DAT_00d1798c != 0`), first hardpoint (wheel):

```
// VehicleNet_UnpackGhostVehicle — wheel hardpoint present on initial
puVar10[0x117] = puVar4;          // CBID from BitStream_readInt(20)
puVar10[0x13a] = local_128;       // item TFID lo
puVar10[0x13b] = local_124;       // item TFID hi
puVar10[0x137] = 1;
*(bool*)(puVar10 + 0x13c) = globalFlag;
puVar10[0x14a] = 0xffffffff;
```

| Dword index | Byte offset | EquipFromCreate use |
| --- | --- | --- |
| `0x117` | **`0x45c`** | `GiveItemByCbid(*(buf+0x45c))` — **wheel CBID** |
| `0x13a` / `0x13b` | **`0x4e8` / `0x4ec`** | `FUN_004bb950(buf+0x4e8)` — existing nest TFID lookup |
| (opcode) | **`0x458`** | Nested nest opcode `0x201B` (set by `FUN_005F5AD0`, not by hardpoint store) |

`FUN_005F5AD0` zero-fills the buffer then sets `*(buf+0x458)=0x201B` and leaves **`*(buf+0x45c)=0`**. Other equip CBIDs are set to **−1**. Wheel is uniquely “0 means missing,” not “−1 means empty nest packet.”

### 11.2 When that seed becomes SetWheelset

| Path | Uses `+0x45c`? | Immediate SetWheelset? |
| --- | --- | --- |
| Initial hardpoint store | Writes CBID into buffer only | **No** |
| `FUN_008078b0` “Creating object from ghost” → `FUN_00812630` → EquipFromCreate | **Yes** | **Yes** if CBID ≠ 0 and GiveItem succeeds |
| Game `CreateVehicle` nest | Separate nest body in game packet | Independent of ghost hardpoint fill |
| Delta hardpoint | PostCorrection queue / later equip | Not via create-buffer `+0x45c` |

**Ghost-first materialize order (no prior table hit):**

```
1. Initial unpack: FUN_005F5AD0 (wheel CBID=0) → hardpoints may overwrite +0x45c
2. Owner/pose may run, but local_12c often 0 → no activate yet
3. FUN_008078b0: FUN_00812630(create buffer) → EquipFromCreate(+0x45c)
4. Attach ghost; later pose+owner can activate with +0x258 set  (if step 3 succeeded)
```

**Create-first (good nest):**

```
1. Game CreateVehicle → EquipFromCreate from nest → +0x258 set
2. Ghost initial: object exists → no re-create from zero buffer
3. Owner+pose activate safe  (hardpoint initial fill irrelevant for live +0x258)
```

**Create-first (zero nest) + owner:**

```
1. Game CreateVehicle with wheel_cbid=0 → +0x258 stays 0
2. Ghost initial hardpoint fill does NOT re-equip the live vehicle
3. Owner+pose activate → AV
```

### 11.3 AutoCore prior mistake in the comment, not the default

Server historically forced `packEquipment = !isInitial` with a comment that initial hardpoints “only fill the create buffer” (implying useless). RE shows that fill **is** the equip input for **ghost materialize**. Default skip remains safe for create-before-ghost, but **ghost-first** (or any materialize-from-buffer path) always saw CBID 0.

### 11.4 Isolation lever: `EnableInitialHardpointPack` (default **false**)

| Setting | Wire effect |
| --- | --- |
| `false` (default) | All initial equipment flags false (unchanged) |
| `true` | Pack **WheelSet only** on initial when `WheelSetMask` is dirty; other slots stay initial-skipped |
| + minimal foreign | Minimal initial mask becomes `PositionMask \| WheelSetMask` so the wheel flag is not stripped |

**What it can fix:** ghost materialize EquipFromCreate seeing non-zero `wheel_cbid` after initial hardpoint unpack.  
**What it cannot fix:** live vehicle already created with null `+0x258` + owner activate on the same initial (no re-apply without IsItemLink).  
**What it is not:** PostCorrection / InventoryEquip role-A.

Live experiment (owner still off first):

```
wire set EnableInitialHardpointPack true
# PathA: EquipFromCreate on ghost materialize should show non-zero wheel_cbid when mask ships
# WireDiag GhostPack detail includes initWheel=1
```

Only after PathA proves non-zero nest materialize: consider `EnableMinimalForeignOwnerBlock true` as a **separate** A/B — not on the same day as first flip unless create-before-ghost is already solid.

### 11.6 Campaign: four priorities (2026-07-11)

| Priority | Approach | Lever | Live |
| --- | --- | --- | --- |
| **P1** | Owner on first initial; **pose deferred** to delta | `EnableDeferredForeignPose` + owner block | **PASS** — commit `8705611` |
| **P2** | First ghost **without** owner; descope; **second initial** owner+pose | `EnableForeignReghostOwner` + owner block | **PASS** — no AV |
| **P3** | Lab client null-`+0x258` guard | inject / private patch | not required |
| **P4** | Distributed client patch | fleet download | last resort only |

#### P1 — defer pose (detail)

**Mechanism:** Foreign initial omits `PositionMask` (kept dirty). Unpack never sets pose flag → no `Vehicle_setDrivingInputs` → no activate on that unpack. Owner still ships on initial (`clientOwner` latch for later AI/GM).

| Metric (conn 18423) | Value |
| --- | --- |
| Client crash | **None** |
| `owner=1/1` GhostPacks | 60 |
| Initial packs | 36, all `deferPose=1`, **0** with PositionMask |
| Initial mask | always `0x100000000` (WheelSet only) |
| Delta packs with PositionMask | 24 |
| Contrast | Prior owner+pose initial (`0x100000002`) → AV |

#### P2 — reghost owner (summary)

Full design, state machine, levers, live metrics, and **feature gap list**: **§12**.

### 11.7 Completeness comparison: P1 vs P2

| Dimension | **P1 defer pose** | **P2 reghost** |
| --- | --- | --- |
| No AV (live) | Yes | Yes |
| Owner on wire | First initial | Second initial only |
| Pose on wire | After first delta(s) | On second initial (with owner) |
| Owner+pose **same** ghost initial | No (by design) | **Yes** (second initial) |
| Client owner latch for AI/GM early | **Yes** (first initial) | Delayed until rescope |
| Visual / scope continuity | Continuous ghost | **Brief descope** (flicker risk) |
| Create-before-activate | Relies on hold + delayed pose | Relies on hold + first no-owner scope |
| Extra server complexity | Mask/dirty only | Per-coid state machine + descope |
| Pure server | Yes | Yes |
| Tooltip-free | Yes | Yes |

**Most complete wire form (owner+pose co-located on one initial after wheels):** **P2** — see §12.  
**Smoother UX without descope:** **P1**.

**Product choice (2026-07-11):** prefer **P2** when maximizing fidelity; keep P1 available as a lower-flicker alternative.

---

## 12. Priority 2 — foreign reghost owner (thorough)

### 12.1 Problem it solves

Retail client activates Havok when:

```text
owner present  ∧  setDrivingInputs (pose unpack)  ∧  VehicleAction empty
  → createVehicleAction → read *(vehicle+0x258) → AV if null
```

Sending **owner + pose on the first foreign ghost initial** (after or with CreateVehicle) still raced null `+0x258` live (`mask=0x100000002`, AV `0x004F5566`).

**P2 strategy:** never put owner on that first ghost. Let create + first ghost establish wheels. Then **force a second TNL initial** that may safely carry owner **and** pose together.

Owner cannot be added on a pure delta (unpack is initial-only for CurrentOwner). Descope + rescope is how TNL generates a new initial without a client patch.

### 12.2 High-level sequence

```text
  interest hits foreign global vehicle
           │
           ▼
  CreateVehicle (full nest, wheelOk) ── hold ──► first ObjectInScope
           │                                         │
           │                                         ▼
           │                              phase = FirstScopedNoOwner
           │                              PackUpdate: SUPPRESS owner
           │                              (pose/wheel/path/template OK)
           │                                         │
           │                              wait ForeignReghostDelayMs (default 500)
           │                                         │
           │                                         ▼
           │                              skip ObjectInScope once
           │                              log ForeignReghostDescope
           │                              TNL: not InScope → KillGhost
           │                                         │
           │                                         ▼
           │                              phase = Descoped
           │                              (may re-CreateVehicle if not ghosted)
           │                                         │
           │                                         ▼
           │                              ObjectInScope again
           │                              phase = RescopedWithOwner
           │                              PackUpdate: ALLOW owner
           │                              second initial: owner + pose (+ initWheel)
           ▼
  later deltas: still under minimal mask filter (see §12.6)
```

### 12.3 State machine (per connection × vehicle coid)

| Phase | Meaning | `ShouldSuppressForeignOwnerOnPack` | Scope behavior |
| --- | --- | --- | --- |
| *(absent)* | Never scoped under reghost | **true** (safe default) | Normal create/hold then first scope |
| `FirstScopedNoOwner` | First `ObjectInScope` recorded | **true** | Stay in scope until delay elapses |
| `Descoped` | One deliberate skip of `ObjectInScope` done | **true** | TNL detaches; next hit may re-create + hold |
| `RescopedWithOwner` | Second `ObjectInScope` after descope | **false** | Owner may pack if owner levers allow |

**One reghost per coid per connection session** — once `RescopedWithOwner`, no further descope for that coid (until map transfer clears tracking).

### 12.4 Code map

| Piece | Location |
| --- | --- |
| Lever | `GhostVehicle.EnableForeignReghostOwner` (default **false**) |
| Wire / env | `WireIsolationLevers` key `EnableForeignReghostOwner` / `AUTOCORE_WIRE_FOREIGN_REGHOST_OWNER` |
| Delay | `TNLConnection.ForeignReghostDelayMilliseconds` (default **500**) |
| Suppress owner on pack | `TNLConnection.ShouldSuppressForeignOwnerOnPack` |
| Descope skip | `TNLConnection.ShouldSkipForeignObjectInScopeForReghost` → `SectorMap.PerformScopeQuery` `continue` |
| Phase notes | `TNLConnection.NoteForeignVehicleGhostScoped` after successful `ObjectInScope` |
| Pack gate | `GhostVehicle.PackUpdate`: `packOwner && !suppressOwnerForReghost` |
| Clear on map transfer | `ClearGlobalVehicleCreateTracking` also clears `_foreignReghost` |
| Tests | `ForeignReghostOwnerTests` |

### 12.5 Live result (2026-07-11, conn 18423)

Config: reghost **on**, defer pose **off**, owner block **on**, initWheel **on**, minimal foreign **on**.

| Metric | Value |
| --- | --- |
| Client crash | **None** |
| `ForeignReghostDescope` | **13** |
| `owner=1/1` packs | 54 |
| `owner=0/1` (suppressed) | 3 (includes first-scope style initial ~936 bits) |
| Second-style initials | mostly **1036 bits**, `mask=0x100000002`, `owner=1/1` |

**Interpretation:** the same owner+pose initial that crashed on first sighting is **safe** after create + first no-owner ghost. Wheels observed on vehicles in-world.

### 12.6 What P2 does **not** give you (still missing)

P2 only fixes **owner-on without AV**. It does **not** turn on full foreign vehicle fidelity. With **minimal foreign profile still on** (current isolation default):

#### A. Delta mask filter (largest gameplay gap)

Foreign non-initial `updateMask` is forced to:

```text
PositionMask | WheelSetMask
```

So these **do not replicate on deltas** even after owner is present:

| Missing on deltas | Player-visible effect |
| --- | --- |
| **AI combat state** (`StateMask`) | Driver AI state not updated on client |
| **Health / max HP** | No live HP bars / damage / corpse from ghost |
| **Target** | Targeting / combat target not updated |
| **Weapons** (front/turret/rear/melee) | Loadout changes after spawn not ghosted |
| **Armor** | Armor hardpoint / resists not ghosted after create |
| **Ornament** | Cosmetic hardpoint not ghosted after create |
| **Skills** | Vehicle/owner skills not ghosted |
| **GM nibble** | Character-owner only; still filtered |
| **Attributes** | Character-owner only; still filtered |
| **Murderer** | Stub / not useful under filter |

CreateVehicle may still paint **initial** nest once; **ongoing** combat/loadout/AI is largely missing — matches “health, targeting, movement feel awful.”

#### B. Pose-only motion quality

Pose deltas carry position/rotation/velocity, but:

- No rich path-follow beyond initial path id block  
- Descope/rescope can interrupt continuity  
- Server AI / physics may not match retail density of pose updates  

#### C. Never wired (all profiles)

| Field | Status |
| --- | --- |
| Handling multipliers (speed, brakes, steering, AVD) | Always client defaults |
| Clan block | TODO / flag false |
| Pet CBID | TODO / flag false |
| Owner skills payload | Flag false |
| Full character-owner cosmetics on NPC drivers | Creature form only for NPCs |

#### D. P2-specific UX costs

| Cost | Effect |
| --- | --- |
| ~500ms+ first ghost without owner | No driver object / no AI latch yet |
| Forced descope | Possible brief pop / re-create |
| Possible second CreateVehicle | Extra bandwidth; IsItemLink=0 nest re-apply |

#### E. Explicitly out of scope for P2

- Client patch / fleet download  
- IsItemLink tooltip re-apply  
- Fixing retail missing null check on `+0x258`  

### 12.7 Recommended levers for “max owner fidelity” (P2)

```json
{
  "EnableMinimalForeignInitialProfile": true,
  "EnableMinimalForeignPathBlock": true,
  "EnableMinimalForeignTemplateSpawnBlock": true,
  "EnableMinimalForeignOwnerBlock": true,
  "EnableForeignReghostOwner": true,
  "EnableDeferredForeignPose": false,
  "EnableInitialHardpointPack": true,
  "EnableOwnerWire": true,
  "EnablePathWire": true,
  "EnableTemplateSpawnWire": true,
  "EnableAiStateWire": true
}
```

**Next work (not P2):** widen minimal foreign **delta** admission (e.g. StateMask → Health → Target → weapons), one family at a time, with owner already safe via P2.

### 12.8 Ops / debugging

| Signal | Meaning |
| --- | --- |
| `ForeignReghostDescope coid=…` | Deliberate skip of ObjectInScope |
| `owner=0/1` on GhostPack | Server has owner but pack suppressed (first phase) |
| `owner=1/1` + `initial=y` + `mask` includes `0x2` | Second initial with owner+pose |
| AV `0x004F5566` again | Reghost failed or owner+pose raced first sighting — check descope count and phase |

Map transfer / `ClearGlobalVehicleCreateTracking` resets reghost phases so the cycle can run again on re-entry.

- **Ship P1** as the default owner-on configuration.  
- Keep **P2** as an optional lever / defense in depth (or for experiments that require owner+pose on a single initial).  
- Do **not** enable naive owner+pose on first foreign initial.  
- P3/P4 only if a new race appears that neither P1 nor P2 covers.

### 11.5 Empty nest semantics (game vs ghost)

| Source | Wheel nest CBID | Client effect |
| --- | --- | --- |
| `CreateWheelSetPacket.WriteEmptyPacket` | **−1** | Empty nest marker |
| Ghost `FUN_005F5AD0` zero-fill | **0** | `GiveItemByCbid(0)` fails → log, no SetWheelset |
| Ghost initial hardpoint with real CBID | **server CBID** | Can succeed on materialize |
| Good game CreateVehicle nest | **real CBID** | SetWheelset on game apply |

PathA `wheel_cbid=0` with nest opcode `0x201B` is the **ghost shell**, not the empty CreateVehicle −1 marker — strong signal the hit was ghost materialize (or a buffer that never got hardpoint seed), not necessarily a missing server CreateWheelSet body.

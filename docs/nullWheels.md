# Null Wheelset Client-Crash Handoff

## Purpose

This document is the complete handoff for the Auto Assault retail-client crash caused by foreign NPC vehicle replication. Read it before changing `GhostVehicle`, NPC spawning, scope/ghosting, or the wire-isolation settings.

The goal is to restore full foreign NPC vehicle ghost replication without reproducing the retail client's access violation at `0x004F5566`.

## Current status

The server is currently configured to use a stable, deliberately limited foreign-NPC vehicle profile:

- foreign `CreateVehicle` packets: enabled;
- foreign TNL ghosting: enabled;
- pose/position ghost updates: enabled;
- initial path block: enabled;
- initial template and spawn-owner blocks: enabled;
- initial owner/driver block: **still unsafe** (AV returned after bit-align + nested MapNpc TFID); keep **off** until activation chain is fixed;
- GM, AI state, health, target, armor, equipment, skills, and other non-pose deltas: suppressed by the minimal profile.

Known-safe settings:

```json
{
  "ScopeGlobalVehicleGhost": true,
  "EnableMinimalForeignInitialProfile": true,
  "EnableMinimalForeignPathBlock": true,
  "EnableMinimalForeignTemplateSpawnBlock": true,
  "EnableMinimalForeignOwnerBlock": false
}
```

### Live results 2026-07-11

| Run | Profile | Result |
| --- | --- | --- |
| A | Bit-aligned owner (no false SpawnOwner bit) | **AV `0x004F5566`** @ 09:01:37 after `owner=1/1` ghosts |
| B | Same + nested equipment `MapNpcIdentity` TFIDs | **Same AV** @ 09:09:59 (new Game.dll 09:08:25) |
| C | Owner off (path/tmpl/spawn/pose) | Stable historically |

**Conclusion:** owner-on is not a packing-width bug alone. Ghidra shows **owner presence is the Havok activation gate** on the ghost pose path; CreateVehicle must leave `vehicle+0x258` non-null *before* that gate fires. Nested TFID fix is still correct policy but insufficient by itself.

### Crash stack (both A and B)

```text
FUN_004F5560          vehicle+0x258 → wheelset+0xB0   (no null check)
  ← Vehicle_buildHavokVehicleFramework  0x005FD390
    ← Vehicle_createVehicleAction       0x004FB660   (ret 0x004FB773)
      ← FUN_00503F30                    enter-world / activate
        ← Vehicle_setDrivingInputs      0x00504C70   (from ghost pose unpack)
```

## Client crash signature

Retail client version:

```text
0.0.14.117.2007.2.1.11
```

Repeated dump signature:

```text
Access violation c0000005 at autoassault!0x004F5566
```

The function is known in client analysis as `FUN_004F5560` (also called the wheel-count/render path). It dereferences a vehicle's wheelset pointer at offset `+0x258` without a null check. The client dump identifies the local player vehicle (`coid=18424`, client vehicle `19656`) at crash time, but the trigger is foreign NPC vehicle state arriving on the same client.

Other relevant client locations:

| Address | Meaning |
| --- | --- |
| `0x004F5560` | Wheel-count getter: `*(vehicle+0x258)+0xB0`. AV site `0x004F5566`. |
| `0x004FEA90` | Set wheelset → writes `vehicle+0x258`. |
| `0x00504480` | Nested equip from CreateVehicle buffer (wheel @ `+0x45C` / TFID `@+0x4E8`). |
| `0x00505270` | `Vehicle_applyCreatePacket` (vtable `+0xC4`). |
| `0x0080A4B0` | Sector recv `CreateVehicle` `0x201D`. |
| `0x00812630` | Ghost-path create vehicle (embedded create blob). |
| `0x005F7720` | `VehicleNet_UnpackGhostVehicle`. |
| `0x00504C70` | `Vehicle_setDrivingInputs` (ghost pose → optional activate). |
| `0x00503F30` | Enter-world activate → `createVehicleAction` (**no wheelset check**). |
| `0x00501420` | `Vehicle_TryActivatePhysics` (**does** check `+0x258`; crash stack does **not** use this). |
| `0x0051A170` | `CVOGReaction_GiveItemByCbid`. |
| `0x005F5AD0` | Alloc/init CreateVehicle message buffer `0xD78` + nested owner forms. |

---

## Client chain map (Ghidra, 2026-07-11)

This is the authoritative path from network message to the AV. Prefer this over earlier one-line hypotheses.

### Path A — Sector `CreateVehicle` (`0x201D`)

```text
FUN_0080A4B0  Recv CreateVehicle
  │
  ├─ CVOGReaction_GiveItemByCbid(packet+4)     // vehicle CBID
  │     fails → log "allocatenewobjectfromcbid failed" and return
  │
  ├─ vtable+0x08  bind CBID / map context
  ├─ vtable+0x1D4 get vehicle object
  │
  └─ vtable+0xC4  Vehicle_applyCreatePacket (0x00505270)
        │
        ├─ vehicle flags from packet:
        │     +0x151 IsInventory → vehicle gate field (equip skip if set)
        │     +0x152 IsActive    → stored on vehicle (NOT the sole activate gate)
        │     active gate for post-create: (IsInventory || IsInInventory@+0xA2)
        │
        ├─ FUN_005C9120  base simple-object apply from buffer
        │
        ├─ FUN_00504480  nested equip (mode from apply args)
        │     if vehicle inventory-gate (+0x2AC) == 0:
        │       1) ResolveObjectTarget(nested wheel TFID @ packet+0x4E8)
        │          hit  → use existing object (skip GiveItem)
        │          miss → GiveItemByCbid(wheel CBID @ packet+0x45C)
        │       2) apply nested create at +0x458
        │       3) FUN_004FEA90 → vehicle+0x258 = wheelset*
        │          (requires type +0x38 == 0x10; else "unhappy type")
        │       also armor / weapons / powerplant / ornament...
        │     else: entire wheel equip skipped
        │
        ├─ if TemplateId != -1: resolve spawn-point / template link
        │     (FUN_004BB340 / CVOGSpawnPoint cast) — separate from nested equip
        │
        └─ if not inventory-path:
              FUN_004FEDC0  or  FUN_004C0140
                world/physics bind paths that establish vehicle+0x08
                (required later by setDrivingInputs)

  then FUN_0080A4B0 continues:
  ├─ vtable+0x218  map bind
  └─ vtable+0x104  flags
```

**Server implications for Path A**

| Server field / policy | Client effect |
| --- | --- |
| Nested `CreateWheelSet` CBID | `GiveItemByCbid` materializes wheelset object |
| Nested wheelset TFID (`ObjectId`) | Looked up **before** GiveItem; wrong/local hit can skip GiveItem |
| Empty CreateWheelSet body size | Must be 340 (212 simple + 128); short body desyncs rest of packet |
| `IsInventory` / inventory flags | Must stay false for field NPCs or equip is skipped |
| `IsActive` | Stored; does **not** alone call Havok |
| `TemplateId` | Spawns template linkage; does **not** replace nested equip for sector 0x201D |

### Path B — Ghost `VehicleNet_UnpackGhostVehicle` (`0x005F7720`)

#### B1. Initial update

```text
VehicleNet_UnpackGhostVehicle  (initial)
  │
  ├─ FUN_005F5AD0(0,0)  alloc CreateVehicle-sized ghost buffer (0xD78)
  ├─ colors, IsActive bit (buffer+0x152 class fields), trim, optional multipliers
  ├─ optional path / template / spawn-owner blocks
  ├─ CurrentOwner-present?
  │     yes → read COID/global/CBID + form flag
  │           form false → FUN_005F5AD0(1,1) creature owner (type 0x2013)
  │           form true  → FUN_005F5AD0(1,0) character owner (type 0x2015)
  │           creature form options: enh, on-use×2, summoner,
  │             DoesntCountAsSummon, level[8], elite
  │             *** no SpawnOwner slot in this form ***
  │           owner object lands on vehicle owner slot (+0xB0 path)
  │
  ├─ resolve local vehicle object (ghost parent → local_12c)
  │
  ├─ hardpoint / equipment mask flags (minimal profile: off on initial)
  │     if wheel hardpoint present: may SetWheelset on local_12c+0x258
  │
  └─ later masks (pose / health / …)
```

Creature-owner form contract (verified assembly): see section “Ghidra finding: vehicle creature-owner form has NO SpawnOwner slot” below.

#### B2. Pose / PositionMask → driving inputs → **unsafe activate**

```text
PositionMask (and related) inside UnpackGhostVehicle
  │
  └─ Vehicle_setDrivingInputs  (0x00504C70)   // xref from 0x005F99AA
        │
        ├─ requires vehicle+0x08 != 0   // physics/world binding from Path A
        │     else: return (no activate)
        │
        ├─ write throttle/steer/sharp-turn (+0x614/+0x618/+0x61C)
        ├─ FUN_004FBC10
        │
        ├─ if param_9==0 AND vehicle+0x1A0==0 (no VehicleAction yet):
        │     owner = vehicle owner pointer (+0xB0 via object layout)
        │     if owner != null
        │        AND owner identity matches vehicle identity check:
        │           FUN_00503F30()          // *** NO wheelset null check ***
        │             └─ if vehicle+0x1A0==0:
        │                  Vehicle_createVehicleAction
        │                    └─ if vehicle+0x08 != 0:
        │                         buildHavokVehicleFramework
        │                           └─ FUN_004F5560()  // CRASH if +0x258==0
        │
        └─ FUN_0053EEC0  apply position/transform (always if +0x08 set)
```

**Contrast — safe path (not on crash stack):**

```text
Vehicle_TryActivatePhysics  0x00501420
  if vehicle+0x258 == 0: log "VOG_DEBUG_STOP"; return
  else: createVehicleAction …
```

Crash dumps always show `createVehicleAction` via `FUN_00503F30`, never `TryActivatePhysics`.

### Why owner-off is stable and owner-on is not

| State | Owner slot | setDrivingInputs activate? | Havok build? | Needs +0x258 |
| --- | --- | --- | --- | --- |
| Owner lever **off** | null | **No** (owner null short-circuits) | No | No (pose only) |
| Owner lever **on** | embedded owner present | **Yes** once `+0x08` bound and action empty | Yes | **Yes — AV if null** |

So the owner block is not merely “extra bits on the wire.” On the retail client it is the **condition that arms Havok construction on the next pose ghost**, without checking the wheelset.

CreateVehicle (Path A) must have already left `vehicle+0x258 != 0` before Path B pose deltas, **or** owner must not be delivered until that is true.

### Ordering on Ark Bay (from WireDiag)

```text
t0    CreateVehicle foreign ×N     Path A — equip + world bind
t0+ε  (map reactions, etc.)
t1    GhostVehicle initial owner=1 Path B1 — install owner
t1+δ  GhostVehicle pose mask=0x2   Path B2 — setDrivingInputs → FUN_00503F30 → AV
```

Typical δ ≈ 0.1–3s. Crash correlates with first wave of `owner=1/1` initials + pose, not with CreateVehicle alone.

### Still open after Ghidra chain map

| Open question | Why it matters | How to prove |
| --- | --- | --- |
| Does Path A leave `+0x258` null for Ark Bay foreign vehicles even with server wheelset CBID? | If yes, fix CreateVehicle equip materialization | CDB traces: EquipFromCreate, SetWheelset, then +0x258 before first ghost |
| Does GiveItemByCbid(wheelset CBID) return null (type table / clonebase)? | Type at def+0x38 must be `0x10` for wheelset path | Trace `0x0051A170` return; client log `allocatenewobjectfromcbid failed` |
| TFID hit on high MapNpc id still wrong object? | Unlikely but possible if client table polluted | Compare resolve hit vs GiveItem path |
| Race: ghost initial before CreateVehicle apply finishes? | Owner+pose could activate mid-equip | WireDiag order + client attach ordering; server guarantee Create before ObjectInScope |
| Does dump’s local vehicle (18424) mean wrong-object activate? | Would reframe the bug | Confirm `this` at 0x004FB660 is foreign vs local |

### Server work already landed (necessary but not sufficient)

| Change | Addresses |
| --- | --- |
| Equip clonebase `DefaultWheelset` at spawn | Nested CreateWheelSet present |
| Nested equipment TFID = `MapNpcIdentity` (global, ≥`0x50000000`) | Avoid local TFID collision skipping GiveItem |
| `CreateWheelSetPacket.WriteEmptyPacket` +128 | Nested empty size 340 |
| Creature-owner form **without** SpawnOwner bit | Ghost owner bit alignment |

### Exact next fix targets (ordered)

1. **Prove Path A equip outcome** with CDB (SetWheelset / `+0x258`) on one Ark Bay foreign CreateVehicle under current binary.
2. If `+0x258` still null: fix GiveItem/type/nested payload field-level (not more owner packing).
3. If `+0x258` is set at CreateVehicle but null at activate: find clear/overwrite between Path A and B2.
4. If `+0x258` is set at activate and still AV: wrong `this` / wrong vehicle — re-examine dump attribution.
5. Optional safety (not permanent fix): withhold foreign owner until equip proven; or never call activate without wheelset (client cannot change — server must not arm owner early).

## Confirmed root cause chain (historical)

The problem is not one single defect. There are two confirmed, closely related failure paths.

### 1. NPC patrol movement exposed missing NPC wheelsets

The historical bisect found the first bad commit exactly:

| Revision | Ark Bay result |
| --- | --- |
| `64dab44` | no crash |
| `b6bb789` | no crash |
| `9152a31` | crash |

`9152a31` added the idle-patrol NPC sector tick:

```text
NpcTicker.Tick
  -> NpcTicker.ApplyMove(vehicle, result)
  -> Vehicle.ApplyServerMove(...)
  -> Ghost.SetMaskBits(GhostObject.PositionMask)
```

`Vehicle.ApplyServerMove` therefore creates the first regular pose ghost updates for patrolling NPC vehicles. Prior to that commit, the broken spawned-NPC wheelset state was not consistently driven into the client render/update path.

At the same time, raw and template NPC vehicle spawns did not equip the clonebase `DefaultWheelset`. `Vehicle.WriteToPacket(CreateVehiclePacket)` therefore wrote an empty nested `CreateWheelSet` payload. Once the patrol tick made the client actively process/render those foreign vehicles, it dereferenced a null wheelset.

### 2. The initial owner/driver block independently reintroduces the crash

After fixing NPC default wheelset creation and adding the minimal foreign profile, the following profile was stable:

```text
required initial body + pose + path + template/spawn
```

Enabling only the initial foreign `CurrentOwner`/driver block reproduced the same `0x004F5566` AV. The owner experiment retained pose-only deltas, so GM and AI-state writes were still suppressed. This makes the owner construction branch itself the active blocker, not a later GM/AI delta.

The creature-owner suffix was incomplete in the server payload. The retail decoder contract and the safe correction are documented below; do not retry the old payload unchanged.

## Ghidra finding: vehicle creature-owner form has NO SpawnOwner slot

Verified from retail `VehicleNet_UnpackGhostVehicle` (`0x005F7720`) disassembly (not from `GhostCreature`).

`VehicleNet_UnpackGhostVehicle` reads an owner block as follows:

```text
CurrentOwner-present flag
  owner COID              64 bits
  owner global flag        1 bit
  owner CBID              20 bits
  owner-form flag          1 bit
  form-specific payload
```

The client allocates the form-specific embedded owner object with `FUN_005F5AD0`; it does **not** resolve the owner through the normal object table. Form flag false → creature path at `0x005F7DCA` (`FUN_005F5AD0(this, 1, 1)`). Its retail read sequence before elite is:

```text
Enhancement-present        [20 bits when true]   → owner+0xD8
On-use-trigger-present     [20 bits when true]   → owner+0x128
On-use-reaction-present    [20 bits when true]   → owner+0x12C
Summoner-present           [64-bit COID + global flag when true] → owner+0xE0
DoesntCountAsSummon        1 bit (stored inverted at owner+0xF0)
Level                      8 bits                → owner+0x114
IsElite                    1 bit
```

**There is no SpawnOwner presence bit in this form.** Addresses previously misread as SpawnOwner:

| Address | Actual meaning |
| --- | --- |
| `0x005F8115` | Bounds check entry for DoesntCountAsSummon |
| `0x005F812C`–`0x005F8164` | OnUseTrigger true-path (`PUSH 0x14` → `+0x128`) |
| `0x005F820C`–`0x005F822F` | DoesntCountAsSummon flag consume |
| `0x005F8239` | Level 8-bit read |
| `0x005F825A` | Elite flag read |

`GhostCreature` **does** pack `SpawnOwner` (`stream.Write(creature.SpawnOwner)`, full 64-bit). That is a different packet. Do not copy it into the vehicle-owner form.

Vehicle-level `CoidSpawnOwner` (the initial template/spawn block, 20-bit) is also a different field and is already gated by `EnableMinimalForeignTemplateSpawnBlock`.

### Incorrect “fix” that caused disconnect/freeze

```csharp
// WRONG for vehicle creature-owner form — do not re-add
stream.WriteFlag(false); // SpawnOwner == -1
```

That extra bit made the client treat server `DoesntCountAsSummon` as part of the level field (unit test: level `0x6d` → `0xda`), then desynced later hardpoint/mask flags → Invalid Packet / disconnect / freeze on Ark Bay load.

### Correct server packing (current)

```csharp
stream.WriteFlag(false); // EnhancementID == -1
stream.WriteFlag(false); // CoidOnUseTrigger == -1
stream.WriteFlag(false); // CoidOnUseReaction == -1
stream.WriteFlag(false); // CreatureSummoner coid == -1
stream.WriteFlag(false); // DoesntCountAsSummon
stream.WriteBits(8, level);
stream.WriteFlag(false); // IsElite
```

Regression: `PackInitial_CreatureOwnerPacked_DoesNotWriteSpawnOwnerSlotBeforeDoesntCountAsSummon`. Focused ghost-wire suite: 98/98. Remaining gate: live Ark Bay with owner lever on (original `0x004F5566` may still fire even with bit-aligned owner).

## Evidence and diagnostics

### Server wire log

`WireDiag` records every relevant outbound game packet and `GhostVehicle` pack when enabled.

Stable profile examples:

```text
GhostVehicle ... bits=726 mask=0x2 initial=y
path=0/1 owner=0/1 tmpl=0/1 spawn=0/1 clientOwner=0 equip=0
profile=minimal sourceMask=FFFFFFFFFFFFFFFF
```

With path enabled:

```text
GhostVehicle ... bits=810 mask=0x2 initial=y
path=1/1 owner=0/1 tmpl=0/1 spawn=0/1 clientOwner=0 equip=0
profile=minimal sourceMask=FFFFFFFFFFFFFFFF
```

With template/spawn enabled:

```text
GhostVehicle ... bits=850 mask=0x2 initial=y
path=1/1 owner=0/1 tmpl=1/1 spawn=1/1 clientOwner=0 equip=0
profile=minimal sourceMask=FFFFFFFFFFFFFFFF
```

The profile constrains later foreign deltas to `PositionMask` (`0x2`) even when the source dirty mask contains unrelated bits (for example `0x82`). This is intentional safety behavior.

### Client debugger capture

The `CreateVehicle` debugger tooling is documented in `docs/CREATEVEHICLE_DEBUGGER.md`. Existing captures are in `docs/debugger-hits/`.

Prior capture observations:

1. The client reached nested `CreateVehicle` wheelset handling.
2. At `EquipFromCreate` and `SetWheelset`, the target vehicle's `+0x258` pointer remained zero for the old NPC packets.
3. The subsequent wheel-count/render path crashed when that pointer was read.

The CDB attach can disrupt DirectInput and freeze the client. If using it:

- attach only to a healthy client;
- use `scripts\cv-debug.cmd arm --background --no-wait`;
- if the client becomes unresponsive, do **not** force-kill CDB; restart the client and then disarm;
- always disarm after a capture.

## Current implementation

### NPC default wheelset repair

`SpawnPoint` now equips `CloneBaseVehicle.VehicleSpecific.DefaultWheelset` for both raw-CBID and template vehicle spawns before `CreateVehicle` serialization.

Relevant code:

- `src/AutoCore.Game/Entities/SpawnPoint.cs`
- `src/AutoCore.Game/Entities/Vehicle.cs`

Regression test:

```text
Spawn_RawVehicleWithDefaultWheelset_EquipsWheelsetForCreateVehicle
```

The fixture supports `defaultWheelsetCbid` through:

```text
AssetManagerTestHelper.RegisterVehicleCloneBase(..., defaultWheelsetCbid: ...)
```

### Minimal foreign ghost profile

`GhostVehicle.EnableMinimalForeignInitialProfile` applies to foreign global vehicles only. Despite its legacy name, it restricts both initial and later foreign vehicle ghost updates:

- effective update mask is `PositionMask`;
- all hardpoint/armor/health/target/skill/AI/GM fields are withheld;
- the required initial body is still emitted;
- separately gated initial blocks can be enabled one at a time.

Available gates:

| Lever | Default | Meaning |
| --- | --- | --- |
| `ScopeGlobalVehicleGhost` | false in code default; true in current test config | Allows foreign vehicle TNL ghost registration. |
| `EnableMinimalForeignInitialProfile` | false in code default | Enables pose-only foreign profile. |
| `EnableMinimalForeignPathBlock` | false | Admits the path block on initial. |
| `EnableMinimalForeignTemplateSpawnBlock` | false | Admits template and spawn-owner blocks on initial. |
| `EnableMinimalForeignOwnerBlock` | false | Admits owner/driver block on initial. **Historically crash/desync trigger; under re-validation with bit-aligned creature-owner form.** |

All are represented in `WireIsolationLevers`, so they can be supplied from JSON, environment variables, or the `sector.wire` command.

### Local player vehicle protection

The local player's vehicle is created through `CreateVehicleExtended`; it must not also receive a `GhostVehicle` initial update. The code excludes it from direct scope registration and from normal map scope selection.

Relevant files:

- `src/AutoCore.Game/TNL/TNLConnection.Sector.cs`
- `src/AutoCore.Game/Map/SectorMap.cs`

## Tests

The work is test-driven. The key test groups are:

| Area | Files |
| --- | --- |
| Ghost bitstream contracts | `GhostVehicleWireTests.cs`, `GhostVehicleWireRegressionTests.cs` |
| Foreign vehicle create/scope ordering | `GlobalVehicleScopeTests.cs` |
| Map-transfer/local-vehicle ghost safety | `MapTransferGhostingTests.cs` |
| Lever parsing and defaults | `WireIsolationLeversTests.cs` |
| NPC default wheelset spawn | `SpawnPointTemplateSpawnTests.cs` |

Useful focused command:

```powershell
dotnet test src\AutoCore.Game.Tests\AutoCore.Game.Tests.csproj --no-restore --filter "FullyQualifiedName~GhostVehicleWireRegressionTests|FullyQualifiedName~GlobalVehicleScopeTests|FullyQualifiedName~SpawnPointTemplateSpawnTests|FullyQualifiedName~WireIsolationLeversTests"
```

The full suite previously had two unrelated `ReactionBoostTests` failures. Do not attribute those to this work without fresh evidence.

## Historical worktrees

Ten detached worktrees were created under `C:\Users\josh\Documents\GitHub\` for historical validation. The decisive first three are:

```text
01-64dab44-baseline       no crash
02-b6bb789-setpath        no crash
03-9152a31-patrol-tick    crash (first bad)
```

The worktrees use a junction to the main checkout's `lib\TNL.NET` dependency because that local project is not materialized by the historical commits themselves.

When testing historical worktrees:

1. Stop the currently running launcher first; all revisions use the same ports.
2. Build and run only one worktree at a time.
3. Use the same account, character, client build, and Ark Bay entry path.
4. Record crash/no-crash before moving to a new revision.

The current recovery work is committed as:

```text
22632b7 fix: stage safe NPC vehicle ghost recovery
```

## Exact next work

Do **not** enable more field families until owner-on is stable.

1. ~~Creature-owner form (no SpawnOwner)~~ — done; live still AVs.
2. ~~Nested MapNpc TFID for equipment~~ — landed; live still AVs.
3. ~~Ghidra full chain Path A + Path B~~ — done (this document).
4. **Path A live capture (non-freezing MinHook injector — not cdb):**
   ```bat
   powershell -File tools\PathAHook\build.ps1
   scripts\path-a-debug.cmd arm
   :: login / Ark Bay
   scripts\path-a-debug.cmd check
   ```
   Artifacts: `%TEMP%\AutoCorePathA\hits.jsonl` and `docs/debugger-hits/path-a-hits.jsonl`.
   Events: `RecvCreateVehicle`, `EquipFromCreate`, `SetWheelset`, `CreateVehicleAction_*`, `ActivateEnterWorld_*` with `v258_before` / `v258_after`.
5. Branch on hits: equip fail (`v258_after` null) vs clear-before-activate vs wrong object.
6. Only after owner-on is stable: AI / health / equipment families.

### Path A debugger (2026-07-11)

| Item | Detail |
| --- | --- |
| Why not cdb | Prior cdb attach freezes DirectInput / client; hard breaks are unsafe for this game |
| Approach | Inject `PathAHook.dll` (MinHook, same pattern as DevTool `SpeedHook`) |
| Injector | `tools/PathADebug` (C#, reuses `AutoLoginInjector` platform) |
| Wrapper | `scripts\path-a-debug.cmd` |
| Log | JSONL, append-only, no process suspend |

Do **not** use `scripts\cv-debug.cmd` / cdb for Path A unless you accept client freeze risk.

### Path A live capture result (2026-07-11, owner-off session)

Source: `docs/debugger-hits/path-a-hits.jsonl` (24 lines after setup).

| Event | Count |
| --- | --- |
| EquipFromCreate | 9 |
| SetWheelset | 5 |
| CreateVehicleAction enter/exit | 4 each |
| `v258_after` still null | **5** EquipFromCreate |

**Healthy Path A (majority of NPCs / player):**

```text
SetWheelset → v258 null → non-null (e.g. 3DB5A550)
EquipFromCreate wheel_cbid=40 or 52, opcode=0x201B (8219)
CreateVehicleAction with v258 already non-null → Havok OK
```

**Broken Path A (5 vehicles):**

```text
EquipFromCreate wheel_cbid=0, opcode=0x201B, mode=2
NO SetWheelset
v258_before = v258_after = null
NO CreateVehicleAction in the capture window
```

Same packet buffer `3E2BF0D8` appears on multiple null-equip hits (reused create blob with nested wheel CBID **0**, not empty’s **−1**).

**Implication:** Path A can and does equip correctly when nested CBID is 40/52. The AV-ready vehicles are those whose CreateVehicle nested wheelset CBID is **0**, so the client never calls SetWheelset. Owner-on later activates Havok on those null-`+0x258` objects via `setDrivingInputs` → `FUN_00503F30`.

**Next code target:** find which server CreateVehicle emissions write nested wheel CBID `0` (not −1 empty, not DefaultWheelset). Log every foreign create: vehicle CBID, `WheelSet?.CBID`, TemplateId, empty-vs-full nested.

### Owner-on retest 2026-07-11 10:02 (FAILED — AV again)

**Server:** all foreign CreateVehicle `wheelOk=1` (cbid 31/52/40). Ghosts `owner=1/1 bits=950`.

**Path A (smoking gun):**

```text
EquipFromCreate  veh=31271110  wheel_cbid=0  v258 stays null   (no SetWheelset)
ActivateEnterWorld_enter  veh=31271110  v258=null
CreateVehicleAction_enter veh=31271110  v258=null   → AV 0x004F5566
```

Other NPCs in the same session equipped correctly (cbid 31/52 → SetWheelset → non-null v258 → safe CreateVehicleAction).

**Implication:** crash is no longer “server never sends wheelsets.” At least one client equip run still sees nested **CBID 0** and then owner arms Havok on that same vehicle. Either a create path we don’t WireDiag, a ghost/create re-apply with a bad buffer, or nested layout still wrong for one emitter. Owner restored **off**.

### Ghidra 2026-07-11 — client ghost buffer leaves wheel CBID = 0

`FUN_005F5AD0` (alloc CreateVehicle-sized ghost buffer `0xD78`):

- zero-fills the whole buffer first;
- sets nested wheel **opcode** at `+0x458` = `0x201B`;
- sets armor/powerplant/ornament CBIDs to **`0xFFFFFFFF` (−1)**;
- **does not** write wheel CBID at `+0x45C` → remains **0** from zero-fill.

Server empty nested path wires **−1** at `0x45C` (layout unit tests lock this). Path A `wheel_cbid=0` therefore matches the **client ghost zero-fill default**, not the server empty contract. Same packet pointer reuse (`3E2BF0D8`) on multiple null equips fits a recycled client buffer.

Server CreateVehicle body size and offsets match retail (`0xD78`, wheel `@0x458/0x45C`). Owner-on must not arm until Path A left `+0x258` non-null for that vehicle.

### Recovery 2026-07-11 — ghost delta WheelSet hardpoint

Live owner-off capture: **every** foreign CreateVehicle had `wheelOk=1` / `wireScan` 31|52, yet Path A still had ~half EquipFromCreate with `wheel_cbid=0` + `nest_hex=1B20…00` (opcode only). No `ActivateEnterWorld` (owner off) → no AV.

Minimal foreign profile previously forced **pose-only masks on all packs**, which **dropped dirty WheelSet forever** after initial. Server now packs `PositionMask|WheelSetMask` on foreign deltas and keeps WheelSet dirty across initial.

**Ghidra follow-up (same day): hardpoint does NOT SetWheelset for foreign ghosts.**

| Address | Name | Finding |
| --- | --- | --- |
| `0x004FEA90` | SetWheelset | Writes `vehicle+0x258` (`this+600`). Requires item type `def+0x38 == 0x10` (wheelset); else logs "unhappy type" but still assigns pointer. |
| `0x00504480` | EquipFromCreate | **Only** Path A nested equip from CreateVehicle buffer; `GiveItemByCbid(packet+0x45C)`; **no** fallback to `IDDefaultWheelset`. |
| `0x005F7720` | UnpackGhostVehicle | Wheel hardpoint (opcode `0x201B` mini-blob): on **delta** calls `VehicleNet_PostCorrectionEvent` only; on **initial** only fills ghost create buffer. **No call to SetWheelset.** |
| `0x005F7360` | VehicleNet_PostCorrectionEvent | Builds local prediction/correction messages `0x203C` / `0x203E` (equip request style). Not a foreign SetWheelset. |
| `0x00503780` | (template vehicle equip) | **Does** default wheels: `operator_new` wheelset, bind CBID from **vehicle clonebase vehicle-specific +0x6F4** (`IDDefaultWheelset`), then `SetWheelset`. Callers: `CVOGSpawnPoint_CreateTemplateVehicle`, `FUN_0058bf50`. |
| `0x005252f0` | (vehicle switch / possess) | If `vehicle+0x258 == null`, creates wheelset from same **+0x6F4** DefaultWheelset and `SetWheelset`. |
| `0x00501420` | TryActivatePhysics | **Safe:** if `+0x258 == null` → log `VOG_DEBUG_STOP`, return. Crash path does **not** use this. |
| `0x00503F30` | ActivateEnterWorld | **Unsafe:** no wheel null check → `createVehicleAction` → Havok → AV. |
| `0x005F5AD0` | ghost CreateVehicle buffer | Opcode `0x201B` at `+0x458`; wheel CBID at `+0x45C` left **0** (zero-fill); other empty CBIDs set to `−1`. |
| `0x0051A170` | GiveItemByCbid | Type `0x10` → wheelset ctor `FUN_005A84F0` (0x2F0). CBID `0` → def lookup fails → null. |

**Conclusion — default wheels hypothesis:**

- Confirmed: client **does** have a DefaultWheelset field (`IDDefaultWheelset` / clonebase vehicle-specific **+0x6F4**).
- Used only on **template spawn** and **vehicle switch when wheel pointer is already null**.
- **Not** used when sector `CreateVehicle` nested equip fails (CBID 0 / GiveItem fail).
- Ghost equipment hardpoints **do not** recover foreign `+0x258`. Server packing `WheelSetMask` is harmless but **not** a client recovery for NPCs.

### Root cause (Ghidra 2026-07-11) — ghost create before sector CreateVehicle

Two client CreateVehicle entry points:

| Entry | Address | Packet source |
| --- | --- | --- |
| Sector queue `0x201D` | `FUN_0080A4B0` | Raw network message (EBX); full nested wheel from server wire |
| Sector dispatch `0x201D`/`0x201E` | `Client_PacketDispatch` → `FUN_00812630` | Packet body (ECX); full wire when from game queue |
| **Ghost missing-object create** | `FUN_008078b0` → `FUN_00812630` | **Ghost create blob** at ghost`+0x5C` (`FUN_005F5AD0` + partial `FUN_005B1360`) |

Ghost blob fill (`FUN_005B1360`) writes only TFID / root CBID / HP fields — **not** nest at `+0x45C` (stays **0**).

**Client tick order in `FUN_008078b0`:**
1. Iterate pending ghosts → if object missing and create blob present → `FUN_00812630` (create + equip from blob)
2. Drain game packet queue (`Client_PacketDispatch`) → sector CreateVehicle
3. Drain second queue (includes `FUN_0080A4B0` for `0x201D`)

So same-tick “CreateVehicle then ObjectInScope” on the server still loses: client may **ghost-create first** with wheel CBID 0. When the full sector create arrives, `FUN_00812630` resolves TFID already present and **skips re-apply** unless `packet+0xA1 != 0` (field NPCs wire `0xA1=0`).

Path A `nest_hex=1B20…00` + good server `wireScan` is this race, not a bad nest serializer.

**Server fix (layered):**

1. **Create-only query:** never `ObjectInScope` on the same scope pass as first `CreateVehicle`.
2. **Hold:** require `ForeignGhostScopeHoldQueries` further scope passes (default **2**) and `ForeignGhostScopeHoldMilliseconds` wall time (default **500**) before ghosting.
3. **Optional re-apply:** `ForceForeignCreateReapply` sets `IsItemLink` on foreign create (client **packet+0xA1**). If the ghost still wins the race, `FUN_00812630` re-applies the full create. Default **off** (re-apply also runs inventory/UI helpers after create).

### WireDiag CreateVehicle wheelset detail (landed)

Every `CreateVehicle` / `CreateVehicleExtended` send (when WireDiag is on) now logs:

```text
CreateVehicle wire coid=… bytes=… vehicleCbid=… wheelsetCbid=… nested=full|empty wheelOk=0|1 templateId=… isActive=…
```

Also on WireDiag lines as `detail=…`. Grep live logs for `wheelOk=0` or `wheelsetCbid=0`.

### WriteToPacket safety net (landed)

`Vehicle.EnsureDefaultWheelSetForWire()` equips clonebase `DefaultWheelset` (MapNpc TFID) if missing before serialize. Field `IsActive` is forced **false** when still no valid wheelset (avoids arming Havok with null `+0x258`).

## Safety checklist

Before deploying any experiment:

- Confirm the new field family is disabled by default.
- Add a failing bitstream test before the production change.
- Run the focused tests and record their result.
- Build the launcher only after stopping the prior launcher.
- Verify the launcher output contains the newly built `AutoCore.Game.dll`.
- Turn on exactly one lever in `wire-isolation.levers.json`.
- Reproduce Ark Bay entry and inspect `server-live.log`.
- On any AV or `Invalid Packet`, immediately restore the last known-safe JSON configuration and restart the server.

## Known-safe configuration summary

Last confirmed stable state (before owner re-validation):

```text
Foreign CreateVehicle:             on
Foreign GhostVehicle registration: on
Minimal foreign profile:           on
Pose deltas:                       on
Initial path:                      on
Initial template/spawn:            on
Initial owner:                     off
GM/AI/health/equipment/etc.:       off via pose-only mask restriction
```

Experiments 2026-07-11:

- Bit-aligned owner → AV.
- Nested MapNpc wheelset TFID + owner → **same AV**.
- Ghidra: owner presence on ghost arms `Vehicle_setDrivingInputs` → `FUN_00503F30` → Havok without wheelset check.

**Production default until Path A equip is proven non-null:** `EnableMinimalForeignOwnerBlock = false`.

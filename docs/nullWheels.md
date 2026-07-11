# Null Wheelset Client-Crash Handoff

## Purpose

This document is the complete handoff for the Auto Assault retail-client crash caused by foreign NPC vehicle replication. Read it before changing `GhostVehicle`, NPC spawning, scope/ghosting, or the wire-isolation settings.

The goal is to restore full foreign NPC vehicle ghost replication without reproducing the retail client's access violation at `0x004F5566`.

## Current status

### Root cause (stable)

Client AV `c0000005 @ 0x004F5566` when `ActivateEnterWorld` / `Vehicle_createVehicleAction` runs with `vehicle+0x258 == 0` (null wheelset). Usual trigger: ghost **owner** present **and** pose path calls `Vehicle_setDrivingInputs` while action empty, before create nest has SetWheelset.

Naive owner-on + pose on the **first** foreign initial still crashes (2026-07-11 ~13:16, `mask=0x100000002 owner=1/1`).

### Validated server fixes (2026-07-11 campaign)

| Priority | Lever | Live result | Commit / code |
| --- | --- | --- | --- |
| **P1** | `EnableDeferredForeignPose` + owner block | **No crash** — owner on first initial, pose on later deltas | `8705611` |
| **P2** | `EnableForeignReghostOwner` + owner block | **No crash** — first ghost without owner, descope, second initial with owner+pose | implemented; commit optional |
| P3/P4 | Client null-wheel guard | Not required for this race | last resort only |

Deep RE, wire maps, and campaign notes: [`docs/debugger-hits/OWNER_WHEEL_RACE_RE.md`](debugger-hits/OWNER_WHEEL_RACE_RE.md).

### Recommended product defaults (max owner fidelity = P2)

```json
{
  "ScopeGlobalVehicleGhost": true,
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

- **P2 (reghost)** — preferred when maximizing fidelity: second ghost initial can carry **owner + pose** after create/wheels exist. Thorough design: [`OWNER_WHEEL_RACE_RE.md` §12](debugger-hits/OWNER_WHEEL_RACE_RE.md).
- **P1 (defer pose)** — alternative if descope flicker is unacceptable: owner on first initial, pose on later deltas (`8705611`).
- Keep create-before-ghost hold + nest re-apply without IsItemLink (no tooltip spam).

### What works today (P2 + minimal foreign)

| Have | Notes |
| --- | --- |
| Foreign CreateVehicle + wheel nest | Wheels visible in-world |
| Ghost scope, path, template, spawn-owner | Initial body blocks |
| Owner/driver after reghost | Second initial; brief no-owner window first |
| Pose after scope | Second initial can include pose with owner |
| No IsItemLink tooltips / no owner-race AV | Live validated |

### What is still missing (same config)

These are **not** fixed by P2; they need later delta expansion or new wire work:

| Missing | Cause |
| --- | --- |
| Live **health** / max HP | Minimal foreign deltas only allow pose + wheel |
| **Targeting** | `TargetMask` filtered out on foreign deltas |
| **AI combat state** updates | `StateMask` filtered (and needs owner latch) |
| Weapon / armor / ornament **changes** after spawn | Equipment masks filtered on foreign deltas |
| Skills, GM, attributes, murderer | Same filter / incomplete stubs |
| Handling multipliers, clan, pet | Never wired (always default/false) |
| Smooth continuous NPC movement | Pose-only deltas + possible reghost pop; density/AI not retail-complete |

**Next focus:** smoother movement first (pose priority + richer `ApplyServerMove`), then widen minimal foreign **delta** admission one family at a time (e.g. AI → health → target → weapons), with owner already safe via P2.

### Movement smoothness (2026-07-11 → RE closure)

**Symptom:** NPC follows the path but **skips/teleports forward every ~250–500 ms** (not smooth frame-rate motion).

#### Client motion model (Ghidra, Final Exam / Gunny context)

Mission `h_1-1_tas_arkbay_finalexam` kills template vehicle **580** (Gunny Sioux, CBID 12425). Live WireDiag: path+owner can ship; continuous pose is still the only reliable visual mover.

| Client path | Address | Role |
| --- | --- | --- |
| Ghost pose unpack | `VehicleNet_UnpackGhostVehicle` `0x005F7720` | Reads pos/rot/vel/angVel + quantized throttle/steer |
| Apply network pose | `Vehicle_setDrivingInputs` `0x00504C70` | Throttle `+0x614`, steer `+0x618` → `FUN_0053EEC0` |
| Pose apply / buffer | `FUN_0053EEC0` | Stores network target buffer; **if Havok fully active, falls through and hard-writes graphics pose every pack** |
| Soft teleport threshold | `DAT_009d000c` = **15.0** | Soft path only hard-teleports when error &gt; 15 units |
| Throttle → action | `FUN_004FBC10` | **No-op unless `vehicle+0x1A0` (VehicleAction) exists** (created by activate) |
| Dead-reckon / validate | `FUN_0053E820` / `FUN_0053F1F0` | Extrapolates buffer with **linear velocity × dt**; corrects when diverged |
| Integrate dt from net | unpack call site | `dt = ghostObj+0xBC * 0.001` (ms→s) into `FUN_0053EB90` |
| Client path AI | `CVOGHBAIDriver_DoLogic` `0x005D7750`, `FUN_005CE990` | Frame-rate path follow — **requires working driver AI attach** |
| Map path geometry | `CVOGMapPath_AdvanceAndSteer` `0x005DF950` | Steer toward waypoints (client-side) |

**Hard conclusion from live A/B:**

1. **`EnableClientSidePathVisual` (suppress idle pose) freezes NPCs** even with `path=1 owner=1 clientOwner=1` — client HBAI is **not** driving our foreign vehicles. Server pose is mandatory for motion.
2. Retail smooth feel is **not** “client path AI alone for remotes”; it is **Havok + throttle/steer + dense pose corrections**, with velocity used for short dead reckoning between packs.
3. Ghost field we call `Acceleration` is **throttle** (`WriteSignedFloat` 6-bit ∈ **[-1,1]**), **not** m/s². Constant-speed path with `d(speed)/dt ≈ 0` left throttle at 0 → Havok freezes → next pose is a visible skip. Fixed: cruise throttle **1.0** while speed &gt; ε.
4. Sparse TNL ghost slots (priority 0.40 vs character 0.5, stream `IsFull()` cut-off) → packs every few hundred ms → skip-forward cadence matches **ghost update rate**, not sector tick alone.

#### Server work applied

| Step | Change |
| --- | --- |
| M1 | Vehicle ghost priority weight **0.40** (was 0.15 props) |
| M1b | **Moving** vehicle weight **0.50** (match Character) + skip boost **0.05** |
| M2 | `ApplyServerMove(dt)` fills angVel/steering; **cruise throttle** while moving |
| M3 | Tick NPCs **before** Pulse; keep `PositionMask` dirty while moving |
| Soft path | `EnableSoftNpcPathMotion` — yaw rate, Y blend, velocity carry (server path quality only) |
| Client-path visual | **Rejected** — freezes (client AI not latched) |

**Live tick tuning:** `/sectorTick` / `sector.tick` (1–5000 ms). Alone does not fix skip if TNL still starves the ghost.

**Still open for true retail parity:** ensure foreign activate creates `VehicleAction` so throttle reaches Havok; mid-session AI state / combat deltas.

#### TNL rate starvation (same cadence after priority — 2026-07-11 evening)

Live WireDiag often shows only **~3 pose packs** per foreign vehicle after scope, not continuous 100 ms streams.

TNL `DefaultFixedBandwidth = 2500` B/s and `DefaultFixedSendPeriod = 96` ms → **~240 B/packet**. Each foreign pose is ~500 bits (~62 B) plus framing. A few GhostVehicles fill the packet; the rest wait **multiple send periods** → **~250–500 ms** between pose packs for a given NPC. Client hard-snaps each pack → skip-forward cadence.

Ctor already called `SetFixedRateParameters(50, 50, 40000, 40000)`, but client rate negotiation (and `LocalRate`/`RemoteRate` sharing one object in this TNL port) can collapse both sides back to ~2500/96.

**Fix:** `TNLConnection.ComputeNegotiatedRate` floors ghosting connections to **≥20 KB/s** effective send and **≤50 ms** period so multi-NPC pose fits every tick.

**Still same cadence after floor (live):** Gunny logged `period=50ms packetSize=1490B` but only **initial + 3 pose deltas** then **no further GhostPack** for that coid. Bandwidth was not the remaining limit — **pose dirty list was dying**. `IsMovingForPoseStream` used velocity only; waypoint waits / zero-vel frames cleared keep-dirty → ghost left `GhostZeroUpdateIndex`.

**Fix (follow-up):** `ShouldStreamPose` keeps `PositionMask` dirty for **path + IdlePatrol** even at zero velocity; `NpcTicker` re-dirties pose while holding on a waypoint; pathing foreign vehicles use **`ObjectLocalScopeAlways`** so interest flaps do not `DetachObject` mid-patrol.

### Historical live result 2026-07-11 morning (owner + pose, no defer)

| Time | Event |
| --- | --- |
| 09:01:33 | Foreign GhostVehicle initial `owner=1/1` + pose |
| 09:01:37 | Client AV `0x004F5566` |

That failure mode is what P1/P2 avoid.

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
| `0x004F5560` | Dereferences vehicle `+0x258` wheelset pointer; AV site is `0x004F5566`. |
| `0x004FEA90` | Client wheelset setter. |
| `0x00504480` | Nested wheelset handling during `CreateVehicle`. |
| `0x005F7720` | `VehicleNet_UnpackGhostVehicle`. |

## Confirmed root cause chain

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

Do **not** enable more field families until the owner branch is stable live.

1. ~~Decoder contract for vehicle creature-owner form~~ — done (no SpawnOwner slot; see above).
2. ~~Live Ark Bay with bit-aligned owner~~ → **AV returned** (see live result table). Owner off again.
3. Confirm foreign CreateVehicle leaves client vehicle `+0x258` null or set (CDB / CREATEVEHICLE debugger).
4. Trace owner-present → `FUN_00503F30` / `Vehicle_TryActivatePhysics` / `Vehicle_createVehicleAction`; require wheelset before Havok build.
5. Server options once client path is clear: ensure CreateVehicle nested wheelset always applies; delay owner until wheelset is client-side; or keep owner off until activation is safe.
6. Only after owner alone is stable: evaluate AI state, health, and equipment.

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

Experiment 2026-07-11: owner lever on + bit-aligned creature-owner form → **AV 0x004F5566 returned**. Lever restored to **false**. Next: prove client `+0x258` after foreign CreateVehicle, and whether owner unpack forces `Vehicle_createVehicleAction` / `FUN_00503F30` without a wheelset.

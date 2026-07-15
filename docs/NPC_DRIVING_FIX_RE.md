# NPC vehicle driving — deep RE + fix plan (`fix/npcDriving`)

Date: 2026-07-14. Client: `autoassault` base `0x400000`. Worktree: `.worktrees/fix-npcDriving` / branch `fix/npcDriving`.

## Symptom (player-visible)

NPC vehicles slide sideways, snap or lag heading on curves, ignore turn radius / accel / brake, and sit with yaw-only chassis pitch/roll so they hover or clip slopes. Movement looks like raw path transforms, not vehicle control.

## One-line root cause

**AutoCore authors path *pose* on the server; retail authors *drive inputs* and lets Havok + suspension produce pose.** Our ghost stream then hard-corrects that yaw-only pose onto the client, so wheels/terrain never get a chance to look right.

---

## 1. Retail pipeline (local HBAI)

```
CVOGHBAIDriver::DoLogic                 005d7750
  └─ ReturnToNormalLocation             005d6e80
       ├─ CVOGWaypoint::UpdateState     005d6300
       │    state0 → FUN_005d5750  (MapPath follow)
       │         └─ CVOGMapPath_AdvanceAndSteer  005df950
       │              • accept radius / reactions / reverse wrap
       │              • aim point → waypoint+0x20
       │              • curvature radius from prev/cur/next  → param_8
       │              • speed scale at waypoint+0x58 (R < 30 → slow)
       └─ vtable+0x4c(aim)  →  CVOGVehicle::MoveToTarget3DPoint  004fc650
            writes vehicle+0x614 thr, +0x618 steer, +0x61c sharp
            └─ FUN_004fbc10 → VehicleAction (vehicle+0x1a0)
                 └─ VehicleAction_applyAction  00598650
                      steer ramp, calcWheelTorque, airStabilization
                      → hkDefaultSteering / suspension / friction
```

**Retail does not lerp chassis XYZ along the MapPath for local AI.**  
Path code chooses an **aim point** (+ curvature slowdown). **Havok** moves the chassis and grounds it through **suspension / wheel contact**.

### 1.1 `MoveToTarget3DPoint` (`004fc650`) — drive math

| Input | Source |
|-------|--------|
| Aim | vehicle `+0x190..+0x198` (set by AI aim) |
| Chassis pos / basis | physics object transform |
| Chassis vel | physics `+0x40..` |

| Output | Offset | Meaning |
|--------|--------|---------|
| Throttle | `+0x614` | Forward / reverse axis |
| Steering | `+0x618` | Lateral aim error vs right vector |
| Sharp | `+0x61c` | 1 when speed high + large heading error (rear traction cut in `calcWheelTorque`) |

Constants (memory):

| Addr | Value | Role |
|------|------:|------|
| `00a0f718` | ~0.01 | Near-zero lateral deadband |
| `00a0f710` | ~0.70 | Lateral threshold for sharp |
| `00aaa7a4` | 15.0 | Speed gate for sharp |
| `00a0f694` | 30.0 | Distance / curvature helpers |
| `00aaab14` | ~1/30 | Curvature slow scale |
| `00a10e78` | 0.05 | Steer ramp step (also used in path slow) |
| `00aaa668` | −1.0 | Reverse / clamp floor |

Rough formula (matches AutoCore `VehicleDriveInputs` intent):

```
toAim     = aim - pos
dir       = normalize(toAim)          // full 3D normalize, thr uses XZ distance too
steer    ∝  right · dir               // clamped [-1,1]
thr      ∝  forward alignment + dist  // reverse-facing → reverse thr path
sharp     = (|v| > 15) && (|lat| > 0.7)
→ FUN_004fbc10 into VehicleAction
```

**No position write in this function.**

### 1.2 Path advance + curvature (`005df950` + state0 `005d5750`)

`AdvanceAndSteer`:

1. Invalid index → nearest point (full 3D distance).
2. Outside accept → keep current index, fill aim.
3. Inside accept → fire `ReactionCoid`, advance ±1, wrap or reverse.
4. Load **prev / current / next** point positions.
5. Circle geometry through those three points → **turn radius** (`param_8`). Degenerate → large radius sentinel.

State0 then stores a **speed scale** at `waypoint+0x58`:

- If radius ≥ **30** → scale ≈ 0 (no extra slow; decompiler sign is noisy — live intent is “tight curves slow”).
- Tight radius → positive slowdown factor involving `(30 − R) * 0.05 * (1/30)`.

So retail path AI already has **look-ahead geometry for curvature** and **corner slowdown**. It still **aims at the path aim point**, not a pure “next node only” pose teleport.

### 1.3 Path heartbeat teleport (`005ce990`)

Client-only helper when local AI owns path: steps along aim, clamps Y via terrain height (`FUN_004cd220` → heightfield / cast) + **+1.0 clearance**, then `CVOGReaction_TeleportTarget`. This is **not** the primary foreign-NPC path; it is local AI / reaction teleport with **ground clamp**, not multi-point chassis plane.

### 1.4 Ground / pitch / roll

| Layer | Address | What it does |
|-------|---------|--------------|
| Creature Y snap | `CVOGCreature_FindTerrainHeight` `004c6100` | Heightfield + optional ray; **Y only** + foot offset |
| Map cast | `CVOGMap_CastTerrainHeight` `004cfe60` | Down-cast collision |
| Vehicle stance | `VehicleAction_applyAction` + `hkDefaultSuspension_update` | **Wheel contacts** set chassis pitch/roll |

There is **no retail “sample 4 corners and slerp pitch/roll on the MapPath follower”** for vehicles. Stance comes from **Havok suspension** when physics runs. Server-forced **yaw-only quaternions** on a hard pose path **override** that stance every ghost pack when physics is “fully ready” (`FUN_0053eec0` fall-through writes entity `+0x84` pos / `+0x94` rot).

### 1.5 Foreign / network apply

`Vehicle_setDrivingInputs` `00504c70` (unpack `VehicleNet_UnpackGhostVehicle` `005f7720`):

```
+0x614 thr, +0x618 steer, +0x61c sharp
FUN_004fbc10()                    // needs VehicleAction
FUN_0053eec0(pos, rot, vel, angVel, integrateDt)
```

`FUN_0053eec0`:

| Branch | Condition | Behavior |
|--------|-----------|----------|
| Soft buffer | Physics object exists but **not fully ready** | Buffer pos/rot/vel/angVel; teleport only if error **> 15**; optional integrate `dt` |
| Hard write | Fully ready **or** no usable physics shell | **Overwrite** entity pos/rot |

Integrate dt on client: `ghostObj+0xBC * 0.001` (ms → s). Zero `dt` → no soft integrate step.

**Live conclusion (existing RE):** foreign NPCs do **not** run client MapPath HBAI usefully when we suppress pose (`EnableClientSidePathVisual` freezes). Server pose + thr/steer is mandatory.

---

## 2. What AutoCore does today

| Layer | Behavior | File |
|-------|----------|------|
| Path index / accept / wait / reactions | Hard geometric stepper | `NpcPathFollower.cs` |
| Default motion | **XZ toward current waypoint** at constant clonebase speed; **yaw = face waypoint**; Y lerp along segment then `SnapToTerrain` | same + `NpcTicker` |
| Soft motion (opt-in, **default OFF**) | Keep hard XZ; **rate-limit yaw lag**; ramp speed; thr/steer from look-ahead aim | `SoftNpcPathMotion.cs` |
| Drive axes | Soft: `VehicleDriveInputs`; hard: dYaw / speed-delta heuristics | `Vehicle.ApplyServerMove` |
| Terrain | **Single TGA sample → Y only** | `NpcTicker.SnapToTerrain` |
| Ghost | pos, rot, vel, angVel, Firing, VehicleFlags, **Acceleration=thr**, **Steering**, turret | `GhostVehicle.cs` |

### 2.1 Exact match to the bug report

| Observed | Mechanism in code |
|----------|-------------------|
| Slide sideways / face wrong while moving | Soft path: **position follows hard chord to waypoint**, **rotation lags** (`LimitYaw`). Velocity still along hard chord → chassis slides. |
| Snap heading | Hard path: `Rotation = YawQuaternion(dx,dz)` every tick toward **current** node; index advance snaps face. |
| No look-ahead steering feel | Position always chases **current** index. Soft look-ahead only affects thr/steer aim, **not** pose tangent. |
| No corner brake / accel feel | Hard path: constant `speed`. Soft ramps speed but **does not** use AdvanceAndSteer radius (no 30u curvature scale). |
| Hover / clip / flat on slopes | Yaw-only quat + pure Y snap. **No pitch/roll**. Client hard pose overwrites suspension. |
| “Raw transform updates” | Dense or sparse ghost packs hard-write XYZ+yaw; thr may not reach Havok if `VehicleAction` missing. |

### 2.2 Soft path design trap (documented intentionally)

`SoftNpcPathMotion` comments reject pure-pursuit **position** because vehicles orbit AcceptDistance. The compromise was:

> hard path XZ for progress + lag yaw for “visible turn”

That is **exactly lateral slide**. Any fix must either:

- move pose along **facing / bicycle model** while still entering AcceptDistance, **or**
- keep hard XZ but **always face velocity** (no lag) and rely on thr/steer for **wheel** animation only.

---

## 3. What the server must do

### 3.1 Separate navigation vs movement (as requested)

| Component | Responsibility | Must not |
|-----------|----------------|----------|
| **Navigator** (`NpcPathFollower`+) | Index, AcceptDistance, WaitTime, reactions, reverse, **desired route** (current + look-ahead aim, curvature radius) | Write final chassis rot from “face next node” alone |
| **Movement controller** (new / evolve Soft) | Convert route → speed, thr, steer, sharp, yaw rate limits, **terrain-aligned orientation**, velocity aligned with facing | Teleport along chord while facing elsewhere |

### 3.2 Movement controller algorithm (server-side, retail-shaped)

Each tick (`dt` ≈ 0.05–0.10 s):

1. **Navigator**  
   - Current target = path point `i` (AcceptDistance logic unchanged — progress must stay geometric).  
   - **Aim** = look-ahead along polyline (~16–28 u, retail-style).  
   - **Radius** = circle through prev/cur/next (port `005df950` end math).  
   - **Desired cruise** = clonebase speed × corner scale (`R ≥ 30 → 1`, tight R → reduce).

2. **Longitudinal**  
   - Approach desired speed with accel / brake limits (soft already has 50/60).  
   - Near wait nodes with WaitTime > 0: brake to 0; zero-wait: carry speed (already).

3. **Lateral / heading**  
   - Desired yaw = atan2 toward **aim** (not only current node when far).  
   - Clamp yaw rate (soft has ~1.5π rad/s — tune to clonebase / retail feel).  
   - **Velocity direction must match facing** (or bicycle: velocity follows yaw with small slip).  
   - **Never** advance XZ along “to node” while yaw lags that vector.

4. **Position progress (AcceptDistance-safe)**  
   Preferred: integrate `pos += facing * speed * dt` (or hard step length along **segment projection**, then correct onto segment).  
   Rejected previously: free pure-pursuit orbit — fix by **arrival test on path arc-length / closest point on segment**, not only “distance to node while drifting beside it”.

5. **Terrain alignment**  
   - Sample heightfield at chassis center + front/back (and optionally left/right) under approximate vehicle length/width from clonebase.  
   - Average contact plane → pitch/roll.  
   - Clamp pitch/roll rates; enforce clearance (clonebase / ~1.0 like client teleport pad).  
   - Y = plane height at center (not lone single sample if multi-point available).

6. **Drive axes (always pack explicitly)**  
   - `VehicleDriveInputs.Compute(pos, yaw, aim, speed)` every tick while moving.  
   - Do **not** derive steer only from `dYaw` of already-aligned pose (wheels stay straight).  
   - Sharp when speed > ~15 and |lat| high (retail).  
   - **Do not** set Handbrake bit for sharp (packing order already fixed: Firing then VehicleFlags).

### 3.3 Defaults / levers

| Lever | Today | Target |
|-------|-------|--------|
| `EnableSoftNpcPathMotion` | OFF | ON after slide fix, or replace with always-on movement controller |
| `EnableClientSidePathVisual` | OFF (freezes) | Keep OFF |
| Sector / ghost rate | Floored | Keep dense pose while moving |

---

## 4. What we must send to clients

Ghost **PositionMask** block (`GhostVehicle` already carries most fields):

| Field | Wire role | Required value for fix |
|-------|-----------|------------------------|
| `Position` XYZ | Hard / soft pose target | Grounded; XZ from controller; Y from terrain plane |
| `Rotation` XYZW | Chassis basis | **Yaw + pitch + roll** (not yaw-only identity X/Z) |
| `Velocity` XYZ | Dead-reckon / soft buffer | **Aligned with facing × speed** (not waypoint chord if facing differs) |
| `AngularVelocity` | Soft integrate | `ω_y = dYaw/dt` (+ small pitch/roll rates optional) |
| `Acceleration` (thr) | `vehicle+0x614` | Cruise ~1 while moving; corner / brake ease; reverse only when re-orienting |
| `Steering` | `vehicle+0x618` | From look-ahead lateral error |
| `VehicleFlags` / sharp | `+0x61c` path | Sharp assist when warranted; **not** permanent handbrake |
| `Firing` | hardpoints | Unrelated; keep packing order |
| Integrate dt | client `ghost+0xBC` | Non-zero so soft buffer can integrate between packs |

**Client prerequisites (not server packets, but must be true):**

1. Foreign vehicle **activated** with **VehicleAction** (`+0x1a0`) so `FUN_004fbc10` is not a no-op → wheels spin / Havok between packs.  
2. Owner / driver create order still required for HUD and activate (existing owner RE).  
3. Dense pose (~50 ms effective) so corrections stay ≪ 15 u soft teleport threshold.

We **cannot** “send suspension”; we send **pose that already sits on the surface** and thr/steer so wheels animate. If physics is fully ready, hard pose **is** the visual chassis — so orientation **must** include terrain pitch/roll on the wire.

---

## 5. Implementation plan (this branch)

Ordered, TDD per `Agents.md`:

1. **Characterization tests** for current hard/soft slide (pos Δ vs facing).  
2. **Port curvature radius** helper from `005df950` (unit tests with known triangles).  
3. **Movement controller**  
   - velocity ‖ facing  
   - look-ahead thr/steer always  
   - corner speed scale  
   - AcceptDistance via closest-on-segment or along-track progress  
4. **Terrain plane** from heightfield multi-sample → quat; tests with synthetic field.  
5. **ApplyServerMove** always takes explicit thr/steer from controller.  
6. **Enable** controller by default; keep lever for A/B.  
7. Live check: path 5092 Skiddoo — no orbit, no slide, wheels turn, slopes pitch.

Out of scope for first PR: full client Havok port on server; multi-body collision between NPCs.

---

## 6. Ghidra anchor index (this pass)

| Address | Name | Role |
|--------:|------|------|
| `005d7750` | `CVOGHBAIDriver_DoLogic` | AI tick |
| `005d6e80` | `CVOGHBAIDriver_ReturnToNormalLocation` | Path vs pursue gate |
| `005d6300` | `CVOGWaypoint_UpdateState` | Waypoint state machine |
| `005d5750` | MapPath follow state | Advance + curvature speed scale |
| `005df950` | `CVOGMapPath_AdvanceAndSteer` | Index / aim / radius |
| `004fc650` | `CVOGVehicle::MoveToTarget3DPoint` | thr/steer/sharp |
| `00504c70` | `Vehicle_setDrivingInputs` | Net entry |
| `004fbc10` | thr → VehicleAction | No-op without action |
| `00598650` | `VehicleAction_applyAction` | Havok vehicle tick |
| `00598040` | `VehicleAction_calcWheelTorque` | Wheels + sharp traction |
| `0053eec0` | Network pose apply | Soft 15u / hard write |
| `005ce990` | Path heartbeat teleport | Local AI ground Y clamp |
| `004c6100` | `CVOGCreature_FindTerrainHeight` | Creature Y (+foot) |
| `004cfe60` | `CVOGMap_CastTerrainHeight` | Ray height |

Related docs: `NPC_VEHICLE_DRIVE_RE.md`, `MOTION_CLIENT_RE.md`, `nullWheels.md`, `NPC.md` §2 / §15.7.

---

## 7. Implementation + lever (2026-07-14)

| Lever | Default | Env |
|-------|---------|-----|
| `EnableNpcVehicleDriveController` | **false** (legacy) | `AUTOCORE_WIRE_NPC_VEHICLE_DRIVE` |

**Ticker priority (vehicles only):**

1. Drive controller ON → `NpcVehicleDriveController.Apply`
2. Else soft ON → `SoftNpcPathMotion.Apply` (legacy)
3. Else hard `NpcPathFollower` only

Foot creatures never use the drive controller. Soft path behavior is unchanged when the drive lever is off.

| Type | Path |
|------|------|
| Controller | `src/AutoCore.Game/Npc/NpcVehicleDriveController.cs` |
| Curvature | `src/AutoCore.Game/Npc/PathCurvature.cs` |
| Terrain plane | `src/AutoCore.Game/Npc/TerrainContactPlane.cs` |

**v1 non-goals still open:** pack sharp via dedicated wire (do not set Handbreak); enable lever by default after live sign-off.

### Terrain / “stays level” RE (2026-07-14 evening)

| Client fact | Implication for server |
|-------------|------------------------|
| `FUN_0053eec0` soft path only when physics **not** fully ready; fully ready → hard write entity `+0x84`/`+0x94` only | Pitch must be on the **entity quaternion** every pack; physics setRotation is skipped when active |
| `FUN_004e8a40` / `004e8ad0` | Local **+Z forward**, **+X right**, Y up — build basis that way |
| `VehicleAction_airStabilization` + applyAction upright impulse when up·world &lt; ~0.7 | Do not pack &gt;~40° tilt; clamp max pitch/roll |
| Soft buffer integrate uses **angVel** (`FUN_0053eb90`) | Pack **full** angVel (pitch/roll rates), not yaw-only |
| Suspension / wheel hardpoints on clonebase | Sample span from `WheelHardPoints`; clearance ≈ radius + 0.35×suspension |

**Server fix shipped:** plane-normal basis quat (not weak Euler), larger default wheelbase, clonebase footprint, ground clearance, slope-aligned linear velocity, full `EstimateAngularVelocity` on `ApplyServerMove`.

---

## 8. Summary for implementers

**Wrong today:** path follower is the movement controller; it teleports along chords with yaw-only facing (or soft lag facing), packs that as truth, and treats thr/steer as optional cosmetics.

**Right (retail-shaped under server authority):** path follower is navigation only; a vehicle controller produces **facing-aligned velocity**, **look-ahead thr/steer**, **curvature-limited speed**, and **terrain-aligned rotation**; ghost carries all of that every pose pack; client needs **VehicleAction** so thr reaches Havok between corrections.

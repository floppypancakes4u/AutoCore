# Phase 5 wiring notes — `NpcTicker` × `ServerConfig` physics tier

**Branch / worktree:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-15  
**Scope:** Wiring plan only (no production code in this note).  
**Plan source:** `~/.claude/plans/cheerful-swimming-reef.md` §Phase 5  
**Evidence:** `drive-controller-spec.md` (`MoveToTarget3DPoint` @ `0x004fc650`), `0.1-step-rate.md` (StepTo @ `0x004d6c80`)

This file is the implementer checklist for **Phase 5 — Controller + tick wiring**: how the retail
Havok physics tier hooks into the sector NPC loop via existing config and the live ticker branch
order. It does **not** re-specify subsystem math (Phases 2–4) or ghost packing (Phase 6).

---

## 1. Goal

When operators opt in via `serverConfig.yaml`:

```yaml
npcVehiclePhysics:
  controllerTier: physics
  enabled: true
```

…pathing **vehicles** must run:

1. `NpcPathFollower.Step` (navigation / accept / wait / reactions — unchanged),
2. bit-exact **drive axes** from `VehicleDriveController` (port of `0x004fc650`),
3. **physics step** via `VehiclePhysicsInstance` + `VehicleActionSim` (Phase 4),
4. `ApplyMove` → ghost pose with thr/steer/sharp from the sim,

…while **foot creatures**, combat/return/flee ownership, and legacy tiers stay intact.

Default config (`enabled: false`, `controllerTier: hard`) must produce **byte-identical** behavior
to today’s ticker (no silent opt-in).

---

## 2. Current code (as of this note)

### 2.1 `ServerConfig` — tier hooks exist, not consumed by ticker

| Symbol | Location | Default | Role |
|--------|----------|---------|------|
| `NpcVehicleControllerTier` | `Diagnostics/ServerConfig.cs` | enum | `Hard=0`, `Soft=1`, `Kinematic=2`, `Physics=3` |
| `NpcVehiclePhysicsEnabled` | same | `false` | Master switch for Havok sim |
| `ControllerTier` | same | `Hard` | Which mover path vehicles use |
| `SubstepHz` | same | `60` (clamped [1,480]) | Placeholder fixed Hz; retail is frameDt/N (`HkVehicleSubstep`) |
| `Gravity` | same | `-9.81` | Fed into `HkVehicleDataCache.BuildFromCloneBases` at WAD load |
| `AirDensityOverride` | same | `null` | Optional aero override |
| `DebugLogging` | same | `false` | Per-vehicle physics log gate |

YAML section: `npcVehiclePhysics` (`enabled`, `controllerTier`, `substepHz`, `gravity`,
`airDensityOverride`, `debugLogging`). Loader: env `AUTOCORE_SERVER_CONFIG_FILE` → content root →
cwd → base dir. Invalid `controllerTier` rejects the load and leaves state unchanged.

**Important:** production code under `src/AutoCore.Game` does **not** read
`ServerConfig.ControllerTier` or `NpcVehiclePhysicsEnabled` for movement. Only
`ServerConfig.Gravity` is used today (`AssetManager` → `HkVehicleDataCache`). Tier selection is
still the older **wire levers** (below).

### 2.2 `NpcTicker.Tick` — live priority (vehicles)

File: `src/AutoCore.Game/Npc/NpcTicker.cs`

Call chain (sector): `SectorServer` main loop → `MapManager.TickNpcs(nowMs, delta/1000)` →
`NpcTicker.Tick(map, nowMs, dt)` **before** `Interface.Pulse()` so `PositionMask` dirties land on
the same TNL frame.

Per entity (snapshot of `map.NpcAiEntities`):

```
NpcCombatAi.Tick(...)
if ReturningHome || FleeUntilMs || PursuingThisTick → continue  // combat owns motion
if no MapPath → continue

wasHolding = nowMs < WaitUntilMs
stagger PathIndex / PathLaneOffset if first latch
hard = NpcPathFollower.Step(pos, path, index, dir, wait, now, speed, dt)
commit path index / direction / WaitUntilMs from hard

// --- MOVER BRANCH (vehicles only for drive path) ---
if NpcVehicleDriveController.Enabled && entity is Vehicle
    result = NpcVehicleDriveController.Apply(hard, …, heightfield, vehicle)
else if SoftNpcPathMotion.Enabled
    result = SoftNpcPathMotion.Apply(hard, …)
// else result stays hard PathStepResult

if !wasHolding || position changed → ApplyMove(entity, result, dt)
else if Vehicle holding on path → SnapToTerrain Y / re-dirty PositionMask
if FireReactionCoid > 0 → map.TriggerReactions
```

**`ApplyMove` nuances already present:**

- Drive controller + `HasDriveInputs` → **skip** TGA single-sample snap (multi-sample heightfield
  already in controller).
- `HasDriveInputs` → `Vehicle.ApplyServerMove(..., thr, steer, sharpTurn)`.
- Else hard/soft without inputs → `ApplyServerMove` without drive axes; Y from `SnapToTerrain`.
- Foot creatures never take the vehicle drive controller; soft path still may apply to them when
  `SoftNpcPathMotion.Enabled`.

### 2.3 Parallel opt-in levers (pre-ServerConfig)

| Lever | Default | Env suffix | Used by ticker today? |
|-------|---------|------------|------------------------|
| `NpcVehicleDriveController.Enabled` | `false` | `AUTOCORE_WIRE_NPC_VEHICLE_DRIVE` | **Yes** (kinematic) |
| `SoftNpcPathMotion.Enabled` | `false` | `AUTOCORE_WIRE_SOFT_NPC_PATH` | **Yes** |
| `ServerConfig.ControllerTier` | `Hard` | via yaml | **No** |
| `ServerConfig.NpcVehiclePhysicsEnabled` | `false` | via yaml | **No** |

Wire levers load via `WireIsolationLevers` (JSON + env). Phase 5 must **not** leave two competing
sources of truth. Recommended: `ServerConfig.ControllerTier` becomes authoritative for path
movers; wire levers map onto the same tiers for tests/compat (see §5).

### 2.4 Physics substrate status (Phase 5 dependencies)

Present under `src/AutoCore.Game/Physics/Vehicle/` (partial Phases 2–4):

| Artifact | Role for Phase 5 |
|----------|------------------|
| `HkVehicleData` / `HkVehicleDataCache` | Immutable per-CBID setup; gravity from `ServerConfig` at WAD load |
| `HkVehicleSubstep` | Retail `frameDt/N` split (`0x4d6c80`); prefer over raw `SubstepHz` for accumulator |
| `HkRigidBody` | Semi-implicit Euler skeleton |
| Subsystem modules | WheelCollide, Suspension, Brake, VelocityDamper, TorqueCurve2D, … |
| **Missing for Phase 5** | `VehicleActionSim`, `VehiclePhysicsInstance`, `VehicleDriveController` (bit-exact), `NpcVehiclePhysicsController` |

`Vehicle` entity has **no** physics-instance field yet. `PathStepResult` already carries
`Throttle`, `Steering`, `SharpTurn`, `HasDriveInputs`. `ApplyServerMove` still **discards**
`sharpTurn` (`_ = sharpTurn`) — Phase 6 concern, but physics controller should still **populate**
it so thr/steer/sharp stay coherent end-to-end.

---

## 3. Target architecture (Phase 5)

```
                    ServerConfig
         ControllerTier + NpcVehiclePhysicsEnabled
                           │
                           ▼
NpcTicker.Tick ──► resolve vehicle mover tier (vehicles only)
                           │
     ┌─────────────────────┼─────────────────────────────┐
     │ Physics             │ Kinematic                   │ Soft / Hard
     ▼                     ▼                             ▼
NpcVehiclePhysicsController   NpcVehicleDriveController   SoftNpcPathMotion / hard only
     │                         (existing)                    (existing)
     ├─ NpcPathFollower hard (already stepped)
     ├─ aim from path look-ahead / PathCurvature cruise
     ├─ VehicleDriveController → thr/steer/sharp  (0x4fc650)
     ├─ VehiclePhysicsInstance.Step(axes, frameDt)
     │     └─ HkVehicleSubstep.Compute → N × VehicleActionSim
     └─ PathStepResult from sim pose + axes
                           │
                           ▼
                    ApplyMove / hold snap
```

### 3.1 New types (plan names — implement under these paths)

| Type | Path | Responsibility |
|------|------|----------------|
| `VehicleDriveController` | `Physics/Vehicle/VehicleDriveController.cs` | Pure axes: pose+vel+aim → thr/steer/sharp. **Bit-exact** `0x4fc650` per `drive-controller-spec.md` (incl. inverted throttle sign). Not the kinematic pose controller. |
| `NpcVehiclePhysicsController` | `Npc/NpcVehiclePhysicsController.cs` | Glue: hard step + aim + axes + sim → `PathStepResult` |
| `VehiclePhysicsInstance` | `Physics/Vehicle/VehiclePhysicsInstance.cs` | Per-vehicle mutable RB + wheels + action state; `Step(thr, steer, handbrake, frameDt)` |
| `VehicleActionSim` | `Physics/Vehicle/VehicleActionSim.cs` | One substep applyAction order (Phase 4) |

Name collision note: existing `NpcVehicleDriveController` is the **kinematic** tier (`ControllerTier.Kinematic`).
New `VehicleDriveController` is the **retail axis generator** used only by the physics tier (and
tests). Do not merge them; do not rename the kinematic class in Phase 5 unless a separate cleanup
is requested.

### 3.2 Gate condition (exact)

Physics path is taken **only** when **all** of:

1. `entity is Vehicle`
2. `ServerConfig.NpcVehiclePhysicsEnabled == true`
3. `ServerConfig.ControllerTier == NpcVehicleControllerTier.Physics`
4. Physics instance can be resolved (CBID has `HkVehicleData`; otherwise **fail closed** to kinematic
   or hard — never throw on the sector tick; log once if `DebugLogging`)

If `controllerTier: physics` but `enabled: false` → **do not** run physics (yaml comments already
document this). Prefer treating as Hard (or last non-physics tier if wire levers set Soft/Kinematic
during transition — see §5.2).

### 3.3 Recommended `NpcTicker` branch (pseudocode)

Replace the dual static `Enabled` checks with tier resolution. Preserve combat / path / hold logic
above the branch.

```csharp
// After NpcPathFollower.Step + state commit; result starts as hard.

if (entity is Vehicle vehicle)
{
    var tier = ResolveVehicleMoverTier(); // see §5

    if (tier == NpcVehicleControllerTier.Physics
        && ServerConfig.NpcVehiclePhysicsEnabled)
    {
        result = NpcVehiclePhysicsController.Apply(
            result, vehicle, path, nowMs, dt, map, npcAi);
    }
    else if (tier == NpcVehicleControllerTier.Kinematic)
    {
        result = NpcVehicleDriveController.Apply(
            result, entity.Position, GetRotation(entity), ResolveSpeed(entity),
            dt, path, nowMs, GetVelocity(entity), npcAi.PathLaneOffset,
            map.MapData?.Heightfield, vehicle);
    }
    else if (tier == NpcVehicleControllerTier.Soft)
    {
        result = SoftNpcPathMotion.Apply(
            result, entity.Position, GetRotation(entity), ResolveSpeed(entity),
            dt, path, nowMs, GetVelocity(entity), npcAi.PathLaneOffset);
    }
    // Hard: leave result as NpcPathFollower output
}
else if (SoftNpcPathMotion.Enabled /* or Soft tier if ever shared */)
{
    // Foot creatures: keep current soft-or-hard behavior (no vehicle physics).
    result = SoftNpcPathMotion.Apply(...);
}
```

**`ApplyMove` update for physics tier:**

- When physics produces `HasDriveInputs` and sim-authored pose, **do not** re-snap with
  `SnapToTerrain` (same rule as drive controller today). Grounding is the wheel-collide /
  suspension path inside the sim.
- Pass thr/steer/sharp into `ApplyServerMove`.
- Phase 6 will teach `ApplyServerMove` to accept sim `AngularVelocity` without
  `EstimateAngularVelocity`; until then, either:
  - **Preferred temporary:** extend `ApplyServerMove` (or a sibling) to take optional angVel when
    physics supplies it (small Phase 5.5/6 overlap), or
  - Accept estimated angVel from quat delta for a short window (worse air / roll fidelity).

Hold-on-waypoint path for physics: zero thr, optional handbrake (retail arrival sets sharp=1 and
throttle 0 — see drive-controller-spec §3 arrival gate); keep `PositionMask` dirty like today’s
vehicle hold branch.

---

## 4. `NpcVehiclePhysicsController` contract

### 4.1 Inputs

| Input | Source |
|-------|--------|
| Hard `PathStepResult` | Already computed; owns index/dir/wait/reaction/NowReversing |
| Vehicle pose / vel | `vehicle.Position`, `Rotation`, `Velocity` (+ angVel when stored) |
| Cruise speed | `NpcTicker.ResolveSpeed(vehicle)` (driver clonebase Speed or `DefaultVehicleSpeed`) |
| Path + lane | `MapPathTemplate`, `npcAi.PathLaneOffset` |
| Aim | Look-ahead along path (reuse `NpcVehicleDriveController` / soft look-ahead pattern, or
  hard waypoint if look-ahead not yet extracted). Retail AI pre-writes aim at entity+0x190. |
| Accept distance | Current path point’s AcceptDistance (for arrival brake in axis gen) |
| Cruise scale | From `PathCurvature` corner scale (kinematic controller already has
  `ResolveCornerSpeedScale`) — maps to `cruiseScale` param of `0x4fc650` |
| Frame dt | Sector tick dt (seconds), same as today’s `NpcTicker` argument |
| Collision query | `IVehicleCollisionQuery` from map heightfield (`TerrainHeightfieldCollisionQuery`) |

### 4.2 Internal sequence (one ticker tick)

1. If hard result is **waiting** (`Arrived && WaitUntilMs > nowMs`): park axes (thr=0, sharp=1 per
   retail arrival), do **not** integrate physics pose (or integrate with thr=0 only — pick one and
   test; recommendation: **freeze pose**, re-dirty mask, match kinematic wait).
2. Resolve **aim** (look-ahead point on path, lane-offset in XZ).
3. `VehicleDriveController.Compute(chassisBasis, pos, vel, aim, acceptDist, cruiseScale, allowReverse)`
   → thr, steer, sharp.  
   **Preserve retail signs:** forward drive throttle is **negative** (`base = -1`); see
   `drive-controller-spec.md` golden vectors.
4. Lazy-get / create `VehiclePhysicsInstance` on vehicle (CBID → `HkVehicleDataCache`).
5. If teleported / large pose error / respawn: `instance.ResetFromEntity(vehicle)`.
6. `instance.Step(thr, steer, sharp != 0, frameDt)` using `HkVehicleSubstep.Compute(frameDt)` loop.
7. Build `PathStepResult`:
   - Navigation fields from hard (index, direction, wait, reaction, NowReversing).
   - Pose/vel from sim; rotation from sim quaternion.
   - `Throttle/Steering/SharpTurn/HasDriveInputs` from step 3 (axes actually fed to sim this tick).
8. Return; ticker `ApplyMove` streams.

### 4.3 Per-vehicle instance lifecycle

| Event | Action |
|-------|--------|
| First physics tick | Create instance; seed RB from entity pos/rot/vel; zero forces |
| Spawn / map attach | Reset or create |
| Death / corpse | Stop stepping (ticker already skips corpses); release instance optional |
| Teleport / path reassignment with large Δpos | Reset RB to entity pose to avoid constraint explosion |
| Tier leave (config flip mid-run) | Stop using instance; optional dispose; entity pose already authoritative |

Storage: private field on `Vehicle` (e.g. `VehiclePhysicsInstance _physics`) with internal getter
for tests. No static global dictionary keyed by COID (map unload / GC hazards).

### 4.4 Substep vs `ServerConfig.SubstepHz`

- **Authoritative retail rule:** `HkVehicleSubstep.Compute(frameDt)` → N substeps, max ~1/30 s
  (`0.1-step-rate.md`).
- `SubstepHz` remains a **config placeholder** (default 60). Phase 5 wiring should call the retail
  split, not `1f/SubstepHz`, unless an explicit debug mode is added later.
- Document in yaml comments when implementing: “substepHz unused when retail accumulator is on”
  or re-purpose as optional override (out of scope unless product asks).

---

## 5. Config × wire-lever reconciliation

### 5.1 Desired end state

| `controllerTier` | `enabled` | Vehicle mover |
|------------------|-----------|---------------|
| `hard` | * | Hard `NpcPathFollower` only |
| `soft` | * | `SoftNpcPathMotion` |
| `kinematic` | * | `NpcVehicleDriveController` |
| `physics` | `false` | Fail closed → `hard` (or documented fallback) |
| `physics` | `true` | `NpcVehiclePhysicsController` |

Foot creatures: never physics / kinematic vehicle controller; soft only if soft tier or legacy soft
lever (product choice: soft tier may be vehicle-only; today’s soft applies to both — preserve
unless tests force a change).

### 5.2 Transition mapping (recommended)

On `ServerConfig.ApplyFromConfigFiles` **or** at first `ResolveVehicleMoverTier()`:

1. If `ControllerTier` is not default Hard **or** physics enabled → trust ServerConfig.
2. Else if `NpcVehicleDriveController.Enabled` → treat as Kinematic.
3. Else if `SoftNpcPathMotion.Enabled` → treat as Soft.
4. Else Hard.

This keeps existing integration tests and env levers working until they are migrated to set
`ServerConfig.ControllerTier` in test setup.

Longer term (not required for Phase 5 green): make wire levers **write through** to
`ServerConfig.ControllerTier` and deprecate dual switches.

### 5.3 Startup order

Already: Launcher/Sector call `ServerConfig.ApplyFromConfigFiles()` early. Ensure this runs
**before** any NPC tick and before WAD/`HkVehicleDataCache` if gravity overrides matter (cache
already uses `ServerConfig.Gravity` at build time).

---

## 6. `VehicleDriveController` RE gate (Phase 5 module)

Before production code for the axis generator:

1. Re-decompile `CVOGVehicle::MoveToTarget3DPoint` @ `0x004fc650` (`PORTING_RULES.md`).
2. Confirm constants via `read_memory` (table in `drive-controller-spec.md` §2).
3. Implement pure function + unit tests for the **four golden vectors** in §5 of that spec
   (throttle signs, reverse, sharp).
4. Do **not** use heuristic `VehicleDriveInputs.Compute` for the physics tier (different gains:
   lateral×1.25, sharp thresholds 6/0.45, **positive** cruise thr). Keep it for legacy soft if
   still referenced.

Chassis basis: +Z forward, +X right, +Y up (`HkVehicleData` / client `FUN_004e8a40` /
`FUN_004e8ad0`).

---

## 7. TDD checklist (Phase 5)

Follow project TDD: failing tests first, then minimal production.

### 7.1 Unit — `VehicleDriveController`

| Test | Assert |
|------|--------|
| Golden #1 straight | thr ≈ −0.6667, steer 0, sharp 0 |
| Golden #2 hard left | thr ≈ −0.660, steer +1, sharp 0 |
| Golden #3 reverse | thr +1, steer −1, sharp 0 |
| Golden #4 high-speed sharp | thr ≈ −0.745, steer −1, sharp 1 |
| Arrival inside accept | thr 0, sharp 1, steer unchanged / not required |

### 7.2 Unit — `NpcVehiclePhysicsController` (with mock / stub sim if full Phase 4 incomplete)

| Test | Assert |
|------|--------|
| Physics disabled | does not change hard result (or is never called) |
| Wait dwell | pose frozen, HasDriveInputs, thr 0 |
| Happy path stub | HasDriveInputs true; thr/steer from controller; pos from sim |

### 7.3 Integration — `NpcTicker`

| Test | Assert |
|------|--------|
| Defaults | hard path behavior (existing tests stay green) |
| Tier Physics + enabled | vehicle uses physics controller (spy or pose property unique to sim) |
| Physics + enabled false | no physics; hard/fallback |
| Tier Kinematic | matches current `NpcVehicleDriveController` integration tests |
| Foot creature + physics tier | still hard/soft foot motion, never vehicle physics |
| Combat pursue / flee / return | still skip path mover |
| Holding vehicle on path | PositionMask re-dirty / Y snap rules preserved |

Reset `ServerConfig.ResetToDefaults()` in test init/cleanup (mirror `ServerConfigTests`).

### 7.4 Regression suite to keep green

- `NpcTickerTests`, `NpcTickerDriveControllerIntegrationTests`
- `NpcPathPaceRegressionTests`, `NpcPathLeashTests`, `NpcFootFollowerTests`
- `ServerConfigTests`

Gate: zero **new** failures vs known baseline.

---

## 8. Explicit non-goals (later phases)

| Item | Phase |
|------|-------|
| Wire `sharpTurn` through ghost / stop discarding in `ApplyServerMove` | 6 |
| Foreign `VehicleAction` + wheelset client race (`nullWheels`) | 6 |
| Full world geometry collide beyond heightfield | planned `IVehicleCollisionQuery` extension |
| Live Launcher A/B on path 5092 | 7 / separate approval |
| Distance LOD fallback to kinematic | optional risk mitigation, not Phase 5 MVP |
| Player vehicles | out of scope (client-authoritative) |

---

## 9. File touch list (when implementing)

| File | Change |
|------|--------|
| `Npc/NpcTicker.cs` | Tier branch; physics top priority; ApplyMove snap rule for physics |
| `Npc/NpcVehiclePhysicsController.cs` | **New** |
| `Physics/Vehicle/VehicleDriveController.cs` | **New** (axes) |
| `Physics/Vehicle/VehiclePhysicsInstance.cs` | **New** (if not finished in Phase 4) |
| `Physics/Vehicle/VehicleActionSim.cs` | **New** (Phase 4 prereq) |
| `Entities/Vehicle.cs` | Lazy physics instance; optional angVel apply path |
| `Diagnostics/ServerConfig.cs` | Optional: helper `IsPhysicsMoverActive`; no yaml schema change required |
| `serverConfig.yaml` (Launcher + Sector) | Comment update for tier precedence |
| Tests under `Game.Tests/Physics/` + `Game.Tests/NpcAi/` | As §7 |

**Do not** expand scope into friction/suspension formula rewrites inside the ticker PR.

---

## 10. Risks & open decisions

| Risk | Mitigation |
|------|------------|
| Dual config (yaml vs wire levers) confuses ops | Document §5; single `ResolveVehicleMoverTier`; log active tier at startup |
| Bit-exact thr **negative** vs existing ghost heuristics that pack **positive** Acceleration | Physics tier streams retail signs; confirm client VehicleAction expects negative thr (RE says yes). Kinematic tier may keep legacy positive packing until unified. |
| `ApplyServerMove` clamps Acceleration to [−1,1] | OK for retail axes; verify clamp does not destroy −0.66 thr |
| CPU: N vehicles × N substeps / 50–100 ms tick | Profile; Phase 5 MVP no LOD; leave hook for far-player kinematic fallback |
| Missing `HkVehicleData` for a CBID | Fail closed to hard/kinematic; log once |
| `SubstepHz` vs retail split | Prefer `HkVehicleSubstep`; do not invent hybrid without a test |
| Combat path vehicles still path while engaged | Unchanged ownership: combat lunge skips path; otherwise path physics continues — match current hard/soft rules |

---

## 11. Implementation order (suggested)

1. **Phase 4 complete** enough for `VehiclePhysicsInstance.Step` to advance pose under thr/steer
   on a flat heightfield (even if some subsystems stubbed with tests).
2. `VehicleDriveController` + golden vector tests (independent).
3. `NpcVehiclePhysicsController` unit tests with real or stub instance.
4. `NpcTicker` tier resolution + integration tests (fail → implement branch).
5. Wire lever / ServerConfig reconciliation helper + startup log line
   (`tier=… enabled=…`).
6. Hand off to Phase 6 for sharp packing + sim angVel + client action prerequisites.

---

## 12. Quick reference — source anchors

| Concern | Path / address |
|---------|----------------|
| Ticker | `src/AutoCore.Game/Npc/NpcTicker.cs` |
| Config | `src/AutoCore.Game/Diagnostics/ServerConfig.cs` |
| YAML | `src/AutoCore.Launcher/serverConfig.yaml`, `src/AutoCore.Sector/serverConfig.yaml` |
| Sector tick order | `src/AutoCore.Sector/Network/SectorServer.cs` (`TickNpcs` before `Pulse`) |
| Drive axes RE | `docs/reconstruction/physics/drive-controller-spec.md` · `0x004fc650` |
| Substep RE | `docs/reconstruction/physics/0.1-step-rate.md` · `0x004d6c80` |
| Plan Phase 5 | `cheerful-swimming-reef.md` §Phase 5 |
| Porting gate | `docs/reconstruction/physics/PORTING_RULES.md` |

---

## 13. Status

| Item | State |
|------|-------|
| Phase 1 ServerConfig scaffold | **Done** (defaults retail-safe; tests green) |
| Phase 0 RE evidence | **Done** (this tree) |
| Phases 2–4 modules | **Partial** (data, substep, rigid body, several subsystems; orchestrator/instance missing) |
| Phase 5 wiring | **Not started** — this note is the plan |
| Code changes from this note | **None** (docs only) |

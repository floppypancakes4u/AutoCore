# Physics Handoff — Retail-Faithful Server-Side NPC Vehicle Physics

> **Written 2026-07-16 by the agent that ran the RE campaign (B1–B4, B6–B8) and started the
> implementation phase (C2 in progress, uncommitted).** This is a full handoff for a fresh agent
> with zero conversation context. Read it top to bottom once, then jump to **§0 "Immediate next
> action"**. Everything referenced here — file paths, line numbers, addresses, test names — was
> verified in this session; line numbers may drift slightly as you edit, always re-grep to confirm.

---

## 0. Immediate next action (start here)

1. **Finish and land C2** (suspension hardpoint impulses) — it is **implemented but uncommitted**
   in the worktree right now. See **§3 "C2 current state"** for the exact diff, what to verify,
   what to delete, and how to commit.
2. Then proceed task-by-task through the remaining plan in **§4** (C4 → C5 → C8 → C-mass → CW →
   C7 → D1 → D2 → D3 → F), each via the subagent-driven process in **§5**, each gated by the test
   baseline in **§6**.
3. The full picture of *why* this branch exists and what "faithful" means is **§1–§2**. The
   RE evidence you'll need per-task is indexed in **§7**. Cheat Engine access (if you need more
   live captures) is **§8**. Non-negotiable constraints are **§9**.

---

## 1. Mission — what this branch is and why

AutoCore is a server emulator for the game **Auto Assault** (`autoassault.exe`, 32-bit client).
NPC vehicles currently drive **kinematically** — the server authors position/rotation directly
(curvature/terrain-aligned heuristics) rather than running real physics, so they slide, snap,
hover, never take ramps, never gain air, and don't respond to collisions realistically.

**Goal:** a **bit-exact, hand-rolled .NET port** of the client's **Havok 2.3 vehicle simulation +
drive controller**, run **server-side for NPC / server-owned vehicles only** (players stay
client-authoritative), so NPC cars drive for real: grounded stance, ramps, ballistic air, wheel
spin, slope stance, real cornering with real mass and weight transfer. Enabled behind
`serverConfig.yaml` (`controllerTier: physics`), **opt-in, OFF by default** — this is a
non-negotiable rollout constraint (see §9).

**Where the branch stood before this session:** the physics *library* was ported (Phases 0–7 of an
earlier plan) — ~20 files under `src/AutoCore.Game/Physics/Vehicle/`, ~2900 tests — but
deliberately simplified in ways that are self-documented in code comments: friction cancels
*absolute* chassis velocity (not wheel-relative slip) causing a "crawl"; suspension and friction
forces apply at the chassis **center of mass** with `r×F` (torque/weight-transfer) **disabled**,
to avoid flip-explosions that occurred under an earlier incorrect inertia-axis guess; the friction
solver is a diagonal/uncoupled reduced model; mass is hardcoded to `1.0`; and the NPC controller
runs the physics sim but then **force-overwrites the body back to a kinematically-authored pose**
every tick — so the sim's output was completely discarded. Wheels also only cast against a terrain
heightfield, never hitting props/ramps/other vehicles.

**This session (before the handoff):**
- Completed the RE campaign: **B1** (friction solver, 5 live-driven scenarios), **B2** (suspension
  gScale/hardpoint/anti-sink, static+B4-corroborated), **B3** (wheel+0x88 drive-torque contact
  gate, live), **B4** (RVInertia axis pairing — found and fixed a real bug: Roll/Pitch were
  swapped in `CreateBody`), plus confirmed **real chassis mass** is available server-side
  (`SimpleObjectSpecific.Mass` == the live rigid-body mass == the vehicle's UI weight, proven on
  two different vehicles via Cheat Engine). Only **B5** (air-stab in-window recovery capture)
  remains open — it's low priority, fully documented for later in
  `docs/reconstruction/physics/B5-airstab-capture-plan.md`, and is **out of scope for this
  implementation effort** (do not attempt it as part of this handoff unless explicitly asked).
- Started the **implementation** phase: a full plan was written and approved (reproduced verbatim
  in **§2**), and **C2 (suspension hardpoint impulses) was implemented by a subagent** but the
  session was interrupted before verification/commit. See **§3**.

---

## 2. The approved plan (verbatim, from `~/.claude/plans/majestic-crunching-tower.md`)

> This is the authoritative task list. Follow it in order. The plan file itself may still exist at
> that path — prefer it as the canonical copy if the two ever diverge, but this copy is
> self-contained.

### Context (as originally written)

The branch `feature-NPC-Retail-Driving` ports the AutoAssault client's Havok 2.3 vehicle sim to run
server-side for NPC vehicles. The reverse-engineering is essentially complete (B1–B4, B6–B8 done;
only B5 air-stab recovery remains, documented for later). But the port is still the
deliberately-simplified version: friction cancels absolute chassis speed (crawl), suspension/
friction apply at the COM with r×F disabled (no weight transfer), the solver is diagonal/uncoupled,
mass is hardcoded to 1.0, and server-stability clamps cap forces/speeds. On top of that,
`NpcVehiclePhysicsController` runs the sim but force-restores the body to a kinematically-authored
pose (`:215-226`), so physics never actually moves the car. Wheels also cast against a terrain
heightfield only, ignoring props/ramps/vehicles.

This plan hardens the sim to the captured retail evidence, flips the NPC controller to be
sim-authoritative (physics-based path-following — steer toward the next waypoint, publish the sim
pose), threads in the real chassis mass (confirmed live: mass = `SimpleObjectSpecific.Mass`,
inertia = `mass × RVInertia`), and **improves (not closes)** wheel collision to also hit world
objects/vehicles — with remaining gaps heavily documented.

**Authoritative task spec:** `~/.claude/plans/crystalline-plotting-crayon.md` (Phases C/D/E/F,
per-task checklists — this is a DIFFERENT plan file, the original port's master spec; read it for
full per-task detail, it is referenced throughout). RE evidence:
`docs/reconstruction/physics/` (0.3 friction, 0.4 suspension, 0.5 wheel-collide,
`asset-mass-findings.md`, oracle goldens).

### Approach & constraints

- Work only in the worktree `C:\Users\josh\Documents\GitHub\AutoCore\.worktrees\feature-NPC-Retail-Driving`.
- **TDD, subagent-driven** (implementer → reviewer → fix → ledger line in
  `.superpowers/sdd/progress.md`), one task per commit.
- **Per-task gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj` — zero new
  failures vs baseline (1 pre-existing:
  `DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll`). Never with the
  Launcher/VS holding DLLs.
- Physics stays **opt-in** (`ServerConfig`: default `Hard`/kinematic; `Physics` tier requires
  `NpcVehiclePhysicsEnabled=true` + `controllerTier=physics`). The config flip is for testing/live
  only.
- Order matters: build the **mass-correct** physics first (C-phase), then swap in real mass, then
  flip the controller (D-phase). Un-`[Ignore]` the parity/oracle contracts as each C-task lands.
- **Execution: continuous** (user-chosen). Run C → C-mass → CW → D → F end-to-end via
  subagent-driven development, one commit per task with the per-task test gate, **without pausing
  between phases**. The only hard stops are the **Phase F live Launcher checks**, which require
  explicit user approval (the user starts the Launcher + client and drives; optional CE spot-checks
  also need a prior ask). If a task can't reach its bit-exact/parity target after reasonable
  effort, commit the best green state, mark the residual in `IMPLEMENTATION-GAPS.md`, and continue
  rather than blocking the whole run.
- **Rollout: opt-in** (user-chosen). Default stays `enabled: false` / `controllerTier: hard`;
  Phase F documents the one-line flip. Do not change the shipped default.

### Phase C — Sim hardening (all in `src/AutoCore.Game/Physics/Vehicle/`)

Do in this order; each is red-oracle → change → green, gated.

- **C2 — Suspension → hardpoint point-impulses.** ⚠️ **IMPLEMENTED, UNCOMMITTED — see §3, finish
  this first.** Replace the pass-2 COM `body.ApplyForce` (`VehicleActionSim.cs:276-284`) with
  per-wheel `body.ApplyPointImpulse(F·dt·n̂, hardpointWorld)` in retail order (evidence:
  `0.4-suspension.md`, postTick `0x64bc70`). Remove the non-retail `MaxSuspensionForce` clamp in
  `HkVehicleSuspension.cs:48-52` (+ const `HkPhysicsConstants.cs:81`), gating behind a
  `ServerConfig` safety flag defaulting **off** until Phase E is green. Tests: `SuspensionOracleTests`
  clamped-vector (`:101`, retail value it must now restore), `VehicleActionSimTests.cs:568`,
  `VehiclePhysicsStabilityTests.cs:74` (`SuspensionForce_IsClamped` → delete/convert).

- **C4 — Friction solver retail-exact (biggest lever).** In `HkVehicleFrictionSolver.cs`: extend
  `Solve` (`:372`, currently scalar `chassisInvMass` + diagonal cancel `:397-398`) to the full
  coupled model — pass the chassis **inverse-inertia tensor + per-axle jacobian arms**, build
  `J·M⁻¹·Jᵀ` (lin+ang, both bodies) with softness, invert the **coupled 2×2**, and **wire in the
  already-implemented `CircleProjection` (`:280`, the real `0x6c3f90` port) +
  `BuildCircleProjectionScales` (`:223`)** in place of `ClampFrictionCircle`. In
  `VehicleActionSim.cs`: fix `slipLong`/`slipLat` to **wheel-relative slip, not absolute chassis
  speed** (`:425` — the crawl root cause); remove the gravity-share load floor (`:466-483`; retail
  `|N|` = aggregated suspension impulse); feed real `mu0/MuSlope/MuMax` from the wheel friction
  table (drop the `MuSlope=0` placeholder at `BuildAxleInput:540`); **re-enable r×F** — apply axle
  impulses at the per-axle averaged contact points, undoing the COM-only stub (`:565`, `:614-615`).
  Un-`[Ignore]` `FrictionSolverOracleTests.PortSolve_ReproducesRetailImpulses_BitExact` (`:116`)
  and the 3 at-speed `RetailParityTests` contracts (`:290/511/665`). Validate against
  `frictionSolver_goldens.json` (5 scenarios, cb+0xc0 linear / cb+0xd0 angular / per-axle out).
  Update `HkVehicleFrictionSolverTests.cs` for the coupled model.

- **C5 — Engine pow + drive-scale contact gate.** Correct the `pow` operand order in
  `HkVehicleEngine.cs`; make `wheel+0x88` a **per-wheel contact gate** (`1.0` grounded / `0.0`
  airborne) sourced from runtime contact, moving the `tRatio` fold-in out of
  `HkVehicleData.cs:295-301` into the calcWheelTorque torque path (evidence:
  `fn_wheel_driveScale_0x88.md`, B3). Files: `HkVehicleEngine.cs`, `HkVehicleData.cs`,
  `HkWheelSetup.cs`; tests updated. Also make the engine torque LUT non-trivial if goldens require.

- **C8 — Port the ticked brake (after C4).** B8 proved brake is ticked
  (`hkDefaultBrake_update 0x64e6f0`). Wire `HkVehicleBrake.ComputeServiceBrakeTorque` (`:44`,
  currently vestigial) into the sim: pedal = reverse component of throttle axis, torque into the
  friction-solver input path, lock flag → zero wheel spin in the preUpdate stage, retail tick
  order. Ensure the D-phase driver does **not** also apply reverse-throttle deceleration
  (double-decel). New `brake_goldens.json` + `BrakeOracleTests.cs`; update `brake-spec.md`.

- **C-mass — Thread real chassis mass (NEW; after C2+C4 make physics mass-correct).** Add a `mass`
  argument to `HkVehicleData.FromVehicleSpecific` (single injection point `:215`) sourced from
  **`SimpleObjectSpecific.Mass`** (rlMass; confirmed live = weight, `asset-mass-findings.md`),
  fallback `1.0` if absent. Inertia = `mass × RVInertia` falls out (`:219-221`). Thread
  `SimpleObjectSpecific` through the `FromVehicleSpecific` call site (`HkVehicleDataCache`/
  `Vehicle.GetOrCreatePhysicsInstance`). Update tests that assumed unit mass (they should now use a
  representative real mass or assert the ratio). This must land **after** the physics is
  mass-correct (real mass rescales every force via `gScale=1/invMass=mass`); base COM (hull
  centroid) stays a documented gap.

- **C7 — Retire server-stability clamps (LAST, after Phase E green).** Remove/gate-off
  `MaxLinearSpeed`/`MaxAngularSpeed` in `HkRigidBody.Integrate` (`:140-142`, `ClampVelocity:153`) +
  consts (`HkPhysicsConstants.cs:83/85`). Delete/convert
  `VehiclePhysicsStabilityTests.Integrate_ClampsLinearAndAngularSpeed` (`:91`) and
  `SuspensionForce_IsClamped` (`:74`). Full suite + parity green.

### Phase CW — Wheel-collision improvement (NEW; independent, can run parallel to C)

Retail casts each wheel against the full Havok broadphase (terrain + world objects + movable
vehicles); the server casts terrain only. **Improve, not close.** The seam is clean:
`IVehicleCollisionQuery.CastRay` → `VehicleRayHit` already carries `IsTerrain` to discriminate, and
the sim chain is agnostic to what's hit.

- New `CompositeVehicleCollisionQuery : IVehicleCollisionQuery` (new file under
  `Physics/Vehicle/`): runs the existing `TerrainHeightfieldCollisionQuery` **and** an
  object/vehicle pass, returns the **nearest** hit. Object pass reuses `SectorMap.Grid` /
  `SpatialHashGrid.QueryRadius` (2D XZ broadphase around the wheel hardpoint) + the collidable
  predicate from `VehicleMapPropRam.IsRamEligibleMapProp` (`Combat/VehicleMapPropRam.cs`), then a
  **segment-vs-proxy-volume** test (box/sphere derived from per-CBID `Scale` +
  `VehicleSpecific.SkirtExtents`) yielding fraction/point/synthesized-normal with `IsTerrain:false`.
- Wire it into the single construction point `NpcVehiclePhysicsController.BuildCollisionQuery`
  (`:443-457`) behind a `ServerConfig` flag (default off initially). New unit tests for the
  composite query (proxy-box hit, nearest-of-terrain-vs-object, collidable filtering, miss →
  terrain).
- **Heavily document the residual gaps** (see "Documented gaps" below): no true hull geometry
  (physics.glm `.cache`/`.tk` never parsed), only a scalar `Scale`+`SkirtExtents` for bounds,
  synthesized (not real) contact normals, 2D grid has no per-object height/extent.

### Phase D — Authority flip (`NpcVehiclePhysicsController`, after C1–C6 + parity green)

- **D1 — Tests first.** In `NpcVehiclePhysicsControllerTests.cs`: keep
  `Apply_CreatesPhysicsInstance...` (`:127`), `Apply_WaitHold...` (`:155`),
  `Apply_NoCloneBase...` (`:179`), `ExtractBasis...` (`:200`); rewrite `Apply_PathCruise...`
  (`:212`) and `Apply_VelocityAlignsWithFacing...` (`:255`) for sim-driven motion (loosened
  tolerances, assert progress + bounded lateral slip); **delete the 12 `IntegrateVertical_*`
  tests** (`:301-601`); add `Apply_PublishesSimPoseVerbatim`, `Apply_SpawnSeatsOnTerrain`,
  `Apply_RecoversWhenBodyFallsOutOfWorld`, `Apply_DivergenceFromPath_TeleportsAndReGrounds`. In
  `VehiclePhysicsStabilityTests.cs` delete the soft-pull/ride-height tests.
- **D2 — Controller rewrite (572 → ~250 lines).** Delete `IntegrateVertical` (`:256-321`),
  `TrySampleFrontRear` + pitch-from-probes (`:326-366`, `:143-163`), the **force-restore block**
  (`:215-226`) and the pre-Step authored-pose write beyond a first-create seed (`:198-210`), the
  dead `SoftPull*` trio (`:470-519`), `ResolveFootprint`/`ResolveRideHeight`/`PitchFromQuaternion`.
  New `Apply` flow: guards + wait-hold → recovery (first-create → `ReGround`; non-finite /
  `PosY < supportY−50` / `|body.Pos − hard.NewPosition| > ResyncDriftThreshold` → `SetPose(hard)` +
  `ReGround`) → aim via `NpcVehicleDriveController.ResolveLookAheadAim` (`:249`) → axes via
  `VehicleDriveController.ComputeAxes` (the retail `0x4fc650` port) **fed from `inst.Body`
  basis/velocity** → `inst.Step(thr, steer, sharp, dt, query)` → **publish `inst.Body`
  pose/rot/vel/angVel verbatim**; thr/steer/sharp = axes. Delete the "not retail" `Path*`
  soft-pull constants (`HkPhysicsConstants.cs:87-112`), keep `TerrainCastWorldDownDot`.
- **D3 — Lifecycle wiring.** Hook `Vehicle.ClearPhysicsInstance()` (`Vehicle.cs:770`, currently
  **zero call sites**) into teleport/`SetPosition`, respawn, and death (`OnDeath:2251` + the
  `ApplyServerMove`/`Position`-setter reposition paths) so a discontinuous reposition drops stale
  sim state (test: teleport → next `Apply` recreates + `ReGround`s). Verify `NpcTicker` wait-hold
  ghost-dirty branch and `ForeignNpcDriverWire`/ghost packing still behave (they already stream
  thr/steer/sharp/angVel).

### Phase E — Parity contracts (already written; gate the C-phase)

`RetailParityTests.cs` exists; un-`[Ignore]` the at-speed turn/downhill/ramp contracts as
C2/C3✓/C4 land. These + the oracle goldens are the acceptance gate before the D flip and before C7.

### Phase F — Verification & rollout

1. Per-task gate every commit (zero new failures vs baseline).
2. Config flip for live runs: `src/AutoCore.Launcher/serverConfig.yaml` +
   `src/AutoCore.Sector/serverConfig.yaml` →
   `npcVehiclePhysics: { enabled: true, controllerTier: physics }` (rollback = `kinematic`);
   rebuild the Launcher from the worktree.
3. **Live Launcher checklist (each run needs explicit user approval; user starts Launcher+
   client):** flat cruise, banked/constant-radius turn, ramp (natural lip launch + ballistic arc +
   landing settle), cliff drop, curb/step (anti-sink), high-speed S-curve — A/B vs `kinematic`,
   with the new object-collision flag on/off.
4. Whole-branch code review; update `docs/reconstruction/physics/README.md` +
   `PHASE_2_4_COMPLETION.md`; update project memory `retail-npc-driving-branch.md`.

### Documented gaps (write these up explicitly — user requirement)

Create/extend `docs/reconstruction/physics/IMPLEMENTATION-GAPS.md` (and cross-link from
`remainingBackgroundWork.md`) covering:
- **B5** — air-stab in-window corrective impulse + recovery goldens (plan already in
  `B5-airstab-capture-plan.md`).
- **Wheel-collision channels** — the composite query uses **proxy volumes, not real hulls**:
  (a) no physics.glm `.cache`/`.tk` hull loader; (b) only scalar `Scale` + `SkirtExtents` for
  bounds (no per-object AABB/OBB); (c) synthesized contact normals for object hits; (d) 2D grid
  has no per-object height/extent; (e) movable vehicle-vs-vehicle wheel contact is approximate.
  Note the retail path (`TtPhantom::castRay 0x580ed0` per-shape `hkShape::castRay`) as the
  fidelity target.
- **Base center of mass** — asset hull centroid (`physics.glm`) not parsed; only the
  `CenterOfMassModifier` delta is applied. Method known (`asset-mass-findings.md`).
- **Friction-solver edge cases** — the 5 captured goldens cover common states; unusual regimes may
  need further captures (C4 will surface them).
- **Transmission / gear + full engine LUT** — present but vestigial; RPM/gear→torque coupling not
  wired if goldens later demand it.

### Verification (how to test end-to-end)

- **Unit/oracle:** each C/CW/D task un-`[Ignore]`s or adds oracle tests; run
  `dotnet test ... --filter <Class>` while iterating, full suite before commit.
- **Parity:** `RetailParityTests` at-speed contracts must go green as C4/C2 land (the analytic
  ballistic oracle catches gravity/lift errors).
- **Bit-exact:** `FrictionSolverOracleTests.PortSolve` + `SuspensionOracleTests` clamped vector
  reproduce the live retail goldens.
- **Live (user-approved):** the Phase F Launcher checklist, A/B vs kinematic, physics debug
  logging on. Optional CE spot-checks of `inst.Body` vs the live client for a matched maneuver
  (ask before using Cheat Engine; user drives).

---

## 3. C2 current state — FINISH THIS FIRST

**Status: implemented by a subagent, session interrupted before verification/commit.** The working
tree in the worktree has these **uncommitted** changes (verified via `git status`/`git diff` at
handoff time):

```
 M src/AutoCore.Game.Tests/Physics/Vehicle/VehicleActionSimTests.cs
 M src/AutoCore.Game.Tests/Physics/Vehicle/VehiclePhysicsStabilityTests.cs
 M src/AutoCore.Game.Tests/Physics/oracles/SuspensionOracleTests.cs
 M src/AutoCore.Game/Diagnostics/ServerConfig.cs
 M src/AutoCore.Game/Physics/Vehicle/HkPhysicsConstants.cs
 M src/AutoCore.Game/Physics/Vehicle/HkVehicleSuspension.cs
 M src/AutoCore.Game/Physics/Vehicle/VehicleActionSim.cs
?? src/AutoCore.Game.Tests/Physics/Vehicle/TempC2DiagnosticTests.cs   <-- SCRATCH FILE, DELETE
```

**What was changed (reviewed and looks correct):**

1. **`VehicleActionSim.cs`** — pass 2 of `ApplyWheelCollideAndSuspension` now does, per grounded
   wheel:
   ```csharp
   var wheel = inst.Wheels[i];
   body.ApplyPointImpulse(
       forceX[i] * dt, forceY[i] * dt, forceZ[i] * dt,
       wheel.ContactPointX, wheel.ContactPointY, wheel.ContactPointZ);
   ```
   replacing the old `body.ApplyForce(forceX[i], forceY[i], forceZ[i])` COM-only stub. Note it uses
   `wheel.ContactPointX/Y/Z` (the **actual cast hit point**, set earlier in pass 1 at
   `originX/Y/Z + down*maxDist*fraction`, lines ~226-228) rather than the raw hardpoint — this is
   *more* correct than the plan's literal wording ("hardpoint") since retail's `wheel+0x20` is the
   contact point, not the static hardpoint. Verify this reasoning holds (cross-check
   `0.4-suspension.md` if in doubt) before trusting it blindly.
   Doc comments were updated accordingly (class remarks + method doc).

2. **`HkVehicleSuspension.cs`** — the `MaxSuspensionForce` clamp in `ComputeForce` is now gated:
   ```csharp
   if (ServerConfig.SuspensionForceClampEnabled)
   {
       if (force > HkPhysicsConstants.MaxSuspensionForce) force = HkPhysicsConstants.MaxSuspensionForce;
       else if (force < -HkPhysicsConstants.MaxSuspensionForce) force = -HkPhysicsConstants.MaxSuspensionForce;
   }
   ```
   (previously unconditional). Added `using AutoCore.Game.Diagnostics;`.

3. **`ServerConfig.cs`** — new `SuspensionForceClampEnabled` bool property (default
   `DefaultSuspensionForceClampEnabled = false`), wired into `ResetToDefaults()` and the YAML DTO
   (`ApplyFromYaml`'s `ServerConfigDto.SuspensionForceClampEnabled`). This is the "safety flag
   defaulting off" the plan calls for.

4. **`HkPhysicsConstants.cs`** — doc comment on `MaxSuspensionForce` updated to note it's now only
   used when the flag is on. Constant value unchanged (still `80f`).

5. **Test updates** (all reviewed, look correct and well-reasoned):
   - `SuspensionOracleTests.cs` — reworked around the new gating: default (flag off) path must
     match `expectedRetail` bit-exact even for the "clamp-active" golden vector (was previously
     asserting the clamped value); a new test flips the flag on and asserts the old clamped
     behavior is preserved; a new test confirms flag-on doesn't disturb non-clamping vectors.
   - `VehicleActionSimTests.cs` — the existing `ApplyAction_KnownSuspensionForceAndDt_ProducesExpectedDeltaV`
     test was extended to predict and assert the **r×J pitch response** (`AngVelX`) from a
     front/rear spring-strength mismatch — a COM-only application would leave this at zero; the
     hardpoint-impulse version must show a specific nonzero value (computed independently in the
     test via `−rz · Jy · InvInertiaX`, then AVD-damped once). Two **new** focused tests were added:
     `ApplyAction_SymmetricEqualSuspension_LiftsWithoutAngularVelocity` (mirrored equal springs →
     pure lift, zero angular velocity on all axes) and
     `ApplyAction_AsymmetricSuspension_ProducesPitchAngularVelocity` (front springs 4× stiffer than
     rear → must produce `AngVelX < -0.1`, i.e. real weight-transfer pitch).
   - `VehiclePhysicsStabilityTests.cs` — `SuspensionForce_IsClamped` renamed to
     `SuspensionForce_ClampsOnlyWhenSafetyFlagEnabled`; asserts unclamped by default and clamped
     when the flag is forced on (restored via `finally`).

6. **`TempC2DiagnosticTests.cs`** (untracked, **must be deleted**) — a scratch diagnostic the
   subagent used to investigate a downhill bounce-oscillation concern (histogram of height-delta
   sign flips over 300 frames on a slope). It ends with `Assert.Fail(...)` printing diagnostic
   numbers — **this is not a real test and will fail the suite if left in.** Delete this file
   before running the gate.

**What is NOT yet verified (do this now):**

- [ ] Delete `src/AutoCore.Game.Tests/Physics/Vehicle/TempC2DiagnosticTests.cs`.
- [ ] Run the full test suite (`dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`,
  10-min timeout) and confirm **zero new failures vs baseline** (see §6 for the exact baseline).
  Pay special attention to:
  - `RetailParityTests` — the downhill/turn/ramp `[Ignore]`d contracts should **stay** ignored
    (C2 doesn't unblock them, C4 does) — but check the **non-ignored** downhill/stability tests
    still pass; the diagnostic scratch file's existence suggests the subagent was worried about
    oscillation on slopes from the new hardpoint impulses. If there IS a new oscillation/instability
    surfacing in a non-ignored test, investigate before committing — it may indicate the
    contact-point-vs-hardpoint choice needs revisiting, or a genuine r×F stability issue that also
    affects C4 (both apply impulses off-COM now).
  - `VehiclePhysicsStabilityTests.GroundedDrive_StaysNearTerrain_NoFlipExplosion` and
    `GroundedDrive_ProducesForwardMotion_NotLaunch` — these guard against exactly the kind of
    flip-explosion the COM-only stub was originally added to avoid. If C2 breaks these, that's a
    serious signal — do not silently paper over it; investigate the r×F stability at unit mass
    (real mass hasn't landed yet — C-mass is later in the plan) before committing.
  - `FrictionSolverOracleTests` and `SuspensionOracleTests` should all pass (suspension oracle was
    explicitly reworked to match).
- [ ] If green: commit with message
  `fix(physics): apply suspension as retail hardpoint point-impulses; gate clamp (C2)` +
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- [ ] Append a ledger line to `.superpowers/sdd/progress.md` (see format in §5).
- [ ] Mark task tracking as C2 complete (see §10 for the task list to keep in sync, if your
  environment has a persistent task tool — this session used one internally; recreate the list
  from §10 if your tool starts empty).

If instead the tests reveal a real regression (flip, oscillation, NaN), do NOT just revert to the
COM stub — that would abandon the whole point of C2. Instead: read `0.4-suspension.md` again,
check whether the retail contact-point vs. hardpoint distinction matters, check whether the
oscillation is a symptom that the friction solver (still COM-only until C4) is now fighting the
suspension's new torque in a way that needs C4 to land first (in which case, note this as a
documented ordering dependency, get C2's *non-oscillating* tests green, and proceed — the
oscillating parity contracts are `[Ignore]`d until C4 regardless).

---

## 4. Remaining task list (after C2 lands)

Execute in this order (dependencies matter — see plan text in §2 for full detail per task):

1. ~~C2~~ (finish per §3)
2. **C4** — friction solver retail-exact. **Biggest fidelity lever.** See §2 for full detail;
   see §7.1 for the friction-solver-specific evidence and current-code map.
3. **C5** — engine pow + wheel+0x88 contact gate.
4. **C8** — port ticked brake (after C4).
5. **C-mass** — thread real chassis mass (after C2 + C4).
6. **CW** — composite wheel-collision query (can run any time after C2, independent of C4/C5/C8).
7. **C7** — retire server-stability clamps (LAST of the C-phase, after Phase E parity is green).
8. **D1** — controller tests for sim authority.
9. **D2** — controller rewrite (sim-authoritative).
10. **D3** — lifecycle wiring (`ClearPhysicsInstance`).
11. **F** — verification, `IMPLEMENTATION-GAPS.md`, live Launcher checklist (needs user).

---

## 5. Execution process (subagent-driven development)

This is how the RE campaign and C2 were executed; keep using it:

1. For each task, dispatch a **fresh implementer subagent** (`Agent` tool, `general-purpose` type,
   NOT a fork — it needs zero prior context, just this handoff's relevant section) with:
   - The specific task's evidence (from §2/§7).
   - Exact current file:line references (re-grep first, they may have drifted).
   - Explicit TDD instructions: write/adjust tests first, watch red, implement, watch green.
   - The test-gate command and the exact baseline (§6) to check against.
   - Instruction to commit with a clear conventional message + the `Co-Authored-By` footer, and to
     write a report file to `.superpowers/sdd/task-<NAME>-report.md`.
   - **Do NOT run two implementer subagents in parallel in the same worktree** — they will race on
     git commits.
4. Once it reports back (async — you get a notification, do not poll or guess), review its diff
   yourself: `git log --oneline`, `git diff <base>..<head>`, re-run the full suite yourself to
   double check the gate, read the report file. Dispatch a **task reviewer** subagent for
   non-trivial tasks (C4 especially) with spec-compliance + code-quality scrutiny.
5. If Critical/Important findings: dispatch a fix subagent, re-review.
6. Append a **one-line completion note** to `.superpowers/sdd/progress.md` (gitignored local
   scratch, but the authoritative "what actually happened" ledger — trust it + `git log` over any
   memory) in this format (see the file for many real examples):
   ```
   Task <NAME>: complete (commit <sha>, review <verdict>). <2-4 sentences: what changed, key
   findings, what it unblocks>. <Minor roll-ups if any>.
   ```
7. Move to the next task.

For the largest task (**C4**), consider splitting the implementer brief into the RE-evidence
digest (§7.1) as its own file the subagent reads first, since it's long.

---

## 6. Test baseline & gate

**Build/test command** (run from the worktree root, 10-min timeout — full suite takes several
minutes):
```
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj
```
Filter while iterating: `--filter <TestClassName>` or `--filter FullyQualifiedName~<substr>`.

**⚠️ A running AutoCore Launcher or Visual Studio instance locks `AutoCore.Launcher` DLLs and
breaks the build** — always test the `AutoCore.Game.Tests` project in isolation (the command above
already scopes to it).

**Baseline (as of `54abfc59`, before C2): 2921 passed, exactly 1 pre-existing failure, 5 skipped
(0 new since branch base):**
```
AutoCore.Game.Tests.Managers.DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll
```
This failure is inherited from `master` and is **not** caused by this branch — never chase it, just
confirm it's the *only* one.

**Gate rule: zero new failures vs this baseline, every single commit.** The 5 skipped tests at
baseline are the intentional `[Ignore]`s (`FrictionSolverOracleTests.PortSolve...`, the 3 at-speed
`RetailParityTests` contracts, possibly one more — re-count when you check). As C4/C2 land, some of
these flip from skipped→passing; that's expected and good. Never *add* new `[Ignore]`s without a
documented reason in the code comment + this handoff's gap list.

To find the exact failing test name after a run: use a trx logger and grep, e.g.
```
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj --logger "trx;LogFileName=res.trx"
grep -oE '<UnitTestResult[^>]*outcome="Failed"[^>]*/?>' src/AutoCore.Game.Tests/TestResults/res.trx | grep -oE 'testName="[^"]*"'
```

---

## 7. RE evidence index (per subsystem)

All reconstruction docs live under `docs/reconstruction/physics/`. Read the specific doc(s) for
whichever task you're on — they are the ground truth, more authoritative than this handoff's
summaries.

| Doc | Covers | Relevant task |
|---|---|---|
| `0.1-step-rate.md` | Substep/frame-rate handling | (background) |
| `0.2-mass-inertia.md` | Mass/inertia/COM; **has the "CORRECTION" box + §2.1 B4 live proof** (`I = mass × RVInertia`, axis pairing X←Pitch/Y←Yaw/Z←Roll) | C-mass, C1 (done) |
| `0.3-friction-solver.md` | Friction solver math, "Live capture (Task B1...)" section with the 5-scenario goldens summary and the resolved long/lat binding, `circleProjection` decompile | **C4** |
| `0.4-suspension.md` | Suspension formula, gScale, **"Task B2 — hardpoint impulse CONFIRMED"** section (the exact decompile C2 implements), anti-sink confirmation | **C2**, C3 (done) |
| `0.5-wheel-collide.md` | Retail wheel cast = Havok broadphase raycast (terrain + world + movable bodies), wheel struct fields | **CW** |
| `0.6-aerodynamics.md` | Aero (already ported, bit-exact, not in scope) | — |
| `0.7-transmission.md` | Transmission (vestigial, only touch if C5 goldens demand it) | C5 (maybe) |
| `0.8-struct-offsets.md` | Global struct offset map | reference |
| `asset-mass-findings.md` | **The confirmed real-mass finding** — full write-up incl. the two-vehicle live cross-check (Callisto X 1500kg→1500, Astimiax 900 2900kg→2900), the sequencing caveat (land WITH C-phase, not alone) | **C-mass** |
| `brake-spec.md` | Brake mechanism (B8: brake IS ticked, not vestigial — chain and formula) | **C8** |
| `drive-controller-spec.md` | `VehicleDriveController`/`0x4fc650` drive-axes math | D2 |
| `engine-torque-spec.md` | calcWheelTorque details | C5 |
| `steering-spec.md` | Steering (already ported) | — |
| `setup-field-mapping.md` | VehicleSpecific→HkVehicleData field mapping | reference |
| `B5-airstab-capture-plan.md` | Full self-contained plan for the deferred B5 task | OUT OF SCOPE here |
| `verified/fn_wheel_driveScale_0x88.md` | wheel+0x88 = contact gate (B3 finding) | **C5** |
| `verified/fn_00598040_uprightPow.md` | calcWheelTorque pow operand order (already resolved) | C5 |
| `PORTING_RULES.md` | General porting/RE methodology rules | reference |
| `README.md` | Phase index/status | update in F |
| `PHASE_2_4_COMPLETION.md` | Residuals from the original port phases | update in F |

### 7.1 — C4 (friction solver) current-code map — from this session's exploration

This is the exact state of the friction solver code **before C2/C4**, captured via a thorough
codebase exploration this session (verify line numbers haven't drifted, but the structure/logic
description is accurate):

**`src/AutoCore.Game/Physics/Vehicle/VehicleActionSim.cs`:**
- `TryApplyFriction` at line ~401 (called ~line 107, before aero/AVD).
- **`slipLong`/`slipLat` (~line 425) use ABSOLUTE chassis velocity**, not wheel-relative slip:
  ```csharp
  float slipLong = body.LinVelX * fwdX + body.LinVelY * fwdY + body.LinVelZ * fwdZ;
  float slipLat  = body.LinVelX * rightX + body.LinVelY * rightY + body.LinVelZ * rightZ;
  ```
  This is THE root cause of the historical "crawl" — the solver's diagonal cancel drives chassis
  speed toward ~0 every tick regardless of throttle, because it's cancelling absolute speed, not
  slip relative to the wheel's own rotation.
- **Gravity-share normal-load floor** (~lines 466-483):
  ```csharp
  float suspForce = HkVehicleSuspension.ComputeForce(...);
  float gravityShare = body.Mass * MathF.Abs(data.GravityY) / Math.Max(1, data.WheelCount);
  float load = MathF.Max(MathF.Abs(suspForce), gravityShare);
  ```
  Retail's `|N|` = the aggregated suspension impulse, no floor. Remove this once C2's hardpoint
  suspension is confirmed stable.
- **Placeholder μ** in `BuildAxleInput` (~line 540): `Mu0 = mu0` (axle-averaged), **`MuSlope = 0f`**
  (no slip-dependent curve), `MuMax = mu0>0 ? mu0 : 1f`. Retail has a real slip-dependent linear μ
  curve (see `0.3-friction-solver.md` Part D "Friction limit from tire load").
- **`ApplyAxleImpulses` (~line 565)** is the "reduced model, no r×F" stub — contact points ARE
  summed/averaged (~lines 584-605) but then discarded:
  ```csharp
  // Reduced model: apply full axle impulse at COM only (no r×F). ...
  body.ApplyPointImpulse(jx, jy, jz, body.PosX, body.PosY, body.PosZ);  // r = 0 !
  ```
  This needs to become a per-axle averaged **contact point** (using the already-computed
  `sumX/sumY/sumZ` — they're just unused right now, marked `_ = sumX; ...` as discards).

**`src/AutoCore.Game/Physics/Vehicle/HkVehicleFrictionSolver.cs`:**
- `AxleFrictionInput` struct (line 8) already has `InvKeffLong`/`InvKeffLat` slots (optional
  overrides for the effective-mass terms) — **these exist for exactly the C4 upgrade**, currently
  unused (`Solve` derives them as `1/chassisInvMass` when unset, a unit-jacobian linear
  approximation — see `ResolveInvKeff` ~line 447).
- `Solve` (line 372) loops 2 axles independently; **diagonal-only cancel** (~lines 397-398):
  ```csharp
  float impLong = -invKeffLong * input.SlipLongitudinal;
  float impLat = -invKeffLat * input.SlipLateral;
  ```
  No off-diagonal coupling (comment explicitly says "Full solve: (impLong,impLat) = -Minv2·(Jv_long,Jv_lat)
  with coupled 2×2" — not yet done).
- Drive-pack bias (~lines 407-419) and slip-μ + clamp (~lines 421-433) are implemented in the
  reduced form; **`ClampFrictionCircle` is used, but a REAL, already-implemented, already
  unit-tested `CircleProjection` (line ~280) + `BuildCircleProjectionScales` (~line 223) exist
  and are simply NOT WIRED IN.** This is good news for C4 — you're mostly wiring existing correct
  code, not writing new circle-projection math from scratch. `CircleProjection` is the actual
  `0x6c3f90` port (iterative ≤16-step friction-ellipse boundary projection over a per-axle LUT —
  see `0.3-friction-solver.md` "Live capture" section, item 3).
- The class XML doc (lines ~83-113) is a self-maintained, accurate checklist of what's ported vs.
  not — read it, it enumerates exactly the C4 residual gaps (full J·M⁻¹·Jᵀ Phase A assembly,
  coupled 2×2 Phase B invert, Phase C/D body-impulse writeback, the caller's 1/mag² pre-scale +
  dual-axle ordered circleProjection with couple feedback, `FUN_006c4150` scale-table product,
  `cb+0xa0` max-slip/airborne lateral zeroing, setup mix0/mix1 gains).

**`src/AutoCore.Game/Physics/Vehicle/HkRigidBody.cs`:**
- `ApplyPointImpulse(jx,jy,jz, pointX,pointY,pointZ)` (~line 85) already exists and does
  `Δv=J·InvMass`, `Δω=InvInertia·(r×J)` with **diagonal** InvInertia — this is what both C2 and C4
  will call. `ApplyForce`/`ApplyTorque` (accumulators) at ~65/~73. `Integrate` (~115) with the C7
  clamps at ~140-142/`ClampVelocity` ~153.

**Live-captured ground truth** (already in the repo, from B1): `frictionSolver_goldens.json` — 5
matched snapshots (rest/launch/cruise/turn/slide) of the retail solver's `setup`/`cb`/`out`
structs, captured at the exact solver-call return address in `postTick` (`0x64c9b2`). The
**key discriminating signature already asserted in `FrictionSolverOracleTests`**:
`out[0]`/`out[2]` (the per-axle friction writebacks) are **exactly zero under grip** (rest, launch,
cruise, turn) and **only become nonzero when the friction circle saturates** (the handbrake slide:
`out[0]=-26.7, out[2]=473.6`). This is the acceptance signature C4's ported solver must reproduce
via `FrictionSolverOracleTests.PortSolve_ReproducesRetailImpulses_BitExact` (currently
`[Ignore]`d + `Assert.Inconclusive`).

### 7.2 — D-phase (controller) current-code map — from this session's exploration

**`src/AutoCore.Game/Npc/NpcVehiclePhysicsController.cs`** (572 lines) — the kinematic hybrid to
invert. Class doc (~lines 12-24) admits it outright: planar nav is facing-aligned kinematic
integration, vertical is a heuristic stick-vs-ballistic model; **the sim runs but its output is
discarded.**

- Main entry `Apply(...)` ~lines 34-248.
- Guard/fail-closed ~43-55; wait-hold branch ~57-76; `MaybeResyncSim` call ~78 (defined ~521-531).
- Aim + kinematic planar nav ~80-104: `ResolveLookAheadAim` (~81, from
  `NpcVehicleDriveController.cs`), `ResolveDesiredYaw` (~86), `LimitYaw` (~88),
  `IntegrateFacingPosition` (~103) — **pure kinematic yaw+nose integration, not physics.**
- Vertical ~110-139 (`const float ride = 0f;`, calls `IntegrateVertical` ~124-137 — **heuristic,
  delete in D2**).
- Pitch-from-probes ~143-163 (`TrySampleFrontRear` ~145 — **heuristic, delete in D2**).
- Velocity assembled from yaw+speed ~166-176.
- Drive axes via `VehicleDriveController.ComputeAxes` ~179-196 — **this call survives into D2**,
  but its inputs (`right`/`forward`/`velocity`) currently come from the **authored** kinematic
  pose, not `inst.Body` — in D2 these must come from the sim body after `Step`.
- `inst.Step(...)` call at ~line 213.
- **THE KEY BLOCK — force-restore, lines ~198-210 (pre-Step pose write) and ~215-226 (post-Step
  force-restore back to the authored pose):** comment literally reads *"Force body back to
  authored pose (physics must not hop us off the path)."* This is what makes physics currently a
  no-op for pose purposes. **D2 deletes this** and instead publishes `inst.Body` verbatim.
- `IntegrateVertical` ~256-321 (delete), `TrySampleFrontRear` ~326-366 (delete),
  `SoftPull*` trio ~470-519 (**dead code, zero call sites already** — just delete, no test impact),
  `ResolveFootprint`/`ResolveRideHeight`/`PitchFromQuaternion` ~372-420 (delete unless still needed
  for the collision-query footprint; `ResolveTerrainSupportY`/`BuildCollisionQuery` ~422-457
  survive — they feed the collision query).

**`src/AutoCore.Game/Physics/Vehicle/VehicleDriveController.cs`** (131 lines) — the retail
`0x4fc650` port. Single method `ComputeAxes(position, right, forward, velocity, aim, acceptDist,
cruiseScale, allowReverse, alwaysDrive=false)` → `(Throttle, Steer, Sharp)`. Pure function, no pose
writes. **Already wired into the controller correctly** (just needs its *inputs* to come from the
sim body post-D2, not the authored pose).

**`src/AutoCore.Game/Npc/NpcVehicleDriveController.cs`** (343 lines) — the kinematic look-ahead
controller. `ResolveLookAheadAim(position, hard, path, laneOffset)` (~lines 249-287) is **the
waypoint-aim logic that produces the target direction** — walks path points from `hard.NewIndex`
accumulating segment length until `LookAheadDistance` is consumed, then interpolates. **This
already exists, is already reused by the physics controller, and survives into D2 unchanged** —
it's exactly the "steer toward the next waypoint" mechanism the user wants; D2 just needs to feed
its *output* into `ComputeAxes` using the sim body's position/basis instead of the authored
kinematic ones.

**`src/AutoCore.Game/Physics/Vehicle/VehiclePhysicsInstance.cs`** (160 lines) — the sim entry:
`Step(throttle, steer, handbrake, frameDt, query)` (~65-77), `SetPose(...)` (~80-93, teleport +
zero velocity), `ReGround(query)` (~102-137, already built in a prior task — casts down from
`PosY+10`, snaps origin, zeros motion, clears drive axes, resets wheel state).

**`src/AutoCore.Game/Npc/NpcTicker.cs`** (263 lines) — per-frame tick. `wasHolding` latch (~55);
tier selection via `ServerConfig.ResolveVehicleMoverTier()` (~84) dispatches to
`NpcVehiclePhysicsController.Apply` for the `Physics` tier (~86-89); publish branch (~134-157)
calls `ApplyMove` (~214-247) which publishes `result.NewPosition` **verbatim** — so once D2 makes
the controller return the sim's real pose, this path requires **no changes**, it already just
forwards whatever the controller returns.

**`src/AutoCore.Game/Entities/Vehicle.cs`** — `ClearPhysicsInstance()` at **line 770**, confirmed
**zero call sites anywhere in `src/`** via repo-wide grep. `OnDeath(DeathType)` at ~line 2251 is a
candidate hook. Teleport/respawn are not discrete methods — they route through `ApplyServerMove`
overloads (~652/660/674) or the `Position` setter; D3 needs to find/add a hook at whichever
discontinuous-reposition call site actually exists for NPCs (may need to add one if none exists
today beyond the controller's own `MaybeResyncSim` drift-based resync).

**`src/AutoCore.Game/Diagnostics/ServerConfig.cs`** — `NpcVehicleControllerTier` enum (line ~12):
`Hard=0, Soft=1, Kinematic=2, Physics=3`. `ResolveVehicleMoverTier()` (~94-111) returns `Physics`
**only when both** `NpcVehiclePhysicsEnabled==true` **and** `ControllerTier==Physics`. Defaults:
`DefaultNpcVehiclePhysicsEnabled = false`, `DefaultControllerTier = Hard` — **do not change these
defaults**, that's the opt-in rollout constraint.

**Tests — `src/AutoCore.Game.Tests/NpcAi/NpcVehiclePhysicsControllerTests.cs`** (650 lines): tests
7-18 (`IntegrateVertical_*`, ~lines 301-601) all target the heuristic vertical integrator being
deleted — delete them in D1. No `SoftPull*` tests exist (the helpers are already dead/untested).
Tests 1-6 (`Apply_CreatesPhysicsInstance...` ~127, `Apply_WaitHold...` ~155,
`Apply_NoCloneBase...` ~179, `ExtractBasis...` ~200, `Apply_PathCruise...` ~212,
`Apply_VelocityAlignsWithFacing...` ~255) need review per D1's plan (keep 4, rewrite 2 for
sim-driven tolerances).

### 7.3 — CW (wheel collision) current-code map + honest gap assessment — from this session's exploration

**`src/AutoCore.Game/Physics/Vehicle/IVehicleCollisionQuery.cs`** — the interface. `CastRay(originX,Y,Z,
dirX,Y,Z, maxDistance, out VehicleRayHit hit)`. `VehicleRayHit` (readonly struct) **already has an
`IsTerrain` bool field** ("True if hit terrain heightfield; false if static/dynamic geometry body")
— the seam for CW was anticipated in the original design. The interface doc-comment literally says
*"world collision geometry can be plugged in later without changing suspension/friction math."*

**`src/AutoCore.Game/Physics/Vehicle/TerrainHeightfieldCollisionQuery.cs`** — the current
terrain-only implementation. Samples height under the ray origin, intersects a horizontal plane at
that height (not true geometry tracing), one refinement pass for sloped rays, always returns
`IsTerrain: true`.

**Construction point (the ONE place to wire in a new query):**
`NpcVehiclePhysicsController.BuildCollisionQuery(SectorMap map, float fallbackGroundY)` at
~lines 443-457 — pulls `map?.MapData?.Heightfield`, wraps it in `TerrainHeightfieldCollisionQuery`,
falls back to a flat plane. This is called from `Apply` (~lines 212-213) and passed straight into
`inst.Step(...)`. **The whole downstream chain (`Step → VehicleActionSim → HkVehicleWheelCollide.CastWheel`)
is agnostic to what's hit** — swapping the query implementation here requires zero downstream
changes.

**What's already available to build the composite query from (confirmed present):**
- **`src/AutoCore.Game/Map/SpatialHashGrid.cs`** — a working 2D (XZ-plane) uniform hash grid,
  `CellSize=128f`, exposed as `SectorMap.Grid`. `QueryRadius(center, radius, buffer)` fills a
  buffer of nearby entities. Already used for combat aggro scans. **No Y/height awareness, no
  raycast, just XZ proximity.**
- **`src/AutoCore.Game/Combat/VehicleMapPropRam.cs`** — the closest existing analog. Does
  `map.Grid.QueryRadius(vehiclePos, 10, buffer)`, filters via `IsRamEligibleMapProp` (a predicate
  checking `cb.Type` + a collidable flag bit on `GraphicsObject`s), picks the single closest prop,
  applies ram damage. **This proves the "which props are collidable" predicate + the spatial-query
  plumbing already exist** — reuse `IsRamEligibleMapProp` (or extract/generalize it) for CW's
  object pass.
- **Per-CBID bounds data that exists:** `SimpleObjectSpecific.Scale` (scalar) +
  `VehicleSpecific.SkirtExtents` (Vector3 half-extents, vehicles only). `SimpleObjectSpecific.PhysicsName`
  also exists (names a `physics.glm` entry) but **nothing parses the actual hull** — see below.
- **Per-object placement transform:** `GraphicsObjectTemplate` gives `Position`/`Rotation`/scalar
  `Scale` — no extents for non-vehicle props beyond that scalar.

**What is genuinely missing (be honest about this in the gap doc):**
- **No real collision geometry anywhere server-side.** `GLMLoader.cs` is purely an archive/stream
  reader (`Load`/`GetStream`/`GetReader`) — it does not interpret `physics.glm`'s `.cache`/`.tk`
  content. Those files were confirmed (via a Python mirror of `GLMLoader`, `tmp/re/glm.py`) to hold
  **collision hull meshes** (vertex list + triangle list `.tk`, compiled binary `.cache`) — a
  vehicle is a compound of several convex pieces (`..._sportscar-p2` through `-p11`). **Nothing
  loads/parses these; `SpawnPoint.cs` even hardcodes a foot-offset constant rather than reading the
  actual creature hull as clear evidence this was never wired up.**
  - No AABB/OBB/sphere per placed object beyond the vehicle skirt-extents case.
  - The 2D grid has no per-object height, so a wheel ray (inherently vertical) can't be resolved
    against arbitrary props without inventing a proxy volume.
  - `TtPhantom::castRay 0x580ed0` (the retail per-shape broadphase raycast, documented in
    `0.5-wheel-collide.md`) is the fidelity target this can never fully reach without a hull
    loader — a substantial separate project, correctly out of scope for CW.

**Realistic "improve, not close" design (confirmed feasible with existing pieces):** a
`CompositeVehicleCollisionQuery` that runs the terrain query, ALSO does a `SectorMap.Grid.QueryRadius`
around the wheel hardpoint filtered by a collidable predicate (generalize
`VehicleMapPropRam.IsRamEligibleMapProp`), does a segment-vs-proxy-box/sphere test (from
`Scale`/`SkirtExtents`), and returns whichever hit (terrain or object) is nearer along the ray,
with `IsTerrain: false` for object hits and a synthesized normal (e.g. box-face normal or
sphere-radial). This closes the "wheels completely ignore ramps/props/vehicles" gap
*approximately* while being honest that it's not real hull geometry.

---

## 8. Cheat Engine access (only if you need MORE live captures)

You likely won't need this for the C/D/CW implementation work — the needed captures (B1-B4) are
already done and their goldens are committed. This section is here in case C4's implementation
surfaces a genuine new ambiguity that needs a fresh capture.

**⚠️ ASK THE USER BEFORE USING CHEAT ENGINE.** This was an explicit user instruction this session
("If you need the cheat engine MCP, let me know before using it") — treat it as a standing
constraint, not a one-time answer.

- MCP server `cheatengine` is registered at **user scope** (`~/.claude.json`) — should be available
  via ToolSearch (`open_process`, `read_memory`, `set_breakpoint`, etc.) if the user has CE running
  and attached with the Lua bridge loaded.
- **Faster/more reliable: direct named-pipe access**, bypassing the MCP tool layer entirely:
  `tmp/re/ce_client.py` (in the worktree, gitignored) speaks the CE Lua bridge's framed JSON-RPC
  protocol directly over `\\.\pipe\CE_MCP_Bridge_v99`. Full protocol + method reference + gotchas
  documented in `tmp/re/CE_API_NOTES.md` — READ THIS before attempting any capture. Key gotchas
  already discovered:
  - `open_process` param is `process_id_or_name`, NOT `pid`.
  - Hardware breakpoint handlers are **async/auto-continuing** — for a one-shot snapshot at a
    specific call, capture via `evaluate_lua` with a custom Lua handler (see `tmp/re/b1c.py` for
    the working pattern: install a breakpoint via `debug_setBreakpoint(...)` in Lua that snapshots
    memory into a Lua global on hit, then `debug_continueFromBreakpoint(co_run)`).
  - The bridge's own breakpoint slot-tracking (`serverState.hw_bp_slots`) can get corrupted across
    many set/remove cycles in one session — if breakpoints silently stop firing, wipe it:
    `evaluate_lua` with `for i=0,3 do pcall(debug_removeBreakpoint,i) end; serverState.hw_bp_slots={}`
    then re-arm.
  - The game's physics tick only runs while the Auto Assault window has **focus** — if a capture
    reads zero hits, the most likely cause is the window isn't focused (not a broken breakpoint).
  - Always verify the 1:1 static memory map first: `read_memory 0x9cc798` should be `FF FF EF 41`
    (float 29.9999998), `0xaf3388` should be `00 00 A0 41` (float 20.0) — base `0x400000`, no ASLR,
    Ghidra address == live address.
  - **Always clean up breakpoints when done** (`clear_all_breakpoints` + explicit
    `debug_removeBreakpoint` for any custom Lua-installed ones) so you don't leave the game with
    stale hardware breakpoints.

---

## 9. Non-negotiable constraints

- Physics tier stays **opt-in** (`controllerTier` OFF by default = `Hard`; instant rollback path =
  `kinematic`). Never flip the shipped default.
- **TDD** for all production physics changes. Zero new test failures vs the baseline in §6, every
  commit.
- **No Launcher start/stop and no game-attach without user approval.** CE use requires asking
  first (§8). The Phase F live checklist explicitly requires the user to start the Launcher +
  client themselves and drive.
- Mass model is transitioning: **currently mass=1.0/inertia=RVInertia (unit-mass model)**; C-mass
  makes it **real mass from `SimpleObjectSpecific.Mass`, inertia = mass × RVInertia** — confirmed
  correct via live cross-check, see `asset-mass-findings.md`. Do NOT skip straight to real mass
  before C2+C4 land (sequencing caveat in §2/§7 — real mass rescales every force, would destabilize
  the still-unit-mass-tuned sim).
  Base COM (asset hull centroid) stays an accepted, documented gap — only the
  `CenterOfMassModifier` delta is applied.
  Wheel `+0x88` gates by contact, not tRatio (see C5).
- Retail has **no soft-pull / ramp-lip / launch-Vy** — never reintroduce heuristic vertical terms
  (this is exactly what D2 removes; don't add anything similar elsewhere).
- Ghidra is READ-ONLY except bookmarks/comments (no renames/retypes). Never attach Ghidra's
  debugger to the game (it freezes the game in this environment — Cheat Engine is the working
  alternative, see §8).
- Keep `CE_MCP_ALLOW_SHELL` unset if you touch the CE bridge config.
- Work only in the worktree, never the main checkout
  (`C:\Users\josh\Documents\GitHub\AutoCore` is on `master` with unrelated WIP — do not touch it).

---

## 10. Task list to recreate (if your environment has a persistent task tool)

This session tracked progress with a task list. If your tool starts fresh, recreate it in this
state (C2 in_progress/needs finishing, everything after it pending, B5 explicitly out of scope):

| # | Subject | Status |
|---|---|---|
| 1 | C2: Suspension → hardpoint point-impulses + remove clamp | **in_progress — finish per §3** |
| 2 | C4: Friction solver retail-exact (coupled 2x2 + circleProjection + wheel-relative slip) | pending |
| 3 | C5: Engine pow + wheel+0x88 contact gate | pending |
| 4 | C8: Port ticked brake update (after C4) | pending |
| 5 | C-mass: Thread real chassis mass into HkVehicleData | pending |
| 6 | CW: Composite wheel-collision query (terrain + objects/vehicles) | pending |
| 7 | C7: Retire server-stability clamps (after parity green) | pending |
| 8 | D1: NPC controller tests for sim authority | pending |
| 9 | D2: NpcVehiclePhysicsController rewrite (sim-authoritative) | pending |
| 10 | D3: Lifecycle wiring (ClearPhysicsInstance) | pending |
| 11 | F + gaps: verification, IMPLEMENTATION-GAPS.md, config flip doc | pending |
| — | B5: airStabilization recovery capture | **deferred/out of scope** — see `B5-airstab-capture-plan.md`, do not start unless separately asked |

---

## 11. Other reference material

- **Master port spec (different, older, still authoritative for full per-task checklist detail):**
  `~/.claude/plans/crystalline-plotting-crayon.md` — Phases A-F with granular checklists; this
  handoff's §2 plan is the *execution roadmap* layered on top of it, updated for the confirmed
  real-mass finding and the new CW workstream.
- **Resume/status doc:** `docs/remainingBackgroundWork.md` (in this worktree) — the running
  handoff doc from the RE campaign; has a full completed-work table with commit SHAs, and section
  §9/§11 with the (now largely completed) B-task status. Cross-link `IMPLEMENTATION-GAPS.md` from
  here when you create it (per plan §2 "Documented gaps").
  Note the RE root-cause note in that doc about the `cheatengine` MCP server registration — an
  earlier confusion about `.mcp.json` scope (root vs `src/`) that's now resolved; irrelevant unless
  you hit MCP connectivity issues.
- **Ledger:** `.superpowers/sdd/progress.md` (gitignored local scratch, in the worktree) — one line
  per completed task, trust it + `git log` over any recollection. Has entries for every B-task and
  C1/C3/C6 already.
- **Task briefs/reports:** `.superpowers/sdd/task-<NAME>-brief.md` / `task-<NAME>-report.md`
  (gitignored scratch) — see existing ones (C3, C6, E1) for the expected format/detail level.
- **Project memory** (persists across sessions, outside the repo):
  `C:\Users\josh\.claude\projects\C--Users-josh-Documents-GitHub-AutoCore\memory\retail-npc-driving-branch.md`
  — has a running summary of this whole branch's history; update it when you land major milestones
  (the pattern used throughout this session: append a dated bullet, keep it terse, link to the
  relevant commit/doc).
- **CE tooling scratch:** `tmp/re/` (gitignored) — `ce_client.py` (direct pipe client),
  `CE_API_NOTES.md` (protocol reference), `glm.py` (Python GLM archive parser mirroring
  `GLMLoader.cs`, used to investigate `physics.glm` contents), various `cap_*.txt`/`*.json` capture
  outputs from B1. Keep using/extending these rather than re-deriving from scratch.
- **Git log context:** recent commits on this branch (`git log --oneline` from the worktree) show
  the full RE campaign history — `54abfc59` is the last commit before this handoff (C2 is
  uncommitted on top of it). Read commit messages for detailed rationale on any prior task.

---

## 12. A note on tone/process for whoever picks this up

This has been a long, careful, evidence-driven effort — the RE campaign specifically prioritized
never guessing (multiple docs have explicit "do NOT invent a formula without a decompile/capture"
warnings) and validating every claim against either a decompile or a live capture before trusting
it. Keep that standard for the implementation phase too: when C4's coupled solver doesn't match a
golden on the first try, don't tune constants until it passes — go back to the decompile evidence
in `0.3-friction-solver.md` and figure out which term is actually wrong. The oracle tests exist
specifically to catch "looks plausible but isn't retail" implementations.

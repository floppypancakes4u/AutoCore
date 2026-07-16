# Phase 6 — Server ghost pose pack notes (`GhostVehicle.PackUpdate`)

| Field | Value |
|---|---|
| Scope | Server wire for foreign/NPC vehicle pose (PositionMask block) |
| Primary source | `src/AutoCore.Game/TNL/Ghost/GhostVehicle.cs` → `PackUpdate` |
| Pose producer | `src/AutoCore.Game/Entities/Vehicle.cs` → `ApplyServerMove` |
| Client unpack | `VehicleNet_UnpackGhostVehicle` @ `0x005F7720` |
| Client apply | `Vehicle_setDrivingInputs` @ `0x00504C70` → `FUN_0053EEC0` / `FUN_004FBC10` |
| Plan phase | Phase 6 — Ghost streaming + client prerequisites |
| Status | **Server pack layout confirmed from C#** (no Ghidra re-read required for this note) |

This note freezes the **server-side PositionMask payload** for Phase 6: which fields carry **angVel**, **throttle (thr)**, and **steer**, how they are encoded, how `ApplyServerMove` fills them today, and what the physics port must still fix before retail-smooth NPC motion.

Related RE (client consumers, not re-derived here): `docs/NPCDriving.md` §7–8, `docs/nullWheels.md`, `docs/MOTION_CLIENT_RE.md`, `docs/reconstruction/physics/drive-controller-spec.md`.

---

## 1. Gate

The pose block is packed only when `PositionMask` is set on the effective update mask:

```text
if (stream.WriteFlag((updateMask & PositionMask) != 0)) { … }
```

Streaming keep-dirty (so TNL does not drop the vehicle from the zero-update index between packs):

| Helper | When | Effect |
|---|---|---|
| `ShouldStreamPose` | non-initial + path patrol / moving | `ret \|= PositionMask` |
| `IsMovingForPoseStream` | `\|v\|` or `\|ω\|` > 0.05 | counts as moving for priority + stream |
| `ShouldSuppressPatrolPoseGhost` | `EnableClientSidePathVisual` + idle path | **suppresses** dirty (rejected for foreign NPCs — freezes) |

Priority boosts for foreign vehicles: idle weight `0.40`, moving weight `0.50` (`MovingVehiclePosePriorityWeight`), skip boost `0.05` while moving.

---

## 2. PositionMask wire layout (authoritative pack order)

Source: `GhostVehicle.PackUpdate` lines ~502–538.

| # | Field (server property) | Encode | Bits / size | Client consumer |
|---|-------------------------|--------|-------------|-----------------|
| 1–3 | `Position.X/Y/Z` | `stream.Write(float)` | 32×3 | soft/hard pose target (`FUN_0053EEC0`) |
| 4–7 | `Rotation.X/Y/Z/W` | `stream.Write(float)` | 32×4 | entity rot; hard-write when Havok fully ready |
| 8–10 | `Velocity.X/Y/Z` | `stream.Write(float)` | 32×3 | soft buffer / dead-reckon (`FUN_0053EB90`) |
| **11–13** | **`AngularVelocity.X/Y/Z`** | `stream.Write(float)` | **32×3** | soft integrate **ω × dt** (full pitch/roll/yaw rates) |
| 14 | `Firing` | `stream.Write(byte)` | 8 | weapon-hardpoint enable set (bit0 front / bit1 turret / bit2 rear) |
| 15 | `VehicleFlags` | `stream.Write(byte)` | 8 | driving flags → **handbrake / sharp** path (`+0x61c`) |
| **16** | **`Acceleration` (= thr)** | **`WriteSignedFloat(..., 6)`** | **6** | entity **`+0x614` throttle** |
| **17** | **`Steering`** | **`WriteSignedFloat(..., 6)`** | **6** | entity **`+0x618` steer** |
| 18 | `WantedTurretDirection` | `stream.Write(float)` | 32 | vehicle `+0x15c` relative turret yaw |

There is **no separate angVel W** on the ghost pose block — angular velocity is a **3-float vector** (rad/s), matching the soft-buffer layout at client buffer `+0x30`.

### 2.1 Critical wire order: Firing then VehicleFlags

Retail unpack reads:

1. **Byte #1** = weapon enable / Firing set  
2. **Byte #2** = `VehicleMovedFlags` (bit0 **Handbreak** → vehicle `+0x61c`)

Server **must** emit `Firing` first, then `(byte)VehicleFlags`. Swapping them mis-delivers `Firing&1` into the handbrake slot:

- `calcWheelTorque` @ `0x598040` halves **rear** drive torque when `entity+0x61c ≠ 0`
- Path NPCs appear to “drive with the brakes on”

Regression: `GhostVehicleWireRegressionTests.PackUpdate_NonInitial_PoseFlagBytes_FiringFirst_HandbrakeNotSetByFiring`.

`VehicleMovedFlags` (`VehicleMovedPacket.cs`):

| Bit | Name | Wire role for NPCs |
|----:|------|--------------------|
| `0x1` | `Handbreak` | Client `+0x61c` / handbrake — **do not set for normal path thr/steer** |
| `0x2` | `SimClient` | unused in current pack path |
| `0x4` | `Corpse` | corpse presentation |

`ApplyServerMove` (Phase 6) maps `sharpTurn` → `VehicleFlags.Handbreak` when the mover supplies a
value (`ApplySharpTurnToVehicleFlags`): non-zero sets bit0, zero clears it. Does **not** touch
`Firing` (byte #1). Unset `sharpTurn` leaves flags unchanged.

---

## 3. angVel — full vector, not yaw-only

### 3.1 Pack

```csharp
stream.Write(parentVehicle.AngularVelocity.X);
stream.Write(parentVehicle.AngularVelocity.Y);
stream.Write(parentVehicle.AngularVelocity.Z);
```

Full IEEE floats — **no quantization**. Client soft path stores them and integrates with `FUN_0053EB90` (`ω × dt` on the rotation buffer). Yaw-only ω loses slope tilt between packs.

### 3.2 How the server fills it today (`ApplyServerMove`)

When `0 < dt < 1`:

```text
AngularVelocity = EstimateAngularVelocity(prevRot, nextRot, dt)
```

`EstimateAngularVelocity`:

1. `qDelta = to * conjugate(from)` (shortest path if `dw < 0`)
2. `angle = 2 * atan2(|xyz|, w)`
3. `ω = axis * (angle / dt)` as `Vector3` (full 3-axis)

When `dt` is out of range (or zero): `AngularVelocity = default` (zeros).

### 3.3 Phase 6 requirement for the physics sim

When `NpcVehiclePhysicsController` / `VehiclePhysicsInstance` supplies **sim** angVel:

- **Consume sim ω directly** — do not re-estimate from quaternion delta when the sim already has body `+0x50..0x58` rates.
- Still pack all three components; pitch/roll rates matter for ramp/slope soft-buffer continuity.

Body layout reference (client Havok RB): linVel `+0x40..0x48`, angVel `+0x50..0x58` (`0.8-struct-offsets.md`).

---

## 4. thr — property name `Acceleration` (not m/s²)

### 4.1 Naming trap

| Server C# name | Wire meaning | Client slot | Valid range |
|----------------|--------------|-------------|-------------|
| `Vehicle.Acceleration` | **Throttle / longitudinal drive axis** | `entity+0x614` | **[-1, +1]** |

It is **not** linear acceleration in m/s². Constant-speed cruise with `d(speed)/dt ≈ 0` and thr=0 freezes client Havok between packs → next pose hard-snaps (visible skip). See `docs/nullWheels.md` M2.

### 4.2 Pack encode — `WriteSignedFloat(f, 6)`

TNL.NET (`BitStream.cs`):

```text
WriteSignedFloat(f, bitCount):
  WriteSignedInt((int)(f * ((1 << (bitCount-1)) - 1)), bitCount)

// bitCount = 6 → scale = (1 << 5) - 1 = 31
// thr ≈ n/31, n ∈ [-31, +31]  (signed 6-bit)
```

Quantization step ≈ **1/31 ≈ 0.032**. Values outside [-1,1] clamp by integer cast / signed int width; pack path should keep thr in [-1,1] before write.

Read path (tests / mirror client): `ReadSignedFloat(6)`.

### 4.3 How the server fills thr today

`ApplyServerMove(..., driveThrottle?, ...)`:

| Source | Behavior |
|--------|----------|
| `driveThrottle` provided | `Acceleration = clamp(driveThrottle, -1, 1)` (also applied when `dt` out of range if explicit) |
| else, `0 < dt < 1` | `ResolvePathThrottle(prevSpeed, nextSpeed, |dYaw|/dt)` |
| else | leave previous / unset (no auto fill) |

`ResolvePathThrottle` (heuristic when no drive controller):

| Condition | thr |
|-----------|-----|
| `nextSpeed ≤ 0.05` | `0` |
| speed rising (`delta > 0.35`) | `1.0` × turnEase |
| speed falling (`delta < -0.35`) | `0.25` × turnEase (coast — avoids reverse thr) |
| cruise | `0.85` × turnEase |
| `|yawRate| > 1.2` | turnEase = `0.55` |

Drive-controller path (`VehicleDriveInputs` / future `VehicleDriveController` @ client `0x4fc650`) should supply **explicit** thr so wheels spin even when pose is already heading-aligned. Retail AI throttle sign convention (forward base **−1** on some axes) is documented in `drive-controller-spec.md` — wire still carries the **normalized** value the client expects at `+0x614`.

Client path after unpack:

```text
+0x614 = thr
FUN_004FBC10  // PushDriveAxesToController — no-op unless VehicleAction (+0x1A0) exists
```

---

## 5. steer — property name `Steering`

### 5.1 Pack

```csharp
stream.WriteSignedFloat(parentVehicle.Steering, 6);  // same 6-bit signed float as thr
```

Client slot: **`entity+0x618`** (normalized steer ∈ [-1, +1]). Downstream:

1. `VehicleAction::applyAction` ramps toward this axis (`VA+0x24` / `VA+0x28`, step `0.05`)
2. `hkDefaultSteering_update` @ `0x64f840` → wheel angles (quadratic inverse-speed falloff)

### 5.2 How the server fills steer today

| Source | Behavior |
|--------|----------|
| `driveSteering` provided | `Steering = clamp(driveSteering, -1, 1)` |
| else, `0 < dt < 1` | `Steering = clamp(dYaw / (π/2 * max(dt, 1e-3)), -1, 1)` |

**Problem for wheel visuals:** if the chassis is already facing the path, `dYaw ≈ 0` ⇒ **steer≈0** every pack ⇒ locked wheels even while turning along a curve. Soft / drive-controller tiers must pack **look-ahead lateral error** (`VehicleDriveInputs` / `MoveToTarget3DPoint`), not dYaw of an already-aligned pose (`NPCDriving.md` §8.2).

---

## 6. Phase 6 checklist (plan ↔ code)

From plan `cheerful-swimming-reef.md` Phase 6 and current worktree:

| Item | Status | Notes |
|------|--------|-------|
| Pack pos/rot/vel | **Done** | full floats |
| Pack **full angVel XYZ** | **Done** on wire | producer is still `EstimateAngularVelocity` unless sim supplies ω |
| Pack Accel=**thr**, Steering | **Done** | 6-bit signed; name is misleading |
| Preserve **Firing → VehicleFlags** order | **Done** + regression test | never swap |
| Keep `PositionMask` dirty while moving/path | **Done** | `ShouldStreamPose` |
| Dense TNL rate (≥20 KB/s, ≤50 ms) | **Done** (negotiated floor) | still need non-starved dirty list |
| Consume **sim** angVel when physics port feeds pose | **Open** | `ApplyServerMove` always estimates when `dt` in range |
| Wire **sharpTurn** without permanent Handbreak | **Open** | `_ = sharpTurn` today; do not set `VehicleFlags.Handbreak` for path assist |
| Non-zero client integrate **dt** (`ghost+0xBC`) | **Open / verify** | client `dt = ghost+0xBC * 0.001`; zero ⇒ no soft integrate |
| Foreign activate → **VehicleAction + wheelset** | **Prerequisite** | thr/steer no-op without `+0x1A0`; null `+0x258` race in `nullWheels.md` |
| Reject client-only path visual for remotes | **Confirmed** | `EnableClientSidePathVisual` freezes foreign NPCs |

---

## 7. Client apply path (what packed fields do)

```text
VehicleNet_UnpackGhostVehicle @ 0x005F7720
  read pos, rot, vel, angVel
  read Firing byte, VehicleFlags byte
  read SignedFloat6 thr, SignedFloat6 steer
  read WantedTurretDirection
  integrateDt = ghostObj+0xBC * 0.001

Vehicle_setDrivingInputs @ 0x00504C70
  write +0x614 thr, +0x618 steer, +0x61c sharp/flags path
  FUN_004FBC10   // needs VehicleAction
  FUN_0053EEC0(pos, rot, vel, angVel, integrateDt)
```

| Apply branch | Condition | Effect of packed fields |
|--------------|-----------|-------------------------|
| Soft buffer | physics shell not fully ready | buffer pos/rot/vel/**angVel**; teleport only if error **> 15 u**; integrate vel/ω × dt |
| Hard write | fully ready / no shell | **overwrite** entity pos/rot from wire (pitch/roll **must** be on rotation) |
| Full Havok between packs | VehicleAction live | thr/steer drive wheels + chassis; next pack **corrects** |

Server cannot stream suspension — stream **grounded pose + drive axes** so client physics animates between packs.

---

## 8. Producer API surface (for Phase 5→6 wiring)

```text
Vehicle.ApplyServerMove(pos, rot, vel, dt)
Vehicle.ApplyServerMove(pos, rot, vel, dt, driveThrottle?, driveSteering?, sharpTurn?)
  → sets Position, Rotation, Velocity
  → AngularVelocity (estimate or future sim)
  → Acceleration (thr), Steering
  → Ghost?.SetMaskBits(PositionMask)  // unless path-visual suppress
```

Physics controller handoff (Phase 5 design): emit `PathStepResult` with sim pos/rot/vel/**angVel**, thr/steer/sharp, `HasDriveInputs=true` → single `ApplyServerMove` that **does not re-estimate ω** when sim ω is present.

Combat aim reuses the same mask: `SetCombatWeaponWire` updates `WantedTurretDirection` / `Firing` and dirties `PositionMask` so observers get fire bits + turret yaw with pose.

---

## 9. Quick reference — thr / steer / angVel only

| Concept | Server property | Wire | Client |
|---------|-----------------|------|--------|
| **angVel** | `AngularVelocity` (Vector3) | 3× f32 | soft buffer ω; integrate with dt |
| **thr** | `Acceleration` (float) | SignedFloat **6** | `+0x614` throttle |
| **steer** | `Steering` (float) | SignedFloat **6** | `+0x618` steer axis |
| sharp / handbrake | `VehicleFlags` bit0 (and future sharp wiring) | byte #2 after Firing | `+0x61c` (handbrake halves rear torque) |

**Do:**

- Pack non-zero cruise thr while moving  
- Pack look-ahead steer, not dYaw-of-aligned-pose  
- Pack full ω (pitch/roll included)  
- Emit Firing then VehicleFlags  

**Do not:**

- Treat `Acceleration` as m/s²  
- Set Handbreak for routine path sharp assist  
- Suppress foreign pose expecting client HBAI to drive  
- Rely on thr/steer without client VehicleAction  

---

## 10. Source anchors

| Artifact | Path / address |
|----------|----------------|
| Pack pose block | `GhostVehicle.cs` `PackUpdate` PositionMask branch |
| angVel / thr / steer producers | `Vehicle.ApplyServerMove`, `EstimateAngularVelocity`, `ResolvePathThrottle` |
| Signed float codec | `lib/TNL.NET/.../BitStream.cs` `WriteSignedFloat` / `ReadSignedFloat` |
| Wire regression | `GhostVehicleWireRegressionTests` (pose order, Firing/flags) |
| Client unpack | `0x005F7720` |
| Client set axes + pose | `0x00504C70` → `0x004FBC10`, `0x0053EEC0` |
| Soft integrate | `0x0053EB90` |
| Soft teleport threshold | `DAT_009d000c` = **15.0** |
| Handbrake rear cut | `calcWheelTorque` `0x598040`, factor **0.5** |
| Plan Phase 6 | `~/.claude/plans/cheerful-swimming-reef.md` |

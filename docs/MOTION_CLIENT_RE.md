# Client vehicle network pose RE (heavy pass)

Date: 2026-07-11. Client build `autoassault` image base `0x400000`.

## Live capture facts (PathAHook `SetDrivingInputs`)

| Fact | Value |
| --- | --- |
| VehicleAction (`+0x1A0`) | Present on foreign NPCs when owner+activate path ran |
| Throttle | ~1.0 after cruise fix |
| Integrate `dt` | ~0.03–0.07 s after first pack |
| True apply rate | **~50–100 ms** (hook was sampling every 5th hit; raw `drive_hit` steps of 5) |
| Server `posePacks2s` | **35–40** @ 50 ms tick / 1000 B packets |

**Conclusion:** Pack starvation was largely a **logging bug** (every-5th sample). Stream is dense. Remaining visual “skip” is **how pose is applied**, not missing packs.

## `Vehicle_setDrivingInputs` (`0x00504C70`)

Callees: `FUN_004FBC10` (throttle→action), optional `FUN_00503F30` (ActivateEnterWorld), `FUN_0053EEC0` (pose apply).

Stack after `this` (thiscall): `pos*, rot*, vel*, angVel*, throttle, steering, sharp, skipActivate, integrateDt`.

Unpack site (`0x005F9940` in `VehicleNet_UnpackGhostVehicle`):

```text
integrateDt = *(float*)(ghostObj + 0xBC) * 0.001   // DAT_00a0f72c = 0.001
```

## `FUN_0053EEC0` — network pose apply (critical)

```text
vehicle+8 = graphics/physics object
if (vehicle+8 && scale_ok) {
  // bVar3 = physics NOT fully ready: (+0x40==0) || (physics+8==0)
  if (bVar3) {
    // SOFT PATH
    flag network target; timestamp = DAT_00b041cc
    buffer = alloc/reuse 0x40
    buffer+0x00 = position
    buffer+0x10 = rotation (if valid)
    buffer+0x20 = velocity (if |v| large enough) else zero
    buffer+0x30 = angular velocity
    if (|pos - current| > 15.0) {   // DAT_009d000c = 15.0f
      hard teleport + impulse
    }
    if (integrateDt != 0) FUN_0053EB90(dt)  // one-shot integrate buffer
    return
  }
  // else: physics fully active — fall through
}
// HARD PATH (no soft buffer): write pos/rot into entity +0x84 / +0x94 if |pos| > eps
```

| Path | Condition | Motion between packs |
| --- | --- | --- |
| Soft buffer | Physics object exists but **not fully ready** | Buffer + optional integrate; teleport only if error > **15 u** |
| Hard entity write | No physics object, or scale fail | Snap entity fields only — **may not move render if inactive** |
| Full Havok | Physics fully ready (`bVar3==false`) | Falls out of soft path → entity field write; **Havok drives** via action+throttle between packs, then next pack **corrects** |

### Why `IsActive=false` froze NPCs (live test)

Inactive create/ghost never builds a usable physics/render drive path. Pose may write entity slots, but **visual stays frozen**. Reverted 2026-07-11.

## Per-frame consumers

| Addr | Role |
| --- | --- |
| `FUN_0053E820` | Dead-reckon buffer vs current physics; reset buffer from physics if diverged (`FUN_0053DEE0`) |
| `FUN_004F3F70` | Vehicle tick ending in `FUN_0053E820` |
| `FUN_0053F1F0` | Blend/catch-up toward buffer (vtable/data xrefs only — dynamic call) |
| `FUN_0053EB90` | Integrate buffer quat/pos from angVel/vel × dt |

## Retail-shaped smoothness (RE interpretation)

1. **Keep NPC IsActive + wheels + owner activate** so VehicleAction exists (confirmed live).
2. **Dense pose** (~50 ms) so correction error stays ≪ 15 u (soft) / small (hard).
3. **Throttle + velocity** aligned so Havok between packs tracks network pos (reduces rubber-band).
4. Soft path (partial physics) only when client leaves physics “not fully ready” — **not** forced by server `IsActive=false` (kills motion).

## Failed experiments

| Experiment | Result |
| --- | --- |
| Suppress pose / client path AI only | Freeze (HBAI not driving foreign) |
| NPC `IsActive=false` + thr=0 | **Freeze** (this RE) |
| Every-5th motion log | False “500 ms median gap” |

## Soft-path force (PathAHook lab patch)

**Mechanism:** MinHook on `FUN_0053EEC0` clears `*(char*)(vehicle+8+0x40)` for the call duration so `bVar3` takes the **soft buffer** branch (teleport only if error > 15 u; otherwise buffer + integrate). Flag restored after call.

**Why not server `IsActive=false`:** that removed the graphics/physics shell entirely → freeze. Soft-force keeps IsActive/action/wheels; only the apply branch changes.

**Arm:** rebuild PathAHook → restart client → `scripts\path-a-debug.cmd arm` → setup must show `"soft":1`. Look for `PoseApplySoftForce` events in hits.jsonl.

## Open RE / next server levers

1. What sets ghost `+0xBC` (ms) on the TNL ghost object — ensure non-zero for integrate.
2. Match clonebase max speed to path speed so thr=1 does not overshoot if soft force is off.
3. If soft force works, consider a permanent lab client binary patch (fleet later).

## Related

- `docs/MOTION_DEBUG.md` — capture workflow  
- `docs/nullWheels.md` — wire levers, activate race  
- PathAHook `SetDrivingInputs` at `0x00504C70`

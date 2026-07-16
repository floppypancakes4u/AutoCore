# Verified: `VehicleEntity_SetSteerInput` @ `0x004f5620`

(+ sibling `VehicleEntity_SetLongitudinalInput` @ `0x004f5650`)

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `VehicleEntity_SetSteerInput` |
| Address / body | `0x004f5620` – `0x004f564a` |
| Convention | MSVC `__thiscall` — `this` in `ECX` (`param_1`) |
| Args | `param_2` = float32 steer axis (passed as `undefined4` in decompile) |
| Role | Gated write of normalized steer input to `entity+0x618` |
| RE tools | `decompile_function` @ `0x4f5620`, `0x4f5650`; `get_function_by_address`; callers |
| Status | **Verified** |

Nearby sibling (contiguous, identical gate shape):

| Symbol | Address / body | Write target |
|---|---|---|
| `VehicleEntity_SetLongitudinalInput` | `0x004f5650` – `0x004f567a` | `entity+0x614` |

Related setters (same family, not this pair): `VehicleEntity_SetHandbrake` `0x004f3620`, `VehicleEntity_SetDriveAxes` `0x004fbec0`.

---

## 1. Decompile (authoritative)

### `VehicleEntity_SetSteerInput` @ `0x004f5620`

```c
/* WI-MOV-001: thiscall. Writes float to this+0x618 (steer input). DriveControlTick: SteerLeft held
   DAT_00d1bc8e → +1; SteerRight DAT_00d1bcc2 → -1; soft L/R ±0.5. VehicleAction_applyAction
   ramps this+0x618 into VA+0x24. */

void __thiscall VehicleEntity_SetSteerInput(int param_1, undefined4 param_2)
{
  int iVar1;

  iVar1 = *(int *)(*(int *)(*(int *)(param_1 + 4) + 4) + 0xb0 + param_1);
  if ((iVar1 == 0) || ((*(byte *)(iVar1 + 0xb4) & 199) == 0)) {
    *(undefined4 *)(param_1 + 0x618) = param_2;
  }
  return;
}
```

### `VehicleEntity_SetLongitudinalInput` @ `0x004f5650`

```c
/* WI-MOV-001: thiscall. Writes float to this+0x614 (longitudinal input). Called from
   Client_Input_DriveControlTick with -1 (Accelerate name held DAT_00d1bc26), +1 (Reverse name
   DAT_00d1bc5a), or 0. Gate: skip if vehicle subsystem at this+linked+0xb0 has flags & 0xC7. */

void __thiscall VehicleEntity_SetLongitudinalInput(int param_1, undefined4 param_2)
{
  int iVar1;

  iVar1 = *(int *)(*(int *)(*(int *)(param_1 + 4) + 4) + 0xb0 + param_1);
  if ((iVar1 == 0) || ((*(byte *)(iVar1 + 0xb4) & 199) == 0)) {
    *(undefined4 *)(param_1 + 0x614) = param_2;
  }
  return;
}
```

Both bodies are ~0x2b bytes of pure gate + single float store. No clamps, no ramps, no side effects.

---

## 2. Writes (the only stores)

| Function | Condition | Store |
|---|---|---|
| `SetSteerInput` | gate open | `*(float*)(this + 0x618) = param_2` |
| `SetLongitudinalInput` | gate open | `*(float*)(this + 0x614) = param_2` |

| Offset | Type | Meaning |
|---|---|---|
| `this+0x614` | f32 | Longitudinal / throttle-brake axis (typically `[-1, +1]`) |
| `this+0x618` | f32 | Steer axis (typically `[-1, +1]`) |

If the gate is closed, **no write** occurs (previous value retained).

There are **no other memory writes** in either function (no handbrake `+0x61c`, no flags, no controller push).

---

## 3. Write gate (identical in both)

```
// Pointer chain (Ghidra expression as-is):
//   *( *(*(this + 4) + 4) + this + 0xb0 )
//
// Reading order:
//   p0 = *(this + 4)
//   p1 = *(p0 + 4)
//   wobj = *(p1 + this + 0xb0)    // entity-relative slot on linked object

wobj = *( *(*(this + 4) + 4) + this + 0xb0 );

// Allow write if:
//   wobj is null, OR
//   flag byte at wobj+0xb4 has none of bits 0x01|0x02|0x04|0x40|0x80 set
if (wobj == 0 || (*(uint8*)(wobj + 0xb4) & 0xC7) == 0)
    store;
```

| Item | Value |
|---|---|
| Mask constant | `199` decimal = **`0xC7`** |
| Bits tested | `0x01 \| 0x02 \| 0x04 \| 0x40 \| 0x80` |
| Semantic | Steer/longitudinal **lock / external override** on the wheel-control object |
| Null `wobj` | Treated as unlocked → write proceeds |

This is the **same** gate used by the NPC drive controller’s proportional steer path when it writes `+0x618` inline (`drive-controller-spec.md` §4). Callers that go through these setters are therefore **still gated**; they do not bypass the mask.

> **Correction note:** Older text claiming deadband `SetSteerInput` is “not subject to this gate” is wrong. The deadband branch does not inline the gate, but the callee applies it. Effectively equivalent to the inline proportional path.

---

## 4. Algorithm (both)

```
// SetSteerInput(this, value)  OR  SetLongitudinalInput(this, value)

wobj = *( *(*(this+4)+4) + this + 0xb0 );
if (wobj != 0 && (*(uint8*)(wobj+0xb4) & 0xC7) != 0)
    return;   // suppressed

// SetSteerInput:
*(float*)(this + 0x618) = value;

// SetLongitudinalInput:
*(float*)(this + 0x614) = value;
```

No range clamp. Callers are responsible for legal `[-1, +1]` (or `0` clear).

---

## 5. Callers (xrefs, sample)

### `SetSteerInput` (`0x004f5620`)

| Caller | Address |
|---|---|
| `Client_Input_DriveControlTick` | `0x009223b0` |
| `FUN_004fc650` (NPC / auto drive-to-point) | `0x004fc650` |
| `FUN_005d73a0` | `0x005d73a0` |
| `FUN_00636ba0` | `0x00636ba0` |
| `FUN_0092f090` | `0x0092f090` |
| `FUN_009373e0` | `0x009373e0` |

### `SetLongitudinalInput` (`0x004f5650`)

| Caller | Address |
|---|---|
| `Client_Input_DriveControlTick` | `0x009223b0` |
| `FUN_004fc650` | `0x004fc650` |
| `FUN_005d73a0` | `0x005d73a0` |
| `FUN_00914c20` | `0x00914c20` |
| `FUN_00925820` | `0x00925820` |
| `FUN_0092f090` | `0x0092f090` |
| `FUN_009373e0` | `0x009373e0` |
| `FUN_00938670` | `0x00938670` |
| `FUN_00946c00` | `0x00946c00` |

**Input-side plate comments (not re-verified here):**

- Steer: held left `DAT_00d1bc8e` → `+1`; held right `DAT_00d1bcc2` → `-1`; soft L/R `±0.5`.
- Longitudinal: Accelerate held `DAT_00d1bc26` → `-1`; Reverse held `DAT_00d1bc5a` → `+1`; else `0`.

Downstream of `entity+0x618`: `VehicleAction_applyAction` ramps into `VA+0x24` (see `steering-spec.md` / `fn_0064f840_steering.md`). Downstream of `entity+0x614`: transmission / wheel torque path. Neither setter pushes axes to the Havok controller; that is a separate call (`VehicleEntity_PushDriveAxesToController` / `SetDriveAxes` family).

---

## 6. Porting implications

1. **Two independent axis stores** on the vehicle entity: `+0x614` (longitudinal), `+0x618` (steer).
2. **Always apply the `0xC7` gate** before writing either axis if matching retail lock behavior.
3. **Do not invent clamps** inside these setters; retail does none.
4. **Callers that write `+0x614` / `+0x618` directly** (e.g. NPC proportional throttle at `FUN_004fc650`) must either reimplement the same gate or go through these helpers for parity.
5. Handbrake / sharp is **not** this path — see `entity+0x61c` / `SetHandbrake` / pack flag docs separately.

---

## 7. Verification checklist

| Check | Result |
|---|---|
| Decompile `0x4f5620` | Single gated store to `+0x618` |
| Decompile `0x4f5650` | Single gated store to `+0x614` |
| Gate mask | `199` ≡ `0xC7` in both |
| Pointer chain | Identical: `*(*(*(this+4)+4) + this + 0xb0)` |
| Extra stores / calls | None |
| Emulation | Not required (trivial control flow, no FP math) |

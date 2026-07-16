# Verified: `FUN_005d6ae0` — rotate local vector by chassis basis @ `0x005d6ae0`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Symbol | `FUN_005d6ae0` (unnamed; general 3×3 rotate helper) |
| Address | `0x005d6ae0` … `0x005d6b76` |
| Role | `out = R · v` — world-rotate a local xyz vector using a column-major rotation (Havok-style column stride 16); write `out.w = 0` |
| Convention | MSVC **`__thiscall`**: `ECX = out`, stack `R`, stack `v`; **`RET 8`** |
| Primary vehicle use | `hkDefaultSteering_update` @ `0x64f840` — chassis **front axis** → world for forward-speed |
| RE tools | `decompile_function` @ `0x5d6ae0`; `disassemble_function` @ `0x5d6ae0`, `0x64f840`, `0x580ed0`, `0x64cf20`; `get_function_callers` |
| Status | **Verified** (asm + decompile re-read) |

No `DAT_*` constants. No C# in this file (RE evidence only).

Related: steering closed form in `fn_0064f840_steering.md`; aero uses the same body `+0x80` rotation / vehicleData `+0x10` front axis (`fn_0064dae0_aero.md`).

---

## 1. Decompile (authoritative)

Ghidra emits three `float*` parameters; the first is **`this` / `ECX` (out)**:

```c
// out  = ECX  (float[4]; xyz written, w forced 0)
// R    = [ESP+4] after entry  (const float*, column-major rotation base)
// v    = [ESP+8] after entry  (const float*, local xyz)
void __thiscall FUN_005d6ae0(float *out, float *R, float *v)
{
  float vx = v[0];
  float vy = v[1];
  float vz = v[2];

  // float indices: R[0],R[1],R[2] @ +0x00; R[4..6] @ +0x10; R[8..10] @ +0x20
  out[0] = R[8] * vz + R[4] * vy + R[0] * vx;
  out[1] = R[9] * vz + R[5] * vy + R[1] * vx;
  out[2] = R[10] * vz + R[6] * vy + R[2] * vx;
  out[3] = 0.0f;
  return;  // RET 8
}
```

**In-place is safe:** `vx,vy,vz` are snapshotted before any store to `out`. Call sites may pass the same pointer for `out` and `v` (e.g. hit normal).

---

## 2. Assembly (calling convention + math)

```
005d6ae0  MOV  EAX, [ESP+0x8]          ; v
005d6ae4  MOVSS XMM0, [EAX]            ; vx
005d6ae8  MOVSS XMM1, [EAX+0x4]        ; vy
005d6aed  MOVSS XMM2, [EAX+0x8]        ; vz
005d6af2  MOV  EAX, [ESP+0x4]          ; R
005d6af6  MOVSS XMM3, [EAX+0x20]       ; R.col2.x
005d6afb  MOVSS XMM4, [EAX+0x10]       ; R.col1.x
          MULSS XMM3, XMM2             ; * vz
          MULSS XMM4, XMM1             ; * vy
          ADDSS XMM3, XMM4
          MOVSS XMM4, [EAX]            ; R.col0.x
          MULSS XMM4, XMM0             ; * vx
          ADDSS XMM3, XMM4
005d6b18  MOVSS [ECX], XMM3            ; out.x
          … same for out.y from R+0x24 / +0x14 / +0x4 …
          … same for out.z from R+0x28 / +0x18 / +0x8 …
005d6b5f  XORPS XMM0, XMM0
005d6b6f  MOVSS [ECX+0xc], XMM0        ; out.w = 0
005d6b74  RET  0x8                     ; pop R, v
```

| Slot | Register / stack | Meaning |
|---|---|---|
| `out` | **`ECX`** | Destination `float[4]` (16 bytes; w always 0) |
| `R` | `[ESP+4]` at entry | Rotation base (not translation) |
| `v` | `[ESP+8]` at entry | Source xyz |

---

## 3. Matrix layout

`R` is a **column-major 3×3** with **column stride 16 bytes** (Havok `hkVector4` columns / `hkRotation` head of `hkTransform`):

| Column | Byte offset | Components used |
|---|---|---|
| col0 (local X → world) | `+0x00` | `(R+0)`, `(R+4)`, `(R+8)` |
| col1 (local Y → world) | `+0x10` | `(R+0x10)`, `(R+0x14)`, `(R+0x18)` |
| col2 (local Z → world) | `+0x20` | `(R+0x20)`, `(R+0x24)`, `(R+0x28)` |

Closed form:

```
out = vx · col0 + vy · col1 + vz · col2
    = R * v_local
out.w = 0
```

**Not** a full transform: **no translation** add. Direction / axis / normal only.

Padding dwords at `+0x0c`, `+0x1c`, `+0x2c` (w of each column) are **unread**.

---

## 4. Authoritative formula

```
RotateLocalToWorld(R, v) → out:
  out.x = R[0x00]*v.x + R[0x10]*v.y + R[0x20]*v.z
  out.y = R[0x04]*v.x + R[0x14]*v.y + R[0x24]*v.z
  out.z = R[0x08]*v.x + R[0x18]*v.y + R[0x28]*v.z
  out.w = 0
```

---

## 5. Steering call site — chassis front axis (`0x64f840`)

### Pseudocode (Ghidra decompile is missing `ECX=out`)

```c
// hkDefaultSteering_update — excerpt
float local_out[4];  // stack, 16-byte aligned region

// iVar1 = *(steering + 8)  → vehicle framework
// chassis body = *(*(framework + 0x30) + 0x3c)
// vehicleData  = *(framework + 0x10)
FUN_005d6ae0(
  /* ECX */  local_out,
  /* R   */  chassisBody + 0x80,          // world rotation
  /* v   */  vehicleData + 0x10           // local front axis (xyz)
);

// forwardSpeed = dot(chassisLinearVel, worldFront)
forwardSpeed =
    body[+0x40] * local_out[0] +
    body[+0x44] * local_out[1] +
    body[+0x48] * local_out[2];
```

### Assembly (call prep) — proof of arg order

```
0064f84c  MOV  EAX, [ESI+0x8]           ; framework
0064f857  MOV  EDX, [EAX+0x10]          ; vehicleData*
0064f85a  MOV  EAX, [EAX+0x30]
0064f85d  MOV  ECX, [EAX+0x3c]          ; chassis rigid body*
0064f865  ADD  EDX, 0x10                ; v  = vehicleData + 0x10  (front axis)
0064f868  ADD  ECX, 0x80                ; R  = body + 0x80         (rotation)
0064f86e  PUSH EDX                      ; stack arg2 = v
0064f86f  PUSH ECX                      ; stack arg1 = R
0064f870  LEA  ECX, [ESP+0x18]          ; out = stack vector
0064f87a  CALL FUN_005d6ae0
; RET 8 restores ESP; out then at [ESP+0x10..0x18]
0064f888  MOVSS XMM1, [body+0x48]       ; vel.z * out.z …
0064f88d  MULSS XMM1, [ESP+0x18]
          … vel.y * out.y, vel.x * out.x …
```

| Input | Path | Meaning |
|---|---|---|
| `R` | `*(*(fw+0x30)+0x3c) + 0x80` | Chassis rigid-body **world rotation** (same block aero uses) |
| `v` | `*(fw+0x10) + 0x10` | Vehicle-data **local front** unit axis (`+0x10/+0x14/+0x18`) |
| `out` | stack `float[4]` | **World front** for `dot(linVel, ·)` |

Steering then applies `SteeringMaxAngle * steer` and the **quadratic** full-speed falloff (see `fn_0064f840_steering.md`). This helper only supplies the forward basis for the speed scalar.

### Sibling: `FUN_0064fb60` @ `0x64fb60`

Identical call shape (`body+0x80`, `vehicleData+0x10`, stack out, then `dot` with `body+0x40..48`). Same front-axis world basis pattern (alternate / related steering path).

---

## 6. Other vehicle call sites (context)

| Caller | Address | Pattern |
|---|---|---|
| `TtPhantom::castRay` | `0x580ed0` @ `0x5810c6` | **In-place** world-rotate of cast **result** (normal at result+0): `ECX=result`, `R=*(hitCollidable+8)+0x20`, `v=result`. Note transform base **`+0x20`** on that object graph (not chassis `+0x80`) |
| `hkVehicleFramework_preUpdate` | `0x64cf20` | Multiple: e.g. rotate into wheel slot `wheel+0x50`; later `R=vehicleData+0x10` path for chassis front (same as steering) into a large stack vector; another rotates a built local direction with `body`-related `R` into `wheel+0x60` region |

Wheel-collide docs that list `FUN_005d6ae0` as “rotate hit normal with body transform” match the phantom site; decompiler two-arg listings omit `ECX=out`.

---

## 7. Port notes / non-goals

- Port as a pure `Vector3`/`Vector4` rotate: **no** translation, **no** normalize, **no** scale.
- Chassis steering / aero / preUpdate speed basis: **`R = body+0x80`**, **`v = vehicleData+0x10` (front)**.
- Do **not** assume every caller uses `+0x80`; phantom cast uses a different transform object offset (`+0x20`).
- `out.w = 0` is intentional (SIMD / hkVector4 lane); consumers of xyz-only are fine.
- Emulation skipped: pure linear algebra, fully determined by asm.

---

## 8. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x5d6ae0` | OK — `out = R·v`, `out.w=0` |
| `disassemble_function` `0x5d6ae0` | OK — `ECX` out, `[ESP+4]` R, `[ESP+8]` v, `RET 8`; column stride `0x10` |
| `disassemble_function` `0x64f840` call @ `0x64f87a` | OK — `PUSH v`, `PUSH R=body+0x80`, `LEA ECX,out`; then `dot(vel, out)` |
| `disassemble_function` `0x580ed0` call @ `0x5810c6` | OK — in-place normal, `R=*(collidable+8)+0x20` |
| Callers list | Many (physics + world); vehicle-critical: steering, preUpdate, phantom cast |
| Conflict with prior docs | Steering doc’s “helper loads forward axis” is correct; arg order is **`out(ECX), R, v`**, not a two-arg mystery |

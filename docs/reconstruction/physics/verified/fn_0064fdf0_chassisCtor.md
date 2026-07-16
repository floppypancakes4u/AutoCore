# Verified: `hkDefaultChassis_ctor` @ `0x0064fdf0`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Reconciled against map notes in `0.2-mass-inertia.md`, `setup-field-mapping.md`, and
`verified/fn_005fd390_buildFramework.md`.

| Item | Value |
|------|--------|
| Address | `0x0064fdf0` |
| Body | `0x0064fdf0` – `0x0064fe08` |
| Ghidra name | `hkDefaultChassis_ctor` |
| Calling convention | MSVC `__thiscall` (`this` in ECX; 1 stack arg; `ret 4`) |
| Role | Construct **`hkDefaultChassis`** (derived from **`hkChassisComponent`**): refcount + CCS basis + install class vtable |
| Allocation size (framework) | **`0x40`** bytes (`Vehicle_buildHavokVehicleFramework` @ `0x5fd390`) |
| Vtable installed | `PTR_FUN_009e4fd0` (`0x009e4fd0`) — class name string `hkDefaultChassis` @ `0x009e4ffc` |
| Base class vtable (temp) | `PTR_FUN_009e728c` (`0x009e728c`) — reflection name `hkChassisComponent` @ `0x009e752c` |
| Callers | `Vehicle_buildHavokVehicleFramework` site `0x005fd4a0`; factory `FUN_0064fe80` site `0x0064fe9c` |

---

## Tools used (this verification)

1. **`decompile_function`** `0x64fdf0` program=`autoassault.exe`
2. **`disassemble_function`** `0x64fdf0`, `0x65eac0`, `0x65e6c0`, `0x5fd390` (call site)
3. **`decompile_function`** base `FUN_0065eac0` `0x65eac0`, CCS init `FUN_0065e6c0` `0x65e6c0`
4. **`read_memory`** vtable `0x9e4fd0`, base vtable `0x9e728c`, `DAT_00a0f2a0`, `DAT_00aaa668`
5. Cross-check: `hkVehicleFramework_wireComponents` `0x636940`, `Vehicle_createVehicleAction` `0x4fb660`,
   `FUN_00651fd0` (desc[0..9] zero), consumers aero/steer/AVD (`*(fw+0x30)+0x3c`)

Did **not** use `disassemble_bytes`. Emulation skipped (pointer/setup graph).

---

## 1. Decompile (authoritative)

### 1.1 `hkDefaultChassis_ctor` @ `0x64fdf0`

```c
// __thiscall: ECX = this, stack arg = CCS descriptor*, ret 4
undefined4 * __thiscall hkDefaultChassis_ctor(undefined4 *this, undefined4 ccsDesc)
{
  FUN_0065eac0(ccsDesc);          // base hkChassisComponent ctor (this in ECX)
  *this = &PTR_FUN_009e4fd0;      // install hkDefaultChassis vtable
  return this;
}
```

Assembly (full body):

```
0064fdf0  MOV  EAX, [ESP+0x4]     ; ccsDesc
0064fdf4  PUSH ESI
0064fdf5  PUSH EAX
0064fdf6  MOV  ESI, ECX           ; save this
0064fdf8  CALL FUN_0065eac0       ; base ctor
0064fdfd  MOV  dword [ESI], 0x9e4fd0
0064fe03  MOV  EAX, ESI
0064fe05  POP  ESI
0064fe06  RET  4
```

**No stores of a rigid-body pointer.** Layout beyond the vtable is filled only by the base CCS init.

### 1.2 Base `FUN_0065eac0` (`hkChassisComponent` ctor) @ `0x65eac0`

```c
undefined4 __thiscall FUN_0065eac0(undefined4 *this, undefined4 ccsDesc)
{
  *(undefined2 *)((int)this + 6) = 1;   // refcount = 1
  *this = &PTR_FUN_009e728c;            // hkChassisComponent vtable
  FUN_0065e6c0(ccsDesc);                // fill CCS type + basis (this in ECX)
  return this;                          // EAX = ECX
}
```

### 1.3 CCS fill `FUN_0065e6c0` @ `0x65e6c0`

```c
// __thiscall: ECX = chassis object, stack = ccsDesc*
// ccsDesc: +0 = int8 CCS enum (0..11), +4 = dword copied to this+0xc
void __thiscall FUN_0065e6c0(int this, undefined1 *ccsDesc)
{
  *(undefined1 *)(this + 8) = *ccsDesc;     // CCS enum
  // switch(*ccsDesc) writes three vec4 basis columns at
  //   this+0x10..+0x1c, +0x20..+0x2c, +0x30..+0x3c
  // using g_flOne (1.0 @ 0xa0f2a0) and DAT_00aaa668 (-1.0)
  // every branch that writes +0x3c stores **float 0.0**
  *(undefined4 *)(this + 0xc) = *(undefined4 *)(ccsDesc + 4);
}
```

CCS name strings sit next to the base class reflection (`CCS_*_UP_*_RIGHT_*_FORWARD` @ `0x9e74c0+`).

---

## 2. Object layout (`hkDefaultChassis`, size `0x40`)

| Offset | Type | Set by | Meaning |
|-------:|------|--------|---------|
| `+0x00` | ptr | ctor | Vtable → `PTR_FUN_009e4fd0` |
| `+0x04` | u16 | allocator (before ctor) | Object size **`0x40`** |
| `+0x06` | u16 | base ctor | **Refcount** = `1` |
| `+0x08` | i8 | `FUN_0065e6c0` | **CCS enum** (builder passes **`6`**) |
| `+0x0c` | dword | `FUN_0065e6c0` from `ccsDesc+4` | Scalar from entity **vtbl+0x28** (`FSTP` at build site) — consumed as **mass-like** input by `hkVehicleFramework_initFromDescriptor` via `desc[0]+0xc` |
| `+0x10..+0x1c` | vec4 | CCS switch | Basis column 0 (local axes) |
| `+0x20..+0x2c` | vec4 | CCS switch | Basis column 1 |
| `+0x30..+0x3c` | vec4 | CCS switch | Basis column 2; **`+0x3c` = float `0.0` (w lane)** |

### Builder call site (CCS arg)

`Vehicle_buildHavokVehicleFramework` @ `0x5fd467`–`0x5fd4a0`:

```
CALL  [entity.vtbl + 0x28]     ; float return in ST0
FSTP  [stack ccsDesc + 4]      ; → becomes chassis+0xc
MOV   byte [ccsDesc + 0], 6    ; CCS type = 6
alloc 0x40
CALL  hkDefaultChassis_ctor(ccsDesc)
```

Type **`6`** CCS basis (from `FUN_0065e6c0` case 6 / asm):

```
+0x20..+0x2c = (0, 1, 0, 0)
+0x30..+0x3c = (-1, 0, 0, 0)     ; DAT_00aaa668 = -1.0 at +0x30; +0x3c = 0
+0x10..+0x1c = (0, 0, 1, 0)      ; after fall-through zero of y/w lanes
```

Constants (`read_memory`):

| Symbol | Address | LE bytes | float32 |
|--------|---------|----------|---------|
| `g_flOne` | `0x00a0f2a0` | `00 00 80 3f` | **`1.0`** |
| `DAT_00aaa668` | `0x00aaa668` | `00 00 80 bf` | **`-1.0`** |

---

## 3. RB pointer slot `+0x3c` — authoritative

### 3.1 What this ctor does **not** do

`hkDefaultChassis_ctor` / base CCS init **never** write a live `hkRigidBody*`.

At the end of construction, **`this+0x3c` is float `0.0`** (matrix w-component), which is a null pointer if misread as `void*`.

Map one-liners that say “`hkDefaultChassis` holds `hkRigidBody` @ `+0x3c`” **conflate two different objects** that both expose a `+0x3c` field.

### 3.2 Where the live rigid body actually lives

Runtime vehicle components resolve the chassis body as:

```
framework = *(component + 0x08)
bodyWrap  = *(framework + 0x30)     // desc[9] after wireComponents
rb        = *(bodyWrap + 0x3c)      // hkRigidBody*
```

Examples (already verified elsewhere):

| Consumer | Addr | Chain |
|----------|------|-------|
| `hkDefaultAerodynamics_update` | `0x64dae0` | `*( *(aero+8)+0x30 ) + 0x3c` |
| `hkDefaultSteering_update` | `0x64f840` | `*( *(parent+0x30)+0x3c )` |
| `hkAngularVelocityDamper_update` | `0x64d810` | `*( *(ctx+0x30)+0x3c )` |
| `hkVehicleFramework_initFromDescriptor` | `0x64b2b0` | `*( *(fw+0x30)+0x3c )` then RB vtbl / motion |

### 3.3 Descriptor wiring (why `fw+0x30` is not this `0x40` object)

`FUN_00651fd0` zeros **desc[0..9]**. Component / body pointers filled by `0x5fd390`:

| desc index | Build store (ESP-relative after stable stack) | `wireComponents` → `fw` |
|-----------:|-----------------------------------------------|-------------------------|
| **[0]** | **`hkDefaultChassis*`** (this ctor) @ `[ESP+0x50]` | **`fw+0x10`** |
| [1] | `hkDefaultWheels*` @ `[ESP+0x54]` | `fw+0x0c` |
| [2] | driver input (`param_4`) | `fw+0x14` |
| … | steering / collide / trans / brake / aero / susp | `+0x18..+0x2c` |
| **[9]** | **`entity+0x08` physics object** (`buildFramework` **param_5**, from `Vehicle_createVehicleAction`) | **`fw+0x30`** |

`Vehicle_createVehicleAction` @ `0x4fb660`:

```c
Vehicle_buildHavokVehicleFramework(
    entity,
    world,
    0,
    driverInput,          // param_4 → desc slot used as input component
    *(entity + 0x08),     // param_5 → physics object → desc[9] → fw+0x30
    0);
```

Therefore:

```
rb = *(*(fw + 0x30) + 0x3c)
   = *( physicsObject + 0x3c )
   = same body as *(*(entity + 0x08) + 0x3c)
```

The **`0x40` `hkDefaultChassis` instance is at `fw+0x10`**, not `fw+0x30`.  
Its job is **CCS axes + mass scalar**, not ownership of the Havok rigid body.

### 3.4 Correct mental model

```
entity+0x08  ──► physics object ──► +0x3c ──► hkRigidBody  (pose, vel, invMass, …)
                     ▲
                     │ desc[9] / fw+0x30
hkVehicleFramework ──┘

entity vtbl+0x28 ──FSTP──► ccsDesc+4 ──► hkDefaultChassis+0x0c  (mass-like)
CCS type 6 ──────────────► hkDefaultChassis+0x08 / +0x10..+0x3c (axes)
                     ▲
                     │ desc[0] / fw+0x10
hkVehicleFramework ──┘
```

RB field map (on the **rigid body**, not on `hkDefaultChassis`): see
[`fn_offsets_rigidbody.md`](fn_offsets_rigidbody.md).

---

## 4. Vtable `PTR_FUN_009e4fd0` (slot summary)

| Slot | Address | Role (from decompile / bytes) |
|-----:|---------|-------------------------------|
| `+0x00` | `0x64fe10` | Scalar-deleting dtor → `FUN_005ee650` + free |
| `+0x04` | `0x5ffd80` | Shared ref helper |
| `+0x08` | `0x5ffdb0` | Shared ref flag helper |
| `+0x0c` | `0x64fe70` | `getClass` → `DAT_00d032ac` |
| `+0x10` | `0x5ffc80` | Empty stub |
| `+0x14` | `0x64fe80` | Factory: alloc `0x40` + `hkDefaultChassis_ctor` |
| `+0x18` | `0x64feb0` | Optional CCS reset via `FUN_0065eb10` |
| `+0x20` | `0x64fe50` | Serialize/export CCS → `FUN_0065ea90` |
| `+0x24` | `0x64fe60` | Apply CCS → `FUN_0065e6c0` |

Not a per-tick update component: `wireComponents` does **not** put this object on the ticked `fw+0x14..+0x2c` list.

---

## 5. Reconciliation with Phase-0 maps

| Map claim | Binary result |
|-----------|---------------|
| `hkDefaultChassis_ctor` @ `0x64fdf0`, size `0x40` | **Match** |
| “Holds `hkRigidBody` @ `+0x3c`” on **this** object | **Mismatch** — `+0x3c` is CCS matrix **w=0**; RB is on **physics object** `+0x3c` |
| Framework chassis access `*(fw+0x30)+0x3c` | **Match** as RB path, but `fw+0x30` is **physics object** (`entity+8`), not the `0x40` chassis component |
| Chassis built after wheels, before steering | **Match** (`0x5fd390`) |
| Body created in this ctor | **No** — body from clonebase / physics loader; only wrapped via `entity+8` |

**Porting note:** Server ports that need the live chassis rigid body should follow  
`entity.physics + 0x3c` (or the framework equivalent `fw+0x30 + 0x3c`), **not** the CCS component at `fw+0x10`.  
Use `hkDefaultChassis` only for **local axes / mass scalar** if reproducing `initFromDescriptor` inertia setup.

---

## 6. Status

| Check | Result |
|-------|--------|
| Primary decompile | **Done** |
| Base + CCS path | **Done** |
| `+0x3c` role on `hkDefaultChassis` | **Verified: float w lane = 0, not RB** |
| Live RB `+0x3c` owner | **Verified: physics object @ `entity+8` / `fw+0x30`** |
| C# port from this file | N/A (RE doc only) |

# VERIFIED — `FUN_00597e90` + VehicleAction vtable `applyAction` slot

**Program:** `autoassault.exe` (image base `0x400000`)  
**Re-verified:** 2026-07-15  
**Tools:** `decompile_function` @ `0x597e90`, `0x597f90`, `0x599520`, `0x636290`, `0x636370`, `0x4fb660`;  
`read_memory` @ `0x9d54c4` (32 B), `0x9d54b0` (32 B), `0x9d54e0`, `0x9c7bc0`;  
`get_function_by_address` / `get_function_callers` / `get_xrefs_to`.

**Verdict (requested):** primary vtable **`PTR_FUN_009d54c4`** slot **`+0x14` = `0x00598650` = `VehicleAction_applyAction`** — **CONFIRMED**.

---

## 0. Identity correction (important)

| Address | Ghidra symbol | Role |
|--------:|--------------|------|
| **`0x00597e90`** | `FUN_00597e90` | **Non-deleting destructor body** (not the ctor) |
| **`0x00597f90`** | `VehicleAction_ctor` | **Actual constructor** |
| **`0x00599520`** | `FUN_00599520` | Scalar deleting dtor — vtable **slot +0x00**; calls `FUN_00597e90` then pool free |

Legacy notes (`0.1-step-rate.md`) labeled `FUN_00597e90` as “ctor” because it (and the real ctor) both **write** `*this = &PTR_FUN_009d54c4`. That write is true for **both** the ctor path and the classic MSVC dtor path (set most-derived vtbl, then chain base).

This file keeps the requested path name (`fn_00597e90_…Ctor`) but documents **both** entry points and the shared vtable proof.

---

## 1. `FUN_00597e90` — non-deleting dtor (body `0x597e90`–`0x597ea1`)

### 1.1 Decompile (verbatim)

```c
void __fastcall FUN_00597e90(undefined4 *param_1)
{
  *param_1 = &PTR_FUN_009d54c4;       // this+0x00 = primary VA vtable
  param_1[2] = &PTR_LAB_009d54b0;     // this+0x08 = secondary VA vtable
  FUN_00636290();                     // base / hkAction teardown (overwrites vtbls)
  return;
}
```

### 1.2 Sole direct caller — scalar deleting dtor

```c
// FUN_00599520 @ 0x599520  (vtbl[0])
int __thiscall FUN_00599520(int param_1, byte param_2)
{
  FUN_00597e90();                     // ~VehicleAction
  if ((param_2 & 1) != 0) {
    // custom pool free via allocator vtbl +0x14
    (**(code **)(*DAT_00b05060 + 0x14))(param_1, *(undefined2 *)(param_1 + 4), 0x24);
  }
  return param_1;
}
```

MSVC pattern: `vtbl[+0x00]` = deleting dtor; body restores most-derived vtables, then chains into base dtor `FUN_00636290` (which sets intermediate base vtables, drops ref on linked object at `this+0x18`, ends on `PTR_LAB_009cc290`).

---

## 2. `VehicleAction_ctor` @ `0x597f90` — real constructor

### 2.1 Call graph

```
Vehicle_createVehicleAction          0x4fb660
  alloc VehicleAction size 0x48 (pool; u16 size stamp at +4)
  └ VehicleAction_ctor               0x597f90
       └ FUN_00636370(param_3)       0x636370   // base / hkAction-like ctor
       └ (optional) FUN_00597ec0     0x597ec0   // when mode param == 1
  **(entity+0x1a0) = VehicleAction*
  FUN_0055fe50(action)               // register into world/island action list
```

Plate on `Vehicle_createVehicleAction`:  
`VehicleAction_ctor(entity, rb, framework, mode)` with `this` = pooled `0x48` object;  
`entity+0x1a0` becomes `{ VehicleAction*, hkVehicleFramework*, driverInput }`.

### 2.2 Decompile (field layout)

```c
// VehicleAction_ctor @ 0x597f90  (body 0x597f90–0x598033)
// __thiscall this = VehicleAction*
VehicleAction_ctor(this, param_2, param_3, param_4, param_5 /*mode*/)
{
  FUN_00636370(param_3);              // base init; wires this+0x18 = param_3 when non-null

  *(u8*)(this + 0x1c) = 0;            // param_1[7]  as byte  — in-collision flag
  *(u8*)(this + 0x2c) = 0;            // param_1[0xb] as byte — all-wheels-airborne
  *(this + 0x3c) = 0;                 // param_1[0xf]         — steering/vehicle inst
  *(this + 0x20) = DAT_009d54e0;      // param_1[8]  = 2.8571429…  (see §4)
  *this        = &PTR_FUN_009d54c4;   // primary vtable
  *(this + 0x8) = &PTR_LAB_009d54b0;  // secondary vtable
  *(this + 0x24) = 0;                 // steer ramp stage 1
  *(this + 0x28) = 0;                 // steer final
  *(this + 0x30) = DAT_009c7bc0;      // 9999999.0 — boost timer sentinel
  *(this + 0x34) = 0;                 // boost cooldown
  *(this + 0x38) = param_5;           // mode / branch flag
  *(this + 0x40) = param_4;           // wheel/framework container
  *(this + 0x44) = param_2;           // entity back-ref (CVOGVehicle*)

  if (param_5 == 1)
    FUN_00597ec0();                   // extra steering/setup path

  return this;
}
```

Offsets match `docs/reconstruction/physics/0.8-struct-offsets.md` §2 (Havok VehicleAction).

### 2.3 Object size

| Evidence | Size |
|----------|-----:|
| `Vehicle_createVehicleAction` pool alloc | **`0x48`** (u16 stamp at `object+4`) |
| Live field ceiling | `this+0x44` entity ptr → object spans at least through `+0x47` |

---

## 3. Primary vtable — `PTR_FUN_009d54c4` @ `0x009d54c4`

Installed by:

* ctor: `VehicleAction_ctor` @ `0x597fed` (DATA xref)
* dtor body: `FUN_00597e90` @ `0x597e90` (DATA xref)

### 3.1 Raw `read_memory` (32 bytes LE)

```
009d54c4:  20 95 59 00  80 fd 5f 00  b0 fd 5f 00  00 64 63 00
009d54d4:  80 fc 5f 00  50 86 59 00  c0 63 63 00  6e db 36 40
```

| Slot | Off | Pointer | Symbol / note |
|-----:|----:|--------:|---------------|
| 0 | `+0x00` | `0x00599520` | scalar deleting dtor → `FUN_00597e90` |
| 1 | `+0x04` | `0x005ffd80` | `hkAnalogDI_vtbl1` (flag-gated helper) |
| 2 | `+0x08` | `0x005ffdb0` | set/clear high bit on `this+5` |
| 3 | `+0x0c` | `0x00636400` | short stub (`mov eax, …; ret`) |
| 4 | `+0x10` | `0x005ffc80` | empty `ret` |
| **5** | **`+0x14`** | **`0x00598650`** | **`VehicleAction_applyAction`** ← **CONFIRMED** |
| 6 | `+0x18` | `0x006363c0` | push `this+0x18` into growable ptr array |
| — | `+0x1c` | `0x4036db6e` | **not a vtable slot** — start of `DAT_009d54e0` float |

### 3.2 applyAction proof

| Check | Result |
|-------|--------|
| Bytes at `0x9d54c4 + 0x14` | `50 86 59 00` → **`0x00598650`** |
| `get_function_by_address(0x598650)` | **`VehicleAction_applyAction`** (body `0x598650`–`0x5994c4`) |
| Dispatch model | Havok island step → action list → `(*vtbl)[+0x14](action, stepInfo)` |
| Docs cross-ref | `0.1-step-rate.md`, `NPCDriving.md`, `0.8-struct-offsets.md` |

**Slot arithmetic:** MSVC 32-bit vtable, 4-byte entries → index `5` = byte offset `0x14`.

### 3.3 Secondary vtable — `PTR_LAB_009d54b0` @ `0x009d54b0` (`this+0x08`)

```
009d54b0:  d0 94 59 00  80 fc 5f 00  70 62 63 00  80 fc 5f 00 …
```

| Off | Pointer | Note |
|----:|--------:|------|
| `+0x00` | `0x005994d0` | secondary-interface entry (no named function at addr in Ghidra) |
| `+0x04` | `0x005ffc80` | empty `ret` |
| `+0x08` | `0x00636270` | (no function defined) |
| `+0x0c` | `0x005ffc80` | empty `ret` |

Dual-vtable layout is consistent with Havok `hkReferencedObject` / `hkAction` multiple inheritance; **applyAction is on the primary table only**.

---

## 4. Ctor DAT constants (`read_memory`)

| Symbol | Address | Bytes (LE) | Value | Written to |
|--------|--------:|------------|------:|------------|
| `DAT_009d54e0` | `0x009d54e0` | `6e db 36 40` | **≈ 2.8571429** (`0x4036db6e`) | `VehicleAction+0x20` (throttle slot; initial value — not 0) |
| `DAT_009c7bc0` | `0x009c7bc0` | `7f 96 18 4b` | **9999999.0** | `VehicleAction+0x30` (boost timer sentinel) |

`DAT_009d54e0` sits immediately after the primary vtable function pointers (at `vtbl+0x1c`); do **not** treat it as an 8th virtual.

---

## 5. Base ctor / dtor chain (brief)

| Fn | Addr | Role |
|----|-----:|------|
| `FUN_00636370` | `0x636370` | Base ctor: refcount word, dual base vtables, optional `this+0x18` link + addref |
| `FUN_00636290` | `0x636290` | Base dtor chain: release `this+0x18`, cycle base vtables, clear weak bit via `FUN_005ffdb0` |

VehicleAction layers its own dual vtables on top of this base.

---

## 6. Port / RE implications

1. **Dispatch:** any code that must invoke the per-tick driver uses **`*(void**)(action+0) + 0x14` → `VehicleAction_applyAction`**. Hardcoding `0x598650` matches retail only for this build; the slot offset is the stable ABI.
2. **Lifetime:** server-side “has VehicleAction” means entity host at `entity+0x1a0` holds a live object whose primary vtbl slot `+0x14` is the applyAction equivalent — without it, thr/steer never reach Havok (`FUN_004fbc10` becomes a no-op on null action).
3. **Do not confuse** `FUN_00597e90` (dtor) with `VehicleAction_ctor` (`0x597f90`). Both touch the same vtable symbols; only the ctor initializes fields / mode / entity back-ref.

---

## 7. Verification checklist

| Item | Status |
|------|--------|
| Decompile `FUN_00597e90` | OK — sets `PTR_FUN_009d54c4` / `PTR_LAB_009d54b0`, chains `FUN_00636290` |
| Decompile `VehicleAction_ctor` `0x597f90` | OK — same vtable install + field init |
| `read_memory` `0x9d54c4+0x14` | **`50 86 59 00` = `0x00598650`** |
| Symbol at `0x598650` | `VehicleAction_applyAction` |
| Caller of ctor | `Vehicle_createVehicleAction` @ `0x4fb660` |
| Object size | `0x48` |

**No C# / production code changed** — documentation only.

# Verified: `FUN_005d6640` @ `0x005d6640` (`hkDefaultEngine` ctor — historical “wheel-collide” slot)

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`. **No C# in this file.**

| Item | Value |
|------|--------|
| Address | `0x005d6640` |
| Body | `0x005d6640` – `0x005d669c` |
| Ghidra name | `FUN_005d6640` |
| Historical label | “raycast wheel-collide component” (size `0x3c`) in `setup-field-mapping.md` / `fn_005fd390_buildFramework.md` |
| **Binary identity** | **`hkDefaultEngine` constructor** |
| Convention | MSVC `__thiscall` (`this` = component; stack arg = 10-dword desc) |
| Allocation size | **`0x3c`** (`*(u16*)(obj+4) = 0x3c` at build site) |
| Vtable installed | `PTR_FUN_009dad44` @ `0x009dad44` |
| Descriptor builder | `FUN_005fc3d0` @ `0x5fc3d0` (see `fn_005fc3d0_wheelCollideBuilder.md`) |
| Sole framework caller | `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (call site `0x5fd590`) |
| Second xref | `0x5d67dc` (copy-ctor / clone path adjacent to vtable; not the build path) |

---

## Tools used (this verification)

1. **`decompile_function`** `0x5d6640` (ctor body)
2. **`decompile_function`** `0x5fc3d0` (descriptor fill), `0x5fd390` (alloc + call site), `0x64f750` (post-helper)
3. **`read_memory`** vtable/reflection block `0x9dad44` length 256; class/field strings `0x9dae18..0x9daed6`; defaults `0x9daecc` / `0xa0f520` / `0xa0f6bc` / `0xaaaae8`
4. **`get_function_xrefs`** / **`get_xrefs_to`** on `0x5d6640`, `0x9dad44`, `0x5d66a0`
5. Cross-check: `fn_005fc3d0_wheelCollideBuilder.md`, `fn_0064bbd0_wheelCollide.md`, `fn_005fd390_speedGovernor.md`

Did **not** use `disassemble_bytes`. No pure-math `DAT_*` in the ctor body itself (constants live in the defaults helper / runtime update).

---

## 1. Naming correction (critical)

Setup maps label this **size-`0x3c`** construction step as **wheel-collide**. The binary identity is **`hkDefaultEngine`**.

| Evidence | Detail |
|---|---|
| Class string | `"hkDefaultEngine"` immediately after engine defaults @ `0x9daed6` (scan often shows `EhkDefaultEngine` if the preceding float bytes are glued) |
| Reflection members @ `0x9dad60` | 10× float: `minRPM`, `optRPM`, `maxRPM`, `torque`, `torqueFactorAtMinRPM`, `torqueFactorAtMaxRPM`, `resistanceFactorAtMinRPM`, `resistanceFactorAtOptRPM`, `resistanceFactorAtMaxRPM`, `clutchSlipRPM` |
| Construction slot | After steering, before transmission — classic Havok **engine** slot |
| Vtable methods | `FUN_005d66a0` / `FUN_005d6460` — RPM / torque-factor / resistance curves (not raycast) |
| True wheel cast | Framework vtbl `+0x20` → `0x64bbd0` → `TtPhantom::castRay` (`fn_0064bbd0_wheelCollide.md`) — **separate object** |

**Filename keeps the historical `wheelCollideComp` token for discoverability; treat the symbol as the engine component ctor.**

---

## 2. Decompile (authoritative)

```c
// FUN_005d6640 @ 0x005d6640
// this  = ECX = heap component (size 0x3c)
// desc  = stack arg = float[10] from FUN_005fc3d0
void __thiscall FUN_005d6640(undefined4 *this, undefined4 *desc)
{
  *(undefined2 *)((int)this + 6) = 1;   // +0x06 refcount / flags halfword
  this[4] = 0;                          // +0x10
  this[3] = 0;                          // +0x0c
  *this = &PTR_FUN_009dad44;            // +0x00 vtable

  this[5]  = desc[0];   // +0x14  minRPM
  this[6]  = desc[1];   // +0x18  optRPM
  this[7]  = desc[2];   // +0x1c  maxRPM
  this[8]  = desc[3];   // +0x20  torque
  this[9]  = desc[4];   // +0x24  torqueFactorAtMinRPM
  this[10] = desc[5];   // +0x28  torqueFactorAtMaxRPM
  this[11] = desc[6];   // +0x2c  resistanceFactorAtMinRPM
  this[12] = desc[7];   // +0x30  resistanceFactorAtOptRPM
  this[13] = desc[8];   // +0x34  resistanceFactorAtMaxRPM
  this[14] = desc[9];   // +0x38  clutchSlipRPM

  return;  // Ghidra void; caller keeps component pointer in its own stack temp
}
```

### Properties

- **Leaf** (no callees). No base-ctor helper (unlike aero/suspension ctors that call a shared `FUN_0065d880`-style init).
- **Pure copy** of 10 dwords after a small header init.
- Does **not** touch `+0x08` (framework back-pointer). That is written later by `hkVehicleFramework_wireComponents` @ `0x636940` (`comp+8 = framework`).

---

## 3. Allocation / call site

From `Vehicle_buildHavokVehicleFramework` @ `0x5fd390` (component #4 in construction order):

```
FUN_005fc3d0(entity, ctx, afStack_110)     // fill float[10] engine desc
obj = allocator(0x3c, 10)
*(u16*)(obj + 4) = 0x3c                    // stored object size
FUN_005d6640(obj, desc)                    // thiscall: ECX=obj, arg=desc
FUN_0064f750(...)                          // post-helper on the *framework setup desc*
                                           // (hkArray empty pattern — NOT the engine object)
```

`FUN_0064f750` zeros array-capacity sentinels (`0x80000000`) at offsets that **overlap** engine float slots if applied to the same object. It is therefore a **setup-descriptor / register helper**, not a second pass over the engine component. Do not model it as wiping `+0x14..+0x38`.

---

## 4. Component layout after ctor (size `0x3c`)

| Offset | Type | Init | Meaning |
|-------:|------|------|---------|
| `+0x00` | ptr | `0x009dad44` | `hkDefaultEngine` vtable |
| `+0x04` | u16 | `0x3c` | Object size (allocator stamp; **not** written by ctor) |
| `+0x06` | u16 | `1` | Refcount / alive flag (ctor) |
| `+0x08` | ptr | *(later)* | Parent **framework** (`wireComponents`) |
| `+0x0c` | f32/int | `0` | Runtime slot zeroed by ctor (engine update writes nearby outputs) |
| `+0x10` | f32/int | `0` | Runtime slot zeroed by ctor |
| `+0x14` | f32 | `desc[0]` | **`minRPM`** |
| `+0x18` | f32 | `desc[1]` | **`optRPM`** |
| `+0x1c` | f32 | `desc[2]` | **`maxRPM`** |
| `+0x20` | f32 | `desc[3]` | **`torque`** (peak) |
| `+0x24` | f32 | `desc[4]` | **`torqueFactorAtMinRPM`** |
| `+0x28` | f32 | `desc[5]` | **`torqueFactorAtMaxRPM`** |
| `+0x2c` | f32 | `desc[6]` | **`resistanceFactorAtMinRPM`** |
| `+0x30` | f32 | `desc[7]` | **`resistanceFactorAtOptRPM`** |
| `+0x34` | f32 | `desc[8]` | **`resistanceFactorAtMaxRPM`** |
| `+0x38` | f32 | `desc[9]` | **`clutchSlipRPM`** |
| `+0x3c` | — | end | Object end (`0x14 + 10*4 = 0x3c`) |

### Descriptor → component (1:1)

```
comp[+0x14 + 4*i] = desc[i]    for i in 0..9
```

No scale, clamp, or reordering inside the ctor. All VehSpec / entity transforms happen in **`FUN_005fc3d0`**.

---

## 5. Descriptor contents (summary — full map in builder doc)

| `desc[i]` | Comp off | Havok name | VehSpec | Transform |
|---:|---:|---|---|---|
| 0 | `+0x14` | `minRPM` | `+0x6a8` `rlMinimumRPM` | × `entity+0x1fc` |
| 1 | `+0x18` | `optRPM` | `+0x6ac` `rlOptimumRPMMin` | × `entity+0x1fc` |
| 2 | `+0x1c` | `maxRPM` | `+0x6b4` `rlMaximumRPMMax` | × `entity+0x1fc` |
| 3 | `+0x20` | `torque` | `+0x69a` `sinTorqueMax` (i16) | `(float)((int)i16 + *(i32*)(entity+0x218))` |
| 4 | `+0x24` | `torqueFactorAtMinRPM` | `+0x6a0` | copy |
| 5 | `+0x28` | `torqueFactorAtMaxRPM` | `+0x6a4` | copy |
| 6 | `+0x2c` | `resistanceFactorAtMinRPM` | `+0x6b8` | copy |
| 7 | `+0x30` | `resistanceFactorAtOptRPM` | `+0x6bc` | copy |
| 8 | `+0x34` | `resistanceFactorAtMaxRPM` | `+0x6c0` | copy |
| 9 | `+0x38` | `clutchSlipRPM` | — | **`= desc[0]`** (scaled minRPM) |

**Not consumed:** `VehSpec+0x6b0` `rlOptimumRPMMax` (Havok has a single `optRPM`; AA maps `OptimumRPMMin` only).

---

## 6. Vtable `PTR_FUN_009dad44` @ `0x009dad44`

| Slot | Address | Role (from decompile / pattern) |
|---:|---|---|
| `+0x00` | `0x0064fe10` | dtor / free path (`FUN_0064fe10` → optional heap free of `*(u16*)(this+4)` size) |
| `+0x04` | `0x005ffd80` | shared component vcall (`hkAnalogDI_vtbl1`-family) |
| `+0x08` | `0x005ffdb0` | flag set/clear on `this+5` |
| `+0x0c` | `0x005d67b0` | (adjacent clone/copy region; Ghidra may not have a named function) |
| `+0x10` | `0x005ffc80` | empty stub (`ret`) |
| `+0x14` | `0x005d66a0` | **engine update** (`FUN_005d66a0` — RPM / clutch / torque-resistance path; calls `FUN_005d6460`) |

`tickSubsystems` @ `0x636a60` invokes **`comp.vtbl+0x14`** on each of the seven ticked framework slots. If this engine object is wired into one of those slots, `FUN_005d66a0` runs every tick.

### Runtime field helpers (not the ctor; for layout consumers)

| Addr | Role |
|---|---|
| `FUN_005d63c0` @ `0x5d63c0` | **export** `this+0x14..+0x38` → float[10] |
| `FUN_005d6410` @ `0x5d6410` | **import** float[10] → `this+0x14..+0x38` |
| `FUN_005d6720` @ `0x5d6720` | **default desc** fill (see §7) |
| `FUN_005d66a0` @ `0x5d66a0` | update (reads `+0x14..+0x38`, `this+8` framework) |
| `FUN_005d6460` @ `0x5d6460` | torque/resistance curve helper used by update |

---

## 7. Default descriptor helper `FUN_005d6720` @ `0x5d6720`

Not called by the ctor. Used as a defaults factory (also from `FUN_004c4e80` and the framework build path near steering → engine).

| Index | Value | Source | Meaning |
|---:|---:|---|---|
| 0 | **1000.0** | `DAT_00a0f520` | minRPM |
| 1 | **4000.0** | `DAT_00a0f6bc` | optRPM |
| 2 | **6000.0** | `DAT_009daed0` | maxRPM |
| 3 | **800.0** | `DAT_009daecc` | torque |
| 4 | **1.0** | `g_flOne` | torqueFactorAtMinRPM |
| 5 | **1.0** | `g_flOne` | torqueFactorAtMaxRPM |
| 6–8 | **0.0** | imm | resistance factors |
| 9 | **2000.0** | `DAT_00aaaae8` | clutchSlipRPM |

On the **vehicle build** path, `FUN_005fc3d0` **fully overwrites** all ten slots from VehSpec, so these defaults do not remain in the live component.

Constants (`read_memory`):

| Symbol | Addr | LE | float32 |
|---|---|---|---|
| `DAT_00a0f520` | `0x00a0f520` | `00 00 7a 44` | **1000.0** |
| `DAT_00a0f6bc` | `0x00a0f6bc` | `00 00 7a 45` | **4000.0** |
| `DAT_009daecc` | `0x009daecc` | `00 00 48 44` | **800.0** |
| `DAT_009daed0` | `0x009daed0` | `00 80 bb 45` | **6000.0** |
| `DAT_00aaaae8` | `0x00aaaae8` | `00 00 fa 44` | **2000.0** |

---

## 8. What this is **not**

| Claim in older maps | Reality |
|---|---|
| “Wheel-collide component” | **False** for this object — wheel cast is framework method `0x64bbd0` |
| `desc[0..2]` = radius / width / final-drive | **False** — those are **min/opt/max RPM**; radius is `VehSpec+0x600[i]` via wheels builder; final drive is `+0x6c4` via transmission |
| `desc[3]` = collision filter info | **False** — `sinTorqueMax` (i16) + `entity+0x218` |
| “No `hkDefaultEngine`” | **Overstated** — component **is** constructed and can be ticked; **drive torque for NPC/port** is still primarily `VehicleAction_calcWheelTorque` + `torqueCurve2D`, not a full Havok engine replacement of that path |

---

## 9. Conflicts vs prior evidence

| Item | Prior | This re-verify | Verdict |
|---|---|---|---|
| Role of `FUN_005d6640` | raycast wheel-collide ctor | **`hkDefaultEngine` ctor** | **binary wins** |
| Size `0x3c` | yes | yes | **match** |
| Vtable `0x9dad44` | yes | yes + class string | **match** (identity upgraded) |
| Copies 10 dwords from desc | yes | yes → `+0x14..+0x38` | **match** |
| Post `FUN_0064f750` | “then …” on component | setup-desc helper (array empty pattern) | **do not apply to engine object** |
| Engine absent from build | “no hkDefaultEngine” | **present** as this component | **docs that deny construction are wrong**; residual-torque notes may still apply to *usage* |

---

## 10. Porting notes (doc only)

1. Port this as an **`hkDefaultEngine`** (or equivalent) object of size **`0x3c`**, vtable methods as needed for fidelity.
2. Fill **`+0x14..+0x38`** from the 10-float descriptor built by `FUN_005fc3d0` rules exactly.
3. **Do not** treat this object as the wheel raycast implementer; keep collide on the framework / phantom path.
4. **Do not** assume constructing this component alone replaces AA `calcWheelTorque` — verify whether your port’s tick path actually consumes `FUN_005d66a0` output before wiring drive force to it.
5. `wireComponents` must set **`engine+0x08 = framework`** if the update method is used (`FUN_005d66a0` reads `*(this+8)`).

---

## 11. Related addresses

| Addr | Role |
|---|---|
| `0x005fc3d0` | Engine **descriptor builder** (`fn_005fc3d0_wheelCollideBuilder.md`) |
| `0x005fd390` | Framework build — sole normal caller |
| `0x005d66a0` / `0x005d6460` | Engine update / curve helpers |
| `0x005d63c0` / `0x005d6410` | export / import of the 10 floats |
| `0x005d6720` | default engine desc factory |
| `0x00636940` | `wireComponents` (sets `+0x08`) |
| `0x0064bbd0` | **True** wheel-collide cast entry |
| `0x005fcce0` | Wheels radius/width builder |
| `0x009dad44` | Engine vtable |
| `0x009dad60` | Engine reflection member table |
| `0x009dae18`…`0x9daec4` | Field name strings (`clutchSlipRPM` … `minRPM`) |

---

## 12. Emulation

Not useful for this function: pointer-free but pure stores. Correctness check is a 10-float golden: feed a known descriptor → expect identical dwords at `obj+0x14..+0x38`, vtable `0x9dad44`, `*(u16*)(obj+6)==1`, `+0x0c/+0x10==0`.

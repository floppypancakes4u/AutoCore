# Verified: `hkDefaultAerodynamics_ctor` @ `0x0064da90`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Reconciled against map evidence `docs/reconstruction/physics/0.6-aerodynamics.md` and
builder note `verified/fn_005fc4f0_aeroBuilder.md`.

| Item | Value |
|------|--------|
| Address | `0x0064da90` |
| Body | `0x0064da90` – `0x0064dad9` |
| Ghidra name | `hkDefaultAerodynamics_ctor` |
| Calling convention | MSVC `__thiscall` (`this` in ECX; descriptor on stack; `ret 4`) |
| Role | Base-init aerodynamics component, install aero vtable, copy 8-word descriptor → `this+0x30..+0x4c` |
| Allocation size (framework) | **`0x50`** bytes (`Vehicle_buildHavokVehicleFramework` path) |
| Vtable installed | `PTR_FUN_009e4b20` (`0x009e4b20`) |

## Tools used (this verification)

1. **`decompile_function`** `0x64da90` (Ghidra MCP / HTTP)
2. **`read_memory`** `0x64da90` length=80 (raw body for ECX vs stack-arg resolution)
3. **`decompile_function`** `0x65d880` (`FUN_0065d880` base init) + **`read_memory`** of its body (confirm `ret 4` + ECX object)
4. **`get_xrefs_to`** `0x64da90`
5. Cross-check sibling ctors: `hkAngularVelocityDamper_ctor` `0x64d900`, `hkDefaultSuspension_ctor` `0x64e510`

Did **not** use `disassemble_bytes`. No formula `DAT_*` floats in this function (`read_memory` for constants N/A beyond code bytes / vtable VA).

## Decompile (Ghidra, with correction)

Ghidra emits:

```c
undefined4 * __thiscall hkDefaultAerodynamics_ctor(undefined4 *param_1, undefined4 *param_2)
{
  FUN_0065d880(param_2);          // DECOMPILER BUG: see §Base init
  *param_1 = &PTR_FUN_009e4b20;
  param_1[0xc] = *param_2;
  param_1[0xd] = param_2[1];
  param_1[0xe] = param_2[2];
  param_1[0xf] = param_2[3];
  param_1[0x10] = param_2[4];
  param_1[0x11] = param_2[5];
  param_1[0x12] = param_2[6];
  param_1[0x13] = param_2[7];
  return param_1;
}
```

**Binary-corrected algorithm** (authoritative):

```c
// this = ECX = aerodynamics component (size 0x50)
// desc = stack arg = 8 dwords from Vehicle_BuildAerodynamicsDescriptor
undefined4 * __thiscall hkDefaultAerodynamics_ctor(undefined4 *this, undefined4 *desc)
{
  // Base vehicle-component init ON this (ECX). Stack still carries `desc`
  // because FUN_0065d880 is thiscall with one unused/cleaned stack arg (ret 4).
  FUN_0065d880(this /*, desc unused in body */);

  *this = &PTR_FUN_009e4b20;   // hkDefaultAerodynamics vtable

  // Verbatim 8× dword copy: desc[0..7] → this+0x30..+0x4c
  this[0xc] = desc[0];   // +0x30 airDensity
  this[0xd] = desc[1];   // +0x34 frontalArea
  this[0xe] = desc[2];   // +0x38 dragCoefficient
  this[0xf] = desc[3];   // +0x3c liftCoefficient
  this[0x10] = desc[4];  // +0x40 extraGravity.x
  this[0x11] = desc[5];  // +0x44 extraGravity.y
  this[0x12] = desc[6];  // +0x48 extraGravity.z
  this[0x13] = desc[7];  // +0x4c pad / extraGravity.w (often uninit from builder)

  return this;
}
```

### Why Ghidra shows `FUN_0065d880(param_2)`

Raw body of the ctor:

| Bytes | Effect |
|-------|--------|
| `mov edi, [esp+0xC]` | `EDI = desc` |
| `push edi` | stack arg for base ctor |
| `mov esi, ecx` | `ESI = this` (**ECX unchanged**) |
| `call FUN_0065d880` | callee uses **ECX = this** |

`FUN_0065d880` body: `mov eax, ecx` then writes vtable/`+6`/zeros via EAX; epilogue **`ret 4`**.
It never loads the pushed stack dword. So the call is **base-init of the component**, not of the descriptor.
Treating `FUN_0065d880(desc)` as truth would imply the descriptor’s first dword becomes a vtable pointer
before the copy — that does not happen.

## Base init: `FUN_0065d880` @ `0x0065d880`

| Item | Value |
|------|--------|
| Body | `0x0065d880` – `0x0065d8bb` |
| Convention | `__thiscall`-like: object in ECX; `ret 4` cleans one stack arg (unused in body) |
| Base vtable | `PTR_FUN_009e7088` (`0x009e7088`) — overwritten immediately by aero ctor |

Effects on `this` (before aero vtable overwrite):

| Offset | Write |
|--------|--------|
| `+0x00` | base vtable `0x009e7088` |
| `+0x06` | `uint16 = 1` |
| `+0x10` … `+0x2c` | eight `float` zeros (`movss` of 0) — covers the **output force** slots `+0x10..+0x1c` used later by update |

Aero ctor then replaces `+0x00` with `PTR_FUN_009e4b20` and fills `+0x30..+0x4c` only. It does **not** re-zero `+0x10..+0x2c` (base already did).

## Component layout after ctor

| Offset | dword index | Source | Semantic |
|--------|-------------|--------|----------|
| `+0x00` | `[0]` | ctor | vtable `PTR_FUN_009e4b20` |
| `+0x06` | (half) | base | `1` |
| `+0x08` | `[2]` | *(framework post-link, not this ctor)* | parent framework / context (used by update) |
| `+0x10..+0x1c` | `[4..7]` | base zero | **output force** xyzw (written by `update`) |
| `+0x20..+0x2c` | `[8..0xb]` | base zero | residual base slots (not aero params) |
| `+0x30` | `[0xc]` | `desc[0]` | **airDensity** ρ |
| `+0x34` | `[0xd]` | `desc[1]` | **frontalArea** A |
| `+0x38` | `[0xe]` | `desc[2]` | **dragCoefficient** Cd |
| `+0x3c` | `[0xf]` | `desc[3]` | **liftCoefficient** Cl |
| `+0x40` | `[0x10]` | `desc[4]` | **extraGravity.x** |
| `+0x44` | `[0x11]` | `desc[5]` | **extraGravity.y** |
| `+0x48` | `[0x12]` | `desc[6]` | **extraGravity.z** |
| `+0x4c` | `[0x13]` | `desc[7]` | pad / **extraGravity.w** (builder leaves uninit) |

Object spans **`0x50`** bytes (`+0x00` … `+0x4f`); last used field ends at `+0x4c`.

## Descriptor → component map (identity)

Pure dword copy; **no scale, clamp, or reorder**.

| `desc[i]` | builder field (VehSpec) | → component |
|-----------|-------------------------|-------------|
| 0 | ρ `+0x5a8` | `+0x30` |
| 1 | A `+0x59c` | `+0x34` |
| 2 | Cd `+0x5a0` | `+0x38` |
| 3 | Cl `+0x5a4` | `+0x3c` |
| 4 | extraG.x `+0x5ac` | `+0x40` |
| 5 | extraG.y `+0x5b0` | `+0x44` |
| 6 | extraG.z `+0x5b4` | `+0x48` |
| 7 | uninit pad | `+0x4c` |

Builder details: `verified/fn_005fc4f0_aeroBuilder.md`. Runtime force math: `verified/fn_0064dae0_aero.md`.

## Call graph / plumbing

```
Vehicle_BuildAerodynamicsDescriptor  0x5fc4f0
        │  fills float desc[8]
        ▼
Vehicle_buildHavokVehicleFramework   0x5fd390
        │  allocates 0x50, call site ~0x5fd710
        ▼
hkDefaultAerodynamics_ctor           0x64da90
        │  then framework hook FUN_0064d930 (registration / bookkeeping)
        ▼
hkDefaultAerodynamics_update         0x64dae0  (vtbl slot +0x14)
```

**Xrefs to ctor** (`get_xrefs_to`):

| From | Role |
|------|------|
| `0x005fd710` in `Vehicle_buildHavokVehicleFramework` | Primary construction path |
| `0x0064ddfc` | Small allocate-and-construct thunk (push size `0x50`, set word at `+4`, call this ctor) — secondary / helper path |

Vtable string anchor: slot related to `"airDensity"` @ `0x009e4bdc` (see `0.6-aerodynamics.md`).

## Constants

**None** in the ctor body beyond:

- Immediate vtable pointer store: `0x009e4b20`
- Structural offsets `0x30` … `0x4c` (8 sequential dwords)
- Call to `FUN_0065d880`

No air-density / 0.5 / −0.5 formula constants here (those live in `update`).

## Reconciliation vs map docs

| Claim | Source | This pass | Result |
|-------|--------|-----------|--------|
| Ctor copies 8 words → `+0x30..+0x4c` | `0.6-aerodynamics.md` | Decompile + bytes | **Match** |
| `param_1[0xc..0x13]` = desc dwords | `fn_005fc4f0_aeroBuilder.md` | Same | **Match** |
| Component size `0x50` | `fn_005fd390_buildFramework.md` | Framework table + thunk `push 0x50` | **Match** |
| Vtable `0x9e4b20` | framework note | `mov [esi], 0x009e4b20` | **Match** |
| Verbatim copy (no transform) | builder / map | 8 plain loads/stores | **Match** |
| Base init target | Ghidra only | Ghidra said `param_2`; **binary = `this`** | **Binary wins** (doc correction) |

**No algorithm conflict** with Phase 0 field semantics. Only correction: decompiler argument to `FUN_0065d880`.

## Port notes (ctor only)

When constructing the server-side aerodynamics component from the 8-word descriptor:

1. Zero / clear output force at `+0x10..+0x1c` (base does this; ports should start force at 0).
2. Copy descriptor floats **by index** into ρ, A, Cd, Cl, extraG.xyz (and zero pad w).
3. Do **not** apply drag/lift math here — that is `update` only.
4. Framework linkage (`this+0x08` → vehicle framework) is **not** performed inside this function; set by the framework assembler after construction.

## Emulation

Not useful: pure pointer stores + base zeroing. No golden float vectors for the ctor itself
(identity map). Goldens belong to `update` given known ρ/A/Cd/Cl/extraG and chassis state.

## Related addresses

| Addr | Name | Role |
|------|------|------|
| `0x0065d880` | `FUN_0065d880` | Shared base component init (vtable + zero force region) |
| `0x005fc4f0` | `Vehicle_BuildAerodynamicsDescriptor` | VehSpec → desc[8] |
| `0x005fd390` | `Vehicle_buildHavokVehicleFramework` | Alloc `0x50` + call this ctor |
| `0x0064d930` | `FUN_0064d930` | Post-ctor framework bookkeeping hook |
| `0x0064dae0` | `hkDefaultAerodynamics_update` | Per-step drag + lift + extraG·mass |
| `0x009e4b20` | `PTR_FUN_009e4b20` | Aerodynamics vtable |
| `0x009e4bdc` | string `"airDensity"` | Vtable-adjacent name anchor |

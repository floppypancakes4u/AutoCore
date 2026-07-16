# Verified: `hkDefaultSuspension_update` @ `0x0064de50`

Program: **`autoassault.exe`** (image base `0x400000`). Havok 2.3 vehicle SDK.
RE gate per `docs/reconstruction/physics/PORTING_RULES.md`.
Reconciled against map evidence `docs/reconstruction/physics/0.4-suspension.md`.

| Item | Value |
|------|--------|
| Address | `0x0064de50` |
| Body | `0x0064de50` – `0x0064df0c` |
| Ghidra name | `hkDefaultSuspension_update` |
| Calling convention | `__thiscall` / `__fastcall` (`this` = ECX = `param_1`); `RET 4` (one unused stack arg, typically dt from component vtbl) |
| Role | Per-wheel spring + damper force → writes scalar `suspForce[i]` |
| Vtable | `PTR_FUN_009e4c00` slot **+0x14** → this function (xref from `0x009e4c14`) |
| Downstream | `hkVehicleFramework_postTickApplyForces` @ `0x64bc70` reads `susp+0x34[i]` and applies as contact-normal impulse |

## Tools used (this verification)

1. **`decompile_function`** `0x64de50` program=`autoassault.exe`
2. **HTTP `disassemble_function`** full body (sign branch / gScale zero-check confirmation)
3. **`read_memory`** `0x00a0f2a0` length=4 (`g_flOne`)
4. **`list_globals`** name_substring=`g_flOne` → label @ `00a0f2a0`
5. **`get_xrefs_to`** `0x64de50` → vtbl DATA at `0x009e4c14`

Did **not** use `disassemble_bytes`. Emulation skipped (pointer-heavy wheel/comp arrays).

## Constants (raw memory)

| Symbol / address | LE bytes | u32 | float32 | Role in formula |
|------------------|----------|-----|---------|-----------------|
| `g_flOne` @ `0x00a0f2a0` | `00 00 80 3f` | `0x3f800000` | **`+1.0`** | Numerator of `gScale = 1 / chassisRB[+0x2c]` |

No other plate floats in this function. Zero is the XMM zero register (`XORPS XMM4,XMM4`), not a DAT_ load.

---

## Object graph (`param_1` = suspension component)

```
susp          = param_1                         // this
fw            = *(susp + 0x08)                  // hkVehicleFramework*
wheels        = *(fw + 0x0c)                    // hkDefaultWheels*
chassisShell  = *(fw + 0x30)
chassisRB     = *(chassisShell + 0x3c)          // rigid body*
wheelBase     = *(wheels + 0x80)                // first wheel
wheel_i       = wheelBase + i * 0xC0            // stride 0xC0
```

### Suspension component fields used here

| Offset | Type | Meaning |
|--------|------|---------|
| +0x08 | ptr | Framework back-pointer |
| +0x28 | `float*` | **restLength[i]** |
| +0x34 | `float*` | **output force[i]** (written) |
| +0x40 | int | **wheelCount** (loop bound) |
| +0x44 | `float*` | **spring strength[i]** |
| +0x50 | `float*` | **compression damping[i]** |
| +0x5c | `float*` | **extension (rebound) damping[i]** |

### Wheel fields used here (stride `0xC0`)

| Offset | Type | Meaning |
|--------|------|---------|
| +0x80 | byte | **in-contact** (`0` → force 0; nonzero → compute) |
| +0xac | float | **suspension scaling factor** (spring multiplier; set in preUpdate) |
| +0xb0 | float | **current suspension length** |
| +0xb4 | float | **closing speed** (damper velocity input) |

**Pointer rule:** `susp+0x28 / +0x34 / +0x44 / +0x50 / +0x5c` are **heap pointers to float tables**, not inline floats. Indexing is `*(*(susp+OFF) + i*4)`.

---

## gScale — `1 / chassisRB[+0x2c]`

Decompile + disassembly:

```
chassisS = *(float *)(chassisRB + 0x2c)
if chassisS == 0.0:          // UCOMISS + LAHF/TEST AH,0x44 equal path
    gScale = 0.0
else:
    gScale = g_flOne / chassisS   // MOVSS g_flOne; DIVSS chassisS
```

| Fact | Value |
|------|-------|
| Source | `*(*( *(susp+8) + 0x30 ) + 0x3c ) + 0x2c` |
| Zero guard | `chassisS == 0` → `gScale = 0` (no Inf/NaN from divide) |
| Non-zero | `gScale = 1.0f / chassisS` |
| Semantic | Same chassis scalar used as inverse-mass style normalizer elsewhere (`postTick`, aero mass = `1/RB+0x2c`). Treat as **`invMass` at `RB+0x2c`** → `gScale = mass` when invMass≠0, else 0. |

---

## closingSpeed sign → compression vs extension damping

Damper coefficient is selected **per wheel** from the sign of `wheel+0xb4` (closing speed).

### Decompile

```c
if (0.0 <= *(float *)(wheel + 0xb4)) {
    dampArr = *(int *)(susp + 0x5c);   // EXTENSION / rebound
} else {
    dampArr = *(int *)(susp + 0x50);   // COMPRESSION
}
```

### Assembly (authoritative branch)

```text
MOVSS  XMM2, [wheel+0xb4]     ; closingSpeed
COMISS XMM4, XMM2             ; compare 0.0 ? closingSpeed
...
JBE    use_extension          ; if 0.0 <= closingSpeed
MOV    EAX, [susp+0x50]       ; compression array
JMP    apply
use_extension:
MOV    EAX, [susp+0x5c]       ; extension array
```

| Condition | Physics meaning | Damping table |
|-----------|-----------------|---------------|
| `closingSpeed < 0` | **Compressing** (length decreasing) | `susp+0x50` **Compression** |
| `closingSpeed >= 0` | **Extending / rebounding** (or stationary) | `susp+0x5c` **Extension** |

**Equality at zero:** uses **extension** (`>= 0`). Matches Phase 0 map; re-confirmed.

---

## Force formula (bit-exact)

Per wheel `i` where `wheel+0x80 != 0`:

```
restLen       = restLength[i]                 // *(susp+0x28)[i]
currLen       = wheel+0xb0
strength      = strength[i]                   // *(susp+0x44)[i]
scale         = wheel+0xac                    // preUpdate length scaling
closingSpeed  = wheel+0xb4
dampCoef      = (closingSpeed >= 0)
                  ? extensionDamp[i]          // *(susp+0x5c)[i]
                  : compressionDamp[i]        // *(susp+0x50)[i]

compression   = restLen - currLen
springTerm    = compression * strength * scale
damperTerm    = dampCoef * closingSpeed
suspForce[i]  = (springTerm - damperTerm) * gScale
```

Single expression as emitted by decompiler:

```
*(float *)(*(susp + 0x34) + i*4) =
    ( ( *(float *)(*(susp + 0x28) + i*4) - *(float *)(wheel + 0xb0) )
      * *(float *)(*(susp + 0x44) + i*4)
      * *(float *)(wheel + 0xac)
      - *(float *)(dampArr + i*4) * *(float *)(wheel + 0xb4)
    ) * gScale;
```

### Airborne / no contact

```
if (*(char *)(wheel + 0x80) == 0)
    suspForce[i] = 0.0;
```

### Port formula (compact)

```
F[i] = 0                                    if !inContact
F[i] = ( (restLen[i] − len[i]) · k[i] · s[i]
         − c_sel[i] · v[i] ) · (1 / RB[+0x2c] or 0)
  where c_sel = (v >= 0) ? c_ext : c_comp
        s[i]  = wheel+0xac,  v[i] = wheel+0xb4,  len[i] = wheel+0xb0
```

---

## Full decompile (Ghidra, this pass)

```c
void __fastcall hkDefaultSuspension_update(int param_1)
{
  int iVar1;
  int iVar2;
  int iVar3;
  int iVar4;
  int iVar5;
  float fVar6;

  iVar1 = *(int *)(*(int *)(param_1 + 8) + 0xc);
  fVar6 = *(float *)(*(int *)(*(int *)(*(int *)(param_1 + 8) + 0x30) + 0x3c) + 0x2c);
  if (fVar6 == 0.0) {
    fVar6 = 0.0;
  }
  else {
    fVar6 = g_flOne / fVar6;
  }
  iVar4 = 0;
  if (0 < *(int *)(param_1 + 0x40)) {
    iVar5 = 0;
    do {
      iVar2 = *(int *)(iVar1 + 0x80) + iVar5;
      if (*(char *)(iVar2 + 0x80) == '\0') {
        *(undefined4 *)(*(int *)(param_1 + 0x34) + iVar4 * 4) = 0;
      }
      else {
        if (0.0 <= *(float *)(iVar2 + 0xb4)) {
          iVar3 = *(int *)(param_1 + 0x5c);
        }
        else {
          iVar3 = *(int *)(param_1 + 0x50);
        }
        *(float *)(*(int *)(param_1 + 0x34) + iVar4 * 4) =
             ((*(float *)(*(int *)(param_1 + 0x28) + iVar4 * 4) - *(float *)(iVar2 + 0xb0)) *
              *(float *)(*(int *)(param_1 + 0x44) + iVar4 * 4) * *(float *)(iVar2 + 0xac) -
             *(float *)(iVar3 + iVar4 * 4) * *(float *)(iVar2 + 0xb4)) * fVar6;
      }
      iVar4 = iVar4 + 1;
      iVar5 = iVar5 + 0xc0;
    } while (iVar4 < *(int *)(param_1 + 0x40));
  }
  return;
}
```

---

## Reconciliation with Phase 0 map (`0.4-suspension.md`)

| Claim in map | Verified? | Notes |
|--------------|-----------|-------|
| `F = ((rest−len)·strength·scale − damp·closing)·gScale` | **Match** | Exact decompile order |
| `closingSpeed < 0` → compression damp (`+0x50`) | **Match** | Asm `JBE` path |
| `closingSpeed >= 0` → extension damp (`+0x5c`) | **Match** | Including zero |
| `gScale = 1/RB[+0x2c]`, zero → 0 | **Match** | `g_flOne` @ `0xa0f2a0` = 1.0 |
| Wheel stride `0xC0`; force out `susp+0x34` | **Match** | |
| In-contact gate `wheel+0x80` | **Match** | byte, zero clears force |

**Binary wins:** no conflicts. Map formula is correct; this file is the port gate for suspension force generation.

## Port notes (no C# here)

- Inputs `wheel+0xac / +0xb0 / +0xb4 / +0x80` are **owned by preUpdate / wheel-collide**, not this function.
- Output is a **scalar force**; direction and `dt` packaging happen in `postTick` (`F * dt * contactNormal`).
- `gScale` is chassis-global (one value for all wheels per tick).
- Damper arrays are **per-wheel**, selected by velocity sign, not by wheel axle/front-rear at runtime (front/rear baked into the tables at setup).

## Emulation

Skipped: requires live suspension component + wheel array pointers. Golden vectors for TDD should be hand-derived from the formula above with fixed `rest/strength/damp/scale/closing/gScale` tuples.

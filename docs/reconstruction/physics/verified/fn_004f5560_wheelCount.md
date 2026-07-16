# Verified: wheel-count helper `FUN_004f5560` @ `0x4f5560`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x004f5560` – `0x004f556c` |
| Size | **13 bytes** (3 instructions) |
| Symbol | `FUN_004f5560` (unnamed in Ghidra; docs call it wheel-count / `WheelsetFieldB0`) |
| Convention | MSVC **`__thiscall` / `__fastcall`** — `this` in **`ECX`** only; no stack args |
| Return | Low byte of `EAX` = wheel count (`uint8`); see §Return width |
| Verified | Ghidra `decompile_function` + `read_memory` (function bytes) + call-site asm context |
| Constants | **none** — pure pointer chase; no `DAT_*` |

---

## Role

Returns the **wheel count** stored on the vehicle’s **wheelset object**.

Used as the loop bound / capacity seed by Havok vehicle builders, friction-table setup,
`VehicleAction_calcWheelTorque`, and a few non-physics callers (render / UI paths).

Sibling friction accessor `FUN_004f5550` @ `0x4f5550` uses the **same** `this+0x258`
wheelset pointer, then jumps to `FUN_005a6f20` for `wheelset+0xb4[idx]`.

---

## Raw bytes (`read_memory` @ `0x004f5560`, length 16)

| Offset | Hex | Instruction |
|---:|---|---|
| `+0` | `8B 81 58 02 00 00` | `mov eax, dword ptr [ecx+0x258]` |
| `+6` | `8A 80 B0 00 00 00` | `mov al, byte ptr [eax+0xb0]` |
| `+C` | `C3` | `ret` |
| `+D` | `CC CC CC` | padding (int3) before next function |

Full LE hex: `8b81580200008a80b0000000c3`

PathAHook prologue check matches the first 8 bytes:
`{ 0x8b, 0x81, 0x58, 0x02, 0x00, 0x00, 0x8a, 0x80 }` (`tools/PathAHook/PathAHook.cpp`).

---

## Decompile (Ghidra pseudocode)

```
undefined4 __fastcall FUN_004f5560(int param_1 /* ECX = vehicle / entity */)
{
  return CONCAT31((int3)((uint)*(int *)(param_1 + 600) >> 8),
                  *(undefined1 *)(*(int *)(param_1 + 600) + 0xb0));
}
```

`600` decimal = **`0x258`**. Equivalent clean form:

```
// this in ECX
wheelset = *(void**)(this + 0x258);
return *(uint8*)(wheelset + 0xb0);   // semantic; see return-width note
```

**No null check.** If `*(this+0x258) == 0` (or invalid), the second load AVs at
**`0x004F5566`** — the known null-wheelset crash site (`docs/nullWheels.md`, Path A
`WheelsetDeref_CRASH_IMMINENT` hook).

---

## Exact algorithm

```
// __thiscall / __fastcall; ECX = CVOGVehicle* (or any object with wheelset @ +0x258)
// No stack args. No side effects. No constants.

uint8 FUN_004f5560(entity):
    wheelset = *(ptr*)(entity + 0x258)     // offset 600 decimal
    return *(uint8*)(wheelset + 0xb0)      // wheel count byte
```

### Return width (important)

Machine code:

1. `mov eax, [ecx+0x258]` — full 32-bit wheelset pointer into `EAX`
2. `mov al, [eax+0xb0]` — **only** overwrites `AL`; bits 8..31 of the pointer remain
3. `ret`

So raw `EAX` is **not** a clean zero-extended byte. Ghidra’s `CONCAT31(ptr>>8, count)`
is accurate for the full register.

**All observed callers only use `AL`**, via:

| Pattern | Sites | Effect |
|---|---|---|
| `MOVSX reg, AL` | e.g. `calcWheelTorque` @ `0x598056`, steering builder @ `0x5fc723` | **signed** `int` from low byte |
| `TEST AL, AL` / `JLE` | e.g. framework builder @ `0x5fd8c8`, `FUN_00834120` @ `0x834209` | compare / branch on low byte only |

Port semantics: treat return as **`uint8` wheel count**. When matching client loops that
`MOVSX`, the bound is `(int)(signed char)count` — for normal counts `1..6` this equals
the unsigned value. Counts ≥ `0x80` would sign-extend negative (not expected for AA
vehicles).

---

## Struct offsets

| Object | Off | Type | Meaning |
|---|---|---|---|
| Vehicle / entity (`this`) | **`+0x258`** (600) | `ptr` | **Wheelset object** (set by `SetWheelset` @ `0x4fea90`; left null if equip fails) |
| Wheelset | **`+0xb0`** | `uint8` | **Wheel count** (this helper) |
| Wheelset | **`+0xb4`** | `float32[≤6]` | Per-wheel base friction (via `FUN_004f5550` → `FUN_005a6f20`) |

Related (not read by this helper):

| Object | Off | Note |
|---|---|---|
| VehicleAction | `+0x44` | Entity back-ref — `calcWheelTorque` loads `ECX` from here **before** the call |
| VehSpec | `+0x4cc` | Axle-0 / front wheel count — **different** field; not this helper |
| wheelsDesc | `+0x0c` | Havok-side wheel count copy after builders run |

---

## Sibling: `FUN_004f5550` @ `0x4f5550` (friction)

Bytes: `8B 89 58 02 00 00 E9 C5 19 0B 00`

```
mov ecx, [ecx+0x258]     ; same wheelset pointer
jmp FUN_005a6f20         ; return float at wheelset+0xb4 + idx*4 (idx on stack)
```

Confirms `+0x258` is the wheelset container and `+0xb0` / `+0xb4` are adjacent layout
fields on that object (`0.5-wheel-collide.md` §4).

---

## Callers (Ghidra `get_function_callers` / xrefs — 43 call sites)

| Caller | Address | Use |
|---|---|---|
| `VehicleAction_calcWheelTorque` | `0x598040` | Loop bound; `ECX = *(VehicleAction+0x44)` then `MOVSX` |
| `Vehicle_BuildSteeringDescriptor` | `0x5fc710` | Capacity / store / loop (×4 sites) |
| `Vehicle_BuildTransmissionDescriptor` | `0x5fc840` | Capacity / loop (×7 sites) |
| `FUN_005fcb00` | `0x5fcb00` | Builder helper (×6 sites) |
| `FUN_005fcce0` | `0x5fcce0` | Friction-table setup (×10 sites) |
| `Vehicle_BuildSuspensionDescriptor` | `0x5fcff0` | Capacity / loop (×8 sites) |
| `Vehicle_buildHavokVehicleFramework` | `0x5fd390` | Torque-share loop bound (×3 sites) |
| `FUN_00834120` | `0x834120` | Non-builder (×2 sites) |
| `FUN_00938380` | `0x938380` | Non-builder (×2 sites) |

Call-site `this` setup examples (asm context):

```
; calcWheelTorque @ 0x59804d
mov  ecx, [esi+0x44]     ; entity from VehicleAction
call FUN_004f5560
movsx eax, al            ; signed bound

; BuildSteeringDescriptor @ 0x5fc718
mov  ecx, esi            ; entity = builder param_1
call FUN_004f5560
movsx ebp, al

; buildHavokVehicleFramework @ 0x5fd8af
mov  ecx, esi            ; entity
call FUN_004f5560
test al, al
jle  ...                 ; empty → skip loop
```

---

## Conflicts vs prior docs

| Source | Claim | This re-verify | Verdict |
|---|---|---|---|
| `0.5-wheel-collide.md` | `wheelCount = byte at (*(comp+0x258))+0xb0` | yes | **match** |
| `fn_005fc710_steeringBuilder.md` | `return *(byte*)(*(int*)(entity+600)+0xb0)` | yes | **match** |
| `fn_00598040_calcWheelTorque.md` | `(int)(signed char)FUN_004f5560()` | yes (`MOVSX AL`) | **match** |
| same doc note “ECX = this+0x44 chain **inside** helper” | setup is **caller-side**; helper only does `+0x258` | **correct caller, wrong “inside helper” wording** |
| PathA / nullWheels | AV at `0x004F5566` when `+0x258` null | second insn is the AV load | **match** |
| Ghidra plate / signature `undefined FUN_004f5560(void)` | misleading void | body returns `undefined4` / low-byte count | trust body + bytes |

**No algorithm conflict** with evidence docs. Port as a one-line wheelset field read.

---

## Port notes (no C# in this gate)

1. Input is the **vehicle entity**, not VehicleAction / not VehSpec.
2. Null wheelset is a **hard fault** in retail; server must ensure equip/`+0x258` before any
   client path that calls this (CreateVehicle nested wheelset, SetWheelset).
3. Do not confuse with `VehSpec+0x4cc` (front/axle-0 count) or `wheelsDesc+0x0c` (post-build
   Havok copy). This helper is the **authoritative source** builders consult first.
4. Expected range for AA cars is small (`2`/`4`/`6`); still store/compare as the raw byte
   and use signed `MOVSX` only when matching a specific client loop.

---

## Emulation

Skipped: function is a 2-load pointer chase; no constants or pure math. Correctness is
fully determined by bytes + call-site `AL` consumption. Golden: for any entity with
`wheelset = entity+0x258` non-null and `*(u8*)(wheelset+0xb0) = N`, helper returns `N` in `AL`.

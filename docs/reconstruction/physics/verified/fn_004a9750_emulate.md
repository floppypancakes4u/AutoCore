# Emulation: `VehicleEngine_torqueCurve2D` @ `0x4a9750`

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| Address | `0x004a9750` |
| Tool | Ghidra MCP `emulate_function` |
| Case | **engine disabled** (`enabled == 0` at `this+0x0c`) |
| Expected | x87 `ST0` = **1.0** (`g_flOne` / `DAT_00a0f2a0`) |
| Result | **SUCCESS** |

---

## Goal

Confirm via P-code emulation (not hand-derived math alone) that the early-out path:

```text
if (*(char*)(engine + 0x0c) == 0)
    return g_flOne;   // 1.0
```

returns exactly **1.0** in `ST0` with a **minimal** memory image.

---

## Setup (minimal memory image)

Convention is MSVC `__thiscall`: `this` in `ECX`. Disabled path only reads `this+0x0c` and the program constant `g_flOne`; no LUT pointer, factors table, or stack float args are required.

| Input | Value |
|---|---|
| `ECX` | `0x7FFE0000` (scratch engine object) |
| Memory `@0x7FFE0000` | 16 zero bytes → `*(char*)(this+0x0c) == 0` |
| Stack | auto (`ESP=0x7FFF0000`, return sentinel `0xDEADBEEF`) |
| `max_steps` | **500** (disabled path is a handful of instructions; avoid unbounded run) |
| `return_registers` | `EAX,ECX,EDX,ESP,EIP,ST0` |

### Call payload (conceptual)

```json
{
  "address": "0x4a9750",
  "registers": {"ECX": "0x7FFE0000"},
  "memory": {
    "regions": [
      {"address": "0x7FFE0000", "hex": "00000000000000000000000000000000"}
    ]
  },
  "max_steps": 500,
  "return_registers": "EAX,ECX,EDX,ESP,EIP,ST0"
}
```

---

## Result

| Field | Value | Notes |
|---|---|---|
| `success` | `true` | Emulator completed without error |
| `function` | `VehicleEngine_torqueCurve2D` | |
| `hit_return` | **`true`** | PC landed on return sentinel |
| `final_pc` | `deadbeef` | Matches stack return sentinel |
| `ST0` | **`0x3fff8000000000000000`** | x87 80-bit encoding of **1.0** |
| `ECX` | `0x7ffe0000` | Unchanged `this` |
| `ESP` | `0x7fff000c` | Consistent with callee stack cleanup past sentinel |

### Decode of `ST0`

Intel 80-bit extended real `0x3FFF_8000000000000000`:

- sign = 0  
- biased exponent = `0x3FFF` → true exponent 0  
- significand = `0x8000…` → integer bit set  

→ **exactly 1.0**. Matches `g_flOne` (`DAT_00a0f2a0` = float32 `0x3f800000`).

---

## Verdict

| Claim | Status |
|---|---|
| Disabled engine (`+0x0c == 0`) returns **1.0** | **Confirmed by emulation** |
| Emulation usable for this early-out alone | **Yes** — minimal image, low step count |
| Emulation practical for in-range LUT / golden vectors | **Still no** — needs full struct (`+0x10/+0x14/+0x18`), factors at `+0x344`, and indirect byte LUT at `+0x3dc`; prior work remains hand-derived (see `fn_004a9750_torqueCurve2D.md`, `engine-torque-spec.md` §4) |

**Success:** disabled-path return is bit-exact **1.0** under Ghidra `emulate_function` with the minimal zeroed engine blob above.

---

## Related

- Algorithm / goldens: [`fn_004a9750_torqueCurve2D.md`](fn_004a9750_torqueCurve2D.md)
- Port spec: [`../engine-torque-spec.md`](../engine-torque-spec.md)
- Caller assembly: [`fn_00598040_calcWheelTorque.md`](fn_00598040_calcWheelTorque.md)

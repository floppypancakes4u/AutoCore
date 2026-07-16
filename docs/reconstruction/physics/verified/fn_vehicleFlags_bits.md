# Verified: `sinVehicleFlags` / `VehSpec+0x5f0` bit map

| Field | Value |
|---|---|
| Program | `autoassault.exe` (image base `0x400000`) |
| DB column | **`sinVehicleFlags`** (UTF-16 string @ `0x00a92f24`) |
| Load site | `VehicleDb_LoadCloneBase` @ `0x7efb40` (bind xref `0x7f32d1` → push name; store via `CALL 0x7b8a60` → `MOV word ptr [EBP+0x130], AX`) |
| Final offset | **`VehicleSpecific + 0x5f0`** (read as **byte** by setup builders) |
| Type in DB | `sin*` → signed **int16** (low byte is the flag nibble used at runtime setup) |
| Verified | Ghidra `decompile_function` of steering + brake builders; `search_strings` / `get_xrefs_to` for DB name |
| Status | **Verified** (bits 0–3; no other consumer of this byte in the framework builders) |

Cross-refs:

- `fn_005fc710_steeringBuilder.md` — bits 2/3 → `doesWheelSteer[]`
- `fn_005fcb00_brakeBuilder.md` — bits 0/1 → `wheelsIsConnectedToHandbrake[]`
- `setup-field-mapping.md` — master VehSpec → Havok map
- `server_handbrake_wire.md` — **different** `VehicleFlags` (ghost wire), do not conflate

---

## 1. Naming — do not conflate two “VehicleFlags”

| Name | Where | What it is |
|---|---|---|
| **`sinVehicleFlags`** | Clonebase / `VehicleSpecific+0x5f0` | **Setup** bitfield: which axles steer + which axles are handbrake-connected |
| **`Vehicle.VehicleFlags` / `VehicleMovedFlags`** | Ghost pose wire byte #2 | **Runtime** drive flags; **bit0 = Handbreak** → entity `+0x61c` |

This note is about **`sinVehicleFlags` / VehSpec+0x5f0` only**. Wire handbrake packing is documented in `server_handbrake_wire.md`.

---

## 2. Authoritative bit map (`VehSpec+0x5f0` low byte)

| Bit | Mask | Shift form | Axle | Role | Consumer |
|---:|---|---|---|---|---|
| **0** | `0x01` | `flags & 1` | **Front** (`i < frontN`) | `wheelsIsConnectedToHandbrake[i]` | Brake builder `FUN_005fcb00` @ `0x5fcb00` |
| **1** | `0x02` | `(flags >> 1) & 1` | **Rear** (`i ≥ frontN`) | `wheelsIsConnectedToHandbrake[i]` | Brake builder `FUN_005fcb00` |
| **2** | `0x04` | `(flags >> 2) & 1` | **Front** | `doesWheelSteer[i]` | Steering builder `Vehicle_BuildSteeringDescriptor` @ `0x5fc710` |
| **3** | `0x08` | `(flags >> 3) & 1` | **Rear** | `doesWheelSteer[i]` | Steering builder `0x5fc710` |
| 4–7 | `0xF0` | — | — | **Unused by these builders** | Not read in `0x5fc710` / `0x5fcb00` |
| 8–15 | high byte of `sin` | — | — | Loaded as int16 in DB path | **Not** read by setup builders (they touch one byte at `+0x5f0`) |

**Axle split** for both consumers:

```
frontN = *(u8*)(VehSpec + 0x4cc)   // axle-0 wheel count
// wheels [0 .. frontN)  = front axle  → bits 0 / 2
// wheels [frontN .. n)  = rear axle   → bits 1 / 3
// n = FUN_004f5560()  // wheel count byte at *(entity+0x258)+0xb0
```

There is **no per-wheel bit in the DB** — only front/rear axle flags, fanned out to every wheel on that axle.

---

## 3. Steering builder — bits 2 / 3

**Function:** `Vehicle_BuildSteeringDescriptor` @ `0x005fc710`  
**Downstream:** `hkDefaultSteering_ctor` @ `0x64fac0` (or `TankSteering_ctor` @ `0x64fc80` if `VehSpec+0x4c0 == 4`)  
Havok reflection string: `wheelsDoesSteer` @ `0x009e4f7c`.

### Decompile (flag loop only)

```c
// VehSpec = *(*(entity + *(entity[1]+4) + 0xac) + 0x3c)
// frontN  = *(char*)(VehSpec_via_entity_plus_600 + 0x4cc)
// flags   = *(byte*)(VehSpec + 0x5f0)

for (i = 0; i < wheelCount; i++) {
    if (i < frontN)
        doesWheelSteer[i] = (flags >> 2) & 1;   // bit 2
    else
        doesWheelSteer[i] = (flags >> 3) & 1;   // bit 3
}
```

### Configurations

| Config | bit2 | bit3 | Value nibble (steer only) |
|---|---:|---:|---|
| Front-steer only (standard cars) | 1 | 0 | `0x04` |
| Rear-steer only | 0 | 1 | `0x08` |
| 4WS (both) | 1 | 1 | `0x0C` |
| No steer | 0 | 0 | `0x00` |

Runtime: `hkDefaultSteering_update` @ `0x64f840` zeros angle for wheels with `doesWheelSteer[i] == 0` (see `fn_0064f840_steering.md`).

---

## 4. Brake builder — bits 0 / 1

**Function:** `FUN_005fcb00` @ `0x005fcb00`  
**Downstream:** `hkDefaultBrake_ctor` @ `0x64ed40` → copy helper `FUN_0064e840`  
Havok reflection string: `wheelsIsConnectedToHandbrake` @ `0x009e4d28`.

### Decompile (flag sites only)

```c
// front axle loop i ∈ [0, frontN):
handbrakeConnected[i] = flags & 1;              // bit 0

// rear axle loop i ∈ [frontN, nWheels):
handbrakeConnected[i] = (flags >> 1) & 1;       // bit 1
```

### Configurations

| Config | bit0 | bit1 | Value nibble (HB only) |
|---|---:|---:|---|
| Rear handbrake only (typical) | 0 | 1 | `0x02` |
| Front only | 1 | 0 | `0x01` |
| All wheels (4-wheel HB) | 1 | 1 | `0x03` |
| None | 0 | 0 | `0x00` |

### Runtime meaning (context)

- **Setup:** flags only decide which wheels receive handbrake *connection* on `hkDefaultBrake`.
- **Runtime input:** entity handbrake byte `+0x61c` (from ghost `VehicleFlags` bit0 / AI sharp) is **not** this bitfield.
- **AA custom path:** primary handbrake *effect* in `calcWheelTorque` (`0x598040`) is rear drive-torque ×0.5 when `entity+0x61c ≠ 0` — independent of `+0x5f0` bits. The connect flags matter if/when `hkDefaultBrake_update` runs with handbrake asserted (see `fn_005fcb00_brakeBuilder.md` §6 / `brake-spec.md`).

---

## 5. Combined typical values

| Pattern | Bits set | Byte (`+0x5f0`) | Meaning |
|---|---|---|---|
| Standard FWD/RWD car | 1 + 2 | **`0x06`** | rear HB-connected + front steers |
| 4WS car, rear HB | 1 + 2 + 3 | **`0x0E`** | rear HB + front+rear steer |
| Front-steer, no HB connect | 2 | **`0x04`** | steers only |
| Rear-steer, rear HB | 1 + 3 | **`0x0A`** | rear steer + rear HB |

*(Observed DB content not sampled in this pass; table is combinatorial from the verified bit map.)*

---

## 6. Port recipe (setup fan-out)

```
flags    = (byte) VehSpec[+0x5f0]          // sinVehicleFlags low byte
frontN   = (byte) VehSpec[+0x4cc]
nWheels  = wheelCount                      // FUN_004f5560

for i in 0 .. nWheels-1:
    if i < frontN:
        doesWheelSteer[i]              = (flags >> 2) & 1
        wheelsIsConnectedToHandbrake[i] =  flags       & 1
    else:
        doesWheelSteer[i]              = (flags >> 3) & 1
        wheelsIsConnectedToHandbrake[i] = (flags >> 1) & 1
```

No `DAT_*` float constants involved. Emulation skipped (pointer-heavy VehSpec / entity graph).

---

## 7. RE checklist

| Step | Result |
|---|---|
| `decompile_function` `0x5fc710` | OK — `(flags>>2)&1` front, `(flags>>3)&1` rear |
| `decompile_function` `0x5fcb00` | OK — `flags&1` front, `(flags>>1)&1` rear |
| Axle split | OK — both use `VehSpec+0x4cc` as front count |
| DB name | OK — `sinVehicleFlags` @ `0xa92f24`, xref from `VehicleDb_LoadCloneBase` `0x7f32d1` |
| Load type | OK — int16 store `[EBP+0x130]`; builders read **byte** at final `+0x5f0` |
| Bits 4+ | No use in these two builders; leave reserved |
| vs Phase-0 docs | **Match** `setup-field-mapping.md` / `steering-spec.md` / prior verified builders |
| vs ghost `VehicleFlags` | **Different object** — runtime handbrake wire bit, not `sinVehicleFlags` |

---

## 8. Confidence

| Claim | Confidence |
|---|---|
| Bits 0–3 meanings and axle assignment | **High** (direct decompile of both sole consumers) |
| Fan-out via `+0x4cc` | **High** |
| DB identity `sinVehicleFlags` → this field | **High** for name; **Medium-High** for absolute EBP→final base (builders prove final off is `+0x5f0`) |
| High byte of `sin` unused at setup | **High** for builders (byte read); **Open** whether any non-physics path reads the int16 |
| Ghost wire `VehicleFlags` unrelated | **High** |

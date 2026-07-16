# Constant block `0xaf3380` — steer / upright plate floats

| Field | Value |
|---|---|
| Program | `autoassault.exe` |
| Tool | Ghidra MCP `read_memory` |
| Address | `0x00af3380` |
| Length | **16** |
| Endian | little-endian |
| Scope | contiguous float32 plate used by mode-`0x02` steer speed-factor and upright-restore |

No C# in this file (RE evidence only).

---

## Raw dump

```
read_memory address=0xaf3380 length=16 program=autoassault.exe
hex: 33 33 33 3f  9a 99 19 3f  00 00 a0 41  00 00 00 00
```

| Offset | Address | LE bytes | Bit pattern | float32 | Practical |
|---:|---|---|---|---:|---|
| **+0** | `0x00af3380` | `33 33 33 3f` | `0x3F333333` | **0.699999988079071** | **0.7** |
| **+4** | `0x00af3384` | `9a 99 19 3f` | `0x3F19999A` | **0.6000000238418579** | **0.6** |
| **+8** | `0x00af3388` | `00 00 a0 41` | `0x41A00000` | **20.0** | **20.0** exactly |
| +12 | `0x00af338c` | `00 00 00 00` | `0x00000000` | **0.0** | zero / pad (not interpreted as steer param) |

---

## Interpretation (+0 / +4 / +8)

### `+0` — `DAT_00af3380` = **0.7** (upright-restore dot threshold)

- Symbol: `DAT_00af3380`
- Role: gate for the **upright-restore angular impulse** in `VehicleAction_applyAction` (`0x598650`), velocity-coupled (non-mode-`0x02`) branch.
- Condition (from RE): engage when `up_dot < 0.7` **and** lower guard `g_flMultiKillCountBlend < up_dot` (tilted, not fully inverted).
- Approx angle: `acos(0.7) ≈ 45.6°` from world-up — body tilt beyond ~45° starts righting.
- Cross-ref: `avd-airstab-spec.md`, `NPCDriving.md`.

### `+4` — dword at `0xaf3384` = **0.6** (neighbor only)

- **Not** the mode-`0x02` speed-factor divisor.
- Contiguous plate neighbor of `0xaf3380` / `0xaf3388`. Documented so ports do not off-by-4 and treat 0.6 as the speed scale.
- Older notes that claimed `DAT_00af3388 = 0.6` were reading **this** dword. Binary authority is the +8 word below.
- Cross-ref: `steering-spec.md` NOTE on `DAT_00af3388`, `verified/fn_0064f840_steering.md`, `README.md` correction.

### `+8` — `_DAT_00af3388` = **20.0** (mode-`0x02` speed-factor divisor)

- Symbol: `_DAT_00af3388` / `DAT_00af3388` (same address; underscore is Ghidra name noise).
- Role: in `applyAction` when movement-mode byte `entity+0x4ce == 0x02`:

  ```
  speedFactor = min(|chassisLinearVel| / 20.0, 1.0)
  targetSteer = desiredSteer * speedFactor
  ```

- Units: same as chassis linear speed (world units/s). Full steer authority only above **20** speed; ramps linearly from 0.
- **Must not** use 0.6 here — that would saturate steer by ~0.6 u/s.
- Cross-ref: `steering-spec.md` Stage 1, `verified/fn_0064f840_steering.md`, `verified/fn_entity_driveAxes_offsets.md`.

### `+12` (length-16 tail)

- Zero dword at `0xaf338c`. Included for full 16-byte plate; **not** one of the three interpreted steer/upright floats. Next meaningful world-axis material in this region is the Y-up triple at `0xaf3390` (`batch_B.md`).

---

## Layout sketch

```
0xaf3380  [ 0.7 f32 ]  upright-restore up_dot threshold
0xaf3384  [ 0.6 f32 ]  neighbor — do not use as speed divisor
0xaf3388  [20.0 f32 ]  mode-0x02 speedFactor = min(speed/20, 1)
0xaf338c  [ 0.0 f32 ]  zero / pad
0xaf3390  [ 0,1,0   ]  world-up (separate block; see batch_B)
```

---

## Gotchas

1. **Off-by-4**: `0.6` and `20.0` sit four bytes apart. Ports and Ghidra symbols have confused them; always re-read length ≥12 from `0xaf3380` if in doubt.
2. **0.7 at `0xaf3380` is not the sharp-turn lateral 0.7** at `0xa0f710` (`batch_A.md`) — same numeric value, different plate and call site.
3. **Upright 0.7 vs traction upright 0.8**: traction falloff uses `0xa0f698 = 0.8`; air/righting gate uses this block’s **0.7**.

---

## RE checklist

| Step | Result |
|---|---|
| `read_memory` `0xaf3380` len=16, `autoassault.exe` | `33 33 33 3f 9a 99 19 3f 00 00 a0 41 00 00 00 00` |
| float +0 | **0.7** (`0x3F333333`) upright-restore threshold |
| float +4 | **0.6** (`0x3F19999A`) neighbor only |
| float +8 | **20.0** (`0x41A00000`) speed-factor divisor |
| float +12 | **0.0** pad |
| Conflict with old “speedFactor / 0.6” | **Rejected** — divisor is +8 = 20.0 |

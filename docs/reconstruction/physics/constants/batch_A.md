# Physics constants batch A

Source: Ghidra `read_memory` on **autoassault.exe** (length 4 each).  
Values are little-endian float32. Roles from nearby reconstruction docs (not re-derived here).

| Address | LE hex | BE word | float32 | Role (if known) |
|---------|--------|---------|---------|-----------------|
| `0xa0f298` | `00 00 00 3f` | `0x3f000000` | **0.5** | Drive-impulse blend (½); rear handbrake torque ×0.5; torqueCurve2D bin base (`scale*0.5`); aero lift scale (`+0.5 ρ A Cl v²`) |
| `0xa0f2a0` | `00 00 80 3f` | `0x3f800000` | **1.0** | `g_flOne` — identity / engine-disabled return; steer/throttle clamp max; reciprocals & blends |
| `0xa0f518` | `00 00 00 00` | `0x00000000` | **0.0** | `g_flZero` — zero literal / comparisons |
| `0xa0f520` | `00 00 7a 44` | `0x447a0000` | **1000.0** | Drive-torque clamp ceiling (calcWheelTorque); also large sentinel for degenerate curvature |
| `0xa0f698` | `cd cc 4c 3f` | `0x3f4ccccd` | **0.8** | Upright threshold: `\|dot(bodyUp, worldUp)\| < 0.8` → traction falloff |
| `0xa0f694` | `00 00 f0 41` | `0x41f00000` | **30.0** | Near-target distance / curvature-radius reference (throttle ease, scale-to-zero) |
| `0xa0f70c` | `cd cc 4c 3e` | `0x3e4ccccd` | **0.2** | Low-speed traction-boost slope: `μ ×= (15−\|v\|)×0.2 + 1` |
| `0xa0f704` | `00 00 80 3e` | `0x3e800000` | **0.25** | Angular-velocity term weight in lateral-slip velocity (friction solver) |
| `0xa0f710` | `33 33 33 3f` | `0x3f333333` | **0.7** | Lateral threshold for sharp-turn path (`\|lateral\| ≥ 0.70`) |
| `0xa0f718` | `0a d7 23 3c` | `0x3c23d70a` | **0.01** | Steer deadband on `\|lateral\|`; near-zero velocity gate; wheel-setup const (`param_3+0x4c`) |
| `0xa0f730` | `cd cc cc 3d` | `0x3dcccccd` | **0.1** | Max frame-delta clamp (10 fps floor, `g_flMultiKillCountBlend`); min `cruiseScale` to apply cruise mul |
| `0xa10e74` | `00 00 00 40` | `0x40000000` | **2.0** | Steer gain (`base*lateral*2`); throttle ramp rate; rear-wheel driver-mod ×2 |
| `0xa10e78` | `cd cc 4c 3d` | `0x3d4ccccd` | **0.05** | Steer-command ramp step per tick (`VA+0x28`); path slow / curve ramp |
| `0xaf3384` | `9a 99 19 3f` | `0x3f19999a` | **0.6** | Neighbor float only — **not** the mode-0x02 speed-factor divisor |
| `0xaf3388` | `00 00 a0 41` | `0x41a00000` | **20.0** | Mode-0x02 speed-factor divisor: `speedFactor = min(speed/20, 1)` (older notes wrongly said 0.6) |
| `0xaaa668` | `00 00 80 bf` | `0xbf800000` | **−1.0** | Reverse / clamp floor; steer clamp min; suspension down-axis Y; default/sentinel friction |
| `0xaaa688` | `00 00 a0 40` | `0x40a00000` | **5.0** | Speed gate to enable throttle scaling (drive controller) |
| `0xaaa6cc` | `00 00 00 bf` | `0xbf000000` | **−0.5** | Aero drag scale: `−0.5 ρ A Cd \|v\| v` |
| `0xaaa7a4` | `00 00 70 41` | `0x41700000` | **15.0** | Low-speed traction-boost / sharp speed gate (units/s); wheel-setup force-feedback-related const |
| `0xa110d8` | `00 00 20 41` | `0x41200000` | **10.0** | Air-stabilization re-ground Y raise; default mass/inertia-related scale (not ang-damp additive) |

## Notes

- All reads: `program=autoassault.exe`, `length=4`.
- LE hex is raw memory order; BE word is the IEEE754 bit pattern as a u32.
- `0xaf3388` = **20.0** and `0xaf3384` = **0.6** (neighbor) — see `steering-spec.md` / `verified/fn_0064f840_steering.md`.
- `0xa110d8` = **10.0** is re-ground Y raise in airStab, not angular damping — see `avd-airstab-spec.md`.

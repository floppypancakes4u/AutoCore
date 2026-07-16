# Wheel `+0x88` drive scale — **RESOLVED (live CE, 2026-07-16, Task B3)**

**Program:** `autoassault.exe` (image base `0x400000`). Live capture on PID 54864 via the CE bridge
(`tmp/re/ce_client.py`); Ghidra decompiles of `preUpdate 0x64cf20` and `postTick 0x64bc70`.

## Result

`wheel+0x88` is a **per-wheel drive-torque contact gate**, not a static weight and not the torque
ratio:

| Wheel state | `wheel+0x88` | Evidence |
|-------------|-------------:|----------|
| **Grounded** (in contact) | **1.0** (`0x3f800000`) | Live, bit-exact on all 4 wheels of a spawned vehicle, ~135k write samples over 3 s |
| **Airborne** (no contact) | **0.0** | `preUpdate 0x64cf20` no-contact branch stores `pfVar7[0x22] = 0.0` (`0x22*4 = 0x88`) |

It is **rewritten every `preUpdate` tick** (store `0064D2F7  movss [esi+0x88], xmm0`, `esi` = wheel
struct), i.e. it tracks contact per frame — it is *not* a construction-time constant.

## Mechanism (how it's written)

- `preUpdate` (framework vtbl `+0x14`, runs before component updates) loops wheels
  (`wheel[i] = *(container+0x80) + i*0xC0`). Per wheel it queries contact via framework vtbl `+0x20`.
- **In contact:** it calls framework vtbl **`+0x24`** = `0x0051e900`, which for this framework class
  is an **empty `ret 0x0C` no-op** (an unused override hook), then stores `+0x88 = 1.0` (`g_flOne`).
  So there is **no per-vehicle scaling** beyond the contact gate.
- **Not in contact:** stores `+0x88 = 0.0` and clears the contact byte at `wheel+0x80`.

## Consumer (why it matters)

`postTick 0x64bc70` drive-pack aggregation (verified line, WI-MOV-004):

```
drivePack[axle] += wheels+0x28[i]  ×  wheel+0x88  /  axleWheelCount
```

`wheels+0x28[i]` is the **calcWheelTorque** per-wheel engine torque (the SoA descriptor array),
carrying the torque-ratio / handbrake / µ product. `wheel+0x88` then **gates it by contact** — an
airborne wheel (`+0x88 = 0`) contributes **zero** drive torque into the friction-solver axle pack.

## Struct layout confirmed (live)

```
framework (postTick ECX / preUpdate param_1)
  +0x0c  → wheels container
container +0x80 → wheelArray            (POINTER — deref, then + i*0xC0; NOT a +0x80 struct field)
wheel[i] = wheelArray + i*0xC0
  wheel+0x80  contact flag byte (1 grounded / 0 airborne; overlaps an adjacent pointer)
  wheel+0x88  drive-torque contact gate  (1.0 grounded / 0.0 airborne)  ← THIS
  wheel+0x8c  spin angular velocity
  wheel+0xB0  suspension current length
```

(Earlier note's "base = wheelsDesc+0x80 + i·0xC0" was off by one deref — `+0x80` holds a *pointer*
to the wheel array.)

## Port guidance (supersedes the old provisional note)

Retail keeps **two** separate factors that the current port folds into one:

1. **Torque ratio** (front/rear drive split, `RearWheelFrictionScalar` on rear) → retail applies it
   in **`calcWheelTorque`**, so it lands in the `wheels+0x28[i]` torque array.
2. **`wheel+0x88` = contact gate** → `1.0` grounded, `0.0` airborne, applied in `postTick` as the
   multiplier on that torque.

The C# port does not yet reproduce the retail `calcWheelTorque` torque-ratio path, so it currently
sets `HkWheelSetup.DriveScale = tRatio` (rear-scaled) as an **interim stand-in** for factor (1),
using `+0x88`'s slot to carry it (`HkVehicleData.FromVehicleSpecific`). That is a modeling
compromise, not the retail layout.

- **C5 refactor (the real fix):** carry `tRatio` in the torque array fed to `AggregateDrivePack`, and
  make the per-wheel scale a **pure contact gate** `inContact ? 1.0f : 0.0f` sourced from each
  wheel's runtime contact state (`HkWheelRuntimeState.InContact`). Then airborne wheels contribute
  zero drive torque *and* the front/rear split is not double-counted against the gate.
- Note: the port already gates drive elsewhere via `AxleFrictionInput.DriveEnabled = inContact && …`
  at the axle level (`VehicleActionSim`), so the *behavioral* gap of the current fold is small; the
  refactor is about matching the retail factorization exactly (relevant once `calcWheelTorque` is
  ported in C5).

## Sources

- Live capture: framework `0x34EF92C0`, container `0x34EF9EC0`, wheelArray `0x34EF9F50`, 4 wheels;
  all `wheel+0x88` = `0x3f800000` while grounded. Data-write BP on `wheel0+0x88` → writer at
  `0x64D2F7` in `preUpdate`. Framework vtable `0x9E4A40`, slot `+0x24` = `0x0051E900` (`ret 0x0C`).
- `0.3-friction-solver.md` §2a · `fn_00598040_uprightPow.md` (the `calcWheelTorque` pow half — the
  other B3 deliverable, already closed: base = `|upDot|`, exp = 4.0).
- Decompiles: `preUpdate 0x64cf20`, `postTickApplyForces 0x64bc70`.

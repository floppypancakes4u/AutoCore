# Wheel `+0x88` drive scale — status note

**Agent:** cancelled after hang (~42 min). Summary from existing RE + `postTickApplyForces` decompile plate.

## Role

In `hkVehicleFramework_postTickApplyForces` @ `0x64bc70`:

```
drivePack[axle] += (wheels+0x28[i] /* calcWheelTorque */) * (wheel+0x88) / axleWheelCount
```

So `wheel+0x88` is the **per-wheel drive-torque scale** into the friction solver’s axle drive pack
(not suspension, not steer).

## Writers (open / medium confidence)

Phase 0 docs call it “drive-wheel weight / radius factor” or “axle factor”. Likely set during:

- wheels descriptor build (`FUN_005fcce0` / `hkDefaultWheels_ctor`), and/or
- framework preUpdate geometry path

Exact first-write site was the hung agent’s target; treat **`+0x88` as setup-time constant per wheel**
until a follow-up RE pins the store.

## Port guidance

- `HkVehicleFrictionSolver.AggregateDrivePack(torques, wheelScales)` already takes per-wheel scales.
- Until the writer is proven, default `wheelScale = 1.0` or map from `HkWheelSetup.TorqueRatio`
  (rear may already include `RearWheelFrictionScalar` at setup).
- Do not invent a radius-only formula without a Ghidra store site.

## Sources

- `0.3-friction-solver.md` §2a
- `0.4-suspension.md` / `0.8-struct-offsets.md` wheel layout
- Decompile plate on `0x64bc70` (drive: `wheels+0x28[i] * wheel+0x88 / axleWheelCount`)

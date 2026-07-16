# AutoAssault Vehicle Physics Setup — VehicleSpecific → Havok Field Mapping

Reverse-engineered from `autoassault.exe` (image base 0x400000, Ghidra project AA-decode).
Read-only analysis. All addresses are RVA/VA in the loaded image.

## Setup call path

```
Vehicle_createVehicleAction        0x4fb660
  └─ Vehicle_buildHavokVehicleFramework   0x5fd390   <-- THE setup function
        ├─ FUN_005fc620                 (init framework desc container)
        ├─ FUN_005fcce0   wheels desc      -> hkDefaultWheels_ctor       0x64fee0  (size 0x390)
        ├─ hkDefaultChassis_ctor          0x64fdf0  (size 0x40; holds hkRigidBody @+0x3c)
        ├─ Vehicle_BuildSteeringDescriptor 0x5fc710 -> hkDefaultSteering_ctor 0x64fac0 (0x38)
        │                                             or TankSteering_ctor    0x64fc80 if VehSpec+0x4c0==4
        ├─ FUN_005fc3d0   wheel-collide desc -> FUN_005d6640              (size 0x3c)
        ├─ Vehicle_BuildTransmissionDescriptor 0x5fc840 -> hkDefaultTransmission_ctor 0x64f610 (0x60)
        ├─ FUN_005fcb00   brake desc         -> hkDefaultBrake_ctor       0x64ed40  (0x54)
        ├─ Vehicle_BuildSuspensionDescriptor 0x5fcff0 -> hkDefaultSuspension_ctor 0x64e510 (0x68)
        ├─ Vehicle_BuildAerodynamicsDescriptor 0x5fc4f0 -> hkDefaultAerodynamics_ctor 0x64da90 (0x50)
        ├─ (inline) AngularVelocityDamper   -> hkAngularVelocityDamper_ctor 0x64d900 (0x14)
        └─ hkVehicleFramework_ctor          0x64cd30  (size 0x360; returned)
```

Construction order inside 0x5fd390: **Wheels → Chassis → Steering → WheelCollide → Transmission →
Brake → Suspension → Aerodynamics → AngularVelocityDamper → Framework**.

### Struct access convention
Every builder reads the **VehicleSpecific** struct, reached as:
`VehSpec = *(*(entity + *(entity[1]+4) + 0xac) + 0x3c)` (clone-template ptr +0x3c).
Offsets below (0x4c0–0x740) are **relative to `VehSpec`**. Per-wheel loops split the wheel array
into **front axle** (index < `VehSpec+0x4cc` = axle-0 count) and **rear axle** (remainder); this is
how single-value front/rear DB fields fan out to all wheels.

`Vehicle_specific` field **identities** come from the clonebase XML attribute names loaded by
`VehicleDb_LoadCloneBase` (0x7efb40): `rlSuspension*`, `rlBrakesMaxTorque*`, `rlSteering*`,
`rlAerodynamics*`, `rlAVD*`, `rlGearRatios*`, `sinTorqueMax`, `tinNumberOfGears`, `rlRVInertia*`.

## Master field map

### Suspension  (Vehicle_BuildSuspensionDescriptor 0x5fcff0 → hkDefaultSuspension)
| VehSpec off (front/rear) | DB field | Havok desc slot | Havok field |
|---|---|---|---|
| +0x55c / +0x560 | rlSuspensionLengthFront/Rear | param[6] array | wheelsLength[i] |
| +0x564 / +0x568 | rlSuspensionStrengthFront/Rear | param[9] array | wheelsStrength[i] |
| +0x56c / +0x570 | rlSuspensionDampeningCoefficientCompressionFront/Rear | param[0xC] array | wheelsDampingCompression[i] |
| +0x574 / +0x578 | rlSuspensionDampeningCoefficientExtensionFront/Rear | param[0xF] array | wheelsDampingRelaxation[i] |
| +0x514.. (12B/wheel) | (per-wheel hardpoint CS vec3) | param[0] array | wheelsHardpointCS[i] |
| — (const, dir=0,down,0) | — | param[3] array | wheelsDirectionCS[i] (fixed = DAT_00aaa668) |

### Steering  (Vehicle_BuildSteeringDescriptor 0x5fc710 → hkDefaultSteering)
| VehSpec off | DB field | Havok field | Transform |
|---|---|---|---|
| +0x594 | rlSteeringMaxAngle | maxSteeringAngle | × entity[0x208] (runtime steer-angle mult) |
| +0x598 | rlSteeringFullSpeedLimit | maxSpeedFullSteeringAngle | × entity[0x20c] (runtime mult) |
| +0x5f0 bit2 / bit3 | (does-steer flags front/rear) | doesWheelSteer[i] | bitfield |

### Brake  (FUN_005fcb00 → hkDefaultBrake)
| VehSpec off | DB field | Havok field | Transform |
|---|---|---|---|
| +0x57c | rlBrakesMaxTorqueFront | wheelsMaxBrakingTorque[front] | × entity[0x200] (runtime brake mult) |
| +0x580 | rlBrakesMaxTorqueRear | wheelsMaxBrakingTorque[rear] | × entity[0x204] |
| +0x5f0 bit0 / bit1 | (handbrake flags front/rear) | wheelsIsConnectedToHandbrake[i] | bitfield |
| +0x58c / +0x590 | (min-pedal-input-to-block front/rear) | wheelsMinPedalInputToBlock[i] | — |
| (const 0) | — | wheelsMinTimeToBlock[i] | fixed 0 |

### Transmission  (Vehicle_BuildTransmissionDescriptor 0x5fc840 → hkDefaultTransmission)
| VehSpec off | DB field | Havok field | Transform |
|---|---|---|---|
| +0x699 (byte) | tinNumberOfGears | numGears | — |
| +0x69c (i16) | (downshift RPM) | downshiftRPM | × entity[0x1fc] |
| +0x69e (i16) | (upshift RPM) | upshiftRPM | × entity[0x1fc] |
| +0x6c4 | (primary transmission ratio) | primaryTransmissionRatio | — |
| +0x6c8 | rlReverseGearRatio | reverseGearRatio | — |
| +0x6cc | (clutch delay time) | clutchDelayTime | — |
| +0x6d0[i] | rlGearRatios0..N | gearsRatio[i] array | — |
| +0x5e8 / +0x5ec | (front/rear torque split) | wheelsTorqueRatio[i] (driven/undriven share, ÷axle count) | derived |

### Wheels  (FUN_005fcce0 → hkDefaultWheels)
| VehSpec off | DB field | Havok field | Transform |
|---|---|---|---|
| +0x600[i] | rlGearRatios* block reused as per-wheel torque-ratio | wheelsTorqueRatio[i] | rear wheels ×VehSpec+0x740 |
| +0x618[i] | (per-wheel friction/second value) | wheelsFriction / axle table[i] | — |
| +0x740 | (rear-wheel torque factor) | (multiplier applied to rear wheelsTorqueRatio) | scale |
| (consts) | — | wheelsForceFeedbackMultiplier, wheelsViscosityFriction, wheelsMass | fixed (DAT_00aaa7a4, DAT_00aaa68c, DAT_00a0f718, g_flMsToSeconds) |

### Wheel-collide  (FUN_005fc3d0 → raycast wheel-collide component, size 0x3c)
| VehSpec off | Havok slot | Transform |
|---|---|---|
| +0x6a8 | param[0] (wheel radius) | × entity[0x1fc] |
| +0x6ac | param[1] (wheel width)  | × entity[0x1fc] |
| +0x6b4 | param[2] (final-drive / rolling radius) | × entity[0x1fc] |
| +0x69a (i16)+entity[0x218] | param[3] (collision filter info) | additive |
| +0x6a0 | param[4] | — |
| +0x6a4 | param[5] | — |
| +0x6b8 | param[6] | — |
| +0x6bc | param[7] | — |
| +0x6c0 | param[8] | — |

### Aerodynamics  (Vehicle_BuildAerodynamicsDescriptor 0x5fc4f0 → hkDefaultAerodynamics)
Slots copied verbatim (no scaling); DB names: rlAerodynamicsAirDensity, rlAerodynamicsFrontalArea,
rlAerodynamicsDrag, rlAerodynamicsLift, rlAerodynamicsExtraGravityX/Y/Z.
| VehSpec off | Havok desc slot | Havok field |
|---|---|---|
| +0x5a8 | param[0] | airDensity |
| +0x59c | param[1] | frontalArea |
| +0x5a0 | param[2] | dragCoefficient |
| +0x5a4 | param[3] | liftCoefficient |
| +0x5ac / +0x5b0 / +0x5b4 | param[4/5/6] | extraGravity.x/y/z |

### Angular-velocity damper  (inline in 0x5fd390 → hkAngularVelocityDamper)
| VehSpec off | DB field | Havok field | Transform |
|---|---|---|---|
| +0x5b8 | rlAVDNormalSpinDamping | normalSpinDamping | × entity[0x84] (runtime mult) |
| +0x5bc | rlAVDCollisionSpinDamping | collisionSpinDamping | × entity[0x84] |
| +0x5c0 | (collision threshold / vec ptr) | collisionThreshold | — |

### Speed-governor precompute (tail of 0x5fd390 → written to vehicle+0x110 = entity[0x44])
Uses +0x699 NumberOfGears, +0x600 GearRatios[], +0x6b4/+0x6c4 (final-drive & wheel radius),
+0x5e8/+0x5ec (torque split), +0x6d0[lastGear], × DAT_009dd348. Produces a gear-ratio-weighted
top-speed constant. **Investigation anchor for SpeedLimiter / AbsoluteTopSpeed behaviour.**

## Runtime multiplier registers (prefix/upgrade adjustments)
The `entity[...]` scalars applied above are per-instance adjustment factors (from
`rl*AdjustPercent` prefixes, see `vPrefixVehicle`):
`+0x1fc` gear/wheel-dim mult, `+0x200`/`+0x204` brake front/rear, `+0x208`/`+0x20c` steering
angle/speed, `+0x84` AVD spin, `+0x218` wheel collision-filter offset.

## Fields NOT consumed by the framework builders (flagged)
| DB field | Status |
|---|---|
| rlRVInertiaRoll / Pitch / Yaw | **Not read in any framework builder.** Applied (if anywhere) to the hkRigidBody inertia tensor during chassis/body construction, or **dead**. Needs confirmation in hkDefaultChassis / rigid-body mass-properties path. |
| sinTorqueMax, MinTorqueFactor, MaxTorqueFactor | **Not in setup.** Engine was replaced — no hkDefaultEngine. Torque is produced at runtime by `VehicleAction_calcWheelTorque` (0x598040) + `VehicleEngine_torqueCurve2D` (0x4a9750). These fields feed that path, not the Havok component build. |
| rlReverseGearRatio (+0x6c8) | Consumed (reverseGearRatio) — listed for completeness. |

## Key transforms / notes
- **No hkDefaultEngine**: AA removed Havok's engine component; engine torque is fully custom (AA layer).
- Steering, brake, transmission-RPM, wheel-radius fields are **scaled by per-instance prefix
  multipliers**; suspension and aerodynamics values are copied **verbatim**.
- Front/rear DB scalars are **fanned out per wheel** using the axle-0 wheel count at `VehSpec+0x4cc`.
- `VehSpec+0x4c0 == 4` selects **TankSteering** instead of hkDefaultSteering (tracked vehicles).
- Wheel `wheelsDirectionCS` is a fixed down-vector constant, not a DB field.

## Confidence
- **High**: which VehSpec offset feeds which Havok descriptor slot (direct from decompiled builders).
- **High**: field identities for Suspension/Steering/Brake/Aero/AVD/Transmission/GearRatios
  (unambiguous semantic + XML attribute-name match).
- **Medium**: exact aerodynamics slot↔name pairing (order inferred from hkDefaultAerodynamics layout).
- **To confirm**: RVInertia application site; the +0x618 wheels-desc value's precise Havok field name.

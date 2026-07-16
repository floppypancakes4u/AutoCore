# CenterOfMassModifier apply site — status note

**Agent:** cancelled after hang (~42 min). Summary from existing Phase 0 evidence + setup decompile.

## Finding (not newly decompiled in this pass)

- **Base COM** lives in the chassis `hkpRigidBody` inside the physics asset (`.hkx` / `strPhysicsName`). Not a clonebase scalar.
- **`CenterOfMassModifier`** (`VehicleSpecific` / `fCenterOfMassModifierX/Y/Z`) is a **delta** applied on top of asset COM.
- Server port (no asset mass properties): use **unit mass**, diagonal inertia from `RVInertia*`, and apply
  `COM_local += CenterOfMassModifier` on the server rigid body (already stored on `HkVehicleData`).

## Apply site status

| Claim | Confidence |
|-------|------------|
| Modifier is additive on asset COM | High (0.2-mass-inertia.md) |
| Exact client instruction that does `COM += modifier` | **Open** — bulk-copied with chassis/body construction; not in `FUN_005fc620` inertia slots |
| Needed for handling parity | Low — mass-normalized; COM offset mainly affects weight transfer / pitch-roll |

## Port guidance

`HkVehicleData` already retains `CenterOfMassModifier{X,Y,Z}`. When integrating impulses, apply forces at hardpoints relative to `bodyOrigin + COM_modifier` if absolute COM fidelity is needed later. Do not block Phase 2–4 on locating the client apply site.

## Source

- `docs/reconstruction/physics/0.2-mass-inertia.md`
- Setup path `Vehicle_buildHavokVehicleFramework` `0x5fd390` (no COM-modifier math in the framework builder itself)

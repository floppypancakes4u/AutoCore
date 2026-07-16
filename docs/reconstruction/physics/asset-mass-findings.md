# Asset mass / COM / inertia — extraction findings (2026-07-16)

Investigation of "can the server load real chassis mass/COM/inertia from client files at startup?"
Result: **mostly already-available server-side; only one confirmation and (optionally) a collision-hull
parse remain.** This is simpler than the original 0.2-mass-inertia.md framing implied.

## What was checked

- **`physics.glm` parses cleanly** (Python mirror of `GLMLoader.cs`; format: trailing int32 → `CHNK`
  header → string table + 18-byte entries; **all 15109 entries uncompressed**, `scheme=0`,
  `size==real`). Tooling: `tmp/re/glm.py` (scratch; the format is fully in `GLMLoader.cs` so it's
  trivial to re-derive).
- **`physics.glm` content = collision geometry, NOT mass properties.** Entries are `.tk` (text convex
  hull: vertex list + triangle list) + `.cache` (compiled binary: plane equations + verts). A vehicle
  is a **compound of convex pieces** (`obj_..._sportscar-p2 … -p11`). Example
  `obj_gen_bj_mov_01_husk_sportscar-p2`: 9 hull verts, bbox `0.74×0.63×0.83`, centroid
  `(-0.014, 1.318, 2.613)` — one small piece, not the whole body. No mass/inertia scalar in these files.
- **`1500.0` (the live RB mass from B4/B1) is NOT a hardcoded code constant** — a byte-pattern search
  for `00 80 bb 44` across `autoassault.exe` hit exactly **one data address** (`0x00bbb7f4`) and no code
  immediates. So the chassis mass is **data-driven**, not a baked default.
- **Server already loads `SimpleObjectSpecific.Mass`** (rlMass; `SimpleObjectSpecific.cs:65`,
  `reader.ReadSingle()`) and all `RVInertia*` — both per-vehicle.
- ⚠️ **The 0.2 doc's loader addresses are stale/wrong.** `FUN_004f1180` and `FUN_004ee080` both
  decompile to **particle code** (MaelstromParticles / GrowingSpriteParticles) in the current Ghidra
  DB — do not trust those addresses for the rigid-body loader.

## The resulting model (what the port needs, and from where)

| Quantity | Source | Status |
|----------|--------|--------|
| **Chassis inertia tensor** | `mass × RVInertia{Roll,Pitch,Yaw}` on axes `Z/X/Y` | **Already computable** — B4 proved `I = mass × RVInertia` (live: `1500×[1,3,3]=[1500,4500,4500]` etc.); RVInertia is loaded. No asset parse. |
| **Chassis mass** | **`SimpleObjectSpecific.Mass` (rlMass)** — hypothesis | **Loaded already; needs 1 confirmation** that the chassis RB mass equals this field (see below). Not a code constant. |
| **Base center of mass** | mass-weighted centroid of the compound convex hulls in `physics.glm` | **Parseable** (demonstrated for one hull); compound-hull aggregation is the only remaining work. **Low priority** — the `CenterOfMassModifier` delta is already applied via `HkVehicleData.ComputeLeverArm`, and base-COM mainly affects weight transfer. |

**Bottom line:** if the mass hypothesis holds, the port gets **real mass + real inertia from data it
already loads**, with zero Havok-blob parsing — just replace `HkPhysicsConstants.UnitMass` in
`HkVehicleData.FromVehicleSpecific` with `SimpleObjectSpecific.Mass` (fallback to `1.0` if absent), and
inertia falls out as `mass × RVInertia`. Base COM from the hulls is a separate, optional refinement.

## CONFIRMED (2026-07-16, live CE cross-check on two different-weight vehicles)

**`chassis RB mass == SimpleObjectSpecific.Mass (rlMass) == the vehicle's real weight`, and
`inertia == mass × RVInertia`.** Two vehicles with different weights, read live via a postTick
capture (`fw → +0x30 chassis → +0x3c rb`, `mass = 1/(rb+0x2c)`, principal inertia at `rb+0xe0`):

| Vehicle | UI weight | live RB mass | live inertia `rb+0xe0` | = mass × RVInertia |
|---------|-----------|-------------:|------------------------|--------------------|
| Callisto X   | 1500 kg | **1500.0** | I=[4500, 4500, 1500] | 1500 × [Pitch 3, Yaw 3, Roll 1] |
| Astimiax 900 | 2900 kg | **2900.0** | I=[5800, 2900, 5800] | 2900 × [Pitch 2, Yaw 1, Roll 2] |

The mass **tracks the vehicle's weight** (1500→1500, 2900→2900) — it is NOT a fixed default. Since
`SimpleObjectSpecific.Mass` is the weight the server already loads, **the port can use real
mass + real inertia with zero asset parsing**: `mass = SimpleObjectSpecific.Mass`, inertia =
`mass × RVInertia` (axes Z←Roll, X←Pitch, Y←Yaw per C1). Base COM (hull centroid) remains the only
optional asset-parse, and is low priority.

> **Sequencing caveat:** real mass changes every force magnitude (`gScale = 1/invMass = mass`;
> suspension/friction impulses scale with it). The current sim is tuned around **unit mass** (the
> `MaxSuspensionForce=80` clamp, COM-force suspension, reduced friction). So the mass swap should land
> **with the C-phase** (C1 real inertia + C2 hardpoint suspension + C4 friction), not in isolation —
> dropping real mass into the unit-mass-tuned sim would destabilize it and break the current tests.

### (historical) The confirmation approach used

1. **Live cross-check (fastest, needs CE + user):** with a vehicle spawned, read the RB mass
   (`1/*(rb+0x2c)`, walk `VA this → +0x44 → +0x08 → +0x3c`) **and** the vehicle's SimpleObject/clonebase
   `rlMass` field from memory, and compare. If equal → confirmed. (Requires the "ask before CE" nod.)
2. **Static (no permission):** find the *real* rigid-body-mass setter (the 0.2 addresses are wrong).
   Trace from `Vehicle_buildHavokVehicleFramework 0x5fd390` / `hkDefaultChassis_ctor 0x64fdf0`, or find
   what reads the SimpleObject mass (`object+0x2c`, DB `rlMass`) and writes the chassis body invMass.
   Alternatively parse `clonebase.wad` for a known drivable vehicle and check its `Mass` value is a
   plausible ~1500-class number.

If the hypothesis is **wrong** (mass = shape-volume × density, or a physics-material scalar), fall back
to parsing: but note the inertia is still `mass × RVInertia` (B4), so only the mass scalar would need a
density/volume derivation — and the shape volume is computable from the `physics.glm` hulls.

## Recommended port change (once mass is confirmed)

```csharp
// HkVehicleData.FromVehicleSpecific — replace the unit-mass line:
var mass = simpleObject?.Mass > 0f ? simpleObject.Mass : HkPhysicsConstants.UnitMass; // fallback
// inertia already = mass * RVInertia (B4); COM modifier already applied via ComputeLeverArm.
```
Thread `SimpleObjectSpecific.Mass` through to `FromVehicleSpecific` (it currently only takes
`VehicleSpecific`). Keep unit-mass as the graceful fallback so nothing regresses.

## Pointers
- GLM format: `src/AutoCore.Game/Managers/Asset/GLMLoader.cs`; scratch parser `tmp/re/glm.py`.
- Mass field: `src/AutoCore.Game/CloneBases/Specifics/SimpleObjectSpecific.cs` (`Mass`, off 65).
- Inertia relationship: `0.2-mass-inertia.md §2.1` (B4 live proof `I = mass × RVInertia`).
- Constraint reopened: `remainingBackgroundWork.md` §13 + "Asset-mass extraction" task.

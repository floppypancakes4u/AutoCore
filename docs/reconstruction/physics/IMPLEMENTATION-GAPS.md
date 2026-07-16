# Implementation gaps — retail-faithful NPC vehicle physics

Living list of known fidelity holes after the RE campaign (B1–B4, B6–B8) and C/CW
implementation work. Cross-linked from `docs/agents/physicsHandoff.md` and
`docs/remainingBackgroundWork.md`.

---

## Wheel-collision channels (Task CW)

Retail wheels cast via `TtPhantom::castRay` @ `0x580ed0` against **per-shape** Havok
geometry overlapping the vehicle phantom (terrain + static world + movable bodies). See
`0.5-wheel-collide.md`.

Server CW adds `CompositeVehicleCollisionQuery` (opt-in:
`ServerConfig.CompositeWheelCollisionEnabled` / YAML `compositeWheelCollisionEnabled`,
**default OFF**). It picks the nearest of:

1. Existing `TerrainHeightfieldCollisionQuery` (height plane sample).
2. XZ `SpatialHashGrid.QueryRadius` → collidable filter
   (`VehicleMapPropRam.IsRamEligibleMapProp` for map props + other `Vehicle`s) →
   segment-vs-**proxy AABB**.

### Residual (honest — improve, not close)

| Gap | Detail |
|-----|--------|
| **No true hull geometry** | `physics.glm` `.cache` / `.tk` collision meshes are never parsed server-side (`GLMLoader` is archive I/O only). Retail fidelity target remains per-shape `hkShape::castRay`. |
| **Scalar Scale + SkirtExtents only** | Proxy half-extents from entity/CBID `SimpleObjectSpecific.Scale` (props) or `VehicleSpecific.SkirtExtents` (vehicles). No per-object AABB/OBB from assets; no compound convex pieces. |
| **Synthesized contact normals** | Object hits use AABB face normals (slab enter axis), not mesh-derived normals. |
| **2D grid has no height/extent** | `SpatialHashGrid` is XZ-only; vertical placement of proxies is invented from Position.Y ± halfY. Distant tall props off the cell neighborhood are invisible. |
| **Vehicle-vs-vehicle approximate** | Other vehicles use skirt-extent AABBs (axis-aligned, not oriented). Self is excluded so wheels do not hit own chassis. No dynamic RB response / phantom overlap list. |
| **Axis-aligned only** | Placement `Rotation` is ignored for proxy orientation. |

Terrain-only path remains the default until operators set `compositeWheelCollisionEnabled: true`.

---

## B5 — air-stab recovery

In-window corrective impulse + recovery goldens not captured. Plan:
`B5-airstab-capture-plan.md`. Out of scope for C/CW/D unless separately scheduled.

---

## Base center of mass

Asset hull centroid from `physics.glm` not parsed; only `CenterOfMassModifier` delta is
applied. Method notes in `asset-mass-findings.md`.

---

## Friction-solver edge cases

C4 ported coupled long/lat + CircleProjection + wheel-relative slip. Full dual-body /
cross-axle blob `PortSolve` against live `cb` still residual (see task-C4-report).

---

## Transmission / gear + full engine LUT

Present but vestigial; RPM/gear→torque coupling not fully wired if later goldens demand it.

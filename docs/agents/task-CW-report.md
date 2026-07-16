# Task CW report — composite wheel-collision query (improve, not close)

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**Gate:** `dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj`

## What changed

### `CompositeVehicleCollisionQuery.cs` (new)
- Implements `IVehicleCollisionQuery`: terrain pass + object/vehicle pass, **nearest** hit wins.
- Object pass: `SectorMap.Grid.QueryRadius` (XZ around wheel origin) →
  - map props via `VehicleMapPropRam.IsRamEligibleMapProp`
  - other `Vehicle`s (approximate vehicle-vs-vehicle)
  - excludes casting vehicle (`excludeSelf`)
- Segment-vs-AABB proxy from:
  - props: scalar `Scale` (entity, else CBID) as full edge → half = scale/2
  - vehicles: `VehicleSpecific.SkirtExtents` × instance scale
- Object hits: `IsTerrain: false`, synthesized face normals.

### `NpcVehiclePhysicsController.BuildCollisionQuery`
- Single construction point wraps terrain in composite when
  `ServerConfig.CompositeWheelCollisionEnabled` is true **and** map is non-null.
- Passes casting `vehicle` as `excludeSelf`.
- **Default OFF** — behaviour unchanged until opted in.

### `ServerConfig`
- Flag: **`CompositeWheelCollisionEnabled`**
- YAML key: **`compositeWheelCollisionEnabled`** under `npcVehiclePhysics`
- Default / `ResetToDefaults`: **false**
- Documented (commented) in Launcher + Sector `serverConfig.yaml`

### Tests (`CompositeVehicleCollisionQueryTests` + `ServerConfigTests`)
- Proxy-box hit (non-terrain + upward normal)
- Nearest-of-terrain-vs-object (both directions)
- Non-collidable filtering → terrain
- Miss object → terrain
- Null map → terrain only
- Self-vehicle exclude
- Other vehicle skirt-extents hit
- `BuildCollisionQuery` flag off/on
- AABB slab unit seam
- Config defaults / YAML / reset

### Docs
- `docs/reconstruction/physics/IMPLEMENTATION-GAPS.md` (created)
- Cross-link from `docs/reconstruction/physics/README.md`

## Flag name

| Surface | Name |
|---------|------|
| C# | `ServerConfig.CompositeWheelCollisionEnabled` |
| YAML | `npcVehiclePhysics.compositeWheelCollisionEnabled` |
| Default | `false` |

## Residual gaps

1. **No true hull geometry** — `physics.glm` never parsed; retail target is `TtPhantom::castRay` / per-shape `hkShape::castRay`.
2. **Only scalar Scale + SkirtExtents** for proxy bounds (no asset AABB/OBB/compounds).
3. **Synthesized contact normals** (AABB face), not mesh normals.
4. **2D XZ grid** has no per-object height/extent metadata.
5. **Vehicle-vs-vehicle** is approximate axis-aligned skirt AABBs (rotation ignored).

## Suite result

```
Passed:  2970
Failed:     1  (baseline only: DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll)
Skipped:    4
Total:   2975
```

Zero new failures vs baseline gate. Physics master opt-in unchanged; composite flag defaults OFF.

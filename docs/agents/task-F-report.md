# Task F report — verification, rollout, live checklist

**Branch:** `feature-NPC-Retail-Driving`  
**Date:** 2026-07-16  
**HEAD at F:** see `git log --oneline` (D3 + earlier C/CW commits)

## What landed this session (implementation chain)

| Task | Commit (short) | Summary |
|------|----------------|---------|
| C2 | `9231f3a6` | Suspension contact-point impulses; clamp gated off |
| C4 | `2ebcf2fb` | Wheel-relative slip; coupled friction; CircleProjection; crawl fixed |
| C5 | `28ba8f3b` | Upright pow + wheel drive-scale contact gate |
| C8 | `641acde4` | Ticked service brake into friction path |
| C-mass | `50b8b05c` | `SimpleObjectSpecific.Mass` → chassis mass/inertia |
| CW | `d71e7eba` | Composite wheel collision (flag default OFF) |
| D1 | `89ffeb97` | Controller contracts for sim authority |
| D2 | `20fdf5bf` | Sim-authoritative controller (no force-restore) |
| D3 | `9712b43f` | Clear physics instance on teleport/death/respawn |
| F | (this commit) | Docs + opt-in YAML restore + live checklist |

**Deferred:** C7 (retire speed clamps — after more Phase E green).

## Suite gate (unit)

```
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj
```

Expected: **exactly 1** pre-existing failure
`DeathLootDeliveryTests.AutoLootItem_AddsCargoWithCreateAddResponseCargoSendAll`.
Zero new failures. ~2962+ pass, ~4 skip (C-phase residuals).

## Opt-in rollout (shipped defaults)

| Layer | Physics default |
|-------|-----------------|
| `ServerConfig` C# | `NpcVehiclePhysicsEnabled=false`, `ControllerTier=Hard` |
| Launcher / Sector `serverConfig.yaml` | **`enabled: false`**, **`controllerTier: hard`** (restored in F) |
| CW composite casts | `compositeWheelCollisionEnabled` default OFF |

### One-line flip for live A/B (both Launcher and Sector YAML)

```yaml
npcVehiclePhysics:
  controllerTier: physics
  enabled: true
  # optional:
  # compositeWheelCollisionEnabled: true
  # debugLogging: true
```

Rebuild Launcher **from this worktree** after the flip. Rollback = `enabled: false` / `controllerTier: hard` (or `kinematic`).

## Live Launcher checklist (needs explicit user approval)

Do **not** start Launcher without asking. When approved:

1. Stop any existing Launcher; build this worktree’s `AutoCore.Launcher`.
2. Flip YAML as above; start Launcher; attach client.
3. A/B each scenario vs kinematic/hard:

| # | Scenario | Pass criteria |
|---|----------|---------------|
| 1 | Flat cruise | Grounded stance, forward progress, no float |
| 2 | Banked / constant-radius turn | Stays grounded, no upward drift explosion |
| 3 | Ramp | Lip launch + ballistic arc + landing settle |
| 4 | Cliff drop | Free fall then settle / re-ground |
| 5 | Curb / step | Anti-sink position lift, no tunnel |
| 6 | High-speed S-curve | Bounded lateral slip, no flip explosion |
| 7 | CW on/off | Props/ramps interact when composite flag on |

Optional CE spot-checks of `inst.Body` vs live client — **ask before using Cheat Engine**.

## Docs updated in F

- `docs/reconstruction/physics/README.md` — status table
- `docs/reconstruction/physics/IMPLEMENTATION-GAPS.md` — C7/E/D residuals
- Shipped YAML opt-in restored
- This report + project memory `retail-npc-driving-branch.md`

## Residuals (do not block live A/B)

See `IMPLEMENTATION-GAPS.md` — B5, base COM, PortSolve bit-exact, downhill micro-hop, ramp climb, C7 clamps, D2 steer residual, CW proxy geometry.

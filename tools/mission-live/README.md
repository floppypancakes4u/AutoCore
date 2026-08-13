# Mission Live Testing Harness

Python orchestrator that drives a live Auto Assault client (DevTool named pipe)
and AutoCore Dev API to validate registry missions.

## Prerequisites

1. AutoCore Launcher/Sector running with Dev API on `27999`
2. Patched client with `DevTool.dll` loaded (`\\.\pipe\devtool`)
3. Character logged in with GM level ≥ 1
4. Python 3.10+ and `pip install -r requirements.txt` (PyYAML, colorama, pytest)

## Commands

```powershell
cd tools\mission-live
pip install -r requirements.txt

python -m mission_live doctor
python -m mission_live categories
python -m mission_live list --category travel
python -m mission_live run --category travel
python -m mission_live run --category cargo --force-grant
python -m mission_live run                  # TTY: pick a category from a menu
python -m mission_live run --id 1234
python -m mission_live run --id 1234 --force-grant
python -m mission_live run --registry
python -m mission_live coverage
python -m mission_live report --open
```

Or via wrapper:

```powershell
.\scripts\run-mission-live.ps1 doctor
.\scripts\run-mission-live.ps1 categories
.\scripts\run-mission-live.ps1 run --category travel
.\scripts\run-mission-live.ps1 run --id 1234
```

Categories come from the **retail catalog** (not only `registry/missions.yaml`): `missions.json` via `MISSION_LIVE_CATALOG`, else `missions.glm` + `clonebase.wad` (`AA_INSTALL` / `MISSION_LIVE_GLM` / `MISSION_LIVE_WAD`).

| Category | What it is |
|----------|------------|
| `travel` | talk → optional patrol → speak-only turn-in |
| `cargo` | item hand-in (patrol + deliver only) |
| `combat` | kill / kill_aggregate |
| `collect` | collect |
| `other` | mixed / remaining |

`run --category` walks that bucket in continent → level → id order. Combat/collect/other still SKIP unsupported requirement types.

NPC interact can open a **multi-mission picker** (`mission-dialog: kind=select`) instead of a single Accept/Complete box. Setup and turn-in send `mission pick <id>` to click the matching `[level] name` row, then continue with Accept/Complete/OK.

**Multi-pad AutoComplete patrol** (Crater Run / 874): one objective lists several GenericTargets. `/tptowaypoint` snaps to the **next** pad and the server credits that visit (same as client AutoPatrol `0x20B3`) so the GPS/journal can advance without an Observe/NPC click. The patrol strategy loops `/tptowaypoint` until `progress >= max` (or the objective/mission finishes). `mission patrol` is a read-only DevTool line (`id/seq/progress/max/next`) for live diagnosis.

### Race / class filter

`run --category` and `run --registry` ask the Dev API (`GET /mission-state`) for the logged-in character's race/class **before** any mission setup. Missions whose `reqRace` / `reqClass` do not match are dropped from the queue (no `/clearAllMissions`, warp, or prereq seed). `--force-grant` disables the prefilter. `run --id` still fetches a plan, but setup now checks race first and returns immediately on mismatch.

## Registry

Edit [`registry/missions.yaml`](registry/missions.yaml) by hand, or import from retail data:

```powershell
python scripts/add_mission_to_registry.py --title "Live and Direct"
python scripts/add_mission_to_registry.py --id 3032
```

Example entry shape:

```yaml
missions:
  - id: 1234
    policy: partial   # partial | strict
    tags: [patrol]
    notes: "starter patrol"
```

- `partial` — unsupported requirement types are SKIP; mission may end PARTIAL
- `strict` — any SKIP fails the mission
- `--force-grant` — if race/class gates block natural accept, use `/giveMission` and log `forceGrant` in the report

## Extending strategies

Add a module under `mission_live/strategies/`, implement `execute(ctx, req, mission_id=, seq=)`, register in `strategies/__init__.py` `STRATEGIES` dict.

## Outputs

- `out/results.json` — machine-readable run log
- `out/report.html` — self-contained HTML report + coverage matrix

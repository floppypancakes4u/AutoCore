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
python -m mission_live run --id 1234
python -m mission_live run --id 1234 --force-grant
python -m mission_live run --registry
python -m mission_live coverage
python -m mission_live report --open
```

Or via wrapper:

```powershell
.\scripts\run-mission-live.ps1 doctor
.\scripts\run-mission-live.ps1 run --id 1234
```

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

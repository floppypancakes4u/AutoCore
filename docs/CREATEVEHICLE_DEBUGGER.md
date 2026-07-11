# CreateVehicle / Havok — automated debugger

## Primary path (Python harness)

PowerShell wrappers are **deprecated** (attach thrash / encoding / false-arm). Use:

```bat
scripts\cv-debug.cmd arm --background --no-wait
scripts\cv-debug.cmd check
scripts\cv-debug.cmd status
scripts\cv-debug.cmd disarm
```

Or from `tools/`:

```bat
cd tools
python -m createvehicle_debug arm --background --no-wait
python -m createvehicle_debug check
python -m createvehicle_debug status
python -m createvehicle_debug disarm
```

`arm` without `--background` **blocks** (owns the cdb session). Prefer `--background` so a detached monitor keeps cdb alive.

### Workflow

1. Start a **healthy** game client (restart if it was frozen by a bad attach).
2. **`arm --background --no-wait`** — single cdb attach (`-pd -cf`, no `-g`), arm confirmed from **this session’s log only**. Client freezes ~2–5s at attach, then resumes.
3. Reproduce at your pace (login / Ark Bay foreign CreateVehicle).
4. **`check`** — parse hits → `docs/debugger-hits/SUMMARY.json` + `hits.jsonl`.
5. **`disarm`** when done.

### Safety

| Rule | Why |
|------|-----|
| `-pd` | Debugger exit detaches without killing the game |
| **No `-g`** | With this cdb, `-g` prevents `-cf` from running (BPs never set) |
| `-cf` script ending in `g` | Sets BPs at attach break, then resumes (~few seconds freeze) |
| No force-kill of cdb if client not responding | Leaving a suspended process mid-break freezes the game |
| One attach; refuse if cdb already running | Stops re-attach thrash |
| Arm markers from session log only | No false OK from stale `LATEST.log` |

### Artifacts

| Artifact | Path |
|----------|------|
| Live log | `docs/debugger-hits/LATEST.log` |
| Session log | `docs/debugger-hits/createvehicle-*.log` |
| Status | `docs/debugger-hits/STATUS.json` |
| Hits | `docs/debugger-hits/hits.jsonl` |
| Summary | `docs/debugger-hits/SUMMARY.json` |
| Agent log | `docs/debugger-hits/agent.log` |
| Local cdb | `tools/cdb/cdb.exe` |
| BP script | `scripts/createvehicle-auto-debug.cdb` |
| Harness | `tools/createvehicle_debug/` |

### Capture points (auto-continue after log)

| Label | VA | Dumps |
|-------|-----|--------|
| RecvCreateVehicle | `0x0080A4B0` | packet CBID, `+0x458/+0x45c` wheelset opcode/CBID |
| EquipFromCreate | `0x00504480` | `vehicle+0x258`, equip CBID |
| SetWheelset | `0x004FEA90` | wheelset object, `+0x258` |
| CreateVehicleAction | `0x004FB660` | `+0x258` |
| WheelCountGetter NULL | `0x004F5560` | only if `vehicle+0x258==0` |
| AV | first-chance | eip / vehicle |

### Tests

```bat
cd tools
python -m pytest createvehicle_debug/tests -v
```

## Deprecated

`scripts/createvehicle-*-debug*.ps1` — do not use for arming.

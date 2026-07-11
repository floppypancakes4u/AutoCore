# NPC motion debug (client + server)

## Why

Foreign NPC vehicles **skip/teleport** along paths every ~250–500 ms. Server WireDiag often shows only a handful of `GhostPack` pose deltas per vehicle, while the client hard-snaps each pack via `Vehicle_setDrivingInputs` → `FUN_0053EEC0`.

Guessing is slow. Capture **client pose-apply cadence** and **server pack density** in the same session.

## Client capture (non-freezing MinHook)

Extends Path A hook with **`Vehicle_setDrivingInputs` @ `0x00504C70`**.

Logs to `%TEMP%\AutoCorePathA\hits.jsonl`:

| Field | Meaning |
| --- | --- |
| `ev=SetDrivingInputs` | Ghost pose applied on client |
| `px,py,pz` | Network position |
| `vx,vy,vz` | Network velocity |
| `throttle` | Ghost accel field ([-1,1]) |
| `dt` | Integrate dt (`ghost+0xBC * 0.001`) |
| `action` | `vehicle+0x1A0` VehicleAction* (null → throttle ignored) |
| `v258` | Wheelset pointer |

### Workflow

1. Rebuild hook (after PathAHook.cpp change):
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\PathAHook\build.ps1
   ```
2. Start a **healthy** client (restart if cdb froze it earlier).
3. Arm:
   ```bat
   scripts\path-a-debug.cmd arm
   ```
   Setup line must include `"drive":1`.
4. Login, watch Gunny / patrol for **15+ seconds**.
5. Parse:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\parse-motion-hits.ps1
   ```
6. Expectation if server streams well: **avgGapMs ~50–150** per vehicle.  
   If **avgGapMs ~250–500** or sparse hits: client only sees starved packs (or local apply is gated).  
   If **action is null** on foreign NPCs: throttle never reaches Havok.

## Server capture

| Signal | Where |
| --- | --- |
| `Sector ghost rates: period=… packetSize=…` | Connect (TNL floor) |
| `PathPoseForce dirty=N` | Every ~2s while path NPCs exist |
| `[WireDiag] GhostPack … mask=0x2` | Each pose pack |

Healthy: **dozens** of `mask=0x2` packs per pathing coid per 10 s, not 3–4 then silence.

```powershell
Select-String server-live.log -Pattern "GhostPack name=GhostVehicle|PathPoseForce|Sector ghost rates" |
  Select-Object -Last 40
```

## Capture 2026-07-11 evening (user)

```
SetDrivingInputs ~400+ hits on one vehicle
actionPresent 100%          ← VehicleAction exists (throttle path live)
thr mostly 1.0              ← cruise throttle reaches client
dt avg ~0.053s              ← integrate dt usually non-zero
gap p10=94  p50=484  p90=516  max=781
gaps>400: majority of intervals
```

**Conclusion:** Client has the ingredients for smooth motion (`action`, `throttle=1`, `dt≈50ms`). The skip is **starved pose applies** — median **~480 ms** between `setDrivingInputs`, with short clusters at ~100 ms. After a long gap the position jump is ~15–25 u (visible teleport); in a dense cluster jumps are ~1.6–2 u.

Server was force-dirtying 30 path vehicles while WireDiag often showed only a handful of packs — dirties on unghosted shells never pack. Follow-ups: dirty only **ghosted** path vehicles, **50 ms** sector tick, log `posePacks2s` + negotiated period/size.

## Related RE

- `docs/nullWheels.md` — movement RE, rate starvation, keep-dirty
- `docs/PATH_A_DEBUGGER.md` — Path A equip hooks (same injector)

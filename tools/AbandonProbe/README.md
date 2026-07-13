# AbandonProbe

Non-freezing **ReadProcessMemory** probe for why the Mission Journal **Abandon** button
does not appear in `autoassault.exe`.

Does **not** use cdb or DLL inject — the client keeps running.

## Build / run

```powershell
cd tools\AbandonProbe
dotnet test AbandonProbe.Tests\AbandonProbe.Tests.csproj
dotnet run --project AbandonProbe -- selftest
dotnet run --project AbandonProbe -- probe
dotnet run --project AbandonProbe -- watch
```

## What it checks

| Layer | Source (RE) | Live fields |
|-------|-------------|-------------|
| **Create gate** | `FUN_008a5fe0` @ `0x008A674F` → `0x004CE340` on `DAT_00D1B644` | `gameClient+0xAC`, `+0x100` |
| **Journal chrome** | `DAT_00D1B898` | `+0x50C` tab, `+0x518` selection, `+0x584` abandon btn ptr |
| **Show rules** | `FUN_008a3510` | Self only; need selection; need non-null abandon ptr |
| **Pending confirm** | `DAT_00D1B4B4` | mission id if confirm dialog armed |

**Not per-mission:** no mission id participates in create/show. If create gate blocks, *all* missions lack Abandon.

## Typical diagnoses

| PRIMARY | Meaning |
|---------|---------|
| `CreateGateBlocked` | Helper returned true → Abandon control never created (`+0x584 == 0`) |
| `WrongTab` | Button exists; switch to **Self** tab |
| `NoSelection` | Select a mission row |
| `ShouldBeVisible` | Logic says show it — if missing, UI skin/layout issue |

## Workflow

1. Log into the client with active missions.
2. Open **Mission Journal**, **Self** tab, select a mission.
3. Run `probe` (or `watch` while flipping tabs).
4. Read `PRIMARY` and `wouldCreateAbandon` / `abandon ptr`.

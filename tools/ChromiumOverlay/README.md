# Chromium Overlay (experimental)

Transparent Chromium/CEF overlay for the retail Auto Assault client, with an injected bridge that publishes game-side state into the page.

**Branch:** `experiement-chromium`

## Architecture

```text
ChromiumLauncher.exe
  ├─ starts autoassault.exe -developer
  ├─ injects ChromiumBridge.dll (x86, MinHook-ready)
  └─ starts ChromiumHost.exe (CEF overlay)

ChromiumBridge  ── MMF Local\AutoCoreChromium_State ──►  ChromiumHost
```

The CEF host is **out-of-process** (x64). Only the thin bridge DLL is injected into the 32-bit game.

### Overlay presentation

- **Off-screen CEF** (no browser child HWND)
- **Per-pixel alpha** via `UpdateLayeredWindow` + `ULW_ALPHA`
- **Fully click-through:** `WS_EX_TRANSPARENT` + `HTTRANSPARENT` + `WS_EX_NOACTIVATE`
- Page: transparent background, large white **Hello World**, bridge JSON in `#state`

## Prerequisites

- .NET 8 SDK
- VS 2022 C++ x86 tools for the bridge
- MinHook under `AutoCore.DevTool/SpeedHook/minhook`
- CefSharp pulled via NuGet on first host build

## Build

```powershell
powershell -ExecutionPolicy Bypass -File tools\ChromiumOverlay\build-all.ps1
```

## Run

```powershell
# Default: prefers Auto Assault.bak (patched RE client) when present
.\tools\ChromiumOverlay\ChromiumLauncher\bin\Debug\net8.0-windows\ChromiumLauncher.exe

# Or explicit:
.\ChromiumLauncher.exe --game-path "C:\Program Files (x86)\NetDevil\Auto Assault.bak"
```

### MSXML 4.0 dialog

The **stock** `Auto Assault\exe\autoassault.exe` (2007) still checks for MSXML 4.0 and can show a legacy “Microsoft XML parser 4.0” error even when newer MSXML is installed. The local **patched** client is:

```text
C:\Program Files (x86)\NetDevil\Auto Assault.bak\exe\autoassault.exe
```

ChromiumLauncher’s default (no `--game-path` / no `AA_INSTALL`) uses **`.bak` when that exe exists**. Do not point at the stock install if you already patched the check out of the bak build.

Useful flags: `--pid N`, `--no-launch`, `--skip-inject`, `--skip-host`, `--bridge`, `--host`.

Environment: `AA_INSTALL` = game install root (overrides the bak preference when set).

## Success criteria

1. Client starts with `-developer` and **stays running**
2. Large white **Hello World** over the game; **background fully transparent**
3. `#state` updates with bridge JSON (`tick` advancing)
4. Mouse/keyboard pass through to the game

## If the game closes immediately

Launcher prints which step killed the process:

```text
[FAIL] Game died during/after inject — CEF was not started yet.
[FAIL] Game exited … after CEF host start
```

Isolate:

```powershell
# Inject only (no overlay window)
.\ChromiumLauncher.exe --skip-host

# Host only (no DLL inject) — attach to already-running game
.\ChromiumLauncher.exe --no-launch --skip-inject
```

Bridge setup no longer calls MinHook (MMF + publisher only). Overlay waits ~2s off-screen before covering the client.

## Logs

| Log | Path |
|-----|------|
| Bridge | `%TEMP%\AutoCoreChromium\bridge.log` |
| Host | `%TEMP%\AutoCoreChromium\host.log` |
| CEF | `%TEMP%\AutoCoreChromium\cef.log` |

If the game is up but you see no overlay:

1. Open `%TEMP%\AutoCoreChromium\host.log`
2. Look for `Cef.Initialize returned True`, `COVER`, `ULW ok=True`
3. **Exclusive fullscreen hides layered overlays** even when `ULW ok=True`. Your `.bak` client had `MODE_WINDOWED=0`. Fix:
   ```powershell
   powershell -File scripts\aa-set-windowed.ps1 -GamePath "C:\Program Files (x86)\NetDevil\Auto Assault.bak"
   ```
   Then restart the game (windowed 1024×768) and re-run the launcher.

## Player combat pools (HP / Power / Shield)

Bridge reads the local player vehicle (in-process) and publishes:

```json
{"hasVehicle":true,"hp":80,"maxHp":100,"power":40,"maxPower":50,"shield":20,"maxShield":30,...}
```

Offsets (bak client / DevTool): shield `+0x144/+0x148`, power `+0x12C/+0x12E` (int16), HP via vtable `+0x248/+0x240` (SEH-guarded). Values appear after you are **in-world on a vehicle**.

## Extending (game → CEF)

1. Extend the publisher JSON in `ChromiumBridge.cpp`
2. Host pushes into `window.__applyGameState` / GDI status line

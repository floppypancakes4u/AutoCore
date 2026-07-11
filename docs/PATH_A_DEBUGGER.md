# Path A Debugger (non-freezing)

Captures the retail client **CreateVehicle equip chain** without attaching a debugger that freezes DirectInput.

## Why this exists

The older `cv-debug` / cdb path freezes the client (attach break + DirectInput). Path A investigation needs live Ark Bay entry with foreign `CreateVehicle`, so capture must be **in-process MinHook logging only**.

## Components

| Piece | Path |
| --- | --- |
| Hook DLL | `tools/PathAHook/PathAHook.dll` |
| Build | `tools/PathAHook/build.ps1` |
| Injector CLI | `tools/PathADebug` → `PathADebug.exe` |
| Wrapper | `scripts/path-a-debug.cmd` |
| Hit log | `%TEMP%\AutoCorePathA\hits.jsonl` |
| Repo copy | `docs/debugger-hits/path-a-hits.jsonl` (via `check`) |

## Hooked VAs (image base `0x400000`)

| Event | VA | What is logged |
| --- | --- | --- |
| RecvCreateVehicle | `0x0080A4B0` | packet ptr, root CBID, wheel opcode/CBID |
| EquipFromCreate | `0x00504480` | vehicle, packet, root opcode/CBID, wheel CBID/opcode/TFID, isInventory/isActive, mode, `nest_hex` when wheel CBID≤0, `+0x258` before/after |
| SetWheelset | `0x004FEA90` | vehicle, wheelset object, `+0x258` before/after |
| CreateVehicleAction | `0x004FB660` | vehicle, `+0x258` enter/exit |
| ActivateEnterWorld | `0x00503F30` | vehicle, `+0x258` enter/exit |

All detours call the original function; they never `int3` or suspend.

## Workflow

1. Start a **healthy** client (restart if previously frozen by cdb).
2. Build hook (once):
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\PathAHook\build.ps1
   ```
3. Arm:
   ```bat
   scripts\path-a-debug.cmd arm
   ```
4. Login and enter Ark Bay (foreign CreateVehicle fires).
5. Inspect:
   ```bat
   scripts\path-a-debug.cmd check
   scripts\path-a-debug.cmd tail
   ```
6. Disarm: exit the client (no remote unload API yet).

## Interpreting hits

| Pattern | Meaning |
| --- | --- |
| `SetWheelset` with `v258_after` non-null | Path A equip succeeded for that vehicle |
| `EquipFromCreate` with `v258_after` null and wheel_cbid valid | GiveItem/SetWheelset path failed silently |
| `EquipFromCreate` with `wheel_cbid=0` and `v258` stays null | Nested packet has CBID **0** (not −1 empty) — no SetWheelset |
| `ActivateEnterWorld_enter` with `v258_before` null | Owner/ghost armed Havok without wheelset → expected AV setup |
| `CreateVehicleAction_enter` with `v258_before` null | Same, closer to crash site |

## Capture 2026-07-11 (first live run)

- **5/9** EquipFromCreate left `+0x258` null; all had `wheel_cbid=0`, opcode `0x201B`, no SetWheelset.
- **4** vehicles equipped cleanly (`wheel_cbid` 40 or 52) and ran CreateVehicleAction with non-null wheelset.
- Conclusion: fix nested CreateVehicle emissions that put **CBID 0** in the wheelset slot; owner-on will AV on those objects.

## Safety

- Client build is verified (same hash as AuthHook / SpeedHook).
- Prologue bytes checked before each MinHook install.
- No cdb, no `-g`, no attach break.
- If `arm` fails with exit 0 from setup, see `hits.jsonl` for `SetupPathAHook_FAILED`.

## Related

- Full chain map: `docs/nullWheels.md` § Client chain map
- Do not use: `scripts/cv-debug.cmd` for this investigation

# System: User input / command handling

**ID:** SYS-INPUT  
**Priority:** 1  
**Status:** partial reconstruction  
**Updated:** 2026-07-15

## Scope

Keyboard/DIK → action-slot table → edge flags → frame poll that fires quickbar, interact, UI toggles, and drive-related inputs.

## Known entry points

| Symbol | Address | Role |
|--------|---------|------|
| Client_Input_OnKeyDown_MatchAction | 0x00911030 | WM key-down: match DIK (+ LSHIFT 0x2A) against action table; set held/edge |
| Client_Input_PollBoundActions | 0x00925d60 | Per-frame: consume edge flags → QuickBar, interact, UI, chat reply |
| Client_Input_DriveControlTick | 0x009223b0 | Drive axes from held bindings → entity + push controller |

## Behavioral flow

1. Key down → scan action-slot table (base ~DAT_00d1bc18 / entry DAT_00d1bbee, stride 0x34 / 0x1a shorts).
2. On match: entry held (+2/+4 region) and edge (+5) set; `FUN_0093a5c0(1)`.
3. ESC special-cases UI cancel / special-event abort / menu stack (large branch).
4. Poll: for each binding with held && edge, clear edge and dispatch (QB slots 0–9 + shift-QB, UseObject, etc.).
5. DriveControlTick separately samples longitudinal/steer/brake holds into entity +0x614/+0x618/+0x61c.

## State owners

- Global action-slot table around `DAT_00d1bc18` / `DAT_00d1bbee` (stride 0x34 bytes).
- Pair pattern: held @ slot, edge @ slot+1 (documented in Ghidra plate).
- Local player entity `DAT_00d1b6d8` / game object with +0xe98 character.

## Dependencies

- SYS-ACTIONS (QuickBar activate)
- SYS-MOVEMENT (drive axes)
- SYS-INTERACT (UseObject from poll)

## Confidence

- Control flow of key match + edge set: **high**
- Full meaning of every DAT_* binding: **probable** (need bind table dump)
- ESC UI cancel graph: **probable**

## Open questions

- Complete bind-index → gameplay-action enumeration
- Relation of DirectInput poll vs WM path

## Verification gaps

- Runtime key→edge not captured this session (UF-002)
- PollBoundActions is large; not every branch reconstructed as clean source

## Next recommended work

- Dump action table layout into OBJECT_LAYOUTS
- Finish clean reconstruction of DriveControlTick key axis section

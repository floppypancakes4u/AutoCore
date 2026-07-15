# Review: Client_SendRespawnInSector (aa_exe_00935300)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_00935300.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_SendRespawnInSector.cpp` |
| Caller path | `docs/reconstruction/raw/aa_exe_0091ee20.md` / `Client_INC_ContactCountdownTick.cpp` (option 0) |
| System doc | `docs/reconstruction/systems/death-respawn.md` (if present; RESPAWN notes in raw plate) |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x2073` | C2S RespawnInSector |
| Wire size | `0x28` | including opcode |
| Entity | game `+0xe98` | must be non-null |
| Vehicle child | entity `+0x250` | must be non-null |
| Selection gate | `*FUN_00402ae0(tmp) == game+0xd28` | must match selected object |
| Pose source | `FUN_00404c90` / `FUN_00404a20` | **current** pos / quat (not destination) |
| COID fields | entity TFID at off `+0x164`/`+0x168` | packet `+0x20`/`+0x24` (`local_18`/`local_14`) |
| Post-send | `FUN_007fc840` | UI/state follow-up |

## Concrete raw ↔ clean matches (3+)

1. **Early gates** — Both require `game+0xe98 != 0` and `entity+0x250 != 0`; else return without send.
2. **Selection gate** — Both: `*FUN_00402ae0(...) == *(game+0xd28)` or abort (no packet).
3. **Packet layout** — Opcode `0x2073`; three floats from position helper; four floats from quaternion helper; 64-bit COID from local entity TFID `+0x164`/`+0x168`; `Client_SendSectorPacket(game, 0x28, &pkt)`.
4. **static_assert / size** — Clean `sizeof(RespawnInSectorPacket) == 0x28` matches raw size argument `0x28`.
5. **Caller** — INC countdown option `0` calls this after airlift toast (separate unit `aa_exe_0091ee20`).

## Skeptical review (unit-specific falsification)

1. **Hypothesis: packet COID is the vehicle COID.**  
   **Falsified for this builder:** COID is read from entity at `game+0xe98` TFID slots (`+0x164`/`+0x168`). Raw plate + live note: **character** coid. Vehicle is only required as child `+0x250` presence gate. (Downstream `RecvSpecialEvent` also matches packet TFID to `+0xe98` character — same quirk family.)

2. **Hypothesis: pos/quat are the destination repair-station pose.**  
   **Falsified:** helpers are current vehicle pose (`FUN_00404c90` / `FUN_00404a20`). Server resolves destination (LastStation / pad); client only ships current pose + coid.

3. **Hypothesis: send proceeds without a selected object match.**  
   **Falsified:** selection gate against `game+0xd28` is mandatory in both raw and clean.

4. **Hypothesis: this function handles SpecialEvent presentation.**  
   **Falsified:** this is **C2S only**. Presentation is S2C `0x20A9` (`Client_RecvSpecialEvent`). Do not merge reviews.

## Residual uncertainty (this unit)

- Exact identity of `FUN_00404c90` / `FUN_00404a20` (which transform space) — treated as “current vehicle pose” from plate.
- Contents of `FUN_007fc840` after send (UI disable? busy flag?) not expanded.
- Death-UI entry that starts the INC countdown remains UF-001 (caller chain partial via `0091ee20`).
- Runtime capture of `0x2073` payload (UF-002) not re-run this session.

## Verdict

**Accept** — static C2S `0x2073` size `0x28`, gates, current pose packing, and character COID source match raw. Not runtime-confirmed.

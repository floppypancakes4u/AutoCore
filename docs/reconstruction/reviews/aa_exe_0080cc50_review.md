# Review: Client_RecvSpecialEvent (aa_exe_0080cc50)

**Date:** 2026-07-15  
**Roles performed:** reconstruction, context, skeptical (static)

## What was inspected

| Artifact | Path |
|----------|------|
| Raw decompile | `docs/reconstruction/raw/aa_exe_0080cc50.md` |
| Clean reconstruction | `docs/reconstruction/reconstructed-exact/Client_RecvSpecialEvent.cpp` |
| Dispatch map | `docs/reconstruction/raw/aa_exe_00815710.md` (case `0x20a9`) |
| Upstream C2S | `aa_exe_00935300` RespawnInSector (different unit) |

**Key constants / offsets (this unit only):**

| Item | Value | Role |
|------|-------|------|
| Opcode | `0x20A9` | S2C SpecialEvent |
| Event type | packet `+0x04` | `0` Respawn, `1` TeleportOut, `2` TeleportIn |
| Dest pos | packet `+0x08` float3 | pose bundle |
| Dest quat | packet `+0x14` float4 | pose bundle |
| Target TFID | packet `+0x28` / `+0x2c` | match local entity |
| Respawn flag | packet `+0x40` | non-zero for full Respawn ctor path |
| Local entity | game `+0xe98` | character in live captures |
| Vehicle required | entity `+0x250` | null → silent return |
| Fallback type | clone type `0x14` | Vehicle only |

## Concrete raw ↔ clean matches (3+)

1. **Fast-path TFID compare** — Both: if `game+0xe98` non-null, compare entity TFID at off `+0x164`/`+0x168` to packet `+0x28`/`+0x2c`; mismatch → resolve fallback.
2. **Resolve fallback** — Both: `CVOGReaction_ResolveObjectTarget(1, tfid_lo, tfid_hi)`; null → return; clone type at `pi[0x2a]+0x38` must be `0x14`; else return; then vfunc `+0x1dc` entity for further work.
3. **Vehicle child gate** — Both: `*(entity + 0x250) == 0` → return (no event).
4. **Type dispatch** — Type `0` → `operator_new(0x70)` + `ClientSpecialEvent_Respawn_ctor(..., flag at +0x40 != 0, pose)`; `1` → new `0x34` TeleportOut; `2` → new `0x50` TeleportIn; other → return.
5. **Dispatch** — PacketDispatch `0x20a9` → this handler.

## Skeptical review (unit-specific falsification)

1. **Hypothesis: packet TFID should be vehicle coid for respawn anim.**  
   **Falsified by raw plate + live note:** fast path compares to local entity at `+0xe98` (**character** coid in live captures). Vehicle coid mismatches → fallback; fallback only accepts clone type `0x14` and then still needs `+0x250`. Sending vehicle coid silent-returns in practice when resolve fails or type path doesn’t yield a usable entity — plate warns “silent-returns (no anim).” Clean **deliberately preserves** character-vs-vehicle quirk.

2. **Hypothesis: type 0 Respawn ignores packet+0x40.**  
   **Falsified:** raw passes `*(int*)(param_1 + 0x40) != 0` into Respawn ctor. Clean models `flag != 0`.

3. **Hypothesis: missing local entity always hard-fails.**  
   **Falsified:** `game+0xe98 == 0` jumps to resolve fallback (same as TFID mismatch), not immediate return.

4. **Hypothesis: this function sends RespawnInSector.**  
   **Falsified:** pure S2C presentation. C2S is `0x2073` / `Client_SendRespawnInSector`.

## Residual uncertainty (this unit)

- Exact stack layout of pose bundle passed into Respawn ctor (`local_50` packing from packet floats) — clean leaves pose arg simplified/`nullptr` placeholder with comment.
- Full behavior of `FUN_00403150` registration after event construction.
- TeleportOut/In ctor internals out of scope.
- UF-001 death UI still not fully closed (this unit is presentation only).

## Verdict

**Accept** — TFID fast/fallback, type 0/1/2 ctors, vehicle gate, and character-coid quirk match raw. Pose-bundle argument packing to ctors is slightly idealized in clean but control flow is solid.

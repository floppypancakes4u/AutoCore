# System: Progression — XP / Level / Currency notifies

**ID:** SYS-PROGRESSION  
**Priority:** ~7–8 (economy/progression vertical)  
**Status:** complete (static) with residuals  
**Related:** UF-005 **closed** / WQ-018 **complete** (floater 0x34 / type 3 @ +0x30 wired)  
**Prior authority doc:** [`docs/XP.md`](../../XP.md) — **do not replace**; this page is the reconstruction-tree index for client handlers.

---

## Packet roles (critical distinction)

| Opcode | Name | Direction | Semantics | Client entry |
|--------|------|-----------|-----------|--------------|
| **`0x2017`** | `CharacterLevel` | S→C | **Absolute** snapshot: level, XP total, currency, pools, mana/HP | `Client_RecvCharacterLevel` @ `0x00810f00` → `CVOGCharacter_ApplyCharacterLevelPacket` @ `0x00531e90` |
| **`0x205F`** | `EMSG_Sector_GiveXP` | S→C | **Additive** XP grant + floater | `Client_AwardKillExperience` @ `0x0080ae70` → `CVOGReaction_AddExperience` (non-kill path) |
| **`0x205E`** | `EMSG_Sector_GiveCredits` | S→C | **Additive** currency delta + floater/sound | `Client_RecvGiveCredits` @ `0x0080cac0` → `CVOGCharacter_AddCredits` |

There is **no** client function literally named `GiveXp` — server-side AutoCore `ExperienceService.GiveXp` is the recommended mirror of `CVOGReaction_AddExperience` + notify packets.

Dispatch map: `Client_PacketDispatch` @ `0x00815710` cases `0x2017` / `0x205e` / `0x205f` (see `raw/aa_exe_00815710.md`).

---

## End-to-end client paths

### A) Login / restore / full UI sync — `0x2017`

```
Client_PacketDispatch (0x2017)
  → Client_RecvCharacterLevel (0x00810f00)
       Lookup TFID (packet +8/+c/+10)
       → vfunc+0xcc CVOGCharacter_ApplyCharacterLevelPacket (0x00531e90)
            absolute: Level@char+0x6c8, Currency@+0x720, XP@+0x730,
                      skill/attrib/research, mana; gated HP to vehicle
       if local TFID match:
            Client_RefreshLocalCharacterLevelUi (0x0092f4d0)
            optional HUD vfuncs
       Client_RefreshOpenMissionUiWindows
```

**Wire (with opcode):** Level `u8` @ `+0x18`, Currency `i64` @ `+0x20`, Experience `i32` @ `+0x28`, Health @ `+0x2C`, mana shorts @ `+0x34`, point pools thereafter.  
AutoCore: `CharacterLevelPacket.cs`.

### B) Mid-session XP grant — `0x205F`

```
Client_PacketDispatch (0x205f)
  → Client_AwardKillExperience (0x0080ae70)
       char = game+0xe98; else VOG_DEBUG_STOP
       CVOGReaction_AddExperience(char, amount@+4, PacketOrNonKill)
       if LevelHint@+8 != -1: char+0x738 = hint; +0x734 = GetTickCount()
       if vehicle@char+0x250: combat floater type 3 (XP)
```

**Wire:** Amount `i32` @ `+0x04`, LevelHint `i8` @ `+0x08` (`-1` = none).  
AutoCore: `GiveXPPacket.cs`.

**Important:** Packet path uses **`PacketOrNonKill`**, so the 5s kill spree / weapon-bonus branch inside `AddExperience` does **not** run. Spree for retail kills is either baked into server `Amount` or computed only on pure client kill paths (`CVOGCombat_CalculateAndAwardKillXP` with `KillPath`).

### C) Mid-session credits — `0x205E`

```
Client_PacketDispatch (0x205e)
  → Client_RecvGiveCredits (0x0080cac0)
       CVOGCharacter_AddCredits(char, amount i64 @ +0x08)  // → +0x720
       positive: "credits" UI sound
       vehicle: floater type 4 (if char+0xd6c == 0); money HUD refresh
```

AutoCore: `GiveCreditsPacket.cs` / `CurrencySync`.

### D) Apply kernel — `CVOGReaction_AddExperience` @ `0x00533c30`

| Step | Behavior |
|------|----------|
| KillPath only | Spree byte `+0x738` within 5s, clamp 0..5; optional weapon table scale `(table[i]+1)*amount` with index from nested `+0xe818` (0..15) |
| Always | `scaled = (int)(amount * float@+0xc54)` personal XP gain |
| Soft cap | If at max level (`+0xc50`) and `specialMode(+0x6b4)<1`: clamp so total stays below `Experience_GetCumulativeThreshold(level)-1` |
| Apply | `totalXp(+0x730) += scaled`; return false if scaled==0 |
| Level | Up/down loops via `CVOGCharacter_LevelUp` / `LevelDown`; guard ~300; threshold sentinel `0x7fffffff` |

Threshold table: `Experience_GetCumulativeThreshold` @ `0x0052c860` → `tExperienceLevel.intExperience` (map row `+0x10`).

Level-up grants: `CVOGCharacter_LevelUp` @ `0x00532d30` — skill/attrib from row `+0x14` (packed), research row `+0x18` → char `+0x580`; LogicUI `0x2D` when notify.

---

## Character field map (client)

| Offset | Field | Set by |
|--------|-------|--------|
| `+0x6c8` | Level | Apply absolute; LevelUp/Down |
| `+0x6cc` / `+0x6ce` | Skill / attribute points | Apply absolute; LevelUp add |
| `+0x580` | Research points | Apply absolute; LevelUp add |
| `+0x720` | Currency (int64) | Apply absolute; AddCredits delta |
| `+0x730` | Total experience | Apply absolute; AddExperience delta |
| `+0x734` | Last kill / hint timestamp | KillPath; LevelHint path |
| `+0x738` | Spree / level-hint byte | KillPath; LevelHint path |
| `+0xc50` | Max level | config |
| `+0xc54` | Personal XP gain float | UI “Personal XP Gain” |
| `+0x6b4` | Special mode (skip cap) | state |

---

## Strings / UI anchors

| Address | String |
|---------|--------|
| `0x009d64cc` | `EMSG_Sector_GiveXP` |
| `0x00a15a5c` | `Level Up` |
| `0x009e290c` | `Personal XP Gain: %0.1f%%` |
| `0x009e28f0` | `Convoy XP Gain: %0.1f%%` |
| `0x009e5278` | `You have received %d %s experience` |
| `0x00a8b16c` | `//tExperienceLevel/row` |
| `0x00a8b5d0` | `//tCreatureExperienceLevel/row` |

---

## Formulas (summary — full detail in `docs/XP.md`)

| Concern | Formula gist | Client site |
|---------|--------------|-------------|
| **Apply** | `total += (int)(amount * personalGain)`; level via cumulative thresholds | `0x00533c30` |
| **Kill** | CreatureXP ± level-diff × XPPercent × share × global scalar; spree +5%/stack in combat calc | `0x004d80b0` (+ death `0x004da630`) — **global scalar BSS 0 in retail image** |
| **Mission** | `span(TargetLevel) * QuestFrac[XPIndex] * scalers` on final objective | `0x0059dde0` via CompleteObjective |
| **LevelUp grants** | skill/attrib/research from `tExperienceLevel` row for new level | `0x00532d30` |

Server recommendation: compute award server-side → send `0x205F` with final amount → send `0x2017` after level-ups for absolute UI consistency (see XP.md persistence section).

---

## Reconstruction artifacts

| Kind | Path |
|------|------|
| Raw | `raw/aa_exe_00810f00.md` (v1 existing), `raw/aa_exe_0080ae70.md`, `raw/aa_exe_0080cac0.md`, `raw/aa_exe_00533c30.md`, `raw/aa_exe_00531e90.md`, `raw/aa_exe_00532d30.md`, `raw/aa_exe_0052c860.md` |
| Exact | `reconstructed-exact/Client_RecvCharacterLevel.cpp`, `Client_AwardKillExperience.cpp`, `Client_RecvGiveCredits.cpp`, `CVOGReaction_AddExperience.cpp`, `CVOGCharacter_ApplyCharacterLevelPacket.cpp` |
| Functions | `functions/aa_exe_00810f00.md`, `aa_exe_0080ae70.md`, `aa_exe_00533c30.md`, `aa_exe_0080cac0.md` |
| Reviews | `reviews/aa_exe_0080ae70_review.md`, `aa_exe_00533c30_review.md`, `aa_exe_00810f00_review.md` |

---

## Residuals (not eligible high-pri — UF-005 / WQ-018 closed)

| ID | Residual | Class |
|----|----------|-------|
| — | Kill formula detail under `docs/XP.md` (not re-derived this pass); weapon-bonus table index source (`+0xe818`) not fully typed | optional depth |
| WQ-RT-01 / UF-002 | Runtime dual-run / live floater amounts need Launcher approval | blocked |
| GLOBAL_KILL_SCALAR | Still BSS 0 at `0x00B037F8` — client-local kill XP dead in this image | image fact (not open work) |

---

## AutoCore mapping

| Client | AutoCore |
|--------|----------|
| GiveXP wire | `GiveXPPacket` |
| CharacterLevel wire | `CharacterLevelPacket` / `CharacterLevelManager` |
| GiveCredits wire | `GiveCreditsPacket` / `CurrencySync.AddCredits` |
| Apply kernel | `ExperienceService.GiveXp` (intended mirror) |

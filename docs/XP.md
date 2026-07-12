# Experience (XP) system

Server-authoritative experience awards for Auto Assault, reverse-engineered from the retail client (`autoassault.exe`) and validated against `wad.xml` / clonebase fields already loaded (or still missing) in AutoCore.

**Scope of this doc:** exact calculations, data sources, packets, AutoCore gaps, and a recommended server shape.  
**Not in this doc:** production code changes (implement later from this spec).

Ghidra target: `autoassault.exe`.

---

## Authority model

| Concern | Owner |
|--------|--------|
| Compute award amount (kill / mission / area / reaction / outpost) | **Server** — must match formulas below |
| Persist total XP + level + level-up points | **Server** — write-through absolute columns on `character` (see [DB persistence](#db-persistence)); incomplete today |
| Apply local total + combat floater | Client via `GiveXP` (`0x205F`) → `CVOGReaction_AddExperience` |
| Full level / XP / currency / points UI | Client via `CharacterLevel` (`0x2017`) |
| Client-local kill calculation | Present in binary but **non-functional here** (see [GLOBAL_KILL_SCALAR](#open-issue-global_kill_scalar-dat_00b037f8)) — do **not** rely on it for multiplayer |

Retail multiplayer path: server computes amount → sends `EMSG_Sector_GiveXP` → client applies and shows the purple XP floater (combat message type `3`).

---

## Opcodes and packets

| Direction | Opcode | Name | Role |
|-----------|--------|------|------|
| S→C | `0x205F` | `EMSG_Sector_GiveXP` | Grant amount + optional level/spree hint; floater |
| S→C | `0x2017` | `CharacterLevel` | Full snapshot (level, XP, currency, attrib/skill/research) |

### `GiveXP` wire format

Client handler: `Client_AwardKillExperience` @ `0x0080AE70`  
AutoCore: `src/AutoCore.Game/Packets/Sector/GiveXPPacket.cs`

| Offset | Size | Field |
|--------|------|--------|
| `0x00` | 4 | Opcode `0x205F` (written by `SendGamePacket`) |
| `0x04` | 4 | `Amount` (`int32`) — XP to apply |
| `0x08` | 1 | `LevelHint` (`sbyte`); **`-1` (`0xFF`)** = no hint |

Handler behavior:

1. Bail if no local client character.
2. `CVOGReaction_AddExperience(Amount, isKillPath=0)`.
3. If `LevelHint != -1`, write byte to character `+0x738` and timestamp `+0x734` (same fields used as kill-spree counter / bonus input).
4. Queue combat floater packet type **`3`** (XP) on the local vehicle.

### `CharacterLevel` wire format

AutoCore: `src/AutoCore.Game/Packets/Sector/CharacterLevelPacket.cs`

Notable fields: `Level`, `Currency`, **`Experience`** (absolute total), attribute/skill/research point pools. Used after login (credits/XP restore pattern) and after level-ups.

### Debug chat (current AutoCore)

`/xp` / `/experience` in `ChatManager` only sends a `CharacterLevelPacket` with `Experience = amount`. That is a **UI set**, not a grant, and does not persist XP (there is no experience column yet).

---

## Data tables

Canonical source on a stock install:  
`C:\Program Files (x86)\NetDevil\Auto Assault\wad.xml`

Client SQL strings in-binary confirm the same schemas (`//tExperienceLevel/row`, `//tCreatureExperienceLevel/row`, `//tQuestXPLookup/row`, `//tContinentExploredAreas/row`, `//tOutpost` pulse query).

### `tExperienceLevel` — player level thresholds

| Column | Meaning |
|--------|---------|
| `IDLevel` | Level (1..120 in retail data) |
| `intExperience` | **Cumulative** XP required to **be** this level / finish previous span |
| `iSkillPoints` | Granted on reaching this level |
| `iAttributePoints` | Granted on reaching this level |
| `iResearchPoints` | Granted on reaching this level |

AutoCore: `ExperienceLevel` + `WorldDBLoader` / `WadXmlWorldDataLoader.LoadExperienceLevels`.

**Samples (retail `wad.xml`):**

| Level | Cumulative XP | Skill | Attrib | Research |
|------:|--------------:|------:|-------:|---------:|
| 1 | 1000 | 1 | 2 | 0 |
| 2 | 3300 | 1 | 2 | 0 |
| 3 | 5600 | 1 | 2 | 0 |
| 4 | 8800 | 1 | 2 | 0 |
| 5 | 12000 | 1 | 2 | 3 |
| 10 | 39000 | 1 | 2 | 2 |
| 20 | 135000 | 1 | 2 | 2 |
| 120 | 9483000 | 3 | 2 | 0 |

**Level span** for level `L` (XP needed while *at* level `L` to reach `L+1`):

```
span(L) = Experience[L] - Experience[L-1]   // L > 1
span(1) = Experience[1]
```

Examples: `span(1)=1000`, `span(5)=12000-8800=3200`.

Lookup client-side: `FUN_0052c860` @ `0x0052C860` (map keyed by level → `intExperience` at record `+0x10`).

### `tCreatureExperienceLevel` — kill base XP

| Column | Meaning |
|--------|---------|
| `IDCreatureLevel` | Creature / effective level index |
| `intExperience` | Base XP for that level |

**Not loaded by AutoCore today.** Required for kill XP and (recommended) area XP.

**Samples:**

| Creature level | Base XP |
|---------------:|--------:|
| 0 | 38 |
| 1 | 39 |
| 2 | 40 |
| 5 | 45 |
| 10 | (see full table) |
| 20 | 112 |
| 50 | 263 |
| 125 | 685 |

126 rows in retail data (0..125).

Lookup: `FUN_004c97b0` @ `0x004C97B0`.

### `tQuestXPLookup` — mission XP fraction

| Column | Meaning |
|--------|---------|
| `IDQuestXPIndex` | Matches `MissionObjective.XPIndex` |
| `rlLevelXP` | Fraction of the mission’s **level span** |

**Not loaded by AutoCore today.**

| Index | `rlLevelXP` |
|------:|------------:|
| 0 | 0 |
| 1 | 0.02 |
| 2 | 0.04 |
| 3 | 0.06 |
| 4 | 0.08 |
| 5 | 0.10 |
| 6 | 0.15 |
| 7 | 0.20 |
| 8 | 0.25 |
| 9 | 0.30 |

### `tContinentExploredAreas` — first-visit area metadata

| Column | AutoCore field | Meaning |
|--------|----------------|---------|
| `IDContinentObject` | `ContinentObjectId` | Continent |
| `IDExploredArea` | `Area` | Area id 1..32 |
| `strExploredAreaName` | `AreaName` | UI name |
| `intXPLevel` | `XPLevel` | Reward level index (0 = none) |

Loaded. Geometry is **not** here — TGA sampling is documented in `Documentation/MAP_REVEAL.md`.

Retail: 741 rows; `intXPLevel` histogram includes many `0`s and values up through `80`.

### Creature clonebase — `XPPercent`

`CreatureSpecific.XPPercent` (float), read from clonebase. Used as a kill multiplier (`creature+0x500` in client death path).

### Mission objective binary fields

`MissionObjective` (`src/AutoCore.Game/Mission/MissionObjective.cs`):

| Field | Role |
|-------|------|
| `XP` | Static int (legacy / display / special requirements; **not** the primary complete formula) |
| `XPIndex` | → `tQuestXPLookup` |
| `XPScaler` | Multiplier on fraction |
| `XPBalanceScaler` | Multiplier on fraction |
| `Credits` / `CreditsIndex` / `CreditScaler` | Credit sibling of XP |
| `SkillPoints` / `AttribPoints` | Granted on objective advance/complete paths |

Mission template also has **`TargetLevel`** (`short`) — used as the level whose span feeds mission XP (client mission field used at calc time).

### `tOutpost` — pulse XP percent

| Column | Meaning |
|--------|---------|
| `bIsOutpost` | True vs false outpost pulse tables |
| `lPulseIndex` | Pulse step |
| `lMilliSecondsToNextPulse` | 900000 ms (15 min) in retail |
| `fPercentLevelXP` | Fraction of **current player level span** |
| `lNumTokens` | Outpost tokens (separate from XP) |

**Not loaded by AutoCore today.**

Sample true-outpost percents: `0.0006`, `0.0012`, `0.0024`, `0.0072`.

---

## Core apply path

### `CVOGReaction_AddExperience` @ `0x00533C30`

**Thiscall:** `bool AddExperience(Character* this, int amount, char isKillPath)`

This is the single client kernel that mutates total XP and levels. Server `GiveXp` should mirror the same rules for multiplayer consistency (especially cap, personal scalar, and level-up table grants).

#### Algorithm

```
// 1) Kill-path only (isKillPath != 0)
if isKillPath:
  now = GetTickCount()
  if now - lastKillTs(+0x734) < 5000:
    spree(+0x738) = min(spree + 1, 5)
  else:
    spree = 0
  lastKillTs = now
  if weaponContextAllowsBonus(FUN_004ce340):
    // scale amount by (table[spreeClamped] + 1.0f); spree clamped to 0..15
    amount = round((table[i] + 1.0f) * amount)

// 2) Personal XP gain scalar (UI: "Personal XP Gain: %0.1f%%")
scaled = (int)(amount * personalXpGain(+0xc54))

// 3) Soft cap at max level
if atMaxLevel(+0xc50) and not specialMode(+0x6b4):
  scaled = min(scaled, xpToJustBelowNextThreshold - 1)

// 4) No-op
if scaled == 0: return false

// 5) Apply
totalXp(+0x730) += scaled

// 6) Level up / down loops (guard ~300 iterations)
while totalXp >= threshold(currentLevel):  // FUN_0052c860
  LevelUp(FUN_00532d30)                   // points from tExperienceLevel
// negative XP can de-level via FUN_005330e0

return true
```

**Packet grants** (`GiveXP` handler) call with **`isKillPath = 0`**, so the 5s spree table scaling in step 1 does **not** run on S→C grants. Spree/bonus for kills is either:

- applied inside kill calc **before** `AddExperience(..., 1)` on pure client paths, and/or  
- reflected by server choosing `Amount` (and optionally `LevelHint`).

For AutoCore, prefer: **server computes final integer amount** (including spree if you implement it), send `GiveXP` with `isKillPath` semantics already baked into `Amount`, `LevelHint = -1` unless you intentionally drive the `+0x738` byte.

#### Character field map (client)

| Offset | Use |
|--------|-----|
| `+0x730` | Total experience |
| `+0x6c8` | Level (short/int usage in level-up) |
| `+0xc50` | Max level cap |
| `+0xc54` | Personal XP gain multiplier (float) |
| `+0x734` | Last kill timestamp |
| `+0x738` | Spree / level-hint byte |
| `+0x6b4` | Special mode flag (affects cap) |

#### Level-up: `FUN_00532d30` @ `0x00532D30`

On each level gained:

- Increment level.
- Add `iSkillPoints`, `iAttributePoints`, `iResearchPoints` from `tExperienceLevel` for the new level.
- Dirty flags, skill refresh, `CVOGCharacter_SearchAutoMissions`, optional LogicUI type `0x2D`.

De-level: `FUN_005330e0` reverses point grants.

---

## Kill XP

### Entry points

| Address | Name | Role |
|---------|------|------|
| `0x004DA630` | death / loot handler | Builds mult, convoy loop, calls kill XP |
| `0x004D80B0` | `CVOGCombat_CalculateAndAwardKillXP` | Formula + `AddExperience(..., 1)` |

### Inputs

| Input | Source |
|-------|--------|
| Player level `P` | Killer character level (vtable `+0x27c`) |
| Victim level `V` | Dead creature/vehicle level |
| `XPPercent` | Creature template (`CreatureSpecific.XPPercent`, client `+0x500`) |
| Participation | Damage share scalar from combat bookkeeping |
| Convoy factors | Extra product terms at call site; up to 4 members in range |
| Spree byte | Character `+0x738` (0..5 on kill path) |

Call-site product (simplified):

```
mult = XPPercent * participation * convoyShareOrOne
```

### Level-difference base — `FUN_004c9800` @ `0x004C9800`

Uses `tCreatureExperienceLevel` via `FUN_004c97b0`.

Prep in kill XP (before lookup): if the high side of the level pair exceeds the low by more than **3**, clamp the high side to `low + 3`.

Let `diff = P - V` after that prep (player minus victim).

#### Easy / grey kills (`diff ≥ 0`)

1. `base = CreatureXP[V]` (victim level row).
2. If grey-check enabled and **`diff ≥ 10`** → **return 0** (tutorial text: gray = worthless, no XP).
3. Else:

```
adj = round(diff * 1.5 * base * (-0.1))   // ≈ -15% of base per level above
base' = max(0, base + adj)
```

Constants:

| Address | Type | Value |
|---------|------|------:|
| `0x009CBB68` | double | `1.5` |
| `0x009CBB60` | double | `-0.1` |

#### Hard kills (`diff < 0`)

Victim higher than player: lookup a higher creature-level row (difference clamped, floor around **−9**) and interpolate with:

| Address | Type | Value |
|---------|------|------:|
| `0x00AAA6A4` | float | `0.005` |

```
base' = CreatureXP[boostedLevel] + floor(abs(extraDiff) * CreatureXP[...] * 0.005)
```

(Exact intermediate clamp matches `FUN_004c9800` negative branch — port from assembly if tests disagree.)

### Final kill amount — `CVOGCombat_CalculateAndAwardKillXP`

```
eff = LevelDiffBase(P, V)                    // § above

// Optional multi-recipient blend (count > 0), constant 0.1 at 0x00A0F730:
if count > 0:
  eff = ceil( (eff + trunc(count * 0.1 * eff)) / count )

raw = ceil(eff * GLOBAL_KILL_SCALAR * mult)

if raw < 1:
  xp = 0
else:
  stacks = (spreeByte <= 1) ? 0 : (spreeByte - 1)   // 0.05 at 0x009CBF80
  xp = raw + ceil(stacks * raw * 0.05)

AddExperience(character, xp, isKillPath=1)
// local player may also emit combat floater type 3
```

### Open issue: `GLOBAL_KILL_SCALAR` (`DAT_00b037f8`)

| Fact | Detail |
|------|--------|
| Use site | `0x004D8142` — `FMUL dword ptr [0x00B037F8]` |
| Static image | BSS dword **0** |
| Writers in binary | **None** found |
| Effect | Client-local kill awards always compute **0** XP in this build |

Older plate comments assumed `0.1`; that constant is **not** present at this address.

**Server recommendation:** implement with:

```
GLOBAL_KILL_SCALAR = 1.0
```

so `tCreatureExperienceLevel` values are the unit of XP (same-level L1 kill ≈ **39** before `XPPercent` / share).  

**Validate live:** one kill, log server amount vs client floater. If retail pacing feels wrong, adjust **only** this scalar.

### Convoy

- Death handler can award each in-range convoy member (loop of 4, distance gate).
- UI strings: “Convoy XP Gain”, “Personal XP Gain”.
- Personal multiplier still applied inside `AddExperience` via `+0xc54`.

### Server hook (intended)

On authoritative NPC/vehicle death where a player (or convoy) earned credit:

```
amount = ComputeKillXp(killer, victim, damageShare, convoyContext)
GiveXp(killer, amount, source=Kill)
// repeat per eligible convoy member with their own share
```

---

## Mission XP

### When it is awarded (client RE)

`CVOGReaction_CompleteObjective` @ `0x00533F90`:

| Situation | XP | Skill/attrib on objective |
|-----------|----|---------------------------|
| Advance to next objective | **No** `AddExperience` in this path | Yes (`FUN_005312c0` / `FUN_00531250`) |
| **Final** objective (mission complete) | **Yes** — `FUN_0059DDE0` then `AddExperience(calc, 0)` | Yes on complete branch |
| Credits on complete | `FUN_0059DF20` | added to currency fields |

### Calculator — `FUN_0059DDE0` @ `0x0059DDE0`

```
frac     = tQuestXPLookup[objective.XPIndex].rlLevelXP
spanMult = objective.XPBalanceScaler * frac * objective.XPScaler

L        = mission.TargetLevel          // short; client mission+0x11c
cum      = tExperienceLevel[L].intExperience
if L > 1:
  levelSpan = cum - tExperienceLevel[L-1].intExperience
else:
  levelSpan = cum

xp = (int)(levelSpan * spanMult)        // trunc toward zero in decompile
```

Complete path then applies a **±0.5001** style adjust (`DAT_00aaa6d0 ≈ 0.5001`) before int cast when packaging the grant (nearest-int behavior).

**Worked example:**

- `TargetLevel = 5` → span `12000 - 8800 = 3200`
- `XPIndex = 5` → `0.10`
- `XPScaler = XPBalanceScaler = 1.0`
- **XP = 320**

### Static `MissionObjective.XP`

Parsed and logged by AutoCore (`NpcInteractHandler` incomplete-handler text). Client **complete** path prefers the **calculator**, not this int. Treat raw `XP` as:

- fallback only if `XPIndex == 0` and calc returns 0 **and** live missions prove they need it, or  
- data for special requirement types (e.g. crazy-taxi `ExpReward` tables — separate XML export path @ `FUN_005acf10`).

### Credits sibling — `FUN_0059DF20`

Same shape as XP but uses credit lookup tables / `CreditsIndex` + `CreditScaler`. Implement beside mission XP when economy is wired.

### AutoCore gap

`NpcInteractHandler.AdvanceOrCompleteObjective` currently:

- sends complete/advance packets and journal updates,
- **does not** apply XP, credits, skill, or attrib rewards (explicit `IncompleteHandlerLog` warnings).

---

## Area / exploration XP

### Facts

- Tutorial string: first visit to a named area grants XP and map reveal.
- Server already: TGA sample → bit set → persist → `UnlockRegion` (`ExplorationManager`, `MAP_REVEAL.md`).
- Client explore bit path (`CVOGCharacter_SetAreaExploredBit` @ `0x005326B0`, `Client_LocalDiscoveryTick` @ `0x005D6C60`) **does not** call `AddExperience`.
- Therefore first-visit XP is **server-only** in a private-server world (same as intended multiplayer).

### Recommended formula (hypothesis — validate live)

```
meta = ContinentAreas[(continentId, areaId)]
if meta is null or meta.XPLevel <= 0:
  return  // bit still set; no XP

amount = tCreatureExperienceLevel[meta.XPLevel].intExperience
GiveXp(character, amount, source=Exploration)  // once per first bit
```

**Why this mapping:** `intXPLevel` values line up with creature level indices (1..80), not quest fractions or absolute thousands. Example: continent `691` area `1` “Green land” has `intXPLevel=1` → expect **~39 XP**.

**Alternatives** (if live disagrees):

1. `LevelDiffBase(playerLevel, XPLevel)` with `mult = 1`.
2. `span(playerLevel) * (XPLevel * k)` — no strong RE support yet.

`MAP_REVEAL.md` already notes GiveXP on first visit as future work.

### Idempotency

Only when `TryRevealArea` flips a **new** bit (same as fog unlock).

---

## Reaction XP

| `ReactionType` | Value | Intended server behavior |
|----------------|------:|--------------------------|
| `AddXP` | 28 | `GiveXp(activator, amount)` |
| `SetLevel` | 79 | Adjust level / grant XP-to-level |

Template fields (`ReactionTemplate`): `GenericVar1` (int), `GenericVar2` (float), `GenericVar3` (int).

Client sites near `0x0057DF4B` / `0x0057DFFA` convert a float amount to int and call `AddExperience(..., 0)`.

**XP needed to reach relative level** — `FUN_0052DEC0` @ `0x0052DEC0`:

```
need = (int)(ExperienceThreshold(level + delta - 1) / personalXpGain) - currentXp + 1
```

AutoCore: type 28 is **not** handled in `Reaction.TriggerCore` (falls through / no economy). Do **not** rely on client-only `GroupReactionCall` (`0x206C`) for XP authority.

**Field mapping:** treat `GenericVar1` as absolute XP for `AddXP` until a map dump shows otherwise; document any counterexample found in `.fam` reactions.

---

## Outpost pulse XP

`FUN_00607830` @ `0x00607830`:

```
if outpostState[+0x238] < 1: return 0

levelSpan = Exp(playerLevel) - Exp(playerLevel - 1)   // 0 if level <= 1 edge cases
percent   = pulseTable[fPercentLevelXP]                 // FUN_006075b0
scalar    = outpost[+0x21c]

amount = round_to_int(levelSpan * percent * scalar)
```

One call site gates player level **`≥ 60`** (`0x3C`) before awarding — outpost-specific, not a global XP rule.

Pulse interval in data: **900000 ms**. Tables differ for true outpost vs false (`bIsOutpost`).

---

## Packet / UI contract (server)

| Event | Packets | Notes |
|-------|---------|--------|
| Any successful grant | `GiveXP { Amount, LevelHint=-1 }` | Client applies + floater |
| Level changed | also `CharacterLevel` full snapshot | Keep server Level/XP/points authoritative |
| Login | `CharacterLevel` after create | Same pattern as credits restore |
| Mission complete | existing mission packets **plus** GiveXP | Avoid double-grant |
| Area first visit | existing `UnlockRegion` **plus** GiveXP | Once per bit |

**Idempotency rules:**

- Kill: once per death credit event.
- Mission: once per mission completion (and per design for intermediate skill/attrib only).
- Area: once per explored bit.
- Reaction: once per successful trigger (respect reaction cooldowns/conditions).

---

## AutoCore today vs intended

| Source | Intended | Current AutoCore | Priority |
|--------|----------|------------------|----------|
| Persist total XP | Absolute `character.Experience` + `Level` on every grant; login `CharacterLevel` restore | **No** `Experience` column; Level never updated on grant | P0 |
| Kill | § Kill XP + `GiveXP` | Not awarded | P0 |
| Mission complete | § Mission XP + `GiveXP` | Explicit incomplete warnings | P0 |
| Mission intermediate | Skill/attrib points | Not applied | P1 |
| Area first visit | § Area XP + `GiveXP` | Bits + map only | P1 |
| Reaction `AddXP` | Flat `GiveXP` | Unhandled | P1 |
| Outpost pulse | § Outpost | Not implemented | P2 |
| Admin `/xp` | Grant or set + persist | UI-only `CharacterLevel` | P2 |
| Load `tCreatureExperienceLevel` | Required for kill/area | Missing | P0 |
| Load `tQuestXPLookup` | Required for missions | Missing | P0 |
| `GiveXP` packet | Production grants | Combat text probe only | — |

Probe reference: `CombatTextCommand` type 3 sends `GiveXP` for floater testing — correct opcode, not a real economy path.

---

## Recommended server shape (implement later)

```
ExperienceService
  Tables:
    ExperienceLevels          // already loaded
    CreatureExperienceLevels  // new from wad/DB
    QuestXpLookup             // new from wad/DB
    ContinentAreas            // already loaded (XPLevel)

  GiveXp(character, amount, source, levelHint = -1)
    → personal scalar, cap, total += amount
    → while level-up: grant table points, bump Level
    → persist
    → Send GiveXP; if leveled Send CharacterLevel

  ComputeKillXp(killer, victim, damageShare, convoy)
  ComputeMissionXp(mission, objective)   // TargetLevel + XPIndex/scalers
  ComputeAreaXp(continentId, areaId)
  XpToRelativeLevel(character, delta)    // SetLevel support
```

**Hooks:**

| Hook site | Call |
|-----------|------|
| Combat death credit | `ComputeKillXp` → `GiveXp` |
| `AdvanceOrCompleteObjective` final | `ComputeMissionXp` → `GiveXp` (+ credits later) |
| Intermediate objective | skill/attrib only (RE) |
| `ExplorationManager` new bit | `ComputeAreaXp` → `GiveXp` |
| `ReactionType.AddXP` | `GiveXp(GenericVar1)` |

Follow repo TDD (`AGENTS.md`): table-driven unit tests using the sample rows in this doc before wiring hooks.

### Minimal `GiveXp` pseudocode

```
function GiveXp(character, amount, source, levelHint = -1):
  if amount == 0: return
  amount = (int)(amount * character.PersonalXpGain)   // default 1.0
  amount = ClampSoNotExceedingMaxLevel(character, amount)
  if amount == 0: return

  character.Experience += amount
  leveled = false
  while character.Experience >= Threshold(character.Level)
        and character.Level < character.MaxLevel:
    character.Level += 1
    GrantPointsFromTable(character.Level)
    leveled = true

  // Always persist absolute Level + Experience (and points when columns exist)
  ExperiencePersistence.SaveProgress(character.Coid, character.Level, character.Experience, ...)

  Send(GiveXPPacket { Amount = amount, LevelHint = levelHint })
  if leveled:
    Send(CharacterLevelPacket { Level, Experience, Currency, points, ... })
```

---

## DB persistence

Experience must survive disconnects and process restarts. AutoCore already has proven patterns for **absolute economy rows on `character`** (credits) and **background queues** (exploration / missions). XP should follow the **credits** pattern: absolute fields on `character`, write-through on every grant, reload on sector login.

### Current state

| Field | DB today | In-memory today | Notes |
|-------|----------|-----------------|--------|
| `Level` | `character.Level` (`byte`, default 1) | `Character.Level` → `DBData.Level` | Column exists; **never updated on level-up** (no GiveXp yet) |
| `Experience` | **missing** | **missing** | Must add column + property |
| Skill / attrib / research pools | **missing** | Not tracked as character fields | Optional follow-on; see below |
| `Credits` | `character.Credits` | `Character.SetCredits` | Reference implementation |

Until `Experience` is stored, any grant is process-local and **desyncs on relog** (client total from last `GiveXP` vs server Level from create).

### Schema (char database)

Mirror `CharContext.EnsureCharacterEconomySchema()` (idempotent `ALTER TABLE ... ADD COLUMN`).

**Recommended migration** (call from `CharContext.EnsureCreated` / new `EnsureCharacterProgressSchema()`):

```sql
ALTER TABLE `character`
  ADD COLUMN `Experience` INT NOT NULL DEFAULT 0;

-- Optional, when level-up point pools are server-authoritative:
ALTER TABLE `character`
  ADD COLUMN `SkillPoints` SMALLINT NOT NULL DEFAULT 0;
ALTER TABLE `character`
  ADD COLUMN `AttributePoints` SMALLINT NOT NULL DEFAULT 0;
ALTER TABLE `character`
  ADD COLUMN `ResearchPoints` SMALLINT NOT NULL DEFAULT 0;
```

| Column | Type | Default | Meaning |
|--------|------|---------|---------|
| `Experience` | `INT` (signed, non-negative in practice) | `0` | **Cumulative** total XP (same meaning as client `+0x730` and `CharacterLevelPacket.Experience`) |
| `Level` | already exists | `1` | Current level; always written **together** with `Experience` on grant/level-up |
| `SkillPoints` etc. | optional `SMALLINT` | `0` | Unspent pools after `tExperienceLevel` grants |

**Do not** store “XP into current level” only — retail and the client use **cumulative** totals against `tExperienceLevel.intExperience` thresholds.

**New character create** (`CharacterSelectionManager` / `CharacterData` defaults):

- `Level = 1`
- `Experience = 0`
- Point pools `0` (or apply level-1 table grants if design requires; retail table L1 is the first threshold, not a free level-up)

### EF model

`src/AutoCore.Database/Char/Models/CharacterData.cs`:

```csharp
public byte Level { get; set; } = 1;
public int Experience { get; set; } = 0;
// optional:
// public short SkillPoints { get; set; }
// public short AttributePoints { get; set; }
// public short ResearchPoints { get; set; }
```

Map via existing EF conventions on table `character` (same as `Credits`).

### Entity API (in-memory)

On `Character` (same style as credits):

```csharp
public int Experience => DBData?.Experience ?? 0;
public void SetExperience(int experience) { DBData.Experience = Math.Max(0, experience); }
public void SetLevel(byte level) { DBData.Level = level; }  // or mutate DBData.Level directly
```

All grants mutate **`DBData` first**, then persist that absolute snapshot. Never “DB += delta” without reading the live row if another path might write concurrently — prefer **absolute overwrite** of `(Level, Experience[, points])` from the in-memory character after the grant.

### Persistence API (recommended)

Follow `CurrencySync` + `InventoryPersistence.LoadCredits` / `SaveCredits`:

| Operation | Behavior |
|-----------|----------|
| `LoadProgress(coid)` | `SELECT Level, Experience[, points]`; return DTO |
| `SaveProgress(coid, level, experience[, points])` | Load row by PK, set columns, `SaveChanges`; throw if character missing |
| `GiveXp` tail | After in-memory apply → `SaveProgress` with **absolute** values |

Sketch:

```csharp
// InventoryPersistence or dedicated CharacterProgressPersistence
public (byte Level, int Experience) LoadProgress(long characterCoid);
public void SaveProgress(long characterCoid, byte level, int experience /*, points... */);
```

**Production path:** short-lived `CharContext` per call (same as `SaveCredits`) — simple, correct, easy to test with a fake store.

**Why not only logout flush?**  
`CharacterWorldStatePersistence` only writes map/pose today and runs on disconnect. XP grants are frequent mid-session; if the process dies before logout, unflushed XP is lost. **Write-through on every successful `GiveXp`** matches credits and avoids that hole.

**Optional later optimization:** latest-wins queue per `characterCoid` (like `ExplorationPersistenceQueue`) if kill spam makes sync MySQL writes hot. Keep absolute snapshot in the queue entry, not deltas (delta queues double-apply on retry). Flush still required on disconnect (`TNLConnection` already flushes mission/exploration-style work).

### Login / sector enter restore

Mirror credits login restore (`CurrencySync.TryCreateLoginRestorePacket` + `TNLConnection.Sector`):

1. `Character.LoadFromDB` already loads `CharacterData` (includes `Level`; will include `Experience` once mapped).
2. Optionally **reload** progress from DB at sector enter (guards stale memory if multiple writers): `LoadProgress(coid)` → `SetLevel` / `SetExperience`.
3. After create packets (credits stay 0 on `CreateCharacterExtended` for crash safety):
   - Send **`CharacterLevel` (`0x2017`)** with:
     - `Level` = DB level  
     - `Experience` = DB cumulative XP  
     - `Currency` = credits (existing)  
     - mana / points when available  
4. Do **not** send a fake `GiveXP` for the full total on login — that would re-apply XP on the client. Absolute `CharacterLevel` is the restore path (same as money).

Extend `CharacterLevelManager.BuildPacket` (or a sibling progress helper) so login and post-level-up snapshots share one builder:

```csharp
new CharacterLevelPacket {
  CharacterId = character.ObjectId,
  Level = character.Level,
  Experience = character.Experience,
  Currency = character.Credits,
  // SkillPoints / AttributePoints / ResearchPoints when persisted
  CurrentMana = ...,
  MaxMana = ...,
}
```

Today `BuildPacket` only fills level + mana — **must** include `Experience` (and currency) for a correct restore.

### Grant path order (durability)

Recommended order inside `GiveXp`:

1. Validate character has positive `ObjectId.Coid` and attached `DBData`.
2. Apply amount + level-ups **in memory** on `DBData`.
3. **`SaveProgress`** (absolute Level + Experience [+ points]). If save throws, log and fail the grant (do not send packets for unpersisted state — same “fail loud” idea as `SaveCredits`).
4. Send `GiveXP` (delta for floater).
5. If leveled (or always, for safety): send `CharacterLevel` absolute snapshot.

If you must keep the client responsive under DB outage, document the trade-off explicitly; default for AutoCore economy is **persist then notify**.

### Admin / debug commands

| Command | Should become |
|---------|----------------|
| `/xp <amount>` | Either **grant** (`GiveXp`) or **set absolute** Experience + recompute Level from thresholds; always `SaveProgress` + packets |
| `/level <n>` | Set level + clamp Experience into that level’s legal range (or set to threshold−1 / threshold); persist |

Do not leave `/xp` as “send `CharacterLevel` only” — that lies to the client without DB.

### Mission / area / kill interaction with other persistence

| Source | XP row | Related persistence |
|--------|--------|---------------------|
| Kill | `SaveProgress` on grant | None else |
| Mission complete | `SaveProgress` on grant | `MissionPersistence` completion row (already); keep mission complete **idempotent** so XP is not re-granted on replay |
| Area first visit | `SaveProgress` on grant | `character_exploration` bits (already); only grant when bit **newly** set |
| Reaction AddXP | `SaveProgress` on grant | Map reaction cooldowns as applicable |

**Idempotency:** DB absolute XP is not enough if the **event** is replayed (e.g. mission complete packet twice). Event-level guards (mission completed set, exploration bit, death credit id) remain mandatory; persistence only stores the resulting totals.

### Logout / crash

| Event | Behavior |
|-------|----------|
| Normal disconnect | Progress already write-through; world pose via `CharacterWorldStatePersistence` (unchanged) |
| Process crash mid-session | Last successful `SaveProgress` is truth; at most one grant lost if crash between memory apply and save (prefer save-before-packet to limit client/server split) |
| Relog | Load `Level`+`Experience` → `CharacterLevel` restore |

### World static data (not char DB)

Static XP tables live in **world** data (`wad.xml` / world MySQL), not the char DB:

| Table | Load into |
|-------|-----------|
| `tExperienceLevel` | Already: `WorldDBLoader.ExperienceLevels` |
| `tCreatureExperienceLevel` | Add loader (dict by creature level) |
| `tQuestXPLookup` | Add loader (dict by index) |
| `tContinentExploredAreas` | Already: `ContinentAreas` |

No per-character rows for those.

### Tests (persistence)

When implementing (TDD):

1. `SaveProgress` then `LoadProgress` round-trip Level + Experience.
2. `GiveXp` with fake store: memory and store both updated; second load matches.
3. Login builder packet includes Experience from DB-backed character.
4. Missing character coid → no silent success (throw or structured fail).
5. Schema ensure is idempotent (`EnsureCharacterProgressSchema` twice).

### Persistence checklist (implementation order)

1. `EnsureCharacterProgressSchema` + `CharacterData.Experience` (+ optional point columns).  
2. `Character` get/set + load path from existing `LoadFromDB`.  
3. `LoadProgress` / `SaveProgress` (credits-style).  
4. `GiveXp` → save then packets.  
5. Sector login `CharacterLevel` restore with Experience + Level (+ Currency).  
6. Fix `/xp` / `/level` to use the same API.  
7. Hook kill / mission / area / reaction sources.

---

## Worked examples

### 1. Same-level kill

- `P = V = 1`, `XPPercent = 1`, share = 1, `GLOBAL_KILL_SCALAR = 1`, spree = 0  
- `CreatureXP[1] = 39`  
- Grey adjust 0  
- **XP ≈ 39**

### 2. Grey / worthless kill

- `P = 12`, `V = 1` → `diff = 11 ≥ 10`  
- **XP = 0**

### 3. Mission complete

- `TargetLevel = 5`, span = 3200  
- `XPIndex = 5` → 0.10, scalers 1.0  
- **XP = 320**

### 4. Area first visit (hypothesis)

- `XPLevel = 1` → `CreatureXP[1] = 39`  
- **XP = 39** (once)

### 5. Level-up boundary

- Start: level 1, total XP 990  
- Grant 20 → total 1010 ≥ 1000  
- Level becomes 2; grant L2 table skill/attrib/research  
- Remaining XP stays cumulative (1010), not reset to 0

### 6. Outpost pulse (illustration)

- Level 60, span = `Exp(60) - Exp(59)` (from full table)  
- True outpost pulse 0: `fPercentLevelXP ≈ 0.0006`  
- **XP ≈ round(span * 0.0006 * outpostScalar)**

---

## Ghidra anchors

| Address | Name / role |
|---------|-------------|
| `0x0080AE70` | `Client_AwardKillExperience` — S2C `GiveXP` |
| `0x00533C30` | `CVOGReaction_AddExperience` — apply + level loops |
| `0x004D80B0` | `CVOGCombat_CalculateAndAwardKillXP` |
| `0x004DA630` | Death/loot → kill XP / convoy |
| `0x004C9800` | Level-diff base (grey/hard) |
| `0x004C97B0` | Creature XP table lookup |
| `0x0052C860` | Player XP threshold lookup |
| `0x00532D30` | Level-up (+ points) |
| `0x005330E0` | De-level |
| `0x00533F90` | `CVOGReaction_CompleteObjective` |
| `0x0059DDE0` | Mission XP calculator |
| `0x0059DF20` | Mission credits calculator |
| `0x0052DEC0` | XP required for relative level |
| `0x00607830` | Outpost pulse XP amount |
| `0x006075B0` | Outpost pulse percent table pick |
| `0x005326B0` | `CVOGCharacter_SetAreaExploredBit` (no XP) |
| `0x005D6C60` | `Client_LocalDiscoveryTick` (no XP) |

---

## Related AutoCore files

| Path | Relevance |
|------|-----------|
| `src/AutoCore.Game/Packets/Sector/GiveXPPacket.cs` | `0x205F` wire |
| `src/AutoCore.Game/Packets/Sector/CharacterLevelPacket.cs` | `0x2017` snapshot |
| `src/AutoCore.Game/Combat/CombatTextCommand.cs` | GiveXP floater probe |
| `src/AutoCore.Game/Mission/MissionObjective.cs` | XP fields |
| `src/AutoCore.Game/Mission/Mission.cs` | `TargetLevel` |
| `src/AutoCore.Game/CloneBases/Specifics/CreatureSpecific.cs` | `XPPercent` |
| `src/AutoCore.Database/World/Models/ExperienceLevel.cs` | Thresholds |
| `src/AutoCore.Database/World/Models/ContinentArea.cs` | Area `XPLevel` |
| `src/AutoCore.Database/Char/Models/CharacterData.cs` | Level only — **add Experience** (+ optional point pools) |
| `src/AutoCore.Database/Char/CharContext.cs` | `EnsureCharacterEconomySchema` pattern for progress columns |
| `src/AutoCore.Game/Inventory/CurrencySync.cs` | Credits persist + login restore pattern to mirror |
| `src/AutoCore.Game/Inventory/InventoryPersistence.cs` | `LoadCredits` / `SaveCredits` pattern for progress |
| `src/AutoCore.Game/Managers/CharacterLevelManager.cs` | Build `CharacterLevel` — must include Experience |
| `src/AutoCore.Game/Managers/Asset/WadXmlWorldDataLoader.cs` | Partial table load |
| `src/AutoCore.Game/Managers/NpcInteractHandler.cs` | Mission reward gaps |
| `src/AutoCore.Game/Managers/ExplorationManager.cs` | First-visit hook site |
| `src/AutoCore.Game/Entities/Reaction.cs` | `AddXP = 28` |
| `Documentation/MAP_REVEAL.md` | Exploration; notes future GiveXP |
| `docs/missionState.md` | Mission reward idempotency notes |

---

## Live validation checklist

Before calling kill/area formulas “final”:

1. **Kill scalar:** server grant with `GLOBAL_KILL_SCALAR=1.0` on a known creature; confirm floater amount vs `CreatureXP[V] * XPPercent`.
2. **Grey band:** player 10+ levels above mob → 0 XP.
3. **Mission:** complete a mission with known `TargetLevel` / `XPIndex`; compare to `span * frac * scalers`.
4. **Area:** first enter an area with `intXPLevel > 0`; confirm amount (creature-table hypothesis).
5. **Level-up:** grant across a threshold; confirm skill/attrib/research from `tExperienceLevel` and `CharacterLevel` snapshot.
6. **Persistence:** grant XP → kill process (or restart sector) → relog → `character.Experience` / `Level` in MySQL match pre-crash totals; client bar matches via `CharacterLevel` restore (not a full-total `GiveXP`).
7. **Reaction AddXP:** trigger a known type-28 reaction; confirm `GenericVar1` amount.
8. **Idempotent mission:** complete same mission twice → XP granted once; DB total not double-counted.

---

## Summary

| Source | Formula gist |
|--------|----------------|
| **Apply** | `total += amount * personalGain`; level up via cumulative `tExperienceLevel` |
| **Kill** | `CreatureXP` ± level-diff grey/hard × `XPPercent` × share × scalar; optional spree +5%/stack |
| **Mission** | `span(TargetLevel) * QuestFrac[XPIndex] * XPScaler * XPBalanceScaler` on **final** objective |
| **Area** | Once per bit: prefer `CreatureXP[ContinentArea.XPLevel]` (**validate**) |
| **Reaction** | Absolute grant (`GenericVar1`) |
| **Outpost** | `span(playerLevel) * fPercentLevelXP * scalar` on pulse |

AutoCore already has the **notify** packet (`GiveXP`) and some **tables** (player levels, area XPLevel, creature `XPPercent`). It does **not** yet compute or persist awards. Implementing a single `GiveXp` service with the formulas above, plus **write-through absolute `character.Experience`/`Level` persistence** (credits-style), is the path to retail-correct XP.

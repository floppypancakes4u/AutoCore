# OBJECT_LAYOUTS

Offset tables for recovered objects. Unresolved padding kept explicit.

## CVOGCharacter — progression fields (client)

Evidence: `CVOGReaction_AddExperience`, `CVOGCharacter_ApplyCharacterLevelPacket`, `CVOGCharacter_LevelUp` (UF-005 pass).

| Offset | Size | Proposed Field | Evidence | Confidence | Conflicts |
|--------|------|----------------|----------|------------|-----------|
| `+0x12c` | 2 | CurrentMana | ApplyCharacterLevelPacket | high | — |
| `+0x12e` | 2 | MaxMana | ApplyCharacterLevelPacket | high | — |
| `+0x250` | 4 | Vehicle* | floater / HP gates | high | — |
| `+0x580` | 2 | ResearchPoints | LevelUp / Apply | high | — |
| `+0x6b4` | 4 | SpecialMode (skip max XP cap if >0) | AddExperience | high | — |
| `+0x6c8` | 4 | Level | Apply absolute; LevelUp++ | high | soft-cap also uses vfunc+0x27c |
| `+0x6cc` | 2 | SkillPoints pool | LevelUp / Apply | high | — |
| `+0x6ce` | 2 | AttributePoints pool | LevelUp / Apply | high | — |
| `+0x720` | 8 | Currency (int64) | Apply absolute; AddCredits delta | high | — |
| `+0x730` | 4 | TotalExperience | Apply absolute; AddExperience += | high | — |
| `+0x734` | 4 | LastKillTick / LevelHint timestamp | KillPath; GiveXP LevelHint | high | dual use |
| `+0x738` | 1 | SpreeOrLevelHint | KillPath; GiveXP LevelHint | high | dual use |
| `+0xc50` | 4 | MaxLevel | AddExperience soft cap | high | — |
| `+0xc54` | 4 | PersonalXpGain (float) | AddExperience scale | high | — |

## Placeholder

Further layouts under `types/<name>.md` as added.

| Offset | Size | Proposed Field | Evidence | Confidence | Conflicts |
|--------|------|----------------|----------|------------|-----------|

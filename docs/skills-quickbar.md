# Skills and quick bar

`CreateCharacterExtended` restores 100 item COIDs at packet offset `0x410`, 100 skill IDs at
`0x730`, then a variable learned-skill tail.  Each learned skill is eight bytes: `int32 skillId`,
`byte rank`, and three reserved zero bytes.  The client parser is
`CVOGCharacter_ApplyCreateFromPacket` / `Client_RecvCreateCharacter` at `0x008146b0` (writes
char `+0x930` items via `FUN_00520890`, char `+0x74c` skills via `FUN_005208c0`).

The server persists purchased ranks in `character_learned_skill` and non-empty quick-bar slots in
`character_quickbar`.  `SkillIncrement` and `QuickBarUpdate` are client requests; validation is
server authoritative.  A successful rank purchase is synchronized through `CharacterLevel` so
the remaining unspent-point pool is refreshed.  Login remains the authoritative full restore.

Skill validation uses the WAD skill record's minimum character level, three prerequisite IDs, and
maximum rank.  Invalid requests do not mutate memory or persistence.

## QuickBarUpdate (C2S `0x2062`)

Client sends this when the player assigns, reassigns, or clears a quick-bar slot (drag/drop, and
auto-map after first skill train).  UI is optimistic; there is **no** server→client ack.  Persist
only; the next login restores via `CreateCharacterExtended`.

| Client send site | Address |
|---|---|
| Primary UI send | `FUN_00826720` |
| Alternate send (slot in `CL`) | `FUN_007fc100` |
| Auto-map after train | `FUN_00897170` @ `0x00897250` |
| Transport | `Client_SendSectorPacket` @ `0x00807460`, size `0x10` |

### Wire layout (16 bytes including opcode; 12-byte body after opcode)

| Body offset | Type | Field |
|---|---|---|
| +0 | `byte` | Slot index 0–99 (`page * 10 + index`) |
| +1 | `byte` | `IsItem`: `0` = skill, non-zero = item |
| +2 | `uint16` | Padding (client often leaves uninitialized — ignore) |
| +4 | `int64` | Value: skill id (sign-extended) or item COID |

Example live capture (`bodyLength=12`):

```
0000D6343708000000000000
```

→ slot `0`, skill (`IsItem=0`), pad `D634` ignored, value `0x837` = skill **2103**.

### Server mapping

| Condition | `ItemCoid` | `SkillId` |
|---|---|---|
| Skill place (`IsItem=0`, value ≥ 0) | `-1` | `(int)value` |
| Skill clear (`IsItem=0`, value &lt; 0) | `-1` | `0` |
| Item place (`IsItem≠0`, value not empty) | `value` | `0` |
| Clear slot (`IsItem≠0`, value `-1`) | `-1` | `0` |

A slot is exclusive: skill assignment clears the item COID; item assignment clears the skill id.
Empty skill = `0`; empty item = `-1` (matches create-packet defaults).

## Regression tests and coverage

| Suite | Focus |
|---|---|
| `QuickBarUpdatePacketTests` | Wire parse (live capture, item/skill/clear, short body) |
| `QuickBarUpdateHandlerRegressionTests` | `HandleQuickBarUpdatePacket` via reflection (apply/reject/null char) |
| `SkillsHpPowerRegressionTests` | `CharacterSkillService.TryUpdateQuickBar` validation matrix |

Coverage gate (90%+ on touched production code):

```powershell
dotnet test src/AutoCore.Game.Tests/AutoCore.Game.Tests.csproj `
  --filter "FullyQualifiedName~QuickBar|FullyQualifiedName~SkillsHpPower" `
  --collect:"XPlat Code Coverage" `
  --results-directory TestResults/quickbar-cov `
  --settings src/AutoCore.Game.Tests/quickbar.coverlet.runsettings

powershell -File scripts/measure-quickbar-coverage.ps1
```

Scoped modules: `QuickBarUpdatePacket`, `CharacterSkillService`, and handler lines in
`TNLConnection.Sector.cs` (not the entire Sector partial). The real DB branch of
`CharacterSkillService.Persist` is intentionally skipped in unit tests via `PersistForTests`.

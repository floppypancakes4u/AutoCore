# Topic Extraction: MakeNotInvincible + SetFactionFromVar

## Executive Summary

Two map reaction types that were logging `INCOMPLETE[Reaction.Unhandled]` on live maps (e.g. condenser / generator mission objects) now have **generic server handlers**:

| Type | Enum | Value | Server behavior |
|------|------|------:|-----------------|
| MakeInvincible | `MakeInvincible` | 6 | `target.SetInvincible(true)` for each target |
| MakeNotInvincible | `MakeNotInvincbile` (typo preserved) | 7 | `target.SetInvincible(false)` |
| SetFactionFromVar | `SetFactionFromVar` | 22 | `target.Faction = ROUND(logicVar[GenericVar1])` |

Handlers are **mission-agnostic**: they only use `Template.Objects`, `Template.ActOnActivator`, and `Template.GenericVar1` (variable id). No hardcoded mission or COID lists.

Client still applies visuals via **0x206C** `GroupReactionCall`. Server must flip invincible/faction for **combat and AI authority** (e.g. `TakeDamage` / `IsInvincible`, ghost faction packing).

---

## Client RE (Ghidra `CVOGReaction_Dispatch` @ `0x0057C500`)

| Case | Behavior |
|------|----------|
| **6** | For each Objects entry: resolve object → vtable `+0x1BC(1)` set invincible |
| **7** | Same with `+0x1BC(0)` clear invincible |
| **0x16 (22)** | Build targets; `CVOGMap_LookupVariable(GenericVar1)`; `faction = (int)ROUND(value)`; apply via set-faction helpers |

Nested reactions still fire on client; AutoCore already chains `Template.Reactions` through `SectorMap.TriggerReactionsInternal`.

---

## Target resolution (generic)

Shared helper `EnumerateReactionTargets`:

1. If `ActOnActivator` → yield activator only.
2. Else for each COID in `Template.Objects` → `map.GetObjectByCoid`; skip missing (client-only props) with debug log.

Same pattern as `HandleDelete`.

---

## Live log examples (now handled)

```
type=MakeNotInvincbile (7) objs=[9301]
type=SetFactionFromVar (22) g1=217 objs=[9301]
```

- Object **9301** becomes damageable server-side.
- Faction set from **per-character map logic variable 217** (whatever mission/map logic wrote into that var earlier).

---

## Files

| Path | Change |
|------|--------|
| `src/AutoCore.Game/Entities/Reaction.cs` | Cases + `HandleSetInvincible`, `HandleSetFactionFromVar`, `EnumerateReactionTargets` |
| `src/AutoCore.Game/Entities/ClonedObjectBase.cs` | `SetInvincible(bool)` |
| `src/AutoCore.Game.Tests/Entities/ReactionInvincibleAndFactionTests.cs` | 7 unit tests |

---

## Related fix: map-prop combat (GraphicsObject HP)

Live follow-up: after MakeNotInvincible, **Damage** packets were sent but object 9301 never died / HP bar did not move.

### Root causes

1. **No mutable HP** on map `GraphicsObject` (base `TakeDamage` returned 0; `GetCurrentHP` always max).
2. **No combat ghost** — map props never called `CreateGhost()`, so `HealthMask` HP never reached the client. Damage floaters alone do not update prop HP bars for local map objects the way they do for ghosted vehicles/NPCs.
3. **Must restart Launcher/Sector** after rebuild — in-process DLLs do not hot-reload.

### Fix

- Mutable HP on `GraphicsObject`; death → leave map + `DestroyObject`.
- **Lazy combat ghosts only** — `SetInvincible(false)` (MakeNotInvincible) or first `TakeDamage` creates/scopes a ghost via `ObjectLocalScopeAlways`.
- **Do not** ghost all map props at load (regression: filled client ghost slots → **all NPCs disappeared**).
- Combat debug: `TakeDamage: GraphicsObject coid=… before→after/max dealt=…`.

See `GraphicsObjectDamageTests`. **Restart `AutoCore.Launcher` after build.**

## Not implemented (related gaps)

- `SetFaction` (20) / `ResetFaction` (21) / team-faction variants — still unhandled if maps use them without vars.
- Ghost dirty bits for mid-session faction change (create path packs faction; live clients may rely on 0x206C until ghost status is extended).
- Client-only objects never present on server remain client-visual-only.
- `SetFactionFromVar` log `Faction=-1 (var[217])` means logic var 217 was empty/unset or intentionally -1 — separate from damage; ensure map VariableSet runs before the faction reaction if human faction is required for AI.

---

## Tests

```text
dotnet test --filter FullyQualifiedName~ReactionInvincibleAndFactionTests
```

Covers: clear/set invincible, ActOnActivator, faction from var + ROUND, missing object, no character.

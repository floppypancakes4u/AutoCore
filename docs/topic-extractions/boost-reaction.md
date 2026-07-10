# Topic Extraction: Boost Reaction

## Executive Summary

`ReactionType.Boost` (enum value **24**) is a **pure-client** map reaction. The server returns success from `TriggerIfPossible` so `GroupReactionCall` (**0x206C**) is still delivered; the client applies a local physics impulse from clonebase / reaction COID. No server physics authority is applied.

This stops `INCOMPLETE[Reaction.Unhandled]` spam for live map boosts (e.g. `1_Boost` coid 37661).

| Field | Role (client) |
|-------|----------------|
| `GenericVar1` | Map logic variable id → boost magnitude |
| `GenericVar3` | `-1` = **add** impulse to current velocity; otherwise **replace** velocity with scaled direction |
| `Objects` / `ActOnActivator` | Target list builder (often ActOnActivator / activator vehicle; Objects may be empty) |

---

## Client RE (Ghidra `CVOGReaction_Dispatch` @ `0x0057C500`)

**Case `0x18` (24):**

1. Build targets (`vtable +0x2d0`).
2. For each target: `CVOGMap_LookupVariable(GenericVar1)`.
3. If physics-capable: read velocity, normalize if speed above threshold, scale by variable value.
4. If `GenericVar3 == -1`, add to current velocity; else replace.
5. `CVOGPhysics_ApplyImpulseVector` + physics refresh; fire nested reactions.

---

## Server behavior

```csharp
case ReactionType.Boost:
    // Pure-client via 0x206C — no Incomplete log.
    return true;
```

AutoCore does **not** simulate vehicle impulse on the server. Authority for combat/AI is unrelated; boost pads are feel/physics on the client.

---

## Files

| Path | Change |
|------|--------|
| `src/AutoCore.Game/Entities/Reaction.cs` | `case ReactionType.Boost` pure-client |
| `src/AutoCore.Game.Tests/Entities/ReactionBoostTests.cs` | Unhandled-free success tests |
| `docs/topic-extractions/boost-reaction.md` | This note |

---

## Tests

```text
dotnet test --filter FullyQualifiedName~ReactionBoostTests
```

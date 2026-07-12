# Skill Cast

`ReactionType.SkillCast` (12) is now server-dispatched through `SkillService`.
For a map reaction, `GenericVar1` is the WAD skill ID and `GenericVar3` is its rank. The
currently authoritative effect family is direct repair (`SkillElement.ElementType == 10`): it
restores the activating object's HP, dirties its health ghost state, and sends
`SkillStatusEffect` (0x2031) to the owning player. Missing or unsupported definitions return
false and make no state change.

Retail skill 857 is `INC Repair station heal`. Its Heal element uses equation type 1 with a
base value of `0.15`; equation type 1 is evaluated against the target's maximum pool, so each
authored cast restores 15% maximum HP. Treating `0.15` as an absolute amount rounds to zero and
silently disables the pad.

Collision triggers containing a `SkillCast` reaction pulse that reaction once per second while
the collider remains inside. Pulse deadlines are keyed by vehicle, trigger, and reaction, allowing
multiple vehicles to use the same pad independently. A full-health vehicle emits no skill traffic;
the cadence remains armed so repair resumes within one second if it takes damage while still on the
pad. Exiting, disconnecting, clearing, or resetting the trigger removes that vehicle's pulse state.

Client intent packet layouts established from the existing Ghidra analysis:

| Opcode | Packet | Body after opcode |
|---|---|---|
| 0x2030 | `RequestCastSkill` | pad4, target TFID, skill ID i32, target position Vector3 |
| 0x2032 | `CancelSkill` | pad4, target TFID, skill ID i32 |

Packet readers exist but are deliberately not routed yet: player learned-skill persistence,
ownership/rank validation, cooldown/cast state, and `SkillIncrement`/`QuickBarUpdate` packet
layouts must be implemented first. Status effects, damage, chains, summoning, passives, and
timed effects remain unsupported and therefore cannot be applied by this first vertical slice.

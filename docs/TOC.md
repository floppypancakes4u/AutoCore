# AutoCore documentation

## Getting started

- [Quick Start](../QUICKSTART.md) — run the stack in minutes
- [Setup](../SETUP.md) — detailed server configuration
- [Client setup](../CLIENT_SETUP.md) — point the retail client at your server
- [Discord bot (optional)](discord.md) — presence + DM account create / password change

## Observability

- [Logging / observability audit (LG register)](logging-observability-audit.md) — design D1–D8, limitations, playtest recipes
- [Structured log event catalog](logging-event-catalog.md) — every GameLog event name + error codes
- [Exception-safety audit (SS-nn)](exception-safety-audit.md) — crash boundaries and accepted risk
- [ID collisions (COID / TFID / simple_object)](id-collisions.md) — SS-31 character-select AV, shared COID space, guards and hardening
- [Logging overhaul plan / handoff](migrateGoals.md) — phased plan (phases 0–6 complete)

## Core systems

- [Networking and packet layout](networking.md) — how to build client-compatible game packets (login, sector, inventory)
- [Experience (XP)](XP.md) — kill / mission / area formulas, GiveXP packet, server gaps
- [Mission handler](missionHandler.md) — canonical lifecycle, requirement types, how the handler must process each type
- [Mission work notes](missionWork.md) — persistence handoff, New Day / Rogers / Track This live gaps
- [Mission state](missionState.md) — client RE, packet layouts, quest wire format
- [Mission testing map](testing/mission-testing-map.md) — test inventory and component cards
- [Mission regression catalog](testing/mission-regression-catalog.md) — REG-001…006
- [NPC AI / spawn faction](NPC.md) — hostility, FactionDirty / OriginalFaction, tutorial combat NPCs

## Testing

- [Full test suite report (2026-08-14)](full-test-suite-report-2026-08-14.md) — `fix-packets` / `3893c979b`: 25 failures, 7 skipped (rerun after transfer/opcode commit; same 25 red)

## Client reverse engineering

- [Physics exploration / vehicle recreation guide](physicsExploration.md) — Havok vehicle pipeline, formulas, tuning offsets, collisions, and networking
- [Mission journal / ConvoyMissions semantic fix (pass 25)](pdbmissionjournal25.md) — retail live mission deltas and removal of solo `0x8010` misuse
- [Opcode certification closure](pdbopcodeclosure.md) — dispositions for the leftover production PARTIAL opcodes; 94/94 have a precise verdict
- [Exhaustive opcode audit](pdbopcodeexhaustiveaudit.md) — full GameOpcode ledger vs the loaded client
- [Client debug / dev surface (command levels, S:-server commands, unwired code)](CLIENT_DEV_SURFACE_RE.md) — full command matrix by access level, server-executed commands, dead Havok-VDB runtime

## Tools

- [Inventory catalog](../tools/inventory-catalog/README.md) — exported item definitions
- [Mission viewer](../tools/mission-viewer/README.md) — offline mission browser (GLM + WAD export)

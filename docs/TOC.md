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

## Client reverse engineering

- [Client debug / dev surface (command levels, S:-server commands, unwired code)](CLIENT_DEV_SURFACE_RE.md) — full command matrix by access level, server-executed commands, dead Havok-VDB runtime

## Tools

- [Inventory catalog](../tools/inventory-catalog/README.md) — exported item definitions
- [Mission viewer](../tools/mission-viewer/README.md) — offline mission browser (GLM + WAD export)

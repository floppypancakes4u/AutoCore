# AutoCore documentation

## Getting started

- [Quick Start](../QUICKSTART.md) — run the stack in minutes
- [Setup](../SETUP.md) — detailed server configuration
- [Client setup](../CLIENT_SETUP.md) — point the retail client at your server

## Core systems

- [Networking and packet layout](networking.md) — how to build client-compatible game packets (login, sector, inventory)
- [Experience (XP)](XP.md) — kill / mission / area formulas, GiveXP packet, server gaps
- [Mission handler](missionHandler.md) — canonical lifecycle, requirement types, how the handler must process each type
- [Mission work notes](missionWork.md) — persistence handoff, New Day / Rogers / Track This live gaps
- [Mission state](missionState.md) — client RE, packet layouts, quest wire format
- [Mission testing map](testing/mission-testing-map.md) — test inventory and component cards
- [Mission regression catalog](testing/mission-regression-catalog.md) — REG-001…006
- [NPC AI / spawn faction](NPC.md) — hostility, FactionDirty / OriginalFaction, tutorial combat NPCs

## Tools

- [Inventory catalog](../tools/inventory-catalog/README.md) — exported item definitions

# Scripts

Index of reusable tools under [`scripts/`](scripts/). For placement rules (reusable vs one-off), see [AGENTS.md](AGENTS.md).

Game-data tools default to `C:\Program Files (x86)\NetDevil\Auto Assault`; override with env var `AA_INSTALL`.

---

## Reverse engineering / game data (Python)

| Script | What it does |
|--------|----------------|
| [`aa_paths.py`](scripts/aa_paths.py) | Shared path helpers for the game install, `clonebase.wad`, `missions.glm`, maps GLMs, and the inventory catalog. Other Python RE scripts import this module. |
| [`list_glm_contents.py`](scripts/list_glm_contents.py) | Lists files inside a `.glm` archive (name, offset, size) with optional name filter. Can extract one matching member (e.g. a `.fam` map) to disk. |
| [`parse_fam_map.py`](scripts/parse_fam_map.py) | Walks a `.fam` MapData file and dumps objects and spawn points (active/inactive, creature CBIDs). Can simulate naive COID allocation to identify a runtime spawn index. |
| [`parse_fam_triggers.py`](scripts/parse_fam_triggers.py) | Scans a `.fam` for Trigger templates (CBID 78) and prints name, flags, reaction COIDs, and conditions. Optional filter by name and heuristic map-variable dump. |
| [`parse_fam_reactions.py`](scripts/parse_fam_reactions.py) | Scans a `.fam` for Reaction templates (CBID 86) and dumps type, object lists, nested reactions, and DoForAll/condition trailing fields. Filter by COID or name substring. |
| [`dump_clonebase.py`](scripts/dump_clonebase.py) | Dumps CloneBaseSpecific / SimpleObject fields from `clonebase.wad` by unique name or CBID (flags, targetable, usable-related bits, HP, etc.). |
| [`dump_wad_mission.py`](scripts/dump_wad_mission.py) | Locates binary mission records in `clonebase.wad` and prints gates, prereq mission IDs, RequirementsOred/Negative, continent, and related fields. Can dump objective WorldPosition/ContinentObject by name. |
| [`extract_mission_xml.py`](scripts/extract_mission_xml.py) | Extracts `<Mission>` XML blocks from `missions.glm` by ID or name substring. Prints a filtered summary or full XML (optional write to file). |
| [`mission_useitem_stats.py`](scripts/mission_useitem_stats.py) | Aggregates pattern stats for all `useitem` requirements in `missions.glm` (PrimaryCOID/CBID, explode/destroy/in-world, etc.). Can sample missions with PrimaryExplode set. |
| [`scan_map_ids.py`](scripts/scan_map_ids.py) | Counts little-endian i32 hits for CBIDs/COIDs across maps GLMs and optional ASCII/UTF-16 string needles. Use before extracting a full `.fam`. |
| [`lookup_inventory_cbid.py`](scripts/lookup_inventory_cbid.py) | Looks up CBIDs in `tools/inventory-catalog/inventory-items.json` (displayName, uniqueName, className). Faster than opening `clonebase.wad` when the catalog has the row. |

---

## Setup & ops (PowerShell)

| Script | What it does |
|--------|----------------|
| [`init-databases.ps1`](scripts/init-databases.ps1) | Creates the MySQL databases required by AutoCore (auth/world/character, etc.) using local MySQL credentials. |
| [`recreate-char-db.ps1`](scripts/recreate-char-db.ps1) | Drops and recreates the character database so schema can be reset cleanly during development. |
| [`setup-client.ps1`](scripts/setup-client.ps1) | Adds or removes a Windows hosts-file entry so the retail Auto Assault client points at a local auth server (run as Administrator). |
| [`tail-mission-log.ps1`](scripts/tail-mission-log.ps1) | Tails `server-live.log` and prints only mission-related diagnostic lines (MISSION-DIAG, AutoPatrol, grant/fail, pad hits, etc.). |

---

## Coverage gates (PowerShell)

Each script reads a Cobertura coverage file (default: newest under `TestResults/`), measures line coverage on a scoped set of types/files, and exits non-zero if below the configured minimum rate.

| Script | What it does |
|--------|----------------|
| [`measure-combat-coverage.ps1`](scripts/measure-combat-coverage.ps1) | Scoped coverage gate for combat visualization and related combat surface (not currency). Default minimum 90%. |
| [`measure-currency-coverage.ps1`](scripts/measure-currency-coverage.ps1) | Scoped coverage gate for currency economy and client sync (e.g. GiveCredits / CharacterLevel). Default minimum 90%. |
| [`measure-item-stacks-coverage.ps1`](scripts/measure-item-stacks-coverage.ps1) | Scoped coverage gate for inventory stack/command modules and related inventory packets. Default minimum 80%. |
| [`measure-mission-coverage.ps1`](scripts/measure-mission-coverage.ps1) | Scoped coverage gate for runtime mission logic, requirement models, and mission packets (excludes heavy WAD Read paths). Default minimum 90%. |
| [`measure-mission-combat-coverage.ps1`](scripts/measure-mission-combat-coverage.ps1) | Scoped coverage gate for map-prop combat, kill progress, and invincible/faction mission combat modules. Default minimum 90%. |
| [`measure-mission-phase-coverage.ps1`](scripts/measure-mission-phase-coverage.ps1) | Scoped coverage gate for focused mission-phase feature modules (hard gate); reports large orchestration files separately. Default minimum 90%. |
| [`measure-npc-curmax-coverage.ps1`](scripts/measure-npc-curmax-coverage.ps1) | Scoped coverage gate for Cur/Max NPC driver-attach modules (see `docs` NPC notes). Default minimum 95%. |
| [`measure-player-pose-coverage.ps1`](scripts/measure-player-pose-coverage.ps1) | Line-range coverage gate for remote player pose smoothness (`Vehicle` network pose + sector pose tick). Accepts one or more coverage files. |
| [`measure-quickbar-coverage.ps1`](scripts/measure-quickbar-coverage.ps1) | Scoped coverage gate for QuickBarUpdate persistence (packet + service + sector handler slice). |
| [`measure-scoped-coverage.ps1`](scripts/measure-scoped-coverage.ps1) | Scoped coverage gate for inventory modules, related sector inventory/item-drop packets, and Vehicle inventory-adjacent surface. Default minimum 90%. |
| [`measure-skills-hp-power-coverage.ps1`](scripts/measure-skills-hp-power-coverage.ps1) | Scoped coverage gate for skills, HP, and power modules. Default minimum 90%. |
| [`measure-town-pose-coverage.ps1`](scripts/measure-town-pose-coverage.ps1) | Scoped coverage gate for town on-foot logout/resume pose capture and disconnect teardown races. Default minimum 95%. |
| [`measure-world-state-coverage.ps1`](scripts/measure-world-state-coverage.ps1) | Method-focused coverage gate for character/vehicle world-state persistence surface. Default minimum 90%. |
| [`measure-xp-coverage.ps1`](scripts/measure-xp-coverage.ps1) | Scoped coverage gate for XP economy (ExperienceService, kill awards, GiveXP / level packets). Default minimum 90%. |

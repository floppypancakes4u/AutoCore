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
| [`export_missions_json.py`](scripts/export_missions_json.py) | Exports all missions from `missions.glm` + `clonebase.wad` into `tools/mission-viewer/missions.json` and embeds them in a standalone HTML mission viewer. |
| [`mission_useitem_stats.py`](scripts/mission_useitem_stats.py) | Aggregates pattern stats for all `useitem` requirements in `missions.glm` (PrimaryCOID/CBID, explode/destroy/in-world, etc.). Can sample missions with PrimaryExplode set. |
| [`scan_map_ids.py`](scripts/scan_map_ids.py) | Counts little-endian i32 hits for CBIDs/COIDs across maps GLMs and optional ASCII/UTF-16 string needles. Use before extracting a full `.fam`. |
| [`lookup_inventory_cbid.py`](scripts/lookup_inventory_cbid.py) | Looks up CBIDs in `tools/inventory-catalog/inventory-items.json` (displayName, uniqueName, className). Faster than opening `clonebase.wad` when the catalog has the row. |
| [`add_mission_to_registry.py`](scripts/add_mission_to_registry.py) | Upserts a mission into `tools/mission-live/registry/missions.yaml` from `missions.glm` + `clonebase.wad` (`--id` / `--title` / `--name`). Infers policy/tags from requirement types. |
| [`sync_talk_patrol_deliver_registry.py`](scripts/sync_talk_patrol_deliver_registry.py) | Filters `missions.json` for talk → optional patrol → speak-only deliver missions and writes them into the mission-live registry (`--replace` / `--upsert` / `--dry-run`). |
| [`../tools/mission-live`](tools/mission-live/README.md) | Live mission harness (`python -m mission_live`): DevTool pipe + Dev API oracle, retail-catalog categories (`travel`/`cargo`/`combat`/`collect`/`other`), registry YAML, HTML coverage report. Phase-1 strategies: Patrol, Deliver, Mission req. |

---

## Setup & ops (PowerShell)

| Script | What it does |
|--------|----------------|
| [`run-mission-live.ps1`](scripts/run-mission-live.ps1) | Forwards remaining args to `tools/mission-live` (`python -m mission_live …`). Use for doctor/categories/list/run/coverage/report against a live client+server. |
| [`init-databases.ps1`](scripts/init-databases.ps1) | Creates the MySQL databases required by AutoCore (auth/world/character, etc.) using local MySQL credentials. |
| [`export-starter-db.ps1`](scripts/export-starter-db.ps1) | Dumps live MySQL into shareable starter SQL under `sql/` (`autocore_world` with static data; auth/char schema only — no accounts or player rows). Re-run after world-data changes; use `-Force` to overwrite. |
| [`import-starter-db.ps1`](scripts/import-starter-db.ps1) | Imports `sql/autocore_starter.sql` into local MySQL so other operators get world data without any accounts. Create an admin after first boot via `DefaultAdminPassword` or `auth.create`. |
| [`recreate-char-db.ps1`](scripts/recreate-char-db.ps1) | Drops and recreates the character database so schema can be reset cleanly during development. |
| [`wipeplayers.ps1`](scripts/wipeplayers.ps1) | Truncates all character/player tables in `autocore_char` (chars, vehicles, inventory, missions, skills, clans, …) while keeping `account` rows and leaving `autocore_auth` untouched, then re-bases the `simple_object` coid sequence to 0x0800_0000 so persistent coids stay clear of authored map-object coids (SS-31 face C, docs/id-collisions.md §5.5). Use `-Force` for non-interactive runs; also available as the Grok workflow `wipeplayers`. |
| [`check-id-collisions.ps1`](scripts/check-id-collisions.ps1) | Read-only health check for the COID/`simple_object` identity-collision corruption classes in [`docs/id-collisions.md`](docs/id-collisions.md) (character/vehicle identity clobbered by another allocation, inventory pointing at a character/vehicle COID, orphan `Type=0` placeholder rows). Prints offending rows and counts; exits 1 if any query finds rows (CI-friendly), 0 if clean. Never writes to the database. |
| [`setup-client.ps1`](scripts/setup-client.ps1) | Adds or removes a Windows hosts-file entry so the retail Auto Assault client points at a local auth server (run as Administrator). |
| [`tail-mission-log.ps1`](scripts/tail-mission-log.ps1) | Tails `server-live.log` and prints only mission-related diagnostic lines (MISSION-DIAG, AutoPatrol, grant/fail, pad hits, etc.). |
| [`diff-worldentry-wire.ps1`](scripts/diff-worldentry-wire.ps1) | Slices `log-sector.txt` on `WireDiag.BeginSegment` world-entry markers and diffs the create/ghost stream of two entries. Use `-List` to see the segments (label carries `map=` and `resets=`), then `-Left/-Right` to compare a working entry against a frozen one. Needs the server started with `AUTOCORE_WIRE_DIAG=1`. |

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
| [`measure-solution-coverage.ps1`](scripts/measure-solution-coverage.ps1) | Solution-wide first-party assembly coverage gate (de-duped Cobertura). Collects all test projects, reports per-assembly line %, fails below 80%. TNL.NET reported separately. |
| [`measure-skills-hp-power-coverage.ps1`](scripts/measure-skills-hp-power-coverage.ps1) | Scoped coverage gate for skills, HP, and power modules. Default minimum 90%. |
| [`measure-town-pose-coverage.ps1`](scripts/measure-town-pose-coverage.ps1) | Scoped coverage gate for town on-foot logout/resume pose capture and disconnect teardown races. Default minimum 95%. |
| [`measure-world-state-coverage.ps1`](scripts/measure-world-state-coverage.ps1) | Method-focused coverage gate for character/vehicle world-state persistence surface. Default minimum 90%. |
| [`measure-xp-coverage.ps1`](scripts/measure-xp-coverage.ps1) | Scoped coverage gate for XP economy (ExperienceService, kill awards, GiveXP / level packets). Default minimum 90%. |

"""
PLATE: Add / update mission-live registry entries from retail game data.

Reads missions.glm (XML narrative + requirements) and clonebase.wad (gates,
giver NPC, continent, race/class) and upserts into
tools/mission-live/registry/missions.yaml.

Examples:
  python scripts/add_mission_to_registry.py --title "Live and Direct"
  python scripts/add_mission_to_registry.py --id 3032
  python scripts/add_mission_to_registry.py --name h_1-1_tas_arkbay_liveanddirect
  python scripts/add_mission_to_registry.py --title "Live and Direct" --dry-run

Requires: missions.glm + clonebase.wad under AA_INSTALL (or --glm / --wad).
"""

from __future__ import annotations

import argparse
import struct
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_clonebase, default_missions_glm, repo_root  # noqa: E402

try:
    import yaml
except ImportError:  # pragma: no cover
    yaml = None

# Phase-1 harness auto strategies (keep in sync with mission_live.plan.SUPPORTED_TYPES).
SUPPORTED_REQ_TYPES = frozenset({"patrol", "mission"})

RACE_NAMES = {
    -1: "any",
    0: "human",
    1: "mutant",
    2: "cyborg",
}


def default_registry_path() -> Path:
    return repo_root() / "tools" / "mission-live" / "registry" / "missions.yaml"


def find_mission_blocks(data: bytes) -> list[tuple[int, int]]:
    blocks: list[tuple[int, int]] = []
    idx = 0
    while True:
        start = data.find(b"<Mission ", idx)
        if start < 0:
            break
        end = data.find(b"</Mission>", start)
        if end < 0:
            break
        end += len(b"</Mission>")
        blocks.append((start, end))
        idx = end
    return blocks


def parse_glm_mission(block: bytes) -> dict[str, Any] | None:
    try:
        root = ET.fromstring(block.decode("latin-1"))
    except ET.ParseError:
        return None

    mid_raw = root.get("ID") or root.get("Id") or root.get("id")
    if not mid_raw:
        return None
    try:
        mission_id = int(mid_raw)
    except ValueError:
        return None

    req_counts: dict[str, int] = {}
    objectives: list[dict[str, Any]] = []
    for obj in root.findall("Objective"):
        reqs = []
        for req in obj.findall("Requirement"):
            rtype = (req.get("type") or "unknown").lower()
            req_counts[rtype] = req_counts.get(rtype, 0) + 1
            reqs.append({"type": rtype, "slot": int(req.get("slot") or 0)})
        objectives.append(
            {
                "sequence": int(obj.get("sequence") or 0),
                "objectiveId": int(obj.get("ID") or 0),
                "title": (obj.findtext("Title") or "").strip(),
                "requirements": reqs,
            }
        )

    return {
        "id": mission_id,
        "name": (root.get("name") or "").strip(),
        "title": (root.findtext("Title") or "").strip(),
        "internal": (root.findtext("Internal") or "").strip(),
        "coreMission": (root.findtext("CoreMission") or "").strip() == "1",
        "requirementCounts": req_counts,
        "objectives": objectives,
        "objectiveCount": len(objectives),
    }


def load_glm_index(glm_path: Path) -> dict[int, dict[str, Any]]:
    data = glm_path.read_bytes()
    by_id: dict[int, dict[str, Any]] = {}
    for start, end in find_mission_blocks(data):
        parsed = parse_glm_mission(data[start:end])
        if parsed:
            by_id[parsed["id"]] = parsed
    return by_id


def find_glm_by_title(by_id: dict[int, dict[str, Any]], title: str) -> list[dict[str, Any]]:
    needle = title.strip().lower()
    return [m for m in by_id.values() if (m.get("title") or "").lower() == needle]


def find_glm_by_name_substr(by_id: dict[int, dict[str, Any]], name: str) -> list[dict[str, Any]]:
    needle = name.strip().lower()
    return [m for m in by_id.values() if needle in (m.get("name") or "").lower()]


def utf16_name_at(data: bytes, off: int, nchars: int = 65) -> str:
    chars: list[str] = []
    for k in range(nchars):
        ch = struct.unpack_from("<H", data, off + k * 2)[0]
        if ch == 0:
            break
        if 32 <= ch < 127:
            chars.append(chr(ch))
        else:
            return ""
    return "".join(chars)


def parse_wad_mission_at(data: bytes, j: int) -> dict[str, Any] | None:
    mid = struct.unpack_from("<i", data, j)[0]
    name = utf16_name_at(data, j + 4, 65)
    if len(name) < 3:
        return None
    typ = data[j + 4 + 130]
    base = j + 4 + 130 + 2
    npc = struct.unpack_from("<i", data, base)[0]
    priority = struct.unpack_from("<i", data, base + 4)[0]
    req_race = struct.unpack_from("<h", data, base + 8)[0]
    req_class = struct.unpack_from("<h", data, base + 10)[0]
    req_lvl_min = struct.unpack_from("<i", data, base + 12)[0]
    req_lvl_max = struct.unpack_from("<i", data, base + 16)[0]
    req_missions = list(struct.unpack_from("<4i", data, base + 20))
    p = base + 20 + 16 + 2 + 2  # after req missions + isRep + pad
    p += 4 * 4  # Item
    p += 4 * 4  # ItemTemplate
    p += 4 * 4  # ItemValue
    p += 4 * 2  # ItemIsKit
    p += 4 * 4  # ItemQuantity
    p += 2 + 2  # AutoAssign + ActiveObjectiveOverride
    continent = struct.unpack_from("<i", data, p)[0]
    p += 4
    p += 4 * 7  # achievement/discipline/rewards/event
    p += 2 + 2  # targetLevel + pad
    ored = struct.unpack_from("<i", data, p)[0]
    prereqs = [x for x in req_missions if x > 0]
    return {
        "id": mid,
        "name": name,
        "type": typ,
        "npc": npc,
        "priority": priority,
        "reqRace": req_race,
        "reqClass": req_class,
        "reqLevelMin": req_lvl_min,
        "reqLevelMax": req_lvl_max,
        "reqMissionIds": prereqs,
        "continent": continent,
        "requirementsOred": ored,
    }


def find_wad_by_id(data: bytes, mid: int) -> dict[str, Any] | None:
    pat = struct.pack("<i", mid)
    idx = 0
    while True:
        j = data.find(pat, idx)
        if j < 0:
            return None
        parsed = parse_wad_mission_at(data, j)
        if parsed and parsed["id"] == mid:
            return parsed
        idx = j + 4


def suggest_policy(req_counts: dict[str, int]) -> str:
    if not req_counts:
        return "partial"
    unsupported = sum(c for t, c in req_counts.items() if t not in SUPPORTED_REQ_TYPES)
    return "strict" if unsupported == 0 else "partial"


def suggest_tags(glm: dict[str, Any], wad: dict[str, Any] | None) -> list[str]:
    tags: list[str] = []
    name = (glm.get("name") or "").lower()
    internal = (glm.get("internal") or "").lower()
    if "tutorial" in internal or "tutorial" in name:
        tags.append("tutorial")
    race = (wad or {}).get("reqRace", -1)
    race_tag = RACE_NAMES.get(int(race), None)
    if race_tag and race_tag != "any":
        tags.append(race_tag)
    counts = glm.get("requirementCounts") or {}
    for t in sorted(counts):
        tags.append(t)
    if "arkbay" in name:
        tags.append("arkbay")
    if glm.get("coreMission"):
        tags.append("core")
    # de-dupe preserve order
    seen: set[str] = set()
    out: list[str] = []
    for t in tags:
        if t not in seen:
            seen.add(t)
            out.append(t)
    return out


def build_registry_entry(
    glm: dict[str, Any],
    wad: dict[str, Any] | None,
    *,
    policy: str | None = None,
    timeout_sec: float | None = None,
) -> dict[str, Any]:
    counts = glm.get("requirementCounts") or {}
    entry: dict[str, Any] = {
        "id": int(glm["id"]),
        "title": glm.get("title") or "",
        "name": glm.get("name") or (wad or {}).get("name") or "",
        "internal": glm.get("internal") or "",
        "policy": policy or suggest_policy(counts),
        "tags": suggest_tags(glm, wad),
        "notes": "",
        "continent": int((wad or {}).get("continent") or 0),
        "npc": int((wad or {}).get("npc") or 0),
        "reqRace": int((wad or {}).get("reqRace", -1)),
        "reqClass": int((wad or {}).get("reqClass", -1)),
        "reqLevelMin": int((wad or {}).get("reqLevelMin") or 0),
        "reqMissionIds": list((wad or {}).get("reqMissionIds") or []),
        "requirementCounts": dict(counts),
        "objectiveCount": int(glm.get("objectiveCount") or 0),
    }
    race_name = RACE_NAMES.get(entry["reqRace"], str(entry["reqRace"]))
    unsupported = [t for t in counts if t not in SUPPORTED_REQ_TYPES]
    bits = [
        glm.get("internal") or glm.get("title") or entry["name"],
        f"race={race_name}",
        f"reqs={dict(counts)}",
    ]
    if unsupported:
        bits.append(f"unsupported={unsupported} -> policy partial")
    entry["notes"] = "; ".join(bits)
    if timeout_sec is not None:
        entry["timeoutSec"] = timeout_sec
    return entry


def load_registry_yaml(path: Path) -> dict[str, Any]:
    if yaml is None:
        raise RuntimeError("PyYAML is required. pip install pyyaml")
    if not path.exists():
        return {"missions": []}
    data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    if not isinstance(data, dict):
        data = {"missions": []}
    missions = data.get("missions")
    if missions is None:
        data["missions"] = []
    elif not isinstance(missions, list):
        raise ValueError(f"registry missions must be a list: {path}")
    return data


def upsert_mission(registry: dict[str, Any], entry: dict[str, Any]) -> str:
    missions: list[dict[str, Any]] = registry.setdefault("missions", [])
    mid = int(entry["id"])
    for i, existing in enumerate(missions):
        if int(existing.get("id", -1)) == mid:
            # Preserve user overrides for policy/timeout/tags/notes when already set intentionally?
            # Prefer fresh game-data snapshot but keep explicit timeoutSec if present and new has none.
            merged = dict(existing)
            merged.update(entry)
            if existing.get("timeoutSec") is not None and entry.get("timeoutSec") is None:
                merged["timeoutSec"] = existing["timeoutSec"]
            missions[i] = merged
            return "updated"
    missions.append(entry)
    missions.sort(key=lambda m: int(m.get("id") or 0))
    return "added"


def dump_yaml(registry: dict[str, Any], path: Path) -> None:
    if yaml is None:
        raise RuntimeError("PyYAML is required. pip install pyyaml")
    path.parent.mkdir(parents=True, exist_ok=True)
    # Prefer block style for readability.
    text = yaml.safe_dump(
        registry,
        sort_keys=False,
        allow_unicode=True,
        default_flow_style=False,
    )
    path.write_text(text, encoding="utf-8")


def resolve_missions(
    by_id: dict[int, dict[str, Any]],
    *,
    ids: list[int],
    titles: list[str],
    names: list[str],
) -> list[dict[str, Any]]:
    found: list[dict[str, Any]] = []
    seen: set[int] = set()

    def add(m: dict[str, Any]) -> None:
        mid = int(m["id"])
        if mid not in seen:
            seen.add(mid)
            found.append(m)

    for mid in ids:
        m = by_id.get(mid)
        if m is None:
            raise SystemExit(f"mission id {mid} not found in missions.glm")
        add(m)

    for title in titles:
        matches = find_glm_by_title(by_id, title)
        if not matches:
            raise SystemExit(f"title {title!r} not found in missions.glm")
        if len(matches) > 1:
            ids_list = ", ".join(str(m["id"]) for m in matches)
            raise SystemExit(f"title {title!r} matches multiple missions: {ids_list}")
        add(matches[0])

    for name in names:
        matches = find_glm_by_name_substr(by_id, name)
        if not matches:
            raise SystemExit(f"name {name!r} not found in missions.glm")
        if len(matches) > 1:
            # Prefer exact name match
            exact = [m for m in matches if (m.get("name") or "").lower() == name.lower()]
            if len(exact) == 1:
                add(exact[0])
                continue
            ids_list = ", ".join(f"{m['id']}:{m.get('name')}" for m in matches[:10])
            raise SystemExit(f"name {name!r} matches multiple missions: {ids_list}")
        add(matches[0])

    return found


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Examples:")[0].strip())
    ap.add_argument("--glm", type=Path, default=None)
    ap.add_argument("--wad", type=Path, default=None)
    ap.add_argument("--registry", type=Path, default=None, help="Path to missions.yaml")
    ap.add_argument("--id", type=int, action="append", default=[], help="Mission id")
    ap.add_argument("--title", action="append", default=[], help="Exact Title match")
    ap.add_argument("--name", action="append", default=[], help="Internal name / substring")
    ap.add_argument("--policy", choices=("partial", "strict"), default=None)
    ap.add_argument("--timeout-sec", type=float, default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args(argv)

    if not args.id and not args.title and not args.name:
        ap.error("provide --id, --title, and/or --name")

    glm_path = args.glm or default_missions_glm()
    wad_path = args.wad or default_clonebase()
    registry_path = args.registry or default_registry_path()

    if not glm_path.is_file():
        print(f"missing {glm_path}", file=sys.stderr)
        return 1
    if not wad_path.is_file():
        print(f"missing {wad_path}", file=sys.stderr)
        return 1

    print(f"loading GLM {glm_path} …")
    by_id = load_glm_index(glm_path)
    print(f"  {len(by_id)} missions")

    wad_data = wad_path.read_bytes()
    missions = resolve_missions(by_id, ids=args.id, titles=args.title, names=args.name)

    registry = load_registry_yaml(registry_path)
    actions: list[str] = []
    for glm in missions:
        wad = find_wad_by_id(wad_data, int(glm["id"]))
        entry = build_registry_entry(
            glm,
            wad,
            policy=args.policy,
            timeout_sec=args.timeout_sec,
        )
        action = upsert_mission(registry, entry)
        actions.append(f"{action} id={entry['id']} title={entry['title']!r} policy={entry['policy']}")
        print(
            f"{action}: id={entry['id']} title={entry['title']!r} "
            f"name={entry['name']!r} continent={entry['continent']} npc={entry['npc']} "
            f"race={entry['reqRace']} reqs={entry['requirementCounts']} policy={entry['policy']}"
        )

    if args.dry_run:
        print("dry-run: registry not written")
        print(yaml.safe_dump({"missions": [m for m in registry["missions"] if int(m["id"]) in {int(x['id']) for x in missions}]}, sort_keys=False))
        return 0

    dump_yaml(registry, registry_path)
    print(f"wrote {registry_path} ({len(registry['missions'])} missions)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

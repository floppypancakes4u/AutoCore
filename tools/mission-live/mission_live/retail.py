"""Retail mission catalog: GLM XML + optional clonebase.wad merge."""

from __future__ import annotations

import json
import os
import struct
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


class CatalogError(RuntimeError):
    pass


def default_install_dir() -> Path:
    return Path(os.environ.get("AA_INSTALL", r"C:\Program Files (x86)\NetDevil\Auto Assault"))


def default_missions_glm() -> Path:
    override = os.environ.get("MISSION_LIVE_GLM")
    if override:
        return Path(override)
    return default_install_dir() / "missions.glm"


def default_clonebase_wad() -> Path:
    override = os.environ.get("MISSION_LIVE_WAD")
    if override:
        return Path(override)
    return default_install_dir() / "clonebase.wad"


def default_missions_json() -> Path:
    override = os.environ.get("MISSION_LIVE_CATALOG")
    if override:
        return Path(override)
    return Path(__file__).resolve().parents[3] / "tools" / "mission-viewer" / "missions.json"


def _child_int(elem: ET.Element, *names: str, default: int | None = None) -> int | None:
    for name in names:
        child = elem.find(name)
        if child is None or child.text is None or not str(child.text).strip():
            continue
        try:
            return int(child.text)
        except ValueError:
            continue
    return default


def _parse_requirement(req: ET.Element) -> dict[str, Any]:
    rtype = (req.get("type") or "unknown").strip().lower()
    out: dict[str, Any] = {
        "type": rtype,
        "slot": int(req.get("slot") or 0),
    }
    if rtype == "deliver":
        num = _child_int(req, "NumToDeliver", default=0)
        item = _child_int(req, "CBIDItem", "ItemCBID", default=-1)
        npc = _child_int(req, "TargetNPCCBID", "NPCTargetCBID", default=0)
        completes = _child_int(req, "NPCTargetCompletes", default=1)
        out["numToDeliver"] = 0 if num is None else num
        out["itemCbid"] = -1 if item is None else item
        out["npcTargetCbid"] = 0 if npc is None else npc
        out["npcTargetCompletes"] = bool(completes)
    return out


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
            parsed = _parse_requirement(req)
            rtype = parsed["type"]
            req_counts[rtype] = req_counts.get(rtype, 0) + 1
            reqs.append(parsed)
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
        "npcGiverCbid": 0,
        "continent": 0,
        "reqRace": -1,
        "reqClass": -1,
        "reqLevelMin": 0,
        "requirementCounts": req_counts,
        "objectives": objectives,
        "objectiveCount": len(objectives),
    }


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


def load_glm_index(glm_path: Path) -> dict[int, dict[str, Any]]:
    data = glm_path.read_bytes()
    by_id: dict[int, dict[str, Any]] = {}
    for start, end in find_mission_blocks(data):
        parsed = parse_glm_mission(data[start:end])
        if parsed:
            by_id[parsed["id"]] = parsed
    return by_id


def merge_wad_fields(mission: dict[str, Any], wad: dict[str, Any]) -> dict[str, Any]:
    if wad.get("npc") is not None:
        mission["npcGiverCbid"] = int(wad["npc"])
    if wad.get("continent") is not None:
        mission["continent"] = int(wad["continent"])
    if wad.get("reqLevelMin") is not None:
        mission["reqLevelMin"] = int(wad["reqLevelMin"])
    if wad.get("reqRace") is not None:
        mission["reqRace"] = int(wad["reqRace"])
    if wad.get("reqClass") is not None:
        mission["reqClass"] = int(wad["reqClass"])
    return mission


def _utf16_name_at(data: bytes, off: int, nchars: int = 65) -> str:
    chars: list[str] = []
    for k in range(nchars):
        if off + k * 2 + 2 > len(data):
            break
        ch = struct.unpack_from("<H", data, off + k * 2)[0]
        if ch == 0:
            break
        if 32 <= ch < 127:
            chars.append(chr(ch))
        else:
            return ""
    return "".join(chars)


def parse_wad_mission_at(data: bytes, j: int) -> dict[str, Any] | None:
    if j + 4 + 130 + 2 + 40 > len(data):
        return None
    mid = struct.unpack_from("<i", data, j)[0]
    name = _utf16_name_at(data, j + 4, 65)
    if len(name) < 3:
        return None
    base = j + 4 + 130 + 2
    if base + 36 > len(data):
        return None
    npc = struct.unpack_from("<i", data, base)[0]
    req_race = struct.unpack_from("<h", data, base + 8)[0]
    req_class = struct.unpack_from("<h", data, base + 10)[0]
    req_lvl_min = struct.unpack_from("<i", data, base + 12)[0]
    req_missions = list(struct.unpack_from("<4i", data, base + 20))
    p = base + 20 + 16 + 2 + 2
    p += 4 * 4  # Item
    p += 4 * 4  # ItemTemplate
    p += 4 * 4  # ItemValue
    p += 4 * 2  # ItemIsKit
    p += 4 * 4  # ItemQuantity
    p += 2 + 2  # AutoAssign + ActiveObjectiveOverride
    if p + 4 > len(data):
        return None
    continent = struct.unpack_from("<i", data, p)[0]
    return {
        "id": mid,
        "name": name,
        "npc": npc,
        "reqRace": req_race,
        "reqClass": req_class,
        "reqLevelMin": req_lvl_min,
        "reqMissionIds": [x for x in req_missions if x > 0],
        "continent": continent,
    }


def index_wad_for_ids(data: bytes, ids: set[int]) -> dict[int, dict[str, Any]]:
    """One linear scan: parse a record when the i32 at an offset is a wanted id."""
    out: dict[int, dict[str, Any]] = {}
    if not ids:
        return out
    limit = max(0, len(data) - 200)
    for j in range(0, limit, 4):
        mid = struct.unpack_from("<i", data, j)[0]
        if mid not in ids or mid in out:
            continue
        parsed = parse_wad_mission_at(data, j)
        if parsed and parsed["id"] == mid:
            out[mid] = parsed
            if len(out) == len(ids):
                break
    return out


def apply_wad(catalog: dict[int, dict[str, Any]], wad_path: Path) -> None:
    if not wad_path.exists():
        return
    data = wad_path.read_bytes()
    indexed = index_wad_for_ids(data, set(catalog.keys()))
    for mid, mission in catalog.items():
        wad = indexed.get(mid)
        if wad:
            merge_wad_fields(mission, wad)


def _load_json_catalog(path: Path) -> dict[int, dict[str, Any]]:
    data = json.loads(path.read_text(encoding="utf-8"))
    missions = data.get("missions") or data
    out: dict[int, dict[str, Any]] = {}
    if isinstance(missions, dict):
        for k, v in missions.items():
            try:
                mid = int(k)
            except (TypeError, ValueError):
                continue
            entry = v if isinstance(v, dict) else {"id": mid}
            entry.setdefault("id", mid)
            out[mid] = entry
        return out
    if isinstance(missions, list):
        for m in missions:
            if not isinstance(m, dict):
                continue
            mid = m.get("id") or m.get("missionId")
            if mid is None:
                continue
            m = dict(m)
            m["id"] = int(mid)
            out[int(mid)] = m
    return out


def load_retail_catalog(
    json_path: Path | None = None,
    glm_path: Path | None = None,
    wad_path: Path | None = None,
    *,
    required: bool = True,
) -> dict[int, dict[str, Any]]:
    json_path = json_path if json_path is not None else default_missions_json()
    glm_path = glm_path if glm_path is not None else default_missions_glm()
    wad_path = wad_path if wad_path is not None else default_clonebase_wad()

    if json_path.exists():
        return _load_json_catalog(json_path)

    if glm_path.exists():
        catalog = load_glm_index(glm_path)
        apply_wad(catalog, wad_path)
        return catalog

    if required:
        raise CatalogError(
            "Retail catalog not found. Set MISSION_LIVE_CATALOG to missions.json, "
            "or provide missions.glm (MISSION_LIVE_GLM / AA_INSTALL) and clonebase.wad."
        )
    return {}

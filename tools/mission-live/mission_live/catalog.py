"""Retail mission catalog helpers (optional AA_INSTALL / export JSON)."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any


def repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def default_missions_json() -> Path:
    return repo_root() / "tools" / "mission-viewer" / "missions.json"


def load_catalog(path: Path | None = None) -> dict[int, dict[str, Any]]:
    """
    Load mission id -> summary from missions.json if present.
    Returns empty dict when the export has not been generated.
    """
    path = path or default_missions_json()
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    missions = data.get("missions") or data
    if isinstance(missions, dict):
        # id-keyed
        out: dict[int, dict[str, Any]] = {}
        for k, v in missions.items():
            try:
                out[int(k)] = v if isinstance(v, dict) else {"id": int(k)}
            except (TypeError, ValueError):
                continue
        return out
    if isinstance(missions, list):
        out = {}
        for m in missions:
            if not isinstance(m, dict):
                continue
            mid = m.get("id") or m.get("missionId")
            if mid is None:
                continue
            out[int(mid)] = m
        return out
    return {}


def requirement_types_in_catalog_entry(entry: dict[str, Any]) -> list[str]:
    types: list[str] = []
    for obj in entry.get("objectives") or []:
        for req in obj.get("requirements") or []:
            t = req.get("type") or req.get("RequirementType")
            if t:
                types.append(str(t))
    return types

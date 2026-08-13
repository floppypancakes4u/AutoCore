"""Retail mission catalog helpers (optional AA_INSTALL / export JSON)."""

from __future__ import annotations

from pathlib import Path
from typing import Any

from mission_live.retail import (
    default_missions_json,
    load_retail_catalog,
)


def repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def load_catalog(path: Path | None = None) -> dict[int, dict[str, Any]]:
    """
    Load mission id -> summary from missions.json, else missions.glm.
    Returns empty dict when neither source is available.
    """
    if path is not None:
        return load_retail_catalog(json_path=path, required=False)
    return load_retail_catalog(required=False)


def requirement_types_in_catalog_entry(entry: dict[str, Any]) -> list[str]:
    types: list[str] = []
    for obj in entry.get("objectives") or []:
        for req in obj.get("requirements") or []:
            t = req.get("type") or req.get("RequirementType")
            if t:
                types.append(str(t))
    return types

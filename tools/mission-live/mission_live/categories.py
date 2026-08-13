"""Exclusive playstyle categories for the retail mission catalog."""

from __future__ import annotations

from typing import Any, Iterable

from mission_live.talk_patrol_deliver import (
    ALLOWED_REQ_TYPES,
    deliver_requirements,
    is_talk_patrol_deliver,
    requirement_type_set,
)

CATEGORIES: tuple[str, ...] = ("travel", "cargo", "combat", "collect", "other")

CATEGORY_DESCRIPTIONS: dict[str, str] = {
    "travel": "talk + optional patrol + speak-only turn-in",
    "cargo": "item hand-in (patrol + deliver only)",
    "combat": "kill / kill_aggregate",
    "collect": "collect",
    "other": "mixed / remaining",
}

_COMBAT_TYPES = frozenset({"kill", "kill_aggregate"})


def _giver_cbid(mission: dict[str, Any]) -> int:
    try:
        return int(mission.get("npcGiverCbid") or 0)
    except (TypeError, ValueError):
        return 0


def _item_cbid(req: dict[str, Any]) -> int:
    for key in ("itemCbid", "itemCBID", "cbidItem", "CBIDItem", "itemId"):
        if key in req and req[key] is not None:
            try:
                return int(req[key])
            except (TypeError, ValueError):
                return 0
    return 0


def is_cargo_deliver(req: dict[str, Any]) -> bool:
    """True when a deliver requirement hands in an item."""
    if (req.get("type") or "").strip().lower() != "deliver":
        return False
    try:
        if int(req.get("numToDeliver") or 0) > 0:
            return True
    except (TypeError, ValueError):
        pass
    return _item_cbid(req) > 0


def is_pure_cargo(mission: dict[str, Any]) -> bool:
    """Giver + only patrol/deliver types + at least one cargo deliver."""
    if _giver_cbid(mission) <= 0:
        return False
    types = requirement_type_set(mission)
    if not types or (types - ALLOWED_REQ_TYPES):
        return False
    if "deliver" not in types:
        return False
    return any(is_cargo_deliver(req) for req in deliver_requirements(mission))


def category_for(mission: dict[str, Any]) -> str:
    """First matching exclusive bucket."""
    if is_talk_patrol_deliver(mission):
        return "travel"
    if is_pure_cargo(mission):
        return "cargo"
    types = requirement_type_set(mission)
    if types & _COMBAT_TYPES:
        return "combat"
    if "collect" in types:
        return "collect"
    return "other"


def filter_catalog(missions: Iterable[dict[str, Any]], name: str) -> list[dict[str, Any]]:
    key = (name or "").strip().lower()
    if key not in CATEGORIES:
        raise ValueError(f"unknown category: {name}")
    return [m for m in missions if category_for(m) == key]


def sort_key(mission: dict[str, Any]) -> tuple[int, int, int]:
    try:
        continent = int(mission.get("continent") or 0)
    except (TypeError, ValueError):
        continent = 0
    try:
        level = int(mission.get("reqLevelMin") or 0)
    except (TypeError, ValueError):
        level = 0
    try:
        mid = int(mission.get("id") or mission.get("missionId") or 0)
    except (TypeError, ValueError):
        mid = 0
    return (continent, level, mid)


def sort_missions(missions: Iterable[dict[str, Any]]) -> list[dict[str, Any]]:
    return sorted(missions, key=sort_key)


def missions_in_category(missions: Iterable[dict[str, Any]], name: str) -> list[dict[str, Any]]:
    return sort_missions(filter_catalog(missions, name))

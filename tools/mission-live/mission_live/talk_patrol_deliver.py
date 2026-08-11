"""Talk → optional patrol → speak-only deliver mission filter."""

from __future__ import annotations

from typing import Any


ALLOWED_REQ_TYPES = frozenset({"patrol", "deliver"})


def _iter_requirements(mission: dict[str, Any]):
    for obj in mission.get("objectives") or []:
        for req in obj.get("requirements") or []:
            yield req


def requirement_type_set(mission: dict[str, Any]) -> set[str]:
    types: set[str] = set()
    for req in _iter_requirements(mission):
        t = (req.get("type") or "").strip().lower()
        if t:
            types.add(t)
    return types


def deliver_requirements(mission: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        req
        for req in _iter_requirements(mission)
        if (req.get("type") or "").strip().lower() == "deliver"
    ]


def patrol_count(mission: dict[str, Any]) -> int:
    return sum(
        1
        for req in _iter_requirements(mission)
        if (req.get("type") or "").strip().lower() == "patrol"
    )


def is_speak_only_deliver(req: dict[str, Any]) -> bool:
    """True for talk-to-NPC turn-ins (no cargo item to hand in)."""
    if int(req.get("numToDeliver") or 0) > 0:
        return False
    # Prefer explicit item fields when present (GLM / alternate exports).
    for key in ("itemCbid", "itemCBID", "cbidItem", "CBIDItem", "itemId"):
        if key in req and req[key] is not None:
            try:
                if int(req[key]) > 0:
                    return False
            except (TypeError, ValueError):
                return False
    if req.get("npcTargetCompletes") is False:
        return False
    try:
        return int(req.get("npcTargetCbid") or 0) > 0
    except (TypeError, ValueError):
        return False


def is_talk_patrol_deliver(mission: dict[str, Any]) -> bool:
    """
    Live-and-Direct-shaped mission:

    - accept from a giver NPC (npcGiverCbid)
    - objectives are only patrol and/or deliver
    - exactly one speak-only deliver turn-in
    - zero or more patrol waypoints
    """
    try:
        giver = int(mission.get("npcGiverCbid") or 0)
    except (TypeError, ValueError):
        giver = 0
    if giver <= 0:
        return False

    types = requirement_type_set(mission)
    if not types or (types - ALLOWED_REQ_TYPES):
        return False
    if "deliver" not in types:
        return False

    delivers = deliver_requirements(mission)
    if len(delivers) != 1:
        return False
    return is_speak_only_deliver(delivers[0])


def filter_talk_patrol_deliver(missions: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [m for m in missions if is_talk_patrol_deliver(m)]

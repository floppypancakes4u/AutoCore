"""Capability matrix + plan helpers."""

from __future__ import annotations

from typing import Any

# Phase-1 auto strategies. Everything else is unsupported → SKIP/PARTIAL.
SUPPORTED_TYPES = frozenset({"Patrol", "Mission", "Deliver"})


def capability_for(req_type: str) -> str:
    return "auto" if req_type in SUPPORTED_TYPES else "unsupported"


def annotate_plan(plan: dict[str, Any]) -> dict[str, Any]:
    """Attach capability flags to each requirement in a Dev API mission plan."""
    out = dict(plan)
    objectives = []
    for obj in plan.get("objectives") or []:
        obj2 = dict(obj)
        reqs = []
        for req in obj.get("requirements") or []:
            r = dict(req)
            r["capability"] = capability_for(str(r.get("type") or ""))
            reqs.append(r)
        obj2["requirements"] = reqs
        objectives.append(obj2)
    out["objectives"] = objectives
    out["supportedRequirementCount"] = sum(
        1 for o in objectives for r in o["requirements"] if r["capability"] == "auto"
    )
    out["unsupportedRequirementCount"] = sum(
        1 for o in objectives for r in o["requirements"] if r["capability"] != "auto"
    )
    return out


def race_class_eligible(plan: dict[str, Any], state: dict[str, Any]) -> tuple[bool, str]:
    """Return (ok, reason). Unrestricted when reqRace/reqClass == -1."""
    req_race = int(plan.get("reqRace", -1))
    req_class = int(plan.get("reqClass", -1))
    need_race = req_race != -1
    need_class = req_class != -1
    if not need_race and not need_class:
        return True, ""

    if not state.get("hasBody"):
        return False, "character body race/class unavailable"

    race = state.get("race")
    class_id = state.get("class")
    if need_race and race != req_race:
        return False, f"reqRace={req_race} characterRace={race}"
    if need_class and class_id != req_class:
        return False, f"reqClass={req_class} characterClass={class_id}"
    return True, ""


def filter_race_class_eligible(
    missions: list[dict[str, Any]],
    state: dict[str, Any],
    *,
    force_grant: bool = False,
) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    """Split missions into (eligible, skipped) using character race/class.

    ``force_grant`` or a missing character body leaves the list unchanged so a
    live run can still attempt the mission (or skip later with a real state).
    """
    if force_grant or not state.get("hasBody"):
        return list(missions), []
    keep: list[dict[str, Any]] = []
    skip: list[dict[str, Any]] = []
    for mission in missions:
        ok, _reason = race_class_eligible(mission, state)
        (keep if ok else skip).append(mission)
    return keep, skip

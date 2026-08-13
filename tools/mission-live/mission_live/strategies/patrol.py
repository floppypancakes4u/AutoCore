"""Patrol requirement strategy: walk AutoComplete pads via /tptowaypoint."""

from __future__ import annotations

import re
import time
from typing import Any, Callable

from mission_live.strategies.base import RunContext, StepResult

_POS_RE = re.compile(
    r"player:\s*(?:vehicle|character)\s+"
    r"x=(-?\d+(?:\.\d+)?)\s+y=(-?\d+(?:\.\d+)?)\s+z=(-?\d+(?:\.\d+)?)",
    re.IGNORECASE,
)

_PATROL_RE = re.compile(
    r"mission-patrol:\s*id=(-?\d+)\s+seq=(-?\d+)\s+progress=(-?\d+)\s+max=(-?\d+)\s+next=(-?\d+)",
    re.IGNORECASE,
)


def parse_mission_patrol(output: str) -> dict[str, int] | None:
    """Parse a DevTool ``mission patrol`` status line."""
    m = _PATROL_RE.search(output or "")
    if not m:
        return None
    return {
        "id": int(m.group(1)),
        "seq": int(m.group(2)),
        "progress": int(m.group(3)),
        "max": int(m.group(4)),
        "next": int(m.group(5)),
    }


def needed_pads(req: dict[str, Any]) -> int:
    targets = req.get("targets") or []
    listed = len(targets) if isinstance(targets, list) else 0
    target_count = int(req.get("targetCount") or 0)
    laps = int(req.get("laps") or 1)
    if laps < 1:
        laps = 1
    return max(target_count, listed, 1) * laps


class PatrolStrategy:
    requirement_type = "Patrol"

    def can_handle(self, req: dict[str, Any]) -> bool:
        return str(req.get("type") or "") == "Patrol"

    def execute(self, ctx: RunContext, req: dict[str, Any], *, mission_id: int, seq: int) -> StepResult:
        key = f"{mission_id}/{seq}/Patrol"
        start = _quest_snapshot(ctx.state(), mission_id)
        baseline = dict(start) if start else None
        before_pos = _read_position(ctx)
        needed = needed_pads(req)
        auto_complete = req.get("autoComplete")
        if auto_complete is None:
            auto_complete = True

        def advanced(s: dict) -> bool:
            after = _quest_snapshot(s, mission_id)
            if after is None:
                return True
            if baseline is None:
                return False
            if after["seq"] > baseline["seq"]:
                return True
            if after["seq"] == baseline["seq"] and after["progress"] > baseline["progress"]:
                return True
            return False

        attempts = 0
        max_attempts = max(needed * 3, 12)
        outputs: list[str] = []
        while attempts < max_attempts:
            attempts += 1
            tp = ctx.chat("/tptowaypoint", settle_sec=0.0)
            outputs.append(str(tp.get("output") or ""))
            try:
                status = ctx.cmd("mission patrol")
                outputs.append(str(status.get("output") or ""))
            except Exception:
                pass

            outcome = _wait_pose_or_progress(ctx, advanced, before_pos, timeout=2.0)
            if outcome == "advanced" or _quest_done(ctx, mission_id, baseline, needed):
                if _quest_finished(ctx, mission_id, start, needed):
                    return _pass(key, attempts, start, ctx, outputs)
                after = _quest_snapshot(ctx.state(), mission_id)
                if after is not None:
                    baseline = dict(after)
                    before_pos = _read_position(ctx) or before_pos
                continue

            if not auto_complete:
                interact = ctx.cmd("action activate-or-interact")
                outputs.append(str(interact.get("output") or ""))
                if _poll_advanced(ctx, advanced, timeout=0.8):
                    if _quest_finished(ctx, mission_id, start, needed):
                        return _pass(key, attempts, start, ctx, outputs)
                    after = _quest_snapshot(ctx.state(), mission_id)
                    if after is not None:
                        baseline = dict(after)
                        before_pos = _read_position(ctx) or before_pos
                    continue

            before_pos = _read_position(ctx) or before_pos

        return StepResult(
            key=key,
            status="FAIL",
            detail=f"no progress after {attempts} tp attempts",
            before=start or {},
            after=_quest_snapshot(ctx.state(), mission_id) or {},
            output="\n".join(outputs)[-2000:],
        )


def _quest_finished(
    ctx: RunContext,
    mission_id: int,
    start: dict[str, Any] | None,
    needed: int,
) -> bool:
    after = _quest_snapshot(ctx.state(), mission_id)
    if after is None:
        return True
    if start is not None and after["seq"] > start["seq"]:
        return True
    if after["progress"] >= needed:
        return True
    if after["max"] > 1 and after["progress"] >= after["max"]:
        return True
    if needed <= 1 and start is not None and after["progress"] > start["progress"]:
        return True
    if needed <= 1 and start is None and after["progress"] > 0:
        return True
    return False


def _quest_done(
    ctx: RunContext,
    mission_id: int,
    baseline: dict[str, Any] | None,
    needed: int,
) -> bool:
    after = _quest_snapshot(ctx.state(), mission_id)
    if after is None:
        return True
    if baseline is not None and after["seq"] > baseline["seq"]:
        return True
    if after["progress"] >= needed:
        return True
    return False


def _pass(
    key: str,
    attempts: int,
    before: dict[str, Any] | None,
    ctx: RunContext,
    outputs: list[str],
) -> StepResult:
    return StepResult(
        key=key,
        status="PASS",
        detail=f"attempts={attempts}",
        before=before or {},
        after=_quest_snapshot(ctx.state(), _mission_from_key(key)) or {},
        output="\n".join(outputs)[-2000:],
    )


def _mission_from_key(key: str) -> int:
    # "{missionId}/{seq}/Patrol"
    return int(key.split("/", 1)[0])


def _quest_snapshot(state: dict[str, Any], mission_id: int) -> dict[str, Any] | None:
    for q in state.get("activeQuests") or []:
        if int(q.get("missionId") or 0) == mission_id:
            return {
                "seq": int(q.get("seq") or 0),
                "progress": int(q.get("progress") or 0),
                "max": int(q.get("max") or 0),
            }
    return None


def _read_position(ctx: RunContext) -> tuple[float, float, float] | None:
    try:
        doc = ctx.cmd("player position")
    except Exception:
        return None
    out = str(doc.get("output") or "")
    matches = list(_POS_RE.finditer(out))
    if not matches:
        return None
    m = matches[0]
    return float(m.group(1)), float(m.group(2)), float(m.group(3))


def _moved(
    before: tuple[float, float, float] | None,
    after: tuple[float, float, float] | None,
    *,
    min_delta: float = 5.0,
) -> bool:
    if before is None or after is None:
        return False
    dx = after[0] - before[0]
    dy = after[1] - before[1]
    dz = after[2] - before[2]
    return (dx * dx + dy * dy + dz * dz) >= (min_delta * min_delta)


def _poll_advanced(ctx: RunContext, advanced: Callable[[dict], bool], *, timeout: float) -> bool:
    deadline = time.time() + timeout
    while time.time() < deadline:
        if advanced(ctx.state()):
            return True
        ctx.sleep(0.08)
    return advanced(ctx.state())


def _wait_pose_or_progress(
    ctx: RunContext,
    advanced: Callable[[dict], bool],
    before_pos: tuple[float, float, float] | None,
    *,
    timeout: float,
) -> str:
    """
    After /tptowaypoint: return ``advanced`` once quest progress moves.

    As soon as client pose reflects the teleport, poll briefly for AutoComplete
    credit — do not wait out a fixed settle if progress already landed.
    """
    if before_pos is None:
        # No baseline pose — poll progress only, then let caller interact.
        if _poll_advanced(ctx, advanced, timeout=min(timeout, 0.7)):
            return "advanced"
        return "timeout"

    deadline = time.time() + timeout
    while time.time() < deadline:
        if advanced(ctx.state()):
            return "advanced"
        pos = _read_position(ctx)
        if _moved(before_pos, pos):
            # Pose verified — short AutoComplete window, then caller may interact.
            if _poll_advanced(ctx, advanced, timeout=0.45):
                return "advanced"
            return "posed"
        ctx.sleep(0.08)
    if advanced(ctx.state()):
        return "advanced"
    return "timeout"

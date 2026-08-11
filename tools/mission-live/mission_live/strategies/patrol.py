"""Patrol requirement strategy: /tptowaypoint + interact loop."""

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


class PatrolStrategy:
    requirement_type = "Patrol"

    def can_handle(self, req: dict[str, Any]) -> bool:
        return str(req.get("type") or "") == "Patrol"

    def execute(self, ctx: RunContext, req: dict[str, Any], *, mission_id: int, seq: int) -> StepResult:
        key = f"{mission_id}/{seq}/Patrol"
        before = _quest_snapshot(ctx.state(), mission_id)
        before_pos = _read_position(ctx)

        def advanced(s: dict) -> bool:
            after = _quest_snapshot(s, mission_id)
            if after is None:
                return True
            if before is None:
                return False
            if after["seq"] > before["seq"]:
                return True
            if after["seq"] == before["seq"] and after["progress"] > before["progress"]:
                return True
            return False

        attempts = 0
        max_attempts = 12
        outputs: list[str] = []
        while attempts < max_attempts:
            attempts += 1
            # Enter chat immediately; advance as soon as pose/progress verifies.
            tp = ctx.chat("/tptowaypoint", settle_sec=0.0)
            outputs.append(str(tp.get("output") or ""))
            outcome = _wait_pose_or_progress(ctx, advanced, before_pos, timeout=2.0)
            if outcome == "advanced":
                return _pass(key, attempts, before, ctx, outputs)

            interact = ctx.cmd("action activate-or-interact")
            outputs.append(str(interact.get("output") or ""))
            if _poll_advanced(ctx, advanced, timeout=0.8):
                return _pass(key, attempts, before, ctx, outputs)
            before_pos = _read_position(ctx) or before_pos

        return StepResult(
            key=key,
            status="FAIL",
            detail=f"no progress after {attempts} tp+interact attempts",
            before=before or {},
            after=_quest_snapshot(ctx.state(), mission_id) or {},
            output="\n".join(outputs)[-2000:],
        )


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

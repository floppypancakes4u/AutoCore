"""Mission live runner."""

from __future__ import annotations

import time
from typing import Any

from mission_live.plan import annotate_plan
from mission_live.registry import RegistryEntry
from mission_live.strategies import handle_dialogs, strategy_for
from mission_live.strategies.base import MissionResult, RunContext, StepResult
from mission_live.strategies.setup import run_setup


def run_mission(
    ctx: RunContext,
    mission_id: int,
    *,
    policy: str = "partial",
    title_hint: str = "",
) -> MissionResult:
    started = time.time()
    result = MissionResult(mission_id=mission_id, status="ERROR", policy=policy)
    ctx.mission_result = result

    if ctx.progress is not None:
        ctx.progress.mission_header(mission_id, title_hint)

    ctx.step_begin("plan", str(mission_id))
    try:
        plan = annotate_plan(ctx.oracle.mission_plan(mission_id))
    except Exception as ex:
        result.status = "ERROR"
        result.fail_locus = "plan"
        err = StepResult(key="plan", status="ERROR", detail=str(ex))
        result.steps.append(err)
        ctx.step_end(err)
        result.duration_sec = time.time() - started
        return result

    result.title = title_hint or str(plan.get("title") or plan.get("name") or "")
    ctx.plan = plan
    plan_step = StepResult(key="plan", status="PASS", detail=result.title or str(mission_id))
    result.steps.append(plan_step)
    ctx.step_end(plan_step)

    # Setup
    setup_steps = run_setup(ctx, mission_id, plan)
    result.steps.extend(setup_steps)

    if any(s.status == "SKIP" and s.key == "setup/raceClass" for s in setup_steps):
        result.status = "SKIP"
        result.fail_locus = "setup/raceClass"
        result.duration_sec = time.time() - started
        return result

    if any(s.status == "FAIL" for s in setup_steps):
        result.status = "FAIL"
        result.fail_locus = next(s.key for s in setup_steps if s.status == "FAIL")
        result.duration_sec = time.time() - started
        return result

    skipped = 0
    failed = False

    for obj in plan.get("objectives") or []:
        seq = int(obj.get("sequence") or 0)
        state = ctx.state()
        # Skip objectives already past
        active = _active(state, mission_id)
        if active and int(active.get("seq") or 0) > seq:
            continue
        if _is_completed(state, mission_id):
            break

        reqs = obj.get("requirements") or []
        if not reqs:
            # Empty objective — try tp+interact once then dialogs
            ctx.step_begin(f"{mission_id}/{seq}/empty", "tp+interact")
            ctx.chat("/tptowaypoint")
            ctx.sleep(0.8)
            ctx.cmd("action activate-or-interact")
            ctx.sleep(0.8)
            empty = StepResult(key=f"{mission_id}/{seq}/empty", status="PASS", detail="tp+interact")
            result.steps.append(empty)
            ctx.step_end(empty)
            result.steps.extend(handle_dialogs(ctx))
            continue

        for req in reqs:
            req_type = str(req.get("type") or "")
            key = f"{mission_id}/{seq}/{req_type}"
            strat = strategy_for(req_type)
            if getattr(strat, "manages_console", False):
                produced = strat.execute(ctx, req, mission_id=mission_id, seq=seq)
                produced_steps = produced if isinstance(produced, list) else [produced]
                result.steps.extend(produced_steps)
                step = _aggregate_step(key, produced_steps)
            else:
                ctx.step_begin(key)
                step = strat.execute(ctx, req, mission_id=mission_id, seq=seq)
                result.steps.append(step)
                ctx.step_end(step)
                # Patrol pads auto-complete without mission UI — avoid MemScan hitches.
                # Deliver owns its own interact + complete dialogs.
                if req_type not in ("Patrol", "Deliver"):
                    result.steps.extend(handle_dialogs(ctx))

            if step.status == "SKIP":
                skipped += 1
                if policy == "strict" and not _is_informational_skip(step):
                    failed = True
                    result.fail_locus = step.key
                    break
            elif step.status == "FAIL":
                failed = True
                result.fail_locus = step.key
                break
            elif step.status == "ERROR":
                failed = True
                result.fail_locus = step.key
                break

            # If mission completed, stop
            if _is_completed(ctx.state(), mission_id):
                break
        if failed or _is_completed(ctx.state(), mission_id):
            break

    # Final dialogs only if the turn-in box may still be up.
    completed = _is_completed(ctx.state(), mission_id)
    if not completed:
        result.steps.extend(handle_dialogs(ctx))
        completed = _is_completed(ctx.state(), mission_id)
    if completed:
        # End state wins: a premature Deliver/verify FAIL (dialog not up yet)
        # is not a failure if we then clicked Complete and the mission finished.
        result.status = "PASS"
        result.fail_locus = ""
    elif failed:
        result.status = "FAIL"
    elif skipped:
        result.status = "PARTIAL" if policy == "partial" else "FAIL"
        if policy == "strict" and not result.fail_locus:
            result.fail_locus = "unsupported_requirements"
    else:
        # Still active with no more auto work
        result.status = "PARTIAL" if policy == "partial" else "FAIL"
        if not result.fail_locus:
            result.fail_locus = "incomplete"

    result.duration_sec = time.time() - started
    if ctx.progress is not None:
        ctx.progress.note(f"result {result.status}" + (f" locus={result.fail_locus}" if result.fail_locus else ""))
    return result


def run_registry(
    ctx: RunContext,
    entries: list[RegistryEntry],
) -> list[MissionResult]:
    results: list[MissionResult] = []
    for entry in entries:
        if entry.timeout_sec:
            ctx.step_timeout_sec = entry.timeout_sec
        results.append(
            run_mission(
                ctx,
                entry.mission_id,
                policy=entry.policy,
                title_hint=entry.notes,
            )
        )
    return results


def _active(state: dict[str, Any], mission_id: int) -> dict[str, Any] | None:
    for q in state.get("activeQuests") or []:
        if int(q.get("missionId") or 0) == mission_id:
            return q
    return None


def _is_completed(state: dict[str, Any], mission_id: int) -> bool:
    return mission_id in {int(x) for x in (state.get("completedMissionIds") or [])}


def _is_informational_skip(step: StepResult) -> bool:
    """SKIP classify-none is not an unsupported requirement."""
    return step.key.startswith("dialog/") and step.detail == "none"


def _aggregate_step(key: str, steps: list[StepResult]) -> StepResult:
    """Collapse multi-step strategies into one status for the runner loop."""
    if not steps:
        return StepResult(key=key, status="ERROR", detail="no steps produced")
    for status in ("ERROR", "FAIL"):
        hit = next((s for s in steps if s.status == status), None)
        if hit is not None:
            return hit
    # Intermediate classify-none SKIPs must not hide a later verify PASS.
    passes = [s for s in steps if s.status == "PASS"]
    if passes:
        return passes[-1]
    skip = next((s for s in steps if s.status == "SKIP"), None)
    if skip is not None:
        return skip
    return steps[-1]

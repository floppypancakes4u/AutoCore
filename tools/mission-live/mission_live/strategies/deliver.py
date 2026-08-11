"""Deliver turn-in: teleport to NPC, interact, complete mission dialog."""

from __future__ import annotations

from typing import Any

from mission_live.strategies.base import RunContext, StepResult
from mission_live.strategies.dialogs import handle_dialogs


class DeliverStrategy:
    """Phase-1 deliver / turn-in.

    Emits separate console/report steps:
      {mission}/{seq}/Deliver/teleport
      {mission}/{seq}/Deliver/interact
      plus dialog/complete from handle_dialogs
      {mission}/{seq}/Deliver/verify
    """

    requirement_type = "Deliver"
    manages_console = True

    def can_handle(self, req: dict[str, Any]) -> bool:
        return str(req.get("type") or "") == "Deliver"

    def execute(self, ctx: RunContext, req: dict[str, Any], *, mission_id: int, seq: int) -> list[StepResult]:
        steps: list[StepResult] = []
        prefix = f"{mission_id}/{seq}/Deliver"

        # --- teleport ---
        tp_key = f"{prefix}/teleport"
        ctx.step_begin(tp_key)
        cbid = int(req.get("npcTargetCbid") or 0)
        try:
            if cbid > 0:
                r = ctx.chat(f"/tptonpc {cbid}", settle_sec=2.0)
                detail = f"tptonpc {cbid}"
            else:
                r = ctx.chat("/tptowaypoint", settle_sec=1.0)
                detail = "tptowaypoint"
            out = str(r.get("output") or "")
            ok = bool(r.get("ok")) and bool(r.get("entered", True))
            tp_step = StepResult(
                key=tp_key,
                status="PASS" if ok else "FAIL",
                detail=detail,
                output=out[-1500:],
            )
        except Exception as ex:  # noqa: BLE001 — strategy boundary
            tp_step = StepResult(key=tp_key, status="ERROR", detail=str(ex))
        ctx.step_end(tp_step)
        steps.append(tp_step)
        if tp_step.status != "PASS":
            return steps

        # --- interact ---
        ix_key = f"{prefix}/interact"
        ctx.step_begin(ix_key)
        try:
            ctx.cmd("action activate-or-interact")
            ctx.sleep(0.45)
            ix_step = StepResult(key=ix_key, status="PASS", detail="activate-or-interact")
        except Exception as ex:  # noqa: BLE001
            ix_step = StepResult(key=ix_key, status="ERROR", detail=str(ex))
        ctx.step_end(ix_step)
        steps.append(ix_step)
        if ix_step.status != "PASS":
            return steps

        # --- complete mission dialog(s) ---
        steps.extend(handle_dialogs(ctx))

        # Sticky turn-in: one more interact + dialogs if still incomplete.
        if not _mission_done(ctx, mission_id):
            retry_key = f"{prefix}/interact#2"
            ctx.step_begin(retry_key)
            try:
                ctx.cmd("action activate-or-interact")
                ctx.sleep(0.45)
                retry = StepResult(key=retry_key, status="PASS", detail="retry interact")
            except Exception as ex:  # noqa: BLE001
                retry = StepResult(key=retry_key, status="ERROR", detail=str(ex))
            ctx.step_end(retry)
            steps.append(retry)
            steps.extend(handle_dialogs(ctx))

        verify_key = f"{prefix}/verify"
        ctx.step_begin(verify_key)
        if _mission_done(ctx, mission_id):
            done = StepResult(key=verify_key, status="PASS", detail=f"mission {mission_id} completed")
        else:
            done = StepResult(
                key=verify_key,
                status="FAIL",
                detail=f"mission {mission_id} still not completed after deliver",
            )
        ctx.step_end(done)
        steps.append(done)
        return steps


def _mission_done(ctx: RunContext, mission_id: int) -> bool:
    state = ctx.state()
    completed = {int(x) for x in (state.get("completedMissionIds") or [])}
    return mission_id in completed

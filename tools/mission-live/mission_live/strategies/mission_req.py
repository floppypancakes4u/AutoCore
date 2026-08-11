"""Mission objective requirement: seed listed mission ids as completed."""

from __future__ import annotations

from typing import Any

from mission_live.strategies.base import RunContext, StepResult


class MissionReqStrategy:
    requirement_type = "Mission"

    def can_handle(self, req: dict[str, Any]) -> bool:
        return str(req.get("type") or "") == "Mission"

    def execute(self, ctx: RunContext, req: dict[str, Any], *, mission_id: int, seq: int) -> StepResult:
        key = f"{mission_id}/{seq}/Mission"
        ids = [int(x) for x in (req.get("missionIds") or []) if int(x) > 0]
        outputs: list[str] = []
        if ids:
            cmd = "/seedcompleted " + " ".join(str(i) for i in ids)
            r = ctx.chat(cmd)
            outputs.append(str(r.get("output") or ""))

        state = ctx.state()
        completed = set(int(x) for x in (state.get("completedMissionIds") or []))
        missing = [i for i in ids if i not in completed]
        status = "PASS" if not missing else "FAIL"
        return StepResult(
            key=key,
            status=status,
            detail=("ok" if not missing else f"missing completed: {missing}"),
            output="\n".join(outputs)[-2000:],
        )

"""Stub for unsupported requirement types."""

from __future__ import annotations

from typing import Any

from mission_live.strategies.base import RunContext, StepResult


class UnsupportedStrategy:
    def __init__(self, req_type: str):
        self.requirement_type = req_type

    def can_handle(self, req: dict[str, Any]) -> bool:
        return True

    def execute(self, ctx: RunContext, req: dict[str, Any], *, mission_id: int, seq: int) -> StepResult:
        t = str(req.get("type") or self.requirement_type or "unknown")
        return StepResult(
            key=f"{mission_id}/{seq}/{t}",
            status="SKIP",
            detail=f"unsupported requirement type in phase 1: {t}",
        )

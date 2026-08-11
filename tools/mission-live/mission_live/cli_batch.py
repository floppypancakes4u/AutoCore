"""Batch-run helpers: resume and soft-fail isolation."""

from __future__ import annotations

from pathlib import Path

from mission_live.report.model import load_results
from mission_live.strategies.base import MissionResult, StepResult


def mission_ids_done(results: list[MissionResult]) -> set[int]:
    return {int(r.mission_id) for r in results}


def should_skip_mission(mission_id: int, done: set[int], *, resume: bool) -> bool:
    return bool(resume) and int(mission_id) in done


def mission_result_from_dict(raw: dict) -> MissionResult:
    steps = []
    for s in raw.get("steps") or []:
        steps.append(
            StepResult(
                key=str(s.get("key") or ""),
                status=str(s.get("status") or ""),
                detail=str(s.get("detail") or ""),
                before=dict(s.get("before") or {}),
                after=dict(s.get("after") or {}),
                output=str(s.get("output") or ""),
            )
        )
    return MissionResult(
        mission_id=int(raw.get("missionId") or raw.get("mission_id") or 0),
        status=str(raw.get("status") or "ERROR"),
        title=str(raw.get("title") or ""),
        force_grant=bool(raw.get("forceGrant") or raw.get("force_grant")),
        seeded_prereqs=list(raw.get("seededPrereqs") or []),
        steps=steps,
        fail_locus=str(raw.get("failLocus") or raw.get("fail_locus") or ""),
        policy=str(raw.get("policy") or "partial"),
        duration_sec=float(raw.get("durationSec") or raw.get("duration_sec") or 0.0),
    )


def load_prior_results(out_dir: Path) -> list[MissionResult]:
    doc = load_results(out_dir / "results.json")
    return [mission_result_from_dict(m) for m in (doc.get("missions") or []) if m]


def error_mission_result(
    mission_id: int,
    *,
    title: str = "",
    policy: str = "partial",
    force_grant: bool = False,
    detail: str,
) -> MissionResult:
    step = StepResult(key="batch/actuator", status="ERROR", detail=detail)
    return MissionResult(
        mission_id=mission_id,
        status="ERROR",
        title=title,
        force_grant=force_grant,
        steps=[step],
        fail_locus="batch/actuator",
        policy=policy,
    )

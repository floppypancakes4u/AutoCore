"""Runner scoring: a completed mission must not FAIL on classify-none."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.runner import _aggregate_step, run_mission
from mission_live.strategies.base import RunContext, StepResult


def test_aggregate_step_prefers_later_pass_over_classify_skip():
    steps = [
        StepResult(key="874/1/Deliver/teleport", status="PASS"),
        StepResult(key="874/1/Deliver/interact", status="PASS"),
        StepResult(key="dialog/classify", status="SKIP", detail="none"),
        StepResult(key="dialog/complete", status="PASS"),
        StepResult(key="874/1/Deliver/verify", status="PASS", detail="mission 874 completed"),
    ]
    agg = _aggregate_step("874/1/Deliver", steps)
    assert agg.status == "PASS"
    assert agg.key.endswith("/verify")


class _CompletedAfterDeliverOracle:
    def __init__(self) -> None:
        self.completed: list[int] = []

    def mission_plan(self, mission_id: int) -> dict[str, Any]:
        return {
            "title": "Crater Run",
            "objectives": [
                {
                    "sequence": 0,
                    "requirements": [{"type": "Patrol", "targetCount": 1, "targets": [1]}],
                },
                {
                    "sequence": 1,
                    "requirements": [{"type": "Deliver", "npcTargetCbid": 3192}],
                },
            ],
        }

    def mission_state(self) -> dict[str, Any]:
        if self.completed:
            return {"activeQuests": [], "completedMissionIds": list(self.completed)}
        return {
            "activeQuests": [{"missionId": 874, "seq": 1, "progress": 0, "max": 1}],
            "completedMissionIds": [],
        }


class _Actuator:
    def __init__(self, oracle: _CompletedAfterDeliverOracle) -> None:
        self.oracle = oracle
        self.cmds: list[str] = []
        self._complete_clicks = 0

    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line.startswith("chat-direct"):
            return {"ok": True, "output": "chat-direct: dispatcher result=1 accepted=1", "entered": True}
        if line == "mission dialog":
            return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
        if line == "mission select":
            return {"ok": True, "output": "mission-select: found=0\nmission-select: count=0\n"}
        if line == "mission complete":
            self._complete_clicks += 1
            if self._complete_clicks == 1:
                self.oracle.completed = [874]
                return {"ok": True, "output": "mission-completion: action found=1 clicked=1\n"}
            return {"ok": True, "output": "mission-completion: action found=0 clicked=0\n"}
        if line == "mission ok":
            return {"ok": True, "output": "mission-current: action found=0 clicked=0\n"}
        if line == "mission active":
            return {"ok": True, "output": 'mission-active: id=874 name="Crater Run"\n'}
        return {"ok": True, "output": "ok"}


def test_run_mission_strict_pass_when_deliver_completes_after_classify_none(monkeypatch):
    from mission_live import runner

    def fake_setup(ctx, mission_id, plan):
        return [StepResult(key="setup/ok", status="PASS")]

    monkeypatch.setattr(runner, "run_setup", fake_setup)

    oracle = _CompletedAfterDeliverOracle()
    actuator = _Actuator(oracle)
    ctx = RunContext(
        actuator=actuator,
        oracle=oracle,
        settle_sec=0.0,
        chat_settle_sec=0.0,
        ui_settle_sec=0.0,
        chat_retry_sec=0.0,
    )
    result = run_mission(ctx, 874, policy="strict", title_hint="Crater Run")
    assert result.status == "PASS", result.to_dict()
    assert result.fail_locus == ""
    assert any(s.key.endswith("/verify") and s.status == "PASS" for s in result.steps)
    # Final classify-none after turn-in must not be a scoring SKIP.
    trailing = [s for s in result.steps if s.key == "dialog/classify"]
    assert all(s.status != "SKIP" for s in trailing)


class _LateCompleteOracle(_CompletedAfterDeliverOracle):
    pass


class _LateCompleteActuator(_Actuator):
    """Complete box appears only after deliver verify has already run."""

    def __init__(self, oracle: _CompletedAfterDeliverOracle) -> None:
        super().__init__(oracle)
        self._dialog_polls = 0

    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line.startswith("chat-direct"):
            return {"ok": True, "output": "chat-direct: dispatcher result=1 accepted=1", "entered": True}
        if line == "mission dialog":
            self._dialog_polls += 1
            # Two deliver classify rounds see none; the box is up for the
            # post-verify drain (live 874: classify none, then complete).
            if self._dialog_polls >= 3:
                return {"ok": True, "output": "mission-dialog: found=1 kind=complete\n"}
            return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
        if line == "mission select":
            return {"ok": True, "output": "mission-select: found=0\nmission-select: count=0\n"}
        if line == "mission complete":
            if self.oracle.completed:
                return {"ok": True, "output": "mission-completion: action found=0 clicked=0\n"}
            # Only honor Complete once the late box is classified.
            if self._dialog_polls >= 3:
                self.oracle.completed = [874]
                return {"ok": True, "output": "mission-completion: action found=1 clicked=1\n"}
            return {"ok": True, "output": "mission-completion: action found=0 clicked=0\n"}
        if line == "mission ok":
            return {"ok": True, "output": "mission-current: action found=0 clicked=0\n"}
        if line == "mission active":
            return {"ok": True, "output": 'mission-active: id=874 name="Crater Run"\n'}
        return {"ok": True, "output": "ok"}


def test_run_mission_pass_when_complete_dialog_arrives_after_verify(monkeypatch):
    from mission_live import runner

    def fake_setup(ctx, mission_id, plan):
        return [StepResult(key="setup/ok", status="PASS")]

    monkeypatch.setattr(runner, "run_setup", fake_setup)

    oracle = _LateCompleteOracle()
    actuator = _LateCompleteActuator(oracle)
    ctx = RunContext(
        actuator=actuator,
        oracle=oracle,
        settle_sec=0.0,
        chat_settle_sec=0.0,
        ui_settle_sec=0.0,
        chat_retry_sec=0.0,
    )
    ctx.dialog_timeout_sec = 0.0
    result = run_mission(ctx, 874, policy="strict", title_hint="Crater Run")
    assert 874 in oracle.completed
    assert result.status == "PASS", result.to_dict()
    assert result.fail_locus == ""
    assert any(s.key == "dialog/complete" and s.status == "PASS" for s in result.steps)

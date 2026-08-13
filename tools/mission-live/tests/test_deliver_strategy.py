"""Deliver turn-in: teleport + interact as separate steps."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.plan import capability_for
from mission_live.strategies.deliver import DeliverStrategy
from mission_live.strategies.base import MissionResult, StepResult


class FakeCtx:
    def __init__(self) -> None:
        self.chats: list[str] = []
        self.cmds: list[str] = []
        self.mission_result = MissionResult(mission_id=3032, status="RUNNING", title="t")
        self._progress = {"3032": 17}
        self._completed: list[int] = []
        self._complete_clicks = 0
        self.ui_settle_sec = 0.01

    def chat(self, text: str, *, settle_sec: float | None = None) -> dict[str, Any]:
        self.chats.append(text)
        return {"ok": True, "output": "ok", "entered": True}

    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line == "mission complete":
            self._complete_clicks += 1
            if self._complete_clicks == 1:
                self._completed = [3032]
                self._progress["3032"] = 19
                return {"ok": True, "output": "mission-completion: action found=1 clicked=1"}
            return {"ok": True, "output": "mission-completion: action found=0 clicked=0"}
        if line == "mission ok":
            return {"ok": True, "output": "mission-current: action found=0 clicked=0"}
        return {"ok": True, "output": "ok"}

    def state(self) -> dict[str, Any]:
        return {
            "progressByMissionId": dict(self._progress),
            "completedMissionIds": list(self._completed),
            "activeMissionIds": [] if self._completed else [3032],
        }

    def sleep(self, seconds: float | None = None) -> None:
        return

    def step_begin(self, key: str, detail: str = "") -> None:
        pass

    def step_end(self, step: StepResult) -> StepResult:
        return step

    def progress(self, detail: str = "") -> None:
        pass


def test_deliver_is_auto_capability():
    assert capability_for("Deliver") == "auto"


def test_deliver_emits_teleport_then_interact_steps():
    ctx = FakeCtx()
    strat = DeliverStrategy()
    req = {"type": "Deliver", "npcTargetCbid": 2477, "npcContinentId": 789}
    steps = strat.execute(ctx, req, mission_id=3032, seq=18)
    assert isinstance(steps, list)
    keys = [s.key for s in steps]
    assert keys[0] == "3032/18/Deliver/teleport"
    assert keys[1] == "3032/18/Deliver/interact"
    assert any(k == "dialog/complete" for k in keys)
    assert any(k.endswith("/verify") for k in keys)
    assert steps[0].status == "PASS"
    assert steps[1].status == "PASS"
    assert any(s.key.endswith("/verify") and s.status == "PASS" for s in steps)
    assert "/tptonpc 2477" in ctx.chats
    assert any("activate-or-interact" in c for c in ctx.cmds)


def test_deliver_falls_back_to_tptowaypoint_without_cbid():
    ctx = FakeCtx()
    strat = DeliverStrategy()
    req = {"type": "Deliver", "npcTargetCbid": 0}
    steps = strat.execute(ctx, req, mission_id=3032, seq=18)
    assert steps[0].status == "PASS"
    assert "/tptowaypoint" in ctx.chats


class LateCompleteCtx(FakeCtx):
    """Turn-in box is missing for the first two classify rounds, then appears."""

    def __init__(self) -> None:
        super().__init__()
        self.dialog_timeout_sec = 0.0
        self._dialog_rounds = 0

    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line == "mission dialog":
            self._dialog_rounds += 1
            if self._dialog_rounds >= 3:
                return {"ok": True, "output": "mission-dialog: found=1 kind=complete\n"}
            return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
        if line == "mission select":
            return {"ok": True, "output": "mission-select: found=0\n"}
        if line == "mission complete":
            if self._dialog_rounds >= 3:
                self._complete_clicks += 1
                self._completed = [3032]
                return {"ok": True, "output": "mission-completion: action found=1 clicked=1"}
            return {"ok": True, "output": "mission-completion: action found=0 clicked=0"}
        if line == "mission ok":
            return {"ok": True, "output": "mission-current: action found=0 clicked=0"}
        return {"ok": True, "output": "ok"}


def test_deliver_waits_for_late_complete_dialog_before_verify():
    ctx = LateCompleteCtx()
    steps = DeliverStrategy().execute(
        ctx, {"type": "Deliver", "npcTargetCbid": 3192}, mission_id=3032, seq=1
    )
    verify = next(s for s in steps if s.key.endswith("/verify"))
    assert verify.status == "PASS", [s.to_dict() for s in steps]
    assert any(s.key == "dialog/complete" and s.status == "PASS" for s in steps)
    assert ctx._completed == [3032]

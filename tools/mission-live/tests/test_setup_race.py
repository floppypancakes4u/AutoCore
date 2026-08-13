"""Setup must gate race/class before any live chat work."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.strategies.base import MissionResult, StepResult
from mission_live.strategies.setup import run_setup


class FakeSetupCtx:
    def __init__(self, state: dict[str, Any], *, force_grant: bool = False) -> None:
        self.chats: list[str] = []
        self.force_grant = force_grant
        self.mission_result = MissionResult(mission_id=1, status="ERROR")
        self.prep_continent = 789
        self._state = state

    def chat(self, text: str, *, settle_sec: float | None = None) -> dict[str, Any]:
        self.chats.append(text)
        return {"ok": True, "output": "ok", "entered": True}

    def state(self) -> dict[str, Any]:
        return dict(self._state)

    def sleep(self, seconds: float | None = None) -> None:
        return

    def step_begin(self, key: str, detail: str = "") -> None:
        pass

    def step_end(self, step: StepResult) -> StepResult:
        return step

    def record_step(self, steps, key, status, detail="", output="", *, began=True):
        if not began:
            self.step_begin(key, detail)
        step = StepResult(key=key, status=status, detail=detail, output=output)
        steps.append(step)
        return self.step_end(step)

    def wait_until(self, predicate, timeout=None, interval=0.5) -> bool:
        return predicate(self.state())


def test_setup_skips_race_mismatch_without_any_chat():
    ctx = FakeSetupCtx({"hasBody": True, "race": 0, "class": 0, "continentId": 789})
    steps = run_setup(
        ctx,
        248,
        {"reqRace": 1, "reqClass": -1, "continent": 694, "npc": 1923, "reqLevelMin": 10},
    )
    assert ctx.chats == []
    assert steps[0].key == "setup/raceClass"
    assert steps[0].status == "SKIP"
    assert "reqRace" in steps[0].detail

"""Patrol strategy: advance once pose/progress verifies."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.strategies.patrol import PatrolStrategy
from mission_live.strategies.base import StepResult


class FakeCtx:
    def __init__(self) -> None:
        self.chats: list[str] = []
        self.cmds: list[str] = []
        self._progress = 0
        self._pos = (0.0, 0.0, 0.0)
        self.chat_calls = 0

    def chat(self, text: str, *, settle_sec: float | None = None) -> dict[str, Any]:
        self.chats.append(text)
        self.chat_calls += 1
        # Teleport snaps pose immediately; progress credits on next state poll.
        self._pos = (100.0 * self.chat_calls, 0.0, 0.0)
        self._progress = self.chat_calls
        return {"ok": True, "output": "ok", "entered": True}

    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line == "player position":
            x, y, z = self._pos
            return {
                "ok": True,
                "output": f"player: vehicle x={x} y={y} z={z}\nplayer: character x={x} y={y} z={z}",
            }
        return {"ok": True, "output": "ok"}

    def state(self) -> dict[str, Any]:
        return {
            "activeQuests": [
                {"missionId": 3032, "seq": 0, "progress": self._progress, "max": 18},
            ],
            "completedMissionIds": [],
        }

    def sleep(self, seconds: float | None = None) -> None:
        return

    def step_begin(self, key: str, detail: str = "") -> None:
        pass

    def step_end(self, step: StepResult) -> StepResult:
        return step


def test_patrol_passes_after_pose_without_fixed_settle():
    ctx = FakeCtx()
    step = PatrolStrategy().execute(ctx, {"type": "Patrol"}, mission_id=3032, seq=0)
    assert step.status == "PASS"
    assert step.detail == "attempts=1"
    assert ctx.chats == ["/tptowaypoint"]
    # Progress credited from teleport — interact not required.
    assert not any("activate-or-interact" in c for c in ctx.cmds)


class MultiPadFakeCtx(FakeCtx):
    def __init__(self) -> None:
        super().__init__()
        self._progress = 0
        self._max = 5
        self._completed = False

    def chat(self, text: str, *, settle_sec: float | None = None) -> dict[str, Any]:
        self.chats.append(text)
        self.chat_calls += 1
        self._pos = (100.0 * self.chat_calls, 0.0, 0.0)
        if self._progress < self._max:
            self._progress += 1
        if self._progress >= self._max:
            self._completed = True
        return {"ok": True, "output": "ok", "entered": True}

    def state(self) -> dict[str, Any]:
        if self._completed and self._progress >= self._max:
            # Last pad completes the objective (quest leaves active).
            if self.chat_calls >= self._max:
                return {"activeQuests": [], "completedMissionIds": [874]}
        return {
            "activeQuests": [
                {"missionId": 874, "seq": 0, "progress": self._progress, "max": self._max},
            ],
            "completedMissionIds": [],
        }


def test_patrol_walks_every_multi_pad_without_interact():
    ctx = MultiPadFakeCtx()
    req = {
        "type": "Patrol",
        "targetCount": 5,
        "targets": [74751, 74752, 74753, 74754, 74755],
    }
    step = PatrolStrategy().execute(ctx, req, mission_id=874, seq=0)
    assert step.status == "PASS"
    assert ctx.chats == ["/tptowaypoint"] * 5
    assert not any("activate-or-interact" in c for c in ctx.cmds)


def test_parse_mission_patrol_line():
    from mission_live.strategies.patrol import parse_mission_patrol

    parsed = parse_mission_patrol(
        'mission-patrol: id=874 seq=0 progress=2 max=5 next=74753'
    )
    assert parsed == {
        "id": 874,
        "seq": 0,
        "progress": 2,
        "max": 5,
        "next": 74753,
    }
    assert parse_mission_patrol("") is None
    assert parse_mission_patrol("mission-active: id=874 name=\"Crater Run\"") is None

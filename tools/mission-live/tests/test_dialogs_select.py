"""Multi-mission picker dialog (kind=select) — click a row, then accept."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.strategies.base import (
    MissionResult,
    StepResult,
    dialog_found,
    dialog_kind,
)
from mission_live.strategies.dialogs import accept_mission_from_npc, handle_dialogs


class SelectFakeCtx:
    def __init__(self, mission_id: int = 703) -> None:
        self.cmds: list[str] = []
        self.ui_settle_sec = 0.01
        self.mission_result = MissionResult(mission_id=mission_id, status="RUNNING")
        self._mission_id = mission_id
        self._phase = "select"
        self._picked = False
        self._accepted = False

    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line == "mission dialog":
            if self._phase == "select":
                return {
                    "ok": True,
                    "output": (
                        "mission-dialog: found=1 kind=select\n"
                        "mission-select: count=2\n"
                        'mission-select: id=703 cmd=0x9C4F name="[12] Crush Groove"\n'
                        'mission-select: id=771 cmd=0x9C50 name="[13] Other Job"\n'
                    ),
                }
            if self._phase == "new":
                return {"ok": True, "output": "mission-dialog: found=1 kind=new\n"}
            return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
        if line.startswith("mission pick"):
            want = line.split(None, 2)[-1] if line.split()[1:] else ""
            if want in ("703", "Crush Groove", "[12] Crush Groove"):
                self._picked = True
                self._phase = "new"
                return {
                    "ok": True,
                    "output": "mission-select: action found=1 clicked=1 id=703\n",
                }
            return {"ok": True, "output": "mission-select: action found=0 clicked=0\n"}
        if line == "mission accept":
            if self._phase == "new":
                self._accepted = True
                self._phase = "done"
                return {"ok": True, "output": "mission: action found=1 clicked=1\n"}
            return {"ok": True, "output": "mission: action found=0 clicked=0\n"}
        if line == "mission complete":
            return {"ok": True, "output": "mission-completion: action found=0 clicked=0\n"}
        if line == "mission ok":
            return {"ok": True, "output": "mission-current: action found=0 clicked=0\n"}
        if line == "mission active":
            if self._accepted:
                return {
                    "ok": True,
                    "output": 'mission-active: id=703 name="Crush Groove"\n',
                }
            return {"ok": True, "output": 'mission-active: id=0 name=""\n'}
        if line == "action activate-or-interact":
            self._phase = "select"
            return {"ok": True, "output": "ok"}
        return {"ok": True, "output": "ok"}

    def state(self) -> dict[str, Any]:
        return {"activeQuests": [{"missionId": 703}] if self._accepted else []}

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


def test_dialog_kind_parses_select():
    assert dialog_kind({"output": "mission-dialog: found=1 kind=select\n"}) == "select"
    assert dialog_kind({"output": "mission-dialog: found=0 kind=select\n"}) == "none"
    assert dialog_kind({
        "output": (
            "mission-dialog: found=0 kind=none\n"
            "mission-select: count=2\n"
            'mission-select: id=703 cmd=0x9C4F name="[12] Crush Groove"\n'
        )
    }) == "select"
    assert dialog_found(
        {"output": "mission-select: action found=1 clicked=1 id=703\n"},
        kind="select",
    )


def test_accept_picks_matching_row_then_accepts():
    ctx = SelectFakeCtx(703)
    steps: list[StepResult] = []
    ok = accept_mission_from_npc(ctx, steps, mission_id=703, rounds=1)
    assert ok is True
    assert "mission pick 703" in ctx.cmds
    assert "mission accept" in ctx.cmds
    pick_steps = [
        s for s in steps
        if "missionPick" in s.key or s.key.endswith("/pick") or s.key.endswith("/select")
    ]
    assert any(s.status == "PASS" for s in pick_steps)


def test_handle_dialogs_picks_select_list_for_current_mission():
    ctx = SelectFakeCtx(703)
    results = handle_dialogs(ctx)
    assert "mission pick 703" in ctx.cmds
    assert any(
        s.key in ("dialog/pick", "dialog/select") and s.status == "PASS" for s in results
    )


def test_handle_dialogs_identifies_select_before_any_click():
    ctx = SelectFakeCtx(703)
    results = handle_dialogs(ctx)
    mission_cmds = [c for c in ctx.cmds if c.startswith("mission ")]
    assert mission_cmds[0] == "mission dialog"
    assert "mission pick 703" in mission_cmds
    assert mission_cmds.index("mission dialog") < mission_cmds.index("mission pick 703")

    classify = next(s for s in results if s.key == "dialog/classify")
    select = next(s for s in results if s.key in ("dialog/pick", "dialog/select"))
    assert classify.detail == "select"
    assert classify.status == "PASS"
    assert results.index(classify) < results.index(select)


def test_accept_identifies_dialog_after_interact_before_pick():
    ctx = SelectFakeCtx(703)
    steps: list[StepResult] = []
    accept_mission_from_npc(ctx, steps, mission_id=703, rounds=1)
    interact_i = ctx.cmds.index("action activate-or-interact")
    after = ctx.cmds[interact_i + 1 :]
    mission_cmds = [c for c in after if c.startswith("mission ")]
    assert mission_cmds, "expected a mission command after interact"
    assert mission_cmds[0] == "mission dialog"
    assert "mission pick 703" in mission_cmds
    assert mission_cmds.index("mission dialog") < mission_cmds.index("mission pick 703")
    classify = next(s for s in steps if s.key == "dialog/classify")
    assert classify.detail == "select"
    assert classify.status == "PASS"
    assert any(s.key == "dialog/select" and s.status == "PASS" for s in steps)


class LegacySelectFakeCtx(SelectFakeCtx):
    """Old DevTool: mission dialog misses the picker; mission select lists it."""

    def cmd(self, line: str) -> dict[str, Any]:
        if line == "mission dialog":
            self.cmds.append(line)
            if self._phase == "select":
                return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
            if self._phase == "new":
                return {"ok": True, "output": "mission-dialog: found=1 kind=new\n"}
            return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
        if line == "mission select" or line == "mission select status":
            self.cmds.append(line)
            if self._phase == "select":
                return {
                    "ok": True,
                    "output": (
                        "mission-select: found=1\n"
                        "mission-select: count=2\n"
                        'mission-select: id=703 cmd=0x9C4F name="[12] Crush Groove"\n'
                        'mission-select: id=771 cmd=0x9C50 name="[13] Other Job"\n'
                    ),
                }
            return {"ok": True, "output": "mission-select: found=0\nmission-select: count=0\n"}
        return super().cmd(line)


def test_classify_uses_mission_select_when_dialog_kind_is_none():
    ctx = LegacySelectFakeCtx(703)
    results = handle_dialogs(ctx)
    classify = next(s for s in results if s.key == "dialog/classify")
    assert classify.status == "PASS"
    assert classify.detail == "select"
    assert "mission select" in ctx.cmds
    assert any(s.key == "dialog/select" and s.status == "PASS" for s in results)
    assert classify.status != "SKIP"


class EmptyDialogCtx(SelectFakeCtx):
    def cmd(self, line: str) -> dict[str, Any]:
        self.cmds.append(line)
        if line in ("mission dialog", "mission select", "mission select status"):
            return {"ok": True, "output": "mission-dialog: found=0 kind=none\n"}
        if line == "mission complete":
            return {"ok": True, "output": "mission-completion: action found=0 clicked=0\n"}
        if line == "mission ok":
            return {"ok": True, "output": "mission-current: action found=0 clicked=0\n"}
        return {"ok": True, "output": "ok"}


def test_identify_dialog_none_is_pass_not_skip():
    from mission_live.strategies.dialogs import identify_dialog

    ctx = EmptyDialogCtx(874)
    results: list[StepResult] = []
    kind, _ = identify_dialog(ctx, results, timeout=0.0)
    assert kind == "none"
    classify = results[0]
    assert classify.key == "dialog/classify"
    assert classify.detail == "none"
    assert classify.status == "PASS", "no open box is a successful classify, not a SKIP"

"""Unit tests for mission_live (no live client)."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.plan import annotate_plan, capability_for, race_class_eligible
from mission_live.report.model import summarize
from mission_live.strategies.base import MissionResult as MR
from mission_live.strategies.unsupported import UnsupportedStrategy


def test_capability_matrix():
    assert capability_for("Patrol") == "auto"
    assert capability_for("Mission") == "auto"
    assert capability_for("Deliver") == "auto"
    assert capability_for("Kill") == "unsupported"


def test_annotate_plan():
    plan = {
        "missionId": 1,
        "objectives": [
            {
                "sequence": 0,
                "requirements": [{"type": "Patrol"}, {"type": "Kill"}],
            }
        ],
    }
    out = annotate_plan(plan)
    assert out["supportedRequirementCount"] == 1
    assert out["unsupportedRequirementCount"] == 1
    assert out["objectives"][0]["requirements"][0]["capability"] == "auto"
    assert out["objectives"][0]["requirements"][1]["capability"] == "unsupported"


def test_race_class_unrestricted():
    ok, reason = race_class_eligible({"reqRace": -1, "reqClass": -1}, {})
    assert ok and reason == ""


def test_race_class_mismatch_skips():
    ok, reason = race_class_eligible(
        {"reqRace": 1, "reqClass": -1},
        {"hasBody": True, "race": 0, "class": 0},
    )
    assert not ok
    assert "reqRace" in reason


def test_race_class_match():
    ok, _ = race_class_eligible(
        {"reqRace": 1, "reqClass": 2},
        {"hasBody": True, "race": 1, "class": 2},
    )
    assert ok


def test_unsupported_strategy_skips():
    step = UnsupportedStrategy("Kill").execute(
        ctx=None,  # type: ignore[arg-type]
        req={"type": "Kill"},
        mission_id=9,
        seq=0,
    )
    assert step.status == "SKIP"
    assert step.key == "9/0/Kill"


def test_summarize_counts():
    results = [
        MR(mission_id=1, status="PASS"),
        MR(mission_id=2, status="PARTIAL"),
        MR(mission_id=3, status="FAIL"),
    ]
    s = summarize(results)
    assert s["PASS"] == 1
    assert s["PARTIAL"] == 1
    assert s["FAIL"] == 1
    assert s["total"] == 3


def test_live_step_printer_rewrites_same_line():
    from io import StringIO

    from mission_live.console import LiveStepPrinter

    buf = StringIO()
    printer = LiveStepPrinter(stream=buf, enabled=True)
    printer.begin("setup/warp", "789")
    printer.end("PASS", "789")
    out = buf.getvalue()
    assert "setup/warp" in out
    assert "PASS" in out
    assert "\r" in out
    assert "+" in out and "s]" in out  # elapsed timestamp


def test_live_step_printer_pipe_mode_one_line_per_step():
    from io import StringIO

    from mission_live.console import LiveStepPrinter

    buf = StringIO()
    printer = LiveStepPrinter(stream=buf, enabled=False)
    printer.begin("plan", "3032")
    printer.end("FAIL", "refused")
    lines = [ln for ln in buf.getvalue().splitlines() if ln.strip()]
    assert len(lines) == 1
    assert "FAIL" in lines[0]
    assert "plan" in lines[0]
    assert "+" in lines[0] and "s]" in lines[0]


def test_parse_active_mission_id():
    from mission_live.strategies.dialogs import parse_active_mission_id

    assert parse_active_mission_id('mission-active: id=3032 name="Live and Direct"') == 3032
    assert parse_active_mission_id("mission-active: id=0 name=\"\"") == 0
    assert parse_active_mission_id("") == 0


def test_chat_was_accepted_detects_busy_reject():
    from mission_live.strategies.base import action_clicked, chat_was_accepted, dialog_found

    assert chat_was_accepted({"ok": 1, "output": "chat: command accepted=0\n"}) is False
    assert chat_was_accepted({"ok": 1, "output": "chat: queued length=9 slash=1\nchat: command accepted=1\n"}) is True
    # chat-direct: dispatcher often returns 0; still counts as entered.
    assert chat_was_accepted({
        "ok": 1,
        "output": "chat-direct: dispatcher result=0x00000000 slash=1 channel=10\nchat-direct: command accepted=0\n",
    }) is True
    assert chat_was_accepted({
        "ok": 1,
        "output": "chat-direct: player state not available (not in gameplay)\nchat-direct: command accepted=0\n",
    }) is False
    assert chat_was_accepted({"ok": 1, "output": "chat-direct: command accepted=1\n"}) is True
    assert action_clicked({"ok": 1, "output": "mission: action found=0 clicked=0\n"}) is False
    assert action_clicked({"ok": 1, "output": "mission: action found=1 clicked=1\n"}) is True
    assert dialog_found({"output": "mission: new-dialog found=1\n"}, kind="new") is True
    assert dialog_found({"output": "mission-completion: found=0\n"}, kind="completion") is False

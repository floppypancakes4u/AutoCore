"""Tests for soft actuator failures and resume helpers."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.cli_batch import load_prior_results, mission_ids_done, should_skip_mission
from mission_live.strategies.base import MissionResult


def test_resume_skips_completed_ids():
    prior = [
        MissionResult(mission_id=248, status="FAIL"),
        MissionResult(mission_id=369, status="PASS"),
    ]
    done = mission_ids_done(prior)
    assert done == {248, 369}
    assert should_skip_mission(248, done, resume=True)
    assert not should_skip_mission(408, done, resume=True)
    assert not should_skip_mission(248, done, resume=False)


def test_load_prior_results_roundtrip(tmp_path: Path):
    from mission_live.report.model import write_results

    write_results(
        [MissionResult(mission_id=1, status="PASS", title="a")],
        out_dir=tmp_path,
    )
    loaded = load_prior_results(tmp_path)
    assert len(loaded) == 1
    assert loaded[0].mission_id == 1
    assert loaded[0].status == "PASS"

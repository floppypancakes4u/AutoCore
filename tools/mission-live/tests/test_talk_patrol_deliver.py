"""Tests for talk → optional patrol → speak-only deliver filter."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.talk_patrol_deliver import (
    filter_talk_patrol_deliver,
    is_talk_patrol_deliver,
    patrol_count,
)


def _mission(**kwargs):
    base = {
        "id": 1,
        "title": "t",
        "npcGiverCbid": 100,
        "objectives": [],
    }
    base.update(kwargs)
    return base


def test_live_and_direct_shape_matches():
    m = _mission(
        id=3032,
        objectives=[
            {"requirements": [{"type": "patrol"}]},
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 0,
                        "npcTargetCbid": 2468,
                        "npcTargetCompletes": True,
                    }
                ]
            },
        ],
    )
    assert is_talk_patrol_deliver(m)
    assert patrol_count(m) == 1


def test_zero_patrol_talk_to_turnin_matches():
    m = _mission(
        objectives=[
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 0,
                        "npcTargetCbid": 50,
                        "npcTargetCompletes": True,
                    }
                ]
            }
        ]
    )
    assert is_talk_patrol_deliver(m)
    assert patrol_count(m) == 0


def test_rejects_kill_or_cargo_or_missing_giver():
    kill = _mission(
        objectives=[
            {"requirements": [{"type": "kill"}]},
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 0,
                        "npcTargetCbid": 1,
                        "npcTargetCompletes": True,
                    }
                ]
            },
        ]
    )
    cargo = _mission(
        objectives=[
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 3,
                        "npcTargetCbid": 1,
                        "npcTargetCompletes": True,
                    }
                ]
            }
        ]
    )
    no_giver = _mission(
        npcGiverCbid=0,
        objectives=[
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 0,
                        "npcTargetCbid": 1,
                        "npcTargetCompletes": True,
                    }
                ]
            }
        ],
    )
    assert not is_talk_patrol_deliver(kill)
    assert not is_talk_patrol_deliver(cargo)
    assert not is_talk_patrol_deliver(no_giver)


def test_filter_keeps_only_matches():
    good = _mission(
        id=10,
        objectives=[
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 0,
                        "npcTargetCbid": 9,
                        "npcTargetCompletes": True,
                    }
                ]
            }
        ],
    )
    bad = _mission(id=11, objectives=[{"requirements": [{"type": "kill"}]}])
    out = filter_talk_patrol_deliver([good, bad])
    assert [m["id"] for m in out] == [10]

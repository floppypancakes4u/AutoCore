"""Exclusive catalog category partition (travel / cargo / combat / collect / other)."""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.categories import (
    CATEGORIES,
    category_for,
    filter_catalog,
    sort_missions,
)


def _mission(mid: int = 1, **kwargs):
    base = {
        "id": mid,
        "title": "t",
        "npcGiverCbid": 100,
        "continent": 0,
        "reqLevelMin": 0,
        "objectives": [],
    }
    base.update(kwargs)
    return base


def _deliver(*, cargo: bool = False, npc: int = 50) -> dict:
    if cargo:
        return {
            "type": "deliver",
            "numToDeliver": 3,
            "itemCbid": 9001,
            "npcTargetCbid": npc,
            "npcTargetCompletes": True,
        }
    return {
        "type": "deliver",
        "numToDeliver": 0,
        "npcTargetCbid": npc,
        "npcTargetCompletes": True,
    }


def test_talk_patrol_speak_only_is_travel():
    m = _mission(
        3032,
        title="Live and Direct",
        objectives=[
            {"requirements": [{"type": "patrol"}]},
            {"requirements": [_deliver()]},
        ],
    )
    assert category_for(m) == "travel"


def test_item_hand_in_is_cargo():
    m = _mission(objectives=[{"requirements": [_deliver(cargo=True)]}])
    assert category_for(m) == "cargo"


def test_kill_plus_deliver_is_combat_not_travel():
    m = _mission(
        objectives=[
            {"requirements": [{"type": "kill"}]},
            {"requirements": [_deliver()]},
        ]
    )
    assert category_for(m) == "combat"


def test_kill_aggregate_is_combat():
    m = _mission(objectives=[{"requirements": [{"type": "kill_aggregate"}]}])
    assert category_for(m) == "combat"


def test_collect_only_is_collect():
    m = _mission(objectives=[{"requirements": [{"type": "collect"}]}])
    assert category_for(m) == "collect"


def test_useitem_no_giver_and_empty_are_other():
    useitem = _mission(objectives=[{"requirements": [{"type": "useitem"}]}])
    no_giver = _mission(npcGiverCbid=0, objectives=[{"requirements": [_deliver()]}])
    empty = _mission(objectives=[])
    assert category_for(useitem) == "other"
    assert category_for(no_giver) == "other"
    assert category_for(empty) == "other"


def test_collect_plus_cargo_is_collect_not_cargo():
    m = _mission(
        objectives=[
            {"requirements": [{"type": "collect"}]},
            {"requirements": [_deliver(cargo=True)]},
        ]
    )
    assert category_for(m) == "collect"


def test_partition_is_exclusive_and_complete():
    missions = [
        _mission(1, objectives=[{"requirements": [_deliver()]}]),
        _mission(2, objectives=[{"requirements": [_deliver(cargo=True)]}]),
        _mission(3, objectives=[{"requirements": [{"type": "kill"}]}]),
        _mission(4, objectives=[{"requirements": [{"type": "collect"}]}]),
        _mission(5, objectives=[{"requirements": [{"type": "escort"}]}]),
    ]
    assigned = {m["id"]: category_for(m) for m in missions}
    assert assigned == {1: "travel", 2: "cargo", 3: "combat", 4: "collect", 5: "other"}
    seen: set[int] = set()
    for name in CATEGORIES:
        ids = [m["id"] for m in filter_catalog(missions, name)]
        assert not (set(ids) & seen)
        seen.update(ids)
    assert seen == {1, 2, 3, 4, 5}


def test_is_cargo_deliver_edge_cases():
    from mission_live.categories import is_cargo_deliver

    assert is_cargo_deliver({"type": "patrol"}) is False
    assert is_cargo_deliver({"type": "deliver", "numToDeliver": "nope", "itemCbid": "x"}) is False
    assert is_cargo_deliver({"type": "deliver", "itemCbid": None}) is False


def test_cargo_via_item_cbid_alias_and_bad_giver_is_other():
    cargo = _mission(
        objectives=[
            {
                "requirements": [
                    {
                        "type": "deliver",
                        "numToDeliver": 0,
                        "CBIDItem": 44,
                        "npcTargetCbid": 1,
                        "npcTargetCompletes": True,
                    }
                ]
            }
        ]
    )
    assert category_for(cargo) == "cargo"
    patrol_only = _mission(objectives=[{"requirements": [{"type": "patrol"}]}])
    assert category_for(patrol_only) == "other"
    bad_giver = _mission(npcGiverCbid="x", objectives=[{"requirements": [_deliver(cargo=True)]}])
    assert category_for(bad_giver) == "other"


def test_sort_key_tolerates_garbage_fields():
    from mission_live.categories import sort_key

    assert sort_key({"continent": "na", "reqLevelMin": "x", "id": "nope"}) == (0, 0, 0)


def test_filter_catalog_rejects_unknown_name():
    import pytest

    with pytest.raises(ValueError, match="unknown category"):
        filter_catalog([], "sidequest")


def test_sort_missions_by_continent_then_level_then_id():
    missions = [
        _mission(9, continent=2, reqLevelMin=1),
        _mission(3, continent=1, reqLevelMin=10),
        _mission(4, continent=1, reqLevelMin=1),
        _mission(2, continent=1, reqLevelMin=1),
    ]
    ordered = sort_missions(missions)
    assert [m["id"] for m in ordered] == [2, 4, 3, 9]

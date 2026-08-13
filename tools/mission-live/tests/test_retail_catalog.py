"""Retail catalog load: GLM deliver fields + JSON fallback."""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.retail import CatalogError, parse_glm_mission, load_retail_catalog


GLM_CARGO = b"""<Mission ID="42" name="cargo_sample">
  <Title>Haul Freight</Title>
  <Internal></Internal>
  <CoreMission>0</CoreMission>
  <Objective ID="1" sequence="0">
    <Title>Deliver crates</Title>
    <Requirement type="deliver" slot="0">
      <CBIDItem>9001</CBIDItem>
      <NumToDeliver>3</NumToDeliver>
      <TargetNPCCBID>77</TargetNPCCBID>
      <NPCTargetCompletes>1</NPCTargetCompletes>
    </Requirement>
  </Objective>
</Mission>
"""

GLM_TRAVEL = b"""<Mission ID="3032" name="live_and_direct">
  <Title>Live and Direct</Title>
  <Objective ID="1" sequence="0">
    <Requirement type="patrol" slot="0" />
  </Objective>
  <Objective ID="2" sequence="1">
    <Requirement type="deliver" slot="0">
      <CBIDItem>-1</CBIDItem>
      <NumToDeliver>0</NumToDeliver>
      <TargetNPCCBID>2468</TargetNPCCBID>
      <NPCTargetCompletes>1</NPCTargetCompletes>
    </Requirement>
  </Objective>
</Mission>
"""


def test_parse_glm_rejects_bad_xml_and_missing_id():
    assert parse_glm_mission(b"<notxml") is None
    assert parse_glm_mission(b"<Mission name='x'></Mission>") is None
    assert parse_glm_mission(b"<Mission ID='abc'></Mission>") is None


def test_parse_glm_deliver_extracts_cargo_fields():
    parsed = parse_glm_mission(GLM_CARGO)
    assert parsed is not None
    assert parsed["id"] == 42
    assert parsed["title"] == "Haul Freight"
    req = parsed["objectives"][0]["requirements"][0]
    assert req["type"] == "deliver"
    assert req["numToDeliver"] == 3
    assert req["itemCbid"] == 9001
    assert req["npcTargetCbid"] == 77
    assert req["npcTargetCompletes"] is True


def test_parse_glm_speak_only_deliver_has_no_item():
    parsed = parse_glm_mission(GLM_TRAVEL)
    assert parsed is not None
    req = parsed["objectives"][1]["requirements"][0]
    assert req["type"] == "deliver"
    assert req["numToDeliver"] == 0
    assert req["itemCbid"] == -1
    assert req["npcTargetCbid"] == 2468


def test_load_retail_catalog_from_id_keyed_json(tmp_path: Path):
    payload = {"missions": {"8": {"title": "Keyed", "npcGiverCbid": 3}}}
    path = tmp_path / "missions.json"
    path.write_text(json.dumps(payload), encoding="utf-8")
    catalog = load_retail_catalog(json_path=path, glm_path=tmp_path / "missing.glm")
    assert catalog[8]["id"] == 8
    assert catalog[8]["title"] == "Keyed"


def test_load_retail_catalog_from_json(tmp_path: Path):
    payload = {
        "missions": [
            {
                "id": 7,
                "title": "From JSON",
                "npcGiverCbid": 11,
                "continent": 694,
                "reqLevelMin": 2,
                "objectives": [{"requirements": [{"type": "collect"}]}],
            }
        ]
    }
    path = tmp_path / "missions.json"
    path.write_text(json.dumps(payload), encoding="utf-8")
    catalog = load_retail_catalog(json_path=path, glm_path=tmp_path / "missing.glm")
    assert 7 in catalog
    assert catalog[7]["title"] == "From JSON"
    assert catalog[7]["npcGiverCbid"] == 11


def test_load_retail_catalog_from_glm_file(tmp_path: Path):
    glm = tmp_path / "missions.glm"
    glm.write_bytes(GLM_CARGO)
    catalog = load_retail_catalog(json_path=tmp_path / "no.json", glm_path=glm)
    assert 42 in catalog
    req = catalog[42]["objectives"][0]["requirements"][0]
    assert req["numToDeliver"] == 3
    assert req["itemCbid"] == 9001


def test_merge_wad_fields_sets_giver_continent_level():
    from mission_live.retail import merge_wad_fields

    mission = parse_glm_mission(GLM_TRAVEL)
    assert mission is not None
    merge_wad_fields(
        mission,
        {"npc": 1923, "continent": 694, "reqRace": 1, "reqClass": -1, "reqLevelMin": 4},
    )
    assert mission["npcGiverCbid"] == 1923
    assert mission["continent"] == 694
    assert mission["reqLevelMin"] == 4
    assert mission["reqRace"] == 1


def _wad_record(mid: int, name: str, npc: int, continent: int, level: int) -> bytes:
    import struct

    buf = bytearray(320)
    struct.pack_into("<i", buf, 0, mid)
    encoded = name.encode("utf-16-le")
    buf[4 : 4 + len(encoded)] = encoded
    base = 4 + 130 + 2
    struct.pack_into("<i", buf, base, npc)
    struct.pack_into("<h", buf, base + 8, 1)
    struct.pack_into("<h", buf, base + 10, -1)
    struct.pack_into("<i", buf, base + 12, level)
    p = base + 20 + 16 + 2 + 2
    p += 16 + 16 + 16 + 8 + 16 + 4
    struct.pack_into("<i", buf, p, continent)
    return bytes(buf)


def test_apply_wad_merges_all_catalog_ids_in_one_scan(tmp_path: Path):
    from mission_live.retail import apply_wad

    catalog = {
        42: parse_glm_mission(GLM_CARGO),
        3032: parse_glm_mission(GLM_TRAVEL),
    }
    wad = tmp_path / "clonebase.wad"
    wad.write_bytes(_wad_record(42, "cargo_sample", 11, 100, 3) + _wad_record(3032, "live_and_direct", 22, 694, 1))
    apply_wad(catalog, wad)
    assert catalog[42]["npcGiverCbid"] == 11
    assert catalog[42]["continent"] == 100
    assert catalog[3032]["npcGiverCbid"] == 22
    assert catalog[3032]["reqLevelMin"] == 1


def test_load_catalog_helpers_and_optional_empty(tmp_path: Path):
    from mission_live.catalog import load_catalog, requirement_types_in_catalog_entry, repo_root
    from mission_live.retail import default_missions_glm, default_missions_json, load_retail_catalog

    assert repo_root().name in {"AutoCore", "src"} or (repo_root() / "tools").exists()
    path = tmp_path / "missions.json"
    path.write_text(
        json.dumps({"missions": [{"id": 1, "objectives": [{"requirements": [{"type": "Patrol"}]}]}]}),
        encoding="utf-8",
    )
    loaded = load_catalog(path)
    assert 1 in loaded
    monkeypatch_cat = pytest.MonkeyPatch()
    monkeypatch_cat.setattr(
        "mission_live.catalog.load_retail_catalog",
        lambda **k: {9: {"id": 9}} if k.get("required") is False else {},
    )
    try:
        assert 9 in load_catalog()
    finally:
        monkeypatch_cat.undo()
    assert requirement_types_in_catalog_entry(loaded[1]) == ["Patrol"]
    empty = load_retail_catalog(
        json_path=tmp_path / "no.json",
        glm_path=tmp_path / "no.glm",
        wad_path=tmp_path / "no.wad",
        required=False,
    )
    assert empty == {}
    monkey_glm = tmp_path / "custom.glm"
    monkeypatch = pytest.MonkeyPatch()
    monkeypatch.setenv("MISSION_LIVE_GLM", str(monkey_glm))
    monkeypatch.setenv("MISSION_LIVE_CATALOG", str(path))
    try:
        assert default_missions_glm() == monkey_glm
        assert default_missions_json() == path
    finally:
        monkeypatch.undo()


def test_load_retail_catalog_missing_sources_raises(tmp_path: Path):
    with pytest.raises(CatalogError, match="missions.json|missions.glm"):
        load_retail_catalog(
            json_path=tmp_path / "no.json",
            glm_path=tmp_path / "no.glm",
            wad_path=tmp_path / "no.wad",
        )

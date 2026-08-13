"""CLI: categories, list, run --category, interactive menu."""

from __future__ import annotations

import io
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from mission_live.cli import main
from mission_live.strategies.base import MissionResult


def _mission(mid: int, *, kind: str, continent: int = 1, level: int = 0) -> dict:
    if kind == "travel":
        reqs = [
            {"type": "deliver", "numToDeliver": 0, "npcTargetCbid": 50, "npcTargetCompletes": True}
        ]
    elif kind == "cargo":
        reqs = [
            {
                "type": "deliver",
                "numToDeliver": 2,
                "itemCbid": 9,
                "npcTargetCbid": 50,
                "npcTargetCompletes": True,
            }
        ]
    elif kind == "combat":
        reqs = [{"type": "kill"}]
    elif kind == "collect":
        reqs = [{"type": "collect"}]
    else:
        reqs = [{"type": "useitem"}]
    return {
        "id": mid,
        "title": f"{kind}-{mid}",
        "npcGiverCbid": 100 if kind != "other" else 0,
        "continent": continent,
        "reqLevelMin": level,
        "objectives": [{"requirements": reqs}],
    }


FAKE_CATALOG = {
    10: _mission(10, kind="travel", continent=2, level=5),
    11: _mission(11, kind="travel", continent=1, level=1),
    20: _mission(20, kind="cargo", continent=3, level=0),
    21: _mission(21, kind="cargo", continent=1, level=0),
    30: _mission(30, kind="combat"),
    40: _mission(40, kind="collect"),
    50: _mission(50, kind="other"),
}


def _patch_catalog(monkeypatch):
    monkeypatch.setattr(
        "mission_live.cli.load_retail_catalog",
        lambda **_k: FAKE_CATALOG,
    )


def test_categories_prints_names_and_counts(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    rc = main(["categories"])
    assert rc == 0
    out = capsys.readouterr().out
    for name in ("travel", "cargo", "combat", "collect", "other"):
        assert name in out
    assert "2" in out  # travel count
    assert "travel" in out and "cargo" in out


def test_list_category_cargo_prints_sorted_ids(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    rc = main(["list", "--category", "cargo"])
    assert rc == 0
    lines = [ln for ln in capsys.readouterr().out.splitlines() if ln.strip()]
    ids = [int(ln.split()[0]) for ln in lines if ln.split()[0].isdigit()]
    assert ids == [21, 20]


def test_run_category_skips_other_race_before_runner(monkeypatch, capsys):
    catalog = {
        11: _mission(11, kind="travel", continent=1, level=1),
        10: _mission(10, kind="travel", continent=2, level=5),
    }
    catalog[11]["reqRace"] = 0
    catalog[10]["reqRace"] = 1
    monkeypatch.setattr("mission_live.cli.load_retail_catalog", lambda **_k: catalog)
    seen: list[int] = []

    def fake_run(ctx, mission_id, *, policy="partial", title_hint=""):
        seen.append(int(mission_id))
        return MissionResult(mission_id=mission_id, status="PASS", title=title_hint, policy=policy)

    class FakeOracle:
        def mission_state(self):
            return {"hasBody": True, "race": 0, "class": 0}

    monkeypatch.setattr("mission_live.cli.run_mission", fake_run)
    monkeypatch.setattr("mission_live.cli.write_results", lambda *a, **k: Path("results.json"))
    monkeypatch.setattr("mission_live.cli.write_html_report", lambda *a, **k: Path("report.html"))
    monkeypatch.setattr("mission_live.cli.DevApiOracle", lambda **_k: FakeOracle())
    rc = main(["run", "--category", "travel"])
    assert rc == 0
    assert seen == [11]
    out = capsys.readouterr().out
    assert "skipped" in out.lower()
    assert "10" in out or "1" in out


def test_run_category_travel_invokes_sorted_ids(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    seen: list[int] = []

    def fake_run(ctx, mission_id, *, policy="partial", title_hint=""):
        seen.append(int(mission_id))
        return MissionResult(mission_id=mission_id, status="PASS", title=title_hint, policy=policy)

    monkeypatch.setattr("mission_live.cli.run_mission", fake_run)
    monkeypatch.setattr("mission_live.cli.write_results", lambda *a, **k: Path("results.json"))
    monkeypatch.setattr("mission_live.cli.write_html_report", lambda *a, **k: Path("report.html"))
    rc = main(["run", "--category", "travel"])
    assert rc == 0
    assert seen == [11, 10]


def test_run_no_selector_tty_menu_selects_cargo(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    seen: list[int] = []

    def fake_run(ctx, mission_id, *, policy="partial", title_hint=""):
        seen.append(int(mission_id))
        return MissionResult(mission_id=mission_id, status="PASS", title=title_hint, policy=policy)

    monkeypatch.setattr("mission_live.cli.run_mission", fake_run)
    monkeypatch.setattr("mission_live.cli.write_results", lambda *a, **k: Path("results.json"))
    monkeypatch.setattr("mission_live.cli.write_html_report", lambda *a, **k: Path("report.html"))
    stdin = io.StringIO("2\n")
    stdin.isatty = lambda: True  # type: ignore[method-assign]
    monkeypatch.setattr(sys, "stdin", stdin)
    rc = main(["run"])
    assert rc == 0
    assert seen == [21, 20]


def test_categories_catalog_error_exits_2(monkeypatch, capsys):
    from mission_live.retail import CatalogError

    def _raise(**_k):
        raise CatalogError("missions.json or missions.glm missing")

    monkeypatch.setattr("mission_live.cli.load_retail_catalog", _raise)
    rc = main(["categories"])
    assert rc == 2
    assert "missions.json" in capsys.readouterr().err


def test_list_and_run_category_catalog_error(monkeypatch, capsys):
    from mission_live.retail import CatalogError

    def _raise(**_k):
        raise CatalogError("missions.json or missions.glm missing")

    monkeypatch.setattr("mission_live.cli.load_retail_catalog", _raise)
    assert main(["list", "--category", "travel"]) == 2
    assert main(["run", "--category", "travel"]) == 2


def test_run_menu_invalid_then_name(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    seen: list[int] = []

    def fake_run(ctx, mission_id, *, policy="partial", title_hint=""):
        seen.append(int(mission_id))
        return MissionResult(mission_id=mission_id, status="PASS", title=title_hint, policy=policy)

    monkeypatch.setattr("mission_live.cli.run_mission", fake_run)
    monkeypatch.setattr("mission_live.cli.write_results", lambda *a, **k: Path("results.json"))
    monkeypatch.setattr("mission_live.cli.write_html_report", lambda *a, **k: Path("report.html"))
    stdin = io.StringIO("nope\ncargo\n")
    stdin.isatty = lambda: True  # type: ignore[method-assign]
    monkeypatch.setattr(sys, "stdin", stdin)
    assert main(["run"]) == 0
    assert seen == [21, 20]


def test_run_menu_exhausted_invalid_exits_2(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    stdin = io.StringIO("x\ny\nz\n")
    stdin.isatty = lambda: True  # type: ignore[method-assign]
    monkeypatch.setattr(sys, "stdin", stdin)
    assert main(["run"]) == 2


def test_run_category_fail_and_error_exit_codes(monkeypatch, capsys):
    _patch_catalog(monkeypatch)

    def fake_run(ctx, mission_id, *, policy="partial", title_hint=""):
        status = "FAIL" if mission_id == 11 else "ERROR"
        return MissionResult(mission_id=mission_id, status=status, title=title_hint, policy=policy)

    monkeypatch.setattr("mission_live.cli.run_mission", fake_run)
    monkeypatch.setattr("mission_live.cli.write_results", lambda *a, **k: Path("results.json"))
    monkeypatch.setattr("mission_live.cli.write_html_report", lambda *a, **k: Path("report.html"))
    assert main(["run", "--category", "travel"]) == 2

    def fake_fail(ctx, mission_id, *, policy="partial", title_hint=""):
        return MissionResult(mission_id=mission_id, status="FAIL", title=title_hint, policy=policy)

    monkeypatch.setattr("mission_live.cli.run_mission", fake_fail)
    assert main(["run", "--id", "11"]) == 1


def test_run_menu_quit_returns_0(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    stdin = io.StringIO("q\n")
    stdin.isatty = lambda: True  # type: ignore[method-assign]
    monkeypatch.setattr(sys, "stdin", stdin)
    rc = main(["run"])
    assert rc == 0


def test_run_no_selector_non_tty_exits_2(monkeypatch, capsys):
    _patch_catalog(monkeypatch)
    stdin = io.StringIO("")
    stdin.isatty = lambda: False  # type: ignore[method-assign]
    monkeypatch.setattr(sys, "stdin", stdin)
    rc = main(["run"])
    assert rc == 2

"""CLI entrypoints for mission_live."""

from __future__ import annotations

import argparse
import json
import os
import sys
import webbrowser
from pathlib import Path

# Allow `python -m mission_live` from tools/mission-live
_ROOT = Path(__file__).resolve().parents[1]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

from mission_live.actuator import ActuatorError, DevToolActuator
from mission_live.categories import (
    CATEGORIES,
    CATEGORY_DESCRIPTIONS,
    category_for,
    missions_in_category,
)
from mission_live.cli_batch import (
    error_mission_result,
    load_prior_results,
    mission_ids_done,
    should_skip_mission,
)
from mission_live.console import LiveStepPrinter, enable_coloring, paint
from mission_live.oracle import DevApiOracle
from mission_live.plan import filter_race_class_eligible
from mission_live.registry import RegistryEntry, load_registry
from mission_live.report.coverage import build_coverage
from mission_live.report.html import write_html_report
from mission_live.report.model import default_out_dir, write_results
from mission_live.retail import CatalogError, load_retail_catalog
from mission_live.runner import run_mission
from mission_live.strategies.base import RunContext


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="mission_live", description="Live mission testing harness")
    parser.add_argument("--dev-api", default=os.environ.get("MISSION_LIVE_DEV_API", "http://127.0.0.1:27999"))
    parser.add_argument("--pipe", default=os.environ.get("MISSION_LIVE_PIPE", r"\\.\pipe\devtool"))
    parser.add_argument("--out", type=Path, default=None, help="Output directory for results/report")
    sub = parser.add_subparsers(dest="cmd", required=True)

    sub.add_parser("doctor", help="Check DevTool pipe + Dev API health")

    p_run = sub.add_parser("run", help="Run one or more missions")
    p_run.add_argument("--id", type=int, action="append", default=[], help="Mission id (repeatable)")
    p_run.add_argument("--registry", action="store_true", help="Run all registry missions")
    p_run.add_argument(
        "--category",
        choices=CATEGORIES,
        default=None,
        help="Run retail-catalog missions in this playstyle bucket",
    )
    p_run.add_argument("--force-grant", action="store_true", help="Bypass race/class via /giveMission")
    p_run.add_argument("--policy", choices=("partial", "strict"), default="partial")
    p_run.add_argument(
        "--resume",
        action="store_true",
        help="Skip mission ids already present in out/results.json and append new runs",
    )

    sub.add_parser("categories", help="List playstyle categories and catalog counts")
    p_list = sub.add_parser("list", help="List catalog missions in a category")
    p_list.add_argument("--category", choices=CATEGORIES, required=True)

    sub.add_parser("coverage", help="Print retail/registry/last-run coverage summary")

    p_report = sub.add_parser("report", help="Write HTML report from last results")
    p_report.add_argument("--open", action="store_true", help="Open report in browser")

    args = parser.parse_args(argv)
    out_dir = args.out or default_out_dir()

    if args.cmd == "doctor":
        return _doctor(args)
    if args.cmd == "categories":
        return _categories()
    if args.cmd == "list":
        return _list_category(args.category)
    if args.cmd == "run":
        return _run(args, out_dir)
    if args.cmd == "coverage":
        cov = build_coverage()
        print(json.dumps({k: v for k, v in cov.items() if k != "rows"}, indent=2))
        print(f"rows={len(cov.get('rows') or [])}")
        return 0
    if args.cmd == "report":
        path = write_html_report(out_dir=out_dir)
        print(path)
        if args.open:
            webbrowser.open(path.as_uri())
        return 0
    return 2


def _catalog_missions() -> list[dict]:
    catalog = load_retail_catalog()
    return list(catalog.values())


def _print_category_table(missions: list[dict]) -> None:
    for i, name in enumerate(CATEGORIES, start=1):
        count = sum(1 for m in missions if category_for(m) == name)
        desc = CATEGORY_DESCRIPTIONS[name]
        print(f"  {i}) {name:<8} {count:>5}  {desc}")


def _categories() -> int:
    try:
        missions = _catalog_missions()
    except CatalogError as ex:
        print(str(ex), file=sys.stderr)
        return 2
    _print_category_table(missions)
    return 0


def _list_category(name: str) -> int:
    try:
        missions = _catalog_missions()
    except CatalogError as ex:
        print(str(ex), file=sys.stderr)
        return 2
    for m in missions_in_category(missions, name):
        mid = int(m.get("id") or m.get("missionId") or 0)
        title = str(m.get("title") or m.get("name") or "")
        continent = m.get("continent") or 0
        level = m.get("reqLevelMin") or 0
        print(f"{mid}  {title}  continent={continent}  level={level}")
    return 0


def _prompt_category(missions: list[dict]) -> str | None:
    print("Select a category:")
    _print_category_table(missions)
    print("  q) quit")
    by_index = {str(i): name for i, name in enumerate(CATEGORIES, start=1)}
    names = set(CATEGORIES)
    for _ in range(3):
        raw = input("> ").strip().lower()
        if raw in {"q", "quit"}:
            return None
        if raw in by_index:
            return by_index[raw]
        if raw in names:
            return raw
        print("Unknown category. Enter a number, name, or q.", file=sys.stderr)
    return ""


def _registry_by_id() -> dict[int, RegistryEntry]:
    return {e.mission_id: e for e in load_registry()}


def _character_state(oracle) -> dict:
    try:
        return oracle.mission_state() or {}
    except Exception:
        return {}


def _apply_race_filter(missions: list[dict], state: dict, *, force_grant: bool) -> list[dict]:
    keep, skip = filter_race_class_eligible(missions, state, force_grant=force_grant)
    if state.get("hasBody"):
        print(f"character race={state.get('race')} class={state.get('class')}")
    if skip:
        print(
            f"race filter: skipped {len(skip)} (race/class mismatch), running {len(keep)}"
        )
    return keep


def _doctor(args) -> int:
    enable_coloring()
    actuator = DevToolActuator(pipe_name=args.pipe)
    oracle = DevApiOracle(base_url=args.dev_api)
    pipe_ok = actuator.ping()
    api_ok = oracle.ping()
    print(f"devtool_pipe={paint(str(pipe_ok), 'green' if pipe_ok else 'red')} ({args.pipe})")
    print(f"dev_api={paint(str(api_ok), 'green' if api_ok else 'red')} ({args.dev_api})")
    if api_ok:
        try:
            health = oracle.health()
            chars = health.get("connectedCharacters") or []
            print(f"connectedCharacters={len(chars)}")
            for c in chars:
                print(f"  - {c.get('characterName')} coid={c.get('characterCoid')}")
        except Exception as ex:
            print(f"health_error={ex}")
        try:
            state = oracle.mission_state()
            if state.get("hasBody"):
                print(f"character race={state.get('race')} class={state.get('class')} level={state.get('level')}")
            else:
                print("character race/class unavailable (no body)")
        except Exception as ex:
            print(f"mission_state_error={ex}")
    return 0 if pipe_ok and api_ok else 2


def _run(args, out_dir: Path) -> int:
    category = getattr(args, "category", None)
    if not args.id and not args.registry and not category:
        if not sys.stdin.isatty():
            print("Specify --id, --registry, and/or --category", file=sys.stderr)
            return 2
        try:
            missions = _catalog_missions()
        except CatalogError as ex:
            print(str(ex), file=sys.stderr)
            return 2
        picked = _prompt_category(missions)
        if picked is None:
            return 0
        if not picked:
            return 2
        category = picked

    enable_coloring()
    progress = LiveStepPrinter()
    ctx = RunContext(
        actuator=DevToolActuator(pipe_name=args.pipe),
        oracle=DevApiOracle(base_url=args.dev_api),
        force_grant=bool(args.force_grant),
        progress=progress,
    )

    results = load_prior_results(out_dir) if args.resume else []
    done = mission_ids_done(results) if args.resume else set()
    if args.resume and done:
        print(f"resume: keeping {len(done)} prior result(s), skipping those ids")

    def _persist() -> None:
        write_results(results, out_dir=out_dir)
        write_html_report(out_dir=out_dir)

    def _print_one(r) -> None:
        status_colored = paint(
            r.status,
            {
                "PASS": "green",
                "PARTIAL": "yellow",
                "SKIP": "yellow",
                "FAIL": "red",
                "ERROR": "red",
            }.get(r.status, "dim"),
        )
        print(
            f"mission {r.mission_id}: {status_colored} locus={r.fail_locus or '-'} "
            f"forceGrant={r.force_grant}"
        )

    def _run_one(mission_id: int, *, policy: str, title_hint: str = "") -> None:
        if should_skip_mission(mission_id, done, resume=bool(args.resume)):
            print(f"skip resume id={mission_id}")
            return
        try:
            result = run_mission(ctx, mission_id, policy=policy, title_hint=title_hint)
        except ActuatorError as ex:
            result = error_mission_result(
                mission_id,
                title=title_hint,
                policy=policy,
                force_grant=bool(args.force_grant),
                detail=str(ex),
            )
            if progress is not None:
                progress.note(f"result ERROR actuator={ex}")
        result.force_grant = bool(args.force_grant)
        results.append(result)
        done.add(mission_id)
        _persist()
        _print_one(result)

    registry = _registry_by_id()
    ran: set[int] = set()
    body_state = _character_state(ctx.oracle)

    def _maybe_run(mission_id: int, *, policy: str, title_hint: str = "") -> None:
        if mission_id in ran:
            return
        ran.add(mission_id)
        _run_one(mission_id, policy=policy, title_hint=title_hint)

    for mid in args.id:
        _maybe_run(mid, policy=args.policy)
    if category:
        try:
            missions = _catalog_missions()
        except CatalogError as ex:
            print(str(ex), file=sys.stderr)
            return 2
        print(f"category={category}")
        queued = _apply_race_filter(
            missions_in_category(missions, category),
            body_state,
            force_grant=bool(args.force_grant),
        )
        for entry in queued:
            mid = int(entry.get("id") or entry.get("missionId") or 0)
            if mid <= 0:
                continue
            reg = registry.get(mid)
            if reg and reg.timeout_sec:
                ctx.step_timeout_sec = reg.timeout_sec
            policy = reg.policy if reg else args.policy
            title = str(entry.get("title") or (reg.notes if reg else "") or "")
            _maybe_run(mid, policy=policy, title_hint=title)
    if args.registry:
        catalog: dict[int, dict] = {}
        try:
            catalog = {int(m.get("id") or 0): m for m in _catalog_missions()}
        except CatalogError:
            catalog = {}
        queued = []
        for entry in load_registry():
            cat = catalog.get(entry.mission_id) or {}
            queued.append(
                {
                    "id": entry.mission_id,
                    "reqRace": cat.get("reqRace", -1),
                    "reqClass": cat.get("reqClass", -1),
                    "policy": entry.policy,
                    "notes": entry.notes,
                    "timeoutSec": entry.timeout_sec,
                }
            )
        for gate in _apply_race_filter(queued, body_state, force_grant=bool(args.force_grant)):
            mid = int(gate["id"])
            if gate.get("timeoutSec"):
                ctx.step_timeout_sec = float(gate["timeoutSec"])
            _maybe_run(mid, policy=str(gate.get("policy") or args.policy), title_hint=str(gate.get("notes") or ""))

    path = write_results(results, out_dir=out_dir)
    html_path = write_html_report(out_dir=out_dir)
    print(f"wrote {path}")
    print(f"wrote {html_path}")

    if any(r.status == "ERROR" for r in results):
        print(
            "hint: run `python -m mission_live doctor` — ERROR usually means Dev API (:27999) "
            "or DevTool pipe is down. Start Launcher/Sector with DevControl enabled and a "
            "patched client with DevTool loaded.",
            file=sys.stderr,
        )
        return 2
    if any(r.status == "FAIL" for r in results):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

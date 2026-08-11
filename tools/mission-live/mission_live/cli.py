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
from mission_live.cli_batch import (
    error_mission_result,
    load_prior_results,
    mission_ids_done,
    should_skip_mission,
)
from mission_live.console import LiveStepPrinter, enable_coloring, paint
from mission_live.oracle import DevApiOracle
from mission_live.registry import load_registry
from mission_live.report.coverage import build_coverage
from mission_live.report.html import write_html_report
from mission_live.report.model import default_out_dir, write_results
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
    p_run.add_argument("--force-grant", action="store_true", help="Bypass race/class via /giveMission")
    p_run.add_argument("--policy", choices=("partial", "strict"), default="partial")
    p_run.add_argument(
        "--resume",
        action="store_true",
        help="Skip mission ids already present in out/results.json and append new runs",
    )

    sub.add_parser("coverage", help="Print retail/registry/last-run coverage summary")

    p_report = sub.add_parser("report", help="Write HTML report from last results")
    p_report.add_argument("--open", action="store_true", help="Open report in browser")

    args = parser.parse_args(argv)
    out_dir = args.out or default_out_dir()

    if args.cmd == "doctor":
        return _doctor(args)
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
    return 0 if pipe_ok and api_ok else 2


def _run(args, out_dir: Path) -> int:
    if not args.id and not args.registry:
        print("Specify --id and/or --registry", file=sys.stderr)
        return 2

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

    for mid in args.id:
        _run_one(mid, policy=args.policy)
    if args.registry:
        for entry in load_registry():
            if entry.timeout_sec:
                ctx.step_timeout_sec = entry.timeout_sec
            _run_one(entry.mission_id, policy=entry.policy, title_hint=entry.notes)

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

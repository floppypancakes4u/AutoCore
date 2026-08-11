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

from mission_live.actuator import DevToolActuator
from mission_live.console import LiveStepPrinter, enable_coloring, paint
from mission_live.oracle import DevApiOracle
from mission_live.registry import load_registry
from mission_live.report.coverage import build_coverage
from mission_live.report.html import write_html_report
from mission_live.report.model import default_out_dir, write_results
from mission_live.runner import run_mission, run_registry
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

    results = []
    for mid in args.id:
        results.append(run_mission(ctx, mid, policy=args.policy))
    if args.registry:
        results.extend(run_registry(ctx, load_registry()))

    path = write_results(results, out_dir=out_dir)
    html_path = write_html_report(out_dir=out_dir)
    print(f"wrote {path}")
    print(f"wrote {html_path}")
    for r in results:
        status_colored = paint(r.status, {
            "PASS": "green",
            "PARTIAL": "yellow",
            "SKIP": "yellow",
            "FAIL": "red",
            "ERROR": "red",
        }.get(r.status, "dim"))
        print(f"mission {r.mission_id}: {status_colored} locus={r.fail_locus or '-'} forceGrant={r.force_grant}")

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

"""Report models and JSON persistence."""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from mission_live.strategies.base import MissionResult


def default_out_dir() -> Path:
    return Path(__file__).resolve().parents[2] / "out"


def write_results(results: list[MissionResult], out_dir: Path | None = None) -> Path:
    out_dir = out_dir or default_out_dir()
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / "results.json"
    payload = {
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "missions": [r.to_dict() for r in results],
        "summary": summarize(results),
    }
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    return path


def load_results(path: Path | None = None) -> dict[str, Any]:
    path = path or (default_out_dir() / "results.json")
    if not path.exists():
        return {"missions": [], "summary": {}}
    return json.loads(path.read_text(encoding="utf-8"))


def summarize(results: list[MissionResult]) -> dict[str, int]:
    counts = {"PASS": 0, "PARTIAL": 0, "FAIL": 0, "SKIP": 0, "ERROR": 0}
    for r in results:
        counts[r.status] = counts.get(r.status, 0) + 1
    counts["total"] = len(results)
    return counts

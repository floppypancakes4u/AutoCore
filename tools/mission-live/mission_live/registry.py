"""Registry YAML loader."""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

try:
    import yaml
except ImportError:  # pragma: no cover
    yaml = None


@dataclass
class RegistryEntry:
    mission_id: int
    policy: str = "partial"  # partial | strict
    tags: list[str] = field(default_factory=list)
    notes: str = ""
    timeout_sec: float | None = None

    @staticmethod
    def from_dict(raw: dict[str, Any]) -> "RegistryEntry":
        return RegistryEntry(
            mission_id=int(raw["id"]),
            policy=str(raw.get("policy", "partial")).lower(),
            tags=list(raw.get("tags") or []),
            notes=str(raw.get("notes") or ""),
            timeout_sec=float(raw["timeoutSec"]) if raw.get("timeoutSec") is not None else None,
        )


def default_registry_path() -> Path:
    return Path(__file__).resolve().parents[1] / "registry" / "missions.yaml"


def load_registry(path: Path | None = None) -> list[RegistryEntry]:
    path = path or default_registry_path()
    if yaml is None:
        raise RuntimeError("PyYAML is required. pip install -r requirements.txt")
    if not path.exists():
        return []
    data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    missions = data.get("missions") or []
    if not isinstance(missions, list):
        raise ValueError(f"registry missions must be a list: {path}")
    return [RegistryEntry.from_dict(m) for m in missions if m]

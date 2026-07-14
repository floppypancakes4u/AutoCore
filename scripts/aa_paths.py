"""
PLATE: Shared path defaults for Auto Assault reverse-engineering scripts.

Usage (other scripts import this):
  from aa_paths import default_install, default_catalog, repo_root

Env overrides:
  AA_INSTALL  - game install dir (default: C:\\Program Files (x86)\\NetDevil\\Auto Assault)
"""

from __future__ import annotations

import os
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INSTALL = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault")


def repo_root() -> Path:
    return REPO_ROOT


def default_install() -> Path:
    env = os.environ.get("AA_INSTALL")
    if env:
        return Path(env)
    return DEFAULT_INSTALL


def default_catalog() -> Path:
    return REPO_ROOT / "tools" / "inventory-catalog" / "inventory-items.json"


def default_clonebase() -> Path:
    return default_install() / "clonebase.wad"


def default_missions_glm() -> Path:
    return default_install() / "missions.glm"


def maps_glm_paths(install: Path | None = None) -> list[Path]:
    root = install or default_install()
    return [root / f"maps{i}.glm" for i in range(1, 5)] + [root / "misc.glm"]

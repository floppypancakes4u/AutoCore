"""
PLATE: Look up clonebase IDs in tools/inventory-catalog/inventory-items.json.

What it does:
  Loads the inventory catalog and prints displayName / uniqueName / className
  (and other keys) for each requested CBID. Faster than opening clonebase.wad
  when the catalog already has the row.

Origins: tmp-map/lookup_cbids.py, cbid12448.py

Examples:
  python scripts/lookup_inventory_cbid.py 12184 11849 12448
  python scripts/lookup_inventory_cbid.py 9301 --keys
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_catalog  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("cbids", type=int, nargs="+")
    ap.add_argument("--catalog", type=Path, default=None)
    ap.add_argument(
        "--keys",
        action="store_true",
        help="Print all keys for the first hit",
    )
    args = ap.parse_args()

    path = args.catalog or default_catalog()
    if not path.is_file():
        print(f"missing {path}", file=sys.stderr)
        return 1
    data = json.loads(path.read_text(encoding="utf-8"))
    items = data if isinstance(data, list) else data.get("items", [])
    by_cbid = {
        it["cbid"]: it
        for it in items
        if isinstance(it, dict) and "cbid" in it
    }

    for cbid in args.cbids:
        it = by_cbid.get(cbid)
        if not it:
            print(f"{cbid}: not in catalog")
            continue
        fields = {
            k: it.get(k)
            for k in (
                "cbid",
                "displayName",
                "uniqueName",
                "className",
                "type",
                "longDescription",
            )
            if k in it or k in ("cbid", "displayName", "uniqueName", "className")
        }
        print(cbid, fields)
        if args.keys:
            print("  keys:", sorted(it.keys()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""
PLATE: Count little-endian i32 occurrences of CBIDs/COIDs inside maps*.glm / misc.glm.

What it does:
  Quick density scan to see which map archives contain a template COID or CBID
  before extracting a full .fam. Also searches ASCII and UTF-16 name needles.

Origins: tmp-map/scan_maps.py, scan_map_coids.py

Examples:
  python scripts/scan_map_ids.py 12184 11849
  python scripts/scan_map_ids.py 9985 --string scavsupply
  python scripts/scan_map_ids.py 15820 --glm maps1.glm
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_install, maps_glm_paths  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("ids", type=int, nargs="*", help="i32 values to count (CBID/COID)")
    ap.add_argument(
        "--string",
        "-s",
        action="append",
        default=[],
        help="Also search for this ASCII/UTF-16 substring",
    )
    ap.add_argument(
        "--glm",
        action="append",
        default=[],
        help="Specific glm basename or path (default: maps1-4 + misc)",
    )
    args = ap.parse_args()

    if not args.ids and not args.string:
        print("provide ids and/or --string", file=sys.stderr)
        return 1

    if args.glm:
        paths = []
        for g in args.glm:
            p = Path(g)
            if not p.is_file():
                p = default_install() / g
            paths.append(p)
    else:
        paths = maps_glm_paths()

    for path in paths:
        if not path.is_file():
            print(f"{path.name}: missing")
            continue
        data = path.read_bytes()
        parts = [f"{path.name} size={len(data)}"]
        for n in args.ids:
            parts.append(f"{n}={data.count(struct.pack('<i', n))}")
        for s in args.string:
            a = data.find(s.encode("latin-1", "replace"))
            u = data.find(s.encode("utf-16-le"))
            parts.append(f"str:{s!r} ascii@{a} utf16@{u}")
        print("  ".join(parts))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

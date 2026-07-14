"""
PLATE: Extract mission XML blocks from missions.glm by ID, internal name, or substring.

What it does:
  missions.glm is a blob of concatenated <Mission ...>...</Mission> XML.
  Finds matching missions and either prints a filtered summary (title, objectives,
  requirements) or writes full XML to a file.

Origins: tmp-map/extract_finalexam.py, extract_m2946.py, cmp_missions.py,
  class_first.py, _extract_missions.py

Examples:
  python scripts/extract_mission_xml.py --id 2946
  python scripts/extract_mission_xml.py --name bountyhunterfirstassignment
  python scripts/extract_mission_xml.py --name finalexam --out tmp-map/finalexam.xml
  python scripts/extract_mission_xml.py --id 3035 --full

Requires: missions.glm under AA_INSTALL (or --glm path).
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_missions_glm  # noqa: E402

SUMMARY_KEYS = (
    "Mission name",
    "<Title>",
    "<Internal>",
    "Requirement",
    "CoreMission",
    "Objective name",
    "ExternalText",
    'type="',
    "TargetNPCCBID",
    "NPCTarget",
    "PrimaryCBID",
    "SecondaryCBID",
    "PrimaryCOID",
    "sequence=",
    "GiveItem",
    "Continent",
    "CBIDItem",
    "GenericTarget",
    "Patrol",
    "ReqMission",
    "Level",
    "Progress",
    "Repeat",
    "CompleteCount",
    "PrimaryExplode",
    "PrimaryDestroy",
    "PrimaryInWorld",
)


def find_mission_blocks(data: bytes) -> list[tuple[int, int]]:
    """Return list of (start, end) byte ranges for each </Mission> closed block."""
    blocks = []
    idx = 0
    while True:
        start = data.find(b"<Mission ", idx)
        if start < 0:
            break
        end = data.find(b"</Mission>", start)
        if end < 0:
            break
        end += len(b"</Mission>")
        blocks.append((start, end))
        idx = end
    return blocks


def mission_matches(block: bytes, mid: int | None, name: str | None) -> bool:
    if mid is not None:
        if re.search(rb'\bID="' + str(mid).encode() + rb'"', block[:200]):
            return True
    if name:
        if name.encode("utf-8") in block or name.encode("latin-1", "replace") in block:
            return True
    return False


def summarize(xml: str) -> None:
    for line in xml.splitlines():
        s = line.strip()
        if not s:
            continue
        if any(k in s for k in SUMMARY_KEYS):
            if "Description" in s or "NotComplete" in s or "CompleteText" in s:
                continue
            print(s[:240])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("--glm", type=Path, default=None)
    ap.add_argument("--id", type=int, action="append", default=[], help="Mission ID")
    ap.add_argument(
        "--name",
        action="append",
        default=[],
        help="Substring of mission name / title / internal id",
    )
    ap.add_argument("--out", type=Path, help="Write first match full XML here")
    ap.add_argument(
        "--full",
        action="store_true",
        help="Print full XML instead of filtered summary",
    )
    ap.add_argument(
        "--list-all-ids",
        action="store_true",
        help="Print all mission IDs and names (no filter)",
    )
    args = ap.parse_args()

    glm_path = args.glm or default_missions_glm()
    if not glm_path.is_file():
        print(f"missing {glm_path}", file=sys.stderr)
        return 1
    data = glm_path.read_bytes()
    blocks = find_mission_blocks(data)
    print(f"{glm_path}: {len(blocks)} mission blocks")

    if args.list_all_ids:
        for start, end in blocks:
            head = data[start : start + 300].decode("utf-8", "replace")
            m = re.search(r'Mission name="([^"]+)"[^>]*ID="(\d+)"', head)
            if m:
                print(f"  {m.group(2)}\t{m.group(1)}")
        return 0

    if not args.id and not args.name:
        print("provide --id and/or --name (or --list-all-ids)", file=sys.stderr)
        return 1

    matches = []
    for start, end in blocks:
        block = data[start:end]
        ok = False
        for mid in args.id:
            if mission_matches(block, mid, None):
                ok = True
        for name in args.name:
            if mission_matches(block, None, name):
                ok = True
        if ok:
            matches.append(block)

    if not matches:
        print("no matches")
        return 1

    print(f"matches: {len(matches)}")
    for i, block in enumerate(matches):
        xml = block.decode("utf-8", "replace")
        head = xml[:200]
        m = re.search(r'Mission name="([^"]+)"[^>]*ID="(\d+)"', head)
        label = f"{m.group(2)} {m.group(1)}" if m else f"match[{i}]"
        print(f"\n========== {label} ==========")
        if args.full:
            print(xml)
        else:
            summarize(xml)

    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(matches[0].decode("utf-8", "replace"), encoding="utf-8")
        print(f"\nwrote {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

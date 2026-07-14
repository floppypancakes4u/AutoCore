"""
PLATE: Aggregate statistics for Requirement type="useitem" across missions.glm.

What it does:
  Scans every useitem requirement block and counts pattern combinations of
  PrimaryCOID / PrimaryCBID / SecondaryCBID / SecondaryGiveAtStart /
  PrimaryInWorld / PrimaryExplode / PrimaryDestroy / ProgressTime / RepeatCount.
  Useful when designing or implementing UseItem mission handlers.

Origins: tmp-map/useitem_stats.py, explode_scan.py

Examples:
  python scripts/mission_useitem_stats.py
  python scripts/mission_useitem_stats.py --sample-explode 5
"""

from __future__ import annotations

import argparse
import collections
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_missions_glm  # noqa: E402


def tag_val(block: str, tag: str) -> str | None:
    a = block.find(f"<{tag}>")
    if a < 0:
        return None
    a += len(tag) + 2
    b = block.find(f"</{tag}>", a)
    if b < 0:
        return None
    return block[a:b].strip()


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("--glm", type=Path, default=None)
    ap.add_argument(
        "--sample-explode",
        type=int,
        default=0,
        help="Print N sample missions with PrimaryExplode=1",
    )
    ap.add_argument("--top", type=int, default=25, help="How many pattern buckets to show")
    args = ap.parse_args()

    glm_path = args.glm or default_missions_glm()
    if not glm_path.is_file():
        print(f"missing {glm_path}", file=sys.stderr)
        return 1
    text = glm_path.read_bytes().decode("utf-8", "replace")

    stats: collections.Counter[str] = collections.Counter()
    n = 0
    i = 0
    while True:
        j = text.find('<Requirement type="useitem"', i)
        if j < 0:
            break
        k = text.find("</Requirement>", j)
        if k < 0:
            break
        block = text[j:k]
        n += 1

        def flag(tag: str) -> str:
            v = tag_val(block, tag)
            if v is None or v == "-1":
                return "none"
            return "set"

        key = (
            f"PCOID={flag('PrimaryCOID')}/PCBID={flag('PrimaryCBID')}/"
            f"SCBID={flag('SecondaryCBID')}/SecGive={tag_val(block,'SecondaryGiveAtStart')}/"
            f"InWorld={tag_val(block,'PrimaryInWorld')}/Explode={tag_val(block,'PrimaryExplode')}/"
            f"Destroy={tag_val(block,'PrimaryDestroy')}/Prog={tag_val(block,'ProgressTime')}/"
            f"Rep={tag_val(block,'RepeatCount')}"
        )
        stats[key] += 1
        i = k

    print(f"total useitem requirements: {n}")
    for k, v in stats.most_common(args.top):
        print(f"  {v:4d}  {k}")

    explode_count = text.count("<PrimaryExplode>1</PrimaryExplode>")
    print(f"\nPrimaryExplode=1 occurrences: {explode_count}")

    if args.sample_explode > 0:
        print(f"\n--- sample explode missions (up to {args.sample_explode}) ---")
        i = 0
        shown = 0
        while shown < args.sample_explode:
            j = text.find("<PrimaryExplode>1</PrimaryExplode>", i)
            if j < 0:
                break
            start = text.rfind("<Mission ", max(0, j - 8000), j)
            title = text.find("<Title>", start, j)
            title_end = text.find("</Title>", title, j) if title > 0 else -1
            t = text[title + 7 : title_end] if title > 0 else "?"
            block = text[j - 400 : j + 200]
            print("---", t[:60])
            for tag in [
                "PrimaryDestroy",
                "PrimaryInWorld",
                "PrimaryExplode",
                "PrimaryUseText",
                "ProgressText",
                "PrimaryCBID",
                "SecondaryCBID",
            ]:
                m = re.search(rf"<{tag}>([^<]*)</{tag}>", block)
                if m:
                    print(f"  {tag}={m.group(1)}")
            shown += 1
            i = j + 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

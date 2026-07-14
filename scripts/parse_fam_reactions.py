"""
PLATE: Dump Reaction templates (CBID 86) from a .fam by COID or name substring.

What it does:
  Scans object headers for reaction records and prints name, reaction type,
  actOn, object list, nested reaction COIDs, and trailing condition/DoForAll bytes.
  Useful when tracing Create/Delete/Activate reaction chains on a map.

Origins: tmp-map/rx.py, rx15819.py, parse_arkbay2.py (reaction branch)

Examples:
  python scripts/parse_fam_reactions.py tmp-map/arkbay.fam
  python scripts/parse_fam_reactions.py tmp-map/arkbay.fam --coid 15819
  python scripts/parse_fam_reactions.py tmp-map/arkbay.fam --filter gunny
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path


def parse_reaction_body(body: bytes) -> dict | None:
    if len(body) < 88:
        return None
    name = body[:65].split(b"\x00")[0].decode("latin1", "replace")
    if not name or sum(1 for c in name if c.isprintable()) < max(3, len(name) * 0.6):
        return None
    rtype = body[65]
    act_on = body[66]
    obj_check = struct.unpack_from("<i", body, 67)[0]
    do_convoy = body[71]
    g1 = struct.unpack_from("<i", body, 72)[0]
    g2 = struct.unpack_from("<f", body, 76)[0]
    g3 = struct.unpack_from("<i", body, 80)[0]
    obj_count = struct.unpack_from("<i", body, 84)[0]
    if obj_count < 0 or obj_count > 40:
        return None
    p = 88
    objs = []
    for _ in range(obj_count):
        if p + 4 > len(body):
            break
        objs.append(struct.unpack_from("<i", body, p)[0])
        p += 4
    rx_count = struct.unpack_from("<i", body, p)[0] if p + 4 <= len(body) else -1
    p += 4
    rxs = []
    for _ in range(max(0, min(rx_count, 40))):
        if p + 4 > len(body):
            break
        rxs.append(struct.unpack_from("<i", body, p)[0])
        p += 4
    all_cond = None
    cond_count = None
    do_for_all = None
    if p < len(body):
        all_cond = body[p]
        p += 1
        if p + 4 <= len(body):
            cond_count = struct.unpack_from("<i", body, p)[0]
            p += 4
            if 0 <= (cond_count or 0) <= 8:
                p += 12 * cond_count
                if p < len(body):
                    do_for_all = body[p]
    return {
        "name": name,
        "type": rtype,
        "act_on": act_on,
        "obj_check": obj_check,
        "do_convoy": do_convoy,
        "g1": g1,
        "g2": g2,
        "g3": g3,
        "objs": objs,
        "nested": rxs,
        "all_cond": all_cond,
        "cond_count": cond_count,
        "do_for_all": do_for_all,
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("fam", type=Path)
    ap.add_argument("--coid", type=int, action="append", default=[])
    ap.add_argument("--filter", "-f", default="", help="Name substring filter")
    args = ap.parse_args()

    fam = args.fam.read_bytes()
    want = set(args.coid) if args.coid else None
    filt = args.filter.lower()
    by: dict[int, dict] = {}
    i = 0
    while i < len(fam) - 20:
        cbid = struct.unpack_from("<i", fam, i + 1)[0]
        coid = struct.unpack_from("<i", fam, i + 5)[0]
        osize = struct.unpack_from("<i", fam, i + 9)[0]
        if (
            cbid == 86
            and 80 <= osize <= 4000
            and 1000 < coid < 200000
            and i + 13 + osize <= len(fam)
        ):
            if want is not None and coid not in want:
                i += 1
                continue
            body = fam[i + 13 : i + 13 + osize]
            rx = parse_reaction_body(body)
            if rx and (not filt or filt in rx["name"].lower()):
                if coid not in by or osize > by[coid].get("_size", 0):
                    rx["_size"] = osize
                    rx["_off"] = i
                    by[coid] = rx
        i += 1

    print(f"reactions: {len(by)}")
    for coid in sorted(by):
        r = by[coid]
        print(
            f"coid={coid} off={r['_off']} size={r['_size']} name={r['name']!r} "
            f"type={r['type']} actOn={r['act_on']} objCheck={r['obj_check']} "
            f"objs={r['objs']} nested={r['nested']} "
            f"allCond={r['all_cond']} condCount={r['cond_count']} doForAll={r['do_for_all']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

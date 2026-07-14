"""
PLATE: Scan a .fam map for Trigger templates (CBID 78) and dump name/reactions/conditions.

What it does:
  Brute-scans object headers (layer u8 + cbid + coid + size), parses trigger bodies
  (loc/rot/scale/name/retrigger/act counts/type/flags/reaction list/conditions),
  and optionally filters by name substring. Also heuristically dumps map variables
  for known boolean/create names if present.

Origins: tmp-map/_parse_gunny_triggers.py

Examples:
  python scripts/parse_fam_triggers.py tmp-map/arkbay.fam
  python scripts/parse_fam_triggers.py tmp-map/arkbay.fam --filter gunny
  python scripts/parse_fam_triggers.py tmp-map/arkbay.fam --vars
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path


def try_parse_trigger(body: bytes, mapver: int) -> dict | None:
    if len(body) < 120:
        return None
    p = 0
    loc = struct.unpack_from("<ffff", body, p)
    p += 16
    p += 16  # rot
    scale = struct.unpack_from("<f", body, p)[0]
    p += 4
    name = body[p : p + 64].split(b"\x00")[0].decode("latin1", "replace")
    p += 64
    if not name or len(name) < 3:
        return None
    if sum(1 for c in name if c.isprintable()) < len(name) * 0.8:
        return None
    retrig = struct.unpack_from("<f", body, p)[0]
    p += 4
    actdel = struct.unpack_from("<f", body, p)[0]
    p += 4
    actcnt = struct.unpack_from("<i", body, p)[0]
    p += 4
    ttype = body[p]
    p += 1
    docoll = body[p]
    p += 1
    docond = body[p]
    p += 1
    if mapver >= 44:
        p += 1  # ShowMapTransitionDecals
    doact = body[p]
    p += 1
    allcond = body[p]
    p += 1
    if mapver >= 60:
        p += 1  # ApplyToAllColliders
    if p + 4 > len(body):
        return None
    rcount = struct.unpack_from("<i", body, p)[0]
    p += 4
    if rcount < 0 or rcount > 30 or p + 8 * rcount > len(body):
        return None
    reactions = []
    for _ in range(rcount):
        reactions.append(struct.unpack_from("<i", body, p)[0])
        p += 4
    if p + 4 > len(body):
        return {
            "name": name,
            "scale": scale,
            "actcnt": actcnt,
            "ttype": ttype,
            "docoll": docoll,
            "docond": docond,
            "doact": doact,
            "allcond": allcond,
            "reactions": reactions,
            "conditions": [],
            "pos": loc[:3],
            "retrig": retrig,
            "actdel": actdel,
        }
    tcount = struct.unpack_from("<i", body, p)[0]
    p += 4
    if 0 <= tcount <= 30:
        for _ in range(tcount):
            if p + 5 > len(body):
                break
            p += 1  # global
            p += 4  # coid
    conditions: list[tuple[int, int, int]] = []
    if p + 4 <= len(body):
        for skip in range(0, 8):
            pp = p + skip
            if pp + 4 > len(body):
                break
            ccount = struct.unpack_from("<i", body, pp)[0]
            if 0 <= ccount <= 8 and pp + 4 + 12 * ccount <= len(body):
                pp += 4
                conds = []
                ok = True
                for _ in range(ccount):
                    left = struct.unpack_from("<i", body, pp)[0]
                    right = struct.unpack_from("<i", body, pp + 4)[0]
                    ctype = body[pp + 8]
                    if left < 0 or left > 500 or right < 0 or right > 500 or ctype > 10:
                        ok = False
                        break
                    conds.append((left, right, ctype))
                    pp += 12
                if ok:
                    conditions = conds
                    break
    return {
        "name": name,
        "scale": scale,
        "actcnt": actcnt,
        "ttype": ttype,
        "docoll": docoll,
        "docond": docond,
        "doact": doact,
        "allcond": allcond,
        "reactions": reactions,
        "conditions": conditions,
        "pos": loc[:3],
        "retrig": retrig,
        "actdel": actdel,
    }


def dump_variables(fam: bytes, names: list[bytes] | None = None) -> None:
    """Heuristic variable records: id/type/value/initial before a known name string."""
    if names is None:
        names = [
            b"L1_hascreated_gunny2",
            b"L1_hascreated_gunny1",
            b"L1_gunnysioux1_hasdeleted",
            b"l1_boolean_hasactiveobj_whatsascab1",
            b"L1_boolean_hasactiveobjective_final",
            b"l1_playerhealth_percent",
            b"L0_const_1",
            b"l1_gunnyheal_lock",
        ]
    results = []
    for name in names:
        idx = 0
        while True:
            j = fam.find(name, idx)
            if j < 0:
                break
            for back in (9, 13, 8, 12, 5):
                start = j - back
                if start < 0:
                    continue
                vid = struct.unpack_from("<i", fam, start)[0]
                vtype = fam[start + 4]
                if 0 < vid < 500 and vtype < 30:
                    val = struct.unpack_from("<f", fam, start + 5)[0]
                    init = struct.unpack_from("<f", fam, start + 9)[0]
                    results.append((name.decode(), vid, vtype, val, init, start))
                    break
            idx = j + 1
    seen: set[tuple[str, int]] = set()
    for r in results:
        key = (r[0], r[1])
        if key in seen:
            continue
        seen.add(key)
        print(f"var {r[0]!r} id={r[1]} type={r[2]} val={r[3]} init={r[4]}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("fam", type=Path, help="Path to .fam map file")
    ap.add_argument(
        "--filter",
        "-f",
        default="",
        help="Only print triggers whose name contains this (case-insensitive)",
    )
    ap.add_argument("--vars", action="store_true", help="Also dump known map variables")
    args = ap.parse_args()

    fam = args.fam.read_bytes()
    mapver = struct.unpack_from("<i", fam, 0)[0]
    print(f"mapver={mapver} size={len(fam)}")

    if args.vars:
        print("--- variables ---")
        dump_variables(fam)

    filt = args.filter.lower()
    by: dict[int, dict] = {}
    i = 0
    while i < len(fam) - 20:
        cbid = struct.unpack_from("<i", fam, i + 1)[0]
        coid = struct.unpack_from("<i", fam, i + 5)[0]
        osize = struct.unpack_from("<i", fam, i + 9)[0]
        if (
            cbid == 78
            and 100 <= osize <= 3000
            and 1000 < coid < 200000
            and i + 13 + osize <= len(fam)
        ):
            body = fam[i + 13 : i + 13 + osize]
            t = try_parse_trigger(body, mapver)
            if t and (not filt or filt in t["name"].lower()):
                if coid not in by or len(body) > by[coid].get("_len", 0):
                    t["_len"] = len(body)
                    by[coid] = t
        i += 1

    print(f"--- triggers ({len(by)}) ---")
    for coid in sorted(by):
        t = by[coid]
        print(
            f"{coid} {t['name']!r} scale={t['scale']:.2f} act={t['actcnt']} "
            f"coll={t['docoll']} cond={t['docond']} onAct={t['doact']} all={t['allcond']} "
            f"rx={t['reactions']} conditions={t['conditions']} "
            f"pos=({t['pos'][0]:.1f},{t['pos'][1]:.1f},{t['pos'][2]:.1f})"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

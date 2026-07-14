"""
PLATE: Dump CloneBaseSpecific / SimpleObject fields from clonebase.wad by unique name or CBID.

What it does:
  Locates a UTF-16 unique name (or scans for CBID i32 + plausible Type), then prints
  CBID, Type, TilesetFlags, short/long desc, Fx, IsGeneratable/Targetable/Available,
  store/loot flags, BaseValue, and for object types: Armor, ReqLevel, Flags bits, HP.

Useful for RE of bitIsUsable / targetable / quest object flags without the full WADLoader.

Origins: tmp-map/clone_flags.py, clone_flags2.py

Examples:
  python scripts/dump_clonebase.py --name gen_n_static_str_02_scavsupply
  python scripts/dump_clonebase.py --cbid 12184
  python scripts/dump_clonebase.py --cbid 11849 9301

Requires: clonebase.wad under AA_INSTALL (or --wad path).
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_clonebase  # noqa: E402


def u16s(wad: bytes, off: int, nchars: int) -> str:
    chars = []
    for j in range(nchars):
        ch = struct.unpack_from("<H", wad, off + j * 2)[0]
        if ch == 0:
            break
        if 32 <= ch < 127:
            chars.append(chr(ch))
        else:
            chars.append("?")
    return "".join(chars)


def dump_at_record(wad: bytes, j: int) -> None:
    """j = offset of CloneBaseId (i32)."""
    cbid, typ, tileset = struct.unpack_from("<iii", wad, j)
    uname = u16s(wad, j + 12, 65)
    p = j + 12 + 130
    short = u16s(wad, p, 65)
    p += 130
    longd = u16s(wad, p, 257)
    p += 514
    fx = u16s(wad, p, 65)
    p += 130
    is_gen, is_tgt, avail, instores, inloot = struct.unpack_from("<IIIII", wad, p)
    p += 20
    baseval, commgrp, sellable = struct.unpack_from("<iii", wad, p)
    p += 12
    print(f"=== CBID={cbid} Type={typ} TilesetFlags={tileset} @0x{j:x} ===")
    print(f"UniqueName={uname!r}")
    print(f"Short={short!r}")
    print(f"Long={longd[:100]!r}")
    print(f"Fx={fx!r}")
    print(
        f"IsGeneratable={is_gen} IsTargetable={is_tgt} Available={avail} "
        f"InStores={instores} InLoot={inloot}"
    )
    print(f"BaseValue={baseval} CommodityGroup={commgrp} IsSellable={sellable}")
    if typ in (1, 3, 4, 6):
        armor = struct.unpack_from("<i", wad, p)[0]
        p_sos = p
        # 11 i32 + 3 float = 56, then reqlevel i16, flags i16
        p2 = p_sos + 11 * 4 + 3 * 4
        req_level, flags, subtype, minhp, maxhp = struct.unpack_from("<hhhhh", wad, p2)
        print(
            f"Armor={armor} ReqLevel={req_level} Flags=0x{flags:04x} "
            f"SubType={subtype} HP={minhp}-{maxhp}"
        )
        bits = [b for b in range(16) if flags & (1 << b)]
        if bits:
            print(f"  flag bits set: {bits}")


def dump_by_name(wad: bytes, name: str) -> bool:
    b = name.encode("utf-16-le")
    i = wad.find(b)
    if i < 0:
        print(f"{name}: NOT FOUND")
        return False
    # Prefer layout UniqueName immediately after id+type+tileset (12 bytes)
    for back in (12, 20, 16, 24):
        start = i - back
        if start < 0:
            continue
        cbid = struct.unpack_from("<i", wad, start)[0]
        typ = struct.unpack_from("<i", wad, start + 4)[0]
        if 1 <= cbid < 100000 and typ in (1, 2, 3, 4, 5, 6, 7, 28):
            dump_at_record(wad, start)
            return True
    print(f"{name}: found string at {i} but could not lock record start")
    return False


def dump_by_cbid(wad: bytes, cbid: int, limit: int = 5) -> int:
    pat = struct.pack("<i", cbid)
    idx = 0
    found = 0
    while found < limit:
        j = wad.find(pat, idx)
        if j < 0 or j > len(wad) - 200:
            break
        typ = struct.unpack_from("<i", wad, j + 4)[0]
        if typ in (1, 2, 3, 4, 5, 6, 7, 28):
            uname = u16s(wad, j + 12, 40)
            if uname:
                dump_at_record(wad, j)
                found += 1
        idx = j + 4
    if found == 0:
        print(f"CBID {cbid}: no plausible CloneBase record")
    return found


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("--wad", type=Path, default=None, help="Path to clonebase.wad")
    ap.add_argument("--name", action="append", default=[], help="UniqueName (UTF-16 field)")
    ap.add_argument("--cbid", type=int, action="append", default=[], help="CloneBase ID")
    args = ap.parse_args()

    wad_path = args.wad or default_clonebase()
    if not wad_path.is_file():
        print(f"missing {wad_path}", file=sys.stderr)
        return 1
    wad = wad_path.read_bytes()
    print(f"loaded {wad_path} ({len(wad)} bytes)")

    if not args.name and not args.cbid:
        print("provide --name and/or --cbid", file=sys.stderr)
        return 1

    for n in args.name:
        dump_by_name(wad, n)
    for c in args.cbid:
        dump_by_cbid(wad, c)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

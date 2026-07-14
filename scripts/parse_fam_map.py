"""
PLATE: Walk a .fam map file (MapData layout) and dump spawn points / object COIDs.

What it does:
  Parses the versioned map header, then VOGO+client object table
  (layer, cbid, coid, body). Classifies spawn points (CBID 77 / inventory
  class SpawnPoint), prints active vs inactive spawns with creature CBIDs,
  and can simulate naive COID allocation to identify a runtime alloc index.

Origins: tmp-map/lookup_coid.py (full MapData walker).

Examples:
  python scripts/parse_fam_map.py tmp-map/arkbay.fam
  python scripts/parse_fam_map.py tmp-map/arkbay.fam --target-alloc 18132
  python scripts/parse_fam_map.py tmp-map/arkbay.fam --list-coids 15820 16463

Requires: optional inventory catalog for CBID name resolution
  (tools/inventory-catalog/inventory-items.json).
"""

from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_catalog  # noqa: E402


class R:
    def __init__(self, d: bytes, p: int = 0):
        self.d = d
        self.p = p

    def i32(self) -> int:
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def u32(self) -> int:
        v = struct.unpack_from("<I", self.d, self.p)[0]
        self.p += 4
        return v

    def i64(self) -> int:
        v = struct.unpack_from("<q", self.d, self.p)[0]
        self.p += 8
        return v

    def f32(self) -> float:
        v = struct.unpack_from("<f", self.d, self.p)[0]
        self.p += 4
        return v

    def u8(self) -> int:
        v = self.d[self.p]
        self.p += 1
        return v

    def bool(self) -> bool:
        return self.u8() != 0

    def i16(self) -> int:
        v = struct.unpack_from("<h", self.d, self.p)[0]
        self.p += 2
        return v

    def skip(self, n: int) -> None:
        self.p += n

    def lengthed(self) -> str:
        n = self.i32()
        s = self.d[self.p : self.p + n]
        self.p += n
        return s.decode("utf-8", "replace")

    def utf8_on(self, length: int) -> str:
        s = self.d[self.p : self.p + length]
        self.p += length
        z = s.find(b"\0")
        if z < 0:
            z = length
        return s[:z].decode("utf-8", "replace")


def load_catalog(path: Path | None) -> dict[int, dict]:
    if not path or not path.is_file():
        return {}
    raw = json.loads(path.read_text(encoding="utf-8"))
    items = raw.get("items", raw if isinstance(raw, list) else [])
    return {it["cbid"]: it for it in items if isinstance(it, dict) and "cbid" in it}


def parse_objects(fam: bytes) -> tuple[int, int, list[dict]]:
    r = R(fam)
    map_ver = r.i32()
    if map_ver >= 27:
        r.i32()
    r.skip(8)
    r.f32()
    r.u8()
    r.bool()
    for _ in range(3):
        r.i16()
    if map_ver >= 11:
        r.bool()
        r.bool()
        r.lengthed()
    if map_ver >= 36:
        r.f32()
    if map_ver >= 45:
        r.i32()
    for _ in range(4):
        r.f32()
    r.i32()  # num_mod
    num_vogo = r.i32()
    num_client = r.i32()
    highest = r.i32()
    r.i64()
    r.i64()
    if map_ver >= 33:
        r.i64()
    if map_ver >= 34:
        r.i64()
    ms = r.i32()
    for _ in range(ms):
        r.i32()
        r.i32()
        if map_ver >= 18:
            r.u8()
        r.lengthed()
    vw = r.i32()
    for _ in range(vw):
        r.i32()
        r.i32()
        r.u8()
        r.skip(12)
        r.i64()
        r.i64()
        oc = r.i32()
        r.skip(oc * 4)
    vc = r.i32()
    for _ in range(vc):
        r.i32()
        r.u8()
        r.f32()
        r.f32()
        if map_ver >= 46:
            r.bool()
        r.utf8_on(64)
    if map_ver >= 47:
        r.lengthed()
        region_count = r.u32()
        for _ in range(region_count):
            r.u8()
            weather_count = r.u32()
            for __ in range(weather_count):
                r.u32()
                r.u32()
                r.f32()
                r.i32()
                r.u8()
                r.u32()
                r.u32()
                if map_ver >= 54:
                    r.u32()
                r.lengthed()
            r.lengthed()
            for __ in range(4):
                r.lengthed()
    if map_ver >= 38 and r.u8() != 0:
        plane_count = r.i32()
        r.skip(16)
        r.skip(plane_count * 16)

    objects = []
    for _ in range(num_vogo + num_client):
        layer = r.u8() if map_ver > 5 else 0
        cbid = r.i32()
        coid = r.i32()
        osize = r.i32()
        body = r.d[r.p : r.p + osize]
        r.p += osize
        objects.append(
            {"layer": layer, "cbid": cbid, "coid": coid, "size": osize, "body": body}
        )
    return map_ver, highest, objects


def parse_spawn(body: bytes, map_ver: int) -> dict:
    p = 0
    te = struct.unpack_from("<qqq", body, p)
    p += 24
    loc = struct.unpack_from("<ffff", body, p)
    p += 16
    p += 16  # rot
    radius = struct.unpack_from("<f", body, p)[0]
    p += 4
    respawn = struct.unpack_from("<f", body, p)[0]
    p += 4
    act = struct.unpack_from("<f", body, p)[0]
    p += 4
    use_gen = body[p]
    p += 1
    has_champ = body[p]
    p += 1
    champ = body[p]
    p += 1
    spawn_ch = body[p]
    p += 1
    is_active = body[p]
    p += 1
    if map_ver >= 31:
        p += 1  # randomly offset
    spawns = []
    for _ in range(12):
        lo = body[p]
        hi = body[p + 1]
        p += 4
        stype = struct.unpack_from("<i", body, p)[0]
        p += 4
        lvl = body[p]
        is_t = body[p + 1]
        p += 4
        if stype != -1:
            spawns.append(
                {
                    "lo": lo,
                    "hi": hi,
                    "type": stype,
                    "lvl": lvl,
                    "isTemplate": is_t != 0,
                }
            )
    loot = struct.unpack_from("<i", body, p)[0] if p + 4 <= len(body) else -1
    p += 4
    loot_pct = struct.unpack_from("<f", body, p)[0] if p + 4 <= len(body) else 0.0
    p += 4
    path = struct.unpack_from("<q", body, p)[0] if p + 8 <= len(body) else 0
    p += 8
    patrol = struct.unpack_from("<f", body, p)[0] if p + 4 <= len(body) else 0.0
    return {
        "te": te,
        "loc": loc,
        "radius": radius,
        "respawn": respawn,
        "act": act,
        "is_active": is_active != 0,
        "spawns": spawns,
        "path": path,
        "patrol": patrol,
        "loot": loot,
        "loot_pct": loot_pct,
    }


def describe_cbid(cbid: int, is_template: bool, items: dict[int, dict]) -> str:
    if is_template:
        return f"VehicleTemplateId={cbid}"
    it = items.get(cbid)
    if not it:
        return f"CBID={cbid}"
    name = it.get("displayName") or it.get("uniqueName") or "?"
    return f"CBID={cbid} {it.get('className')} '{name}'"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("fam", type=Path, help="Path to .fam map file")
    ap.add_argument(
        "--catalog",
        type=Path,
        default=default_catalog(),
        help="inventory-items.json for CBID names",
    )
    ap.add_argument(
        "--target-alloc",
        type=int,
        default=None,
        help="Simulate 1-alloc-per-active-spawn and highlight this counter value",
    )
    ap.add_argument(
        "--list-coids",
        type=int,
        nargs="*",
        default=None,
        help="Only print objects with these map COIDs",
    )
    ap.add_argument(
        "--inactive",
        action="store_true",
        help="Also dump inactive spawn points (Create/Activate candidates)",
    )
    args = ap.parse_args()

    fam = args.fam.read_bytes()
    items = load_catalog(args.catalog)
    map_ver, highest, objects = parse_objects(fam)
    print(f"mapVer={map_ver} objects={len(objects)} HighestCoid={highest}")

    if args.list_coids is not None:
        want = set(args.list_coids)
        for o in objects:
            if o["coid"] in want:
                name = o["body"][:64].split(b"\x00")[0].decode("latin1", "replace")
                print(
                    f"coid={o['coid']} cbid={o['cbid']} size={o['size']} "
                    f"layer={o['layer']} name={name!r}"
                )
        return 0

    spawn_cbids = {77}
    for o in objects:
        it = items.get(o["cbid"])
        if it and it.get("className") == "SpawnPoint":
            spawn_cbids.add(o["cbid"])

    active = []
    for o in objects:
        if o["cbid"] not in spawn_cbids:
            continue
        try:
            sp = parse_spawn(o["body"], map_ver)
        except Exception as e:
            print(f"parse fail coid={o['coid']}: {e}")
            continue
        if sp["is_active"] and sp["spawns"]:
            active.append((o, sp))

    print(f"spawn CBIDs={sorted(spawn_cbids)} active_with_spawns={len(active)}")
    target = args.target_alloc
    counter = highest + 1
    for idx, (o, sp) in enumerate(active):
        desc = ", ".join(
            describe_cbid(c["type"], c["isTemplate"], items) for c in sp["spawns"]
        )
        mark = " <<<" if target is not None and counter == target else ""
        print(
            f"[{idx:03d}] counter={counter} spCoid={o['coid']} "
            f"pos=({sp['loc'][0]:.0f},{sp['loc'][1]:.0f},{sp['loc'][2]:.0f}) "
            f"r={sp['radius']:.0f} {desc}{mark}"
        )
        counter += 1
    print(f"naive counter ends at {counter}")

    if args.inactive:
        print("\n=== inactive spawn points ===")
        for o in objects:
            if o["cbid"] not in spawn_cbids:
                continue
            try:
                sp = parse_spawn(o["body"], map_ver)
            except Exception:
                continue
            if sp["is_active"] or not sp["spawns"]:
                continue
            types = ", ".join(
                describe_cbid(c["type"], c["isTemplate"], items) + f" lvl={c['lvl']}"
                for c in sp["spawns"]
            )
            print(
                f"spCoid={o['coid']} pos=({sp['loc'][0]:.0f},{sp['loc'][1]:.0f},{sp['loc'][2]:.0f}) "
                f"te={sp['te']} | {types}"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

"""
PLATE: Locate a mission record inside clonebase.wad (binary Mission.Read layout).

What it does:
  Finds mission Id (i32) + UTF-16 Name field, then prints Type, NPC giver,
  priority, race/class gates, level range, prerequisite mission IDs,
  continent, RequirementsOred / RequirementsNegative, region/pocket, and
  objective count. Can also dump MissionObjective WorldPosition/ContinentObject
  by objective name.

Origins: tmp-map/extract_m2945.py, _wad_missions.py, _ored.py, _prereqs.py

Examples:
  python scripts/dump_wad_mission.py --id 2945
  python scripts/dump_wad_mission.py --name h_0-1_tas_backrange_bountyhunterreporttotheloa
  python scripts/dump_wad_mission.py --objective h_0-1_bountyhunterreporttotheloa_patrol1

Requires: clonebase.wad under AA_INSTALL.
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from aa_paths import default_clonebase  # noqa: E402


def utf16(s: str) -> bytes:
    return s.encode("utf-16-le")


def read_u16_name(data: bytes, off: int, nchars: int = 65) -> str:
    chars = []
    for k in range(nchars):
        ch = struct.unpack_from("<H", data, off + k * 2)[0]
        if ch == 0:
            break
        if 32 <= ch < 127:
            chars.append(chr(ch))
        else:
            chars.append("?")
    return "".join(chars)


def dump_mission_at(data: bytes, j: int) -> None:
    """Parse Mission.Read fields starting at record offset j (CloneBaseId/Id)."""
    mid = struct.unpack_from("<i", data, j)[0]
    name = read_u16_name(data, j + 4, 65)
    typ = data[j + 4 + 130]
    base = j + 4 + 130 + 2  # after name + type + pad
    npc = struct.unpack_from("<i", data, base)[0]
    priority = struct.unpack_from("<i", data, base + 4)[0]
    req_race = struct.unpack_from("<h", data, base + 8)[0]
    req_class = struct.unpack_from("<h", data, base + 10)[0]
    req_lvl_min = struct.unpack_from("<i", data, base + 12)[0]
    req_lvl_max = struct.unpack_from("<i", data, base + 16)[0]
    req_missions = struct.unpack_from("<4i", data, base + 20)
    p = base + 20 + 16  # after req missions
    is_rep = struct.unpack_from("<h", data, p)[0]
    p += 2
    p += 2  # pad
    p += 4 * 4  # Item
    p += 4 * 4  # ItemTemplate
    p += 4 * 4  # ItemValue floats
    p += 4 * 2  # ItemIsKit
    p += 4 * 4  # ItemQuantity
    auto = struct.unpack_from("<h", data, p)[0]
    p += 2
    aoo = struct.unpack_from("<h", data, p)[0]
    p += 2
    continent = struct.unpack_from("<i", data, p)[0]
    p += 4
    # Achievement, Discipline, DisciplineValue, RewardDiscipline,
    # RewardDisciplineValue, RewardUnassignedDisciplinePoints, RequirementEventId
    p += 4 * 7
    target_level = struct.unpack_from("<h", data, p)[0]
    p += 2
    p += 2  # pad
    ored = struct.unpack_from("<i", data, p)[0]
    neg = struct.unpack_from("<i", data, p + 4)[0]
    region = struct.unpack_from("<i", data, p + 8)[0]
    pocket = struct.unpack_from("<i", data, p + 12)[0]
    nobj = data[p + 16]
    print(
        f"mid={mid} @0x{j:x} name={name!r} type={typ} NPC={npc} prio={priority} "
        f"race={req_race} class={req_class} lvl={req_lvl_min}-{req_lvl_max} "
        f"reqMissions={req_missions}"
    )
    print(
        f"  rep={is_rep} auto={auto} aoo={aoo} continent={continent} "
        f"targetLvl={target_level} RequirementsOred={ored} RequirementsNegative={neg} "
        f"region={region} pocket={pocket} nObj={nobj}"
    )


def find_by_id(data: bytes, mid: int, limit: int = 5) -> int:
    pat = struct.pack("<i", mid)
    idx = 0
    hits = 0
    while hits < limit:
        j = data.find(pat, idx)
        if j < 0:
            break
        name_bytes = data[j + 4 : j + 4 + 130]
        ok = True
        chars = []
        for k in range(0, 130, 2):
            ch = name_bytes[k] | (name_bytes[k + 1] << 8)
            if ch == 0:
                break
            if ch < 32 or ch > 126:
                ok = False
                break
            chars.append(chr(ch))
        name = "".join(chars)
        if ok and len(name) >= 3:
            dump_mission_at(data, j)
            hits += 1
        idx = j + 4
    return hits


def find_by_name(data: bytes, name: str) -> int:
    b = utf16(name)
    i = data.find(b)
    if i < 0:
        print(f"name {name!r}: not found")
        return 0
    # Name field follows Id (4)
    j = i - 4
    if j < 0:
        return 0
    dump_mission_at(data, j)
    return 1


def dump_objective(data: bytes, obj_name: str) -> None:
    u16 = utf16(obj_name)
    i = data.find(u16)
    print(f"objective {obj_name!r} at {i}")
    if i < 0:
        return
    # MissionObjective.ReadNew: QuestId i32, ObjectiveId i32, Sequence byte, pad1, Name UTF16 65
    rec = i - 10
    quest_id, obj_id = struct.unpack_from("<ii", data, rec)
    seq = data[rec + 8]
    after_names = i + 130 + 130 + 2
    world_pos, cont_obj = struct.unpack_from("<ii", data, after_names)
    print(
        f"  quest={quest_id} objId={obj_id} seq={seq} "
        f"WorldPosition={world_pos} ContinentObject={cont_obj}"
    )


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("Origins:")[0].strip())
    ap.add_argument("--wad", type=Path, default=None)
    ap.add_argument("--id", type=int, action="append", default=[])
    ap.add_argument("--name", action="append", default=[])
    ap.add_argument("--objective", action="append", default=[])
    args = ap.parse_args()

    wad_path = args.wad or default_clonebase()
    if not wad_path.is_file():
        print(f"missing {wad_path}", file=sys.stderr)
        return 1
    data = wad_path.read_bytes()

    if not args.id and not args.name and not args.objective:
        print("provide --id, --name, and/or --objective", file=sys.stderr)
        return 1

    for mid in args.id:
        n = find_by_id(data, mid)
        if n == 0:
            print(f"mid {mid}: no hits")
    for name in args.name:
        find_by_name(data, name)
    for obj in args.objective:
        dump_objective(data, obj)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

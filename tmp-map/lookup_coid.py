"""Identify runtime map-NPC COID 0x500046D4 = CoidBase+18132 on arkbaytutorial."""
import json
import struct
from pathlib import Path

fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()
catalog_path = Path(
    r"C:\Users\josh\Documents\GitHub\AutoCore\tools\inventory-catalog\inventory-items.json"
)
_raw = json.loads(catalog_path.read_text(encoding="utf-8"))
items = {it["cbid"]: it for it in _raw["items"]}


class R:
    def __init__(self, d, p=0):
        self.d = d
        self.p = p

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def u32(self):
        v = struct.unpack_from("<I", self.d, self.p)[0]
        self.p += 4
        return v

    def i64(self):
        v = struct.unpack_from("<q", self.d, self.p)[0]
        self.p += 8
        return v

    def f32(self):
        v = struct.unpack_from("<f", self.d, self.p)[0]
        self.p += 4
        return v

    def u8(self):
        v = self.d[self.p]
        self.p += 1
        return v

    def bool(self):
        return self.u8() != 0

    def i16(self):
        v = struct.unpack_from("<h", self.d, self.p)[0]
        self.p += 2
        return v

    def skip(self, n):
        self.p += n

    def lengthed(self):
        n = self.i32()
        s = self.d[self.p : self.p + n]
        self.p += n
        return s.decode("utf-8", "replace")

    def utf8_on(self, length):
        s = self.d[self.p : self.p + length]
        self.p += length
        z = s.find(b"\0")
        if z < 0:
            z = length
        return s[:z].decode("utf-8", "replace")


r = R(fam)
map_ver = r.i32()
if map_ver >= 27:
    r.i32()
r.skip(8)
r.f32()
r.u8()
r.bool()
[r.i16() for _ in range(3)]
if map_ver >= 11:
    r.bool()
    r.bool()
    r.lengthed()
if map_ver >= 36:
    r.f32()
if map_ver >= 45:
    r.i32()
[r.f32() for _ in range(4)]
num_mod = r.i32()
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
for i in range(num_vogo + num_client):
    layer = r.u8() if map_ver > 5 else 0
    cbid = r.i32()
    coid = r.i32()
    osize = r.i32()
    body = r.d[r.p : r.p + osize]
    r.p += osize
    objects.append({"layer": layer, "cbid": cbid, "coid": coid, "size": osize, "body": body})

print(f"HighestCoid={highest} counter_start={highest+1}")
print(f"Request coid=0x5000_0000+18132 => alloc when counter==18132 (index {18132-(highest+1)})")


def parse_spawn(body):
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
        rand_off = body[p]
        p += 1
    else:
        rand_off = 0
    spawns = []
    for i in range(12):
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
    loot = struct.unpack_from("<i", body, p)[0]
    p += 4
    loot_pct = struct.unpack_from("<f", body, p)[0]
    p += 4
    path = struct.unpack_from("<q", body, p)[0]
    p += 8
    patrol = struct.unpack_from("<f", body, p)[0]
    p += 4
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
    }


def describe_cbid(cbid, is_template):
    if is_template:
        return f"VehicleTemplateId={cbid}"
    it = items.get(cbid)
    if not it:
        return f"CBID={cbid} (not in inventory catalog)"
    name = it.get("displayName") or it.get("uniqueName") or "?"
    return f"CBID={cbid} {it.get('className')} '{name}' [{it.get('uniqueName')}]"


# Spawn point CBIDs from ObjectTemplate.AllocateTemplateFromCBID - SpawnPoint type
# Inventory may list SpawnPoint class. Also cbid 77 historically.
spawn_cbids = set()
for o in objects:
    it = items.get(o["cbid"])
    if it and it.get("className") == "SpawnPoint":
        spawn_cbids.add(o["cbid"])
if 77 not in spawn_cbids:
    spawn_cbids.add(77)
print("spawn CBIDs", sorted(spawn_cbids))

active = []
for o in objects:
    if o["cbid"] not in spawn_cbids:
        continue
    try:
        sp = parse_spawn(o["body"])
    except Exception as e:
        print("parse fail coid", o["coid"], e)
        continue
    if not sp["is_active"]:
        continue
    if not sp["spawns"]:
        continue
    active.append((o, sp))

print(f"active spawnpoints with spawns: {len(active)}")

# Simulate naive allocation: 1 coid per spawn child only (creature-only path).
# Also show multi-spawn ambiguity.
counter = highest + 1
TARGET = 18132
hits = []
for idx, (o, sp) in enumerate(active):
    # GetSpawn: if one real spawn use it; else random among them — list all
    choices = sp["spawns"]
    primary = choices[0] if len(choices) == 1 else None
    # creature/vehicle each allocate at least 1; vehicles more
    # For identification of counter==18132, record when counter is TARGET before alloc
    before = counter
    # Assume 1 allocation for creature; vehicles uncertain
    if primary and not primary["isTemplate"]:
        it = items.get(primary["type"])
        cls = (it or {}).get("className", "")
        if cls == "Vehicle":
            # vehicle + optional driver + wheelset — unknown; mark multi
            allocs = 1  # minimum
            multi = True
        else:
            allocs = 1
            multi = False
    elif primary and primary["isTemplate"]:
        allocs = 1
        multi = True  # +driver +gear
    else:
        allocs = 1
        multi = True

    for a in range(allocs):
        if counter == TARGET:
            hits.append(
                {
                    "spawn_coid": o["coid"],
                    "spawn_cbid": o["cbid"],
                    "pos": sp["loc"][:3],
                    "choices": choices,
                    "primary": primary,
                    "multi": multi or len(choices) > 1,
                    "active_index": idx,
                    "before": before,
                }
            )
        counter += 1

print(f"naive 1-alloc-per-active-spawn ends counter at {counter}")
print(f"hits for 18132 under naive model: {len(hits)}")
for h in hits:
    print("---")
    print(
        f"spawnPoint template COID={h['spawn_coid']} map pos=({h['pos'][0]:.1f},{h['pos'][1]:.1f},{h['pos'][2]:.1f})"
    )
    print(f"  active_index={h['active_index']} multi_uncertain={h['multi']}")
    for c in h["choices"]:
        print("  ", describe_cbid(c["type"], c["isTemplate"]), f"lvlOff={c['lvl']} count={c['lo']}-{c['hi']}")

# Also print first 40 active spawns with counter progression (1 each)
print("\n=== First 45 active spawns (1 coid each assumption) ===")
counter = highest + 1
for idx, (o, sp) in enumerate(active[:45]):
    choices = sp["spawns"]
    desc = ", ".join(describe_cbid(c["type"], c["isTemplate"]) for c in choices)
    mark = " <<<" if counter == TARGET else ""
    print(
        f"[{idx:02d}] counter={counter} spCoid={o['coid']} pos=({sp['loc'][0]:.0f},{sp['loc'][1]:.0f},{sp['loc'][2]:.0f}) {desc}{mark}"
    )
    counter += 1

# Look up CBID 18132 as red herring
print("\nCBID 18132 (clonebase, NOT the COID):", describe_cbid(18132, False))

print("\n=== Inactive spawnpoints (post-load Create/Activate candidates) ===")
inactive = []
for o in objects:
    if o["cbid"] not in spawn_cbids:
        continue
    try:
        sp = parse_spawn(o["body"])
    except Exception:
        continue
    if sp["is_active"] or not sp["spawns"]:
        continue
    inactive.append((o, sp))
print("count", len(inactive))
for o, sp in inactive:
    types = ", ".join(
        describe_cbid(c["type"], c["isTemplate"]) + f" lvl={c['lvl']}"
        for c in sp["spawns"]
    )
    print(
        f"spCoid={o['coid']} pos=({sp['loc'][0]:.0f},{sp['loc'][1]:.0f},{sp['loc'][2]:.0f}) "
        f"te={sp['te']} | {types}"
    )

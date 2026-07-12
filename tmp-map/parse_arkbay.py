import struct
from pathlib import Path

glm = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\maps1.glm")
data = glm.read_bytes()
header_off = struct.unpack_from("<i", data, len(data) - 4)[0]
assert data[header_off : header_off + 4] == b"CHNK"
str_table_off = struct.unpack_from("<i", data, header_off + 8)[0]
str_table_size = struct.unpack_from("<i", data, header_off + 12)[0]
entry_count = struct.unpack_from("<i", data, header_off + 16)[0]
st = data[str_table_off : str_table_off + str_table_size]
names = []
cur = []
for b in st:
    if b == 0:
        names.append(bytes(cur).decode("latin1", errors="replace"))
        cur = []
    else:
        cur.append(b)
assert len(names) == entry_count, (len(names), entry_count)

pos = header_off + 20
entries = {}
for name in names:
    offset = struct.unpack_from("<i", data, pos)[0]
    pos += 4
    size = struct.unpack_from("<i", data, pos)[0]
    pos += 4
    realsize = struct.unpack_from("<i", data, pos)[0]
    pos += 4
    mtime = struct.unpack_from("<i", data, pos)[0]
    pos += 4
    scheme = struct.unpack_from("<h", data, pos)[0]
    pos += 2
    entries[name] = (offset, size)

fam_name = next(n for n in names if "arkbaytutorial" in n and n.endswith(".fam"))
print("fam", fam_name, entries[fam_name])
off, size = entries[fam_name]
fam = data[off : off + size]
out = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam")
out.parent.mkdir(exist_ok=True)
out.write_bytes(fam)
print("wrote", len(fam), "mapVer", struct.unpack_from("<i", fam, 0)[0])


def parse_spawn_body(body, map_ver):
    # After ObjectTemplate base fields that precede TriggerEvents in GraphicsObject/SpawnPoint
    # From AutoCore SpawnPointTemplate.Read - need object template fields first
    # Simplified: session said TriggerEvents 24, Loc 16, Rot 16, Radius, Respawn, ActRange,
    # UseGen, HasChamp, ChampChance, SpawnChance, IsActive then spawns...
    # GraphicsObjectTemplate.Read calls ReadTriggerEvents first after base ObjectTemplate.Read
    # ObjectTemplate.Read is empty virtual - so TriggerEvents at start of graphics/spawn body
    br = body
    p = 0

    def ri64():
        nonlocal p
        v = struct.unpack_from("<q", br, p)[0]
        p += 8
        return v

    def ri32():
        nonlocal p
        v = struct.unpack_from("<i", br, p)[0]
        p += 4
        return v

    def rf():
        nonlocal p
        v = struct.unpack_from("<f", br, p)[0]
        p += 4
        return v

    def rbool():
        nonlocal p
        v = br[p]
        p += 1
        return v != 0

    te = [ri64(), ri64(), ri64()]
    lx, ly, lz, lw = rf(), rf(), rf(), rf()
    # rotation quat 4 floats
    p += 16
    radius = rf()
    respawn = rf()
    act_range = rf()
    use_gen = rbool()
    has_champ = rbool()
    champ_chance = rbool()  # may be byte
    spawn_chance = rbool()
    is_active = rbool()
    # alignment - map version dependent. Prior session worked; try spawn list count
    # Read remaining heuristically for name / path
    return {
        "te": te,
        "pos": (lx, ly, lz),
        "is_active": is_active,
        "radius": radius,
        "path_guess_off": p,
    }


# Scan all VOGOs: need header counts. Parse MapData-style header partially.
p = 0
map_ver = struct.unpack_from("<i", fam, p)[0]
p += 4
if map_ver >= 27:
    p += 4  # iteration
p += 8
p += 4  # grid
p += 1  # tileset
p += 1  # useroad
p += 6  # music 3*i16
if map_ver >= 11:
    p += 1  # clouds
    p += 1  # tod
    # lengthed string
    slen = struct.unpack_from("<i", fam, p)[0]
    p += 4 + slen
if map_ver >= 36:
    p += 4  # cull
if map_ver >= 45:
    p += 4  # imports
# entry point vector4
p += 16
# module placements
num_mod = struct.unpack_from("<i", fam, p)[0]
p += 4
print("num_mod", num_mod, "pos", p)
# This is fragile - use brute force object scan instead

# Brute: for each position where we see layer?, cbid, coid, size with size reasonable
found = {}
i = 0
n = len(fam)
while i < n - 16:
    # try with layer byte
    for with_layer in (True, False):
        q = i
        if with_layer:
            layer = fam[q]
            q += 1
        cbid = struct.unpack_from("<i", fam, q)[0]
        coid = struct.unpack_from("<i", fam, q + 4)[0]
        osize = struct.unpack_from("<i", fam, q + 8)[0]
        if osize < 8 or osize > 5000:
            continue
        if coid not in (
            14090,
            14138,
            15820,
            15818,
            15819,
            16462,
            16463,
            16500,
            16464,
            16467,
            13939,
            14134,
            14130,
            16285,
            14139,
            14142,
            14133,
            16461,
            16465,
            16468,
            16469,
        ):
            continue
        if cbid < 1 or cbid > 500:
            continue
        body_start = q + 12
        body_end = body_start + osize
        if body_end > n:
            continue
        found[coid] = {
            "cbid": cbid,
            "size": osize,
            "off": i,
            "layer": layer if with_layer else None,
            "body": fam[body_start:body_end],
        }
    i += 1

for coid in sorted(found):
    f = found[coid]
    print(f"OBJ coid={coid} cbid={f['cbid']} size={f['size']} off={f['off']}")

# Parse spawn points (cbid 77)
print("\n--- SpawnPoints ---")
for coid, f in sorted(found.items()):
    if f["cbid"] != 77:
        continue
    body = f["body"]
    te = struct.unpack_from("<qqq", body, 0)
    pos = struct.unpack_from("<ffff", body, 24)
    # After rot (16), radius respawn actrange (12), 5 bools
    base = 24 + 16 + 12
    flags = body[base : base + 8]
    # find isActive near end of fixed header - session used specific layout
    # print hex dump of first 80 bytes
    print(f"SP {coid} te={list(te)} pos=({pos[0]:.2f},{pos[1]:.2f},{pos[2]:.2f})")
    print("  head", body[:80].hex())

# Parse reactions (cbid 86) for 15819 etc
print("\n--- Reactions / Triggers of interest ---")
for coid, f in sorted(found.items()):
    body = f["body"]
    if f["cbid"] == 86:  # reaction
        # name at start often
        name = body[:40].split(b"\x00")[0].decode("latin1", errors="replace")
        # type at known offset from prior session - try scan for type byte and object lists
        print(f"RX {coid} cbid={f['cbid']} size={f['size']} name='{name}'")
        print("  ", body[:100].hex())
    if f["cbid"] == 78:  # trigger
        name = body[:40].split(b"\x00")[0].decode("latin1", errors="replace")
        print(f"TR {coid} size={f['size']} name='{name}'")
        print("  ", body[:120].hex())
    if f["cbid"] not in (77, 78, 86) and coid in (16462, 16500, 13939):
        name = body[:40].split(b"\x00")[0].decode("latin1", errors="replace")
        print(f"GFX {coid} cbid={f['cbid']} size={f['size']} name='{name}' te?={list(struct.unpack_from('<qqq', body, 0)) if f['size']>=24 else None}")

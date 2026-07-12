import struct
from pathlib import Path

data = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\maps1.glm").read_bytes()
header_off = struct.unpack_from("<i", data, len(data) - 4)[0]
str_table_off = struct.unpack_from("<i", data, header_off + 8)[0]
str_table_size = struct.unpack_from("<i", data, header_off + 12)[0]
st = data[str_table_off : str_table_off + str_table_size]
names = []
cur = bytearray()
for b in st:
    if b == 0:
        names.append(cur.decode("latin1"))
        cur.clear()
    else:
        cur.append(b)

pos = header_off + 20
entries = {}
for name in names:
    offset, size, realsize, mtime = struct.unpack_from("<iiii", data, pos)
    pos += 22  # + scheme i16 + pad i32
    entries[name] = (offset, size, realsize)

fam_name = "sec_f_h_map_tut_j2_arkbaytutorial.fam"
off, size, rs = entries[fam_name]
print("entry", fam_name, off, size, rs)
fam = data[off : off + size]
Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").write_bytes(fam)
print("mapver", struct.unpack_from("<i", fam, 0)[0], "len", len(fam))

interest = {
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
    16241,
    16218,
}

found = {}
n = len(fam)
i = 0
while i < n - 20:
    cbid = struct.unpack_from("<i", fam, i + 1)[0]
    coid = struct.unpack_from("<i", fam, i + 5)[0]
    osize = struct.unpack_from("<i", fam, i + 9)[0]
    if coid in interest and 1 <= cbid <= 200 and 20 <= osize <= 2000 and i + 13 + osize <= n:
        body = fam[i + 13 : i + 13 + osize]
        # Prefer larger/better hits: take first valid or replace if name looks better
        if coid not in found or osize > found[coid]["size"]:
            found[coid] = {"cbid": cbid, "size": osize, "off": i, "body": body}
    i += 1

print("found", sorted(found.keys()))

for coid in sorted(found):
    f = found[coid]
    body = f["body"]
    name = body[:64].split(b"\x00")[0].decode("latin1", errors="replace")
    print(f"coid={coid} cbid={f['cbid']} size={f['size']} off={f['off']} name={name[:50]!r}")
    if f["cbid"] == 77:  # spawn
        te = list(struct.unpack_from("<qqq", body, 0))
        pos4 = struct.unpack_from("<ffff", body, 24)
        # after rot 16 + 12 floats/bools region
        print(f"  SPAWN te={te} pos=({pos4[0]:.1f},{pos4[1]:.1f},{pos4[2]:.1f})")
        # IsActive after UseGen HasChamp ChampChance SpawnChance - see template
        # TriggerEvents24 + loc16 + rot16 + r4 + resp4 + act4 + 2bool + 2byte + isActive
        p = 24 + 16 + 12
        # UseGenerator bool, HasChampion bool, ChampionChance byte, SpawnChance byte, IsActive bool
        use_gen = body[p]
        has_ch = body[p + 1]
        champ = body[p + 2]
        spawn_ch = body[p + 3]
        is_act = body[p + 4]
        print(f"  flags useGen={use_gen} hasChamp={has_ch} champ={champ} spawnCh={spawn_ch} isActive={is_act}")
    if f["cbid"] == 86:  # reaction
        # name 65 utf8, type byte, actOn bool, objCheck i32, doForConvoy bool, g1 i32, g2 f32, g3 i32
        name65 = body[:65].split(b"\x00")[0].decode("latin1", errors="replace")
        rtype = body[65]
        act_on = body[66]
        obj_check = struct.unpack_from("<i", body, 67)[0]
        do_convoy = body[71]
        g1 = struct.unpack_from("<i", body, 72)[0]
        g2 = struct.unpack_from("<f", body, 76)[0]
        g3 = struct.unpack_from("<i", body, 80)[0]
        obj_count = struct.unpack_from("<i", body, 84)[0]
        objs = []
        p = 88
        for _ in range(max(0, min(obj_count, 20))):
            objs.append(struct.unpack_from("<i", body, p)[0])
            p += 4
        rx_count = struct.unpack_from("<i", body, p)[0] if p + 4 <= len(body) else -1
        p += 4
        rxs = []
        for _ in range(max(0, min(rx_count, 20))):
            if p + 4 > len(body):
                break
            rxs.append(struct.unpack_from("<i", body, p)[0])
            p += 4
        print(
            f"  RX name={name65!r} type={rtype} actOn={act_on} objCheck={obj_check} "
            f"g1={g1} g2={g2} g3={g3} objs={objs} nested={rxs}"
        )
    if f["cbid"] == 78:  # trigger
        name65 = body[:65].split(b"\x00")[0].decode("latin1", errors="replace")
        # TriggerTemplate layout from AutoCore
        print(f"  TR name={name65!r}")
        print("  hex", body[65:120].hex())

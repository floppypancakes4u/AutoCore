import struct
from pathlib import Path

fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()


def parse_spawn(off):
    osize = struct.unpack_from("<i", fam, off + 9)[0]
    coid = struct.unpack_from("<i", fam, off + 5)[0]
    body = fam[off + 13 : off + 13 + osize]
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
    act_range = struct.unpack_from("<f", body, p)[0]
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
    rand_off = body[p]
    p += 1
    print(
        f"coid={coid} radius={radius} respawn={respawn} actRange={act_range} "
        f"isActive={is_active} te={te} pos=({loc[0]:.1f},{loc[1]:.1f},{loc[2]:.1f})"
    )
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
            print(f"  spawn[{i}] lo={lo} hi={hi} type={stype} lvl={lvl} isTemplate={is_t}")


for off in [434291, 442511, 513268]:
    parse_spawn(off)

# Also print 16462/16500 as any CBID object near create list offsets from string search
for target in (16462, 16500, 13939):
    print("--- search", target)
    b = struct.pack("<i", target)
    start = 0
    while True:
        i = fam.find(b, start)
        if i < 0:
            break
        # possible object header: layer u8, cbid i32, coid i32 at i
        off = i - 5
        if off >= 0:
            cbid = struct.unpack_from("<i", fam, off + 1)[0]
            osize = struct.unpack_from("<i", fam, off + 9)[0]
            if 1 <= cbid <= 30000 and 8 <= osize <= 8000 and off + 13 + osize <= len(fam):
                print(f"  hit@{i} header@{off} cbid={cbid} size={osize}")
        start = i + 1

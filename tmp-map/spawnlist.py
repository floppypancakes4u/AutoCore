from pathlib import Path
import struct

# Use maps1 glm fam and better object parse for spawn lists of 14090 and 15820
# From parse_arkbay2, spawn bodies start at known offs
fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()

def find_spawn(coid):
    raw = struct.pack("<i", coid)
    i = 0
    best = None
    while True:
        j = fam.find(raw, i)
        if j < 0: break
        # layout: ? + cbid i32 + coid i32 + size i32 at j-5?
        # parse_arkbay2: cbid at i+1, coid at i+5, osize at i+9
        start = j - 5
        if start >= 0:
            cbid = struct.unpack_from("<i", fam, start+1)[0]
            oc = struct.unpack_from("<i", fam, start+5)[0]
            osize = struct.unpack_from("<i", fam, start+9)[0]
            if oc == coid and cbid == 77 and 100 <= osize <= 2000:
                body = fam[start+13:start+13+osize]
                if best is None or osize > best[0]:
                    best = (osize, start, body, cbid)
        i = j+1
    return best

for coid in [14090, 15820, 14138]:
    b = find_spawn(coid)
    if not b:
        print(coid, "not found"); continue
    osize, start, body, cbid = b
    te = struct.unpack_from("<qqq", body, 0)
    pos = struct.unpack_from("<ffff", body, 24)
    print(f"=== spawn {coid} off={start} size={osize} te={te} pos={pos[:3]}")
    # After mapver>=31 randomly offset bool after isActive
    # layout after TE24+loc16+rot16: r4 resp4 act4 useGen hasChamp champ spawnCh isActive [random]
    p = 24+16+16  # wait TE is 24, loc 16, rot 16 = 56
    # ReadTriggerEvents: 3 x int64 = 24
    # Location Vector4 = 16, Rotation quat = 16 ? p=56
    p = 56
    radius, respawn, actrange = struct.unpack_from("<fff", body, p); p+=12
    use_gen = body[p]; has_ch=body[p+1]; champ=body[p+2]; spawn_ch=body[p+3]; is_act=body[p+4]; p+=5
    # mapver 61 >= 31: randomly offset
    rnd = body[p]; p+=1
    print(f"  radius={radius:.1f} resp={respawn:.1f} actR={actrange:.1f} isActive={is_act} rnd={rnd}")
    # 12 spawn lists
    for i in range(12):
        # SpawnList.Read - need structure
        chunk = body[p:p+40]
        # print ints
        ints = struct.unpack_from("<10i", body, p) if p+40 <= len(body) else []
        # try find non -1 spawn types
        p0 = p
        # From code: IsTemplate, SpawnType, etc.
        break
    # dump remaining as potential spawn types (positive ints near cbid range)
    for off in range(p, min(len(body)-4, p+400), 4):
        v = struct.unpack_from("<i", body, off)[0]
        if 1000 < v < 30000:
            print(f"  body+{off}: {v}")

# SpawnList from source
cs = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\EntityTemplates\SpawnPointTemplate.cs").read_text()
# find SpawnList class
idx = cs.find("class SpawnList")
print(cs[idx:idx+800])

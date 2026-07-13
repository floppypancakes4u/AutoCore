from pathlib import Path
import struct
# Search clonebase.wad for creature 12448 - might be in binary
wad = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad").read_bytes()
target = struct.pack("<i", 12448)
count = 0
idx = 0
hits = []
while count < 5:
    i = wad.find(target, idx)
    if i < 0: break
    hits.append(i)
    idx = i+1
    count += 1
print("hits", hits)
# also search missions glm for TargetNPCCBID 12448 context - pad gunny name
m = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\missions.glm").read_bytes()
# find gunny near 12448
i = m.find(b"12448")
print("mission 12448 at", i)
# search wad.xml
wadxml = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\wad.xml")
if wadxml.exists():
    t = wadxml.read_text(encoding="latin1", errors="replace")
    j = t.find("12448")
    print("wadxml", j, t[max(0,j-100):j+150] if j>=0 else "no")
# parse spawn 15820 spawns more carefully from fam
fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()
# object at 513268 from earlier
start = 513268
# layout from parse_arkbay2: record starts earlier
for s in range(513250, 513280):
    cbid = struct.unpack_from("<i", fam, s+1)[0]
    coid = struct.unpack_from("<i", fam, s+5)[0]
    osize = struct.unpack_from("<i", fam, s+9)[0]
    if coid==15820 and cbid==77:
        body = fam[s+13:s+13+osize]
        print("found spawn record", s, osize)
        p = 56 # after TE24+loc16+rot16
        p += 12 # radius respawn act
        p += 5 # flags
        p += 1 # random
        print("spawn lists at", p)
        for i in range(12):
            lo = body[p]; hi = body[p+1]; p += 4
            st = struct.unpack_from("<i", body, p)[0]; p += 4
            lvl = body[p]; isT = body[p+1]; p += 4
            if st != -1 and st != 0xFFFFFFFF:
                print(f"  slot{i}: lo={lo} hi={hi} type={st} lvl={lvl} isTemplate={isT}")
        break

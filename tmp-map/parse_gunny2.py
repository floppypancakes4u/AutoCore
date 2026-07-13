from pathlib import Path
import struct
fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()

# Parse known entries around gunny2 create chain using parse_arkbay2 style
# Search for coid 13939 (patrol target) and 16462, 16459 etc
for coid in [13939, 15818, 15819, 15820, 16462, 16463, 16464, 14138, 16459, 16499, 16500]:
    raw = struct.pack("<i", coid)
    # find near "SPAWN" or template records - look for coid as field in object table
    # simpler: find all occurrences and print nearby names
    idx=0
    hits=0
    while hits < 3:
        i = fam.find(raw, idx)
        if i < 0: break
        # try name at i+4 etc
        region = fam[max(0,i-20):i+100]
        printable = "".join(chr(b) if 32<=b<127 else "." for b in region)
        if any(c.isalpha() for c in printable):
            print(f"coid {coid} @{i}: {printable[:90]}")
            hits += 1
        idx = i+1

print("--- trigger 16463 reactions ---")
# from earlier hex of 16463: reactions at end
# re-parse trigger 16463 body more carefully using MapDataLoader knowledge

# Extract strings around coll_spawn_gunny2
i = fam.find(b"L1_coll_spawn_gunny2")
if i<0: i = fam.find(b"l1_coll_spawn_gunny2")
print("coll name at", i)
# dump reaction ints after name area - look for sequence of small ints as reaction list
for off in range(i, i+200, 4):
    v = struct.unpack_from("<i", fam, off)[0]
    if 10000 < v < 20000:
        print(f"  @{off}: {v}")

i2 = fam.find(b"l1_rem_creates_gunny2")
print("rem creates at", i2)
for off in range(i2, i2+200, 4):
    v = struct.unpack_from("<i", fam, off)[0]
    if 10000 < v < 20000 or v in (52,4,1,0,2,3):
        print(f"  @{off}: {v}")

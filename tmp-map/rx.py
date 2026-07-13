from pathlib import Path
import struct
fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()
# find reaction 15819 body from parse_arkbay2 off
# coid=15819 off=513145 - parse said size=110
off = 513145
# object header: unknown byte + cbid + coid + size
# start of record: find coid at 513150 area
for start in range(513130, 513160):
    cbid = struct.unpack_from("<i", fam, start+1)[0]
    coid = struct.unpack_from("<i", fam, start+5)[0]
    osize = struct.unpack_from("<i", fam, start+9)[0]
    if coid==15819 and cbid==86:
        body = fam[start+13:start+13+osize]
        print("found start", start, "size", osize)
        name = body[:65].split(b"\x00")[0]
        print("name", name)
        print("type", body[65], "actOn", body[66])
        print("objCheck", struct.unpack_from("<i", body, 67)[0])
        print("doConvoy", body[71])
        print("g1", struct.unpack_from("<i", body, 72)[0], "g2", struct.unpack_from("<f", body, 76)[0], "g3", struct.unpack_from("<i", body, 80)[0])
        print("objCount", struct.unpack_from("<i", body, 84)[0])
        # after objects+nested, doForAllPlayers?
        # Read ReactionTemplate.Read
        break

# print ReactionTemplate.Read
print(Path(r"C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\EntityTemplates\ReactionTemplate.cs").read_text()[400:2000])

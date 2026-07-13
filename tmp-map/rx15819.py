from pathlib import Path
import struct
fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()
# reaction 15819 full parse including DoForAllPlayers
for s in range(513130, 513160):
    cbid = struct.unpack_from("<i", fam, s+1)[0]
    coid = struct.unpack_from("<i", fam, s+5)[0]
    osize = struct.unpack_from("<i", fam, s+9)[0]
    if coid==15819 and cbid==86:
        body = fam[s+13:s+13+osize]
        print("size", osize)
        name = body[:65].split(b"\x00")[0]
        print("name", name)
        rtype = body[65]; act=body[66]
        objcheck = struct.unpack_from("<i", body, 67)[0]
        do_convoy = body[71]
        g1 = struct.unpack_from("<i", body, 72)[0]
        g2 = struct.unpack_from("<f", body, 76)[0]
        g3 = struct.unpack_from("<i", body, 80)[0]
        ocount = struct.unpack_from("<i", body, 84)[0]
        p = 88
        objs = [struct.unpack_from("<i", body, p+4*i)[0] for i in range(ocount)]
        p = 88 + 4*ocount
        rcount = struct.unpack_from("<i", body, p)[0]; p+=4
        rxs = [struct.unpack_from("<i", body, p+4*i)[0] for i in range(max(0,min(rcount,10)))]
        p += 4*max(0,min(rcount,10))
        # mapver 61 >= 8: allCond, condCount, doForAll
        # may need skip for text type
        print("type", rtype, "actOn", act, "objCheck", objcheck, "objs", objs, "nested", rxs)
        print("remaining", body[p:].hex())
        if p < len(body):
            allc = body[p]; p+=1
            if p+4 <= len(body):
                cc = struct.unpack_from("<i", body, p)[0]; p+=4
                print("allCond", allc, "condCount", cc)
                p += 12*max(0,min(cc,8))
                if p < len(body):
                    print("DoForAllPlayers", body[p])
        break
# also coll_spawn_gunny2 16463 reactions
print("--- 16463 ---")
for s in range(540140, 540180):
    cbid = struct.unpack_from("<i", fam, s+1)[0]
    coid = struct.unpack_from("<i", fam, s+5)[0]
    osize = struct.unpack_from("<i", fam, s+9)[0]
    if coid==16463 and cbid==78:
        body = fam[s+13:s+13+osize]
        print("trig size", osize, "hex tail", body[65:140].hex())
        break

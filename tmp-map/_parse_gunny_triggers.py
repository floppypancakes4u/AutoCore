import struct
from pathlib import Path

fam = Path(__file__).with_name("arkbay.fam").read_bytes()
mv = struct.unpack_from("<i", fam, 0)[0]
print("mapver", mv)


def try_parse_trigger(body, mapver=mv):
    if len(body) < 120:
        return None
    p = 0
    loc = struct.unpack_from("<ffff", body, p)
    p += 16
    rot = struct.unpack_from("<ffff", body, p)
    p += 16
    scale = struct.unpack_from("<f", body, p)[0]
    p += 4
    name = body[p : p + 64].split(b"\x00")[0].decode("latin1", "replace")
    p += 64
    if not name or len(name) < 3:
        return None
    # printable-ish
    if sum(1 for c in name if c.isprintable()) < len(name) * 0.8:
        return None
    retrig = struct.unpack_from("<f", body, p)[0]
    p += 4
    actdel = struct.unpack_from("<f", body, p)[0]
    p += 4
    actcnt = struct.unpack_from("<i", body, p)[0]
    p += 4
    ttype = body[p]
    p += 1
    docoll = body[p]
    p += 1
    docond = body[p]
    p += 1
    if mapver >= 44:
        p += 1  # ShowMapTransitionDecals
    doact = body[p]
    p += 1
    allcond = body[p]
    p += 1
    if mapver >= 60:
        p += 1  # ApplyToAllColliders
    if p + 4 > len(body):
        return None
    rcount = struct.unpack_from("<i", body, p)[0]
    p += 4
    if rcount < 0 or rcount > 30 or p + 8 * rcount > len(body):
        return None
    reactions = []
    for _ in range(rcount):
        c = struct.unpack_from("<i", body, p)[0]
        p += 4
        reactions.append(c)
    # targets: bool global + int32 coid padded?
    if p + 4 > len(body):
        return dict(
            name=name,
            scale=scale,
            actcnt=actcnt,
            ttype=ttype,
            docoll=docoll,
            docond=docond,
            doact=doact,
            allcond=allcond,
            reactions=reactions,
            conditions=[],
            pos=loc[:3],
        )
    tcount = struct.unpack_from("<i", body, p)[0]
    p += 4
    # TFID = bool + int32 + pad? ReadFIDFromFile: Global bool, Coid int32 — may pack to 8
    if 0 <= tcount <= 30:
        for _ in range(tcount):
            if p + 5 > len(body):
                break
            p += 1  # global
            p += 4  # coid
            # align?
    conditions = []
    if p + 4 <= len(body):
        # skip padding to align condition count - try a few offsets
        for skip in range(0, 8):
            pp = p + skip
            if pp + 4 > len(body):
                break
            ccount = struct.unpack_from("<i", body, pp)[0]
            if 0 <= ccount <= 8 and pp + 4 + 12 * ccount <= len(body):
                pp += 4
                conds = []
                ok = True
                for _ in range(ccount):
                    left = struct.unpack_from("<i", body, pp)[0]
                    right = struct.unpack_from("<i", body, pp + 4)[0]
                    ctype = body[pp + 8]
                    if left < 0 or left > 500 or right < 0 or right > 500 or ctype > 10:
                        ok = False
                        break
                    conds.append((left, right, ctype))
                    pp += 12
                if ok:
                    conditions = conds
                    p = pp
                    break
    return dict(
        name=name,
        scale=scale,
        actcnt=actcnt,
        ttype=ttype,
        docoll=docoll,
        docond=docond,
        doact=doact,
        allcond=allcond,
        reactions=reactions,
        conditions=conditions,
        pos=loc[:3],
    )


def try_parse_variable_block():
    """Variables section often early in fam after header."""
    # Scan for known var names and capture preceding id/type/value
    results = []
    for name in [
        b"L1_hascreated_gunny2",
        b"L1_hascreated_gunny1",
        b"L1_gunnysioux1_hasdeleted",
        b"l1_boolean_hasactiveobj_whatsascab1",
        b"L1_boolean_hasactiveobjective_final",
        b"l1_playerhealth_percent",
        b"L0_const_1",
        b"l1_gunnyheal_lock",
        b"L1_hascreated_gunny2",
    ]:
        idx = 0
        while True:
            j = fam.find(name, idx)
            if j < 0:
                break
            # id(4) type(1) pad? value(4) initial(4) name(64) — try common layouts
            for back in (9, 13, 8, 12, 5):
                start = j - back
                if start < 0:
                    continue
                vid = struct.unpack_from("<i", fam, start)[0]
                vtype = fam[start + 4]
                if 0 < vid < 500 and vtype < 30:
                    val = struct.unpack_from("<f", fam, start + 5)[0]
                    init = struct.unpack_from("<f", fam, start + 9)[0]
                    results.append((name.decode(), vid, vtype, val, init, start))
                    break
            idx = j + 1
    # unique by name+vid
    seen = set()
    for r in results:
        key = (r[0], r[1])
        if key in seen:
            continue
        seen.add(key)
        print(f"var {r[0]!r} id={r[1]} type={r[2]} val={r[3]} init={r[4]}")


print("--- variables ---")
try_parse_variable_block()

print("--- gunny/scab/heal triggers ---")
i = 0
by = {}
while i < len(fam) - 20:
    cbid = struct.unpack_from("<i", fam, i + 1)[0]
    coid = struct.unpack_from("<i", fam, i + 5)[0]
    osize = struct.unpack_from("<i", fam, i + 9)[0]
    if cbid == 78 and 100 <= osize <= 3000 and 1000 < coid < 200000 and i + 13 + osize <= len(fam):
        body = fam[i + 13 : i + 13 + osize]
        t = try_parse_trigger(body)
        if t and any(k in t["name"].lower() for k in ("gunny", "scab", "heal")):
            if coid not in by or len(body) > by[coid]["_len"]:
                t["_len"] = len(body)
                by[coid] = t
    i += 1

for coid in sorted(by):
    t = by[coid]
    print(
        f"{coid} {t['name']!r} scale={t['scale']:.2f} act={t['actcnt']} "
        f"coll={t['docoll']} cond={t['docond']} onAct={t['doact']} all={t['allcond']} "
        f"rx={t['reactions']} conditions={t['conditions']}"
    )

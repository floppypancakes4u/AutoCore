import binascii, struct
base = 0x60c1b0
hexstr = (
    "81ec00020000568bf1e812a819000fb64e088b94240c020000f30f2c4c8a046a01516aff6870f79d008bc8e800ac1900"
    "508d5424106864f79d0052ff1554669c008b8424280200008b942424020000508d4c241c5152ff1570659c0083c420"
    "8d4424048d50015e8a0883c00184c975f72bc281c400020000c21000"
)
code = binascii.unhexlify(hexstr)

def s32(x: bytes) -> int:
    return struct.unpack('<i', x)[0]

calls = []
for i in range(len(code)-5):
    if code[i] == 0xE8:
        rel = s32(code[i+1:i+5])
        src = base + i
        dst = src + 5 + rel
        calls.append((src, dst, rel))

print(f"len={len(code)} bytes, CALLs={len(calls)}")
for src,dst,rel in calls:
    print(f"0x{src:08X} -> 0x{dst:08X} (rel {rel})")

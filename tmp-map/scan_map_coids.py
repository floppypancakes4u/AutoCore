import struct

for maps in ["maps1.glm", "maps2.glm", "maps3.glm", "maps4.glm"]:
    path = rf"C:\Program Files (x86)\NetDevil\Auto Assault\{maps}"
    try:
        data = open(path, "rb").read()
    except OSError as e:
        print(maps, e)
        continue
    print("===", maps, "size", len(data))
    for c in [6518, 6519, 6520, 6521, 6522, 6523, 6524, 5448, 5216, 2945, 2472]:
        print(c, data.count(struct.pack("<i", c)))

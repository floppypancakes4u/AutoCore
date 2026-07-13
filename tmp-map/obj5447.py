from pathlib import Path
p = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\missions.glm")
data = p.read_bytes()
# find objective 5447
needle = b'ID="5447"'
idx = 0
while True:
    i = data.find(needle, idx)
    if i < 0: break
    start = data.rfind(b"<Mission", max(0, i-5000), i)
    # get objective chunk
    ostart = data.rfind(b"<Objective", max(0, i-200), i)
    oend = data.find(b"</Objective>", i)
    print(data[ostart:oend+12].decode("latin1","replace")[:1500])
    print("--- mission context ---")
    mend = data.find(b"</Mission>", i)
    mstart = data.rfind(b"<Mission", 0, i)
    chunk = data[mstart:min(mstart+800, mend)].decode("latin1","replace")
    print(chunk[:800])
    print("====")
    idx = i+1
    if idx > i+1 and idx-i > 0:
        break
# also search by objective id in nearby

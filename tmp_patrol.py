from pathlib import Path
misc = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\misc.glm").read_bytes()
start = misc.find(b'h_0-1_trackthis_patrol')
print(misc[start:start+1800].decode("latin-1","replace"))

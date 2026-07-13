from pathlib import Path
p = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\missions.glm")
data = p.read_bytes()
needle = b"h_1-1_tas_arkbay_finalexam"
# find Mission element
idx = data.find(needle)
print("needle at", idx)
# walk back to <Mission
start = data.rfind(b"<Mission", 0, idx)
end = data.find(b"</Mission>", idx)
xml = data[start:end+len(b"</Mission>")].decode("latin1", "replace")
out = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\finalexam.xml")
out.write_text(xml, encoding="utf-8")
print(xml)
print("wrote", out, "len", len(xml))

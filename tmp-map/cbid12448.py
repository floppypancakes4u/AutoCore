from pathlib import Path
import struct
# Confirm pad spawn 15820 spawn list and create reaction on arkbay
fam = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tmp-map\arkbay.fam").read_bytes()
# Find creature 12448 clonebase exists
wad = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad")
print("wad", wad.exists(), wad.stat().st_size if wad.exists() else 0)
# search inventory for 12448
inv = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\tools\inventory-catalog\inventory-items.json")
if inv.exists():
    import json
    data = json.loads(inv.read_text(encoding="utf-8"))
    # might be list or dict
    items = data if isinstance(data, list) else data.get("items", data.get("Items", []))
    if isinstance(items, dict):
        print("12448", items.get("12448") or items.get(12448))
    else:
        for it in items:
            if isinstance(it, dict) and (it.get("cbid")==12448 or it.get("id")==12448 or it.get("CBID")==12448):
                print(it)
                break
        else:
            # string search
            s = inv.read_text(encoding="utf-8")
            i = s.find("12448")
            print("idx", i, s[max(0,i-80):i+120] if i>=0 else "not found")

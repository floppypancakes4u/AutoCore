import struct
import os

path = r"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad"
data = open(path, "rb").read()
name_u16 = "h_0-1_tas_backrange_bountyhunterreporttotheloa".encode("utf-16le")
idx16 = data.find(name_u16)
print("utf16 name", idx16)
if idx16 < 4:
    raise SystemExit("not found")

mid = struct.unpack_from("<i", data, idx16 - 4)[0]
print("mission id", mid)

# After Id(4) + Name(130 UTF16 chars? ReadUTF16StringOn(65) = 65*2 = 130)
pos = idx16 + 130
typ = data[pos]
pos += 1
pos += 1  # pad
npc = struct.unpack_from("<i", data, pos)[0]
pos += 4
prio = struct.unpack_from("<i", data, pos)[0]
pos += 4
req_race, req_class = struct.unpack_from("<hh", data, pos)
pos += 4
print("type", typ, "npc giver", npc, "prio", prio, "race", req_race, "class", req_class)

# Walk to objectives is complex; instead extract objective WorldPosition from objective name strings
for obj_name in [
    "h_0-1_bountyhunterreporttotheloa_patrol1",
    "h_0-1_bountyhunterreporttotheloa_deliver1",
]:
    u16 = obj_name.encode("utf-16le")
    i = data.find(u16)
    print("obj", obj_name, "at", i)
    if i < 0:
        continue
    # MissionObjective.ReadNew: QuestId i32, ObjectiveId i32, Sequence byte, pad1, Name UTF16 65, Map UTF16 65, pad2, WorldPosition i32, ContinentObject i32
    # Name starts after QuestId+ObjId+Seq+pad = 4+4+1+1=10 from record start
    rec = i - 10
    quest_id, obj_id = struct.unpack_from("<ii", data, rec)
    seq = data[rec + 8]
    print("  quest", quest_id, "objId", obj_id, "seq", seq)
    # after name 130 + map 130 + pad 2
    after_names = i + 130 + 130 + 2
    world_pos, cont_obj = struct.unpack_from("<ii", data, after_names)
    print("  WorldPosition", world_pos, "ContinentObject", cont_obj)

# Also dump visual waypoints 6518-6524 if in maps
print("search map coids in wad as i64/i32")
for coid in range(6518, 6525):
    # just confirm presence of little-endian int
    b = struct.pack("<i", coid)
    print(coid, "count", data.count(b))

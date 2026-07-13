from pathlib import Path
import struct
# Find mission 3037 binary NPC field from clonebase
wad = Path(r"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad").read_bytes()
# mission name utf16 at known offset 38384372 from earlier
off = 38384372
# Mission.Read: Id i32, Name utf16 65 chars = 130 bytes, Type byte, pad1, NPC i32
# At 38384372 we found id+name - need find actual mission structure start
# Search for utf16 name
name = "h_1-1_tas_arkbay_finalexam".encode("utf-16-le")
i = wad.find(name)
print("name at", i)
# Id is 4 bytes before name
mission_id = struct.unpack_from("<i", wad, i-4)[0]
print("id before name", mission_id)
# After name 65*2=130, Type, pad, NPC
p = i + 130
# might be padded to 65 wchar including nulls
# Name = reader.ReadUTF16StringOn(65) = 65*2 = 130
typ = wad[p]; p+=1
p+=1 # pad
npc = struct.unpack_from("<i", wad, p)[0]
print("type", typ, "NPC", npc)
# Also check if reactions dictionary on map uses coid as key - are reactions for create on map when only in fam?

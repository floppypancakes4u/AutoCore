from pathlib import Path
import struct
# reparse 13939 and 15820 spawn list / pad CBID from fam using MapDataLoader knowledge
# Read SpawnPointTemplate fields from AutoCore
from pathlib import Path
cs = Path(r"C:\Users\josh\Documents\GitHub\AutoCore\src\AutoCore.Game\EntityTemplates\SpawnPointTemplate.cs").read_text(encoding="utf-8", errors="replace")
print(cs[:2500])

# EVIDENCE_INDEX

| Evidence ID | Kind | Location / address | Related systems | Notes |
|-------------|------|--------------------|-----------------|-------|
| EV-GHIDRA-AA | Ghidra DB | AA-decode / autoassault.exe | all | Primary static source |
| EV-DOCS-MOTION | Prior RE | docs/MOTION_CLIENT_RE.md | movement | Pose soft/hard |
| EV-DOCS-INV | Prior RE | docs/inventory-*.md | inventory | Grid footprint |
| EV-DOCS-RESPAWN | Prior RE | Documentation/RESPAWN_SYSTEM.md | death-respawn | Packet layouts |
| EV-DOCS-MISSION | Prior RE | docs/missionState.md | missions | Offsets |
| EV-DOCS-NET | Prior RE | docs/networking.md | comms | Write contract |
| EV-DOCS-XP | Prior RE | docs/XP.md | progression | Formulas (authority) |
| EV-SYS-PROGRESSION | System doc | docs/reconstruction/systems/progression-xp.md | progression | Client S2C handlers UF-005 |
| EV-RAW-00810f00 | Raw capture | docs/reconstruction/raw/aa_exe_00810f00.md | progression | CharacterLevel 0x2017 |
| EV-RAW-0080ae70 | Raw capture | docs/reconstruction/raw/aa_exe_0080ae70.md | progression | GiveXP 0x205F |
| EV-RAW-0080cac0 | Raw capture | docs/reconstruction/raw/aa_exe_0080cac0.md | progression | GiveCredits 0x205E |
| EV-RAW-00533c30 | Raw capture | docs/reconstruction/raw/aa_exe_00533c30.md | progression | AddExperience kernel |
| EV-GHIDRA-COMBAT | Export | tools/ghidra/vehicle_combat_pool.* | combat | Imported |
| EV-DEBUG-HITS | Runtime | docs/debugger-hits/ | createvehicle | Historical |
| EV-RAW-* | Raw captures | docs/reconstruction/raw/* | many | v1 dumps |
| EV-TEST-LOGIC | Unit tests | experiments/test_reconstructed_logic.py | packets/pose | Passing |

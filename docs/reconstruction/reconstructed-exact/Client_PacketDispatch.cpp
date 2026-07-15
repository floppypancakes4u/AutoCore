/*
 * Purpose: Central S2C game opcode dispatch.
 * Stable ID: aa_exe_00815710  Address: 0x00815710
 * Full case map: docs/reconstruction/raw/aa_exe_00815710.md
 */

#include <cstdint>

// Packet header: first uint32 is opcode (dwOpcode)
struct GamePacketHeader {
    uint32_t dwOpcode;
    // ... body varies
};

extern void Client_RecvCharacterLevel(void*, void*);
extern void Client_RecvCreateCharacter();
extern void Client_CreateVehicleObjectApply(void*, int);
extern void Client_RecvDestroyObject(void*);
extern void Client_RecvBroadcast(void*);
extern void Client_RecvSkillStatusEffect(void*);
extern void Client_RecvInventoryGrabResponse(void*, void*);
extern void Client_RecvInventoryDropResponse();
extern void Client_RecvInventoryEquip(void*);
extern void Client_RecvInventoryUnequipNotify();
extern void Client_RecvInventoryUnequipResponse();
extern void Client_RecvInventoryUsePaint(void*);
extern void Client_RecvInventoryUseItemResponse(void*);
extern void Client_RecvInventoryAddItem(void*);
extern void Client_RecvCraftFromAssemblyKitResponse(void*);
extern void Client_RecvUnlockRegion(void*);
extern void Client_RecvGiveCredits(void*, void*);
extern void Client_AwardKillExperience(void*);
extern void Client_RecvGroupReactionCall(void*, void*);
extern void Client_RecvNpcMissionDialog(void*);
extern void Client_RecvCompleteDynamicObjective(void*);
extern void Client_RecvObjectiveState(void*);
extern void Client_RecvSpecialEvent(void*);
// many FUN_* handlers omitted — see raw map

// Pure lookup used by tests: known high-priority opcodes → stable handler id strings
inline const char* PacketDispatch_HandlerName(uint32_t opcode)
{
    switch (opcode) {
    case 0x2017: return "Client_RecvCharacterLevel";
    case 0x201d: return "Client_CreateVehicleObjectApply_0";
    case 0x201e: return "Client_CreateVehicleObjectApply_1";
    case 0x2020: return "Client_RecvDestroyObject";
    case 0x2035:
    case 0x2039: return "Client_RecvInventoryGrabResponse";
    case 0x2037:
    case 0x203b: return "Client_RecvInventoryDropResponse";
    case 0x203c: return "Client_RecvInventoryEquip";
    case 0x203e: return "Client_RecvInventoryUnequipNotify";
    case 0x203f: return "Client_RecvInventoryUnequipResponse";
    case 0x2044: return "Client_RecvInventoryUsePaint";
    case 0x2046: return "Client_RecvInventoryUseItemResponse";
    case 0x2047: return "Client_RecvInventoryAddItem";
    case 0x205b: return "Client_RecvUnlockRegion";
    case 0x205e: return "Client_RecvGiveCredits";
    case 0x205f: return "Client_AwardKillExperience";
    case 0x206c: return "Client_RecvGroupReactionCall";
    case 0x206d: return "Client_RecvNpcMissionDialog";
    case 0x2070: return "Client_RecvCompleteDynamicObjective";
    case 0x2071: return "Client_RecvObjectiveState";
    case 0x20a9: return "Client_RecvSpecialEvent";
    default: return nullptr; // unknown or FUN_* — see full raw map
    }
}

// Binary: returns 1 if handled (including fall-through no-ops), 0 if unknown
inline int PacketDispatch_IsKnownSectorOpcode(uint32_t opcode)
{
    if (opcode == 0x8063 || opcode == 0x9001 || opcode == 0x9004 || opcode == 0x901c)
        return 1;
    if (opcode >= 0x8064 && opcode != 0x9001 && opcode != 0x9004 && opcode != 0x901c)
        return 0;
    // Representative set from switch (not exhaustive for all FUN_*)
    switch (opcode) {
    case 0x2002: case 0x2004: case 0x2005: case 0x2006: case 0x2007:
    case 0x200b: case 0x2010: case 0x2012: case 0x2013: case 0x2014:
    case 0x2015: case 0x2016: case 0x2017: case 0x2018: case 0x2019:
    case 0x201a: case 0x201b: case 0x201c: case 0x201d: case 0x201e:
    case 0x2020: case 0x2021: case 0x2023: case 0x2025: case 0x2026:
    case 0x2028: case 0x202c: case 0x202d: case 0x2031: case 0x2032:
    case 0x2033: case 0x2035: case 0x2037: case 0x2039: case 0x203b:
    case 0x203c: case 0x203e: case 0x203f: case 0x2044: case 0x2046:
    case 0x2047: case 0x2049: case 0x204c: case 0x204d: case 0x204f:
    case 0x2050: case 0x2052: case 0x2054: case 0x2058: case 0x205b:
    case 0x205e: case 0x205f: case 0x2060: case 0x2068: case 0x2069:
    case 0x206b: case 0x206c: case 0x206d: case 0x206f: case 0x2070:
    case 0x2071: case 0x2075: case 0x2076: case 0x2077: case 0x2079:
    case 0x207b: case 0x207d: case 0x207f: case 0x2081: case 0x2084:
    case 0x2086: case 0x2088: case 0x208c: case 0x208e: case 0x2090:
    case 0x2091: case 0x2093: case 0x2098: case 0x209a: case 0x209b:
    case 0x209d: case 0x209f: case 0x20a1: case 0x20a3: case 0x20a6:
    case 0x20a8: case 0x20a9: case 0x20aa: case 0x20ac: case 0x20ad:
    case 0x20af: case 0x20b2: case 0x20b5: case 0x20b7: case 0x20ba:
    case 0x20bc: case 0x20be: case 0x20c0: case 0x20c2: case 0x20c3:
    case 0x20c4: case 0x20c5:
        return 1;
    // fall-through no-op group
    case 0x2003: case 0x2008: case 0x2009: case 0x200a: case 0x200c:
    case 0x200d: case 0x200e: case 0x200f: case 0x2022: case 0x2029:
    case 0x203d: case 0x2040: case 0x2041: case 0x2042: case 0x2043:
    case 0x2067: case 0x206a: case 0x2083: case 0x208a:
        return 1;
    default:
        return 0;
    }
}

// Runtime dispatch body is the large switch in raw — not inlined here to avoid
// duplicating 100+ FUN_* stubs. Pure helpers above are verified against the map.

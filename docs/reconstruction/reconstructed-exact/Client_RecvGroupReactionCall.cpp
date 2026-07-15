/*
 * Purpose: S2C GroupReactionCall (0x206C) — apply decoded reaction/variable batch.
 * Stable ID: aa_exe_008092a0
 * Address: 0x008092a0
 * Confidence: high (control flow), medium (decoded entry field map vs wire bits)
 *
 * Wire is bit-packed (GroupReactionCallPacket). This function runs on a
 * post-decode buffer with stride 0x28 per entry.
 */

#include <cstdint>

struct TFID16 {
    int32_t w[4];
};

extern int*  FUN_004bb160(int a, uint32_t coidLo, uint32_t coidHi);
extern void* Object_ResolveFromTFID(TFID16* tfid);
extern char  TFID_NotEquals(void* a, void* b); // decompile returns char
extern void  CVOGMap_SetVariable(uint32_t id, uint32_t valueBits, int flags);

// Decoded buffer param_2:
//   +0x04 count (u8)
// entry i at param_2 + i*0x28:
//   +0x0c type (0=Reaction, non-zero=Variable)  [also addressed as param_2+0xc+i*0x28]
//   +0x10/14 reaction coid pair OR variable id/value
//   +0x18 activator TFID 16B
//   +0x28 SingleClientOnly-style gate byte (raw: *(entry+0x28); when 0 always fire)

static void* CharacterOrVehicleTfid(void* game, int preferVehicle)
{
    int character = *(int*)((char*)game + 0xe98);
    if (character == 0)
        return nullptr;
    if (preferVehicle) {
        int vehicle = *(int*)(character + 0x250);
        if (vehicle != 0) {
            int vbase = *(int*)(*(int*)(vehicle + 4) + 4);
            return (void*)(vbase + 0x164 + vehicle);
        }
    }
    int cbase = *(int*)(*(int*)(character + 4) + 4);
    return (void*)(cbase + 0x164 + character);
}

static void FireReaction(uint8_t* entry)
{
    int* reactionObj = FUN_004bb160(0,
                                    *(uint32_t*)(entry + 0x10),
                                    *(uint32_t*)(entry + 0x14));
    if (reactionObj == nullptr)
        return;
    void* activator = Object_ResolveFromTFID((TFID16*)(entry + 0x18));
    using ApplyFn = void (*)(int*, void*);
    auto** vt = reinterpret_cast<void**>(*reactionObj);
    ((ApplyFn)vt[0x2c0 / 4])(reactionObj, activator);
}

void Client_RecvGroupReactionCall(void* game, uint8_t* decoded)
{
    uint8_t count = decoded[4];
    if (count == 0)
        return;

    for (uint8_t i = 0; i < count; ++i) {
        uint8_t* entry = decoded + (uint32_t)i * 0x28;
        char type = *(char*)(decoded + 0xc + (uint32_t)i * 0x28);

        if (type != 0) {
            CVOGMap_SetVariable(*(uint32_t*)(entry + 0x10),
                                *(uint32_t*)(entry + 0x14),
                                0);
            continue;
        }

        // Reaction
        char singleOnly = *(char*)(entry + 0x28);
        if (singleOnly == 0) {
            FireReaction(entry);
            continue;
        }

        // SingleClientOnly: fire only when activator TFID matches local vehicle or character
        int character = *(int*)((char*)game + 0xe98);
        if (character == 0)
            continue;

        int vehicle = *(int*)(character + 0x250);
        if (vehicle == 0) {
            void* charTfid = CharacterOrVehicleTfid(game, /*preferVehicle=*/0);
            if (TFID_NotEquals(entry + 0x18, charTfid) == 0)
                FireReaction(entry);
            continue;
        }

        int vbase = *(int*)(*(int*)(vehicle + 4) + 4);
        void* vehTfid = (void*)(vbase + 0x164 + vehicle);
        if (TFID_NotEquals(entry + 0x18, vehTfid) == 0) {
            FireReaction(entry);
            continue;
        }

        void* charTfid = CharacterOrVehicleTfid(game, 0);
        if (TFID_NotEquals(entry + 0x18, charTfid) == 0)
            FireReaction(entry);
    }
}

/*
 * Purpose: S2C NpcMissionDialog (0x206D) — open NPC mission offer/turn-in UI.
 * Stable ID: aa_exe_00815070
 * Address: 0x00815070
 * Confidence: high (packet offsets + UI call), probable (mission hash helpers)
 */

#include <cstdint>

// Absolute packet layout (opcode at +0):
//   +0x00 opcode 0x206D
//   +0x08 NPC TFID 16B
//   +0x18 count i32 (loop uses low byte only)
//   +0x20 entries[count] stride 40:
//         +0 missionId i32
//         +8 eight item coid i32 (-1 empty)

extern void* FUN_004bb070(void* npc_tfid_at_pkt_plus_8);
extern void  FUN_0052d8b0(int a, int b);
extern int*  FUN_0053fff0(); // mission template hash
extern void  FUN_0052c700(void* missionField0, int32_t itemSlotsInit[/*11*/]);
extern void  Client_ShowNpcMissionDialogUI(void* game, void* npcObj, char openFlag);

void Client_RecvNpcMissionDialog(void* game, uint32_t* pkt /* unaff_EBX */)
{
    // pkt+2 dwords = absolute +0x08 NPC TFID
    void* npcObj = FUN_004bb070(pkt + 2);
    FUN_0052d8b0(0, -1);

    uint8_t count = *reinterpret_cast<uint8_t*>(reinterpret_cast<char*>(pkt) + 0x18);
    uint32_t* cursor = pkt;

    for (int i = 0; i < (int)count; ++i) {
        // mission id at entry dword[8] relative to packet base on first pass (+0x20),
        // then cursor advances by 10 dwords (40 bytes) each iteration.
        uint32_t missionId = cursor[8];

        int* hashRoot = FUN_0053fff0();
        int table = *hashRoot;
        void* missionNodePayload = nullptr;

        if (table != 0) {
            int chain = *(int*)(*(int*)(table + 0x10) +
                                (*(uint32_t*)(table + 8) & missionId) * 4 + 4);
            // raw: *( *(table+0x10) + (mask&id)*4 ) + 4
            int bucketHead = *(int*)(*(int*)(table + 0x10) +
                                     (*(uint32_t*)(table + 8) & missionId) * 4);
            chain = *(int*)(bucketHead + 4);
            while (chain != 0) {
                if (missionId == *(uint32_t*)(chain + 0x10)) {
                    missionNodePayload = *(void**)(chain + 8);
                    break;
                }
                chain = *(int*)(chain + 0xc);
            }
        }

        if (missionNodePayload != nullptr) {
            int32_t itemSlots[11];
            for (int k = 0; k < 11; ++k)
                itemSlots[k] = -1;
            // item coids: cursor+10 dwords (absolute +0x28 on first entry)
            uint32_t* src = cursor + 10;
            for (int k = 0; k < 8; ++k)
                itemSlots[k] = (int32_t)src[k];

            // raw: FUN_0052c700(*puVar5, local_30) where puVar5 is hash node payload
            FUN_0052c700(*(void**)missionNodePayload, itemSlots);
        }

        cursor += 10; // stride 40 bytes
    }

    Client_ShowNpcMissionDialogUI(game, npcObj, /*openFlag=*/1);
}

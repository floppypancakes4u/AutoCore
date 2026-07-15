/*
 * Purpose: C2S MissionDialogResponse (0x206E) size 0x20 from dialog object buffer.
 * Stable IDs:
 *   prepare opcode — aa_exe_008abd70 @ 0x008abd70
 *   fill fields    — aa_exe_008ae7c0 @ 0x008ae7c0 (partial)
 *   send           — aa_exe_008ab8f0 @ 0x008ab8f0
 * Confidence: high for layout + send; high for opcode assignment
 */

#include <cstdint>

// Dialog object layout (protocol-relevant):
//   +0x644  NPC object*
//   +0x648  dialog state (0..3)
//   +0x64c  turn-in mode flag
//   +0x650  opcode (0x206E) — prepared by Client_NpcDialog_PrepareResponseOpcode
//   +0x654  missionId i32
//   +0x658  accepted/button i32 (often 0 on OK live captures)
//   +0x65c  pad / hi dword
//   +0x660  MissionGiver TFID 16B
//   +0x670  selected mission def*

struct MissionDialogResponsePacket {
    uint32_t opcode;      // +0x00 = 0x206E
    int32_t  missionId;   // +0x04
    int32_t  accepted;    // +0x08 (bool + pad7 in server reader)
    int32_t  pad0c;       // +0x0c
    int32_t  giverTfid[4];// +0x10
}; // 0x20 total

extern void* g_pSectorNetConnection_INFERRED;
extern void  FUN_008aa320();
extern void  FUN_00792490();

// --- Prepare (0x008abd70) ---
void Client_NpcDialog_PrepareResponseOpcode(void* dialog /*unaff_ESI*/, void* missionDef)
{
    *(void**)((char*)dialog + 0x670) = missionDef;
    *(uint32_t*)((char*)dialog + 0x650) = 0x206E;
    // ... remaining body is UI chrome refresh (omitted — not wire protocol)
}

// --- Fill from HandleButton state 1 (0x008ae7c0 excerpt) ---
void Client_MissionDialog_FillResponseFields(void* dialog,
                                             int buttonIndex,
                                             int32_t missionId,
                                             void* npcObjectOrNull)
{
    *(int32_t*)((char*)dialog + 0x654) = missionId;

    if (npcObjectOrNull == nullptr) {
        *(uint8_t*)((char*)dialog + 0x668) = 0;
        *(int32_t*)((char*)dialog + 0x660) = -1;
        *(int32_t*)((char*)dialog + 0x664) = -1;
    } else {
        int base = *(int*)(*(int*)((char*)npcObjectOrNull + 4) + 4);
        int32_t* tfid = (int32_t*)(base + 0x164 + (int)(intptr_t)npcObjectOrNull);
        // raw uses object+0x160 via *( *(obj+4)+4 ) + 0x164 + obj
        auto* dst = (int32_t*)((char*)dialog + 0x660);
        dst[0] = tfid[0];
        dst[1] = tfid[1];
        dst[2] = tfid[2];
        dst[3] = tfid[3];
    }

    // accepted field: button index (retail OK often 0 / false)
    *(int32_t*)((char*)dialog + 0x658) = buttonIndex;
    *(int32_t*)((char*)dialog + 0x65c) = buttonIndex >> 31;
}

// --- Send + dialog teardown (0x008ab8f0) ---
void Client_SendMissionDialogResponse(void* dialog /*param_1 int* */)
{
    auto* pkt = (MissionDialogResponsePacket*)((char*)dialog + 0x650);

    if (pkt->opcode != 0 && g_pSectorNetConnection_INFERRED != nullptr) {
        auto** vt = *(void***)g_pSectorNetConnection_INFERRED;
        using SendFn = void (*)(void*, int, void*, int, int);
        // (**(conn+0x18))(0xffffffff, dialog+0x650, 0x20, 0)
        ((SendFn)vt[0x18 / 4])(g_pSectorNetConnection_INFERRED, -1, pkt, 0x20, 0);
    }

    // UI teardown / optional related-window hide (DAT_00d1d8dc path omitted detail)
    FUN_008aa320();
    {
        auto** dvt = *(void***)dialog;
        using VFn = void (*)(void*);
        ((VFn)dvt[0x3ac / 4])(dialog);
    }
    FUN_00792490();
}

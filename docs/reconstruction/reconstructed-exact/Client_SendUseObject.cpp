/*
 * Purpose: C2S UseObject (0x2072) for world/NPC interaction.
 * Stable ID: aa_exe_00916740
 * Address: 0x00916740
 * Packet size: 0x20 including opcode
 * Confidence: high
 */

#include <cstdint>

struct UseObjectPacket {
    uint32_t opcode;       // 0x2072
    uint32_t pad;          // +4
    int32_t  tfid[4];      // +8  16-byte TFID from object+0x160
    int32_t  objectiveId;  // +0x18 or -1
};

extern int  Client_FindObjectiveMatchingTarget(int targetKey);
extern void* g_pSectorNetConnection_INFERRED;
// connection vtable +0x18 = send(guaranteed?, buf, size, flags)

void Client_SendUseObject(void* game /*param_1*/, int object /*in_EAX*/)
{
    *(int*)((char*)game + 0xd28) = object;

    UseObjectPacket pkt{};
    pkt.opcode = 0x2072;
    pkt.tfid[0] = *(int*)(object + 0x160);
    pkt.tfid[1] = *(int*)(object + 0x164);
    pkt.tfid[2] = *(int*)(object + 0x168);
    pkt.tfid[3] = *(int*)(object + 0x16c);

    int cloneField = *(int*)(*(int*)(object + 0xa8) + 0x34);
    int objRec = Client_FindObjectiveMatchingTarget(cloneField);
    pkt.objectiveId = (objRec == 0) ? -1 : *(int*)(objRec + 0x10);

    if (g_pSectorNetConnection_INFERRED != nullptr) {
        auto** vt = *(void***)g_pSectorNetConnection_INFERRED;
        using SendFn = void (*)(void*, int, void*, int, int);
        // (**(code **)(conn + 0x18))(0xffffffff, local_20, 0x20, 0);
        ((SendFn)vt[0x18 / 4])(g_pSectorNetConnection_INFERRED, -1, &pkt, 0x20, 0);
    }
}

/*
 * Alt path: Client_SendUseObject_IfInteractable @ 0x00930d70
 * Only if interactable (FUN_00524520) OR clone type == 4; and *(game+0xe04)+0xf6 == 0.
 * Does NOT attach objective id (local_8 used differently / not set like primary path).
 * Returns 1 on send, 0 otherwise.
 */

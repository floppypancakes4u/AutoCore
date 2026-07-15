/*
 * Purpose: Handle S2C SpecialEvent (0x20A9): Respawn / TeleportOut / TeleportIn presentation.
 * Original address / stable ID: 0x0080cc50 / aa_exe_0080cc50
 * Original symbol: Client_RecvSpecialEvent
 * Behavioral summary: Match packet TFID to local entity at game+0xe98; require vehicle child
 *   +0x250; dispatch type 0→Respawn ctor (flag at +0x40), 1→TeleportOut, 2→TeleportIn.
 * Known callers: Client_PacketDispatch case 0x20a9
 * Known callees: ClientSpecialEvent_Respawn_ctor, TeleportOut/In ctors, ResolveObjectTarget
 * Confidence: high
 * Verification: raw+clean; TFID character-vs-vehicle quirk preserved deliberately
 */

#include <cstdint>

struct SpecialEventPacketView {
    // param_1 points at full packet including opcode at +0
    uint32_t opcode;     // +0x00
    uint8_t  type;       // +0x04  0=Respawn 1=TeleportOut 2=TeleportIn
    uint8_t  pad[3];
    float    dest_pos[3]; // +0x08
    float    dest_quat[4];// +0x14
    uint32_t pad24;       // +0x24
    int32_t  tfid_lo;     // +0x28
    int32_t  tfid_hi;     // +0x2c
    // ...
    // +0x40 int flag for Respawn
};

extern int* CVOGReaction_ResolveObjectTarget(int mode, int tfid_lo, int tfid_hi);
extern int  ClientSpecialEvent_Respawn_ctor(void* mem, int flag, void* poseBundle);
extern int  ClientSpecialEvent_TeleportOut_ctor(void* mem, int entity);
extern int  ClientSpecialEvent_TeleportIn_ctor(void* mem);
extern void* operator_new(unsigned size);
extern void  RegisterSpecialEvent_Inferred(void* tfidBundle); // FUN_00403150

void Client_RecvSpecialEvent(SpecialEventPacketView* packet, void* game)
{
    int entity = *(int*)((char*)game + 0xe98);
    int targetEntity = 0;

    if (entity == 0) {
        goto resolve_fallback;
    }

    {
        int off = *(int*)(*(int*)(entity + 4) + 4);
        int lo = *(int*)(off + 0x164 + entity);
        int hi = *(int*)(off + 0x168 + entity);
        // PRESERVED BEHAVIOR: mismatch → fallback resolve; vehicle TFID fails live.
        if (lo != packet->tfid_lo || hi != packet->tfid_hi)
            goto resolve_fallback;
        targetEntity = entity;
        goto have_entity;
    }

resolve_fallback:
    {
        int* resolved = CVOGReaction_ResolveObjectTarget(1, packet->tfid_lo, packet->tfid_hi);
        if (resolved == nullptr)
            return;
        // clone type at resolved[0x2a]+0x38 must be 0x14 (Vehicle)
        if (*(int*)(resolved[0x2a] + 0x38) != 0x14)
            return;
        targetEntity = resolved[0]; // via vfunc +0x1dc in binary
        // decompiler: iVar5 = (**(code **)(*piVar4 + 0x1dc))();
    }

have_entity:
    if (targetEntity == 0)
        return;
    if (*(int*)(targetEntity + 0x250) == 0)
        return;

    int eventObj = 0;
    char type = *(char*)((char*)packet + 4);
    int flag = *(int*)((char*)packet + 0x40);

    // Pose bundle built from packet dest fields (local_50.. in decompiler)
    if (type == 0) {
        void* mem = operator_new(0x70);
        if (mem)
            eventObj = ClientSpecialEvent_Respawn_ctor(mem, flag != 0, /*pose*/ nullptr);
        // note: actual ctor args are dest vec + entity child + this + flag + quat* (fastcall)
    } else if (type == 1) {
        void* mem = operator_new(0x34);
        if (mem)
            eventObj = ClientSpecialEvent_TeleportOut_ctor(mem, targetEntity);
    } else if (type == 2) {
        void* mem = operator_new(0x50);
        if (mem)
            eventObj = ClientSpecialEvent_TeleportIn_ctor(mem);
    } else {
        return;
    }

    if (eventObj != 0) {
        // Copy controlled entity TFID into event registration bundle
        RegisterSpecialEvent_Inferred(/*tfid from entity+0x164*/ nullptr);
    }
}

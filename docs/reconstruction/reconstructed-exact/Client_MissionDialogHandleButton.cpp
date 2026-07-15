/*
 * Purpose: Mission dialog UI button router (states 0–3).
 * Stable ID: aa_exe_008ae7c0
 * Address: 0x008ae7c0
 * Confidence: high for state machine + local complete path; probable for reward masks
 */

#include <cstdint>

extern int   DAT_00d1b6d8; // character global
extern void  FUN_007a69d0();
extern char* FUN_007a6de0(const char* s, int);
extern void  FUN_007fdfb0(void* game, void* msg, int, int, int);
extern void  Client_SendSectorPacket(void* game, short size, void* buffer);
extern void  Client_ShowNpcMissionDialogUI(void* game, void* npc, char flag);
extern void  Client_HideMissionDialogIfOpen();
extern void  Client_MaybeShowFirstTimeTip(int tipId);
extern void  Client_RefreshMissionDialogChrome();
extern void  Client_RefreshOpenMissionUiWindows(void* game);
extern void  Client_ShowMissionRewardChatToast(int objective);
extern void  CVOGReaction_GiveMission(int missionId);
extern char  CVOGReaction_CompleteObjective(int objId, int a, int b, int force);
extern int   CVOGReaction_ResolveObjectTarget(int global, uint32_t lo, uint32_t hi);
extern char  FUN_005714e0(int item, void*, void*, int, int);
extern void  FUN_008ac7a0();
extern void  FUN_0092fd00();
extern void  FUN_008f8200(int, int, void*, char*, int);
extern int   DAT_00d1b8dc;
extern char  DAT_00d1b216;
extern int   DAT_00d1ad10;
extern int   DAT_00d1b4b4;
extern char  DAT_00d1a840[]; // game root

// dialog+0x648 states:
//   0 → C2S 0x206F size 0x18 (mission accept-request)
//   1 → accept offer (GiveMission) OR turn-in (CompleteObjective local)
//   2 → abandon confirmation prompt
//   3 → re-show NPC dialog UI
//
// first arg is button/choice index (not a dialog pointer). dialog is in_EAX.

char Client_MissionDialogHandleButton(int buttonIndex, int /*unused_iButtonIndex*/)
{
    void* dialog = nullptr; // raw: in_EAX — supplied by caller thiscall/reg
    // For reconstruction readability, assume dialog is recovered as in_EAX.
    // Callers: FUN_008aec40, FUN_008af020.

    if (DAT_00d1b6d8 == 0)
        return 0;

    FUN_007a69d0();

    // Gate: dialog+0x708 + buttonIndex*4 must be non-zero (button enabled)
    // *(int*)(dialog + 0x708 + buttonIndex*4)
    // If secondary widget busy (dialog+0x6e0), cancel and return 0.

    int state = *(int*)((char*)dialog + 0x648);

    if (state == 2) {
        if (buttonIndex == 1) {
            // Confirm abandon: store mission id into DAT_00d1b4b4, show confirm UI
            void** mission = *(void***)((char*)dialog + 0x670);
            DAT_00d1b4b4 = (mission == nullptr) ? -1 : **(int**)mission;
            // sprintf "Are you sure you wish to abandon \"%s\"?"
            FUN_007fdfb0(&DAT_00d1a840, /*msg*/, 0x4e47, 1, 0);
            return 0;
        }
        return 1;
    }

    if (state == 0) {
        // C2S 0x206F size 0x18
        struct {
            uint32_t opcode;
            uint32_t a;
            uint32_t b;
            uint8_t  button;
            uint8_t  pad[3];
        } pkt{};
        pkt.opcode = 0x206F;
        pkt.a = *(uint32_t*)((char*)dialog + 0x678);
        pkt.b = *(uint32_t*)((char*)dialog + 0x67c);
        pkt.button = (uint8_t)buttonIndex;
        Client_SendSectorPacket(&DAT_00d1a840, 0x18, &pkt);
        return 1;
    }

    if (state == 3) {
        Client_ShowNpcMissionDialogUI(&DAT_00d1a840,
                                         *(void**)((char*)dialog + 0x644),
                                         0);
        return 0;
    }

    if (state != 1 || *(int*)((char*)dialog + 0x670) == 0)
        return 1;

    char turnIn = *(char*)((char*)dialog + 0x64c);

    // Reward selection gate when turn-in and masks incomplete
    if (turnIn != 0) {
        uint32_t m0 = *(uint32_t*)((char*)dialog + 0x558);
        uint32_t m1 = *(uint32_t*)((char*)dialog + 0x55c);
        uint32_t r0 = *(uint32_t*)((char*)dialog + 0x578);
        uint32_t r1 = *(uint32_t*)((char*)dialog + 0x57c);
        if ((m0 & m1) != 0xffffffffu && (r0 & r1) == 0xffffffffu) {
            auto* msg = FUN_007a6de0("You need to select a reward first!", -1);
            FUN_007fdfb0(&DAT_00d1a840, msg, -1, 1, 0);
            return 0;
        }
        // inventory space check for selected reward item if r0/r1 valid
        if ((r0 & r1) != 0xffffffffu) {
            int item = CVOGReaction_ResolveObjectTarget(1, r0, r1);
            if (item != 0) {
                char a = 0, b = 0;
                if (FUN_005714e0(item, &a, &b, 1, -1) == 0) {
                    auto* msg = FUN_007a6de0(
                        "Your inventory is full. You must have space in your inventory to receive the mission reward.",
                        -1);
                    FUN_007fdfb0(&DAT_00d1a840, msg, -1, 1, 0);
                    return 0;
                }
            }
        }
    }

    // Fill MissionGiver TFID at dialog+0x660 from dialog+0x644 NPC object
    int npc = *(int*)((char*)dialog + 0x644);
    if (npc == 0) {
        *(uint8_t*)((char*)dialog + 0x668) = 0;
        *(int32_t*)((char*)dialog + 0x660) = -1;
        *(int32_t*)((char*)dialog + 0x664) = -1;
    } else {
        int base = *(int*)(*(int*)(npc + 4) + 4);
        int32_t* src = (int32_t*)(base + 0x164 + npc);
        auto* dst = (int32_t*)((char*)dialog + 0x660);
        dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; dst[3] = src[3];
    }

    int* missionDef = *(int**)((char*)dialog + 0x670);
    *(int32_t*)((char*)dialog + 0x654) = *missionDef; // mission id

    if (turnIn == 0) {
        // Offer accept path — prepare accepted field; local GiveMission if button==0
        *(int32_t*)((char*)dialog + 0x658) = buttonIndex;
        *(int32_t*)((char*)dialog + 0x65c) = buttonIndex >> 31;
        if (buttonIndex == 0 && DAT_00d1b6d8 != 0) {
            CVOGReaction_GiveMission(*missionDef);
            // optional FUN_0092fd00 for special mission flags
            Client_HideMissionDialogIfOpen();
            Client_MaybeShowFirstTimeTip(2);
            FUN_008ac7a0();
        }
        // 0x206E send occurs on dialog teardown (Client_SendMissionDialogResponse)
        return 1;
    }

    // Turn-in / complete path
    *(int32_t*)((char*)dialog + 0x658) = *(int32_t*)((char*)dialog + 0x578);
    *(int32_t*)((char*)dialog + 0x65c) = *(int32_t*)((char*)dialog + 0x57c);

    int seqIdx = *(uint8_t*)(missionDef + 0x4c);
    int objective = *(int*)(missionDef[0x4f] - 4 + seqIdx * 4);
    if (objective == 0)
        return 1;

    // toast "Finished Mission \"...\""
    Client_ShowMissionRewardChatToast(objective);
    char ok = CVOGReaction_CompleteObjective(
        *(int*)(objective + 0x10),
        *(int*)((char*)dialog + 0x578),
        *(int*)((char*)dialog + 0x57c),
        /*force=*/0);
    if (ok == 0)
        return 0;

    Client_RefreshMissionDialogChrome();
    Client_HideMissionDialogIfOpen();
    Client_RefreshOpenMissionUiWindows(&DAT_00d1a840);
    return 1;
}

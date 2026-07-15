/*
 * Purpose: Activate quickbar slot (skill / item / power) or on-foot special slots.
 * Stable ID: aa_exe_009436c0
 * Address: 0x009436c0
 */

#include <cstdint>

extern void Client_CastSkillFromQuickBarSlot(int skillId);
extern void Client_QuickBarActivateSkillSlot(char slot);
extern void Input_TryFireSecondaryWeapons();
extern void FUN_00922270(); // primary fire on-foot vehicle path
extern void FUN_00941d50(int);
extern void FUN_008a0ed0(); // mode==1 branch

void Client_QuickBar_ActivateSlot(void* client /*in_EAX*/, char slot, char mode, char page)
{
    int entity = *(int*)((char*)client + 0xe98);
    // On-foot / special UI present path:
    if (entity != 0) {
        char f6b9 = *(char*)(entity + 0x6b9);
        char f6b8 = *(char*)(entity + 0x6b8);
        int ui = *(int*)((char*)client + 0xf38);
        if ((f6b9 != 0 || f6b8 != 0) && ui != 0 /* && ui visible */) {
            if (slot == 0) {
                if (f6b9 != 0) { Client_QuickBarActivateSkillSlot(0); return; }
                FUN_00922270();
                return;
            }
            if (slot == 1) {
                if (f6b9 != 0) { Client_QuickBarActivateSkillSlot(1); return; }
                Input_TryFireSecondaryWeapons();
                return;
            }
        }
    }

    int qbUi = *(int*)((char*)client + 0x10b0);
    if (qbUi == 0)
        return;

    int pageIdx = (page == -1) ? *(int*)(qbUi + 0x50c) : (int)page;
    if (mode == 1) {
        FUN_008a0ed0();
        return;
    }

    int index = (int)slot + pageIdx * 10;
    char* reentrancy = (char*)client + 0x3b80 + index;
    if (*reentrancy != 0)
        return;
    *reentrancy = 1;

    int type = *(int*)((char*)client + 0x3220 + index * 0x18);
    // skill id / item id at (index*3+0x645)*8
    int idSlot = (index * 3 + 0x645) * 8;
    int id = *(int*)((char*)client + idSlot);

    if (type == 1) {
        Client_CastSkillFromQuickBarSlot(id);
    } else if (type == 2) {
        // item use via cargo lookup FUN_005710c0; reject short==8 at clone+0x3f4
        // FUN_00941d50(1)
    } else if (type == 5) {
        // power: time gate + FUN_00941fb0 chat/power lines
    }
    *reentrancy = 0;
}

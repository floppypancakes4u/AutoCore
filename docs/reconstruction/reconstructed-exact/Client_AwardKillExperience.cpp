/*
 * Purpose: S2C GiveXP (0x205F) — apply XP + optional level hint + XP floater.
 * Stable ID: aa_exe_0080ae70
 * Evidence: raw/aa_exe_0080ae70.md
 *
 * Floater stack named by Ghidra as distance from frame pointer:
 *   uStack_38 .. uStack_18 are consecutive uint32 at +0x00..+0x20 from &uStack_38
 *   uStack_10 is at +0x28 (0x38-0x10)
 *   uStack_8  is at +0x30 (0x38-0x08)  ← type 3 MUST live here
 * Span from &uStack_38 through uStack_8 inclusive: 0x34 bytes.
 */

#include <cstdint>
#include <cstddef>
#include <cstring>

#ifdef _WIN32
#include <windows.h>
#else
unsigned long GetTickCount();
#endif

enum XpIsKillPath : int {
    PacketOrNonKill = 0,
    KillPath = 1,
};

extern bool CVOGReaction_AddExperience(void* character, int amount, XpIsKillPath path);
extern void Client_EnqueueCombatFloater_INFERRED(void* floaterBase /* &uStack_38 */);
extern void FUN_007a4480(int, const char*);
extern uint32_t DAT_00a1e840, DAT_00a1e844, DAT_00a1e848, DAT_00a1e84c;

// Layout: offsetof(uStack_8) == 0x30, sizeof == 0x34
struct GiveXpFloaterStack {
    uint32_t uStack_38; // +0x00 color0
    uint32_t uStack_34; // +0x04 color1
    uint32_t uStack_30; // +0x08 color2
    uint32_t uStack_2c; // +0x0c color3
    uint32_t uStack_28; // +0x10 tfid0
    uint32_t uStack_24; // +0x14 tfid1
    uint32_t uStack_20; // +0x18 tfid2
    uint32_t uStack_1c; // +0x1c tfid3
    uint32_t uStack_18; // +0x20 amount
    uint32_t hole_24;   // +0x24  (fills to uStack_10 at +0x28)
    uint8_t  uStack_10; // +0x28 flag = 0
    uint8_t  pad_29[7]; // +0x29..+0x2f  (7 bytes → uStack_8 at +0x30)
    uint32_t uStack_8;  // +0x30 type = 3
};

static_assert(offsetof(GiveXpFloaterStack, uStack_18) == 0x20, "amount offset");
static_assert(offsetof(GiveXpFloaterStack, uStack_10) == 0x28, "flag offset");
static_assert(offsetof(GiveXpFloaterStack, uStack_8) == 0x30, "type must be at +0x30");
static_assert(sizeof(GiveXpFloaterStack) == 0x34, "floater stack span");

void Client_AwardKillExperience(void* game, const uint8_t* packetBase)
{
    void* character = *(void**)((char*)game + 0xe98);
    if (character == nullptr) {
        FUN_007a4480(0, "VOG_DEBUG_STOP");
        return;
    }

    int amount = *(const int32_t*)(packetBase + 4);
    CVOGReaction_AddExperience(character, amount, PacketOrNonKill);

    int8_t levelHint = *(const int8_t*)(packetBase + 8);
    if (levelHint != -1) {
        *(int8_t*)((char*)character + 0x738) = levelHint;
        *(uint32_t*)((char*)character + 0x734) = (uint32_t)GetTickCount();
    }

    int vehicle = *(int*)((char*)character + 0x250);
    if (vehicle == 0)
        return;

    // entity resolve: vfunc +0x1c8 then + offset table (raw)
    int entity =
        (**(int (**)())(
            *(int*)(*(int*)(*(int*)(vehicle + 4) + 4) + 4 + vehicle) + 0x1c8))();
    entity = entity + *(int*)(*(int*)(entity + 4) + 4);

    GiveXpFloaterStack frame{};
    frame.uStack_38 = DAT_00a1e840;
    frame.uStack_34 = DAT_00a1e844;
    frame.uStack_30 = DAT_00a1e848;
    frame.uStack_2c = DAT_00a1e84c;
    frame.uStack_28 = *(uint32_t*)(entity + 0x164);
    frame.uStack_24 = *(uint32_t*)(entity + 0x168);
    frame.uStack_20 = *(uint32_t*)(entity + 0x16c);
    frame.uStack_1c = *(uint32_t*)(entity + 0x170);
    frame.uStack_18 = (uint32_t)amount;
    frame.hole_24 = 0;
    frame.uStack_10 = 0;
    frame.uStack_8 = 3; // XP — at offsetof 0x30
    Client_EnqueueCombatFloater_INFERRED(&frame);
}

enum : uint32_t { kGiveXpOpcode = 0x205F };

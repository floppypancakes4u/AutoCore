/*
 * Purpose: Grant mission template to character (active hashes + first objective).
 * Original address / stable ID: 0x005327C0 / aa_exe_005327c0
 * Original symbol: CVOGReaction_GiveMission
 * Behavioral summary: Lookup template; reject if disabled/already active; prereq completed
 *   hashes; AddActiveObjective; UnlockContinentObject; insert mission; optional UI toast.
 * Known callees: CNDHash_LookupByKey, CVOGMission_AddActiveObjective,
 *   CVOGReaction_UnlockContinentObject, FUN_0053c360, Client_PlayNamedInterfaceSound
 * State read: template tables; char+0x540/0x548/0x538/0x53c
 * State modified: active mission/objective hashes; continent unlock
 * Confidence: high (control flow from raw); hash node alloc detail probable
 * Verification: raw aa_exe_005327c0.md; static review
 */

#include <cstdint>
#include <cstring>
#include <cstdio>

extern void* CNDHash_LookupByKey(void* hash, uint32_t key);
extern void* FUN_0053fff0(); // mission template table holder
extern void  FUN_00547920(int); // UI busy / lock
extern void  CVOGMission_AddActiveObjective(void* objective);
extern void  CVOGReaction_UnlockContinentObject(void* character, uint32_t continentObjId);
extern char  CVOGCharacter_WeaponAllowsKillXpBonus();
extern void  FUN_0053c360(uint32_t missionId, void* templateRec, int);
extern void  FUN_00538b20(uint32_t missionId, int);
extern void* operator_new(unsigned size);
extern void* FUN_004111f0(); // mission runtime record ctor
extern void  FUN_00538a40(int* out, uint32_t* missionId);
extern void  FUN_0053c660(uint32_t, void* rec, int);
extern void  FUN_0052d8b0(int, uint32_t);
extern void  FUN_007a69d0();
extern void* FUN_007a6de0(const char*, int);
extern void  FUN_0040c5c0(char*);
extern void  Client_GetMissionCompleteAudioTable(const char*, ...);
extern void  Client_PlayNamedInterfaceSound(const char*, ...);
extern void  FUN_007a4480(int, const char*, ...);

// Returns 1 on grant success, 0 on failure.
uint32_t CVOGReaction_GiveMission(void* character, uint32_t missionId)
{
    // Template table root
    void** tableHolder = (void**)FUN_0053fff0();
    if (tableHolder == nullptr || *tableHolder == nullptr)
        return 0;

    // template = hash lookup by missionId
    uint32_t* templateRec = (uint32_t*)CNDHash_LookupByKey(*tableHolder, missionId);
    if (templateRec == nullptr)
        return 0;

    // enabled flag at template+0x4c (as byte on word index 0x4c of uint32* in decompiler)
    if (*(char*)(templateRec + 0x4c) == 0)
        return 0;

    FUN_00547920(1);
    if (*(char*)(templateRec + 0x5a) == 0)
        FUN_00547920(0);

    // Already active at character+0x540?
    if (CNDHash_LookupByKey(*(void**)((char*)character + 0x540), missionId) != nullptr)
        return 0; // outer: only enters grant when active lookup is null

    // Prereq: if short at template+0x2b != -1, completed-hash gates
    if (*(short*)(templateRec + 0x2b) != -1) {
        char allowBonus = CVOGCharacter_WeaponAllowsKillXpBonus();
        if (allowBonus == 0) {
            if (CNDHash_LookupByKey(*(void**)((char*)character + 0x538), missionId) != nullptr)
                return 0;
        }
        allowBonus = CVOGCharacter_WeaponAllowsKillXpBonus();
        if (allowBonus != 0) {
            if (CNDHash_LookupByKey(*(void**)((char*)character + 0x53c), missionId) != nullptr)
                return 0;
        }
    }

    // First objective from template[0x4f]
    int* objListHead = (int*)templateRec[0x4f];
    uint32_t objectiveId = *(uint32_t*)(*objListHead + 0x10);
    void* existingObj = CNDHash_LookupByKey(*(void**)((char*)character + 0x548), objectiveId);
    if (existingObj == nullptr) {
        CVOGMission_AddActiveObjective((void*)*objListHead);
    } else {
        int off = *(int*)(*(int*)((char*)character + 4) + 4);
        FUN_007a4480(1, "Already had objective %l for COID %I64d\n",
                     objectiveId,
                     *(uint32_t*)(off + 0x164 + (int)character),
                     *(uint32_t*)(off + 0x168 + (int)character));
    }

    // Unlock continent object id at first objective +0x120
    CVOGReaction_UnlockContinentObject(
        character, *(uint32_t*)(*(int*)templateRec[0x4f] + 0x120));

    // Insert mission if still not in +0x540
    if (CNDHash_LookupByKey(*(void**)((char*)character + 0x540), missionId) != nullptr) {
        int off = *(int*)(*(int*)((char*)character + 4) + 4);
        FUN_007a4480(1, "Already had mission %l for COID %I64d\n", missionId,
                     *(uint32_t*)(off + 0x164 + (int)character),
                     *(uint32_t*)(off + 0x168 + (int)character));
        return 1;
    }

    FUN_0053c360(missionId, templateRec, 0);
    char allowBonus = CVOGCharacter_WeaponAllowsKillXpBonus();
    if (allowBonus != 0 && (*(short*)(templateRec + 0x3e) == 0 || templateRec[0x40] != (uint32_t)-1))
        FUN_0053c360(missionId, templateRec, 0);

    if (*(short*)(templateRec + 0x2b) == -1) {
        if (CNDHash_LookupByKey(*(void**)((char*)character + 0x538), missionId) != nullptr)
            FUN_00538b20(missionId, 0);
    }

    void* recMem = operator_new(0x30);
    uint32_t* runtimeRec = nullptr;
    if (recMem != nullptr)
        runtimeRec = (uint32_t*)FUN_004111f0();

    uint32_t mid = missionId;
    int local_1a0 = 0;
    FUN_00538a40(&local_1a0, &mid);
    // copy 0xC dwords from (local_1a0+0x18) into runtimeRec when valid
    if (local_1a0 != *(int*)((char*)character + 0x50c) && (local_1a0 + 0x18) != 0 && runtimeRec) {
        uint32_t* src = (uint32_t*)(local_1a0 + 0x18);
        for (int i = 0; i < 0xc; i++)
            runtimeRec[i] = src[i];
    }

    FUN_0053c660(*templateRec, runtimeRec, 0);
    FUN_0052d8b0(0, *templateRec);

    // Standard mission toast when short@+0x3e == 0
    if (*(short*)(templateRec + 0x3e) == 0) {
        FUN_007a69d0();
        void* name = FUN_007a6de0((const char*)templateRec[0x53], -1);
        void* prefix = FUN_007a6de0("Received Mission", -1);
        char local_100[256];
        sprintf(local_100, "%s: %s", (const char*)name, (const char*)prefix);
        char local_198[128];
        local_198[0] = 0;
        strncpy(local_198, local_100, 0x80);
        FUN_0040c5c0(local_198);
        Client_GetMissionCompleteAudioTable("gen_give_quest", 0, -1, -1, 0, 0, 0x1e, 0);
        Client_PlayNamedInterfaceSound("gen_give_quest", 0, -1, -1, 0, 0, 0x1e, 0);
        return 1;
    }

    return 1;
}

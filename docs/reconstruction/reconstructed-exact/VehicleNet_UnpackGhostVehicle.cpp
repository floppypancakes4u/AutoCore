/*
 * Purpose: TNL GhostVehicle unpack (initial + delta).
 * Stable ID: aa_exe_005f7720  Address: 0x005f7720
 * Evidence: raw/aa_exe_005f7720.md
 *
 * APPLY ORDER (raw 1473-1494): heat → shield (vs current max) → shieldMax
 * OWNER (initial): flag → 64-bit TFID + key + form flag → Ghost_UnpackOwnerForm live call
 */

#include <cstdint>

extern void BitStream_readBits(int nbits, void* out);
extern uint32_t BitStream_readInt();
extern int BitStream_readFlag();
extern float BitStream_readQuantizedFloat();
extern void FUN_005f5ad0(int mode, int creatureOwnerForm);
extern void FUN_005b1360(void* stream, void* vehicle);
extern void FUN_007a4480(int, const char*, ...);
extern void Vehicle_setDrivingInputs(
    int vehicle, float* pos, float* rot, float* vel, float* angVel,
    float throttle, float steering, uint8_t handbrake, char skipActivate, float integrateDt);
extern char DAT_00d02820;
extern int  DAT_00d1798c;

// ---- BitStream flag (inline decompile pattern on stream object) ----
inline bool Ghost_StreamReadFlag(uint8_t* stream, bool* outFlag)
{
    uint32_t pos = *(uint32_t*)(stream + 0x18);
    uint32_t limit = *(uint32_t*)(stream + 0x2c);
    if (limit < pos) {
        *(uint8_t*)(stream + 0x1c) = 1;
        *outFlag = false;
        return false;
    }
    uint8_t b = *(uint8_t*)((pos >> 3) + *(int*)(stream + 0x0c));
    *(uint32_t*)(stream + 0x18) = pos + 1;
    *outFlag = (b & (uint8_t)(1 << (pos & 7))) != 0;
    return true;
}

inline bool Ghost_ReadOptionalU32(uint8_t* stream, bool* flagOut, uint32_t* valueOut)
{
    bool flag = false;
    if (!Ghost_StreamReadFlag(stream, &flag)) {
        *flagOut = false;
        return false;
    }
    *flagOut = flag;
    if (flag && valueOut)
        BitStream_readBits(0x20, valueOut);
    return true;
}

// ---- Combat apply (raw order) ----
inline int Ghost_ClampShield(int shield, int maxShield)
{
    int v = shield;
    if (maxShield <= shield)
        v = maxShield;
    if (v < 1)
        return 0;
    if (shield < maxShield)
        return shield;
    return maxShield;
}

inline void Ghost_ApplyCombatFields(
    int* vehicle,
    bool heatDirty, int heat,
    bool shieldDirty, int shield,
    bool shieldMaxDirty, int shieldMax,
    bool powerDirty, int /*power*/)
{
    if (heatDirty)
        *(int*)((char*)vehicle + 0x150) = heat;

    if (shieldDirty) {
        int curMax = *(int*)((char*)vehicle + 0x148);
        *(int*)((char*)vehicle + 0x144) = Ghost_ClampShield(shield, curMax);
    }

    if (shieldMaxDirty) {
        *(int*)((char*)vehicle + 0x148) = shieldMax;
        if (shieldMax < *(int*)((char*)vehicle + 0x144))
            *(int*)((char*)vehicle + 0x144) = shieldMax;
    }

    if (powerDirty) {
        // vtbl+0x214 then +0xAC (raw)
    }
}

// ---- Owner form (raw unpack_creature_owner_form / vehicle-owner branch) ----
inline void Ghost_UnpackOwnerForm(
    int* ghost, uint8_t* stream, bool vehicleOwnerForm,
    uint32_t ownerLo, uint32_t ownerHi, uint32_t ownerKey, char local_131)
{
    if (!vehicleOwnerForm) {
        FUN_005f5ad0(1, 1); // creature-owner — no wheels path
        int owner = ghost[0x18];
        *(char*)(owner + 0x98) = local_131;
        *(uint32_t*)(owner + 4) = ownerKey;
        *(uint32_t*)(owner + 0x90) = ownerLo;
        *(uint32_t*)(owner + 0x94) = ownerHi;
        int veh = ghost[0x17];
        *(uint8_t*)(owner + 0x8a) = *(uint8_t*)(veh + 0x8a);
        *(uint32_t*)(owner + 0x1c) = *(uint32_t*)(veh + 0x1c);
        *(uint32_t*)(owner + 0x20) = *(uint32_t*)(veh + 0x20);
        *(uint32_t*)(owner + 0x108) = *(uint32_t*)(veh + 0xe0);
        *(uint32_t*)(owner + 0xf8) = *(uint32_t*)(veh + 0x90);
        *(uint32_t*)(owner + 0xfc) = *(uint32_t*)(veh + 0x94);
        // optional flag-gated ints +0xd8 / +0x128 / +300 follow in raw
        (void)stream;
    } else {
        FUN_005f5ad0(1, 0); // vehicle-owner + wheels path
        int owner = ghost[0x18];
        *(char*)(owner + 0x98) = local_131;
        *(uint32_t*)(owner + 0x90) = ownerLo;
        *(uint32_t*)(owner + 0x94) = ownerHi;
        *(uint32_t*)(owner + 4) = ownerKey;
        int veh = ghost[0x17];
        *(uint8_t*)(owner + 0x8a) = *(uint8_t*)(veh + 0x8a);
        *(uint32_t*)(owner + 0x1c) = *(uint32_t*)(veh + 0x1c);
        *(uint32_t*)(owner + 0x20) = *(uint32_t*)(veh + 0x20);
        *(uint32_t*)(owner + 0xd8) = *(uint32_t*)(veh + 0x90);
        *(uint32_t*)(owner + 0xdc) = *(uint32_t*)(veh + 0x94);
        uint8_t b = 0;
        BitStream_readBits(8, &b);
        *(uint8_t*)(owner + 0x128) = b;
        (void)stream;
    }
}

// Initial-path owner block: MUST call Ghost_UnpackOwnerForm (live, not comment).
inline void Ghost_ReadOwnerBlockAndUnpack(int* ghost, uint8_t* stream, uint32_t* vehicleInit)
{
    // Flag: owner TFID present (raw before LAB_005f7cf3)
    bool hasOwner = false;
    Ghost_StreamReadFlag(stream, &hasOwner);

    if (!hasOwner) {
        vehicleInit[0x36] = 0xffffffffu;
        vehicleInit[0x37] = 0xffffffffu;
        return;
    }

    // BitStream_readBits(0x40, &local_e0) → lo/hi
    struct { uint32_t lo; uint32_t hi; } ownerTfid{};
    BitStream_readBits(0x40, &ownerTfid);
    vehicleInit[0x36] = ownerTfid.lo;
    vehicleInit[0x37] = ownerTfid.hi;

    BitStream_readFlag(); // intermediate flag in raw between TFID and key
    uint32_t ownerKey = BitStream_readInt();

    // Form flag: 0 → creature, 1 → vehicle
    bool vehicleOwnerForm = false;
    Ghost_StreamReadFlag(stream, &vehicleOwnerForm);

    char local_131 = 0; // set earlier in full function; default 0 here
    Ghost_UnpackOwnerForm(
        ghost, stream, vehicleOwnerForm,
        ownerTfid.lo, ownerTfid.hi, ownerKey, local_131);
}

// ---- Drive dirty ----
struct GhostDrivePayload {
    float f[13];
    uint8_t b0;
    uint8_t b1;
};

inline bool Ghost_ReadDriveDirty(uint8_t* stream, bool* driveDirty, GhostDrivePayload* out)
{
    bool flag = false;
    if (!Ghost_StreamReadFlag(stream, &flag)) {
        *driveDirty = false;
        return false;
    }
    *driveDirty = flag;
    if (!flag)
        return true;
    for (int i = 0; i < 13; i++)
        BitStream_readBits(0x20, &out->f[i]);
    BitStream_readBits(8, &out->b0);
    BitStream_readBits(8, &out->b1);
    return true;
}

inline void Ghost_ApplyDriveInputs(int* vehicle, const GhostDrivePayload* drive)
{
    *(const GhostDrivePayload**)((char*)vehicle + 0x15c) =
        const_cast<GhostDrivePayload*>(drive);
    float* p = const_cast<float*>(drive->f);
    Vehicle_setDrivingInputs(
        (int)vehicle, p + 0, p + 3, p + 6, p + 9,
        p[12], 0.0f, drive->b0, 0, 0.0f);
}

void VehicleNet_UnpackGhostVehicle(int* ghost, int netCtx, uint8_t* bitStream)
{
    (void)netCtx;
    if (DAT_00d02820)
        FUN_007a4480(-1, "unpacking update from net ...");

    // ----- INITIAL (DAT_00d1798c != 0) -----
    if (DAT_00d1798c != 0) {
        FUN_005f5ad0(0, 0);
        uint32_t* vehicleInit = (uint32_t*)ghost[0x17];
        FUN_005b1360(bitStream, vehicleInit);
        ghost[0x10] = (int)vehicleInit[0x24];
        ghost[0x11] = (int)vehicleInit[0x25];
        ghost[0x12] = (int)vehicleInit[0x26];
        ghost[0x13] = (int)vehicleInit[0x27];

        float tmp;
        BitStream_readBits(0x20, &tmp);
        vehicleInit[0x41] = *(uint32_t*)&tmp;
        BitStream_readBits(0x20, &tmp);
        vehicleInit[0x42] = *(uint32_t*)&tmp;
        BitStream_readFlag();
        uint8_t b8 = 0;
        BitStream_readBits(8, &b8);
        *((uint8_t*)vehicleInit + 0x153) = b8;

        // flag-gated floats 0x4c..0x52 and optional ints (see raw)

        // LIVE owner path — not a comment
        Ghost_ReadOwnerBlockAndUnpack(ghost, bitStream, vehicleInit);
    }

    // Bound vehicle for delta
    int vehicleObj = 0;
    if (ghost[0x14] != 0) {
        vehicleObj = ((int (*)(int))(*(int*)(ghost[0x14])))(ghost[0x14]);
    } else if ((char)ghost[0x15] == 0) {
        ((void (*)(int*))(*(void**)ghost)[0xc / 4])(ghost);
        *(char*)(ghost + 0x15) = 1;
    }

    // ----- DIRTY FLAGS -----
    // STREAM READ: heat → shieldMax → shield; APPLY: heat → shield → shieldMax
    bool heatDirty = false, shieldDirty = false, shieldMaxDirty = false, powerDirty = false;
    bool driveDirty = false;
    uint32_t heat = 0, shield = 0, shieldMax = 0, power = 0;
    GhostDrivePayload drive{};

    Ghost_ReadOptionalU32(bitStream, &heatDirty, &heat);
    Ghost_ReadOptionalU32(bitStream, &shieldMaxDirty, &shieldMax);
    Ghost_ReadOptionalU32(bitStream, &shieldDirty, &shield);
    Ghost_ReadOptionalU32(bitStream, &powerDirty, &power);
    Ghost_ReadDriveDirty(bitStream, &driveDirty, &drive);

    if (vehicleObj != 0) {
        int* veh = (int*)vehicleObj;
        if (*(char*)((char*)veh + 0x103) == 0) {
            Ghost_ApplyCombatFields(
                veh, heatDirty, (int)heat,
                shieldDirty, (int)shield,
                shieldMaxDirty, (int)shieldMax,
                powerDirty, (int)power);
            if (driveDirty)
                Ghost_ApplyDriveInputs(veh, &drive);
        }
    }
}

enum VehicleGhostMaskBits : uint32_t {
    ShieldMaxMask = 0x02000000,
    ShieldMask = 0x04000000,
    PowerMask = 0x08000000,
    HeatMask = 0x20000000,
};

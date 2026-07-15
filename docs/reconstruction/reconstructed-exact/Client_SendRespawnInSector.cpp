/*
 * Purpose: Build and send C2S RespawnInSector (INC airlift request).
 * Original address / stable ID: 0x00935300 / aa_exe_00935300
 * Original symbol: Client_SendRespawnInSector
 * Confidence: high
 */

#include <cstdint>

struct RespawnInSectorPacket {
    uint32_t opcode; // 0x2073
    float pos_x, pos_y, pos_z;
    float quat_x, quat_y, quat_z, quat_w;
    int32_t coid_lo;
    int32_t coid_hi;
};

static_assert(sizeof(RespawnInSectorPacket) == 0x28, "RespawnInSector must be 0x28");

extern void  Client_SendSectorPacket(void* game, uint32_t size, void* payload);
extern int*  FUN_00402ae0(void* out);
extern float* FUN_00404c90(); // current position
extern float* FUN_00404a20(); // current quaternion
extern void  FUN_007fc840();

void Client_SendRespawnInSector(void* game)
{
    int entity = *(int*)((char*)game + 0xe98);
    if (entity == 0)
        return;
    if (*(int*)(entity + 0x250) == 0)
        return;

    // Selection gate: *FUN_00402ae0(tmp) must equal game+0xd28
    alignas(4) uint8_t tmp[4];
    int* resolved = FUN_00402ae0(tmp);
    if (*resolved != *(int*)((char*)game + 0xd28))
        return;

    RespawnInSectorPacket pkt{};
    pkt.opcode = 0x2073;

    float* pos = FUN_00404c90();
    pkt.pos_x = pos[0];
    pkt.pos_y = pos[1];
    pkt.pos_z = pos[2];

    float* quat = FUN_00404a20();
    pkt.quat_x = quat[0];
    pkt.quat_y = quat[1];
    pkt.quat_z = quat[2];
    pkt.quat_w = quat[3];

    int off = *(int*)(*(int*)(entity + 4) + 4);
    pkt.coid_lo = *(int*)(off + 0x164 + entity);
    pkt.coid_hi = *(int*)(off + 0x168 + entity);

    Client_SendSectorPacket(game, 0x28, &pkt);
    FUN_007fc840();
}

// Pure packet builder (same layout as above) — used by experiments
inline void RespawnInSector_Pack(
    RespawnInSectorPacket* out,
    float px, float py, float pz,
    float qx, float qy, float qz, float qw,
    int64_t coid)
{
    out->opcode = 0x2073;
    out->pos_x = px; out->pos_y = py; out->pos_z = pz;
    out->quat_x = qx; out->quat_y = qy; out->quat_z = qz; out->quat_w = qw;
    out->coid_lo = (int32_t)(coid & 0xffffffff);
    out->coid_hi = (int32_t)((coid >> 32) & 0xffffffff);
}

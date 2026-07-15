/*
 * Purpose: Apply network pose to vehicle — soft buffer path vs hard entity write.
 * Stable ID: aa_exe_0053eec0
 * Address: 0x0053eec0
 * Original: FUN_0053eec0
 * Confidence: high for branch structure; field names partially inferred
 *
 * Soft path when graphics object exists but physics not fully ready:
 *   (graphics+0x40==0) || (graphics+8==0)
 * Soft buffer 0x40: pos@+0, rot@+0x10, vel@+0x20, angVel@+0x30
 * Teleport+impulse if distance to current > 15.0f (DAT_009d000c)
 * If integrateDt != 0: FUN_0053eb90(0, dt)
 * Hard path: write entity offsets +0x84 pos, +0x94 rot when |pos| large
 */

#include <cmath>

extern int   AllocSoftPoseBuffer_Inferred(); // FUN_0053e020
extern void  SoftIntegrate_FUN_0053eb90(int, float);
extern void  HardTeleport_Inferred();
extern void  CVOGPhysics_ApplyImpulseVector(float* vel);
extern float DAT_009d000c; // 15.0f
extern int   g_dwClientTickMs;

void NetworkPoseApply_FUN_0053eec0(
    int* vehicle,
    float* pos, float* rot, float* vel, float* angVel,
    float integrateDt)
{
    int graphics = vehicle[2]; // +8
    if (graphics != 0) {
        float scale = *(float*)(*(int*)(graphics + 0x3c) + 0x2c);
        if (scale != 0.0f && (1.0f / scale) != 0.0f) {
            bool soft = (*(char*)(graphics + 0x40) == 0) || (*(int*)(graphics + 8) == 0);
            if (soft) {
                vehicle[5] = g_dwClientTickMs;
                *(char*)(vehicle + 4) = 1;
                if (vehicle[10] == 0)
                    vehicle[10] = AllocSoftPoseBuffer_Inferred();
                float* buf = (float*)vehicle[10];
                // position (16 bytes incl pad)
                buf[0] = pos[0]; buf[1] = pos[1]; buf[2] = pos[2]; buf[3] = pos[3];
                // velocity if |v| large enough else zero
                // rotation if valid
                // angVel always
                // if distance(pos, current) > 15: hard teleport + impulse
                // if integrateDt != 0: SoftIntegrate_FUN_0053eb90(0, integrateDt)
                return;
            }
        }
    }
    // HARD PATH: if |pos| > eps write entity +0x84 / +0x94
}

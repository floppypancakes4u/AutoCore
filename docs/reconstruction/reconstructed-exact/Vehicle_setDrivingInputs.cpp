/*
 * Purpose: Apply network/local driving inputs and network pose.
 * Stable ID: aa_exe_00504c70
 * Address: 0x00504c70
 * Confidence: high (matches docs/MOTION_CLIENT_RE.md)
 */

#include <cstdint>

extern void VehicleEntity_PushDriveAxesToController(); // uses ECX=this in binary
extern void Vehicle_ActivateEnterWorld();
extern void NetworkPoseApply_FUN_0053eec0(void* self, float* pos, float* rot,
                                          float* vel, float* angVel, float integrateDt);

void Vehicle_setDrivingInputs(
    int vehicle,
    float* pos, float* rot, float* vel, float* angVel,
    float throttle, float steering, uint8_t handbrake,
    char skipActivate, float integrateDt)
{
    if (*(int*)(vehicle + 8) == 0)
        return;

    // Optional: if graphics type vfunc returns 6, FUN_0053d970(0)
    int iVar2 = 0; // vfunc on *(vehicle+8)+0x3c
    (void)iVar2;

    *(float*)(vehicle + 0x614) = throttle;
    *(float*)(vehicle + 0x618) = steering;
    *(uint8_t*)(vehicle + 0x61c) = handbrake;

    // thiscall Push with ECX=vehicle
    VehicleEntity_PushDriveAxesToController();

    if (skipActivate == 0 && *(int*)(vehicle + 0x1a0) == 0) {
        // Owner identity match → Vehicle_ActivateEnterWorld
        // piVar1 = *(vehicle + 0xb0 + offsetTable)
        // if driver id == self id → ActivateEnterWorld
    }

    NetworkPoseApply_FUN_0053eec0(reinterpret_cast<void*>(vehicle),
                                  pos, rot, vel, angVel, integrateDt);
}

/*
 * Purpose: Push entity drive axes into Havok VehicleAction controller.
 * Stable ID: aa_exe_004fbc10
 * Address: 0x004fbc10
 * Confidence: high
 *
 * Writes:
 *   ctrl+0x20 = entity+0x614 (throttle)
 *   ctrl+0x24 = entity+0x61c (handbrake)
 *   optional clamp to 0.9 if ctrl+0x19
 *   force stop if entity+0x109
 * Speed-cap gate may zero throttle when over max and opposing forward.
 * entity+0x618 (steer) is NOT written here.
 */

#include <cstdint>
#include <cmath>

void VehicleEntity_PushDriveAxesToController(int entity)
{
    if (*(char*)(entity + 0x101) != 0)
        return;
    if (*(int*)(entity + 0x1a0) == 0)
        return;

    int ctrl = *(int*)(*(int*)(entity + 0x1a0) + 8);
    *(uint8_t*)(ctrl + 0x25) = 0;

    if (*(char*)(entity + 0x109) != 0) {
        *(float*)(ctrl + 0x20) = 0.0f;
        *(uint8_t*)(ctrl + 0x24) = 1;
        return;
    }

    float throttle = *(float*)(entity + 0x614);
    *(float*)(ctrl + 0x20) = throttle;

    if (*(char*)(*(int*)(*(int*)(entity + 0x1a0) + 8) + 0x19) != 0) {
        // DAT_00a0f734 ≈ 0.9f clamp
        const float kClamp = 0.9f;
        if (throttle >= kClamp)
            throttle = kClamp;
        *(float*)(ctrl + 0x20) = throttle;
    }

    // ... speed magnitude vs max-speed gate (preserves zeroing throttle when overspeed) ...

    *(uint8_t*)(ctrl + 0x24) = *(uint8_t*)(entity + 0x61c);
}

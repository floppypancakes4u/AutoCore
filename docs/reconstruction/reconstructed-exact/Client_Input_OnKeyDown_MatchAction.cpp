/*
 * Purpose: WM key-down → action-slot match; set held+edge; ESC cancel graph.
 * Stable ID: aa_exe_00911030  Address: 0x00911030
 */

#include <cstdint>
#ifdef _WIN32
#include <windows.h>
#else
// stubs for non-Windows analysis builds
inline short GetAsyncKeyState(int) { return 0; }
#endif

extern char  ClientUiVisible_vfunc_0x3d8(void* client); // (*vt+0x3d8)
extern uint8_t FUN_00790020(int key, int lParam);       // map key
extern void* FUN_007f6db0();                              // current action override?
extern void  FUN_0093a5c0(int);
extern int   FUN_0090d390();
extern void  FUN_0090dab0();
extern void  FUN_008012f0();
extern int   FUN_008a70c0();
extern void  FUN_007fc360();
extern void  FUN_0093bac0(void*, int);
extern void  FUN_007fb990();
extern void  FUN_0093e120(int);
extern void  FUN_00402ae0(void*);
extern int   __RTDynamicCast(void*, int, void*, void*, int);
extern void  FUN_004040a0();
extern void  FUN_00402850(void*, void*, int);
extern void  FUN_007fca10();
extern void  FUN_007fef20(int, int, int);
extern int*  DAT_00d1b780;
extern int   DAT_00d1b778;
extern short DAT_00d1bc18; // action table region
extern short DAT_00d1bbee; // entry base
// ... other DAT_* for ESC UI graph

// Action entry stride: 0x1a shorts = 0x34 bytes (from decompiler loop)
static constexpr int kActionStrideShorts = 0x1a;
static constexpr short kShiftDik = 0x2a;
static constexpr int kVkEscape = 0x1b;

uint32_t Client_Input_OnKeyDown_MatchAction(int* client, uint32_t keyCode, int lParam)
{
    // If UI visible and ESC: cancel graph (menus, special events, path UI). Many early return 1.
    char uiVis = 0;
    if (client)
        uiVis = ClientUiVisible_vfunc_0x3d8(client);

    if (uiVis != 0 && keyCode == (uint32_t)kVkEscape) {
        // Flag at client+0x50d prevents re-entry
        if (*(char*)((char*)client + 0x50d) == 0) {
            *(char*)((char*)client + 0x50d) = 1;
            if (FUN_0090d390() != 0) {
                FUN_0090dab0();
                return 1;
            }
            // ... additional ESC targets (chat, menus, special event list, etc.)
            // Full ordered list in raw capture; each returns 1 when handled.
            // Fall through to normal match only via LAB_009113c1 when unhandled.
        } else {
            return 1;
        }
        // If none handled, falls into LAB_009113c1 in binary
    }

    // Normal path: map key, compute shift modifier
    uint8_t mapped = FUN_00790020((int)keyCode, lParam);
    short shiftDik = 0;
    if (GetAsyncKeyState(0x10) != 0 || GetAsyncKeyState(0xa0) != 0 || GetAsyncKeyState(0xa1) != 0)
        shiftDik = kShiftDik;

    // Build scancode-ish key from lParam bits (decompiler: sVar14)
    short matchKey = (short)(((lParam >> 0x18) & 1) << 7) + (short)((lParam >> 16) & 0xff);

    short* overrideEntry = (short*)FUN_007f6db0();
    short* entry = overrideEntry;

    if (entry == nullptr) {
        // Only when DAT_00d1b780 matches session DAT_00d1b778
        if (DAT_00d1b780 != nullptr && *DAT_00d1b780 == DAT_00d1b778) {
            int index = 0;
            short* ps = &DAT_00d1bc18;
            for (;;) {
                // Primary bind at ps[-0x15], secondary at ps[-0x14]
                // Match matchKey; require shift field *ps / ps[1] consistent with shiftDik
                // Skip if held already (ps[3] char)
                bool primaryHit = (ps[-0x15] == matchKey);
                bool secondaryHit = (ps[-0x14] == matchKey);
                if (primaryHit || secondaryHit) {
                    bool shiftOk;
                    if (primaryHit) {
                        if (shiftDik == 0)
                            shiftOk = (*ps != 0);
                        else
                            shiftOk = (*ps == 0);
                        // inverted decompiler structure: see raw — if shift free or required
                        // Simplified structural match: when conditions pass and !held → entry
                        if ((char)ps[3] == 0) {
                            entry = &DAT_00d1bbee + index * kActionStrideShorts;
                            break;
                        }
                    }
                    (void)shiftOk;
                    (void)secondaryHit;
                }
                ps += kActionStrideShorts;
                index++;
                if ((int)ps > 0xd1d477)
                    return mapped; // end of table
            }
        }
    }

    if (entry != nullptr) {
        // held at entry+2 (as char on short*), edge at entry+5
        if (*(char*)(entry + 2) == 0) {
            *(char*)(entry + 2) = 1;          // held
            *(char*)((char*)entry + 5) = 1; // edge
            FUN_0093a5c0(1);
        }
    }

    return mapped;
}

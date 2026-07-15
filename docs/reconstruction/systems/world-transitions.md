# System: World / region transitions

**ID:** SYS-WORLD  
**Priority:** 10  
**Status:** complete (static) with residuals  
**Updated:** 2026-07-15

## Entry points

| Symbol | Address | Role |
|--------|---------|------|
| Client_RecvUnlockRegion | 0x00809550 | S2C 0x205B unlock/explored bits |

## Flow

UnlockFlag==0 → RelockContinentObject; else unlock continent and optionally set explored bits 1..32.

## Residuals (not eligible high-pri)

| ID | Residual | Class |
|----|----------|-------|
| WQ-RT-01 / UF-002 | Runtime dual-run of unlock path | blocked |

High-pri UnlockRegion handler is complete (static); broader transfer/login lifecycle is SYS-LIFECYCLE deferred (WQ-017).

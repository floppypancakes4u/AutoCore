# Review: Client_RecvStoreTransactionResponse (FUN_00810670)

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw `raw/aa_exe_00810670.md`
- Dispatch case 0x2028
- `StoreTransactionResponsePacket.cs`
- vendor-store-useobject topic extraction

## Falsification attempts against raw

1. **Is +0x28 success and +0x29 isBuy?**  
   - Fail path: `if (*(char*)(in_EAX+0x28)==0)` then toast by `+0x29` (0 → sell fail string, else buy fail string).  
   - Success path: `if (+0x29==0)` sell else buy.  
   - **Verdict:** confirmed against raw strings and branch structure.

2. **Credits absolute vs delta?**  
   - Raw writes packet +0x20/+0x24 straight to `character+0x720/+0x724`.  
   - **Verdict:** absolute balance; server must send post-transaction total.

3. **Must destroy sold item before 0x2028?**  
   - Sell path resolves item at +0x08 then `FUN_00571d80` destroy; if destroy fails → `FUN_007fc150` hand clear.  
   - If object already gone, resolve returns 0 and early-return (no hand clear).  
   - **Verdict:** prior advice "do not DestroyObject before 0x2028" matches raw; **confirmed**.

4. **Buy branch TFID compare at +0x10**  
   - Compares packet +0x10/+0x14 to local character TFID fields.  
   - **Risk:** interpretation of "self grant vs store slot" is reconstructed intent; field names remain tentative.  
   - **Verdict:** branch exists; semantic labels probable.

## Verdict

Accept fail path, credits write, sell destroy sequencing. Buy sub-branches need live packets for full annotation.

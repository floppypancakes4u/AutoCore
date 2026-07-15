# Review: Client_UI_InventoryDropToGrid (store branches)

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw `raw/aa_exe_00860a50.md`
- Opcode immediates `'\'' ' '` = 0x2027, `'6' ' '` = 0x2036

## Falsification attempts against raw

1. **Is store sell really inv type 4?**  
   - Raw: `*(int*)(in_EAX[0x15b]+4) == 4` then build 0x2027 with `uStack_c8=0` (IsBuy=0).  
   - **Verdict:** confirmed for this client build.

2. **Does sell include store COID at +0x28?**  
   - Sell path sets item TFID at uStack_e8 (+0x18) from cursor item; does not clearly fill +0x28 store block (zeros from stack).  
   - Buy path FUN_0088e180 fills store TFID.  
   - **Verdict:** sell may omit store COID; server should not require it for sell (matches packet comments).

3. **Second 0x2027 branch with IsBuy=1**  
   - When `char+0xcd0 != 0` and UI mode `DAT_00d1b1f8[0x125]==4`.  
   - **Verdict:** alternate buy-via-drop exists; primary buy remains FUN_0088e180.

4. **In-flight flag DAT_00d1a8f6**  
   - If set, returns 1 without send (dedupe).  
   - Server should expect single request per UI action.

## Verdict

Accept store sell/buy drop branches and opcode discrimination.

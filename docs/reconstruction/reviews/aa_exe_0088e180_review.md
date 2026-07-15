# Review: Client_SendStoreTransactionBuy (FUN_0088e180)

**Date:** 2026-07-15  
**Roles:** reconstruction, context, skeptical (static)

## Inspected

- Raw `raw/aa_exe_0088e180.md`
- Stack layout vs `StoreTransactionRequestPacket`
- Sell path sibling `aa_exe_00860a50`

## Falsification attempts against raw

1. **Opcode and size?**  
   - `auStack_40[0]=0x2027`; `Client_SendSectorPacket(..., 0x40, auStack_40)`.  
   - **Verdict:** confirmed.

2. **IsBuy always 1 here?**  
   - `uStack_8 = 1` only path.  
   - **Verdict:** this function is buy-only; sell is DropToGrid.

3. **Item TFID from item[0x58] == object+0x160?**  
   - `0x58 * 4 = 0x160` — matches TFID convention used elsewhere.  
   - Store TFID from store object same pattern.  
   - **Verdict:** confirmed.

4. **Afford check exact formula**  
   - Uses price vt+0x168, fee +0xce8/+0xcec, credits − reserved.  
   - 64-bit compare is easy to mis-simplify.  
   - **Verdict:** control flow gate exists; reconstructed arithmetic marked probable for edge carry cases.

## Verdict

Accept packet layout and IsBuy=1 send path.

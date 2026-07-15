# NAMING_REGISTRY

Canonical renames for reconstructed entities. Prefer descriptive conservative names; never invent unsupported intent.

| Canonical name | Kind | Stable ID / address | Original | System | Evidence | Confidence | Aliases |
|----------------|------|---------------------|----------|--------|----------|------------|---------|
| Client_RecvCharacterLevel | function | aa_exe_00810f00 / 0x00810f00 | Client_RecvCharacterLevel | progression | Ghidra symbol + decompile; dispatch 0x2017 | high | — |
| CVOGCharacter_ApplyCharacterLevelPacket | function | aa_exe_00531e90 / 0x00531e90 | CVOGCharacter_ApplyCharacterLevelPacket | progression | vfunc+0xcc from RecvCharacterLevel | high | — |
| Client_RefreshLocalCharacterLevelUi | function | aa_exe_0092f4d0 / 0x0092f4d0 | Client_RefreshLocalCharacterLevelUi | progression | called on local TFID match | high | — |
| Client_AwardKillExperience | function | aa_exe_0080ae70 / 0x0080ae70 | Client_AwardKillExperience | progression | Ghidra symbol; dispatch 0x205f; plate notes misnomer | high | Client_RecvGiveXP (behavioral; not renamed in Ghidra) |
| CVOGReaction_AddExperience | function | aa_exe_00533c30 / 0x00533c30 | CVOGReaction_AddExperience | progression | XP apply kernel; docs/XP.md | high | — |
| CVOGCharacter_LevelUp | function | aa_exe_00532d30 / 0x00532d30 | CVOGCharacter_LevelUp | progression | called from AddExperience loop | high | — |
| CVOGCharacter_LevelDown | function | aa_exe_005330e0 / 0x005330e0 | CVOGCharacter_LevelDown | progression | negative XP path | high | — |
| Experience_GetCumulativeThreshold | function | aa_exe_0052c860 / 0x0052c860 | Experience_GetCumulativeThreshold | progression | tExperienceLevel lookup | high | — |
| Client_RecvGiveCredits | function | aa_exe_0080cac0 / 0x0080cac0 | Client_RecvGiveCredits | progression | dispatch 0x205e; additive currency | high | — |
| Client_EnqueueCombatFloater_INFERRED | function | aa_exe_00402620 / 0x00402620 | Client_EnqueueCombatFloater_INFERRED | combat-ui | floater type 3=XP, 4=credits | medium | — |
| GiveXpPacketBody | type | wire 0x205F | GiveXpPacketBody | progression | Amount@+4, LevelHint@+8 | high | AutoCore GiveXPPacket |
| Packet_CharacterLevel | type | wire 0x2017 | Packet_CharacterLevel | progression | absolute Level/Currency/XP | high | AutoCore CharacterLevelPacket |
| Packet_GiveCredits | type | wire 0x205E | Packet_GiveCredits | progression | int64 amount@+8 | high | AutoCore GiveCreditsPacket |
| XpIsKillPath | enum | — | XpIsKillPath | progression | PacketOrNonKill=0, KillPath≠0 | high | — |
| CombatFloaterType_XP | constant | type=3 | — | progression | AwardKillExperience stack | high | — |
| CombatFloaterType_Credits | constant | type=4 | — | progression | RecvGiveCredits stack | high | — |
| Client_RecvNpcMissionDialog | function | aa_exe_00815070 / 0x00815070 | Client_RecvNpcMissionDialog | dialog-vendors | Ghidra decompile + PacketDispatch 0x206D | high | — |
| Client_RecvGroupReactionCall | function | aa_exe_008092a0 / 0x008092a0 | Client_RecvGroupReactionCall | dialog-vendors | Ghidra decompile + PacketDispatch 0x206C | high | EMSG_Sector_MissionDialog (string table misname risk) |
| Client_SendMissionDialogResponse | function | aa_exe_008ab8f0 / 0x008ab8f0 | FUN_008ab8f0 | dialog-vendors | decompile send dialog+0x650 size 0x20 opcode 0x206E | high | — |
| Client_MissionDialogHandleButton | function | aa_exe_008ae7c0 / 0x008ae7c0 | Client_MissionDialogHandleButton | dialog-vendors | decompile state machine | high | — |
| Client_NpcDialog_PrepareResponseOpcode | function | aa_exe_008abd70 / 0x008abd70 | Client_NpcDialog_PrepareResponseOpcode | dialog-vendors | sets dialog+0x650=0x206E | high | — |
| Client_RecvStoreTransactionResponse | function | aa_exe_00810670 / 0x00810670 | FUN_00810670 | dialog-vendors | PacketDispatch 0x2028 + decompile | high | — |
| Client_SendStoreTransactionBuy | function | aa_exe_0088e180 / 0x0088e180 | FUN_0088e180 | dialog-vendors | decompile C2S 0x2027 IsBuy=1 | high | — |
| Client_UI_InventoryDropToGrid | function | aa_exe_00860a50 / 0x00860a50 | Client_UI_InventoryDropToGrid | dialog-vendors / inventory | decompile; store sell 0x2027 | high | store sell/buy drop |

## Opcode relation notes (UF-005)

| Opcode | Canonical handler | Semantics | vs others |
|--------|-------------------|-----------|-----------|
| **0x2017** | Client_RecvCharacterLevel | **Absolute** level, total XP, currency, point pools, mana/(gated) HP | Snapshot/UI restore; does **not** call AddExperience |
| **0x205F** | Client_AwardKillExperience | **Additive** XP via AddExperience(PacketOrNonKill) + floater type 3 | Mid-session grant; LevelHint writes spree/hint byte only |
| **0x205E** | Client_RecvGiveCredits | **Additive** currency via AddCredits + floater type 4 | Economy sibling; not XP |

Server-side name `GiveXp` is AutoCore service API mirroring `CVOGReaction_AddExperience` + notify packets — **no** client symbol `GiveXp`.

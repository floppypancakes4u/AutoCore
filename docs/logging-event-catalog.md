# Structured log event catalog

Every first-class `GameLog` event name emitted from **production** code under `src/`
(excluding `*Tests` projects and the dual-write `Legacy` mirror).

Drift guard: `AutoCore.Utils.Tests.Logging.LogEventCatalogSyncTests` scans
`GameLog.(Info|Debug|Trace|Warn|Error|Fatal|Audit)("Name"` and `GameLog.Operation("Name"`
(which expands to `NameStarted` / `NameCompleted` / `NameFailed`) and asserts set equality
with the **EventName** column below (plus allowed dynamic `*RateLimited` summaries).

## Envelope (every NDJSON record)

| Field | Meaning |
|---|---|
| Timestamp | UTC ISO-8601 |
| Level | Trace / Debug / Info / Warning / Error / Fatal |
| EventName | Catalog key |
| Message | Optional human text (legacy dual-write) |
| Audit | `true` for player-action audit trail (bypasses min level + rate limit) |

## Canonical property names

SessionId, ConnectionId, CorrelationId, TransactionId, AccountId, CharacterId, CharacterName,
ItemCoid, ItemCbid, MapId, Before, Delta, After, Reason, Result, DurationMs, ErrorCode,
BuildVersion, CommitHash, ServerInstanceId, Operation, Suppressed, Verb, Container, Quantity.

## Level semantics

| Level | Use |
|---|---|
| Trace/Debug | Development detail; filtered unless `PlayerDiagnostics` enrolled or min level lowered |
| Info | Normal lifecycle / health |
| Warning | Recoverable abnormal (bad client input, slow DB, rate limit) |
| Error | Operation failed; process healthy |
| Fatal | Process/subsystem cannot continue |

## Error-code prefixes

AUTH- · NET- · INV- · ECO- · MIS- · SEC- · DB- · SRV-

## Catalog

*handshake props* = the shared set emitted by `TNLConnection.TransferHandshakeLogProps()`:
`SessionId, CharacterId, ToMapId, TransferPhase, TransferGeneration` plus `FromMapId` when known.

| EventName | Level | Subsystem | Trigger | Key properties | Audit? | ErrorCodes | Notes |
|---|---|---|---|---|---|---|---|
| ServerStarting | Info | Host | BaseServer console init | BuildVersion, CommitHash, ServerInstanceId, ServerName | | | |
| ServerReady | Info | Host | Launcher ready | ServerName | | | |
| ServerShutdownRequested | Info | Host | Shutdown begin | ServerName | | | |
| ServerStopped | Info | Host | Shutdown complete | ServerName | | | |
| HealthSummary | Info | Ops | Sector ~60s | Sessions, TickAvgMs, TickMaxMs, MissionPersistPending, MissionPersistDeadLettered, UptimeSeconds, WorkingSetBytes, GcGen* | | | |
| TickOverrun | Warning | Ops | Sector tick > budget | DurationMs, BudgetMs | | SRV-001 | |
| DbOperationSlow | Warning | DB | Persist > SlowThresholdMs | Operation, DurationMs | | DB-002 | Default 250ms |
| DbOperationFailed | Error | DB | Persist throws | Operation, DurationMs, ExceptionType | | DB-003 | Rethrows after log |
| ConnectionAccepted | Info | TNL | New connection | ConnectionId, SessionId | | | Lifecycle rate-limit exempt |
| ConnectionClosed | Info | TNL | Connection drop | ConnectionId, SessionId, Reason | | | |
| SectorHandshakeStarted | Info | TNL | Transfer/handshake | SessionId | | | |
| SecurityKeyMismatch | Warning | TNL | Transfer key ≠ expected | SessionId | | SEC-002 | SS-29 log-only |
| CharacterSpawned | Info | TNL | Sector spawn | CharacterId, CharacterName | | | |
| CharacterSelected | Info | TNL | Global select | CharacterId, CharacterName, AccountId | | | |
| SessionEnded | Info | TNL | Disconnect cleanup | Reason, Detail, SessionDurationMs | | | |
| SectorGhostingDeferredForWorldEntry | Info | TNL | Ghosting held back until world entry finishes | SessionId, ConnectionId | | | Foreign ghosts must not precede the local Creates |
| SectorGhostingActivatedAfterWorldEntry | Info | TNL | Deferred ghosting switched on | SessionId, CharacterId | | | |
| SectorGhostingDuplicateActivationPrevented | Info | TNL | Second activation suppressed | SessionId, CharacterId | | | |
| SectorGhostingDisconnectBeforeActivation | Info | TNL | Session ended while ghosting still deferred | SessionId, CharacterId | | | |
| GhostingNeverStartedAfterWorldEntry | Warning | TNL | Watchdog: world entry done, ghosting never began | SessionId, CharacterId, MapId, MapResetCount, StalledForMs, Scoping, Ghosting, GhostingSequence | | NET-006 | `rpcStartGhosting` sits behind the create burst on the guaranteed-ordered queue; a backlog that never drains means it was never queued |
| CharacterWorldStateSaveStarted | Info | Persistence | EndCharacterSession | CharacterId | | | Operation scope |
| CharacterWorldStateSaveCompleted | Info | Persistence | Save ok | CharacterId, DurationMs | | | |
| CharacterWorldStateSaveFailed | Info | Persistence | Save fail / dispose | CharacterId, DurationMs, Reason? | | | |
| MapTransferStarted | Info | Map | Transfer begin | CharacterId, FromMap?, ToMap? | | | Operation scope |
| MapTransferCompleted | Info | Map | Transfer ok | DurationMs | | | |
| MapTransferFailed | Info | Map | Transfer fail | DurationMs, Reason? | | | |
| MapTransferHandshakeWaiting | Info | Map | Stage1 sent; awaiting client Stage2 | *handshake props*, SpawnX, SpawnY, SpawnZ | | | Creates withheld until Stage3 ack |
| MapTransferStage2Received | Info | Map | Client Stage2 arrived | *handshake props* | | | |
| MapTransferStage3Sent | Info | Map | Stage3 sent in reply to Stage2 | *handshake props* | | | |
| MapTransferStage3AckReceived | Info | Map | Client acked Stage3 | *handshake props* | | | Clears phase, then releases Creates |
| MapTransferCreatesReleased | Info | Map | Withheld Creates flushed | *handshake props* | | | |
| MapTransferGhostingActivated | Info | Map | Ghosting activation attempted after Creates | *handshake props* | | | Emitted unconditionally — does **not** prove the client was asked to ghost; `GhostingNeverStartedAfterWorldEntry` observes the outcome |
| MapTransferHandshakeAborted | Info | Map | Handshake abandoned or superseded | *handshake props*, Reason | | | Reason `superseded` when a new transfer replaces one in flight |
| MapTransferHandshakeStalled | Warning | Map | Phase unchanged past the stall budget | *handshake props*, StalledForMs, WaitingFor | | NET-004 | Client parked on a loading screen |
| MapTransferStaleStagePacket | Warning | Map | Stage packet dropped (wrong phase/character/order) | *handshake props*, Reason, PacketCharacterId | | NET-005 | |
| LoginTicketIssued | Info | Login | Ticket minted | AccountId, Username | | | |
| LoginTicketExpired | Info | Login | Ticket sweep | AccountId | | | |
| LoginTicketReplaced | Info | Login | Pending ticket overwritten on re-redirect | AccountId, Username | | | Single-session reconnect |
| GlobalLoginSucceeded | Info | Login | Global accept | AccountId | | | |
| GlobalLoginRejected | Warning | Login | Global reject | Reason | | AUTH-002 | |
| GameSessionSuperseded | Info | Login | Older TNL session kicked by new login | AccountId, OldSessionId, NewSessionId, Reason | | | Process-local |
| AuthLoginSucceeded | Info | Auth | Good password | AccountId, Username | | | Never log password |
| AuthLoginFailed | Warning | Auth | Bad creds / locked | Reason | | AUTH-001 | |
| AuthSessionSuperseded | Info | Auth | Older Auth TCP session kicked by new login | AccountId, OldSessionId, NewSessionId | | | |
| AuthRedirectRequested | Info | Auth | Redirect to global | AccountId | | | |
| CurrencyChanged | Info | Economy | Credits mutate | CharacterId, Reason, Before, Delta, After | yes | | Before+Delta=After |
| ItemAdded | Info | Inventory | Cargo/locker upsert | CharacterId, Container, ItemCoid, ItemCbid, Quantity, X, Y | yes | | |
| ItemMoved | Info | Inventory | Cargo/locker move | CharacterId, Container, ItemCoid, … | yes | | |
| ItemRemoved | Info | Inventory | Cargo/locker delete | CharacterId, Container, ItemCoid | yes | | |
| ItemEquipped | Info | Inventory | Hardpoint equip | VehicleCoid, ItemCoid, ItemCbid | yes | | |
| ItemUnequipped | Info | Inventory | Hardpoint clear | VehicleCoid | yes | | |
| CargoCleared | Info | Inventory | Clear cargo | CharacterId | yes | | |
| InventoryRequestRejected | Warning | Inventory | Failed client inv op | Reason | | INV-001 | Always on (not debug-gated) |
| InventoryPersistFailed | Error | Inventory | DB write after memory mutate | Verb, ItemCoid, CharacterId | | DB-001 | LG-03 |
| VendorPurchaseStarted | Info | Vendor | Buy begin | TransactionId, ItemCbid, … | yes | | TX- scope |
| VendorPurchaseCompleted | Info | Vendor | Buy ok | TransactionId, … | yes | | |
| VendorPurchaseRejected | Warning | Vendor | Buy fail | Reason, TransactionId | | ECO-001 | |
| VendorSaleCompleted | Info | Vendor | Sell/buyback ok | TransactionId, Buyback? | yes | | |
| VendorSaleRejected | Warning | Vendor | Sell fail | Reason | | ECO-002 | |
| MissionGranted | Info | Mission | GrantMission | CharacterId, MissionId | yes | | |
| MissionCompleted | Info | Mission | Complete / force | CharacterId, MissionId, Xp?, Credits?, Forced? | yes | | TX when rewards |
| MissionFailed | Info | Mission | FailMission | CharacterId, MissionId | yes | | |
| MissionPersistDeadLettered | Error | Mission | Max persist retries | DeadLetteredCount | | MIS-001 | SS-14 |
| ObjectUsed | Info | Interact | Use object | Handler, TargetCoid, ObjectiveId | | | Via GameLog.Action — always in /reportbug buffer |
| NpcInteract | Info | Interact | Mission NPC dialog path | NpcCoid, NpcCbid, Outcome, Distance? | | | Outcomes: DialogOpened/OutOfRange/NoNpc/NoDialog/EmptyConsume |
| MissionDialogResponse | Info | Interact | C2S dialog OK/reject | MissionId, Accepted, NpcCoid, Outcome | | | |
| DamageDealt | Info | Combat | Player dealt damage | ActorCharacterId, TargetCoid, Damage, IsCrit | | | Combat tick; identity attached explicitly |
| DamageTaken | Info | Combat | Player took damage | VictimCharacterId, AttackerCoid, Damage, HpAfter | | | |
| Healed | Info | Combat | Player HP restored | Amount, HpAfter, Source | | | |
| CombatHit | Info | Combat | Compact hit breadcrumb | Role, OtherCoid, Damage, Killed | | | Optional summary helper; prefer DamageDealt/Taken |
| SkillCast | Info | Skills | RequestCastSkill | SkillId, Rank, Success, Response, TargetCoid | | | Success and failure |
| PlayerDied | Info | Combat | Player vehicle death | Victim/killer props | yes | | Outside packet scope — identity attached explicitly |
| NpcKilled | Info | Combat | NPC death | Victim, Killer | | | Per-kill not per-hit |
| PlayerRespawned | Info | Respawn | Respawn in sector | CharacterId | yes | | |
| LootReceived | Info | Loot | Deliver/autoloot | CharacterId, ItemCoid?, Credits? | yes | | |
| ChatCommandExecuted | Info | Chat | Any chat command | Command (first token) | yes | | Args at Debug only |
| ChatCommandArgs | Debug | Chat | Command with args | Command, ArgCount | | | |
| ChatMessageSent | Debug | Chat | Local/Global public fan-out | ChatType, CharacterId, RecipientCount, MapId?, InstanceSerial? | | | Message body not logged at Info |
| AdminCommandExecuted | Info | Admin | GM command allowed | AccountId, CharacterId, GMLevel, Command | yes | | SS-28 |
| AdminCommandDenied | Warning | Admin | GMLevel &lt; 1 | Command, GMLevel | | SEC-001 | SS-28 |
| PlayerKicked | Info | Admin | GM `/kick` disconnected session | AccountId, CharacterId, Query, AdminAccountId?, AdminCharacterId? | yes | | Process-local |
| PlayerBanned | Info | Admin | GM `/ban` set auth Locked | AccountId, CharacterId?, Query, OnlineDisconnected, AdminAccountId?, AdminCharacterId? | yes | | |
| PlayerUnbanned | Info | Admin | GM `/unban` cleared auth Locked | AccountId, Query, AdminAccountId?, AdminCharacterId? | yes | | |
| PlayerPorted | Info | Admin | GM `/port` moved a player to/from an anchor | MoverCharacterId, AnchorCharacterId, MapId, SameMap, X, Y, Z | yes | | Cross-map ports go through MapManager.TransferCharacterToMap |
| UnknownOpcodeReceived | Warning | Net | Undefined opcode | Opcode | | NET-001 | |
| MalformedPacketRejected | Warning | Net | Bad packet body | Opcode, Reason? | | NET-002 | |
| DevControlRequest | Info | Dev | HTTP dev control | Path, Method | | | Only when EnableDevControl |
| BugReportSubmitted | Info | Ops | Player `/reportbug` | ReportId, CharacterId, CharacterName, SessionId, ZipBytes, DescriptionLength | yes | | Built zip queued for Discord |
| BugReportUploaded | Info | Ops | Discord accept | ReportId, CharacterId | | | |
| BugReportUploadFailed | Warning/Error | Ops | Discord reject / exception | ReportId, Detail/ExceptionType | | SRV-002 | |

### Dynamic (not scanned as literals)

| Pattern | Level | Notes |
|---|---|---|
| `{EventName}RateLimited` | Warning | Emitted by RateLimiter on window reopen; property `Suppressed` |
| Legacy | (mapped) | Dual-write from `Logger`; property `LegacyType` |

### Operation scopes (expand to Started/Completed/Failed)

| Base name | Subsystem |
|---|---|
| CharacterWorldStateSave | Persistence |
| MapTransfer | Map |

(Test-only operations such as CharacterLoad/CharacterSave/VendorPurchase in unit tests are not production catalog rows.)

using System.Buffers;
using System.IO;

using TNL.Data;
using TNL.Entities;
using TNL.Structures;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL;

using AutoCore.Database.Char.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Extensions;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public partial class TNLConnection : GhostConnection
{
    private static NetClassRepInstance<TNLConnection> _dynClassRep;
#pragma warning disable IDE0052 // Remove unread private members
    private static NetConnectionRep _connRep;
#pragma warning restore IDE0052 // Remove unread private members

    private uint Key { get; set; }
    private long PlayerCoid { get; set; }
    private ushort FragmentCounter { get; set; } = 1;

    /// <summary>
    /// Immediately emits a queued ghost update for a state transition that the client must apply
    /// before accepting further input (currently the owner vehicle's death/corpse transition).
    /// Normal sector ticking may otherwise spend the packet budget on foreign pose updates first.
    /// </summary>
    internal void FlushDeathGhostUpdate()
    {
        if (!IsEstablished())
            return;

        CheckPacketSend(force: true, Environment.TickCount);
    }

    /// <summary>
    /// Active foreign CreateVehicle → ObjectInScope holds for this connection. Keyed by vehicle coid.
    /// Create is re-sent whenever a foreign global vehicle is not currently ghosted (first sighting
    /// or re-scope after TNL detach). Client FUN_008078b0 processes ghost object-create before game
    /// packets; ghost create uses a zeroed wheel nest, so ObjectInScope must wait until CreateVehicle
    /// has been held long enough to land. Cleared on <see cref="ResetGhosting"/> / map transfer.
    /// </summary>
    private readonly Dictionary<long, ForeignVehicleCreateHold> _globalVehicleCreates = new();
    /// <summary>coid → Unix ms when a delayed CreateVehicle owner-attach re-send is due.</summary>
    private readonly Dictionary<long, long> _foreignOwnerAttachReapplyAtUnixMs = new();

    /// <summary>
    /// Foreign path vehicles currently pinned via <see cref="GhostConnection.ObjectLocalScopeAlways"/>
    /// on this connection (see <see cref="Map.SectorMap.PerformScopeQuery"/>), keyed by vehicle coid.
    /// Tracked so a later scope pass can call <see cref="GhostConnection.ObjectLocalClearAlways"/> once
    /// a coid stops qualifying for the pin (path ended, vehicle left the grid, or despawned) — without
    /// this, a pinned ghost can never be detached by TNL's normal InScope-clearing flow again.
    /// </summary>
    private readonly Dictionary<long, GhostObject> _pinnedPathVehicles = new();

    /// <summary>Read-only view of currently pinned path-vehicle ghosts, keyed by coid.</summary>
    public IReadOnlyDictionary<long, GhostObject> PinnedPathVehicles => _pinnedPathVehicles;

    /// <summary>Records that <paramref name="coid"/>'s ghost is pinned via ObjectLocalScopeAlways.</summary>
    public void NotePathVehiclePinned(long coid, GhostObject ghost) => _pinnedPathVehicles[coid] = ghost;

    /// <summary>Forgets a coid's pin once it has been unpinned via ObjectLocalClearAlways.</summary>
    public void ClearPathVehiclePinned(long coid) => _pinnedPathVehicles.Remove(coid);

    /// <summary>
    /// P2 owner-on: first foreign ghost initial withholds owner; after delay, one descope then
    /// rescope so TNL sends a second initial with owner. Keyed by vehicle coid.
    /// </summary>
    private readonly Dictionary<long, ForeignReghostState> _foreignReghost = new();

    /// <summary>
    /// Post-create scope queries that must call <see cref="TryAllowForeignVehicleGhostScope"/> before
    /// ObjectInScope. Create itself does not count (same query continues without TryAllow).
    /// Default 1: interest selection may only include an NPC on sparse ticks; requiring 2+ meant the
    /// hold went stale and CreateVehicle re-spammed forever with zero GhostPacks.
    /// </summary>
    public static int ForeignGhostScopeHoldQueries { get; set; } = 1;

    /// <summary>Minimum wall-clock ms after CreateVehicle before ObjectInScope (0 in unit tests).</summary>
    public static int ForeignGhostScopeHoldMilliseconds { get; set; } = 500;

    /// <summary>Ms after first no-owner scope before forcing one descope for owner re-initial (P2).</summary>
    public static int ForeignReghostDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// If a create hold is not observed in scope for this many ms, drop it so re-entry re-creates.
    /// Must exceed typical interest re-selection gaps (~7s seen live) or holds die before TryAllow.
    /// </summary>
    public static int ForeignCreateHoldStaleGraceMilliseconds { get; set; } = 15000;

    /// <summary>
    /// When true, foreign CreateVehicle sets <see cref="Packets.Sector.CreateSimpleObjectPacket.IsItemLink"/>
    /// so client packet+0xA1 is non-zero and FUN_00812630 re-applies create if the object already exists
    /// from a ghost blob. Default false — multi-hold is preferred; re-apply can touch inventory/UI paths
    /// (e.g. item-link tooltips).
    /// </summary>
    public static bool ForceForeignCreateReapply { get; set; }

    /// <summary>
    /// Delay after a foreign NPC vehicle is ghosted before destroy+CreateVehicle owner-attach
    /// recovery. Ghost owner block materializes the driver first; then a full create (not
    /// IsItemLink) re-runs <c>SetVehicle</c> without tooltip UI. Default 1000 ms.
    /// </summary>
    public static int ForeignOwnerAttachReapplyMilliseconds { get; set; } = 1000;

    public Account Account { get; set; }
    public Character CurrentCharacter { get; set; }

    private sealed class ForeignVehicleCreateHold
    {
        public long CreatedAtUnixMs;
        public long LastSeenScopeUnixMs;
        public int ScopeQueriesSinceCreate;
    }

    internal enum ForeignReghostPhase : byte
    {
        None = 0,
        FirstScopedNoOwner = 1,
        Descoped = 2,
        RescopedWithOwner = 3,
    }

    private sealed class ForeignReghostState
    {
        public ForeignReghostPhase Phase;
        public long FirstScopedAtUnixMs;
    }

    /// <summary>
    /// Test-only mask profile for the initial update of a foreign global vehicle. It is unset in
    /// normal operation; recovery experiments set it on one connection so they cannot alter
    /// other clients' ghost streams.
    /// </summary>
    public ulong? ForeignVehicleInitialMaskOverrideForTests { get; set; }

    /// <summary>
    /// When set (unit tests), world-state logout persistence uses this instead of the EF singleton.
    /// </summary>
    internal static ICharacterWorldStatePersistence WorldStatePersistenceForTests { get; set; }

    /// <summary>
    /// When set (unit tests), replaces <see cref="MissionPersistence.FlushPending"/> during session end
    /// so the disconnect catch path is exercisable (production queue swallows row-level failures).
    /// </summary>
    internal static Action MissionFlushForTests { get; set; }

    private static ICharacterWorldStatePersistence WorldStatePersistence =>
        WorldStatePersistenceForTests ?? CharacterWorldStatePersistence.Instance;

    private SFragmentData FragmentGuaranteed { get; } = new();
    private SFragmentData FragmentNonGuaranteed { get; } = new();
    private SFragmentData FragmentGuaranteedOrdered { get; } = new();

    /// <summary>
    /// True while a create→ghost hold is open for <paramref name="coid"/> (create sent, ObjectInScope
    /// not yet released). SectorMap uses this to avoid re-sending CreateVehicle every hold tick.
    /// Holds not seen in scope for <see cref="ForeignCreateHoldStaleGraceMilliseconds"/> expire so
    /// leave/re-enter mid-hold re-creates.
    /// </summary>
    public bool HasActiveForeignCreateHold(long coid)
    {
        if (!_globalVehicleCreates.TryGetValue(coid, out var hold))
            return false;

        if (IsForeignCreateHoldStale(hold))
        {
            _globalVehicleCreates.Remove(coid);
            return false;
        }

        // Caller is evaluating this coid during a scope pass — keep last-seen fresh.
        hold.LastSeenScopeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return true;
    }

    /// <summary>
    /// Starts (or restarts) the create→ghost hold after a foreign <c>CreateVehicle</c> is sent.
    /// </summary>
    public void NoteForeignVehicleCreateSent(long coid)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _globalVehicleCreates[coid] = new ForeignVehicleCreateHold
        {
            CreatedAtUnixMs = now,
            LastSeenScopeUnixMs = now,
            ScopeQueriesSinceCreate = 0,
        };
    }

    /// <summary>
    /// Legacy once-per-session mark used by older scope paths. Prefer
    /// <see cref="NoteForeignVehicleCreateSent"/> + <see cref="HasActiveForeignCreateHold"/> for re-scope.
    /// Returns <c>true</c> only the first time a hold is opened for <paramref name="coid"/>.
    /// </summary>
    public bool TryMarkGlobalVehicleCreateSent(long coid)
    {
        if (HasActiveForeignCreateHold(coid))
            return false;

        NoteForeignVehicleCreateSent(coid);
        return true;
    }

    /// <summary>
    /// Whether foreign <paramref name="coid"/> may ObjectInScope yet. Call once per scope query after
    /// create has been noted. Requires <see cref="ForeignGhostScopeHoldQueries"/> further queries
    /// and <see cref="ForeignGhostScopeHoldMilliseconds"/> wall time since create.
    /// Unknown coids (create lever off / never held) are allowed immediately.
    /// Stale mid-hold entries are dropped (same rule as <see cref="HasActiveForeignCreateHold"/>).
    /// </summary>
    public bool TryAllowForeignVehicleGhostScope(long coid)
    {
        if (!_globalVehicleCreates.TryGetValue(coid, out var hold))
            return true;

        if (IsForeignCreateHoldStale(hold))
        {
            _globalVehicleCreates.Remove(coid);
            // Stale: left scope mid-hold. Caller should re-create before ObjectInScope.
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        hold.LastSeenScopeUnixMs = now;
        hold.ScopeQueriesSinceCreate++;
        var holdQueries = Math.Max(0, ForeignGhostScopeHoldQueries);
        var holdMs = Math.Max(0, ForeignGhostScopeHoldMilliseconds);
        if (hold.ScopeQueriesSinceCreate < holdQueries)
            return false;

        if (holdMs <= 0)
            return true;

        var elapsed = now - hold.CreatedAtUnixMs;
        return elapsed >= holdMs;
    }

    /// <summary>
    /// Ends the create→ghost hold after ObjectInScope succeeds so a later detach can re-create.
    /// </summary>
    public void ClearForeignVehicleCreateHold(long coid) => _globalVehicleCreates.Remove(coid);

    /// <summary>Compact TNL ghosting state for WireDiag null-wheels / owner-on hunts.</summary>
    internal string FormatGhostingDiag() =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "ghosting={0} scoping={1} doesGhostFrom={2} scopeObj={3} ghostSeq={4}",
            IsGhosting() ? 1 : 0,
            Scoping ? 1 : 0,
            DoesGhostFrom() ? 1 : 0,
            GetScopeObject() != null ? 1 : 0,
            GetGhostingSequence());

    /// <summary>
    /// Forgets all foreign create holds. Called when the client discards its local object table on
    /// a map transfer (see <see cref="EnsureGhostsAndScopeAfterMapTransfer"/>).
    /// </summary>
    internal void ClearGlobalVehicleCreateTracking()
    {
        _globalVehicleCreates.Clear();
        _foreignReghost.Clear();
        _foreignOwnerAttachReapplyAtUnixMs.Clear();
        _pinnedPathVehicles.Clear();
    }

    /// <summary>
    /// Whether to schedule a delayed CreateVehicle re-send for target-frame owner attach
    /// (NPC.md §14.4). Requires a live connection and a creature driver on the vehicle.
    /// </summary>
    public static bool ShouldScheduleForeignOwnerAttachReapply(TNLConnection connection, bool hasCreatureOwner) =>
        connection != null && hasCreatureOwner;

    /// <summary>
    /// Arms a one-shot delayed destroy+CreateVehicle for <paramref name="coid"/> so
    /// <c>CoidCurrentOwner</c> can resolve after the ghost owner block materializes the driver.
    /// </summary>
    public void ScheduleForeignOwnerAttachReapply(long coid)
    {
        var delay = Math.Max(0, ForeignOwnerAttachReapplyMilliseconds);
        _foreignOwnerAttachReapplyAtUnixMs[coid] =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + delay;
    }

    /// <summary>
    /// If a scheduled owner-attach recovery is due for <paramref name="coid"/>, clears it and
    /// returns true (caller should DestroyObject + CreateVehicle without IsItemLink).
    /// </summary>
    public bool TryConsumeForeignOwnerAttachReapply(long coid)
    {
        if (!_foreignOwnerAttachReapplyAtUnixMs.TryGetValue(coid, out var dueAt))
            return false;

        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < dueAt)
            return false;

        _foreignOwnerAttachReapplyAtUnixMs.Remove(coid);
        return true;
    }

    internal bool HasPendingForeignOwnerAttachReapplyForTests(long coid) =>
        _foreignOwnerAttachReapplyAtUnixMs.ContainsKey(coid);

    /// <summary>Test helper: move the due time into the past without sleeping.</summary>
    internal void DebugAgeForeignOwnerAttachReapplyForTests(long coid, long ageMs)
    {
        if (!_foreignOwnerAttachReapplyAtUnixMs.ContainsKey(coid))
            return;

        _foreignOwnerAttachReapplyAtUnixMs[coid] =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Math.Max(0, ageMs);
    }

    /// <summary>
    /// P2: suppress CurrentOwner on ghost packs until a second initial after descope/rescope.
    /// </summary>
    public bool ShouldSuppressForeignOwnerOnPack(long coid)
    {
        if (!GhostVehicle.EnableForeignReghostOwner)
            return false;

        if (!_foreignReghost.TryGetValue(coid, out var state))
            return true;

        return state.Phase != ForeignReghostPhase.RescopedWithOwner;
    }

    /// <summary>
    /// P2: after first no-owner scope and delay, skip ObjectInScope once so TNL kills the ghost.
    /// Returns true when the caller must not call ObjectInScope this query.
    /// </summary>
    public bool ShouldSkipForeignObjectInScopeForReghost(long coid)
    {
        if (!GhostVehicle.EnableForeignReghostOwner)
            return false;

        if (!_foreignReghost.TryGetValue(coid, out var state))
            return false;

        if (state.Phase != ForeignReghostPhase.FirstScopedNoOwner)
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var delay = Math.Max(0, ForeignReghostDelayMilliseconds);
        if (now - state.FirstScopedAtUnixMs < delay)
            return false;

        state.Phase = ForeignReghostPhase.Descoped;
        return true;
    }

    /// <summary>
    /// P2: record first no-owner scope, or promote Descoped → RescopedWithOwner on the second scope.
    /// </summary>
    public void NoteForeignVehicleGhostScoped(long coid)
    {
        if (!GhostVehicle.EnableForeignReghostOwner)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!_foreignReghost.TryGetValue(coid, out var state))
        {
            _foreignReghost[coid] = new ForeignReghostState
            {
                Phase = ForeignReghostPhase.FirstScopedNoOwner,
                FirstScopedAtUnixMs = now,
            };
            return;
        }

        if (state.Phase == ForeignReghostPhase.Descoped)
            state.Phase = ForeignReghostPhase.RescopedWithOwner;
    }

    internal ForeignReghostPhase GetForeignReghostPhaseForTests(long coid) =>
        _foreignReghost.TryGetValue(coid, out var s) ? s.Phase : ForeignReghostPhase.None;

    /// <summary>Test helper: age first-scope clock without sleeping.</summary>
    internal void DebugAgeForeignReghostFirstScopeForTests(long coid, long ageMs)
    {
        if (!_foreignReghost.TryGetValue(coid, out var state))
            return;

        state.FirstScopedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Math.Max(0, ageMs);
    }

    /// <summary>Test helper: age last-seen (left scope) without sleeping the suite.</summary>
    internal void DebugAgeForeignCreateHoldForTests(long coid, long ageMs)
    {
        if (!_globalVehicleCreates.TryGetValue(coid, out var hold))
            return;

        var past = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Math.Max(0, ageMs);
        hold.LastSeenScopeUnixMs = past;
        // Keep CreatedAt older so create-time is not confused with leave-scope stale.
        hold.CreatedAtUnixMs = Math.Min(hold.CreatedAtUnixMs, past);
    }

    /// <summary>Test helper: reset static hold knobs after experiments.</summary>
    internal static void ResetForeignGhostHoldDefaultsForTests()
    {
        ForeignGhostScopeHoldQueries = 1;
        ForeignGhostScopeHoldMilliseconds = 500;
        ForeignCreateHoldStaleGraceMilliseconds = 15000;
        ForeignReghostDelayMilliseconds = 500;
        ForeignOwnerAttachReapplyMilliseconds = 1000;
        ForceForeignCreateReapply = false;
    }

    private static bool IsForeignCreateHoldStale(ForeignVehicleCreateHold hold)
    {
        var graceMs = Math.Max(0, ForeignCreateHoldStaleGraceMilliseconds);
        // When grace is 0 (unit tests that only drive query count), never wall-stale.
        if (graceMs == 0)
            return false;

        var sinceSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - hold.LastSeenScopeUnixMs;
        return sinceSeen > graceMs;
    }

    public new static void RegisterNetClassReps()
    {
        ImplementNetConnection(out _dynClassRep, out _connRep, true);

        NetEvent.ImplementNetEvent(out RPCMsgGuaranteed.DynClassRep,                  "RPC_TNLConnection_rpcMsgGuaranteed",                  NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgGuaranteedOrdered.DynClassRep,           "RPC_TNLConnection_rpcMsgGuaranteedOrdered",           NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgNonGuaranteed.DynClassRep,               "RPC_TNLConnection_rpcMsgNonGuaranteed",               NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgGuaranteedFragmented.DynClassRep,        "RPC_TNLConnection_rpcMsgGuaranteedFragmented",        NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgGuaranteedOrderedFragmented.DynClassRep, "RPC_TNLConnection_rpcMsgGuaranteedOrderedFragmented", NetClassMask.NetClassGroupGameMask, 0);
        NetEvent.ImplementNetEvent(out RPCMsgNonGuaranteedFragmented.DynClassRep,     "RPC_TNLConnection_rpcMsgNonGuaranteedFragmented",     NetClassMask.NetClassGroupGameMask, 0);
    }

    /// <summary>
    /// Sector ghost pose is ~500 bits per vehicle. TNL default remote rates often advertise
    /// ~2500 B/s and ~96ms period → ~240 B/packet. Only a few GhostVehicle poses fit, so each
    /// NPC gets a pack every few packets (the ~250–500ms skip-forward cadence). Floor rates so
    /// multi-NPC pose streams stay dense when we have advertised high LocalRate in the ctor.
    /// </summary>
    public const uint SectorGhostMinSendBandwidth = 20000;
    public const uint SectorGhostMaxSendPeriodMs = 50;

    public TNLConnection()
    {
        SetFixedRateParameters(50, 50, 40000, 40000);
        SetPingTimeouts(7000, 6);
    }

    /// <summary>
    /// After client rate negotiation, keep a floor when this connection ghosts (sector).
    /// Remote MaxRecvBandwidth of 2500 (TNL default) otherwise caps us to starvation
    /// (~240 B/packet → each NPC pose every few packets = skip-forward cadence).
    /// </summary>
    protected override void ComputeNegotiatedRate()
    {
        base.ComputeNegotiatedRate();

        if (!DoesGhostFrom())
            return;

        // Force denser sends even if negotiation (or LocalRate/RemoteRate aliasing) collapsed
        // both sides to TNL defaults (~96ms / 2500 B/s).
        if (CurrentPacketSendPeriod > SectorGhostMaxSendPeriodMs)
            CurrentPacketSendPeriod = SectorGhostMaxSendPeriodMs;

        var flooredSize = (uint)(SectorGhostMinSendBandwidth * CurrentPacketSendPeriod * 0.001f);
        if (flooredSize > MaxPacketDataSize)
            flooredSize = MaxPacketDataSize;

        if (CurrentPacketSendSize < flooredSize)
            CurrentPacketSendSize = flooredSize;
    }

    /// <summary>Negotiated send period (ms) after floors — for diagnostics and tests.</summary>
    public uint NegotiatedPacketSendPeriodMs => CurrentPacketSendPeriod;

    /// <summary>Negotiated max payload bytes per data packet after floors — for diagnostics and tests.</summary>
    public uint NegotiatedPacketSendSizeBytes => CurrentPacketSendSize;

    ~TNLConnection()
    {
        DeleteLocalGhosts();

        if (Account != null)
            Logger.WriteLog(LogType.Network, "Client ({0} | {1}) disconnected", Account.Id, Account.Name);
        else
            Logger.WriteLog(LogType.Network, "Client ({0}) disconnected", PlayerCoid);
    }

    /// <summary>
    /// When set (unit tests), invoked instead of the real TNL RPC send path.
    /// </summary>
    internal static Action<TNLConnection, BasePacket> TestPacketSink { get; set; }

    public void SendGamePacket(BasePacket packet, RPCGuaranteeType type = RPCGuaranteeType.RPCGuaranteedOrdered, bool skipOpcode = false)
    {
        if (Diagnostics.LogFilters.OutgoingPackets)
            Logger.WriteLog(LogType.Network, "Outgoing Packet: {0}", packet.Opcode);

        if (TestPacketSink != null)
        {
            if (WireDiag.Enabled)
            {
                // Tests often short-circuit before payload serialization; still record identity.
                long sinkCoid = CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID();
                string sinkDetail = null;
                if (packet is AutoCore.Game.Packets.Sector.CreateVehiclePacket sinkCv)
                {
                    sinkCoid = sinkCv.ObjectId?.Coid ?? sinkCoid;
                    sinkDetail = WireDiag.FormatCreateVehicleDetail(sinkCv);
                }

                WireDiag.RecordGamePacket(
                    packet.Opcode.ToString(),
                    coid: sinkCoid,
                    bytes: -1,
                    playerCoid: CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID(),
                    detail: sinkDetail);
            }

            TestPacketSink(this, packet);
            return;
        }

        byte[] arr;

        using (var stream = new MemoryStream(0x4000))
        using (var writer = new BinaryWriter(stream))
        {
            if (!skipOpcode)
                writer.Write(packet.Opcode);

            packet.Write(writer);

            // Many packets intentionally "skip" reserved bytes via `writer.BaseStream.Position += N`.
            // With a MemoryStream, advancing Position does NOT automatically extend Length unless we do it.
            // If we don't extend Length, `ToArray()` will return fewer bytes than `Position`, and
            // downstream fragmentation will throw (and clients will see garbage/uninitialized fields).
            stream.SetLength(stream.Position);

            arr = stream.ToArray();
        }

        if (WireDiag.Enabled)
        {
            var previewLen = Math.Min(arr.Length, 48);
            var hex = previewLen > 0 ? Convert.ToHexString(arr.AsSpan(0, previewLen)) : null;
            long objectCoid = 0;
            string detail = null;
            if (packet is AutoCore.Game.Packets.Sector.CreateVehiclePacket cv)
            {
                objectCoid = cv.ObjectId?.Coid ?? 0;
                detail = WireDiag.FormatCreateVehicleDetail(cv);
                var wireWheelCbid = WireDiag.ExtractNestedWheelCbidFromWire(arr);
                if (Diagnostics.LogFilters.CreateVehicleWire)
                {
                    Logger.WriteLog(LogType.Network,
                        "CreateVehicle wire coid={0} bytes={1} {2} wireScanWheelCbid={3}",
                        objectCoid, arr.Length, detail, wireWheelCbid);
                }
                if (wireWheelCbid != int.MinValue && wireWheelCbid <= 0)
                {
                    Logger.WriteLog(LogType.Error,
                        "CreateVehicle NESTED_WHEEL_BAD coid={0} vehicleCbid={1} wireScanWheelCbid={2} objectWheelCbid={3}",
                        objectCoid, cv.CBID, wireWheelCbid, cv.CreateWheelSet?.CBID ?? -1);
                }
                else if (cv.CreateWheelSet != null && cv.CreateWheelSet.CBID > 0
                         && wireWheelCbid != int.MinValue && wireWheelCbid != cv.CreateWheelSet.CBID)
                {
                    Logger.WriteLog(LogType.Error,
                        "CreateVehicle NESTED_WHEEL_MISMATCH coid={0} objectWheelCbid={1} wireScanWheelCbid={2}",
                        objectCoid, cv.CreateWheelSet.CBID, wireWheelCbid);
                }
            }
            else if (packet is AutoCore.Game.Packets.Sector.CreateSimpleObjectPacket so)
                objectCoid = so.ObjectId?.Coid ?? 0;

            WireDiag.RecordGamePacket(
                packet.Opcode.ToString(),
                coid: objectCoid != 0 ? objectCoid : (CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID()),
                bytes: arr.Length,
                playerCoid: CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID(),
                hexPreview: hex,
                detail: detail);

            // Correlate Create* game packets with plain GhostObject scope (client ghost list runs first).
            if (GhostObjectDiag.Enabled
                && packet.Opcode is GameOpcode.CreateSimpleObject
                    or GameOpcode.CreateArmor
                    or GameOpcode.CreateWeapon
                    or GameOpcode.CreatePowerPlant
                    or GameOpcode.CreateWheelSet)
            {
                GhostObjectDiag.Record(
                    "SendCreate",
                    parentType: packet.Opcode.ToString(),
                    cbid: packet is AutoCore.Game.Packets.Sector.CreateSimpleObjectPacket createSo
                        ? createSo.CBID
                        : 0,
                    coid: objectCoid,
                    global: packet is AutoCore.Game.Packets.Sector.CreateSimpleObjectPacket createSo2
                        && createSo2.ObjectId != null
                        && createSo2.ObjectId.Global,
                    playerCoid: CurrentCharacter?.ObjectId.Coid ?? GetPlayerCOID(),
                    detail: string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "bytes={0} opcode={1}",
                        arr.Length,
                        packet.Opcode));
            }
        }

        if (packet.Opcode == GameOpcode.InventoryGrabResponse)
            InventoryGrabDebugLog.RecordOutgoing(arr);
        else
            InventoryDropDebugLog.RecordOutgoingIfTossRelated(packet.Opcode, arr);

        if (packet.Opcode is GameOpcode.CreateSimpleObject
            or GameOpcode.CreateArmor
            or GameOpcode.CreatePowerPlant
            or GameOpcode.CreateWeapon
            or GameOpcode.CreateWheelSet
            or GameOpcode.InventoryCargoSendAll
            or GameOpcode.InventoryAddItem
            or GameOpcode.InventoryGrabResponse
            or GameOpcode.InventoryDropResponse)
        {
            if (Diagnostics.LogFilters.InventoryFlow)
            {
                var previewLength = Math.Min(arr.Length, 96);
                Logger.WriteLog(
                    LogType.Debug,
                    $"Outgoing InventoryFlow Packet: {packet.Opcode} bytes={arr.Length} preview={Convert.ToHexString(arr.AsSpan(0, previewLength))}");
            }
        }

        var arrLength = (uint)arr.Length;
        if (arrLength > 1400U)
        {
            ++FragmentCounter;

            var doneSize = 0U;
            var count = (ushort)Math.Ceiling(arrLength / 220.0);
            for (ushort i = 0; i < count; ++i)
            {
                var buffSize = 220U;
                if (buffSize >= arrLength - doneSize)
                    buffSize = arrLength - doneSize;

                var tempBuff = new byte[buffSize];

                Array.Copy(arr, (int)doneSize, tempBuff, 0, (int)buffSize);

                var stream = new ByteBuffer(tempBuff, buffSize);

                doneSize += buffSize;

                switch (type)
                {
                    case RPCGuaranteeType.RPCGuaranteed:
                        rpcMsgGuaranteedFragmented((uint)packet.Opcode, FragmentCounter, i, count, stream);
                        break;

                    case RPCGuaranteeType.RPCGuaranteedOrdered:
                        rpcMsgGuaranteedOrderedFragmented((uint)packet.Opcode, FragmentCounter, i, count, stream);
                        break;

                    case RPCGuaranteeType.RPCUnguaranteed:
                        rpcMsgNonGuaranteedFragmented((uint)packet.Opcode, FragmentCounter, i, count, stream);
                        break;
                }
            }
        }
        else
        {
            var stream = new ByteBuffer(arr, arrLength);

            switch (type)
            {
                case RPCGuaranteeType.RPCGuaranteed:
                    rpcMsgGuaranteed((uint)packet.Opcode, stream);
                    break;

                case RPCGuaranteeType.RPCGuaranteedOrdered:
                    rpcMsgGuaranteedOrdered((uint)packet.Opcode, stream);
                    break;

                case RPCGuaranteeType.RPCUnguaranteed:
                    rpcMsgNonGuaranteed((uint)packet.Opcode, stream);
                    break;
            }
        }
    }
    #region Handler

    private void HandlePacket(ByteBuffer buffer)
    {
        var rawBytes = new byte[buffer.GetBufferSize()];
        Array.Copy(buffer.GetBuffer(), rawBytes, rawBytes.Length);

        var reader = new BinaryReader(new MemoryStream(rawBytes));
        var rawOpcode = reader.ReadUInt32();
        var gameOpcode = (GameOpcode)rawOpcode;

        if (gameOpcode == GameOpcode.InventoryGrab)
            InventoryGrabDebugLog.RecordIncoming(rawBytes);
        else
            InventoryDropDebugLog.RecordIncomingIfTossRelated(gameOpcode, rawBytes);

        // Check if the opcode is a valid enum value
        if (!Enum.IsDefined(typeof(GameOpcode), gameOpcode))
        {
            Logger.WriteLog(LogType.Error, "Unknown GameOpcode received from client: 0x{0:X} ({1})", rawOpcode, rawOpcode);
        }

        switch (gameOpcode)
        {
            case GameOpcode.CreatureMoved:
            case GameOpcode.VehicleMoved:
                break;

            default:
                if (Diagnostics.LogFilters.IncomingPackets)
                    Logger.WriteLog(LogType.Network, "Incoming Packet: {0}", gameOpcode);
                break;
        }

        try
        {
            switch (gameOpcode)
            {
                // Global
                case GameOpcode.LoginRequest:
                    HandleLoginRequestPacket(reader);
                    break;

                case GameOpcode.LoginNewCharacter:
                    HandleLoginNewCharacterPacket(reader);
                    break;

                case GameOpcode.LoginDeleteCharacter:
                    HandleLoginDeleteCharacterPacket(reader);
                    break;

                case GameOpcode.News:
                    HandleNewsPacket(reader);
                    break;

                case GameOpcode.Login:
                    HandleLoginPacket(reader);
                    break;

                case GameOpcode.Disconnect:
                    HandleDisconnectPacket(reader);
                    break;

                case GameOpcode.Chat:
                    ChatManager.Instance.HandleChatPacket(this, reader);
                    break;

                case GameOpcode.GetFriends:
                    //SocialManager.GetFriends(this);
                    break;

                case GameOpcode.GetEnemies:
                    //SocialManager.GetEnemies(this);
                    break;

                case GameOpcode.GetIgnored:
                    //SocialManager.GetIgnored(this);
                    break;

                case GameOpcode.AddFriend:
                    //SocialManager.AddEntry(this, reader, SocialType.Friend);
                    break;

                case GameOpcode.AddEnemy:
                    //SocialManager.AddEntry(this, reader, SocialType.Enemy);
                    break;

                case GameOpcode.AddIgnore:
                    //SocialManager.AddEntry(this, reader, SocialType.Ignore);
                    break;

                case GameOpcode.RemoveFriend:
                    //SocialManager.RemoveEntry(this, reader.ReadPadding(4).ReadLong(), SocialType.Friend);
                    break;

                case GameOpcode.RemoveEnemy:
                    //SocialManager.RemoveEntry(this, reader.ReadPadding(4).ReadLong(), SocialType.Enemy);
                    break;

                case GameOpcode.RemoveIgnore:
                    //SocialManager.RemoveEntry(this, reader.ReadPadding(4).ReadLong(), SocialType.Ignore);
                    break;

                case GameOpcode.RequestClanInfo:
                    //ClanManager.RequestInfo(this);
                    break;

                case GameOpcode.ConvoyMissionsRequest:
                    HandleConvoyMissionsRequest(reader);
                    break;

                case GameOpcode.RequestClanName:
                    ClanManager.Instance.HandleRequestClanNamePacket(CurrentCharacter, reader);
                    break;

                case GameOpcode.ClanUpdate:
                    ClanManager.Instance.HandleClanUpdatePacket(CurrentCharacter, reader);
                    break;

                // Sector
                case GameOpcode.TransferFromGlobal:
                    HandleTransferFromGlobalPacket(reader);
                    break;

                case GameOpcode.TransferFromGlobalStage2:
                    HandleTransferFromGlobalStage2Packet(reader);
                    break;

                case GameOpcode.TransferFromGlobalStage3:
                    HandleTransferFromGlobalStage3Packet(reader);
                    break;

                case GameOpcode.UpdateFirstTimeFlagsRequest:
                    HandleUpdateFirstTimeFlagsRequest(reader);
                    break;

                case GameOpcode.Broadcast:
                    ChatManager.Instance.HandleBroadcastPacket(this, reader);
                    break;

                case GameOpcode.CreatureMoved:
                    HandleCreatureMovedPacket(reader);
                    break;

                case GameOpcode.VehicleMoved:
                    HandleVehicleMovedPacket(reader);
                    break;

                case GameOpcode.MapTransferRequest:
                    MapManager.Instance.HandleTransferRequestPacket(CurrentCharacter, reader);
                    break;

                case GameOpcode.RespawnInSector:
                    RespawnManager.Instance.HandleRespawnInSectorPacket(CurrentCharacter, reader);
                    break;
                
                case GameOpcode.UseObject:
                    HandleUseObjectPacket(reader);
                    break;

                case GameOpcode.StoreTransactionRequest:
                    HandleStoreTransactionRequestPacket(reader);
                    break;

                case GameOpcode.StoreClose:
                    // Client closed store UI — clear server session when present.
                    HandleStoreClosePacket(reader);
                    break;

                case GameOpcode.AutoPatrol:
                    HandleAutoPatrolPacket(reader);
                    break;

                case GameOpcode.FailMission:
                    HandleFailMissionPacket(reader);
                    break;

                case GameOpcode.MissionDialogResponse:
                    HandleMissionDialogResponse(reader);
                    break;

                case GameOpcode.ChangeCombatModeRequest:
                    MapManager.Instance.HandleChangeCombatModeRequest(CurrentCharacter, reader);
                    break;

                case GameOpcode.ItemPickup:
                    HandleItemPickupPacket(reader);
                    break;

                case GameOpcode.ItemDrop:
                    HandleItemDropPacket(reader);
                    break;

                case GameOpcode.InventoryGrab:
                    HandleInventoryGrabPacket(reader);
                    break;

                case GameOpcode.InventoryGrabMM:
                    HandleInventoryGrabMMPacket(reader);
                    break;

                case GameOpcode.InventoryDrop:
                    HandleInventoryDropPacket(reader);
                    break;

                case GameOpcode.InventoryDropMM:
                    HandleInventoryDropMMPacket(reader);
                    break;

                case GameOpcode.InventoryDestroyItem:
                    HandleInventoryDestroyItemPacket(reader);
                    break;

                case GameOpcode.RequestObject:
                    HandleRequestObjectPacket(reader);
                    break;

                case GameOpcode.Firing:
                    // Client may send fire state without a VehicleMoved (stationary shooting).
                    HandleFiringPacket(reader);
                    break;

                case GameOpcode.SkillIncrement:
                    HandleSkillIncrementPacket(reader);
                    break;

                case GameOpcode.AttributeIncrement:
                    HandleAttributeIncrementPacket(reader);
                    break;

                case GameOpcode.QuickBarUpdate:
                    HandleQuickBarUpdatePacket(reader);
                    break;

                case GameOpcode.RequestCastSkill:
                    HandleRequestCastSkillPacket(reader);
                    break;

                case GameOpcode.Damage:
                    // C2S damage is not used; combat is server-authoritative via VehicleMoved/Firing.
                    break;

                default:
                    Logger.WriteLog(LogType.Error, "Unhandled Opcode: {0}", gameOpcode);
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.WriteLog(LogType.Error, "Caught exception while handling packets!");
            Logger.WriteLog(LogType.Error, "Exception: {0}", e);
        }
    }
    #endregion

    #region RPC Calls
#pragma warning disable IDE1006 // Naming Styles
    public void rpcMsgGuaranteed(uint type, ByteBuffer data)
    #region rpcMsgGuaranteed
    {
        var rpcEvent = new RPCMsgGuaranteed();
        rpcEvent.Functor.Set(new object[] { type, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteed_remote(uint _, ByteBuffer data)
    #endregion
    {
        HandlePacket(data);
    }

    public void rpcMsgGuaranteedOrdered(uint type, ByteBuffer data)
    #region rpcMsgGuaranteedOrdered
    {
        var rpcEvent = new RPCMsgGuaranteedOrdered();
        rpcEvent.Functor.Set(new object[] { type, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteedOrdered_remote(uint _, ByteBuffer data)
    #endregion
    {
        HandlePacket(data);
    }

    public void rpcMsgNonGuaranteed(uint type, ByteBuffer data)
    #region rpcMsgNonGuaranteed
    {
        var rpcEvent = new RPCMsgNonGuaranteed();
        rpcEvent.Functor.Set(new object[] { type, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgNonGuaranteed_remote(uint _, ByteBuffer data)
    #endregion
    {
        HandlePacket(data);
    }

    public void rpcMsgGuaranteedFragmented(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #region rpcMsgGuaranteedFragmented
    {
        var rpcEvent = new RPCMsgGuaranteedFragmented();
        rpcEvent.Functor.Set(new object[] { type, fragment, fragmentId, fragmentCount, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteedFragmented_remote(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #endregion
    {
        Console.WriteLine("MsgGuaranteedFragmented | Type: {0} | Fragment: {1} | FragmentId: {2} | FragmentCount: {3}", type, fragment, fragmentId, fragmentCount);

        ProcessFragment(data, FragmentGuaranteed, type, fragment, fragmentId, fragmentCount);
    }

    public void rpcMsgGuaranteedOrderedFragmented(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #region rpcMsgGuaranteedOrderedFragmented
    {
        var rpcEvent = new RPCMsgGuaranteedOrderedFragmented();
        rpcEvent.Functor.Set(new object[] { type, fragment, fragmentId, fragmentCount, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgGuaranteedOrderedFragmented_remote(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #endregion
    {
        Console.WriteLine("MsgGuaranteedOrderedFragmented | Type: {0} | Fragment: {1} | FragmentId: {2} | FragmentCount: {3}", type, fragment, fragmentId, fragmentCount);

        ProcessFragment(data, FragmentGuaranteedOrdered, type, fragment, fragmentId, fragmentCount);
    }

    public void rpcMsgNonGuaranteedFragmented(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #region rpcMsgNonGuaranteedFragmented
    {
        var rpcEvent = new RPCMsgNonGuaranteedFragmented();
        rpcEvent.Functor.Set(new object[] { type, fragment, fragmentId, fragmentCount, data });

        PostNetEvent(rpcEvent);
    }

    private void rpcMsgNonGuaranteedFragmented_remote(uint type, ushort fragment, ushort fragmentId, ushort fragmentCount, ByteBuffer data)
    #endregion
    {
        Console.WriteLine("MsgNonGuaranteedFragmented | Type: {0} | Fragment: {1} | FragmentId: {2} | FragmentCount: {3}", type, fragment, fragmentId, fragmentCount);

        ProcessFragment(data, FragmentNonGuaranteed, type, fragment, fragmentId, fragmentCount);
    }
#pragma warning restore IDE1006 // Naming Styles
    #endregion RPC Calls

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public void SetPlayerCOID(long connId)
    {
        PlayerCoid = connId;
    }

    public long GetPlayerCOID()
    {
        return PlayerCoid;
    }

    public override NetClassGroup GetNetClassGroup()
    {
        return NetClassGroup.NetClassGroupGame;
    }

    public void GetFixedRateParameters(out uint minPacketSendPeriod, out uint minPacketRecvPeriod, out uint maxSendBandwidth, out uint maxRecvBandwidth)
    {
        minPacketSendPeriod = LocalRate.MinPacketSendPeriod;
        minPacketRecvPeriod = LocalRate.MinPacketRecvPeriod;
        maxSendBandwidth    = LocalRate.MaxSendBandwidth;
        maxRecvBandwidth    = LocalRate.MaxRecvBandwidth;
    }

    public override void WriteConnectRequest(BitStream stream)
    {
        base.WriteConnectRequest(stream);

        if (Interface is not TNLInterface tInterface)
            return;

        stream.Write(TNLInterface.Version);
        stream.Write(Key);
        stream.Write(PlayerCoid);
    }

    public override bool ReadConnectRequest(BitStream stream, ref string errorString)
    {
        if (!base.ReadConnectRequest(stream, ref errorString))
        {
            Logger.WriteLog(LogType.Error, $"ReadConnectRequest: Base class validation failed: {errorString}");
            return false;
        }

        if (!stream.Read(out int version))
        {
            errorString = "Incorrect Version";
            Logger.WriteLog(LogType.Error, "ReadConnectRequest: Failed to read version");
            return false;
        }

        var expectedVersion = TNLInterface.Version;
        if (Interface is TNLInterface tInterface)
        {
            expectedVersion = tInterface.ExpectedVersion;
        }

        if (version != expectedVersion)
        {
            if (Interface is TNLInterface tInterface2 && tInterface2.AllowVersionMismatch)
            {
                Logger.WriteLog(LogType.Network, $"ReadConnectRequest: Version mismatch allowed. Expected: {expectedVersion}, Got: {version}");
            }
            else
            {
                errorString = "Incorrect Version";
                Logger.WriteLog(LogType.Error, $"ReadConnectRequest: Version mismatch. Expected: {expectedVersion}, Got: {version}");
                return false;
            }
        }

        if (!stream.Read(out uint key))
        {
            errorString = "Unknown Key";
            Logger.WriteLog(LogType.Error, "ReadConnectRequest: Failed to read key");
            return false;
        }

        if (!stream.Read(out long playerCoid))
        {
            errorString = "Unknown player ID";
            Logger.WriteLog(LogType.Error, "ReadConnectRequest: Failed to read playerCoid");
            return false;
        }

        Key = key;
        PlayerCoid = playerCoid;

        Logger.WriteLog(LogType.Network, $"ReadConnectRequest: Successfully read connection request. Version: {version}, Key: {key}, PlayerCoid: {playerCoid}");
        return true;
    }

    public override void OnConnectionEstablished()
    {
        base.OnConnectionEstablished();

        if (Interface is TNLInterface tInterface && tInterface.DoGhosting)
        {
            SetGhostTo(false);
            SetGhostFrom(true);
            // Re-apply rate floor now that DoesGhostFrom() is true (ctor negotiated without ghost).
            SetFixedRateParameters(50, 50, 40000, 40000);
            ActivateGhosting();
            Logger.WriteLog(LogType.Network,
                "Sector ghost rates: period={0}ms packetSize={1}B (floor bw={2} B/s period≤{3}ms)",
                NegotiatedPacketSendPeriodMs, NegotiatedPacketSendSizeBytes,
                SectorGhostMinSendBandwidth, SectorGhostMaxSendPeriodMs);
        }

        Logger.WriteLog(LogType.Network, $"Client ({PlayerCoid}) connected from {GetNetAddressString()}");
    }

    public override void OnConnectionTerminated(TerminationReason reason, string reasonString)
    {
        EndCharacterSession();

        var accountInfo = Account != null ? $"Account: {Account.Id} ({Account.Name})" : "Not authenticated";
        var address = SafeNetAddressString();
        Logger.WriteLog(LogType.Network, $"Client ({PlayerCoid}) disconnected from {address}. Reason: {reason}, Details: {reasonString}, {accountInfo}");
    }

    /// <summary>Net address for logging; unit tests may construct connections without a bound address.</summary>
    private string SafeNetAddressString()
    {
        try
        {
            return GetNetAddressString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Sector (owning) disconnect: persist map/pose, then tear down session (SS-03/SS-04).
    /// Global (non-owning) disconnect: only drop this connection's CurrentCharacter reference.
    /// </summary>
    internal void EndCharacterSession()
    {
        if (CurrentCharacter == null)
            return;

        var character = CurrentCharacter;
        // Owner is this connection, or an orphan still on a map (no owner after a crash mid-handoff).
        // Do NOT treat OwningConnection==null alone as ownership: after the sector owner tears down
        // (persist + SetMap(null) + clear owner), the Global connection still holds CurrentCharacter
        // and would re-persist with Map==null, overwriting the town on-foot pose with vehicle/garage
        // coords and spawning the player under terrain on the next login.
        var ownsSession = ReferenceEquals(character.OwningConnection, this)
            || (character.OwningConnection == null && character.Map != null);

        if (!ownsSession)
        {
            CurrentCharacter = null;
            return;
        }

        // Persist before SetMap(null) so Map.ContinentId is still available.
        try
        {
            CharacterWorldStatePersistence.PersistFromCharacter(character, WorldStatePersistence);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                $"EndCharacterSession: failed to persist world state for coid {character.ObjectId.Coid}: {ex.Message}");
        }

        // Drain any pending mission writes so a fast logout cannot outrun the background flush.
        try
        {
            if (MissionFlushForTests != null)
                MissionFlushForTests();
            else
                MissionPersistence.Instance.FlushPending();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                $"EndCharacterSession: failed to flush mission state for coid {character.ObjectId.Coid}: {ex.Message}");
        }

        // SS-04: drop ownership before teardown so chat/send paths cannot use a dead connection.
        character.SetOwningConnection(null);

        // Best-effort teardown: this runs on the MainLoop tick during OnConnectionTerminated, so
        // no single step may throw and abort the rest (which would leak a ghost + a stale registry
        // entry and leave a half-torn-down character). Isolate the steps so ClearGhost and the
        // registry unregister always run, and always clear CurrentCharacter in the finally.
        try
        {
            SafeTeardownStep(character, "SetMap(null)", c => c.SetMap(null));
            SafeTeardownStep(character, "vehicle.SetMap(null)", c => c.CurrentVehicle?.SetMap(null));
            SafeTeardownStep(character, "ClearGhost", c => c.ClearGhost());
            SafeTeardownStep(character, "vehicle.ClearGhost", c => c.CurrentVehicle?.ClearGhost());
            // Drop living entities from the global registry before clearing the connection
            // binding so reconnect loads a fresh character/vehicle (SS-03).
            SafeTeardownStep(character, "UnregisterCharacterSession",
                c => ObjectManager.Instance.UnregisterCharacterSession(c));
        }
        finally
        {
            CurrentCharacter = null;
        }
    }

    /// <summary>Runs one disconnect-teardown step, logging and swallowing any failure so a single
    /// bad step cannot abort the rest of <see cref="EndCharacterSession"/> on the MainLoop tick.</summary>
    private static void SafeTeardownStep(Character character, string step, Action<Character> action)
    {
        try
        {
            action(character);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                $"EndCharacterSession: teardown step '{step}' failed for coid {character.ObjectId.Coid}: {ex}");
        }
    }

    public override void PrepareWritePacket()
    {
        base.PrepareWritePacket();

        // Town maps: scan character (on foot). Field/highway: scan vehicle.
        // Checking only the vehicle left Upside teleporters (and other town pads) inert.
        if (Ghosting && CurrentCharacter != null)
            TriggerManager.Instance.CheckTriggersForPlayer(CurrentCharacter);
    }

    public NetObject GetGhost() => GetScopeObject();

    public int GetTimeSinceLastMessage() => Interface.GetCurrentTime() - LastPacketRecvTime;

    private void ProcessFragment(ByteBuffer theData, SFragmentData sFragment, uint _, ushort fragment, ushort fragmentId, ushort fragmentCount)
    {
        if (sFragment.FragmentId != fragment)
        {
            if (fragment > 0)
                Console.WriteLine("Dropped fragment: {0} vs {1}", sFragment.FragmentId, fragment);

            sFragment.FragmentId = fragment;
            sFragment.TotalSize = 0;
            sFragment.MapFragments.Clear();
        }

        sFragment.MapFragments.Add(fragmentId, theData);
        sFragment.TotalSize += theData.GetBufferSize();

        if (sFragment.MapFragments.Count == fragmentCount)
        {
            Console.WriteLine("Reassembling fragment {0} ({1} fragments", sFragment.FragmentId, fragmentCount);

            var combined = new ByteBuffer(sFragment.TotalSize);

            var off = 0U;

            for (var i = 0; i < sFragment.MapFragments.Count; ++i)
            {
                if (!sFragment.MapFragments.ContainsKey(i))
                {
                    Console.WriteLine("Big error! Fragment doesn't contain a buffer! Fragment: {0} | Index: {1}", fragment, i);
                    return;
                }

                var buff = sFragment.MapFragments[i];

                Array.Copy(buff.GetBuffer(), 0, combined.GetBuffer(), off, buff.GetBufferSize());
                off += buff.GetBufferSize();
            }

            sFragment.FragmentId = 0;
            sFragment.TotalSize = 0;
            sFragment.MapFragments.Clear();

            HandlePacket(combined);
        }
    }

    private class SFragmentData
    {
        public uint FragmentId { get; set; } = 0;
        public uint TotalSize { get; set; } = 0;
        public Dictionary<int, ByteBuffer> MapFragments { get; } = new();
    }

    #region RPC Classes
    private class RPCMsgGuaranteed : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteed> DynClassRep;
        public RPCMsgGuaranteed()
            : base(RPCGuaranteeType.RPCGuaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteed_remote", new[] { typeof(uint), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgGuaranteedOrdered : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteedOrdered> DynClassRep;
        public RPCMsgGuaranteedOrdered()
            : base(RPCGuaranteeType.RPCGuaranteedOrdered, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteedOrdered_remote", new[] { typeof(uint), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgNonGuaranteed : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgNonGuaranteed> DynClassRep;
        public RPCMsgNonGuaranteed()
            : base(RPCGuaranteeType.RPCUnguaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgNonGuaranteed_remote", new[] { typeof(uint), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgGuaranteedFragmented : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteedFragmented> DynClassRep;
        public RPCMsgGuaranteedFragmented()
            : base(RPCGuaranteeType.RPCGuaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteedFragmented_remote", new[] { typeof(uint), typeof(ushort), typeof(ushort), typeof(ushort), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgGuaranteedOrderedFragmented : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgGuaranteedOrderedFragmented> DynClassRep;
        public RPCMsgGuaranteedOrderedFragmented()
            : base(RPCGuaranteeType.RPCGuaranteedOrdered, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgGuaranteedOrderedFragmented_remote", new[] { typeof(uint), typeof(ushort), typeof(ushort), typeof(ushort), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }

    private class RPCMsgNonGuaranteedFragmented : RPCEvent
    {
        public static NetClassRepInstance<RPCMsgNonGuaranteedFragmented> DynClassRep;
        public RPCMsgNonGuaranteedFragmented()
            : base(RPCGuaranteeType.RPCUnguaranteed, RPCDirection.RPCDirAny)
        { Functor = new FunctorDecl<TNLConnection>("rpcMsgNonGuaranteedFragmented_remote", new[] { typeof(uint), typeof(ushort), typeof(ushort), typeof(ushort), typeof(ByteBuffer) }); }
        public override bool CheckClassType(object obj) { return (obj as TNLConnection) != null; }
        public override NetClassRep GetClassRep() { return DynClassRep; }
    }
    #endregion
}

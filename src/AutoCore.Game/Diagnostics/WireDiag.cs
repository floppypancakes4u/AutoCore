namespace AutoCore.Game.Diagnostics;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;

/// <summary>
/// Diagnostic wire log for isolating S2C disconnects (Invalid Packet).
/// Off by default; enable with <see cref="Enabled"/> or env <c>AUTOCORE_WIRE_DIAG=1</c>.
/// </summary>
public static class WireDiag
{
    public const string EnvironmentVariableName = "AUTOCORE_WIRE_DIAG";

    private static long _seq;
    private static readonly object Gate = new();
    private static readonly List<WireDiagEntry> Entries = new();
    private static readonly ConcurrentDictionary<long, int> PartialGhostCounts = new();

    /// <summary>When false, record methods are no-ops (hot path stays cheap).</summary>
    public static bool Enabled { get; set; }

    /// <summary>Max non-initial ghost packs logged per entity COID (initial always logged).</summary>
    public static int MaxPartialGhostPacksPerCoid { get; set; } = 3;

    /// <summary>Cap retained snapshot entries to avoid unbounded memory during long sessions.</summary>
    public static int MaxRetainedEntries { get; set; } = 2000;

    public static long CurrentSeq
    {
        get
        {
            lock (Gate)
                return _seq;
        }
    }

    public static void TryEnableFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
            return;

        Enabled = value is "1" or "true" or "TRUE" or "yes" or "YES";
    }

    public static void ResetForTests()
    {
        lock (Gate)
        {
            _seq = 0;
            Entries.Clear();
            PartialGhostCounts.Clear();
            Enabled = false;
            MaxPartialGhostPacksPerCoid = 3;
            MaxRetainedEntries = 2000;
        }
    }

    public static IReadOnlyList<WireDiagEntry> Snapshot()
    {
        lock (Gate)
            return Entries.ToList();
    }

    public static void RecordGamePacket(
        string name,
        long coid,
        int bytes,
        long playerCoid,
        string hexPreview = null,
        string detail = null)
    {
        if (!Enabled)
            return;

        Append(new WireDiagEntry
        {
            Kind = WireDiagKind.GamePacket,
            Name = name ?? "?",
            Coid = coid,
            Bytes = bytes,
            Bits = -1,
            Mask = 0,
            Initial = false,
            PlayerCoid = playerCoid,
            HexPreview = hexPreview,
            Detail = detail,
        });
    }

    /// <summary>
    /// Detail string for CreateVehicle WireDiag lines. Nested empty CreateWheelSet wires CBID=-1;
    /// a missing nested object reports as empty/-1. Positive CBID is the nested wheelset clonebase.
    /// Path A client capture showed equip failures when nested CBID was 0 (not -1).
    /// </summary>
    public static string FormatCreateVehicleDetail(CreateVehiclePacket packet)
    {
        if (packet == null)
            return "createVehicle=null";

        var nested = packet.CreateWheelSet;
        // Wire empty path writes CBID -1; treat absent nested the same for diagnostics.
        var wheelCbid = nested != null ? nested.CBID : -1;
        var nestedKind = nested == null ? "empty" : "full";
        var wheelOk = wheelCbid > 0 ? 1 : 0;
        // Use `is not null` — TFID overloads ==/!= and (null != null) is true with those operators.
        var wheelTfid = "-";
        if (nested is not null && nested.ObjectId is not null)
        {
            var tfid = nested.ObjectId;
            wheelTfid = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", tfid.Coid, tfid.Global ? 1 : 0);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "vehicleCbid={0} wheelsetCbid={1} nested={2} wheelOk={3} wheelTfid={4} templateId={5} isActive={6} spawnOwner={7} isInventory={8}",
            packet.CBID,
            wheelCbid,
            nestedKind,
            wheelOk,
            wheelTfid,
            packet.TemplateId,
            packet.IsActive ? 1 : 0,
            packet.CoidSpawnOwner,
            packet.IsInventory ? 1 : 0);
    }

    /// <summary>
    /// Scan serialized CreateVehicle bytes for nested CreateWheelSet opcode (0x201B) and return the following CBID.
    /// Returns int.MinValue if the nested opcode is not found.
    /// </summary>
    public static int ExtractNestedWheelCbidFromWire(byte[] packetBytes)
    {
        if (packetBytes == null || packetBytes.Length < 8)
            return int.MinValue;

        const uint createWheelSetOpcode = 0x201Bu;
        for (var i = 0; i + 8 <= packetBytes.Length; i++)
        {
            if (BitConverter.ToUInt32(packetBytes, i) != createWheelSetOpcode)
                continue;
            return BitConverter.ToInt32(packetBytes, i + 4);
        }

        return int.MinValue;
    }

    public static void RecordGhostPack(
        string name,
        long coid,
        int bits,
        ulong mask,
        bool initial,
        long playerCoid,
        string detail = null)
    {
        if (!Enabled)
            return;

        if (!initial)
        {
            var count = PartialGhostCounts.AddOrUpdate(coid, 1, (_, n) => n + 1);
            if (count > MaxPartialGhostPacksPerCoid)
                return;
        }

        Append(new WireDiagEntry
        {
            Kind = WireDiagKind.GhostPack,
            Name = name ?? "?",
            Coid = coid,
            Bytes = -1,
            Bits = bits,
            Mask = mask,
            Initial = initial,
            PlayerCoid = playerCoid,
            Detail = detail,
        });
    }

    public static string FormatLine(WireDiagEntry entry)
    {
        if (entry == null)
            return "[WireDiag] (null)";

        var sb = new StringBuilder(160);
        sb.Append("[WireDiag] seq=");
        sb.Append(entry.Seq);
        sb.Append(" kind=");
        sb.Append(entry.Kind);
        sb.Append(" name=");
        sb.Append(entry.Name);
        sb.Append(" coid=");
        sb.Append(entry.Coid);
        if (entry.Bits >= 0)
        {
            sb.Append(" bits=");
            sb.Append(entry.Bits);
        }

        if (entry.Bytes >= 0)
        {
            sb.Append(" bytes=");
            sb.Append(entry.Bytes);
        }

        sb.Append(" mask=0x");
        sb.Append(entry.Mask.ToString("X", CultureInfo.InvariantCulture));
        sb.Append(" initial=");
        sb.Append(entry.Initial ? 'y' : 'n');
        sb.Append(" conn=");
        sb.Append(entry.PlayerCoid);

        if (!string.IsNullOrEmpty(entry.Detail))
        {
            sb.Append(' ');
            sb.Append(entry.Detail);
        }

        if (!string.IsNullOrEmpty(entry.HexPreview))
        {
            sb.Append(" hex=");
            sb.Append(entry.HexPreview);
        }

        return sb.ToString();
    }

    private static void Append(WireDiagEntry entry)
    {
        lock (Gate)
        {
            entry.Seq = ++_seq;
            Entries.Add(entry);
            while (Entries.Count > MaxRetainedEntries)
                Entries.RemoveAt(0);
        }

        Logger.WriteLog(LogType.Network, FormatLine(entry));
    }
}

public enum WireDiagKind
{
    GamePacket,
    GhostPack,
}

public sealed class WireDiagEntry
{
    public long Seq { get; set; }
    public WireDiagKind Kind { get; set; }
    public string Name { get; set; }
    public long Coid { get; set; }
    public int Bits { get; set; }
    public int Bytes { get; set; }
    public ulong Mask { get; set; }
    public bool Initial { get; set; }
    public long PlayerCoid { get; set; }
    public string Detail { get; set; }
    public string HexPreview { get; set; }
}

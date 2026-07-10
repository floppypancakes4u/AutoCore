namespace AutoCore.Game.Diagnostics;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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
        string hexPreview = null)
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
        });
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

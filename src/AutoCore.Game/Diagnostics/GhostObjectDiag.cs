namespace AutoCore.Game.Diagnostics;

using System.Globalization;
using System.Text;
using AutoCore.Game.Entities;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

/// <summary>
/// Heavy diagnostic log for plain <see cref="GhostObject"/> lifecycle (map-prop combat ghosts,
/// simple objects). Correlates with client PathAHook <c>GhostApply</c> / <c>GhostApply_CRASH_IMMINENT</c>
/// events for AV <c>0x005B0EFF</c> (FUN_005b0ed0 null iface after "Assigned a ghost to waiting").
/// Off by default; enable via <see cref="Enabled"/>, env <c>AUTOCORE_GHOST_OBJECT_DIAG=1</c>, or
/// wire lever <c>GhostObjectDiag</c>.
/// </summary>
public static class GhostObjectDiag
{
    public const string EnvironmentVariableName = "AUTOCORE_GHOST_OBJECT_DIAG";

    private static long _seq;
    private static readonly object Gate = new();
    private static readonly List<GhostObjectDiagEntry> Entries = new();

    /// <summary>When false, record methods are no-ops.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Cap retained snapshot entries.</summary>
    public static int MaxRetainedEntries { get; set; } = 4000;

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
            Enabled = false;
            MaxRetainedEntries = 4000;
        }
    }

    public static IReadOnlyList<GhostObjectDiagEntry> Snapshot()
    {
        lock (Gate)
            return Entries.ToList();
    }

    /// <summary>
    /// True for exact <see cref="GhostObject"/> (not GhostVehicle/Creature/Character subclasses).
    /// Those are the ghosts that run client FUN_005b0ed0 on waiting-bind.
    /// </summary>
    public static bool IsPlainGhostObject(GhostObject ghost) =>
        ghost != null && ghost.GetType() == typeof(GhostObject);

    public static string FormatEntityDetail(ClonedObjectBase entity)
    {
        if (entity == null)
            return "entity=null";

        var id = entity.ObjectId;
        var coid = id?.Coid ?? -1;
        var global = id != null && id.Global ? 1 : 0;
        var pos = entity.Position;
        var hp = entity.GetCurrentHP();
        var maxHp = entity.GetMaximumHP();
        var inv = entity.IsInvincible ? 1 : 0;
        var ghostKind = entity.Ghost == null
            ? "none"
            : entity.Ghost.GetType().Name;

        return string.Format(
            CultureInfo.InvariantCulture,
            "type={0} cbid={1} coid={2} global={3} pos={4:0.###},{5:0.###},{6:0.###} hp={7}/{8} inv={9} ghost={10}",
            entity.GetType().Name,
            entity.CBID,
            coid,
            global,
            pos.X,
            pos.Y,
            pos.Z,
            hp,
            maxHp,
            inv,
            ghostKind);
    }

    public static void RecordEntity(
        string name,
        ClonedObjectBase entity,
        long playerCoid = 0,
        string extra = null)
    {
        if (!Enabled)
            return;

        var detail = FormatEntityDetail(entity);
        if (!string.IsNullOrEmpty(extra))
            detail = detail + " " + extra;

        Record(
            name,
            parentType: entity?.GetType().Name ?? "?",
            cbid: entity?.CBID ?? 0,
            coid: entity?.ObjectId?.Coid ?? 0,
            global: entity?.ObjectId?.Global ?? false,
            playerCoid: playerCoid,
            detail: detail);
    }

    public static void Record(
        string name,
        string parentType,
        int cbid,
        long coid,
        bool global,
        long playerCoid,
        string detail = null)
    {
        if (!Enabled)
            return;

        Append(new GhostObjectDiagEntry
        {
            Name = name ?? "?",
            ParentType = parentType ?? "?",
            Cbid = cbid,
            Coid = coid,
            Global = global,
            PlayerCoid = playerCoid,
            Detail = detail,
        });
    }

    public static string FormatLine(GhostObjectDiagEntry entry)
    {
        if (entry == null)
            return "[GhostObjectDiag] (null)";

        var sb = new StringBuilder(192);
        sb.Append("[GhostObjectDiag] seq=");
        sb.Append(entry.Seq);
        sb.Append(" name=");
        sb.Append(entry.Name);
        sb.Append(" parent=");
        sb.Append(entry.ParentType);
        sb.Append(" cbid=");
        sb.Append(entry.Cbid);
        sb.Append(" coid=");
        sb.Append(entry.Coid);
        sb.Append(" global=");
        sb.Append(entry.Global ? '1' : '0');
        sb.Append(" conn=");
        sb.Append(entry.PlayerCoid);
        if (!string.IsNullOrEmpty(entry.Detail))
        {
            sb.Append(' ');
            sb.Append(entry.Detail);
        }

        return sb.ToString();
    }

    private static void Append(GhostObjectDiagEntry entry)
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

public sealed class GhostObjectDiagEntry
{
    public long Seq { get; set; }
    public string Name { get; set; }
    public string ParentType { get; set; }
    public int Cbid { get; set; }
    public long Coid { get; set; }
    public bool Global { get; set; }
    public long PlayerCoid { get; set; }
    public string Detail { get; set; }
}

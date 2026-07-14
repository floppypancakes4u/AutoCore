namespace AutoCore.Game.Combat;

using System.Collections.Generic;
using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Delays final leave-map / DestroyObject for pure map props after HP hits 0.
/// Client collision already plays local break FX; server keeps the corpse ~12.5s then removes it.
/// </summary>
public static class MapPropCorpseDespawn
{
    /// <summary>Middle of the 10–15s window requested for post-ram corpse lifetime.</summary>
    public const int DespawnDelayMs = 12_500;

    private static readonly object Gate = new();
    private static readonly List<PendingEntry> PendingEntries = new();

    private sealed class PendingEntry
    {
        public GraphicsObject Prop;
        public SectorMap Map;
        public TFID ObjectId;
        public DeathType DeathType;
        public TFID Murderer;
        public long DueTickMs;
    }

    /// <summary>Schedule final network destroy + leave-map for a corpse prop still on the map.</summary>
    public static void Schedule(
        GraphicsObject prop,
        SectorMap map,
        DeathType deathType,
        TFID murderer,
        int delayMs = DespawnDelayMs)
    {
        if (prop == null || map == null)
            return;

        var due = Environment.TickCount64 + Math.Max(0, delayMs);
        var objectId = new TFID(prop.ObjectId.Coid, prop.ObjectId.Global);
        var murdererCopy = murderer == null
            ? new TFID()
            : new TFID(murderer.Coid, murderer.Global);

        lock (Gate)
        {
            // Replace existing schedule for same COID.
            PendingEntries.RemoveAll(p => p.ObjectId.Coid == objectId.Coid);
            PendingEntries.Add(new PendingEntry
            {
                Prop = prop,
                Map = map,
                ObjectId = objectId,
                DeathType = deathType,
                Murderer = murdererCopy,
                DueTickMs = due,
            });
        }

        LogFilters.WriteIf(
            LogFilters.MapPropRam,
            LogType.Debug,
            "MapPropCorpseDespawn: scheduled coid={0} cbid={1} delayMs={2}",
            objectId.Coid,
            prop.CBID,
            delayMs);
    }

    /// <summary>Process due despawns (call from sector main loop).</summary>
    public static int Tick(long nowMs = 0)
    {
        if (nowMs == 0)
            nowMs = Environment.TickCount64;

        List<PendingEntry> due;
        lock (Gate)
        {
            due = PendingEntries.Where(p => nowMs >= p.DueTickMs).ToList();
            foreach (var p in due)
                PendingEntries.Remove(p);
        }

        var count = 0;
        foreach (var p in due)
        {
            Finalize(p);
            count++;
        }

        return count;
    }

    /// <summary>Test seam: run all pending despawns immediately.</summary>
    internal static int FlushAllForTests()
    {
        List<PendingEntry> all;
        lock (Gate)
        {
            all = PendingEntries.ToList();
            PendingEntries.Clear();
        }

        foreach (var p in all)
            Finalize(p);
        return all.Count;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
            PendingEntries.Clear();
    }

    internal static int PendingCountForTests
    {
        get
        {
            lock (Gate)
                return PendingEntries.Count;
        }
    }

    private static void Finalize(PendingEntry p)
    {
        try
        {
            var prop = p.Prop;
            var map = p.Map ?? prop?.Map;
            if (map == null || p.ObjectId == null)
                return;

            // Still on map as this corpse?
            var live = map.GetObjectByCoid(p.ObjectId.Coid) as GraphicsObject;
            if (live == null)
                return;

            LogFilters.WriteIf(
                LogFilters.MapPropRam,
                LogType.Debug,
                "MapPropCorpseDespawn: finalize coid={0} cbid={1}",
                p.ObjectId.Coid,
                live.CBID);

            GraphicsObject.BroadcastDeathPublic(map, p.ObjectId, p.DeathType, p.Murderer, live.Ghost);
            if (ReferenceEquals(live.Map, map))
                live.SetMap(null);
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, "MapPropCorpseDespawn.Finalize failed: {0}", ex.Message);
        }
    }
}

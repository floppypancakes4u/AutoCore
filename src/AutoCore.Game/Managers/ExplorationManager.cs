namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

/// <summary>
/// Server-authoritative map exploration: sample terrain TGA area ids from vehicle position,
/// persist <see cref="CharacterExploration"/>, and notify the client via <see cref="UnlockRegionPacket"/>.
/// </summary>
public class ExplorationManager : Singleton<ExplorationManager>
{
    private readonly ConcurrentDictionary<int, ContinentAreaMask> _masks = new();
    private readonly ConcurrentDictionary<int, byte> _missingMaskLogged = new();
    private readonly ConcurrentDictionary<long, byte> _relockPending = new();
    private readonly ExplorationPersistenceQueue _persistQueue = new();
    private int _backgroundFlushScheduled;

    /// <summary>
    /// When true (default), enqueue schedules a ThreadPool flush so production persists
    /// without blocking the vehicle-move / tick path (SS-05).
    /// </summary>
    internal bool AutoFlushOnEnqueue { get; set; } = true;

    /// <summary>
    /// Persist one (coid, continent, bits) row. Defaults to EF <see cref="CharContext"/> write.
    /// Replace in tests (pattern: FirstTimeFlagsAccountSync saveChanges DI).
    /// </summary>
    internal Action<long, int, uint> PersistRow { get; set; }

    /// <summary>
    /// Delete one exploration row (relock). Defaults to EF; replace in tests.
    /// </summary>
    internal Action<long, int> DeleteRow { get; set; }

    /// <summary>
    /// Optional mask resolver for unit tests (skips GLM/TGA I/O).
    /// </summary>
    internal Func<SectorMap, ContinentAreaMask> ResolveMaskForTests { get; set; }

    internal int PendingPersistCount => _persistQueue.PendingCount;

    public ExplorationManager()
    {
        PersistRow = PersistRowToDatabase;
        DeleteRow = DeleteRowFromDatabase;
    }

    /// <summary>
    /// Called from vehicle movement. No-ops when map/TGA/character data is unavailable.
    /// </summary>
    public void OnVehicleMoved(Vehicle vehicle)
    {
        if (vehicle == null)
            return;

        var character = vehicle.Owner as Character ?? vehicle.GetAsCharacter();
        if (character == null)
            return;

        var map = vehicle.Map ?? character.Map;
        if (map?.MapData == null)
            return;

        TryDiscoverAt(character, map, vehicle.Position.X, vehicle.Position.Z, forceSample: false);
    }

    /// <summary>
    /// After CreateCharacterExtended is sent, re-push exploration via UnlockRegion so the client
    /// applies bits with UI notify (create-packet hash insert does not call the per-bit UI path).
    /// Also samples the current cell so the continent under the player is discovered immediately.
    /// </summary>
    public void SyncExplorationAfterLogin(Character character)
    {
        if (character == null)
            return;

        // Push known continents first (login restore → live UnlockRegion).
        foreach (var kvp in character.GetExplorationSnapshot())
            SendUnlockRegion(character, kvp.Key, kvp.Value);

        // Sample current map position even if bits were already restored.
        var map = character.Map ?? character.CurrentVehicle?.Map;
        var vehicle = character.CurrentVehicle;
        if (map != null && vehicle != null)
        {
            TryDiscoverAt(character, map, vehicle.Position.X, vehicle.Position.Z, forceSample: true);
        }
    }

    /// <summary>
    /// Test hook: attempt reveal without TGA/position (direct area id).
    /// Uses the same enqueue persist path as production discovery (SS-05).
    /// </summary>
    internal bool TryRevealForTests(Character character, int continentId, byte areaId, out uint newBits)
    {
        newBits = 0;
        if (character == null)
            return false;

        if (!character.TryRevealArea(continentId, areaId, out newBits))
            return false;

        EnqueuePersist(character, continentId, newBits);
        SendUnlockRegion(character, continentId, newBits);

        try
        {
            var xp = Experience.ExperienceService.Instance.ComputeAreaXp(continentId, areaId);
            if (xp > 0)
            {
                Experience.ExperienceService.Instance.GiveXp(
                    character,
                    xp,
                    Experience.XpSource.Area);
            }
        }
        catch
        {
            // Tests may not have full XP tables; production TryDiscoverAt logs errors.
        }

        return true;
    }

    /// <summary>
    /// Reaction UnlockContObj (type 32): ensure continent is in the character unlock hash,
    /// persist, and notify via UnlockRegion (0x205B). Idempotent; preserves explored bits.
    /// Client also applies via 0x206C GroupReactionCall → <c>CVOGReaction_UnlockContinentObject</c>.
    /// </summary>
    public bool UnlockContinent(Character character, int continentId)
    {
        if (character == null || continentId == 0)
            return false;

        var newlyUnlocked = character.TryUnlockContinent(continentId);
        var bits = character.GetExploredBits(continentId);

        // Always persist + notify so relog and map-transfer create packets stay authoritative
        // even when the continent was already present from a prior unlock/reveal.
        EnqueuePersist(character, continentId, bits);
        SendUnlockRegion(character, continentId, bits);
        return newlyUnlocked;
    }

    /// <summary>
    /// Reaction RelockContObj (type 70): remove continent from unlock hash, delete persist row,
    /// and send UnlockRegion with UnlockFlag=0.
    /// </summary>
    public bool RelockContinent(Character character, int continentId)
    {
        if (character == null || continentId == 0)
            return false;

        if (!character.TryRelockContinent(continentId))
            return false;

        EnqueuePersist(character, continentId, exploredBits: 0u, relock: true);
        SendRelockRegion(character, continentId);
        return true;
    }

    /// <summary>
    /// Drain pending exploration writes (background path / tests). Ordered by drain snapshot;
    /// latest-wins already applied at enqueue time per (coid, continentId).
    /// </summary>
    public int FlushPendingExplorations()
    {
        var persist = PersistRow ?? PersistRowToDatabase;
        var delete = DeleteRow ?? DeleteRowFromDatabase;
        return _persistQueue.Flush((coid, continentId, bits) =>
        {
            var key = PersistKey(coid, continentId);
            if (_relockPending.TryRemove(key, out _))
                delete(coid, continentId);
            else
                persist(coid, continentId, bits);
        });
    }

    /// <summary>Reset queue and test hooks (unit tests).</summary>
    internal void ResetPersistenceForTests()
    {
        AutoFlushOnEnqueue = false;
        PersistRow = PersistRowToDatabase;
        DeleteRow = DeleteRowFromDatabase;
        ResolveMaskForTests = null;
        _persistQueue.Clear();
        _relockPending.Clear();
        Interlocked.Exchange(ref _backgroundFlushScheduled, 0);
        ClearMaskCache();
    }

    /// <summary>Install a continent mask in the cache (unit tests / discover path).</summary>
    internal void SetMaskForTests(ContinentAreaMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        _masks[mask.ContinentId] = mask;
    }

    private void TryDiscoverAt(Character character, SectorMap map, float x, float z, bool forceSample)
    {
        var continentId = map.ContinentId;
        var mask = GetOrLoadMask(map);
        if (mask == null)
            return;

        var cellX = (int)((x - mask.GridSize * 0.5f) / mask.GridSize);
        var cellZ = (int)((z - mask.GridSize * 0.5f) / mask.GridSize);

        // Always record the cell when force-sampling (login); otherwise only on cell change.
        if (!character.ShouldSampleExplorationCell(continentId, cellX, cellZ) && !forceSample)
            return;

        var areaId = mask.GetAreaId(x, z);
        if (areaId < ContinentAreaMask.MinAreaId || areaId > ContinentAreaMask.MaxAreaId)
            return;

        if (!character.TryRevealArea(continentId, areaId, out var newBits))
            return;

        EnqueuePersist(character, continentId, newBits);
        SendUnlockRegion(character, continentId, newBits);

        // First-visit area XP (docs/XP.md) — once per newly set bit only.
        try
        {
            var xp = Experience.ExperienceService.Instance.ComputeAreaXp(continentId, areaId);
            if (xp > 0)
            {
                Experience.ExperienceService.Instance.GiveXp(
                    character,
                    xp,
                    Experience.XpSource.Area);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "Area XP grant failed continent={0} area={1}: {2}",
                continentId,
                areaId,
                ex.Message);
        }
    }

    private void EnqueuePersist(Character character, int continentId, uint exploredBits, bool relock = false)
    {
        // Relock: persist bits=0 with a sentinel via the same queue; flush maps relock to delete.
        // Using bits=0 alone is ambiguous (unlock-with-no-areas). Mark relock with a parallel set.
        if (relock)
            _relockPending.TryAdd(PersistKey(character.ObjectId.Coid, continentId), 1);

        _persistQueue.Enqueue(character.ObjectId.Coid, continentId, exploredBits);

        if (AutoFlushOnEnqueue)
            ScheduleBackgroundFlush();
    }

    private static long PersistKey(long coid, int continentId)
        => (coid << 32) ^ (uint)continentId;

    private void ScheduleBackgroundFlush()
    {
        if (Interlocked.CompareExchange(ref _backgroundFlushScheduled, 1, 0) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                FlushPendingExplorations();
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundFlushScheduled, 0);

                // Race: items enqueued after drain but before flag clear.
                if (_persistQueue.PendingCount > 0)
                    ScheduleBackgroundFlush();
            }
        });
    }

    private ContinentAreaMask GetOrLoadMask(SectorMap map)
    {
        if (ResolveMaskForTests != null)
            return ResolveMaskForTests(map);

        var continentId = map.ContinentId;
        if (_masks.TryGetValue(continentId, out var cached))
            return cached;

        return LoadMaskFromAssets(map, continentId);
    }

    /// <summary>
    /// GLM/TGA I/O path. Separated so unit tests can cover discovery without assets while
    /// production still loads masks from GLMs. Failures are logged once per continent.
    /// Covered by integration / asset load; unit tests inject masks via ResolveMaskForTests.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "GLM/TGA asset I/O; unit tests inject masks.")]
    private ContinentAreaMask LoadMaskFromAssets(SectorMap map, int continentId)
    {
        var mapData = map.MapData;
        var mapFileName = mapData.ContinentObject?.MapFileName;
        if (string.IsNullOrEmpty(mapFileName) || mapData.GridSize <= 0f)
        {
            LogMissingOnce(continentId, $"missing map file name or grid size (grid={mapData.GridSize})");
            return null;
        }

        var tgaName = $"{mapFileName}.tga";
        if (!AssetManager.Instance.HasFileInGLMs(tgaName))
        {
            LogMissingOnce(continentId, $"TGA '{tgaName}' not found in GLM");
            return null;
        }

        try
        {
            using var stream = AssetManager.Instance.GetFileStreamFromGLMs(tgaName);
            if (stream == null)
            {
                LogMissingOnce(continentId, $"TGA '{tgaName}' stream null");
                return null;
            }

            if (!TgaAreaMaskReader.TryReadAreaIds(stream, out var width, out var height, out var areaIds, out var error))
            {
                LogMissingOnce(continentId, $"TGA parse failed: {error}");
                return null;
            }

            var mask = new ContinentAreaMask(continentId, width, height, mapData.GridSize, areaIds);
            _masks[continentId] = mask;
            return mask;
        }
        catch (Exception ex)
        {
            LogMissingOnce(continentId, ex.Message);
            return null;
        }
    }

    private void LogMissingOnce(int continentId, string reason)
    {
        if (!_missingMaskLogged.TryAdd(continentId, 1))
            return;

        Logger.WriteLog(LogType.Error, "Exploration: no area mask for continent {0}: {1}", continentId, reason);
    }

    /// <summary>
    /// EF write used by background flush. Must never be invoked on the vehicle-move hot path.
    /// Unit tests inject <see cref="PersistRow"/>; live DB path needs CharContext connection string.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "EF CharContext I/O; unit tests inject PersistRow.")]
    private static void PersistRowToDatabase(long coid, int continentId, uint exploredBits)
    {
        try
        {
            using var context = new CharContext();
            var row = context.CharacterExplorations
                .FirstOrDefault(e => e.CharacterCoid == coid && e.ContinentId == continentId);

            if (row == null)
            {
                context.CharacterExplorations.Add(new CharacterExploration
                {
                    CharacterCoid = coid,
                    ContinentId = continentId,
                    ExploredBits = exploredBits,
                });
            }
            else
            {
                row.ExploredBits = exploredBits;
            }

            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "Exploration: failed to persist coid={0} continent={1}: {2}",
                coid, continentId, ex.Message);
        }
    }

    /// <summary>
    /// Client UnlockRegion (FUN_00809550): if the continent entry is missing it only creates an
    /// empty entry (bits=0) and ignores ExploredBits; a second packet with the same payload then
    /// applies per-bit updates. Always send twice so both bootstraps and updates work.
    /// </summary>
    private static void SendUnlockRegion(Character character, int continentId, uint exploredBits)
    {
        var conn = character.OwningConnection;
        if (conn == null)
            return;

        var packet = new UnlockRegionPacket
        {
            ContinentId = continentId,
            UnlockFlag = 1,
            ExploredBits = exploredBits,
        };

        conn.SendGamePacket(packet);
        conn.SendGamePacket(packet);
    }

    /// <summary>UnlockFlag=0 → client RelockContinentObject (drop continent entry).</summary>
    private static void SendRelockRegion(Character character, int continentId)
    {
        var conn = character.OwningConnection;
        if (conn == null)
            return;

        var packet = new UnlockRegionPacket
        {
            ContinentId = continentId,
            UnlockFlag = 0,
            ExploredBits = 0,
        };

        conn.SendGamePacket(packet);
    }

    /// <summary>EF delete for relock. Unit tests inject <see cref="DeleteRow"/>.</summary>
    [ExcludeFromCodeCoverage(Justification = "EF CharContext I/O; unit tests inject DeleteRow.")]
    private static void DeleteRowFromDatabase(long coid, int continentId)
    {
        try
        {
            using var context = new CharContext();
            var row = context.CharacterExplorations
                .FirstOrDefault(e => e.CharacterCoid == coid && e.ContinentId == continentId);
            if (row == null)
                return;

            context.CharacterExplorations.Remove(row);
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error,
                "Exploration: failed to delete coid={0} continent={1}: {2}",
                coid, continentId, ex.Message);
        }
    }

    /// <summary>Clear cached masks (tests / reload).</summary>
    internal void ClearMaskCache()
    {
        _masks.Clear();
        _missingMaskLogged.Clear();
    }
}

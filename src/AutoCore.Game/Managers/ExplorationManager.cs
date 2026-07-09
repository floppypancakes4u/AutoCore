namespace AutoCore.Game.Managers;

using System.Collections.Concurrent;
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
    /// </summary>
    internal bool TryRevealForTests(Character character, int continentId, byte areaId, out uint newBits)
    {
        newBits = 0;
        if (character == null)
            return false;

        if (!character.TryRevealArea(continentId, areaId, out newBits))
            return false;

        SendUnlockRegion(character, continentId, newBits);
        return true;
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

        PersistExploration(character, continentId, newBits);
        SendUnlockRegion(character, continentId, newBits);
    }

    private ContinentAreaMask GetOrLoadMask(SectorMap map)
    {
        var continentId = map.ContinentId;
        if (_masks.TryGetValue(continentId, out var cached))
            return cached;

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

    private static void PersistExploration(Character character, int continentId, uint exploredBits)
    {
        try
        {
            using var context = new CharContext();
            var coid = character.ObjectId.Coid;
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
                character.ObjectId.Coid, continentId, ex.Message);
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

    /// <summary>Clear cached masks (tests / reload).</summary>
    internal void ClearMaskCache()
    {
        _masks.Clear();
        _missingMaskLogged.Clear();
    }
}

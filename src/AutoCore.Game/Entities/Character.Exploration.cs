namespace AutoCore.Game.Entities;

using AutoCore.Database.Char;
using AutoCore.Database.Char.Models;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Utils;

public partial class Character
{
    /// <summary>In-memory continentId → ExploredBits (bit N-1 for area id N).</summary>
    private readonly Dictionary<int, uint> _exploredByContinent = new();

    private int _lastExplorationContinentId = int.MinValue;
    private int _lastExplorationCellX = int.MinValue;
    private int _lastExplorationCellZ = int.MinValue;

    /// <summary>Load exploration rows for this character from the char DB context.</summary>
    internal void LoadExplorations(CharContext context)
    {
        _exploredByContinent.Clear();

        if (context == null)
            return;

        var coid = ObjectId.Coid;
        var rows = context.CharacterExplorations
            .Where(e => e.CharacterCoid == coid)
            .ToList();

        foreach (var row in rows)
        {
            if (row.ContinentId == 0)
                continue;

            _exploredByContinent[row.ContinentId] = row.ExploredBits;
        }

        Logger.WriteLog(LogType.Debug,
            "Character.LoadExplorations: coid={0} loaded {1} continent exploration row(s)",
            ObjectId.Coid, _exploredByContinent.Count);
    }

    /// <summary>Test hook to seed exploration without DB.</summary>
    internal void SetExplorationsForTests(IEnumerable<CharacterExploration> rows)
    {
        _exploredByContinent.Clear();
        if (rows == null)
            return;

        foreach (var row in rows)
        {
            if (row?.ContinentId == 0)
                continue;

            _exploredByContinent[row.ContinentId] = row.ExploredBits;
        }
    }

    /// <summary>Copy up to 50 continents into CreateCharacterExtended.</summary>
    internal void WriteExploration(CreateCharacterExtendedPacket extended)
    {
        if (extended?.ContinentUnlocked == null)
            return;

        var i = 0;
        foreach (var kvp in _exploredByContinent.OrderBy(k => k.Key))
        {
            if (i >= extended.ContinentUnlocked.Length)
                break;

            extended.ContinentUnlocked[i] = new CharacterExploration
            {
                CharacterCoid = ObjectId.Coid,
                ContinentId = kvp.Key,
                ExploredBits = kvp.Value,
            };
            ++i;
        }

        for (; i < extended.ContinentUnlocked.Length; ++i)
            extended.ContinentUnlocked[i] = null;

        if (_exploredByContinent.Count > 0)
        {
            var detail = string.Join(", ",
                _exploredByContinent.OrderBy(k => k.Key).Select(k => $"{k.Key}=0x{k.Value:X8}"));
            Logger.WriteLog(LogType.Network,
                "Character.WriteExploration: coid={0} slots={1} [{2}]",
                ObjectId.Coid,
                Math.Min(_exploredByContinent.Count, extended.ContinentUnlocked.Length),
                detail);
        }
    }

    public uint GetExploredBits(int continentId)
    {
        return _exploredByContinent.TryGetValue(continentId, out var bits) ? bits : 0u;
    }

    /// <summary>Snapshot of continent → bits for login UnlockRegion sync.</summary>
    internal IReadOnlyDictionary<int, uint> GetExplorationSnapshot() => _exploredByContinent;

    /// <summary>
    /// Returns true if the terrain cell changed since last sample (and records the new cell).
    /// </summary>
    internal bool ShouldSampleExplorationCell(int continentId, int cellX, int cellZ)
    {
        if (continentId == _lastExplorationContinentId
            && cellX == _lastExplorationCellX
            && cellZ == _lastExplorationCellZ)
        {
            return false;
        }

        _lastExplorationContinentId = continentId;
        _lastExplorationCellX = cellX;
        _lastExplorationCellZ = cellZ;
        return true;
    }

    /// <summary>
    /// OR area bit into memory. Returns true if this was a new discovery.
    /// </summary>
    internal bool TryRevealArea(int continentId, byte areaId, out uint newBits)
    {
        newBits = 0;
        if (continentId == 0)
            return false;

        var bits = GetExploredBits(continentId);
        if (!ContinentAreaMask.TryAddArea(ref bits, areaId, out newBits))
            return false;

        _exploredByContinent[continentId] = newBits;
        return true;
    }
}

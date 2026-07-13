namespace AutoCore.Game.Managers.Asset;

using AutoCore.Game.Map;
using AutoCore.Utils;

public class MapDataLoader
{
    public Dictionary<int, MapData> MapDatas { get; } = new();

    public bool Load()
    {
        var heightOk = 0;
        var heightMiss = 0;

        foreach (var continentObject in AssetManager.Instance.GetContinentObjects())
        {
            var reader = AssetManager.Instance.GetFileReaderFromGLMs($"{continentObject.MapFileName}.fam");
            if (reader == null)
                continue;

            var mapData = new MapData(continentObject);
            mapData.Read(reader);
            TryLoadHeightfield(mapData, continentObject.MapFileName, ref heightOk, ref heightMiss);

            MapDatas.Add(continentObject.Id, mapData);
        }

        Logger.WriteLog(LogType.Initialize,
            $"MapDataLoader: heightfields loaded={heightOk} missing/failed={heightMiss} (source {{map}}.tga from GLM)");

        return true;
    }

    /// <summary>
    /// Extract continuous terrain height from the map TGA at load time (same encoding as the
    /// level viewer / CVOGTerrain::LoadMapImage). File lives in the maps GLM pack next to the .fam.
    /// </summary>
    private static void TryLoadHeightfield(MapData mapData, string mapFileName, ref int heightOk, ref int heightMiss)
    {
        if (string.IsNullOrEmpty(mapFileName) || mapData.TerrainWidth <= 1 || mapData.TerrainHeight <= 1
            || mapData.GridSize <= 0f)
        {
            heightMiss++;
            return;
        }

        var tgaName = $"{mapFileName}.tga";
        using var stream = AssetManager.Instance.GetFileStreamFromGLMs(tgaName);
        if (stream == null)
        {
            heightMiss++;
            return;
        }

        if (!MapTerrainHeightfield.TryLoad(
                stream,
                mapData.TerrainWidth,
                mapData.TerrainHeight,
                mapData.GridSize,
                out var field,
                out var error))
        {
            heightMiss++;
            Logger.WriteLog(LogType.Error,
                $"MapDataLoader: heightfield load failed for '{tgaName}': {error}");
            return;
        }

        mapData.SetHeightfield(field);
        heightOk++;
    }
}

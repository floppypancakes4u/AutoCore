using System.Collections.Generic;

namespace AutoCore.Game.Managers.Asset
{
    using Map;

    public class MapDataLoader
    {
        public Dictionary<int, MapData> MapDatas { get; } = new();

        public bool Load()
        {
            foreach (var continentObject in AssetManager.Instance.GetContinentObjects())
            {
                var reader = AssetManager.Instance.GetFileReader($"{continentObject.MapFileName}.fam");
                if (reader == null)
                    continue;

                var mapData = new MapData(continentObject);
                mapData.Read(reader);

                MapDatas.Add(continentObject.Id, mapData);
            }

            return true;
        }
    }
}

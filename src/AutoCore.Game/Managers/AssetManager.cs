using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AutoCore.Game.Managers
{
    using Asset;
    using CloneBases;
    using Constants;
    using Database.World.Models;
    using Map;
    using Utils;
    using Utils.Memory;

    public class AssetManager : Singleton<AssetManager>
    {
        private bool DataLoaded { get; set; }
        private WADLoader WADLoader { get; } = new();
        private GLMLoader GLMLoader { get; } = new();
        private MapDataLoader MapDataLoader { get; } = new();
        private WorldDBLoader WorldDBLoader { get; } = new();

        public string GamePath { get; private set; }
        public ServerType ServerType { get; private set; }

        #region Initialize
        public bool Initialize(string gamePath, ServerType serverType)
        {
            Logger.WriteLog(LogType.Initialize, $"Initializing Asset Manager for {serverType}...");

            GamePath = gamePath;
            ServerType = serverType;

            if (!Directory.Exists(GamePath) || !File.Exists(Path.Combine(GamePath, "exe", "autoassault.exe")))
            {
                Logger.WriteLog(LogType.Error, "Invalid GamePath is set in the config!");
                return false;
            }

            return true;
        }

        public bool LoadAllData()
        {
            if (DataLoaded)
                return false;

            var loadWadTask = Task<bool>.Factory.StartNew(() =>
            {
                return WADLoader.Load(Path.Combine(GamePath, "clonebase.wad"));
            });

            var loadGLMTask = Task<bool>.Factory.StartNew(() =>
            {
                return GLMLoader.Load(GamePath);
            });

            var loadWorldDBTask = Task<bool>.Factory.StartNew(() =>
            {
                return WorldDBLoader.Load();
            });

            var loadMapDataTask = Task.WhenAll(loadGLMTask, loadWorldDBTask).ContinueWith((previousValues) =>
            {
                if (previousValues.Result.Any(r => !r))
                    return false;

                return MapDataLoader.Load();
            });

            Task.WaitAll(loadWadTask, loadGLMTask, loadWorldDBTask, loadMapDataTask);

            if (!loadWadTask.Result || !loadGLMTask.Result || !loadWorldDBTask.Result || !loadMapDataTask.Result)
                return false;

            DataLoaded = true;

            Logger.WriteLog(LogType.Initialize, "Asset Manager has loaded all data!");

            return true;
        }
        #endregion

        #region WAD
        public CloneBase GetCloneBase(int CBID)
        {
            if (WADLoader.CloneBases.TryGetValue(CBID, out CloneBase value))
                return value;

            return null;
        }

        public T GetCloneBase<T>(int CBID) where T : CloneBase
        {
            return GetCloneBase(CBID) as T;
        }
        #endregion

        #region GLM
        public BinaryReader GetFileReader(string fileName)
        {
            return GLMLoader.GetReader(fileName);
        }
        #endregion

        #region WorldDB

        public ContinentObject GetContinentObject(int continentObjectId)
        {
            if (WorldDBLoader.ContinentObjects.TryGetValue(continentObjectId, out var result))
                return result;

            return null;
        }

        public IEnumerable<ContinentObject> GetContinentObjects()
        {
            return WorldDBLoader.ContinentObjects.Values;
        }

        public MapData GetMapData(int mapId)
        {
            if (MapDataLoader.MapDatas.TryGetValue(mapId, out var result))
                return result;

            return null;
        }

        #region Global only
        public ConfigNewCharacter GetConfigNewCharacterFor(byte characterRace, byte characterClass)
        {
            if (ServerType != ServerType.Global)
                throw new Exception("Invalid server type!");

            if (WorldDBLoader.ConfigNewCharacters.TryGetValue(Tuple.Create(characterRace, characterClass), out var result))
                return result;

            return null;
        }
        #endregion

        #region Sector only
        #endregion
        #endregion
    }
}

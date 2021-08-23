using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Managers
{
    using Asset;
    using CloneBases;
    using Utils;
    using Utils.Memory;

    public class AssetManager : Singleton<AssetManager>
    {
        private bool DataLoaded { get; set; }
        private WADLoader WADLoader { get; } = new();
        private GLMLoader GLMLoader { get; } = new();

        public string GamePath { get; private set; }

        public bool Initialize(string gamePath)
        {
            Logger.WriteLog(LogType.Initialize, "Initializing Asset Manager...");

            GamePath = gamePath;

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

            if (!WADLoader.Load(Path.Combine(GamePath, "clonebase.wad")))
                return false;

            if (!GLMLoader.Load(GamePath))
                return false;

            DataLoaded = true;

            Logger.WriteLog(LogType.Initialize, "Asset Manager has loaded all data!");
            return true;
        }

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
    }
}

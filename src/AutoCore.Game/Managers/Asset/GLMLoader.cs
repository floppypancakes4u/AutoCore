using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCore.Game.Managers.Asset
{
    using Utils;

    public class GLMLoader
    {
        private Dictionary<string, GLMEntry> FileEntries { get; } = new();

        public bool Load(string directoryPath)
        {
            try
            {
                return LoadInternal(directoryPath);
            }
            catch (Exception e)
            {
                Logger.WriteLog(LogType.Error, $"Encountered exception while loading GLM files: {e}");
                return false;
            }
        }

        public bool LoadInternal(string directoryPath)
        {
            Logger.WriteLog(LogType.Initialize, "Loading GLM files...");

            Logger.WriteLog(LogType.Initialize, $"Loaded {FileEntries.Count} GLM files with {FileEntries.Sum(f => f.Value.FileEntries.Count)} file entries!");
            return true;
        }

        private class GLMEntry
        {
            public string Name { get; init; }
            public Dictionary<string, FileEntry> FileEntries { get; } = new();

            public override string ToString()
            {
                return $"GLMEntry(Name: {Name} | FileCount: {FileEntries.Count})";
            }
        }

        private class FileEntry
        {
            public string Name { get; init; }
            public int Offset { get; init; }
            public int Size { get; init; }
            public int RealSize { get; init; }
            public int ModifiedTime { get; init; }
            public short Scheme { get; init; }
            public int PackFile { get; init; }

            public override string ToString()
            {
                return $"FileEntry(Name: {Name} | Offset: {Offset} | Size: {Size})";
            }
        }
    }
}

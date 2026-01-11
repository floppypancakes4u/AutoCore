using System.Buffers;
using System.Text;

namespace AutoCore.Game.Managers.Asset;

using AutoCore.Utils;

public class GLMLoader
{
    private const string MiscGLM = "misc.glm";

    private Dictionary<string, GLMEntry> GLMEntries { get; } = new();

    public bool Load(string directoryPath)
    {
        var glmFiles = Directory.GetFiles(directoryPath, "*.glm", SearchOption.TopDirectoryOnly);
        var successCount = 0;
        var failCount = 0;

        foreach (var filePath in glmFiles)
        {
            try
            {
                ReadGLMFile(filePath);
                successCount++;
            }
            catch (Exception e)
            {
                Logger.WriteLog(LogType.Error, $"Failed to load GLM file '{filePath}': {e.Message}");
                failCount++;
            }
        }

        if (successCount == 0)
        {
            Logger.WriteLog(LogType.Error, "No GLM files were successfully loaded!");
            return false;
        }

        Logger.WriteLog(LogType.Initialize, $"Loaded {successCount} GLM files (skipped {failCount} failed files) with {GLMEntries.Sum(f => f.Value.FileEntries.Count)} file entries!");

        return true;
    }

    public MemoryStream GetStream(string fileName)
    {
        if (GLMEntries.TryGetValue(MiscGLM, out var miscGlmEntry))
        {
            if (miscGlmEntry.FileEntries.TryGetValue(fileName, out var fileEntry))
            {
                var data = new byte[fileEntry.Size];

                miscGlmEntry.FileStream.Seek(fileEntry.Offset, SeekOrigin.Begin);
                miscGlmEntry.FileStream.Read(data, 0, fileEntry.Size);

                return new MemoryStream(data);
            }
        }

        foreach (var glmEntry in GLMEntries)
        {
            if (glmEntry.Value.FileEntries.TryGetValue(fileName, out var fileEntry))
            {
                var data = new byte[fileEntry.Size];

                glmEntry.Value.FileStream.Seek(fileEntry.Offset, SeekOrigin.Begin);
                glmEntry.Value.FileStream.Read(data, 0, fileEntry.Size);

                return new MemoryStream(data);
            }
        }

        return null;
    }

    public BinaryReader GetReader(string fileName) => new(GetStream(fileName), Encoding.UTF8, false);

    public bool CanGetReader(string fileName)
    {
        if (GLMEntries.TryGetValue(MiscGLM, out var miscGlmEntry))
            if (miscGlmEntry.FileEntries.ContainsKey(fileName))
                return true;

        foreach (var glmEntry in GLMEntries)
            if (glmEntry.Value.FileEntries.ContainsKey(fileName))
                return true;

        return false;
    }

    private void ReadGLMFile(string filePath)
    {
        FileStream fileStream = null;
        try
        {
            fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            if (fileStream.Length < 4)
                throw new Exception($"GLM file is too small ({fileStream.Length} bytes). Minimum size is 4 bytes.");

            var glmEntry = new GLMEntry
            {
                Name = Path.GetFileName(filePath),
                FileStream = fileStream
            };

            using var reader = new BinaryReader(glmEntry.FileStream, Encoding.UTF8, true);

            reader.BaseStream.Seek(reader.BaseStream.Length - 4, SeekOrigin.Begin);

            var headerOff = reader.ReadInt32();
            reader.BaseStream.Seek(headerOff, SeekOrigin.Begin);

            var strHeader = Encoding.UTF8.GetString(reader.ReadBytes(4));
            if (strHeader != "CHNK")
                throw new Exception("Invalid header found!");

            var opts = reader.ReadBytes(4);
            if (opts[0] != 66)
                throw new Exception("No support for GLM text reading!");

            if (opts[1] != 76)
                throw new Exception("Only Little Endian is supported!");

            var strTableOff = reader.ReadInt32();
            var strTableSize = reader.ReadInt32();
            var entryCount = reader.ReadInt32();

            var currPos = reader.BaseStream.Position;

            reader.BaseStream.Seek(strTableOff, SeekOrigin.Begin);

            var stringTable = reader.ReadBytes(strTableSize);
            var fileEntries = CreateEntriesByStringTable(stringTable);

            if (fileEntries.Count != entryCount)
                throw new Exception("The entry count doesn't match!");

            reader.BaseStream.Position = currPos;

            foreach (var entry in fileEntries)
            {
                entry.Read(reader, glmEntry);

                glmEntry.FileEntries.Add(entry.Name, entry);
            }

            GLMEntries.Add(glmEntry.Name, glmEntry);
            fileStream = null; // Don't dispose, it's now owned by GLMEntry
        }
        finally
        {
            // If an exception occurred before adding to GLMEntries, dispose the stream
            if (fileStream != null)
            {
                fileStream.Dispose();
            }
        }
    }

    private static List<FileEntry> CreateEntriesByStringTable(IEnumerable<byte> data)
    {
        var sList = new List<FileEntry>();

        var sb = new StringBuilder();

        foreach (var t in data)
        {
            if (t != 0)
            {
                sb.Append((char)t);
            }
            else
            {
                sList.Add(new FileEntry { Name = sb.ToString() });
                sb.Clear();
            }
        }
        return sList;
    }

    private class GLMEntry
    {
        public string Name { get; init; }
        public Dictionary<string, FileEntry> FileEntries { get; } = new();
        public FileStream FileStream { get; init; }

        public override string ToString()
        {
            return $"GLMEntry(Name: {Name} | FileCount: {FileEntries.Count})";
        }
    }

    private class FileEntry
    {
        public string Name { get; init; }
        public int Offset { get; private set; }
        public int Size { get; private set; }
        public int RealSize { get; private set; }
        public int ModifiedTime { get; private set; }
        public short Scheme { get; private set; }
        public GLMEntry Parent { get; private set; }

        public void Read(BinaryReader reader, GLMEntry parent)
        {
            Parent = parent;

            Offset = reader.ReadInt32();
            Size = reader.ReadInt32();
            RealSize = reader.ReadInt32();
            ModifiedTime = reader.ReadInt32();
            Scheme = reader.ReadInt16();
            _ = reader.ReadInt32();
        }

        public override string ToString()
        {
            return $"FileEntry(Name: {Name} | Offset: {Offset} | Size: {Size} | Parent: {Parent})";
        }
    }
}

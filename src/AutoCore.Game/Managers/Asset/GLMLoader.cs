using System.Text;

namespace AutoCore.Game.Managers.Asset;

using AutoCore.Utils;
using AutoCore.Utils.Memory;

public class GLMLoader
{
    private Dictionary<string, GLMEntry> GLMEntries { get; } = new();

    public bool Load(string directoryPath)
    {
        try
        {
            Directory.GetFiles(directoryPath, "*.glm", SearchOption.TopDirectoryOnly).ToList().ForEach(ReadGLMFile);

            Logger.WriteLog(LogType.Initialize, $"Loaded {GLMEntries.Count} GLM files with {GLMEntries.Sum(f => f.Value.FileEntries.Count)} file entries!");

            return true;
        }
        catch (Exception e)
        {
            Logger.WriteLog(LogType.Error, $"Encountered exception while loading GLM files: {e}");
        }

        return false;
    }

    public BinaryReader GetReader(string fileName)
    {
        foreach (var glmEntry in GLMEntries)
        {
            if (glmEntry.Value.FileEntries.TryGetValue(fileName, out var fileEntry))
            {
                var dataStream = new ArrayPoolMemoryStream(fileEntry.Size);

                glmEntry.Value.FileStream.Seek(fileEntry.Offset, SeekOrigin.Begin);
                glmEntry.Value.FileStream.Read(dataStream.Data, 0, fileEntry.Size);

                return new BinaryReader(dataStream, Encoding.UTF8, false);
            }    
        }

        return null;
    }

    private void ReadGLMFile(string filePath)
    {
        var glmEntry = new GLMEntry
        {
            Name = Path.GetFileName(filePath),
            FileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
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

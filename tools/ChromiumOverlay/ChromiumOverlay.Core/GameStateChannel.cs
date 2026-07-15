using System.IO.MemoryMappedFiles;
using System.Text;

namespace ChromiumOverlay;

/// <summary>
/// Shared memory channel between ChromiumBridge.dll (writer in game process)
/// and ChromiumHost (reader). Layout MUST match ChromiumBridge.cpp:
///   +0x00 int Magic 'CEF1' (bytes 43 45 46 31 → 0x31464543 LE)
///   +0x04 int Version
///   +0x08 int Seq
///   +0x0C int JsonLength
///   +0x10 bytes JSON payload (MaxJsonBytes)
/// </summary>
public sealed class GameStateChannel : IDisposable
{
    public const string MappingName = "Local\\AutoCoreChromium_State";
    public const int MagicCef1 = 0x31464543; // 'CEF1' little-endian
    public const int ChannelVersion = 1;
    public const int MagicOffset = 0x00;
    public const int VersionOffset = 0x04;
    public const int SeqOffset = 0x08;
    public const int JsonLengthOffset = 0x0C;
    public const int JsonPayloadOffset = 0x10;
    public const int MaxJsonBytes = 4096;
    public const int ChannelByteSize = JsonPayloadOffset + MaxJsonBytes;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly bool _ownsMapping;
    private bool _disposed;

    private GameStateChannel(MemoryMappedFile file, MemoryMappedViewAccessor view, bool ownsMapping)
    {
        _file = file;
        _view = view;
        _ownsMapping = ownsMapping;
    }

    public static GameStateChannel Create(string? mappingName = null)
    {
        var name = mappingName ?? MappingName;
        var file = MemoryMappedFile.CreateOrOpen(name, ChannelByteSize);
        var view = file.CreateViewAccessor(0, ChannelByteSize);
        var channel = new GameStateChannel(file, view, ownsMapping: true);
        channel.WriteHeader();
        return channel;
    }

    public static GameStateChannel Open(string? mappingName = null)
    {
        var name = mappingName ?? MappingName;
        var file = MemoryMappedFile.OpenExisting(name);
        var view = file.CreateViewAccessor(0, ChannelByteSize);
        return new GameStateChannel(file, view, ownsMapping: true);
    }

    public bool IsValid
    {
        get
        {
            ThrowIfDisposed();
            return _view.ReadInt32(MagicOffset) == MagicCef1
                   && _view.ReadInt32(VersionOffset) == ChannelVersion;
        }
    }

    public void Publish(string json)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(json);

        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxJsonBytes)
            throw new ArgumentException($"JSON payload exceeds {MaxJsonBytes} bytes.", nameof(json));

        _view.Write(JsonLengthOffset, bytes.Length);
        if (bytes.Length > 0)
            _view.WriteArray(JsonPayloadOffset, bytes, 0, bytes.Length);

        var next = _view.ReadInt32(SeqOffset) + 1;
        _view.Write(SeqOffset, next);
        _view.Write(MagicOffset, MagicCef1);
        _view.Write(VersionOffset, ChannelVersion);
    }

    public int ReadSeq()
    {
        ThrowIfDisposed();
        if (!IsValid)
            return 0;
        return _view.ReadInt32(SeqOffset);
    }

    public bool TryReadJson(out string json)
    {
        json = string.Empty;
        ThrowIfDisposed();
        if (!IsValid)
            return false;

        var len = _view.ReadInt32(JsonLengthOffset);
        if (len < 0 || len > MaxJsonBytes)
            return false;
        if (len == 0)
        {
            json = string.Empty;
            return true;
        }

        var buffer = new byte[len];
        _view.ReadArray(JsonPayloadOffset, buffer, 0, len);
        json = Encoding.UTF8.GetString(buffer);
        return true;
    }

    public void CorruptMagicForTests()
    {
        ThrowIfDisposed();
        _view.Write(MagicOffset, 0);
    }

    private void WriteHeader()
    {
        _view.Write(MagicOffset, MagicCef1);
        _view.Write(VersionOffset, ChannelVersion);
        _view.Write(SeqOffset, 0);
        _view.Write(JsonLengthOffset, 0);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GameStateChannel));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _view.Dispose();
        if (_ownsMapping)
            _file.Dispose();
    }
}

using ChromiumOverlay;

namespace ChromiumOverlay.Tests;

public class GameStateChannelTests
{
    [Fact]
    public void Layout_Constants_MatchDocumentedOffsets()
    {
        Assert.Equal("Local\\AutoCoreChromium_State", GameStateChannel.MappingName);
        Assert.Equal(0x31464543, GameStateChannel.MagicCef1); // 'CEF1' LE
        Assert.Equal(1, GameStateChannel.ChannelVersion);
        Assert.Equal(0x00, GameStateChannel.MagicOffset);
        Assert.Equal(0x04, GameStateChannel.VersionOffset);
        Assert.Equal(0x08, GameStateChannel.SeqOffset);
        Assert.Equal(0x0C, GameStateChannel.JsonLengthOffset);
        Assert.Equal(0x10, GameStateChannel.JsonPayloadOffset);
        Assert.Equal(4096, GameStateChannel.MaxJsonBytes);
        Assert.Equal(0x10 + 4096, GameStateChannel.ChannelByteSize);
    }

    [Fact]
    public void WriterAndReader_RoundTripJsonPayloadAndSeq()
    {
        var mappingName = "Local\\AutoCoreChromium_State_Test_" + Guid.NewGuid().ToString("N");
        using var writer = GameStateChannel.Create(mappingName);
        using var reader = GameStateChannel.Open(mappingName);

        Assert.True(writer.IsValid);
        Assert.Equal(0, reader.ReadSeq());

        var json = """{"pid":1234,"tick":7,"message":"hello from bridge"}""";
        writer.Publish(json);

        Assert.Equal(1, reader.ReadSeq());
        Assert.True(reader.TryReadJson(out var payload));
        Assert.Equal(json, payload);

        writer.Publish("""{"tick":8}""");
        Assert.Equal(2, reader.ReadSeq());
        Assert.True(reader.TryReadJson(out var payload2));
        Assert.Equal("""{"tick":8}""", payload2);
    }

    [Fact]
    public void TryReadJson_ReturnsFalse_WhenMagicInvalid()
    {
        var mappingName = "Local\\AutoCoreChromium_State_Bad_" + Guid.NewGuid().ToString("N");
        using var channel = GameStateChannel.Create(mappingName);
        channel.CorruptMagicForTests();

        Assert.False(channel.IsValid);
        Assert.False(channel.TryReadJson(out _));
    }

    [Fact]
    public void Publish_RejectsOversizedPayload()
    {
        var mappingName = "Local\\AutoCoreChromium_State_Big_" + Guid.NewGuid().ToString("N");
        using var writer = GameStateChannel.Create(mappingName);
        var huge = new string('x', GameStateChannel.MaxJsonBytes + 1);

        Assert.Throws<ArgumentException>(() => writer.Publish(huge));
    }
}

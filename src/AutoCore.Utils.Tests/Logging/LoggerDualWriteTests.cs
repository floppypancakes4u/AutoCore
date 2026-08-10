using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Utils.Tests.Logging;

using AutoCore.Utils.Logging;

/// <summary>
/// The legacy Logger has ~750 call sites that will never all be converted; the dual-write
/// layer makes every one of them session-traceable for free by mirroring each line into
/// the structured pipeline enriched with ambient context.
/// </summary>
[TestClass]
public class LoggerDualWriteTests
{
    private InMemoryLogSink _sink;

    [TestInitialize]
    public void Init()
    {
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false, IsDebugMode = true });
        _sink = new InMemoryLogSink();
        GameLog.SetSinkForTests(_sink);
        GameLog.MinimumLevel = StructuredLogLevel.Trace;
    }

    [TestCleanup]
    public void Cleanup()
    {
        GameLog.ResetForTests();
        LogContext.ClearForTests();
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
    }

    [TestMethod]
    public void WriteLog_MirrorsLineIntoStructuredPipeline_AsLegacyRecord()
    {
        Logger.WriteLog(LogType.Network, "client connected from somewhere");

        var record = _sink.Single("Legacy");

        Assert.AreEqual(StructuredLogLevel.Info, record.Level,
            "Facility types such as Network map to INFO.");
        Assert.AreEqual("client connected from somewhere", record.Message,
            "The raw message must be preserved (without the human-format timestamp prefix).");
        Assert.AreEqual("Network", record.GetProperty("LegacyType"),
            "The original LogType is kept as a property so facility filtering still works.");
    }

    [TestMethod]
    public void WriteLog_SeverityTypes_MapToMatchingStructuredLevels()
    {
        Logger.WriteLog(LogType.Debug, "d");
        Logger.WriteLog(LogType.Warning, "w");
        Logger.WriteLog(LogType.Error, "e");
        Logger.WriteLog(LogType.Fatal, "f");

        // Debug lines are prefixed with map/char identity before dual-write.
        Assert.AreEqual(StructuredLogLevel.Debug, RecordContaining("d").Level);
        Assert.AreEqual(StructuredLogLevel.Warning, RecordWithMessage("w").Level);
        Assert.AreEqual(StructuredLogLevel.Error, RecordWithMessage("e").Level);
        Assert.AreEqual(StructuredLogLevel.Fatal, RecordWithMessage("f").Level);
    }

    [TestMethod]
    public void WriteLog_Debug_DualWriteMessage_IncludesMapCharacterPrefix()
    {
        using (LogContext.Push(
            ("MapName", "Hestia"),
            ("MapId", 707),
            ("CharacterName", "Ada"),
            ("CharacterId", 12L)))
        {
            Logger.WriteLog(LogType.Debug, "skill cast");
        }

        var record = RecordContaining("skill cast");
        StringAssert.Contains(record.Message, "map=Hestia(707)");
        StringAssert.Contains(record.Message, "char=Ada(12)");
        StringAssert.Contains(record.Message, "skill cast");
    }

    [TestMethod]
    public void WriteLog_LegacyLine_IsEnrichedWithAmbientContext()
    {
        using (LogContext.Push(("SessionId", "s-legacy"), ("CharacterId", 42L)))
        {
            Logger.WriteLog(LogType.Command, "did a thing");
        }

        var record = _sink.Single("Legacy");

        Assert.AreEqual("s-legacy", record.GetProperty("SessionId"),
            "Untouched legacy call sites must still be traceable to the player session.");
        Assert.AreEqual(42L, record.GetProperty("CharacterId"));
    }

    [TestMethod]
    public void WriteLog_ExportData_IsNotMirrored()
    {
        Logger.WriteLog(LogType.ExportData, "raw payload dump");

        Assert.AreEqual(0, _sink.Records.Count,
            "ExportData is a raw data channel, not a log line; mirroring it would corrupt the event stream.");
    }

    [TestMethod]
    public void WriteException_MirrorsWithExceptionDetailsPreserved()
    {
        Logger.WriteException(LogType.Error, "SaveCharacter(coid=42)", new InvalidOperationException("boom"));

        var record = _sink.Single("Legacy");

        Assert.AreEqual(StructuredLogLevel.Error, record.Level);
        StringAssert.Contains(record.Message, "SaveCharacter(coid=42)");
        StringAssert.Contains(record.Message, nameof(InvalidOperationException),
            "The exception type (and stack) must survive into the structured record.");
    }

    private StructuredLogRecord RecordWithMessage(string message)
    {
        var match = _sink.Records.FirstOrDefault(r => r.Message == message);
        Assert.IsNotNull(match, $"Expected a mirrored record with message '{message}'.");
        return match;
    }

    private StructuredLogRecord RecordContaining(string fragment)
    {
        var match = _sink.Records.FirstOrDefault(r =>
            r.Message != null && r.Message.Contains(fragment, StringComparison.Ordinal));
        Assert.IsNotNull(match, $"Expected a mirrored record containing '{fragment}'.");
        return match;
    }
}

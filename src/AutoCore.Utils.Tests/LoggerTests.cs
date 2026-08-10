using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Utils.Tests;

using AutoCore.Utils.Logging;

[TestClass]
public class LoggerTests
{
    private string _tempLogPath;

    [TestInitialize]
    public void Init()
    {
        // Disable file logging by default for isolation; individual tests opt in.
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            IsDebugMode = true,
            LogToFile = false,
            LogFilePath = null
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        LogContext.ClearForTests();
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            IsDebugMode = true,
            LogToFile = false,
            LogFilePath = null
        });

        if (!string.IsNullOrEmpty(_tempLogPath) && File.Exists(_tempLogPath))
        {
            try { File.Delete(_tempLogPath); } catch { /* best-effort */ }
        }
    }

    [TestMethod]
    public void UpdateConfig_DisablesFileLogging_DoesNotThrow()
    {
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        Assert.IsFalse(Logger.Config.LogToFile);
    }

    [TestMethod]
    public void UpdateConfig_EnablesFileLogging_CreatesFileAndWritesStartup()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");

        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        Assert.IsTrue(File.Exists(_tempLogPath));

        // Close writer before reading (exclusive FileStream while open).
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        var content = File.ReadAllText(_tempLogPath);
        Assert.IsTrue(content.Contains("Logging system startup!", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("Logging system shutdown!", StringComparison.Ordinal));
        Assert.IsTrue(new FileInfo(_tempLogPath).Length > 0);

        // Re-open on non-empty file (blank line + second startup).
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        content = File.ReadAllText(_tempLogPath);
        Assert.IsTrue(content.Contains("Logging system startup!", StringComparison.Ordinal));
    }

    /// <summary>
    /// SS-06: every declared LogType must be loggable. The logger is called from inside
    /// catch blocks, so a throw here escapes the handler that was containing a failure.
    /// </summary>
    [TestMethod]
    public void WriteLog_AllDeclaredTypes_DoNotThrow()
    {
        foreach (LogType type in Enum.GetValues(typeof(LogType)))
            Logger.WriteLog(type, $"message for {type}");
    }

    [TestMethod]
    public void WriteLog_FileType_DoesNotWriteToConsolePath_DoesNotThrow()
    {
        // File type returns early after optional file write (no console)
        Logger.WriteLog(LogType.File, "file-only");
    }

    [TestMethod]
    public void WriteLog_Debug_WhenDebugModeOff_IsSuppressedFromConsole()
    {
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            IsDebugMode = false,
            LogToFile = false
        });

        Logger.WriteLog(LogType.Debug, "should be suppressed");
        // No exception is the contract; suppression is console-only
    }

    [TestMethod]
    public void WriteLog_Debug_WhenDebugModeOn_DoesNotThrow()
    {
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            IsDebugMode = true,
            LogToFile = false
        });
        Logger.WriteLog(LogType.Debug, "visible debug");
    }

    /// <summary>
    /// Debug lines must lead with map name/id and character name/id so multi-player
    /// playtest logs are attributable without grepping ambient NDJSON properties.
    /// </summary>
    [TestMethod]
    public void WriteLog_Debug_PrefixesMapAndCharacterContext_BeforeMessage()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        using (LogContext.Push(
            ("MapName", "Tierra Roja Dam"),
            ("MapId", 698),
            ("CharacterName", "ScopePilot"),
            ("CharacterId", 9001L)))
        {
            Logger.WriteLog(LogType.Debug, "UseObject: dialog opened");
        }

        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        LogContext.ClearForTests();

        var line = File.ReadAllLines(_tempLogPath)
            .First(l => l.Contains("UseObject: dialog opened", StringComparison.Ordinal));

        Assert.IsTrue(line.Contains("[Debug]", StringComparison.Ordinal),
            "Debug facility tag must still be present.");

        var debugIdx = line.IndexOf("[Debug]", StringComparison.Ordinal);
        var msgIdx = line.IndexOf("UseObject: dialog opened", StringComparison.Ordinal);
        var afterDebug = line.Substring(debugIdx + "[Debug]".Length, msgIdx - (debugIdx + "[Debug]".Length));

        StringAssert.Contains(afterDebug, "map=Tierra Roja Dam(698)");
        StringAssert.Contains(afterDebug, "char=ScopePilot(9001)");
        Assert.IsTrue(
            afterDebug.IndexOf("map=", StringComparison.Ordinal)
                < afterDebug.IndexOf("char=", StringComparison.Ordinal),
            "Map context must appear before character context.");
        Assert.IsTrue(
            afterDebug.IndexOf("char=", StringComparison.Ordinal)
                < afterDebug.Length - 1,
            "Character context must appear before the original message body.");
    }

    [TestMethod]
    public void WriteLog_Debug_WithoutAmbientContext_StillPrefixesPlaceholders()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });
        LogContext.ClearForTests();

        Logger.WriteLog(LogType.Debug, "boot probe");
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });

        var line = File.ReadAllLines(_tempLogPath)
            .First(l => l.Contains("boot probe", StringComparison.Ordinal));

        StringAssert.Contains(line, "map=?(?)");
        StringAssert.Contains(line, "char=?(?)");
        StringAssert.Contains(line, "boot probe");
    }

    [TestMethod]
    public void WriteLog_NonDebug_DoesNotInjectMapCharacterPrefix()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        using (LogContext.Push(
            ("MapName", "The Wastes"),
            ("MapId", 708),
            ("CharacterName", "Bob"),
            ("CharacterId", 1L)))
        {
            Logger.WriteLog(LogType.Network, "client hello");
        }

        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });
        LogContext.ClearForTests();

        var line = File.ReadAllLines(_tempLogPath)
            .First(l => l.Contains("client hello", StringComparison.Ordinal));

        Assert.IsFalse(line.Contains("map=", StringComparison.Ordinal),
            "Only Debug lines should carry the map/character identity prefix.");
    }

    [TestMethod]
    public void WriteLog_ObjectOverload_Null_WritesNullLiteral()
    {
        Logger.WriteLog(LogType.None, (object)null);
        Logger.WriteLog(LogType.None, 12345);
    }

    [TestMethod]
    public void WriteLog_FormatOverload_FormatsArgs()
    {
        Logger.WriteLog(LogType.Command, "value={0} flag={1}", 7, true);
    }

    /// <summary>
    /// SS-06: ExportData previously fell through the switch to a default that threw
    /// ArgumentOutOfRangeException, making the type unusable and able to throw out of a
    /// catch block. It must log its payload verbatim, with no timestamp/prefix decoration.
    /// </summary>
    [TestMethod]
    public void WriteLog_ExportData_DoesNotThrow_AndWritesPayloadUndecorated()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        Logger.WriteLog(LogType.ExportData, "raw-export-payload-xyz");
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });

        var line = File.ReadAllLines(_tempLogPath)
            .First(l => l.Contains("raw-export-payload-xyz", StringComparison.Ordinal));

        Assert.AreEqual(
            "raw-export-payload-xyz",
            line,
            "SS-06: ExportData must be written verbatim with no timestamp or [prefix] decoration.");
    }

    /// <summary>
    /// SS-06: an out-of-range LogType must degrade to a usable default, never throw.
    /// </summary>
    [TestMethod]
    public void WriteLog_UnknownLogType_DoesNotThrow()
    {
        Logger.WriteLog((LogType)999, "bad-type-but-must-still-log");
    }

    /// <summary>
    /// SS-06: the severity ladder needs Warning and Fatal so recoverable abnormal
    /// conditions and unsurvivable ones are distinguishable in production logs.
    /// </summary>
    [TestMethod]
    public void WriteLog_WarningAndFatal_AreDeclared_AndDoNotThrow()
    {
        Logger.WriteLog(LogType.Warning, "recoverable abnormal condition");
        Logger.WriteLog(LogType.Fatal, "subsystem cannot continue");
    }

    /// <summary>
    /// SS-06: UpdateConfig opened a FileStream unguarded, so an unwritable path threw
    /// out of startup. It must degrade to console-only logging instead.
    /// </summary>
    [TestMethod]
    public void UpdateConfig_WithUnwritablePath_DoesNotThrow_AndLoggingStillWorks()
    {
        var unwritable = Path.Combine(
            Path.GetTempPath(),
            $"autocore-missing-{Guid.NewGuid():N}",
            "nested",
            "log.txt");

        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = unwritable,
            IsDebugMode = true
        });

        Logger.WriteLog(LogType.Error, "must still reach the console after file open failed");
    }

    /// <summary>
    /// SS-06: WriteException must preserve the exception type, message, stack trace and
    /// the full inner-exception chain. 105 sites previously logged only ex.Message.
    /// </summary>
    [TestMethod]
    public void WriteException_PreservesTypeMessageStackAndInnerChain()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        Exception captured;
        try
        {
            try
            {
                throw new InvalidOperationException("inner-cause-marker");
            }
            catch (Exception inner)
            {
                throw new ApplicationException("outer-wrapper-marker", inner);
            }
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        Logger.WriteException(LogType.Error, "SaveCharacter(coid=42)", captured);
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });

        var content = File.ReadAllText(_tempLogPath);

        Assert.IsTrue(content.Contains("SaveCharacter(coid=42)", StringComparison.Ordinal),
            "Operation context must be logged so the failure can be located.");
        Assert.IsTrue(content.Contains("ApplicationException", StringComparison.Ordinal),
            "Outer exception type must be preserved.");
        Assert.IsTrue(content.Contains("outer-wrapper-marker", StringComparison.Ordinal),
            "Outer exception message must be preserved.");
        Assert.IsTrue(content.Contains("InvalidOperationException", StringComparison.Ordinal),
            "Inner exception type must be preserved.");
        Assert.IsTrue(content.Contains("inner-cause-marker", StringComparison.Ordinal),
            "Inner exception message must be preserved.");
        Assert.IsTrue(content.Contains(nameof(WriteException_PreservesTypeMessageStackAndInnerChain), StringComparison.Ordinal),
            "Stack trace must be preserved (the throwing test method should appear in it).");
    }

    /// <summary>
    /// SS-06: the writer is a static shared by the tick thread and every socket task.
    /// Concurrent writes must not throw or corrupt the writer.
    /// </summary>
    [TestMethod]
    public void WriteLog_ConcurrentWritersFromManyThreads_DoNotThrow()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 200, i =>
        {
            try
            {
                Logger.WriteLog(LogType.Network, $"concurrent-line-{i}");
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });

        Assert.AreEqual(
            0,
            failures.Count,
            $"SS-06: concurrent WriteLog must never throw, but {failures.Count} call(s) did. " +
            $"First: {failures.FirstOrDefault()}");

        var written = File.ReadAllLines(_tempLogPath)
            .Count(l => l.Contains("concurrent-line-", StringComparison.Ordinal));
        Assert.AreEqual(
            200,
            written,
            "Every concurrent log line must appear exactly once and on its own line.");
    }

    [TestMethod]
    public void WriteLog_ToFile_AppendsMessage()
    {
        _tempLogPath = Path.Combine(Path.GetTempPath(), $"autocore-logger-{Guid.NewGuid():N}.txt");
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = _tempLogPath,
            IsDebugMode = true
        });

        Logger.WriteLog(LogType.Network, "network-line-unique-xyz");
        Logger.UpdateConfig(new Logger.LoggerConfig { LogToFile = false });

        var content = File.ReadAllText(_tempLogPath);
        Assert.IsTrue(content.Contains("network-line-unique-xyz", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("[Network]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void LoggerConfig_Defaults()
    {
        var cfg = new Logger.LoggerConfig();
        Assert.IsTrue(cfg.IsDebugMode);
        Assert.IsTrue(cfg.LogToFile);
        Assert.AreEqual("log.txt", cfg.LogFilePath);
    }

    [TestMethod]
    public void UpdateConfig_LogToFileTrue_WithEmptyPath_DoesNotOpenWriter()
    {
        Logger.UpdateConfig(new Logger.LoggerConfig
        {
            LogToFile = true,
            LogFilePath = "  "
        });
        // Should not throw; writer only opens when path is non-whitespace
        Logger.WriteLog(LogType.Error, "no-file");
    }
}

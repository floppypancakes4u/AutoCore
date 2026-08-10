namespace AutoCore.Utils;

public enum LogType
{
    Debug,
    AI,
    Network,
    Error,
    Test,
    Initialize,
    Command,
    File,
    Security,
    None,
    ExportData,
    Communicator,

    // Appended deliberately: existing members keep their numeric values.
    /// <summary>Recoverable abnormal condition; the operation continued or was retried.</summary>
    Warning,

    /// <summary>The process or a subsystem cannot safely continue.</summary>
    Fatal
}

/// <summary>
/// Central logging sink.
/// <para>
/// SS-06: this type is the last-resort diagnostic layer and is called from inside catch
/// blocks throughout the server. It must therefore <b>never throw</b> — a throw here would
/// escape the handler that was containing a failure and turn a contained error into a crash.
/// Every public entry point is total: bad input, an unwritable log file, a closed console
/// or a hostile <c>ToString()</c> all degrade rather than propagate.
/// </para>
/// <para>
/// All emission is serialised on <see cref="EmitLock"/> because the writer and the console
/// are process-wide statics touched by the tick thread and every socket task concurrently.
/// </para>
/// </summary>
public class Logger
{
    public static LoggerConfig Config { get; private set; } = new();

    /// <summary>Serialises file + console emission and all writer lifecycle changes.</summary>
    private static readonly object EmitLock = new();

    private static StreamWriter _logWriter;

    public static void UpdateConfig(LoggerConfig config)
    {
        if (config == null)
        {
            WriteLog(LogType.Warning, "Logger.UpdateConfig called with null config; keeping previous configuration.");
            return;
        }

        lock (EmitLock)
        {
            Config = config;

            if (Config.LogToFile && _logWriter == null && !string.IsNullOrWhiteSpace(Config.LogFilePath))
                OpenFileWriter(Config.LogFilePath);
            else if (!Config.LogToFile)
                CloseFileWriter();
        }

        ApplyStructuredConfig(config);
    }

    /// <summary>
    /// Applies the structured-pipeline part of the configuration: minimum level (with the
    /// PlaytestDiagnostics override) and the NDJSON file sink. Total: a bad path or level
    /// string degrades, it never aborts startup.
    /// </summary>
    private static void ApplyStructuredConfig(LoggerConfig config)
    {
        try
        {
            var level = Logging.StructuredLogLevel.Info;

            if (!string.IsNullOrWhiteSpace(config.StructuredMinimumLevel) &&
                !Enum.TryParse(config.StructuredMinimumLevel, ignoreCase: true, out level))
            {
                level = Logging.StructuredLogLevel.Info;
                WriteLog(LogType.Warning,
                    $"Unknown StructuredMinimumLevel '{config.StructuredMinimumLevel}'; using Info.");
            }

            // One switch for playtest nights: everything down to Debug.
            if (config.PlaytestDiagnostics && level > Logging.StructuredLogLevel.Debug)
                level = Logging.StructuredLogLevel.Debug;

            Logging.GameLog.MinimumLevel = level;

            Logging.GameLog.SetSink(string.IsNullOrWhiteSpace(config.StructuredLogPath)
                ? null
                : new Logging.NdjsonFileSink(config.StructuredLogPath));
        }
        catch (Exception ex)
        {
            EmergencyReport("applying structured log configuration", ex);
        }
    }

    /// <summary>
    /// Opens the log file. A failure here (missing directory, permission denied, path in use)
    /// must not abort startup — file logging is disabled and the reason is reported to console.
    /// Caller must hold <see cref="EmitLock"/>.
    /// </summary>
    private static void OpenFileWriter(string path)
    {
        try
        {
            var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };

            // Add a new line, if the file had content already
            if (writer.BaseStream.Position != 0)
                writer.WriteLine();

            _logWriter = writer;

            WriteLog(LogType.File, "Logging system startup!");
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or ArgumentException
                                      or NotSupportedException
                                      or System.Security.SecurityException)
        {
            _logWriter = null;
            WriteLog(LogType.Warning,
                $"Logger could not open log file '{path}'; continuing with console-only logging. {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Caller must hold <see cref="EmitLock"/>.</summary>
    private static void CloseFileWriter()
    {
        if (_logWriter == null)
            return;

        WriteLog(LogType.File, "Logging system shutdown!");

        try
        {
            _logWriter.Flush();
            _logWriter.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            EmergencyReport("closing log file", ex);
        }
        finally
        {
            _logWriter = null;
        }
    }

    public static void WriteLog(LogType type, object log)
    {
        string text;

        try
        {
            text = log?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            // A hostile or buggy ToString() must not take down the caller's catch block.
            text = $"<ToString() threw {ex.GetType().Name}>";
        }

        WriteLog(type, text);
    }

    public static void WriteLog(LogType type, string log)
    {
        try
        {
            // Debug lines always lead with map + character identity so multi-player
            // playtest greps stay attributable without opening the NDJSON side channel.
            if (type == LogType.Debug)
                log = PrefixDebugIdentity(log);

            // Dual-write: mirror the raw line into the structured pipeline (enriched with
            // ambient LogContext) so legacy call sites stay session-traceable. ExportData
            // is a raw data channel, not a log line, and is excluded.
            if (type != LogType.ExportData)
                Logging.GameLog.WriteLegacy(type, log);

            var (prefix, color) = Describe(type);

            var text = type == LogType.ExportData
                ? log ?? string.Empty
                : $"[{DateTime.Now:yyyy. MM. dd. HH:mm:ss.fff}] [{prefix}] {log}";

            Emit(type, text, color);
        }
        catch (Exception ex)
        {
            EmergencyReport("formatting log message", ex);
        }
    }

    /// <summary>
    /// Builds <c>map=Name(Id) char=Name(Id) {message}</c> from ambient <see cref="Logging.LogContext"/>.
    /// Missing keys become <c>?</c> so the column layout stays stable when context is absent
    /// (boot, timers, non-player work). Never throws.
    /// </summary>
    internal static string PrefixDebugIdentity(string message)
    {
        try
        {
            string mapName = null;
            string mapId = null;
            string characterName = null;
            string characterId = null;

            foreach (var pair in Logging.LogContext.CurrentProperties)
            {
                switch (pair.Key)
                {
                    case "MapName":
                        mapName ??= FormatContextValue(pair.Value);
                        break;
                    case "MapId":
                        mapId ??= FormatContextValue(pair.Value);
                        break;
                    case "CharacterName":
                        characterName ??= FormatContextValue(pair.Value);
                        break;
                    case "CharacterId":
                        characterId ??= FormatContextValue(pair.Value);
                        break;
                }
            }

            return $"map={mapName ?? "?"}({mapId ?? "?"}) char={characterName ?? "?"}({characterId ?? "?"}) {message}";
        }
        catch
        {
            // Identity decoration is diagnostics only — never block the original line.
            return $"map=?(?) char=?(?) {message}";
        }
    }

    private static string FormatContextValue(object value)
    {
        if (value == null)
            return null;

        try
        {
            var text = value.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteLog(LogType type, string format, params object[] args)
    {
        string text;

        try
        {
            text = string.Format(format, args);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException)
        {
            // Mismatched placeholders must not escalate into a crash; log the raw format instead.
            text = $"<malformed log format '{format}': {ex.GetType().Name}>";
        }

        WriteLog(type, text);
    }

    /// <summary>
    /// Logs an exception with full diagnostics: type, message, stack trace and the entire
    /// inner-exception chain, tagged with the operation that failed.
    /// <para>
    /// Prefer this over <c>WriteLog(..., ex.Message)</c> — a bare message discards the stack
    /// trace and the cause chain, which is what makes production failures undiagnosable.
    /// </para>
    /// </summary>
    /// <param name="type">Severity. Use <see cref="LogType.Warning"/> when the operation
    /// recovered, <see cref="LogType.Error"/> when it failed but the process is healthy,
    /// and <see cref="LogType.Fatal"/> when the subsystem cannot continue.</param>
    /// <param name="operation">What was being attempted, with identifiers but no secrets
    /// (e.g. <c>"SaveCharacter(coid=42)"</c>).</param>
    /// <param name="ex">The exception. Null is tolerated and reported as such.</param>
    public static void WriteException(LogType type, string operation, Exception ex)
    {
        var where = string.IsNullOrWhiteSpace(operation) ? "<unspecified operation>" : operation;

        if (ex == null)
        {
            WriteLog(type, $"{where} failed, but no exception was supplied.");
            return;
        }

        string detail;

        try
        {
            // ex.ToString() already includes type, message, stack trace and the full
            // inner-exception chain (and AggregateException's inner list).
            detail = ex.ToString();
        }
        catch (Exception formatFailure)
        {
            detail = $"<{ex.GetType().Name}.ToString() threw {formatFailure.GetType().Name}>";
        }

        WriteLog(type, $"{where} failed: {detail}");
    }

    private static (string Prefix, ConsoleColor Color) Describe(LogType type) => type switch
    {
        LogType.AI => ("AI", ConsoleColor.Yellow),
        LogType.Debug => ("Debug", ConsoleColor.Magenta),
        LogType.Network => ("Network", ConsoleColor.Green),
        LogType.Error => ("Error", ConsoleColor.Red),
        LogType.Warning => ("Warning", ConsoleColor.DarkYellow),
        LogType.Fatal => ("FATAL", ConsoleColor.Red),
        LogType.Test => ("Test", ConsoleColor.DarkGray),
        LogType.Initialize => ("Init", ConsoleColor.Blue),
        LogType.Command => ("Command", ConsoleColor.Cyan),
        LogType.None => ("", ConsoleColor.White),
        LogType.File => ("FileLog", ConsoleColor.Black), // Only logs to file, color doesn't matter
        LogType.Security => ("Security", ConsoleColor.DarkRed),
        LogType.Communicator => ("Communicator", ConsoleColor.DarkGreen),
        LogType.ExportData => ("Export", ConsoleColor.White),

        // SS-06: an unrecognised value degrades to a usable line. Throwing here previously
        // made LogType.ExportData unusable and could throw out of a catch block.
        _ => ($"Log({(int)type})", ConsoleColor.Gray)
    };

    private static void Emit(LogType type, string text, ConsoleColor color)
    {
        lock (EmitLock)
        {
            if (_logWriter != null)
            {
                try
                {
                    _logWriter.WriteLine(text);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    // Disk full, file deleted, handle closed under us. Disable file logging
                    // rather than throwing on every subsequent line, and say so once.
                    _logWriter = null;
                    EmergencyReport("writing to log file (file logging disabled)", ex);
                }
            }

            if (type == LogType.File)
                return;

            if (!Config.IsDebugMode && type == LogType.Debug)
                return;

            try
            {
                var previous = Console.ForegroundColor;

                Console.ForegroundColor = color;
                Console.WriteLine(text);
                Console.ForegroundColor = previous;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Redirected/closed stdout. Nothing left to report to; drop the line.
                EmergencyReport("writing to console", ex);
            }
        }
    }

    /// <summary>
    /// Last-resort reporting for failures of the logger itself. This is the one place in the
    /// codebase where an exception may be discarded: there is no remaining sink to report to,
    /// and rethrowing would defeat the entire purpose of SS-06.
    /// </summary>
    private static void EmergencyReport(string operation, Exception ex)
    {
        try
        {
            Console.Error.WriteLine($"[Logger] Failure while {operation}: {ex}");
        }
        catch
        {
            // Deliberately empty: stderr is also gone. There is nowhere left to write.
        }
    }

    public class LoggerConfig
    {
        public bool IsDebugMode { get; set; } = true;
        public string LogFilePath { get; set; } = "log.txt";
        public bool LogToFile { get; set; } = true;

        /// <summary>NDJSON structured-event file. Null/empty disables the structured pipeline.</summary>
        public string StructuredLogPath { get; set; }

        /// <summary>Minimum structured severity (Trace/Debug/Info/Warning/Error/Fatal). Default Info.</summary>
        public string StructuredMinimumLevel { get; set; }

        /// <summary>Playtest-night switch: forces the structured minimum level down to Debug.</summary>
        public bool PlaytestDiagnostics { get; set; }
    }
}

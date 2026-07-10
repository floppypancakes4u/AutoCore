namespace AutoCore.Game.Managers;

using AutoCore.Utils;

/// <summary>
/// Loud, greppable diagnostics when a partial handler exercises a path it does not fully implement.
/// Always logs as Error so messages show even when Debug is filtered.
/// Prefix: INCOMPLETE[HandlerName]
/// </summary>
public static class IncompleteHandlerLog
{
    public const string Prefix = "INCOMPLETE";

    /// <summary>Test hook: receives the full message body (no timestamp).</summary>
    internal static Action<string> TestSink { get; set; }

    /// <summary>
    /// Report a partial/stub implementation hit.
    /// </summary>
    /// <param name="handler">Short handler id, e.g. AutoPatrol, Reaction.Create, Mission.CompleteObjective</param>
    /// <param name="context">Runtime ids/state that identify the hit</param>
    /// <param name="gap">What the current code does not do</param>
    /// <param name="todo">Concrete work needed for a generic handler</param>
    public static void Warn(string handler, string context, string gap, string todo)
    {
        var message = $"{Prefix}[{handler}] {context} | gap: {gap} | TODO: {todo}";
        TestSink?.Invoke(message);
        Logger.WriteLog(LogType.Error, message);
    }
}

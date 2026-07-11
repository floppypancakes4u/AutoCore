namespace AutoCore.Sector.Network;

using AutoCore.Game.Diagnostics;
using AutoCore.Utils;
using AutoCore.Utils.Commands;

public partial class SectorServer
{
    private void RegisterCommands()
    {
        CommandProcessor.RegisterCommand("sector.exit", ProcessExitCommand);
        CommandProcessor.RegisterCommand("sector.wire", ProcessWireCommand);
        // Alias when TrimScope strips "sector." → "wire"
        CommandProcessor.RegisterCommand("wire", ProcessWireCommand);
        CommandProcessor.RegisterCommand("sector.tick", ProcessTickCommand);
        CommandProcessor.RegisterCommand("tick", ProcessTickCommand);
    }

    private void ProcessExitCommand(string[] parts)
    {
        var minutes = 0;

        if (parts.Length > 1)
            minutes = int.Parse(parts[1]);

        Timer.Add("exit", minutes * 60000, false, Shutdown);

        Logger.WriteLog(LogType.Command, $"Exiting the server in {minutes} minute(s).");
    }

    private static void ProcessWireCommand(string[] parts)
    {
        WireIsolationLevers.HandleConsoleCommand(parts);
    }

    /// <summary>
    /// Live sector main-loop period. Usage: <c>sector.tick</c> | <c>sector.tick 50</c> | <c>tick 10</c>.
    /// </summary>
    private static void ProcessTickCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            var current = SectorLoopControl.CurrentMilliseconds;
            Logger.WriteLog(LogType.Command,
                current.HasValue
                    ? $"Sector tick is {current.Value}ms. Usage: sector.tick <ms>  (e.g. sector.tick 50)"
                    : "Sector loop control not registered.");
            return;
        }

        if (!int.TryParse(parts[1], out var ms))
        {
            Logger.WriteLog(LogType.Command, "Usage: sector.tick <ms>  (integer 1-5000)");
            return;
        }

        if (!SectorLoopControl.TrySet(ms, out var message))
        {
            Logger.WriteLog(LogType.Command, message);
            return;
        }

        Logger.WriteLog(LogType.Command, message);
    }
}

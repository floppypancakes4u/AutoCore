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
}

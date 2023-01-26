using System.Diagnostics;

namespace AutoCore.Auth;

using AutoCore.Auth.Network;
using AutoCore.Utils;

public class Program : ExitableProgram
{
    private static AuthServer? Server { get; set; }

    public static void Main()
    {
        Initialize(ExitHandlerProc);

        Server = new AuthServer();
        if (!Server.Start())
        {
            Logger.WriteLog(LogType.Error, "Unable to start the server!");

            return;
        }

        Server.ProcessCommands();

        GC.Collect();

        Process.GetCurrentProcess().WaitForExit();
    }

    private static bool ExitHandlerProc(byte sig)
    {
        Logger.WriteLog(LogType.Error, "Shutting down the server...");

        Server?.Shutdown();

        Logger.WriteLog(LogType.Error, "Server shutdown completed!");

        Logger.WriteLog(LogType.Error, "Press any key to exit...");

        return false;
    }
}

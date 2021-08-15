using System;
using System.Diagnostics;

namespace AutoCore.Auth
{
    using Network;
    using Utils;

    public class Program : ExitableProgram
    {
        private static AuthServer Server { get; set; }

        public static void Main(string[] args)
        {
            Initialize(ExitHandlerProc);

            Console.WriteLine("Hello World!");

            Server = new AuthServer();
            Server.Initialize();

            if (!Server.Start())
            {
                Logger.WriteLog(LogType.Error, "Unable to start the server!");

                return;
            }

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
}

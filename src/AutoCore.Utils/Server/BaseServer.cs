namespace AutoCore.Utils.Server;

using AutoCore.Utils.Commands;

public abstract class BaseServer
{
    public abstract bool IsRunning { get; }
    public string Type { get; }

    public BaseServer(string type) => Type = type;

    public void ProcessCommands()
    {
        while (IsRunning)
        {
            CommandProcessor.ProcessCommand();

            Thread.Sleep(25);
        }
    }

    public void InitConsole()
    {
        Console.Title = $"AutoCore - {Type} Server";

        Logger.WriteLog(LogType.Initialize, @"                _         ______              ");
        Logger.WriteLog(LogType.Initialize, @"     /\        | |       / ____|              ");
        Logger.WriteLog(LogType.Initialize, @"    /  \  _   _| |_ ___ | |     ___  _ __ ___ ");
        Logger.WriteLog(LogType.Initialize, @"   / /\ \| | | | __/ _ \| |    / _ \| '__/ _ \");
        Logger.WriteLog(LogType.Initialize, @"  / ____ \ |_| | || (_) | |___| (_) | | |  __/");
        Logger.WriteLog(LogType.Initialize, @" /_/    \_\__,_|\__\___/ \_____\___/|_|  \___|");
        Logger.WriteLog(LogType.Initialize, @" Auto Assault Server - {0}", Type);
        Logger.WriteLog(LogType.Initialize, "");
    }
}

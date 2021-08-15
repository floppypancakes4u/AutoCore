using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCore.Utils.Server
{
    using Commands;

    public abstract class BaseServer
    {
        public bool IsRunning { get; protected set; }
        public abstract string ServerType { get; }

        private async Task ProcessCommandsSafe(CancellationToken stoppingToken)
        {
            try
            {
                await ProcessCommands(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ProcessCommands(CancellationToken stoppingToken)
        {
            while (IsRunning && !stoppingToken.IsCancellationRequested)
            {
                CommandProcessor.ProcessCommand(stoppingToken);

                await Task.Delay(25, stoppingToken);
            }
        }

        private void InitConsole(string type)
        {
            Console.Title = $"AutoCore - {type} Server";

            Logger.WriteLog(LogType.Initialize, @"                _         ______              ");
            Logger.WriteLog(LogType.Initialize, @"     /\        | |       / ____|              ");
            Logger.WriteLog(LogType.Initialize, @"    /  \  _   _| |_ ___ | |     ___  _ __ ___ ");
            Logger.WriteLog(LogType.Initialize, @"   / /\ \| | | | __/ _ \| |    / _ \| '__/ _ \");
            Logger.WriteLog(LogType.Initialize, @"  / ____ \ |_| | || (_) | |___| (_) | | |  __/");
            Logger.WriteLog(LogType.Initialize, @" /_/    \_\__,_|\__\___/ \_____\___/|_|  \___|");
            Logger.WriteLog(LogType.Initialize, @" Auto Assault Server - {0}", type);
            Logger.WriteLog(LogType.Initialize, "");
        }
    }
}

namespace AutoCore.Auth.Network;

using AutoCore.Database.Auth;
using AutoCore.Database.Auth.Models;
using AutoCore.Utils.Commands;
using AutoCore.Utils;

public partial class AuthServer
{
    private void RegisterConsoleCommands()
    {
        CommandProcessor.RegisterCommand("auth.create", ProcessCreateCommand);
        CommandProcessor.RegisterCommand("auth.exit", ProcessExitCommand);
    }

    private void ProcessExitCommand(string[] parts)
    {
        var minutes = 0;

        if (parts.Length > 1)
            minutes = int.Parse(parts[1]);

        Timer.Add("exit", minutes * 60000, false, Shutdown);

        Logger.WriteLog(LogType.Command, $"Exiting the server in {minutes} minute(s).");
    }

    private void ProcessCreateCommand(string[] parts)
    {
        if (parts.Length < 4)
        {
            Logger.WriteLog(LogType.Command, "Invalid create account command! Usage: create <email> <username> <password>");
            return;
        }

        var email = parts[1];
        var userName = parts[2];
        var password = parts[3];

        try
        {
            using var context = new AuthContext();
            
            var salt = Account.CreateSalt();

            context.Accounts.Add(new Account
            {
                Email = email,
                Username = userName,
                Password = Account.Hash(password ?? string.Empty, salt),
                Salt = salt,
                JoinDate = DateTime.Now
            });
            context.SaveChanges();
            

            Logger.WriteLog(LogType.Command, $"Created account: {parts[2]}! (Password: {parts[3]})");
        }
        catch
        {
            Logger.WriteLog(LogType.Error, "Username or email is already taken!");
        }
    }

    /*private void ProcessRestartCommand(string[] parts)
    {
        // TODO: delayed restart, with contacting globals, so they can warn players not to leave the server, or they won't be able to reconnect
    }

    private void ProcessShutdownCommand(string[] parts)
    {
        // TODO: delayed shutdown, with contacting globals, so they can warn players not to leave the server, or they won't be able to reconnect
        // TODO: add timer to report the remaining time until shutdown?
        // TODO: add timer to contact global servers to tell them periodically that we're getting shut down?
    }*/
}

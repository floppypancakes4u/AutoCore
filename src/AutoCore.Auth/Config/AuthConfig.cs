namespace AutoCore.Auth.Config;

using AutoCore.Utils;

public class AuthConfig
{
    public Logger.LoggerConfig LoggerConfig { get; set; } = new();
    public string AuthDatabaseConnectionString { get; set; } = string.Empty;
    public int AuthSocketPort { get; set; }
    public int CommunicatorPort { get; set; }
}

namespace AutoCore.Global.Config;

using AutoCore.Utils;
using AutoCore.Utils.Config;

public class Config
{
    public SocketAsyncConfig SocketAsyncConfig { get; set; }
    public GameConfig GameConfig { get; set; }
    public string CommunicatorAddress { get; set; }
    public int CommunicatorPort { get; set; }
    public string CharDatabaseConnectionString { get; set; }
    public string WorldDatabaseConnectionString { get; set; }
    public string GamePath { get; set; }
    public Logger.LoggerConfig LoggerConfig { get; set; }
    public ServerInfoConfig ServerInfoConfig { get; set; }
}

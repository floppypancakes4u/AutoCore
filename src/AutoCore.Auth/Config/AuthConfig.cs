namespace AutoCore.Auth.Config;

public class AuthConfig
{
    public string ListenAddress { get; set; }
    public int Port { get; set; }
    public int Backlog { get; set; }
    public int ClientTimeout { get; set; }
    public static byte MaxServerCount { get; private set; }
}

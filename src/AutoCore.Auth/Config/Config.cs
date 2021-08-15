using System.Collections.Generic;

namespace AutoCore.Auth.Config
{
    using Data;
    using Utils;
    using Utils.Config;

    public class Config
    {
        public AuthListType AuthListType { get; set; }
        public Dictionary<string, string> Servers { get; set; }
        public SocketAsyncConfig SocketAsyncConfig { get; set; }
        public AuthConfig AuthConfig { get; set; }
        public CommunicatorConfig CommunicatorConfig { get; set; }
        public Logger.LoggerConfig LoggerConfig { get; set; }
    }
}

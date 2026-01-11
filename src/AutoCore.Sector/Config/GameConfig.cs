namespace AutoCore.Sector.Config;

public class GameConfig
{
    public string PublicAddress { get; set; }
    public int Port { get; set; }
    /// <summary>
    /// If true, allows clients with different TNL versions to connect (for testing)
    /// </summary>
    public bool AllowVersionMismatch { get; set; } = false;
    /// <summary>
    /// Expected TNL version. Default is 175. Set to 0 to use default, or set to client version (e.g., 161) to allow that version
    /// </summary>
    public int ExpectedVersion { get; set; } = 0;
}

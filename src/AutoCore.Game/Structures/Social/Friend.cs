namespace AutoCore.Game.Structures.Social;

public class Friend
{
    public long CoidCharacter { get; set; }
    public long CoidFriendCharacter { get; set; }
    public int Level { get; set; }
    public int LastContinentId { get; set; }
    public byte Class { get; set; }
    public bool Online { get; set; }
    public string Name { get; set; }
}

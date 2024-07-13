namespace AutoCore.Game.Structures.Social;

public class Enemy
{
    public long CoidCharacter { get; set; }
    public long CoidEnemyCharacter { get; set; }
    public int Level { get; set; }
    public int LastContinentId { get; set; }
    public int TimesKilled { get; set; }
    public int TimesKilledBy { get; set; }
    public byte Race { get; set; }
    public byte Class { get; set; }
    public bool Online { get; set; }
    public string Name { get; set; }
}

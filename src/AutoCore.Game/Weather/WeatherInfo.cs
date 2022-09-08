namespace AutoCore.Game.Weather;

public class WeatherInfo
{
    public byte EventTimesPerMinute { get; set; }
    public string FxName { get; set; }
    public uint LayerBits { get; set; }
    public uint MaxTimeToLive { get; set; }
    public uint MinTimeToLive { get; set; }
    public float PercentChance { get; set; }
    public int SpecialEventSkill { get; set; }
    public uint SpecialType { get; set; }
    public uint Type { get; set; }
}

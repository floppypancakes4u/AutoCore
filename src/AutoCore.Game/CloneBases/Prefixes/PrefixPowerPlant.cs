namespace AutoCore.Game.CloneBases.Prefixes;

public class PrefixPowerPlant : PrefixBase
{
    public float CoolDownPercent { get; set; }
    public int CoolingRateAdjust { get; set; }
    public float CoolingRatePercent { get; set; }
    public int HeadAdjust { get; set; }
    public float HeatPercent { get; set; }
    public int PowerAdjust { get; set; }
    public float PowerPercent { get; set; }
    public int PowerRegenRateAdjust { get; set; }
    public float PowerRegenRatePercent { get; set; }

    public PrefixPowerPlant(BinaryReader reader)
        : base(reader)
    {
        HeatPercent = reader.ReadSingle();
        HeadAdjust = reader.ReadInt32();
        PowerPercent = reader.ReadSingle();
        PowerAdjust = reader.ReadInt32();
        CoolingRatePercent = reader.ReadSingle();
        CoolingRateAdjust = reader.ReadInt32();
        PowerRegenRatePercent = reader.ReadSingle();
        PowerRegenRateAdjust = reader.ReadInt32();
        CoolDownPercent = reader.ReadSingle();
    }
}

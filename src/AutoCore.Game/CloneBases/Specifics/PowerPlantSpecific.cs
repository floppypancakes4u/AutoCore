namespace AutoCore.Game.CloneBases.Specifics;

public class PowerPlantSpecific
{
    public short CoolRate;
    public int HeatMaximum;
    public int PowerMaximum;
    public short PowerRegenRate;

    public static PowerPlantSpecific ReadNew(BinaryReader br)
    {
        return new PowerPlantSpecific
        {
            HeatMaximum = br.ReadInt32(),
            PowerMaximum = br.ReadInt32(),
            PowerRegenRate = br.ReadInt16(),
            CoolRate = br.ReadInt16()
        };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(HeatMaximum);
        writer.Write(PowerMaximum);
        writer.Write(PowerRegenRate);
        writer.Write(CoolRate);
    }
}

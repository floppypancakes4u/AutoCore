namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBasePowerPlant : CloneBaseObject
{
    public PowerPlantSpecific PowerPlantSpecific;

    public CloneBasePowerPlant(BinaryReader reader)
        : base(reader)
    {
        PowerPlantSpecific = PowerPlantSpecific.ReadNew(reader);
    }
}

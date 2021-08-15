using System.IO;

namespace AutoCore.Game.Clonebase
{
    using Structures.Specifics;

    public class CloneBasePowerPlant : CloneBaseObject
    {
        public PowerPlantSpecific PowerPlantSpecific;

        public CloneBasePowerPlant(BinaryReader br)
            : base(br)
        {
            PowerPlantSpecific = PowerPlantSpecific.Read(br);
        }
    }
}

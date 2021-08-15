using System.IO;

namespace AutoCore.Game.Clonebase
{
    using Structures.Specifics;

    public class CloneBaseArmor : CloneBaseObject
    {
        public ArmorSpecific ArmorSpecific;

        public CloneBaseArmor(BinaryReader br)
            : base(br)
        {
            ArmorSpecific = ArmorSpecific.Read(br);
        }
    }
}

using System.IO;

namespace AutoCore.Game.Clonebase
{
    using Structures.Specifics;

    public class CloneBaseCommodity : CloneBaseObject
    {
        public CommoditySpecific CommoditySpecific;

        public CloneBaseCommodity(BinaryReader br)
            : base(br)
        {
            CommoditySpecific = CommoditySpecific.Read(br);
        }
    }
}

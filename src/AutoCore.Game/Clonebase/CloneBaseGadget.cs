using System.IO;

namespace AutoCore.Game.Clonebase
{
    using Structures.Specifics;

    public class CloneBaseGadget : CloneBaseObject
    {
        public GadgetSpecific GadgetSpecific;

        public CloneBaseGadget(BinaryReader br)
            : base(br)
        {
            GadgetSpecific = GadgetSpecific.Read(br);
        }
    }
}

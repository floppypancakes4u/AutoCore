using System.IO;

namespace AutoCore.Game.Clonebase
{
    using Structures.Specifics;

    public class CloneBaseWheelSet : CloneBaseObject
    {
        public WheelSetSpecific WheelSetSpecific;

        public CloneBaseWheelSet(BinaryReader br)
            : base(br)
        {
            WheelSetSpecific = WheelSetSpecific.Read(br);
        }
    }
}

using System.IO;

namespace AutoCore.Game.Entities.Base
{
    public abstract partial class ClonedObjectBase
    {
        public abstract void Unserialize(BinaryReader br, uint mapVersion);

        //public abstract void WriteToCreatePacket(Packet packet, bool extended = false);
    }
}

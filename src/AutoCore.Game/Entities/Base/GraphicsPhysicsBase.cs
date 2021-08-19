using System;
using System.IO;

namespace AutoCore.Game.Entities.Base
{
    public class GraphicsPhysicsBase : ClonedObjectBase
    {
        public GraphicsBase CVOGGraphicsBase = new();
        public PhysicsBase CVOGPhysicsBase = new();

        public override void Unserialize(BinaryReader br, uint mapVersion)
        {
            CVOGGraphicsBase.UnserializeCreateEffect(br, mapVersion);
            CVOGGraphicsBase.UnserializeTooltip(br, mapVersion);
            //ReadTriggerEvents(br, mapVersion);
            CVOGPhysicsBase.UnSerialize(br, mapVersion);
        }

        /*public override void WriteToCreatePacket(Packet packet, bool extended = false)
        {
            throw new NotSupportedException();
        }*/
    }
}

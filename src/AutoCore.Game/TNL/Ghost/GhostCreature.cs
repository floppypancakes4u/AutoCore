extern alias TNLNET;

using TNLNET::TNL.Entities;
using TNLNET::TNL.Types;
using TNLNET::TNL.Utils;

namespace AutoCore.Game.TNL.Ghost
{
    public class GhostCreature : GhostObject
    {
        private static NetClassRepInstance<GhostCreature> _dynClassRep;

        public new static void RegisterNetClassReps()
        {
            ImplementNetObject(out _dynClassRep);
        }

        public override NetClassRep GetClassRep()
        {
            return _dynClassRep;
        }

        public GhostCreature()
        {
            UpdatePriorityScalar = 1.0f;
        }

        public override void CreatePacket()
        {

        }

        public override void RecreateForExisting()
        {

        }

        public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
        {
            return 0UL;
        }

        public override void UnpackUpdate(GhostConnection connection, BitStream stream)
        {

        }
    }
}

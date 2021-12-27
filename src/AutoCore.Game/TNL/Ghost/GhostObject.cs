using System;

using TNL.Entities;
using TNL.Structures;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost
{
    using Structures;

    public enum GhostType
    {
        Object    = 0,
        Creature  = 1,
        Vehicle   = 2,
        Character = 3
    }

    public class GhostObject : NetObject
    {
        private static NetClassRepInstance<GhostObject> _dynClassRep;

        //protected ClonedObjectBase Parent;
        protected bool WaitingForParent;
        protected float UpdatePriorityScalar;
        protected object MsgCreate;
        protected object MsgCreateOwner;

        public TFID Guid { get; private set; }

        public override NetClassRep GetClassRep()
        {
            return _dynClassRep;
        }

        public static void RegisterNetClassReps()
        {
            ImplementNetObject(out _dynClassRep);
        }

        public GhostObject()
        {
            Guid = new TFID();
            WaitingForParent = true;
            UpdatePriorityScalar = 0.1f;
            NetFlags = new BitSet();
            NetFlags.Set((uint)NetFlag.Ghostable);
        }

        public bool IsGhostVIsibleToMe(NetObject ghost)
        {
            return OwningConnection != null && OwningConnection.GetGhostIndex(ghost) != -1;
        }

        /*public void SetParent(ClonedObjectBase parent)
        {
            WaitingForParent = false;
            Parent = parent;
        }*/

        public override bool OnGhostAdd(GhostConnection theConnection)
        {
            /*if (Parent != null)
                Parent.SetGhost(this);*/

            return true;
        }

        public TNLConnection GetOwningConnection()
        {
            return OwningConnection as TNLConnection;
        }

        public void CleanupCreate()
        {
            MsgCreate = null;
            MsgCreateOwner = null;
        }

        public override void OnGhostRemove()
        {
            /*if (Parent != null)
                Parent.ClearGhost(false);*/
        }

        public virtual void CreatePacket()
        {
            MsgCreate = new object();
        }

        public virtual void RecreateForExisting()
        {
            //if (MsgCreate != null && Parent != null)
            {

            }
        }

        public override float GetUpdatePriority(NetObject scopeObject, ulong updateMask, int updateSkips)
        {
            /*if (Parent == null || !(scopeObject is GhostObject) || (scopeObject as GhostObject).Parent == null)
                return updateSkips * 0.02f;

            var otherParent = (scopeObject as GhostObject).Parent;

            if (otherParent.GetTargetObject() != Parent && otherParent != Parent && otherParent != Parent.Owner && (Parent.GetAsCreature() == null ||
                    (Parent.GetAsCreature().GetSummonOwner() != otherParent.GetTFID())))
            {
                var otherAvPos = otherParent.GetAvatarPosition();
                var thisAvPos = Parent.GetAvatarPosition();

                var val = (float)Math.Sqrt((otherAvPos.X - thisAvPos.X) * (otherAvPos.X - thisAvPos.X) + (otherAvPos.Y - thisAvPos.Y) * (otherAvPos.Y - thisAvPos.Y));
                return UpdatePriorityScalar *
                        (((1.0F -
                            (val / ((otherParent.GetMap().GetNumberOfTerrainGridsPerObjectGrid() * 100.0F) * 1.2F))) *
                            0.5F) + (updateSkips * 0.001F));
            }*/

            return 1.0f;
        }

        public void PackCommon(BitStream stream)
        {
            var faction = 0;
            var teamFaction = 0;

            stream.WriteBits(64, BitConverter.GetBytes(Guid.Coid));
            stream.WriteFlag(Guid.Global);
            stream.WriteInt(0, 20); // clone base id of parent
            stream.WriteInt(0, 18); // max hp
            stream.WriteBits(16, BitConverter.GetBytes(faction));

            if (faction == teamFaction)
            {
                faction = 0;
            }

            stream.WriteBits(16, BitConverter.GetBytes(faction));
        }

        public void UnpackCommon(BitStream stream, object msgCreate)
        {
            var arr = new byte[8];

            stream.ReadBits(64, arr);
            stream.Read(out bool global);

            Guid.Coid = BitConverter.ToInt64(arr, 0);
            Guid.Global = global;

            var cbid = stream.ReadInt(20);
            var maxHp = stream.ReadInt(18);

            stream.ReadBits(16, arr);

            var faction = BitConverter.ToInt16(arr, 0);

            stream.ReadBits(16, arr);

            var teamFaction = BitConverter.ToInt16(arr, 0);
        }

        public void PackSingleSkill(BitStream stream, object skill, int size, int skillTargetType)
        {

        }

        public void UnpackSingleSkill(BitStream stream, object skill, int size)
        {

        }

        public override void UnpackUpdate(GhostConnection connection, BitStream stream)
        {

        }

        public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
        {
            return 0UL;
        }

        public int GetCreatePacketSize(int cbidObject)
        {
            return 0;
        }

        public void UnpackSkills(BitStream stream, bool isOwner)
        {

        }

        public object MallocCreatePacket(int cbidObject, out int size)
        {
            size = 0;
            return null;
        }

        public void PackSkills(BitStream stream, object pBase)
        {

        }

        public static GhostObject CreateObject(TFID id, GhostType type)
        {
            var obj = type switch
            {
                GhostType.Creature => new GhostCreature(),
                GhostType.Vehicle => new GhostVehicle(),
                GhostType.Character => new GhostCharacter(),
                GhostType.Object => new GhostObject(),
                _ => throw new ArgumentException("Could not create GhostObject for not existing type!", "type"),
            };

            obj.Guid = id;

            return obj;
        }
    }
}

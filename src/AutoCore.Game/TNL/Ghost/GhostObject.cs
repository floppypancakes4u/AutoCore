using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Game.Packets.Sector;

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

    protected ClonedObjectBase? Parent { get; set; }
    protected bool WaitingForParent { get; set; }
    protected float UpdatePriorityScalar { get; set; }
    protected object MsgCreate { get; set; }
    protected object MsgCreateOwner { get; set; }

    public TFID Guid { get; private set; } = new();

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
        WaitingForParent = true;
        UpdatePriorityScalar = 0.1f;
        NetFlags = new();
        NetFlags.Set((uint)NetFlag.Ghostable);
    }

    public bool IsGhostVIsibleToMe(NetObject ghost)
    {
        return OwningConnection != null && OwningConnection.GetGhostIndex(ghost) != -1;
    }

    public virtual void SetParent(ClonedObjectBase parent)
    {
        WaitingForParent = false;
        Parent = parent;
    }

    public override bool OnGhostAdd(GhostConnection theConnection)
    {
        Parent?.SetGhost(this);

        return true;
    }

    public override void OnGhostRemove()
    {
        Parent?.ClearGhost();
    }

    public TNLConnection GetOwningConnection()
    {
        return OwningConnection as TNLConnection;
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
        stream.WriteBits(64, BitConverter.GetBytes(Parent.ObjectId.Coid));
        stream.WriteFlag(Parent.ObjectId.Global);
        stream.WriteInt((uint)Parent.CBID, 20);
        stream.WriteInt((uint)Math.Max(Parent.GetMaximumHP(), 0), 18);

        var faction = Parent.GetIDFaction();
        var bareTeamFaction = Parent.GetBareTeamFaction();
        stream.WriteBits(16, BitConverter.GetBytes(faction));

        if (faction == bareTeamFaction)
            bareTeamFaction = 0;

        stream.WriteBits(16, BitConverter.GetBytes(bareTeamFaction));
    }

    public void UnpackCommon(BitStream stream, object msgCreate)
    {
        throw new NotSupportedException();
        /*var arr = new byte[8];

        stream.ReadBits(64, arr);
        stream.Read(out bool global);

        Guid.Coid = BitConverter.ToInt64(arr, 0);
        Guid.Global = global;

        var cbid = stream.ReadInt(20);
        var maxHp = stream.ReadInt(18);

        stream.ReadBits(16, arr);

        var faction = BitConverter.ToInt16(arr, 0);

        stream.ReadBits(16, arr);

        var teamFaction = BitConverter.ToInt16(arr, 0);*/
    }

    public void PackSingleSkill(BitStream stream, CreateSkillHeartbeat skill, int size, int skillTargetType)
    {
        stream.WriteInt(skill.SkillId, 14);
        stream.WriteInt((uint)skill.SkillLevel, 8);
        stream.WriteInt(skill.SkillType, 8);

        if (stream.WriteFlag((skillTargetType & 0x100) == 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(skill.LastTickCount));
            stream.WriteBits(32, BitConverter.GetBytes(skill.DiceSeed));

            if (stream.WriteFlag(Parent.ObjectId != skill.Target))
            {
                stream.WriteBits(64, BitConverter.GetBytes(skill.Target.Coid));
                stream.WriteFlag(skill.Target.Global);
            }

            stream.WriteFlag(skill.ForceDeath);
            stream.WriteInt((uint)skill.DurationCountdown, 10);

            if (stream.WriteFlag(Parent.ObjectId != skill.Caster))
            {
                stream.WriteBits(64, BitConverter.GetBytes(skill.Caster.Coid));
                stream.WriteFlag(skill.Caster.Global);
            }
        }

        //TODO: write the remaining data (subclasses of CreateSkillHeartbeat) as is to the stream
    }

    public void UnpackSingleSkill(BitStream stream, object skill, int size)
    {
        throw new NotSupportedException();
    }

    public override void UnpackUpdate(GhostConnection connection, BitStream stream)
    {
        throw new NotSupportedException();
        /*var temp = new byte[8];

        if (PIsInitialUpdate)
        {
            Guid.Global = stream.ReadFlag();

            if (Guid.Global)
            {
                stream.ReadBits(0x40, temp);

                Guid.Coid = BitConverter.ToInt64(temp);
            }
            else
            {
                // Create packet?
                Guid.Coid = stream.ReadInt(20);
            }
        }*/
    }

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception($"PackUpdate for GhostObject without parent! TFID: ({Guid.Global}, {Guid.Coid})");

        if (PIsInitialUpdate)
        {
            stream.WriteFlag(Guid.Global);

            if (Guid.Global)
                stream.Write(Guid.Coid);
            else
                stream.WriteInt((uint)(Guid.Coid & 0xFFFFFFFF), 20);
        }

        if (stream.WriteFlag((updateMask & 8) != 0))
        {
            stream.WriteInt((uint)Parent.GetCurrentHP(), 18);

            if (stream.WriteFlag(Parent.IsCorpse))
            {
                if (stream.WriteFlag(true)) // this is only sent if the death was recent? idk, strange stuff
                {
                    stream.WriteInt((uint)Parent.DeathType, 3);
                    stream.Write(Parent.Murderer.Coid);
                    stream.WriteFlag(Parent.Murderer.Global);
                }
            }
        }

        if (stream.WriteFlag((updateMask & 2) != 0))
        {
            stream.Write(Parent.Position.X);
            stream.Write(Parent.Position.Y);
            stream.Write(Parent.Position.Z);

            stream.Write(Parent.Rotation.X);
            stream.Write(Parent.Rotation.Y);
            stream.Write(Parent.Rotation.Z);
            stream.Write(Parent.Rotation.W);

            var linearVelocityX = 0.0f;
            var linearVelocityY = 0.0f;
            var linearVelocityZ = 0.0f;
            var linearVelocityValue = (linearVelocityX * linearVelocityX) + (linearVelocityY * linearVelocityY) + (linearVelocityZ * linearVelocityZ);

            if (stream.WriteFlag(linearVelocityValue > 0.00000011920929f))
            {
                stream.Write(linearVelocityX);
                stream.Write(linearVelocityY);
                stream.Write(linearVelocityZ);
            }

            var angularVelocityX = 0.0f;
            var angularVelocityY = 0.0f;
            var angularVelocityZ = 0.0f;
            var angularVelocityValue = (angularVelocityX * angularVelocityX) + (angularVelocityY * angularVelocityY) + (angularVelocityZ * angularVelocityZ);

            if (stream.WriteFlag(angularVelocityValue > 0.00000011920929f))
            {
                stream.Write(angularVelocityX);
                stream.Write(angularVelocityY);
                stream.Write(angularVelocityZ);
            }
        }

        return 0UL;
    }

    public int GetCreatePacketSize(int cbidObject)
    {
        return 0;
    }

    public void UnpackSkills(BitStream stream, bool isOwner)
    {
        throw new NotSupportedException();
    }

    public object MallocCreatePacket(int cbidObject, out int size)
    {
        size = 0;
        return null;
    }

    public void PackSkills(BitStream stream, ClonedObjectBase pBase)
    {
        stream.WriteBits(8, BitConverter.GetBytes(0)); // skill count

        for (var i = 0; i < 0; ++i)
        {
            // TODO
            stream.WriteBits(16, BitConverter.GetBytes(0)); // size

            PackSingleSkill(stream, null, 0, 0);
        }
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

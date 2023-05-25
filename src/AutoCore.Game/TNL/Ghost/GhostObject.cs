using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Entities;
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

    public const ulong InitialMask   = 0x001ul;
    public const ulong PositionMask  = 0x002ul;
    public const ulong TargetMask    = 0x004ul;
    public const ulong HealthMask    = 0x008ul;
    public const ulong StatusMask    = 0x010ul;
    public const ulong MurdererMask  = 0x020ul;
    public const ulong HealthMaxMask = 0x040ul;
    public const ulong SkillsMask    = 0x080ul;
    public const ulong TokenMask     = 0x100ul;

    protected ClonedObjectBase? Parent { get; set; }
    protected float UpdatePriorityScalar { get; set; }
    protected object MsgCreate { get; set; }
    protected object MsgCreateOwner { get; set; }

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

    public override void UnpackUpdate(GhostConnection connection, BitStream stream)
    {
        throw new NotSupportedException();
    }

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception("PackUpdate for GhostObject without parent!");

        if (PIsInitialUpdate)
        {
            stream.WriteFlag(Parent.ObjectId.Global); // local -> auto create packet in the client; global -> external create packets are needed!

            if (Parent.ObjectId.Global)
                stream.Write(Parent.ObjectId.Coid);
            else
                stream.WriteInt((uint)(Parent.ObjectId.Coid & 0xFFFFFFFF), 20);
        }

        if (stream.WriteFlag((updateMask & HealthMask) != 0))
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

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
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

    public void PackSkills(BitStream stream, ClonedObjectBase pBase)
    {
        stream.WriteBits(8, BitConverter.GetBytes(0)); // skill count

        for (var i = 0; i < 0; ++i)
        {
            // TODO
            stream.WriteBits(16, BitConverter.GetBytes(56)); // size - 56 is the minimum size

            PackSingleSkill(stream, null, 0, 0);
        }
    }
}

using AutoCore.Game.Entities;
using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

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
        if (Parent == null)
            throw new Exception("Missing parent for GhostCreature!");

        var creature = Parent.GetAsCreature();

        if (PIsInitialUpdate)
        {
            PackCommon(stream);

            if (stream.WriteFlag(false)) // TODO
                stream.WriteInt(0, 20); // TODO

            if (stream.WriteFlag(false)) // TODO
                stream.WriteInt(0, 20); // TODO

            if (stream.WriteFlag(false)) // TODO
                stream.WriteInt(0, 20); // TODO

            if (stream.WriteFlag(false)) // TODO unk tfid
            {
                stream.WriteInt(0, 32); // tfid coid 64 bits
                stream.WriteInt(0, 32);

                stream.WriteFlag(false); // tfid global
            }

            if (stream.WriteFlag(false)) // TODO
            {
                stream.WriteInt(0, 32); // TODO unk 64bit value
                stream.WriteInt(0, 32);
            }

            stream.WriteFlag(false); // TODO
            stream.WriteBits(8, BitConverter.GetBytes(0));
            stream.WriteFlag(false); // TODO

            PackSkills(stream, creature);
        }

        if (stream.WriteFlag((updateMask & 0x20) != 0))
        {
            stream.WriteInt(0, 32); // TODO unk 64bit value
            stream.WriteInt(0, 32);
        }

        if (stream.WriteFlag((updateMask & 8) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetCurrentHP(), 0), 18);
            stream.WriteFlag(Parent.GetIsCorpse());
        }

        if (stream.WriteFlag((updateMask & 0x40) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetMaximumHP(), 0), 18);
        }

        if (stream.WriteFlag((updateMask & 0x80000000) != 0))
        {
            stream.WriteBits(8, BitConverter.GetBytes(0));
        }

        if (stream.WriteFlag((updateMask & 2) != 0))
        {
            stream.Write(creature.Position.X);
            stream.Write(creature.Position.Y);
            stream.Write(creature.Position.Z);

            stream.Write(creature.Rotation.X);
            stream.Write(creature.Rotation.Y);
            stream.Write(creature.Rotation.Z);
            stream.Write(creature.Rotation.W);

            var linearVelocityX = 0.0f;
            var linearVelocityY = 0.0f;
            var linearVelocityZ = 0.0f;

            stream.Write(linearVelocityX);
            stream.Write(linearVelocityY);
            stream.Write(linearVelocityZ);
            
            var moveToTargetX = 0.0f;
            var moveToTargetY = 0.0f;
            var moveToTargetZ = 0.0f;

            stream.Write(moveToTargetX);
            stream.Write(moveToTargetY);
            stream.Write(moveToTargetZ);
        }

        if (stream.WriteFlag((updateMask & 4) != 0))
        {
            if (Parent.Target == null)
            {
                stream.Write(Parent.Target.ObjectId.Coid);
                stream.WriteFlag(Parent.Target.ObjectId.Global);
            }
            else
            {
                stream.Write((long)0);
                stream.WriteFlag(false);
            }
        }

        return 0UL;
    }

    public override void UnpackUpdate(GhostConnection connection, BitStream stream)
    {

    }
}

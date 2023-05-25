using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

public class GhostCreature : GhostObject
{
    private static NetClassRepInstance<GhostCreature> _dynClassRep;

    public const ulong StateMask = 0x80000000ul;

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

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception("Missing parent for GhostCreature!");

        var creature = Parent.GetAsCreature();

        if (PIsInitialUpdate)
        {
            PackCommon(stream);

            if (stream.WriteFlag(creature.EnhancementId != -1)) // EnhancementId != -1
                stream.WriteInt((uint)creature.EnhancementId, 20); // EnhancementId

            if (stream.WriteFlag(false)) // CoidOnUseTrigger != -1
                stream.WriteInt(0, 20); // CoidOnUseTrigger

            if (stream.WriteFlag(false)) // CoidOnUseReaction != -1
                stream.WriteInt(0, 20); // CoidOnUseReaction

            if (stream.WriteFlag(false)) // CreatureSummoner TFID != (-1, false)
            {
                stream.WriteInt(0, 32); // CreatureSummoner TFID Coid
                stream.WriteInt(0, 32);

                stream.WriteFlag(false); // CreatureSummoner TFID Global
            }

            if (stream.WriteFlag(false)) // CoidSpawnOwner != -1
            {
                stream.WriteInt(0, 32); // CoidSpawnOwner
                stream.WriteInt(0, 32);
            }

            stream.WriteFlag(false); // DoesntCountAsSummon
            stream.WriteBits(8, BitConverter.GetBytes(0)); // Level
            stream.WriteFlag(false); // IsElite

            PackSkills(stream, creature);
        }

        if (stream.WriteFlag((updateMask & MurdererMask) != 0))
        {
            stream.WriteInt(0, 32); // CoidMurderer
            stream.WriteInt(0, 32);
        }

        if (stream.WriteFlag((updateMask & HealthMask) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetCurrentHP(), 0), 18);
            stream.WriteFlag(Parent.GetIsCorpse());
        }

        if (stream.WriteFlag((updateMask & HealthMaxMask) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetMaximumHP(), 0), 18);
        }

        if (stream.WriteFlag((updateMask & StateMask) != 0))
        {
            stream.WriteBits(8, BitConverter.GetBytes(0)); // AI State
        }

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
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

        if (stream.WriteFlag((updateMask & TargetMask) != 0))
        {
            if (Parent.Target != null)
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
}

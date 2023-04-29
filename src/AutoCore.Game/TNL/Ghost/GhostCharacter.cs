using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Entities;

public class GhostCharacter : GhostObject
{
    private static NetClassRepInstance<GhostCharacter> _dynClassRep;

    public const ulong ClanMask    = 0x20000000ul;
    public const ulong PetCBIDMask = 0x40000000ul;
    public const ulong GMMask      = 0x80000000ul;

    public float MapScale { get; set; }
    public int PetCBID { get; set; } = -1;

    public new static void RegisterNetClassReps()
    {
        ImplementNetObject(out _dynClassRep);
    }

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public GhostCharacter()
    {
        UpdatePriorityScalar = 1.0f;
    }

    public override void SetParent(ClonedObjectBase parent)
    {
        base.SetParent(parent);

        // TODO?
    }

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception("Missing parent for GhostCharacter!");

        var character = Parent.GetAsCharacter();

        if (PIsInitialUpdate)
        {
            PackCommon(stream);

            stream.WriteString("", 17); // Name
            stream.WriteString("", 51); // ClanName
            stream.WriteBits(8, BitConverter.GetBytes(0)); // Level
            stream.WriteBits(64, BitConverter.GetBytes((long)0)); // Current Vehicle
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHead
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDBody
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHeadDetail
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHeadDetail
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHeadDetailMouth
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHeadDetailEyes
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHeadDetailHelmer
            stream.WriteBits(32, BitConverter.GetBytes(0)); // IDHair
            stream.WriteBits(32, BitConverter.GetBytes(0)); // CharacterScaleOffset
            stream.WriteInt(0, 24); // Character Color Primary
            stream.WriteInt(0, 24); // Character Color Secondary
            stream.WriteInt(0, 24); // Character Color Skin
            stream.WriteInt(0, 24); // Character Color Hair

            PackSkills(stream, character);
        }

        if (stream.WriteFlag((updateMask & GMMask) != 0))
            stream.WriteInt(0, 4); // GM level

        if (stream.WriteFlag((updateMask & ClanMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // Clan id
            stream.WriteBits(32, BitConverter.GetBytes(0)); // Clan rank
            stream.WriteString("", 51); // Clan name
        }

        if (stream.WriteFlag((updateMask & PetCBIDMask) != 0))
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Pet CBID

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
        {
            stream.Write(character.Position.X);
            stream.Write(character.Position.Y);
            stream.Write(character.Position.Z);

            stream.Write(character.Rotation.X);
            stream.Write(character.Rotation.Y);
            stream.Write(character.Rotation.Z);
            stream.Write(character.Rotation.W);

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

        if (stream.WriteFlag((updateMask & TokenMask) != 0))
        {
            stream.WriteFlag(false); // GivesToken
        }

        return 0UL;
    }

    public override void UnpackUpdate(GhostConnection connection, BitStream stream)
    {

    }

    public override void PerformScopeQuery(GhostConnection connection)
    {

    }
}

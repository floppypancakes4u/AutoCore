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

            stream.WriteString(character.Name, 17);
            stream.WriteString(character.ClanName, 51);
            stream.Write(character.Level);
            stream.Write(character.CurrentVehicle.ObjectId.Coid);
            stream.WriteInt((uint)character.HeadId, 16);
            stream.WriteInt((uint)character.BodyId, 16);
            stream.WriteInt((uint)character.HeadDetail1, 16);
            stream.WriteInt((uint)character.HeadDetail2, 16);
            stream.WriteInt((uint)character.MouthId, 16);
            stream.WriteInt((uint)character.EyesId, 16);
            stream.WriteInt((uint)character.HelmetId, 16);
            stream.WriteInt((uint)character.HairId, 16);
            stream.Write(character.ScaleOffset);
            stream.WriteInt(character.PrimaryColor, 24);
            stream.WriteInt(character.SecondaryColor, 24);
            stream.WriteInt(character.SkinColor, 24);
            stream.WriteInt(character.HairColor, 3);

            PackSkills(stream, character);
        }

        if (stream.WriteFlag((updateMask & GMMask) != 0))
            stream.WriteInt(character.GMLevel, 4);

        if (stream.WriteFlag((updateMask & ClanMask) != 0))
        {
            stream.Write(character.ClanId);
            stream.Write(character.ClanRank);
            stream.WriteString(character.ClanName, 51);
        }

        if (stream.WriteFlag((updateMask & PetCBIDMask) != 0))
            stream.WriteInt(0, 16);

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
        {
            stream.Write(character.Position.X);
            stream.Write(character.Position.Y);
            stream.Write(character.Position.Z);

            stream.Write(character.Rotation.X);
            stream.Write(character.Rotation.Y);
            stream.Write(character.Rotation.Z);
            stream.Write(character.Rotation.W);

            stream.Write(character.Velocity.X);
            stream.Write(character.Velocity.Y);
            stream.Write(character.Velocity.Z);

            stream.Write(character.TargetPosition.X);
            stream.Write(character.TargetPosition.Y);
            stream.Write(character.TargetPosition.Z);
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
        if (Parent == null)
            return;

        var character = Parent.GetAsCharacter();
        if (character == null || Parent.Map == null)
            return;

        Parent.Map.PerformScopeQuery(this, character, connection);
    }
}

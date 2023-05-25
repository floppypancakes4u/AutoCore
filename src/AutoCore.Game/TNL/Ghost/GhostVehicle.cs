using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

public class GhostVehicle : GhostObject
{
    private static NetClassRepInstance<GhostVehicle> _dynClassRep;
    private static ulong[] WeaponBits { get; } = new ulong[3] { FrontWeaponMask, TurretWeaponMask, RearWeaponMask };

    public const ulong AttributeMask    = 0x0000200000ul;
    public const ulong ClanMask         = 0x0000400000ul;
    public const ulong HardpointMask    = 0x0000800000ul;
    public const ulong PetCBIDMask      = 0x0001000000ul;
    public const ulong ShieldMaxMask    = 0x0002000000ul;
    public const ulong ShieldMask       = 0x0004000000ul;
    public const ulong PowerMask        = 0x0008000000ul;
    public const ulong GMMask           = 0x0010000000ul;
    public const ulong HeatMask         = 0x0020000000ul;
    public const ulong ChangeArmor      = 0x0040000000ul;
    public const ulong StateMask        = 0x0080000000ul;
    public const ulong WheelSetMask     = 0x0100000000ul;

    public const ulong FrontWeaponMask  = 0x0400000000ul;
    public const ulong TurretWeaponMask = 0x0800000000ul;
    public const ulong RearWeaponMask   = 0x1000000000ul;
    public const ulong MeleeWeaponMask  = 0x2000000000ul;
    public const ulong OrnamentMask     = 0x4000000000ul;

    public override NetClassRep GetClassRep()
    {
        return _dynClassRep;
    }

    public new static void RegisterNetClassReps()
    {
        ImplementNetObject(out _dynClassRep);
    }

    public GhostVehicle()
    {
        UpdatePriorityScalar = 1.0f;
    }

    public override void SetParent(ClonedObjectBase parent)
    {
        base.SetParent(parent);

        if (parent == null || parent.GetAsVehicle() == null)
            return;

        var vehParent = parent.GetAsVehicle();
        var superCharacter = parent.GetSuperCharacter(false);

        // TODO?
    }

    public override ulong PackUpdate(GhostConnection connection, ulong updateMask, BitStream stream)
    {
        if (Parent == null)
            throw new Exception("Missing parent for GhostVehicle!");

        var ret = 0ul;

        var parentVehicle = Parent.GetAsVehicle();
        var superCharacter = Parent.GetSuperCharacter(false);
        var owner = parentVehicle.Owner;

        if (PIsInitialUpdate)
        {
            PackCommon(stream);

            stream.Write(parentVehicle.PrimaryColor);
            stream.Write(parentVehicle.SecondaryColor);

            stream.WriteFlag(!Parent.Map.MapData.ContinentObject.IsTown); // IsActive

            stream.Write(parentVehicle.Trim);

            if (stream.WriteFlag(false)) // SpeedAdd != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // SpeedAdd
            }

            if (stream.WriteFlag(false)) // BrakesMaxTorqueFrontMultiplier != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // BrakesMaxTorqueFrontMultiplier
            }

            if (stream.WriteFlag(false)) // BrakesMaxTorqueRearAdjustMultiplier != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // BrakesMaxTorqueRearAdjustMultiplier
            }

            if (stream.WriteFlag(false)) // SteeringMaxAngleMultiplier != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // SteeringMaxAngleMultiplier
            }

            if (stream.WriteFlag(false)) // SteeringFullSpeedLimitMultiplier != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // SteeringFullSpeedLimitMultiplier
            }

            if (stream.WriteFlag(false)) // AVDCollisionSpinDampeningMultiplier != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // AVDCollisionSpinDampeningMultiplier
            }

            if (stream.WriteFlag(false)) // AVDNormalSpinDampeningMultiplier != 1.0f
            {
                stream.WriteBits(32, BitConverter.GetBytes(0)); // AVDNormalSpinDampeningMultiplier
            }

            if (stream.WriteFlag(false)) // TODO
            {
                stream.WriteInt(0, 18); // CoidCurrentPathID
                stream.WriteBits(32, BitConverter.GetBytes(0)); // ExtraPathId
                stream.WriteFlag(false); // PathReversing
                stream.WriteFlag(false); // PathIsRoad
                stream.WriteBits(32, BitConverter.GetBytes(0)); // PatrolDistance
            }

            if (stream.WriteFlag(false)) // TemplateId != -1
            {
                stream.WriteInt(0, 20); // TemplateId
            }

            if (stream.WriteFlag(false)) // CoidSpawnOwner != -1
            {
                stream.WriteInt(0, 20); // CoidSpawnOwner
            }

            stream.WriteBits(8, BitConverter.GetBytes(0)); // trick count

            for (var i = 0; i < 0; ++i)
                stream.WriteBits(16, BitConverter.GetBytes(0)); // trick id

            stream.WriteFlag(false); // IsTrailer

            if (stream.WriteFlag(owner != null)) // CurrentOwner
            {
                stream.Write(owner.ObjectId.Coid); // CurrentOwner coid
                stream.WriteFlag(owner.ObjectId.Global); // CurrentOwner global
                stream.WriteInt((uint)owner.CBID, 20); // CurrentOwner CBID

                var characterOwner = owner as Character;

                if (stream.WriteFlag(characterOwner != null))
                {
                    stream.WriteString(characterOwner.Name, 17);
                    stream.WriteString(characterOwner.ClanName, 51);
                    stream.Write(characterOwner.Level);
                    stream.WriteFlag(false); // IsPossessingCreature
                    stream.WriteString(parentVehicle.Name, 33);
                    stream.WriteInt((uint)characterOwner.HeadId, 16);
                    stream.WriteInt((uint)characterOwner.BodyId, 16);
                    stream.WriteInt((uint)characterOwner.HeadDetail1, 16);
                    stream.WriteInt((uint)characterOwner.HeadDetail2, 16);
                    stream.WriteInt((uint)characterOwner.MouthId, 16);
                    stream.WriteInt((uint)characterOwner.EyesId, 16);
                    stream.WriteInt((uint)characterOwner.HelmetId, 16);
                    stream.WriteInt((uint)characterOwner.HairId, 16);
                }
                else
                {
                    if (stream.WriteFlag(false)) // EnhancementID != -1
                        stream.WriteInt(0, 20); // EnhancementID

                    if (stream.WriteFlag(false)) // CoidOnUseTrigger != -1
                        stream.WriteInt(0, 20); // CoidOnUseTrigger

                    if (stream.WriteFlag(false)) // CoidOnUseReaction != -1
                        stream.WriteInt(0, 20); // CoidOnUseReaction

                    if (stream.WriteFlag(false)) // CreatureSummoner coid != -1
                    {
                        stream.WriteBits(64, BitConverter.GetBytes((long)0)); // CreatureSummoner coid
                        stream.WriteFlag(false); // CreatureSummoner coid global
                    }

                    stream.WriteFlag(false); // DoesntCountAsSummon
                    stream.WriteBits(8, BitConverter.GetBytes(0)); // Level
                    stream.WriteFlag(false); // IsElite
                }
            }

            ret = 0x80;
        }
        else
        {
            if (stream.WriteFlag((updateMask & SkillsMask) != 0))
            {
                if (stream.WriteFlag(false)) // Has Owner Skills
                    PackSkills(stream, null); // Owner skills

                PackSkills(stream, parentVehicle);
            }
        }

        if (stream.WriteFlag((updateMask & WheelSetMask) != 0))
        {
            stream.WriteInt(0, 20); // WheelSet CBID
            stream.Write((long)0); // WheelSet coid
            stream.WriteFlag(false); // WheelSet coid global
        }

        for (var i = 0; i < WeaponBits.Length; ++i)
        {
            if (stream.WriteFlag((updateMask & WeaponBits[i]) != 0) && stream.WriteFlag(false)) // TODO
            {
                stream.WriteInt(0, 20); // Weapon[i].CBID
                stream.Write((long)0); // Weapon[i].Coid
                stream.WriteFlag(false); // Weapon[i].CoidGlobal
            }
        }

        if (stream.WriteFlag((updateMask & MeleeWeaponMask) != 0) && stream.WriteFlag(false)) // TODO
        {
            stream.WriteInt(0, 20); // WeaponMeelee CBID
            stream.Write((long)0); // WeaponMeelee Coid
            stream.WriteFlag(false); // WeaponMeelee Coid global
        }

        if (stream.WriteFlag((updateMask & OrnamentMask) != 0) && stream.WriteFlag(false)) // TODO
        {
            stream.WriteInt(0, 20); // Ornament CBID
            stream.Write((long)0); // Ornament Coid
            stream.WriteFlag(false); // Ornament Coid global
        }

        if (stream.WriteFlag((updateMask & ChangeArmor) != 0) && stream.WriteFlag(false)) // TODO
        {
            stream.WriteInt(0, 20); // Armor CBID
            stream.Write((long)0); // Armor Coid
            stream.WriteFlag(false); // Armor Coid global
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Armor[0]
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Armor[1]
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Armor[2]
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Armor[3]
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Armor[4]
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Armor[5]
        }

        if (stream.WriteFlag((updateMask & GMMask) != 0 && false)) // TODO
        {
            stream.WriteInt(0, 4); // GMLevel if owner is character
        }

        if (stream.WriteFlag((updateMask & ClanMask) != 0 && false)) // TODO
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // ClanId
            stream.WriteBits(32, BitConverter.GetBytes(0)); // ClanRank
            stream.WriteString("", 51); // ClanName
        }

        if (stream.WriteFlag((updateMask & PetCBIDMask) != 0 && false)) // TODO
        {
            stream.WriteBits(16, BitConverter.GetBytes(0)); // Pet CBID
        }

        if (stream.WriteFlag((updateMask & MurdererMask) != 0)) // TODO
        {
            stream.WriteBits(64, BitConverter.GetBytes((long)0)); // Coid murderer
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

        if (stream.WriteFlag((updateMask & StateMask) != 0 && false)) // TODO
        {
            stream.WriteBits(8, BitConverter.GetBytes(0)); // AI State if owner is creature
        }

        if (stream.WriteFlag((updateMask & PositionMask) != 0))
        {
            stream.Write(parentVehicle.Position.X);
            stream.Write(parentVehicle.Position.Y);
            stream.Write(parentVehicle.Position.Z);

            stream.Write(parentVehicle.Rotation.X);
            stream.Write(parentVehicle.Rotation.Y);
            stream.Write(parentVehicle.Rotation.Z);
            stream.Write(parentVehicle.Rotation.W);

            stream.Write(parentVehicle.Velocity.X);
            stream.Write(parentVehicle.Velocity.Y);
            stream.Write(parentVehicle.Velocity.Z);

            stream.Write(parentVehicle.AngularVelocity.X);
            stream.Write(parentVehicle.AngularVelocity.Y);
            stream.Write(parentVehicle.AngularVelocity.Z);

            stream.Write((byte)parentVehicle.VehicleFlags);
            stream.Write(parentVehicle.Firing);

            stream.WriteSignedFloat(parentVehicle.Acceleration, 6);
            stream.WriteSignedFloat(parentVehicle.Steering, 6);

            stream.Write(parentVehicle.WantedTurretDirection);
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

        if (stream.WriteFlag(superCharacter != null && (updateMask & AttributeMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribCombat
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribPerception
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribTech
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribTheory
        }

        if (stream.WriteFlag((updateMask & HeatMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // TODO
        }

        if (stream.WriteFlag((updateMask & ShieldMaxMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // MaxShield
        }

        if (stream.WriteFlag((updateMask & ShieldMask) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // Shield
        }

        if (stream.WriteFlag((updateMask & PowerMask) != 0)) // TODO
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // Power
        }

        if (stream.WriteFlag((updateMask & TokenMask) != 0))
        {
            stream.WriteFlag(false); // GivesToken
        }

        return ret;
    }
}

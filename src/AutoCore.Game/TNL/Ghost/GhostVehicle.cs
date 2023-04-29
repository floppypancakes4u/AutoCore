using TNL.Entities;
using TNL.Types;
using TNL.Utils;

namespace AutoCore.Game.TNL.Ghost;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

public class GhostVehicle : GhostObject
{
    private static NetClassRepInstance<GhostVehicle> _dynClassRep;
    private static ulong[] WeaponBits { get; } = new ulong[3] { 0x400000000, 0x800000000, 0x1000000000 };

    public byte ArmorFlags { get; set; }
    public int MaxShields { get; set; }
    public int CbidPet { get; set; } = -1;
    public int MaxHp { get; set; }
    public int CurrHp { get; set; }
    public int Combat { get; set; }
    public int Perception { get; set; }
    public int Tech { get; set; }
    public int Theory { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string ClanName { get; set; } = string.Empty;
    public int ClanId { get; set; } = -1;
    public int ClanRank { get; set; } = -1;

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

        if (PIsInitialUpdate)
        {
            PackCommon(stream);

            stream.WriteBits(32, BitConverter.GetBytes(0)); // ColorPrimary

            stream.WriteBits(32, BitConverter.GetBytes(0)); // ColorSecondary

            stream.WriteFlag(false); // IsActive

            stream.WriteBits(8, BitConverter.GetBytes(0)); // Trim

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

            if (stream.WriteFlag(false)) // CurrentOwner
            {
                stream.Write((long)0); // CurrentOwner coid
                stream.WriteFlag(false); // CurrentOwner global
                stream.WriteInt(0, 20); // CurrentOwner CBID

                if (stream.WriteFlag(false)) // OwnerIsCharacter
                {
                    stream.WriteString("", 17); // Name
                    stream.WriteString("", 51); // ClanName
                    stream.WriteBits(8, BitConverter.GetBytes(0)); // Level
                    stream.WriteFlag(false); // TODO
                    stream.WriteString("", 33); // VehicleName
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHead
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDBody
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHeadDetail
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHeadDetail2
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHeadDetailMouth
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHeadDetailEyes
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHeadDetailHelmet
                    stream.WriteBits(16, BitConverter.GetBytes(0)); // IDHair
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
            if (stream.WriteFlag((updateMask & 0x80) != 0))
            {
                if (stream.WriteFlag(false)) // Has Owner Skills
                    PackSkills(stream, null); // Owner skills

                PackSkills(stream, parentVehicle);
            }
        }

        if (stream.WriteFlag((updateMask & 0x100000000) != 0))
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

        if (stream.WriteFlag((updateMask & 0x2000000000) != 0) && stream.WriteFlag(false)) // TODO
        {
            stream.WriteInt(0, 20); // WeaponMeelee CBID
            stream.Write((long)0); // WeaponMeelee Coid
            stream.WriteFlag(false); // WeaponMeelee Coid global
        }

        if (stream.WriteFlag((updateMask & 0x4000000000) != 0) && stream.WriteFlag(false)) // TODO
        {
            stream.WriteInt(0, 20); // Ornament CBID
            stream.Write((long)0); // Ornament Coid
            stream.WriteFlag(false); // Ornament Coid global
        }

        if (stream.WriteFlag((updateMask & 0x40000000) != 0) && stream.WriteFlag(false)) // TODO
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

        if (stream.WriteFlag((updateMask & 0x10000000) != 0 && false)) // TODO
        {
            stream.WriteInt(0, 4); // GMLevel if owner is character
        }

        if (stream.WriteFlag((updateMask & 0x400000) != 0 && false)) // TODO
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // ClanId
            stream.WriteBits(32, BitConverter.GetBytes(0)); // ClanRank
            stream.WriteString("", 51); // ClanName
        }

        if (stream.WriteFlag((updateMask & 0x1000000) != 0 && false)) // TODO
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // Pet CBID
        }

        if (stream.WriteFlag((updateMask & 0x20) != 0)) // TODO
        {
            stream.WriteBits(64, BitConverter.GetBytes((long)0)); // Coid murderer
        }

        if (stream.WriteFlag((updateMask & 0x8) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetCurrentHP(), 0), 18);
            stream.WriteFlag(Parent.GetIsCorpse());
        }

        if (stream.WriteFlag((updateMask & 0x40) != 0))
        {
            stream.WriteInt((uint)Math.Max(Parent.GetMaximumHP(), 0), 18);
        }

        if (stream.WriteFlag((updateMask & 0x80000000) != 0 && false)) // TODO
        {
            stream.WriteBits(8, BitConverter.GetBytes(0)); // AI State if owner is creature
        }

        if (stream.WriteFlag((updateMask & 0x2) != 0))
        {
            stream.Write(parentVehicle.Position.X);
            stream.Write(parentVehicle.Position.Y);
            stream.Write(parentVehicle.Position.Z);

            stream.Write(parentVehicle.Rotation.X);
            stream.Write(parentVehicle.Rotation.Y);
            stream.Write(parentVehicle.Rotation.Z);
            stream.Write(parentVehicle.Rotation.W);

            var linearVelocityX = 0.0f;
            var linearVelocityY = 0.0f;
            var linearVelocityZ = 0.0f;

            stream.Write(linearVelocityX);
            stream.Write(linearVelocityY);
            stream.Write(linearVelocityZ);

            var angularVelocityX = 0.0f;
            var angularVelocityY = 0.0f;
            var angularVelocityZ = 0.0f;

            stream.Write(angularVelocityX);
            stream.Write(angularVelocityY);
            stream.Write(angularVelocityZ);

            stream.WriteBits(8, BitConverter.GetBytes(0)); // TODO some flags, maybe firing too?
            stream.WriteBits(8, BitConverter.GetBytes(0)); // TODO

            stream.WriteSignedFloat(0.0f, 6); // TODO (parentVehicle.Acceleration, 6);
            stream.WriteSignedFloat(0.0f, 6); // TODO (parentVehicle.Steering, 6);

            stream.WriteBits(32, BitConverter.GetBytes(0)); // TODO
        }

        if (stream.WriteFlag((updateMask & 0x4) != 0))
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

        var superCharacter = Parent.GetSuperCharacter(false);

        if (stream.WriteFlag(superCharacter != null && (updateMask & 0x200000) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribCombat
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribPerception
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribTech
            stream.WriteBits(32, BitConverter.GetBytes(0)); // AttribTheory
        }

        if (stream.WriteFlag((updateMask & 0x20000000) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // TODO
        }

        if (stream.WriteFlag((updateMask & 0x2000000) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // MaxShield
        }

        if (stream.WriteFlag((updateMask & 0x4000000) != 0))
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // TODO
        }

        if (stream.WriteFlag((updateMask & 0x8000000) != 0) && false) // TODO
        {
            stream.WriteBits(32, BitConverter.GetBytes(0)); // TODO
        }

        if (stream.WriteFlag((updateMask & 0x100) != 0))
        {
            stream.WriteFlag(false); // GivesToken
        }

        return ret;
    }

    public override void UnpackUpdate(GhostConnection connection, BitStream stream)
    {

    }

    public void AddEquip(object createMsg, TFID id, int packetSize)
    {

    }

    public void AddEquip2(object createMsg, TFID id, int packetSize)
    {

    }
}

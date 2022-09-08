namespace AutoCore.Game.CloneBases.Specifics;

using AutoCore.Game.Structures;

public struct WeaponSpecific
{
    public float AccucaryModifier;
    public int BulletId;
    public float DamageBonusPerLevel;
    public float DamageScalar;
    public int DmgMaxMax;
    public int DmgMaxMin;
    public int DmgMinMax;
    public int DmgMinMin;
    public int DotDuration;
    public float ExplosionRadius;
    public Vector3 FirePoint;
    public byte Flags;
    public short Heat;
    public float HitBonusPerLevel;
    public DamageSpecific MaxMax;
    public DamageSpecific MaxMin;
    public DamageSpecific MinMax;
    public DamageSpecific MinMin;
    public short OffenseBonus;
    public short PenetrationModifier;
    public float RangeMax;
    public float RangeMin;
    public int RechargeTime;
    public byte SprayTargets;
    public byte SubType;
    public byte TurretSize;
    public float ValidArc;

    public static WeaponSpecific ReadNew(BinaryReader reader)
    {
        var ws = new WeaponSpecific
        {
            FirePoint = Vector3.ReadNew(reader),
            MinMin = DamageSpecific.ReadNew(reader),
            MinMax = DamageSpecific.ReadNew(reader),
            MaxMin = DamageSpecific.ReadNew(reader),
            MaxMax = DamageSpecific.ReadNew(reader),
            ValidArc = reader.ReadSingle(),
            RangeMin = reader.ReadSingle(),
            RangeMax = reader.ReadSingle(),
            ExplosionRadius = reader.ReadSingle(),
            DamageScalar = reader.ReadSingle(),
            AccucaryModifier = reader.ReadSingle(),
            RechargeTime = reader.ReadInt32(),
            DmgMinMin = reader.ReadInt32(),
            DmgMinMax = reader.ReadInt32(),
            DmgMaxMin = reader.ReadInt32(),
            DmgMaxMax = reader.ReadInt32(),
            BulletId = reader.ReadInt32(),
            DotDuration = reader.ReadInt32(),
            PenetrationModifier = reader.ReadInt16(),
            Heat = reader.ReadInt16(),
            SubType = reader.ReadByte(),
            TurretSize = reader.ReadByte(),
            Flags = reader.ReadByte(),
            SprayTargets = reader.ReadByte(),
            OffenseBonus = reader.ReadInt16()
        };

        reader.BaseStream.Position += 2;

        ws.HitBonusPerLevel = reader.ReadSingle();
        ws.DamageBonusPerLevel = reader.ReadSingle();

        return ws;
    }
}

namespace AutoCore.Game.Entities;

using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;

public class Creature : SimpleObject
{
    public int EnhancementId { get; private set; } = -1;

    public Vector3 Velocity { get; private set; }
    public Vector3 AngularVelocity { get; private set; }
    public Vector3 TargetPosition { get; private set; }
    public long SpawnOwner { get; set; }
    public byte Level { get; set; } = 1;

    public Creature()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override Creature GetAsCreature() => this;
    public override Creature GetSuperCreature() => this;

    public virtual byte GetLevel() => Level;

    public void ScaleHealthForLevel(byte baseLevel)
    {
        // This is FAR from perfect or accurate, but it's based off extremely limited information that I have. - Floppy
        // Scale health based on level - linear scaling from level 1
        // BaseHP is the HP at level 1 (e.g., 37 HP for level 1 biomeck instructors)
        // Formula: BaseHP + (Level - 1) * HPPerLevel
        // Level 1: BaseHP (e.g., 37)
        // Level 6-8: ~79 HP, so HPPerLevel = (79 - 37) / (6-1) ≈ 8.4, use 8
        var baseHP = CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
        var levelDiff = Level - 1; // Scale from level 1, not BaseLevel
        const int hpPerLevel = 8; // HP increase per level above level 1
        var scaledHP = baseHP + (levelDiff * hpPerLevel);
        HP = MaxHP = Math.Max(1, scaledHP);
    }

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateCharacterPacket)
            return;
    }

    public override void CreateGhost()
    {
        if (Ghost != null)
            return;

        Ghost = new GhostCreature();
        Ghost.SetParent(this);
    }

    public void HandleMovement(CreatureMovedPacket packet)
    {
        if (Ghost == null)
            return;

        if (packet.ObjectId != ObjectId)
            throw new Exception("WTF? Someone else moves me?");

        // Update position
        Position = packet.Location;
        Rotation = packet.Rotation;
        Velocity = packet.Velocity;
        AngularVelocity = packet.AngularVelocity;
        TargetPosition = packet.TargetPosition;

        Ghost.SetMaskBits(GhostObject.PositionMask);

        // Update target
        if (Target != null)
        {
            if (packet.Target.Coid == -1)
            {
                Target = null;

                Ghost.SetMaskBits(GhostObject.TargetMask);
            }
            else if (packet.Target != Target.ObjectId)
            {
                Target = ObjectManager.Instance.GetObject(packet.Target);

                Ghost.SetMaskBits(GhostObject.TargetMask);
            }
        }
        else if (packet.Target.Coid != -1)
        {
            Target = ObjectManager.Instance.GetObject(packet.Target);

            Ghost.SetMaskBits(GhostObject.TargetMask);
        }
    }
}

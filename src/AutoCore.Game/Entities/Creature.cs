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

    public Creature()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override Creature GetAsCreature() => this;
    public override Creature GetSuperCreature() => this;

    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateCharacterPacket)
            return;
    }

    public override void CreateGhost()
    {
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

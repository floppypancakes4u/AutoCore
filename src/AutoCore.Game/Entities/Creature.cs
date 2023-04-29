namespace AutoCore.Game.Entities;

using AutoCore.Game.Packets.Sector;
using AutoCore.Game.TNL.Ghost;

public class Creature : SimpleObject
{
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
}

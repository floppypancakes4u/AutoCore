namespace AutoCore.Game.Entities;

using AutoCore.Game.Packets.Sector;

public class Creature : SimpleObject
{
    public override void WriteToPacket(CreateSimpleObjectPacket packet)
    {
        base.WriteToPacket(packet);

        if (packet is CreateCharacterPacket)
            return;
    }
}

namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;

public class EnterPointTemplate : GraphicsObjectTemplate
{
    public byte MapTransferType { get; set; }
    public int MapTransferData { get; set; }

    public EnterPointTemplate()
        : base(GraphicsObjectType.Graphics)
    {
    }

    public override void Read(BinaryReader reader, int mapVersion)
    {
        Location = Vector4.ReadNew(reader);
        Rotation = Quaternion.Read(reader);

        MapTransferType = reader.ReadByte();
        MapTransferData = reader.ReadInt32();

        if (mapVersion >= 7)
            Faction = reader.ReadInt32();
    }
}

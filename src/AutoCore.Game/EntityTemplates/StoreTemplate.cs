namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Entities;
using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class StoreTemplate : GraphicsObjectTemplate
{
    public List<ItemType> Items { get; } = new();
    public string Name { get; set; }
    public int MinLevel { get; set; }
    public int MaxLevel { get; set; }
    public bool IsJunkyard { get; set; }
    public bool IsVehicleStore { get; set; }
    public bool IsSouvenirStore { get; set; }

    public StoreTemplate()
        : base(GraphicsObjectType.GraphicsPhysics)
    {
    }

    public override void Read(BinaryReader reader, int mapVersion)
    {
        Location = Vector4.ReadNew(reader);
        Rotation = Quaternion.Read(reader);

        for (var i = 0; i < (mapVersion <= 50 ? 10 : 30); ++i)
            Items.Add(ItemType.Read(reader));

        if (mapVersion > 39)
        {
            Name = reader.ReadLengthedString();
            MinLevel = reader.ReadInt32();
            MaxLevel = reader.ReadInt32();
        }

        if (mapVersion > 40)
            IsJunkyard = reader.ReadBoolean();

        if (mapVersion >= 50)
            IsVehicleStore = reader.ReadBoolean();

        if (mapVersion >= 61)
            IsSouvenirStore = reader.ReadBoolean();
    }

    public class ItemType
    {
        public byte Type { get; set; }
        public float Percentage { get; set; }
        public int Value { get; set; }
        public bool Unlimited { get; set; }
        public int CBID { get; set; }

        public static ItemType Read(BinaryReader reader)
        {
            return new ItemType
            {
                Type = reader.ReadByte(),
                Percentage = reader.ReadSingle(),
                Value = reader.ReadInt32(),
                Unlimited = reader.ReadBoolean(),
                CBID = reader.ReadInt32(),
            };
        }
    }
}

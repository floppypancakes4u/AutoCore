namespace AutoCore.Game.Structures;

using AutoCore.Utils.Extensions;

public class RoadNodeBase
{
    public string FileName { get; set; }
    public List<int> NodeIds { get; } = new();
    public Vector3 Position { get; set; }
    public int UniqueId { get; set; }

    public virtual void Unserialize(BinaryReader reader, int mapVersion)
    {
        UniqueId = reader.ReadInt32();
        Position = Vector3.ReadNew(reader);
        FileName = reader.ReadUTF8StringOn(260);

        var nodeCount = reader.ReadInt32();
        for (var i = 0; i < nodeCount; ++i)
            NodeIds.Add(reader.ReadInt32());
    }
}

public class RoadNode : RoadNodeBase
{
}

public class RoadNodeJunction : RoadNodeBase
{
    public float Rotation { get; set; }
    public List<Vector3> Positions { get; } = new();
    public List<Vector3> Directions { get; } = new();

    public override void Unserialize(BinaryReader reader, int mapVersion)
    {
        base.Unserialize(reader, mapVersion);

        Rotation = reader.ReadSingle();

        if (mapVersion >= 28)
        {
            for (var i = 0; i < 6; ++i)
            {
                Positions.Add(Vector3.ReadNew(reader));
                Directions.Add(Vector3.ReadNew(reader));
            }
        }
    }
}

public class RiverNode : RoadNodeBase
{
    public float WaterDepth { get; set; }
    public uint ReflectColor { get; set; }
    public uint RefractColor { get; set; }

    public override void Unserialize(BinaryReader reader, int mapVersion)
    {
        base.Unserialize(reader, mapVersion);

        WaterDepth = reader.ReadSingle();

        if (mapVersion >= 12)
        {
            ReflectColor = reader.ReadUInt32();
            RefractColor = reader.ReadUInt32();
        }
    }
}

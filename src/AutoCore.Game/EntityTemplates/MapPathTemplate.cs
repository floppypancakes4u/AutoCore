namespace AutoCore.Game.EntityTemplates;

using AutoCore.Game.Structures;
using AutoCore.Utils.Extensions;

public class MapPathTemplate : ObjectTemplate
{
    public int StaticDefaultPathCBID { get; set; }
    public bool ReverseDirection { get; set; }
    public string PathName { get; set; }
    public List<MapPathPoint> Points { get; } = new();

    public override void Read(BinaryReader reader, int mapVersion)
    {
        StaticDefaultPathCBID = reader.ReadInt32();
        ReverseDirection = reader.ReadBoolean();
        PathName = reader.ReadUTF8StringOn(64);

        var pointCount = reader.ReadInt32();
        for (var i = 0; i < pointCount; ++i)
            Points.Add(MapPathPoint.Read(reader));
    }

    public class MapPathPoint
    {
        public Vector3 Position { get; set; }
        public float AcceptDistance { get; set; }
        public long ReactionCoid { get; set; }
        public int WaitTime { get; set; }

        public static MapPathPoint Read(BinaryReader reader)
        {
            var point = new MapPathPoint
            {
                Position = Vector3.ReadNew(reader),
                AcceptDistance = reader.ReadSingle(),
                ReactionCoid = reader.ReadInt64(),
                WaitTime = reader.ReadInt32()
            };

            reader.BaseStream.Position += 4;

            return point;
        }
    }
}

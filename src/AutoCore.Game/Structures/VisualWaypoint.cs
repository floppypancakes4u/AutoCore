namespace AutoCore.Game.Structures;

using AutoCore.Utils.Extensions;

public class VisualWaypoint
{
    public int Id { get; private set; }
    public long ObjectCoid { get; private set; }
    public int ObjectiveCount { get; private set; }
    public int[] Objectives { get; private set; }
    public long ReactionCoid { get; private set; }
    public byte Type { get; private set; }
    public Vector3 Position { get; private set; }

    public static VisualWaypoint Read(BinaryReader reader)
    {
        _ = reader.ReadInt32();

        var wp = new VisualWaypoint
        {
            Id = reader.ReadInt32(),
            Type = reader.ReadByte(),
            Position = Vector3.ReadNew(reader),
            ObjectCoid = reader.ReadInt64(),
            ReactionCoid = reader.ReadInt64(),
            ObjectiveCount = reader.ReadInt32()
        };

        wp.Objectives = reader.ReadConstArray(wp.ObjectiveCount, reader.ReadInt32);

        return wp;
    }

    /// <summary>Unit-test factory for map GPS / HUD waypoint rows.</summary>
    internal static VisualWaypoint CreateForTests(
        int id,
        Vector3 position,
        int[] objectives = null,
        long objectCoid = 0,
        long reactionCoid = 0,
        byte type = 0)
    {
        var objs = objectives ?? Array.Empty<int>();
        return new VisualWaypoint
        {
            Id = id,
            Type = type,
            Position = position,
            ObjectCoid = objectCoid,
            ReactionCoid = reactionCoid,
            ObjectiveCount = objs.Length,
            Objectives = objs,
        };
    }
}

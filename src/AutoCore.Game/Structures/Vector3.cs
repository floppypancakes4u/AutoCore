namespace AutoCore.Game.Structures;

public struct Vector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3 ReadNew(BinaryReader reader)
    {
        return new Vector3
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle()
        };
    }

    public override string ToString()
    {
        return $"Vector3(X: {X} | Y: {Y} | Z: {Z})";
    }
}

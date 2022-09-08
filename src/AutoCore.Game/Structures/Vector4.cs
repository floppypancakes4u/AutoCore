namespace AutoCore.Game.Structures;

public struct Vector4
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    public Vector4(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static Vector4 ReadNew(BinaryReader reader)
    {
        return new Vector4
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            W = reader.ReadSingle()
        };
    }

    public override string ToString()
    {
        return $"Vector4(X: {X} | Y: {Y} | Z: {Z} | W: {W})";
    }
}

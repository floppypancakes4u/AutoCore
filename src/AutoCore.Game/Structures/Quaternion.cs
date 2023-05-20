namespace AutoCore.Game.Structures;

public struct Quaternion
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    public static Quaternion Default { get; } = new(0.0f, 0.0f, 0.0f, 1.0f);

    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static Quaternion Read(BinaryReader br)
    {
        return new Quaternion
        {
            X = br.ReadSingle(),
            Y = br.ReadSingle(),
            Z = br.ReadSingle(),
            W = br.ReadSingle()
        };
    }

    public override string ToString()
    {
        return $"X: {X} | Y: {Y} | Z: {Z} | Angle: {W}";
    }
}

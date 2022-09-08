namespace AutoCore.Game.Structures;

public struct Quaternion
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Angle { get; set; }

    public Quaternion(float x, float y, float z, float angle)
    {
        X = x;
        Y = y;
        Z = z;
        Angle = angle;
    }

    public static Quaternion Read(BinaryReader br)
    {
        return new Quaternion
        {
            X = br.ReadSingle(),
            Y = br.ReadSingle(),
            Z = br.ReadSingle(),
            Angle = br.ReadSingle()
        };
    }

    public override string ToString()
    {
        return $"X: {X} | Y: {Y} | Z: {Z} | Angle: {Angle}";
    }
}

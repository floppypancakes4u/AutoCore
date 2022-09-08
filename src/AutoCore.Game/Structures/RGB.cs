namespace AutoCore.Game.Structures;

public struct RGB
{
    public float B;
    public float G;
    public float R;

    public static RGB ReadNew(BinaryReader reader)
    {
        return new RGB
        {
            R = reader.ReadSingle(),
            G = reader.ReadSingle(),
            B = reader.ReadSingle()
        };
    }

    public override string ToString()
    {
        return $"R: {R} | G: {G} | B: {B}";
    }
}

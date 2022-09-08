namespace AutoCore.Game.Structures;

public struct FrontRear
{
    public float Front;
    public float Rear;

    public static FrontRear ReadNew(BinaryReader reader)
    {
        return new FrontRear
        {
            Front = reader.ReadSingle(),
            Rear = reader.ReadSingle()
        };
    }
}

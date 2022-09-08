namespace AutoCore.Game.Structures;

using AutoCore.Utils.Extensions;

public struct VehicleTrick
{
    public string Description;
    public byte ExclusiveGroup;
    public string FileName;
    public string GroupDescription;
    public byte GroupId;
    public int Id;
    public int VehicleId;

    public static VehicleTrick ReadNew(BinaryReader reader)
    {
        return new VehicleTrick
        {
            Id = reader.ReadInt32(),
            VehicleId = reader.ReadInt32(),
            ExclusiveGroup = reader.ReadByte(),
            GroupId = reader.ReadByte(),
            FileName = reader.ReadUTF16StringOn(65),
            Description = reader.ReadUTF16StringOn(33),
            GroupDescription = reader.ReadUTF16StringOn(33)
        };
    }

    public override string ToString()
    {
        return $"Id: {Id} | File: {FileName} | Desc: {Description} | GDesc: {GroupDescription}";
    }
}

namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

[Flags]
public enum VehicleMovedFlags : byte
{
    Handbreak = 0x1,
    SimClient = 0x2,
    Corpse    = 0x4
}

public class VehicleMovedPacket : ObjectMovedPacket
{
    public override GameOpcode Opcode => GameOpcode.VehicleMoved;

    public float Acceleration { get; set; }
    public float Steering { get; set; }
    public float TurretDirection { get; set; }
    public VehicleMovedFlags VehicleFlags { get; set; }
    public byte Firing { get; set; }
    public TFID Target { get; set; }

    public override void Read(BinaryReader reader)
    {
        base.Read(reader);

        Acceleration = reader.ReadSingle();
        Steering = reader.ReadSingle();
        TurretDirection = reader.ReadSingle();
        VehicleFlags = (VehicleMovedFlags)reader.ReadByte();
        Firing = reader.ReadByte();
        Target = reader.ReadTFID();
    }

    public override void Write(BinaryWriter writer)
    {
        throw new NotSupportedException();
    }
}

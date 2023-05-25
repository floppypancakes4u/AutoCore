namespace AutoCore.Game.Packets.Sector;

using AutoCore.Game.Constants;
using AutoCore.Game.Extensions;
using AutoCore.Game.Structures;

public class ObjectMovedPacket : BasePacket
{
    public override GameOpcode Opcode => GameOpcode.ObjectMoved;

    public TFID ObjectId { get; set; }
    public Vector3 Location { get; set; }
    public Quaternion Rotation { get; set; }
    public Vector3 Velocity { get; set; }
    public Vector3 AngularVelocity { get; set; }
    public bool Absolute { get; set; }
    public Vector3 TargetPosition { get; set; }

    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position += 4;

        ObjectId = reader.ReadTFID();
        Location = Vector3.ReadNew(reader);
        Velocity = Vector3.ReadNew(reader);
        Rotation = Quaternion.Read(reader);
        AngularVelocity = Vector3.ReadNew(reader);
        Absolute = reader.ReadBoolean();

        reader.BaseStream.Position += 3;

        TargetPosition = Vector3.ReadNew(reader);

        reader.BaseStream.Position += 4;
    }

    public override void Write(BinaryWriter writer)
    {
        throw new NotSupportedException();
    }
}

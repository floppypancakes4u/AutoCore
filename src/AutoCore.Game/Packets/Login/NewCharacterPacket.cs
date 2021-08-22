using System;
using System.IO;

namespace AutoCore.Game.Packets.Login
{
    using Constants;
    using Utils.Extensions;

    public class NewCharacterPacket : BasePacket
    {
        public override GameOpcode Opcode => GameOpcode.LoginNewCharacterResponse;

        public int CBID { get; set; }
        public string PlayerName { get; set; }
        public string CharacterName { get; set; }
        public int HeadId { get; set; }
        public int BodyId { get; set; }
        public int HeadDetail1 { get; set; }
        public int HeadDetail2 { get; set; }
        public int HelmetId { get; set; }
        public int EyesId { get; set; }
        public int MouthId { get; set; }
        public int HairId { get; set; }
        public uint PrimaryColor { get; set; }
        public uint SecondaryColor { get; set; }
        public uint EyesColor { get; set; }
        public uint HairColor { get; set; }
        public uint SkinColor { get; set; }
        public uint SpecialityColor { get; set; }
        public int ShardId { get; set; }
        public int VehiclePrimaryColor { get; set; }
        public int VehicleSecondaryColor { get; set; }
        public byte VehicleTrim { get; set; }
        public float ScaleOffset { get; set; }
        public int WheelsetCBID { get; set; }
        public string VehicleName { get; set; }

        public override void Read(BinaryReader reader)
        {
            CBID = reader.ReadInt32();
            PlayerName = reader.ReadUtf8StringOn(33);
            CharacterName = reader.ReadUtf8StringOn(51);
            HeadId = reader.ReadInt32();
            BodyId = reader.ReadInt32();
            HeadDetail1 = reader.ReadInt32();
            HeadDetail2 = reader.ReadInt32();
            HelmetId = reader.ReadInt32();
            EyesId = reader.ReadInt32();
            MouthId = reader.ReadInt32();
            HairId = reader.ReadInt32();
            PrimaryColor = reader.ReadUInt32();
            SecondaryColor = reader.ReadUInt32();
            EyesColor = reader.ReadUInt32();
            HairColor = reader.ReadUInt32();
            SkinColor = reader.ReadUInt32();
            SpecialityColor = reader.ReadUInt32();
            ShardId = reader.ReadInt32();
            VehiclePrimaryColor = reader.ReadInt32();
            VehicleSecondaryColor = reader.ReadInt32();
            VehicleTrim = reader.ReadByte();

            reader.BaseStream.Position += 3;

            ScaleOffset = reader.ReadSingle();
            WheelsetCBID = reader.ReadInt32();
            VehicleName = reader.ReadUtf8StringOn(33);
        }

        public override void Write(BinaryWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}

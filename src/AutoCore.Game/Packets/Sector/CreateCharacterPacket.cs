using System;
using System.IO;

namespace AutoCore.Game.Packets.Sector
{
    using Constants;
    using Extensions;
    using Utils.Extensions;

    public class CreateCharacterPacket : CreateSimpleObjectPacket
    {
        public override GameOpcode Opcode => GameOpcode.CreateCharacter;

        public long CurrentVehicleCoid { get; set; }
        public long CurrentTrailerCoid { get; set; }
        public int HeadId { get; set; }
        public int BodyId { get; set; }
        public int AccessoryId1 { get; set; }
        public int AccessoryId2 { get; set; }
        public int HairId { get; set; }
        public int MouthId { get; set; }
        public int EyesId { get; set; }
        public int HelmetId { get; set; }
        public uint PrimaryColor { get; set; }
        public uint SecondaryColor { get; set; }
        public uint EyesColor { get; set; }
        public uint HairColor { get; set; }
        public uint SkinColor { get; set; }
        public uint SpecialityColor { get; set; }
        public int LastTownId { get; set; }
        public int LastStationMapId { get; set; }
        public byte Level { get; set; }
        private byte Bf297 { get; set; }
        public bool UsingVehicle
        {
            get
            {
                return (Bf297 & 1) == 1;
            }
            set
            {
                if (value)
                {
                    Bf297 |= 1;
                }
                else
                {
                    Bf297 &= 0xFE;
                }
            }
        }
        public bool UsingTrailer
        {
            get
            {
                return (Bf297 & 2) == 2;
            }
            set
            {
                if (value)
                {
                    Bf297 |= 2;
                }
                else
                {
                    Bf297 &= 0xFD;
                }
            }
        }
        public bool IsPosessingCreature
        {
            get
            {
                return (Bf297 & 4) == 4;
            }
            set
            {
                if (value)
                {
                    Bf297 |= 4;
                }
                else
                {
                    Bf297 &= 0xFB;
                }
            }
        }
        public byte GMLevel { get; set; }
        public long ServerTime { get; set; }
        public string Name { get; set; }
        public string ClanName { get; set; }
        public float CharacterScaleOffset { get; set; }

        public override void Read(BinaryReader reader)
        {
            throw new NotImplementedException();
        }

        public override void Write(BinaryWriter writer)
        {
            base.Write(writer);

            writer.Write(CurrentVehicleCoid);
            writer.Write(CurrentTrailerCoid);

            writer.Write(HeadId);
            writer.Write(BodyId);
            writer.Write(AccessoryId1);
            writer.Write(AccessoryId2);
            writer.Write(HairId);
            writer.Write(MouthId);
            writer.Write(EyesId);
            writer.Write(HelmetId);
            writer.Write(PrimaryColor);
            writer.Write(SecondaryColor);
            writer.Write(EyesColor);
            writer.Write(HairColor);
            writer.Write(SkinColor);
            writer.Write(SpecialityColor);

            writer.Write(LastTownId);
            writer.Write(LastStationMapId);

            writer.Write(Level);
            writer.Write(Bf297);
            writer.Write(GMLevel);

            writer.BaseStream.Position += 5;

            writer.Write(ServerTime);

            writer.WriteUtf8StringOn(Name, 51);
            writer.WriteUtf8StringOn(ClanName, 51);

            writer.BaseStream.Position += 2;

            writer.Write(CharacterScaleOffset);

            writer.BaseStream.Position += 4;
        }
    }
}

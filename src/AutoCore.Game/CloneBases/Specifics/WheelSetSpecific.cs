using System.IO;

namespace AutoCore.Game.CloneBases.Specifics
{
    using Utils.Extensions;

    public struct WheelSetSpecific
    {
        public short[] Friction;
        public byte[] NumWheelsAxle;
        public string Wheel0Name;
        public string Wheel1Name;
        public byte WheelSetType;

        public static WheelSetSpecific ReadNew(BinaryReader reader)
        {
            var wss = new WheelSetSpecific
            {
                Friction = reader.ReadConstArray(6, reader.ReadInt16),
                NumWheelsAxle = reader.ReadBytes(2),
                WheelSetType = reader.ReadByte()
            };

            reader.BaseStream.Position += 1;

            wss.Wheel0Name = reader.ReadUTF16StringOn(65);
            wss.Wheel1Name = reader.ReadUTF16StringOn(65);

            return wss;
        }
    }
}

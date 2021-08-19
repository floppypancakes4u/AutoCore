using System.IO;

namespace AutoCore.Game.Packets
{
    using Constant;
    using Utils.Packets;

    public abstract class BasePacket : IOpcodedPacket<GameOpcode>
    {
        public abstract GameOpcode Opcode { get; }

        public abstract void Read(BinaryReader br);
        public abstract void Write(BinaryWriter bw);
    }
}

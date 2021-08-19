using System.IO;

namespace AutoCore.Game.Packets.Global
{
    using Constant;
    using Extensions;

    public class NewsPacket : BasePacket
    {
        public override GameOpcode Opcode => GameOpcode.News;
        public string News { get; }
        public uint Language { get; private set; }

        public NewsPacket(string news, uint language)
        {
            News = news;
            Language = language;
        }

        public override void Write(BinaryWriter writer)
        {
            writer.Write(Opcode);
            writer.Write(Language);
            writer.Write(News.Length + 1);
            writer.WriteUtf8NullString(News);
        }

        public override void Read(BinaryReader reader)
        {
            Language = reader.ReadUInt32();
            _ = reader.ReadUInt32();
        }
    }
}

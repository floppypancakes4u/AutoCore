using System.IO;
using System.Text;

namespace AutoCore.Game.Extensions
{
    using Constant;
    using Structures;

    public static class BinaryWriterExtensions
    {
        private static readonly byte[] TFIDPadding = new byte[7];

        public static void WriteUtf8NullString(this BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
        }

        public static void WriteUtf8StringOn(this BinaryWriter writer, string value, int len)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));

            for (var i = 0; i < len - value.Length; ++i)
                writer.Write((byte)0);
        }

        public static void WriteTFID(this BinaryWriter writer, long coid, bool global)
        {
            writer.Write(coid);
            writer.Write(global);
            writer.Write(TFIDPadding);
        }

        public static void WriteTFID(this BinaryWriter writer, TFID tfid)
        {
            writer.WriteTFID(tfid.Coid, tfid.Global);
        }

        public static void Write(this BinaryWriter writer, GameOpcode opcode)
        {
            writer.Write((uint)opcode);
        }
    }
}

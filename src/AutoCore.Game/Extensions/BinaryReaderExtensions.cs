using System.IO;

namespace AutoCore.Game.Extensions
{
    using Structures;

    public static class BinaryReaderExtensions
    {
        public static TFID ReadTFID(this BinaryReader reader)
        {
            var id = new TFID
            {
                Coid = reader.ReadInt64(),
                Global = reader.ReadBoolean()
            };

            reader.BaseStream.Position += 7;

            return id;
        }
    }
}

using System.IO;

namespace AutoCore.Game.Structures
{
    using Utils.Extensions;

    public struct HeadDetail
    {
        public int CloneBase;
        public int DisableHair;
        public string FileName;
        public int HeadBody;
        public int Id;
        public int MaxTextures;
        public byte Type;

        public static HeadDetail ReadNew(BinaryReader reader)
        {
            var hd = new HeadDetail
            {
                Id = reader.ReadInt32(),
                HeadBody = reader.ReadInt32(),
                CloneBase = reader.ReadInt32(),
                FileName = reader.ReadUTF16StringOn(65),
                Type = reader.ReadByte()
            };

            reader.ReadByte();

            hd.MaxTextures = reader.ReadInt32();
            hd.DisableHair = reader.ReadInt32();

            return hd;
        }

        public override string ToString()
        {
            return $"Id: {Id} | File: {FileName} ";
        }
    }
}

using System.IO;

namespace AutoCore.Game.Structures
{
    using Utils.Extensions;

    public struct HeadBody
    {
        public int CloneBase { get; set; }
        public string FileName { get; set; }
        public int Id { get; set; }
        public int IsBody { get; set; }
        public int IsHead { get; set; }
        public int MaxTextures { get; set; }

        public static HeadBody ReadNew(BinaryReader rename)
        {
            var hb = new HeadBody
            {
                Id = rename.ReadInt32(),
                CloneBase = rename.ReadInt32(),
                IsHead = rename.ReadInt32(),
                IsBody = rename.ReadInt32(),
                FileName = rename.ReadUTF16StringOn(65)
            };

            rename.ReadBytes(2);

            hb.MaxTextures = rename.ReadInt32();

            return hb;
        }

        public override string ToString()
        {
            return $"Id: {Id} | File: {FileName} ";
        }
    }
}

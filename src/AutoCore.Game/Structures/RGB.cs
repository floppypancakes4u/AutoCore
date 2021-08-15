using System.IO;

namespace AutoCore.Game.Structures
{
    public struct RGB
    {
        public float B;
        public float G;
        public float R;

        public static RGB Read(BinaryReader br)
        {
            return new RGB
            {
                R = br.ReadSingle(),
                G = br.ReadSingle(),
                B = br.ReadSingle()
            };
        }

        public override string ToString()
        {
            return $"R: {R} | G: {G} | B: {B}";
        }
    }
}

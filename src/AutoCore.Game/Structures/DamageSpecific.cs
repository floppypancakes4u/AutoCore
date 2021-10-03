using System.IO;

namespace AutoCore.Game.Structures
{
    using Utils.Extensions;

    public class DamageSpecific
    {
        public short[] Damage { get; set; }

        public void Read(BinaryReader reader)
        {
            Damage = reader.ReadConstArray(6, reader.ReadInt16);
        }

        public void Write(BinaryWriter writer)
        {
            writer.WriteConstArray(Damage, 6, writer.Write);
        }

        public static DamageSpecific ReadNew(BinaryReader reader)
        {
            return new DamageSpecific { Damage = reader.ReadConstArray(6, reader.ReadInt16) };
        }
    }
}

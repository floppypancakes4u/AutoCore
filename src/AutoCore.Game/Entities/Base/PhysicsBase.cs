using System.IO;

namespace AutoCore.Game.Entities.Base
{
    using Structures;

    public class PhysicsBase
    {
        public void UnSerialize(BinaryReader br, uint mapVersion)
        {
            var a = Vector4.Read(br);
            var b = Vector4.Read(br);
            var c = br.ReadSingle();
            var d = br.ReadSingle();
            var e = br.ReadByte();
        }
    }
}

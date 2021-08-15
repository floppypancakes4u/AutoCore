using System.IO;

namespace AutoCore.Game.Clonebase
{
    using Constant;
    using Structures.Specifics;

    public class CloneBase
    {
        public CloneBaseSpecific CloneBaseSpecific;

        public CloneBase(BinaryReader br)
        {
            CloneBaseSpecific = CloneBaseSpecific.Read(br);
        }

        public ObjectType Type => (ObjectType) CloneBaseSpecific.Type;
    }
}

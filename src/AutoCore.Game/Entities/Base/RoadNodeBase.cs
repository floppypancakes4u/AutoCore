using System.Collections.Generic;
using System.IO;

namespace AutoCore.Game.Entities.Base
{
    using Structures;
    using Utils.Extensions;

    public class RoadNodeBase
    {
        public string FileName;
        public List<int> NodeIds = new();
        public Vector3 Position;
        public uint UniqueId;

        public virtual void UnSerialize(BinaryReader br, uint mapVersion)
        {
            UniqueId = br.ReadUInt32();
            Position = Vector3.Read(br);
            FileName = br.ReadUtf8StringOn(260);

            var nodeCount = br.ReadUInt32();
            for (var i = 0; i < nodeCount; ++i)
                NodeIds.Add(br.ReadInt32());
        }
    }
}

using System.IO;

namespace AutoCore.Game.CloneBases.Specifics
{
    using Utils.Extensions;

    public struct CloneBaseSpecific
    {
        public uint Available { get; set; }
        public int BaseValue { get; set; }
        public int CloneBaseId { get; set; }
        public int CommodityGroupType { get; set; }
        public string FxFileName { get; set; }
        public uint InLootGenerator { get; set; }
        public uint InStores { get; set; }
        public uint IsGeneratable { get; set; }
        public bool IsSellable { get; set; }
        public uint IsTargetable { get; set; }
        public string LongDesc { get; set; }
        public string ShortDesc { get; set; }
        public int TilesetFlags { get; set; }
        public int Type { get; set; }
        public string UniqueName { get; set; }

        public static CloneBaseSpecific ReadNew(BinaryReader br)
        {
            return new CloneBaseSpecific
            {
                CloneBaseId = br.ReadInt32(),
                Type = br.ReadInt32(),
                TilesetFlags = br.ReadInt32(),
                UniqueName = br.ReadUTF16StringOn(65),
                ShortDesc = br.ReadUTF16StringOn(65),
                LongDesc = br.ReadUTF16StringOn(257),
                FxFileName = br.ReadUTF16StringOn(65),
                IsGeneratable = br.ReadUInt32(),
                IsTargetable = br.ReadUInt32(),
                Available = br.ReadUInt32(),
                InStores = br.ReadUInt32(),
                InLootGenerator = br.ReadUInt32(),
                BaseValue = br.ReadInt32(),
                CommodityGroupType = br.ReadInt32(),
                IsSellable = br.ReadUInt32() == 1
            };
        }
    }
}

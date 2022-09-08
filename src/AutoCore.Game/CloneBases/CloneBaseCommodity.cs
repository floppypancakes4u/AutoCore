namespace AutoCore.Game.CloneBases;

using AutoCore.Game.CloneBases.Specifics;

public class CloneBaseCommodity : CloneBaseObject
{
    public CommoditySpecific CommoditySpecific;

    public CloneBaseCommodity(BinaryReader reader)
        : base(reader)
    {
        CommoditySpecific = CommoditySpecific.ReadNew(reader);
    }
}

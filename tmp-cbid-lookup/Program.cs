using AutoCore.Game.CloneBases;
using AutoCore.Game.Managers.Asset;
var loader = new WADLoader();
loader.Load(args[0]);
foreach (var id in new[]{5468,5477}) {
  var cb = loader.CloneBases[id] as CloneBaseCommodity;
  var c = cb.CommoditySpecific;
  Console.WriteLine($""{id} {cb.CloneBaseSpecific.UniqueName} dropChance={c.DropChance} minLvl={c.MinLevel} maxLvl={c.MaxLevel} group={c.Group} groupType={c.CommodityGroupType}"");
}

using AutoCore.Game.CloneBases;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Constants;

var path = @"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad";
var loader = new WADLoader();
if (!loader.Load(path)) { Console.WriteLine("load failed"); return; }

var weapons = loader.CloneBases.Values.OfType<CloneBaseWeapon>().Take(30).ToList();
var sizeCounts = new Dictionary<int,int>();
var flagCounts = new Dictionary<string,int>();
var factionCounts = new Dictionary<int,int>();
var reqClassCounts = new Dictionary<int,int>();

foreach (var cb in loader.CloneBases.Values.OfType<CloneBaseWeapon>())
{
    var ts = (int)cb.WeaponSpecific.TurretSize;
    sizeCounts[ts] = sizeCounts.GetValueOrDefault(ts) + 1;
    var f = cb.WeaponSpecific.Flags;
    var pos = ((f & 0x02)!=0?"F":"") + ((f & 0x10)!=0?"T":"") + ((f & 0x04)!=0?"R":"") + (cb.WeaponSpecific.SubType==9?"M":"");
    if (string.IsNullOrEmpty(pos)) pos = "none";
    flagCounts[pos] = flagCounts.GetValueOrDefault(pos) + 1;
    var fac = cb.SimpleObjectSpecific.Faction;
    factionCounts[fac] = factionCounts.GetValueOrDefault(fac) + 1;
    var rc = cb.SimpleObjectSpecific.RequiredClass;
    reqClassCounts[rc] = reqClassCounts.GetValueOrDefault(rc) + 1;
}

Console.WriteLine("TurretSize: " + string.Join(", ", sizeCounts.OrderBy(kv=>kv.Key).Select(kv=>$"{kv.Key}={kv.Value}")));
Console.WriteLine("Pos flags: " + string.Join(", ", flagCounts.OrderByDescending(kv=>kv.Value).Select(kv=>$"{kv.Key}={kv.Value}")));
Console.WriteLine("Faction top: " + string.Join(", ", factionCounts.OrderByDescending(kv=>kv.Value).Take(20).Select(kv=>$"{kv.Key}={kv.Value}")));
Console.WriteLine("RequiredClass top: " + string.Join(", ", reqClassCounts.OrderByDescending(kv=>kv.Value).Take(20).Select(kv=>$"{kv.Key}={kv.Value}")));

// sample with name tags
foreach (var cb in loader.CloneBases.Values.OfType<CloneBaseWeapon>().Where(w => w.CloneBaseSpecific.UniqueName.Contains("human") || w.CloneBaseSpecific.UniqueName.Contains("mutant") || w.CloneBaseSpecific.UniqueName.Contains("biomek")).Take(12))
{
    var ws = cb.WeaponSpecific;
    var avg = (ws.DmgMinMin + ws.DmgMaxMax) / 2.0;
    var dps = ws.RechargeTime > 0 ? avg / (ws.RechargeTime / 1000.0) : avg;
    Console.WriteLine($"{cb.CloneBaseSpecific.UniqueName} size={ws.TurretSize} flags={ws.Flags} sub={ws.SubType} fac={cb.SimpleObjectSpecific.Faction} reqClass={cb.SimpleObjectSpecific.RequiredClass} dmg={ws.DmgMinMin}-{ws.DmgMaxMax} rech={ws.RechargeTime} approxDps={dps:F1}");
}

// correlate size tokens with TurretSize
var corr = new Dictionary<string, Dictionary<int,int>>();
foreach (var cb in loader.CloneBases.Values.OfType<CloneBaseWeapon>())
{
    var n = cb.CloneBaseSpecific.UniqueName ?? "";
    string token = n.Contains("_sm_") ? "sm" : n.Contains("_md_") ? "md" : n.Contains("_lg_") ? "lg" : "other";
    if (!corr.ContainsKey(token)) corr[token]=new();
    var ts=(int)cb.WeaponSpecific.TurretSize;
    corr[token][ts]=corr[token].GetValueOrDefault(ts)+1;
}
foreach (var kv in corr) Console.WriteLine($"name {kv.Key}: " + string.Join(", ", kv.Value.OrderBy(x=>x.Key).Select(x=>$"{x.Key}={x.Value}")));

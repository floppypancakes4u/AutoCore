using System;
using System.Linq;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Mission.Requirements;

var loader = new WADLoader();
// WADLoader needs AssetManager for XML extras; still loads binary mission fields without GLM.
// Patch: Mission.Read calls AssetManager.Instance.GetFileStreamFromGLMs - may NRE.
// Set up minimal path if needed.

try {
  loader.Load(@"C:\Program Files (x86)\NetDevil\Auto Assault\clonebase.wad");
} catch (Exception ex) {
  Console.WriteLine("Load partial: " + ex.Message);
}

foreach (var id in new[]{2945,2939,2941,2943,2947,2948,2949,2950,3240}) {
  if (!loader.Missions.TryGetValue(id, out var m)) { Console.WriteLine(id+" MISSING"); continue; }
  Console.WriteLine($"{m.Id} {m.Name}");
  Console.WriteLine($"  NPC={m.NPC} race={m.ReqRace} class={m.ReqClass} lvl={m.ReqLevelMin}-{m.ReqLevelMax}");
  Console.WriteLine($"  reqM=[{string.Join(",", m.ReqMissionId)}] Ored={m.RequirementsOred} Neg={m.RequirementsNegative} cont={m.Continent} auto={m.AutoAssign} rep={m.IsRepeatable}");
  Console.WriteLine($"  nObj={m.NumberOfObjectives} objectives={m.Objectives?.Count}");
  if (m.Objectives != null) {
    foreach (var kv in m.Objectives.OrderBy(x=>x.Key)) {
      var o = kv.Value;
      Console.WriteLine($"    seq={kv.Key} id={o.ObjectiveId} reqs={o.Requirements?.Count}");
      if (o.Requirements == null) continue;
      foreach (var r in o.Requirements) {
        if (r is ObjectiveRequirementDeliver d)
          Console.WriteLine($"      Deliver NPC={d.NPCTargetCBID} completes={d.NPCTargetCompletes}");
        else
          Console.WriteLine($"      {r.GetType().Name}");
      }
    }
  }
}

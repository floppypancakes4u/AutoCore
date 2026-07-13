using System;
using System.IO;
using System.Linq;
using AutoCore.Game.Constants;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Managers.Asset;
using AutoCore.Game.Map;

AssetManager.Instance.AllowMissingCBID = true;
AssetManager.Instance.Initialize(@"C:\Program Files (x86)\NetDevil\Auto Assault", ServerType.Sector);
AssetManager.Instance.LoadAllData();
var continents = WadXmlWorldDataLoader.LoadContinentObjects(Path.Combine(AssetManager.Instance.GamePath, "wad.xml"));

// Upside enter points - how does Back Range -> Upside spawn?
var up = continents[558];
var upMap = new MapData(up);
using (var fam = AssetManager.Instance.GetFileStreamFromGLMs(up.MapFileName + ".fam"))
using (var br = new BinaryReader(fam)) upMap.Read(br);
Console.WriteLine($"Upside Entry={upMap.EntryPoint}");
foreach (var ep in upMap.Templates.Values.OfType<EnterPointTemplate>())
  Console.WriteLine($"EP coid={ep.COID} type={ep.MapTransferType} data={ep.MapTransferData} loc=({ep.Location.X:0.#},{ep.Location.Y:0.#},{ep.Location.Z:0.#}) rot=({ep.Rotation.X:0.##},{ep.Rotation.Y:0.##},{ep.Rotation.Z:0.##},{ep.Rotation.W:0.##})");

// Ark Bay enter for backrange?
foreach (var id in new[]{707,426})
{
  if (!continents.ContainsKey(id)) continue;
  var c = continents[id];
  var md = new MapData(c);
  try {
    using var fam = AssetManager.Instance.GetFileStreamFromGLMs(c.MapFileName + ".fam");
    using var br = new BinaryReader(fam); md.Read(br);
  } catch (Exception ex) { Console.WriteLine($"{id} fail {ex.Message}"); continue; }
  Console.WriteLine($"Map {id} {c.DisplayName} {c.MapFileName}");
  foreach (var ep in md.Templates.Values.OfType<EnterPointTemplate>())
    Console.WriteLine($"  EP type={ep.MapTransferType} data={ep.MapTransferData} loc=({ep.Location.X:0.#},{ep.Location.Y:0.#},{ep.Location.Z:0.#})");
}

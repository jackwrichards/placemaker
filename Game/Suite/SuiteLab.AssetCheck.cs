// Game runner — does the catalog know these prefab names (P2-1: our own copies resolved by name). GPL-3.0.
//   {"do":"lab","op":"assetCheck","names":["Placemaker Slow Invisible Road Path - 2xTwoway"]}

using Newtonsoft.Json.Linq;
using Placemaker.Core;
using Placemaker.Game.Assets;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabAssetCheck(JObject step, JObject rec)
        {
            var cat = _world.GetOrCreateSystemManaged<AssetCatalogSystem>();
            var res = new JObject();
            foreach (var n in (step["names"] as JArray) ?? new JArray())
            {
                string name = (string)n;
                bool exists = cat.PrefabExists(name);
                var e = cat.Resolve(new AssetRef(name), (string)step["category"] ?? "");
                res[name] = new JObject { ["exists"] = exists, ["resolved"] = e.Index };
                Mod.Log.Info($"Placemaker lab: asset check '{name}': exists {exists}, resolves to entity {e.Index}; own names known {AssetCatalogSystem.OwnCount}.");
            }
            rec["assets"] = res;
            // what the current plan asks for its nets and stall objects (after automatic assets)
            var plan = _tool.Plan;
            if (plan != null)
            {
                var ids = new System.Collections.Generic.SortedSet<string>();
                foreach (var l in plan.Layers)
                {
                    if (l == null) continue;
                    foreach (var nn in l.Nets) ids.Add("net " + nn.Asset.Id);
                    foreach (var o in l.Objects) if (o.Role == ObjectRole.ParkingBay) ids.Add("bay " + o.Asset.Id);
                }
                Mod.Log.Info("Placemaker lab: asset check, the plan asks for: " + string.Join(" | ", ids));
            }
        }
    }
}

// Game runner — what the Manage panel knows about each car park (P2-1: fee, occupancy, the car park list). GPL-3.0.
//   {"do":"lab","op":"carParks","label":"after the build"}

using Newtonsoft.Json.Linq;
using Placemaker.Game.Economy;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        private void LabCarParks(JObject step, JObject rec)
        {
            string label = (string)step["label"] ?? "";
            var economy = _world.GetExistingSystemManaged<ParkingEconomySystem>();
            var groups = Services.Groups;
            if (economy == null || groups == null) { rec["error"] = "no parking economy or groups"; return; }
            var list = new JArray();
            foreach (int id in groups.AllGroups)
            {
                bool applies = economy.AppliesTo(id);
                bool fee = economy.TryFee(id, out int amount, out bool on);
                bool occ = economy.TryOccupancy(id, out int spaces, out int taken);
                list.Add(new JObject { ["id"] = id, ["carPark"] = applies, ["fee"] = fee ? amount : -1, ["feeOn"] = on, ["spaces"] = occ ? spaces : -1, ["taken"] = occ ? taken : -1 });
                Mod.Log.Info($"Placemaker lab: car parks {label}: group {id}: car park {applies}, fee feature {ParkingEconomySystem.FeatureEnabled}, fee {(fee ? amount.ToString() : "none")} (on {on}), occupancy {(occ ? taken + " of " + spaces : "none")}; managed {_tool.ManagedGroup}, stage {_tool.Stage}.");
            }
            rec["carParks"] = list;
        }
    }
}

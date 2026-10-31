// Placemaker — which of a type's controls the panel shows (one rule for the game and the mock host).
//
// Hidden, never deleted (the shelf rule for controls): a hidden control keeps its id, its saved values still load, and the
// planner still reads it. Three reasons a control is hidden:
//   - it only placed decals from decal packs (Decals.PackDecals);
//   - its feature was removed everywhere (animal spawns, the user 2026-10-30);
//   - it changed nothing the player could see in the plan (Phase 1 dead controls: Tests/ControlEffectTests lists every
//     shown control of every type and style and checks that each one changes the plan).

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public static class ControlsShown
    {
        /// <summary>(type, control) pairs hidden from the panel; "*" = every type.</summary>
        private static readonly Dictionary<string, string[]> Hidden = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Desire paths are painted only without decals (the decals draw the trodden lines) and changed nothing anywhere the
            // test planned; the paint still follows its default.
            ["*"] = new[] { Decals.DressingControls.DesirePathsId },
            // Animal spawns removed from every place (LayerContext.Object drops the spawners): their switches and counts.
            ["park"] = new[] { "animals" },
            ["wetland"] = new[] { "animals" },
            ["farmstead"] = new[] { "animals" },
            ["natureReserve"] = new[] { "animals" },
            ["hikingTrail"] = new[] { "animals" },
            ["animalPark"] = new[] { "spawnerAsset", "animals", "animalsPerPen" },
            // Phase 1 dead controls (Tests/ControlEffectTests): read by the plan, yet nothing changed in any style, on flat or
            // on sloped land by water. The malls' "pads" is never used at all (the user: "pads is off, but I see pads").
            ["stripMall"] = new[] { "pads", "pocketPlaza", "walkways", "bikes", "foodTrucks", "streetShops", "water" },
            ["mallDistrict"] = new[] { "pads", "pocketPlaza", "walkways", "bikes", "foodTrucks", "hedges", "streetShops", "parkingBlocks", "lotMedian" },
            ["mallPark"] = new[] { "pads" },
            ["retailPark"] = new[] { "pads" },
            // the designed squares lay out their own centre and seating
            ["plaza"] = new[] { "arrangementGap", "arrangementMode", "centerMode", "centerSize", "centerSpacing" },
            // ground paint that has no bare ground to act on (paved, built or terraced over)
            ["amphitheatre"] = new[] { "dressLawn", "dressMulch", "dressPaintWear" },
            ["fairground"] = new[] { "dressLawn", "dressMulch", "dressPaintWear", "layout" },
            ["marketSquare"] = new[] { "dressMulch", "dressPaintWear" },
            ["transitPlaza"] = new[] { "dressMulch", "dressPaintWear" },
            ["parking"] = new[] { "dressMulch", "dressPaintWear", "dressStyle" },
            ["constructionSite"] = new[] { "dressMulch" },
            ["zenGarden"] = new[] { "dressPaintWear" },
            ["abandonedLot"] = new[] { "desirePaths" },
            ["urbanRuins"] = new[] { "benches", "bowl", "lamps", "ledges", "skateItems" },
            ["carYard"] = new[] { "floodlights" },
            ["junkYard"] = new[] { "floodlights", "spoil" },
            ["campground"] = new[] { "facilities" },
            ["adventurePlayground"] = new[] { "sightlines" },
            ["golf"] = new[] { "teeBoxes" },
        };

        /// <summary>Every (type, control) pair hidden here (tests check each still exists).</summary>
        public static IEnumerable<(string Layer, string Control)> HiddenIds
        {
            get { foreach (var kv in Hidden) foreach (var c in kv.Value) yield return (kv.Key, c); }
        }

        /// <summary>Why a control is greyed out when the plan did not read it (UI StepView.disabled).</summary>
        public const string NotUsedKey = "Placemaker.UI.Why.NotUsed";

        /// <summary>
        /// The shown controls of a step that its latest plan never read: in this style, on this land, they change nothing, so
        /// the panel greys them out with <see cref="NotUsedKey"/> (the user: "pads is off but I see pads; small pads 0 → 8 changed
        /// nothing"). Look settings (read by the composer, not the layer) and markers are always in use. Empty when the plan
        /// recorded nothing for the step (an older plan, a layer outside LayerBase without a record).
        /// </summary>
        public static List<string> Unused(IAreaGenerator gen, ComposedPlan plan, string path)
        {
            var list = new List<string>();
            if (gen == null || plan?.SettingsRead == null || path == null || !plan.SettingsRead.TryGetValue(path, out var read) || read == null || read.Count == 0) return list;
            IReadOnlyList<ControlSchema> controls;
            try { controls = gen.Controls; } catch (Exception) { return list; }
            if (controls == null) return list;
            foreach (var c in controls)
            {
                if (c == null || read.Contains(c.Id) || !IsShown(gen, c)) continue;
                if (c.Kind == ControlKind.Markers) continue;
                if (Layers.Common.LookSettings.Find(gen, c.Id).look != null) continue;
                list.Add(c.Id);
            }
            return list;
        }

        public static bool IsShown(IAreaGenerator gen, ControlSchema c)
        {
            if (gen == null || c == null) return c != null;
            if (Decals.PackDecals.IsHiddenControl(gen.Id, c.Id)) return false;
            if (Hidden.TryGetValue(gen.Id, out var ids) && Array.IndexOf(ids, c.Id) >= 0) return false;
            if (Hidden.TryGetValue("*", out var all) && Array.IndexOf(all, c.Id) >= 0) return false;
            return true;
        }
    }
}

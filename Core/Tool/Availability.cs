// Placemaker — what the player can see but not use right now, and why (s15, playtest: "When I can't see
// options, I should see them greyed out. Right now I don't understand why I'm only seeing certain things").
//
// Two sources, both generic so any layer (and s14's street shops) can adopt them without UI code:
//   * value conditions live in the schema (ControlSchema.VisibleWhen / EnabledWhen / DisabledReasonKey) and
//     are evaluated by the UI as the values change;
//   * area conditions come from here, per step path: built-in rules (a feature whose layer needs more area
//     than the whole selection) plus IHasAvailability layers ("Needs a street edge"). Sent with the design
//     as StepView.disabled (control id → reason) and FeatureView.disabled.

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public static class Availability
    {
        /// <summary>"Needs a bigger area ({0} m²)".</summary>
        public const string AreaTooSmall = "Placemaker.Why.AreaTooSmall";

        /// <summary>Per step path: control id or "feature:&lt;layer&gt;" → reason. Steps without anything unavailable are left out.</summary>
        public static Dictionary<string, Dictionary<string, LocText>> For(Recipe recipe, AreaShape area, SiteContext site, Func<string, IAreaGenerator> generators)
        {
            var result = new Dictionary<string, Dictionary<string, LocText>>(StringComparer.Ordinal);
            if (recipe?.Root == null || generators == null) return result;
            double areaM2 = area?.Ring != null && area.Ring.Length >= 3 ? Pricing.PolygonArea(area.Ring) : -1;
            foreach (var kv in RecipePaths.Walk(recipe))
            {
                var step = kv.Value;
                if (step == null) continue;
                IAreaGenerator gen;
                try { gen = generators(step.Layer); } catch (Exception) { gen = null; }
                if (gen == null) continue;
                Dictionary<string, LocText> map = null;
                void Add(string id, LocText why)
                {
                    if (string.IsNullOrEmpty(id) || why == null) return;
                    map ??= new Dictionary<string, LocText>(StringComparer.Ordinal);
                    if (!map.ContainsKey(id)) map[id] = why;
                }

                // A feature that needs more room than the whole selection can never fit.
                if (areaM2 >= 0)
                    foreach (var f in Features.Of(gen))
                    {
                        if (f == null || string.IsNullOrEmpty(f.Layer)) continue;
                        IAreaGenerator fg;
                        try { fg = generators(f.Layer); } catch (Exception) { fg = null; }
                        float min;
                        try { min = fg?.MinArea ?? 0f; } catch (Exception) { min = 0f; }
                        if (min > 0f && areaM2 < min) Add(Unavailable.Feature(f.Layer), new LocText(AreaTooSmall, Math.Round(min)));
                    }

                if (gen is IHasAvailability ha && area?.Ring != null && area.Ring.Length >= 3)
                {
                    var list = new List<Unavailable>();
                    try
                    {
                        ha.Unavailable(new AreaInput
                        {
                            Shape = area,
                            Site = site ?? new SiteContext(),
                            Settings = step.Settings,
                            Markers = Composer.MarkersOn(step.Markers, area.Ring),
                            Seed = step.Seed,
                            RegionTags = new[] { RegionTags.Root },
                        }, list);
                    }
                    catch (Exception) { list.Clear(); }
                    foreach (var u in list)
                        if (u != null) Add(u.Id, new LocText(u.ReasonKey, u.ReasonArgs ?? Array.Empty<object>()));
                }
                if (map != null) result[kv.Key] = map;
            }
            return result;
        }
    }
}

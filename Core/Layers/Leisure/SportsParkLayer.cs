// Sports park (s10 redesign, playtest: "a grid of identical grey fenced courts… uninspired, needs more
// padding, a little more dynamic padding… like an artist painting it"; and "facilities controls AND
// park features for the same things").
//
// One concept for facilities: the sports park has no facility controls of its own. Its facilities are
// the shared field / amenity add-ons (AddOns/Fields: pitch, baseball diamond, running track, cricket
// ground, tennis courts, basketball court, skate bowl; amenities: volleyball, table tennis, fitness),
// offered as FEATURES with host options for how many and how big. The park composes them
// (SportsParkComposer): it carves a sized place for each one ("place:<id>" + SizedTag), and the add-on
// lays the field out inside. Old receipts with the retired "pitches / basketball / tennis…" settings are
// migrated into feature children on load (Migrate, via LayerAliases step migrations).

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.AddOns.Fields;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    [AreaGenerator(51, Group = "sports")]
    public sealed class SportsParkLayer : LeisureLayer
    {
        public const string LayerId = "sportsPark";

        /// <summary>Extra tag on the places the sports park sizes itself: the field takes the size of its place.</summary>
        public const string SizedTag = FieldAddOn.HostSizedTag;

        protected override string Area => "Sports";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/SportsParks.svg";
        public override float MinArea => 1200f;

        /// <summary>Big fields (a hero candidate each), then courts, then small amenities.</summary>
        public static readonly string[] BigFields = { "runningTrack", "cricketGround", "pitch", "baseballDiamond" };
        public static readonly string[] Courts = { "tennisCourts", "basketballCourt", "skateBowl" };
        public static readonly string[] Small = { "volleyball", "tableTennis", "fitness" };

        public static IEnumerable<string> Facilities()
        {
            foreach (var s in BigFields) yield return s;
            foreach (var s in Courts) yield return s;
            foreach (var s in Small) yield return s;
        }

        /// <summary>Feature id → host option ids (count, size) shown under its toggle.</summary>
        public static string[] OptionsOf(string layer)
        {
            switch (layer)
            {
                case "pitch": case "baseballDiamond": case "tennisCourts": return new[] { layer + "Count", layer + "Size" };
                case "runningTrack": case "cricketGround": return new[] { layer + "Size" };
                case "skateBowl": return Array.Empty<string>();
                default: return new[] { layer + "Count" };
            }
        }

        public override string[] Emits
        {
            get
            {
                var tags = new List<string> { RegionTags.Green, RegionTags.Core, StreetFrontage.ShopsTag, SizedTag };
                foreach (var f in Facilities()) tags.Add(Amenities.Place(f));
                return tags.ToArray();
            }
        }

        protected override string[] DefaultFeatures => new[] { "pitch", "tennisCourts", "basketballCourt", "playground", "decals" };

        protected override List<FeatureInfo> BuildFeatures()
        {
            var list = new List<FeatureInfo>();
            foreach (var f in Facilities())
                list.Add(new FeatureInfo
                {
                    Layer = f, Target = Amenities.Place(f), LabelKey = "Placemaker.Feature." + f, TooltipKey = "Placemaker.Feature." + f + ".Tooltip",
                    Options = OptionsOf(f),
                    // One table per spot: the park's "how many" is the number of spots (no second count).
                    Preset = f == "tableTennis" ? new SettingsBag { Values = { ["tables"] = 1.0 } } : new SettingsBag(),
                });
            // Kids' play and the toilets sit in the clubhouse yard (the "core" region next to the clubhouse).
            list.Add(CoreFeature("playground", ("size", "small")));
            list.Add(CoreFeature("restrooms"));
            list.Add(Feature("shops", ("count", 2.0), ("where", "entrances")));
            list.Add(StreetShopsFeature(Area));
            list.Add(Feature("decals", ("theme", "cracks"), ("ground", "auto")));
            return list;
        }

        public static readonly string[] Subtypes = { "community", "stadium", "tennisClub", "campus", "neighbourhood", "arena", "centreCourt", "hillside", "cageCourts", "athletics" };
        public static readonly string[] Heroes = { "auto", "pitch", "runningTrack", "baseballDiamond", "cricketGround", "tennisCourts", "none" };

        private static string[] WithAuto(FieldFormat[] f)
        {
            var r = new string[f.Length + 1];
            r[0] = "auto";
            for (int i = 0; i < f.Length; i++) r[i + 1] = f[i].Id;
            return r;
        }

        protected override void DefineControls(ControlSet c)
        {
            // Layout (essentials): what kind of park, which field is the centrepiece, how much room between things.
            c.Choice("subtype", "community", ControlTier.Essential, "layout", Subtypes).Tip();
            c.Choice("hero", "auto", ControlTier.Essential, "layout", Heroes).Tip();
            c.Slider("spacing", 100, 60, 200, 10, "%", ControlTier.Essential, "layout").Tip();
            c.Choice("fieldAxis", "edge", ControlTier.More, "layout", "edge", "northSouth", "eastWest").Tip();
            // (dynamic decals) How used the park looks: worn turf, mud, puddles, leaves, oil (code-made decals, Core/Decals/Dynamic).
            c.Slider(Placemaker.Core.Decals.Dynamic.Weathering.GrimeId, 100, 0, 300, 10, "%", ControlTier.Essential, "layout").Tip();

            // Facility features: how many and how big (shown under each feature's toggle).
            c.Slider("pitchCount", 1, 1, 4, 1, "count", ControlTier.More, "facilities");
            c.Choice("pitchSize", "auto", ControlTier.More, "facilities", WithAuto(FieldSpecs.Pitch)).Tip();
            c.Slider("baseballDiamondCount", 1, 1, 3, 1, "count", ControlTier.More, "facilities");
            c.Choice("baseballDiamondSize", "auto", ControlTier.More, "facilities", WithAuto(FieldSpecs.Baseball));
            c.Choice("runningTrackSize", "auto", ControlTier.More, "facilities", WithAuto(FieldSpecs.Track));
            c.Choice("cricketGroundSize", "auto", ControlTier.More, "facilities", WithAuto(FieldSpecs.Cricket));
            c.Slider("tennisCourtsCount", 1, 1, 4, 1, "count", ControlTier.More, "facilities").Tip();
            c.Choice("tennisCourtsSize", "two", ControlTier.More, "facilities", "four", "three", "two", "one").Tip();
            c.Choice("tennisSurface", "hard", ControlTier.More, "facilities", "hard", "clay", "grass").Tip();
            c.Slider("basketballCourtCount", 1, 1, 6, 1, "count", ControlTier.More, "facilities");
            c.Slider("volleyballCount", 1, 1, 4, 1, "count", ControlTier.More, "facilities");
            c.Slider("tableTennisCount", 2, 1, 6, 1, "count", ControlTier.More, "facilities");
            c.Slider("fitnessCount", 1, 1, 3, 1, "count", ControlTier.More, "facilities");
            // P2-4 (the lead: a 15 ha park was a few pitches in a wood): on a big site the counts above multiply
            c.Toggle("growWithSite", true, ControlTier.More, "facilities").Tip();

            // Clubhouse: changing pavilion with a spectator terrace beside the main field.
            c.Toggle("changingHut", true, ControlTier.More, "clubhouse").Tip();
            c.Toggle("terrace", true, ControlTier.More, "clubhouse").Tip();
            c.Toggle("kiosk", true, ControlTier.More, "clubhouse").Tip();

            // Paths.
            c.Choice("promenade", "both", ControlTier.More, "paths", "loop", "spine", "both").Tip();
            c.Toggle("entrancePlaza", true, ControlTier.More, "paths").Tip();
            c.Slider("bikeRacks", 6, 0, 20, 1, "count", ControlTier.More, "paths").Tip();
            c.Toggle("lamps", true, ControlTier.More, "paths");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "paths");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "paths");

            // Landscape.
            c.Choice("treeBelt", "single", ControlTier.More, "landscape", "none", "single", "double").Tip();
            c.Slider("treeDensity", 25, 0, 100, 5, "perHa", ControlTier.More, "landscape").Tip();
            c.Toggle("berms", true, ControlTier.More, "landscape").Tip();
            c.Toggle("fence", false, ControlTier.More, "landscape");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "landscape").When("fence=true");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "landscape");

            // Parking: only a tidy lot on the street side of a large park.
            c.Choice("parking", "auto", ControlTier.More, "parking", "auto", "off", "on").Tip();
            c.Slider("lotShare", 8, 3, 20, 1, "%", ControlTier.More, "parking").Tip();

            EntranceMarkers(c);
            DefineStreetShopControls(c);
            foreach (var id in new[] { "subtype", "hero", "fieldAxis", "tennisSurface", "pitchSize", "baseballDiamondSize", "runningTrackSize", "cricketGroundSize", "tennisCourtsSize", "parking", "promenade" })
                c.Fixed(id);
            c.RandomRange("spacing", 80, 140);
            c.RandomRange("treeDensity", 15, 45);
        }

        protected override void DefinePresets()
        {
            Preset("community", ("subtype", "community"), ("hero", "auto"), ("pitchCount", 1.0), ("pitchSize", "auto"), ("tennisCourtsCount", 1.0),
                ("tennisCourtsSize", "two"), ("basketballCourtCount", 1.0), ("promenade", "both"), ("treeBelt", "single"), ("berms", true), ("spacing", 100.0));
            PresetFeatures("community", "fitness");
            Preset("stadium", ("subtype", "stadium"), ("hero", "runningTrack"), ("runningTrackSize", "auto"), ("pitchCount", 1.0), ("pitchSize", "seven"),
                ("treeBelt", "single"), ("berms", true), ("parking", "auto"), ("bikeRacks", 14.0), ("spacing", 95.0), ("promenade", "both"), ("kiosk", true));
            PresetFeatures("stadium", "runningTrack", "fitness", "-tennisCourts", "-basketballCourt", "-playground");
            Preset("tennisClub", ("subtype", "tennisClub"), ("hero", "tennisCourts"), ("tennisCourtsCount", 3.0), ("tennisCourtsSize", "two"),
                ("promenade", "spine"), ("treeBelt", "single"), ("berms", false), ("treeDensity", 35.0), ("spacing", 90.0), ("fence", true), ("tennisSurface", "clay"));
            PresetFeatures("tennisClub", "tableTennis", "-pitch", "-basketballCourt");
            Preset("campus", ("subtype", "campus"), ("hero", "pitch"), ("pitchCount", 1.0), ("pitchSize", "auto"), ("baseballDiamondCount", 1.0),
                ("tennisCourtsCount", 1.0), ("tennisCourtsSize", "two"), ("basketballCourtCount", 2.0), ("volleyballCount", 2.0),
                ("treeBelt", "double"), ("berms", true), ("parking", "on"), ("spacing", 110.0), ("promenade", "both"));
            PresetFeatures("campus", "baseballDiamond", "volleyball", "skateBowl", "fitness");
            Preset("neighbourhood", ("subtype", "neighbourhood"), ("hero", "none"), ("basketballCourtCount", 2.0), ("tennisCourtsCount", 1.0),
                ("tennisCourtsSize", "one"), ("tableTennisCount", 2.0), ("volleyballCount", 1.0), ("promenade", "loop"), ("treeBelt", "single"),
                ("treeDensity", 45.0), ("berms", false), ("parking", "off"), ("spacing", 85.0), ("changingHut", true), ("terrace", false));
            PresetFeatures("neighbourhood", "-pitch", "tableTennis", "volleyball", "skateBowl", "fitness");

            // 2026-10-29: designed sports grounds on their own ground (SportsDesigns).
            Preset("arena", ("subtype", "arena"), ("hero", "pitch"), ("pitchSize", "auto"), ("parking", "off"), ("treeBelt", "single"), ("berms", false));
            PresetFeatures("arena", "-tennisCourts", "-basketballCourt", "-playground", "-decals");
            Preset("centreCourt", ("subtype", "centreCourt"), ("hero", "tennisCourts"), ("parking", "off"), ("tennisSurface", "grass"), ("berms", false));
            PresetFeatures("centreCourt", "-pitch", "-basketballCourt", "-playground", "-decals");
            Preset("hillside", ("subtype", "hillside"), ("hero", "pitch"), ("parking", "off"), ("berms", false));
            PresetFeatures("hillside", "skateBowl", "-playground", "-decals");
            Preset("cageCourts", ("subtype", "cageCourts"), ("hero", "none"), ("parking", "off"), ("berms", false), ("pitchSize", "five"));
            PresetFeatures("cageCourts", "-tennisCourts", "-playground");
            Preset("athletics", ("subtype", "athletics"), ("hero", "runningTrack"), ("parking", "off"), ("berms", false));
            PresetFeatures("athletics", "runningTrack", "-pitch", "-tennisCourts", "-basketballCourt", "-playground", "-decals");
        }

        protected override void PlanCore(LayerContext ctx)
        {
            // 2026-10-29: a designed sports ground (stadium bowl, centre court, terraces, cages, athletics) when the site takes it.
            string subtype = ctx.S("subtype");
            if (SportsDesigns.Handles(subtype) && SportsDesigns.Plan(ctx, Gates(ctx), subtype, Placemaker.Core.Layers.Plaza.PlazaDressing.SquareAxis(ctx.LayerRegion)))
            {
                CommonStats(ctx);
                return;
            }
            new SportsParkComposer(ctx, Gates(ctx)).Run();
            CommonStats(ctx);
        }

        // ───────────── migration of the retired facility controls ─────────────

        /// <summary>
        /// Rewrites a loaded sports park step from the retired facility controls (pitches, basketball,
        /// tennis, fitness, pitchSize small/full) to feature children with host counts, and moves field
        /// children that used to find their own spot into their places. Idempotent.
        /// </summary>
        public static int Migrate(RecipeStep step)
        {
            if (step == null || step.Layer != LayerId) return 0;
            int n = 0;
            var v = step.Settings.Values;
            void Child(string layer)
            {
                string target = Amenities.Place(layer);
                foreach (var ch in step.Children)
                    if (ch != null && ch.Layer == layer && (ch.Target ?? "") == target) { ch.Enabled = true; return; }
                var gen = GeneratorRegistry.Get(layer);
                step.Children.Add(new RecipeStep
                {
                    Layer = layer, Target = target, Seed = Hashing.Mix(step.Seed, Seeds.Hash(layer)), Settings = gen?.Defaults() ?? new SettingsBag(),
                });
            }
            // Field children planned before the redesign found their own spot (Target ""): they now fill a place.
            foreach (var ch in step.Children)
                if (ch != null && string.IsNullOrEmpty(ch.Target))
                    foreach (var f in Facilities())
                        if (ch.Layer == f) { ch.Target = Amenities.Place(f); n++; }
            if (v.TryGetValue("pitches", out var p))
            {
                int k = (int)math.round(Convert.ToDouble(p));
                if (k > 0) { Child("pitch"); v["pitchCount"] = (double)math.clamp(k, 1, 4); }
                v.Remove("pitches"); n++;
            }
            if (v.TryGetValue("pitchSize", out var ps) && ps is string size && size == "small") { v["pitchSize"] = "seven"; n++; }
            if (v.TryGetValue("tennis", out var t))
            {
                int k = (int)math.round(Convert.ToDouble(t));
                if (k > 0)
                {
                    Child("tennisCourts");
                    int per = math.min(4, k);
                    v["tennisCourtsSize"] = per >= 4 ? "four" : per == 3 ? "three" : per == 2 ? "two" : "one";
                    v["tennisCourtsCount"] = (double)math.clamp((k + 3) / 4, 1, 4);
                }
                v.Remove("tennis"); n++;
            }
            if (v.TryGetValue("basketball", out var b))
            {
                int k = (int)math.round(Convert.ToDouble(b));
                if (k > 0) { Child("basketballCourt"); v["basketballCourtCount"] = (double)math.clamp(k, 1, 6); }
                v.Remove("basketball"); n++;
            }
            if (v.TryGetValue("fitness", out var fit))
            {
                if (fit is bool on && on) Child("fitness");
                v.Remove("fitness"); n++;
            }
            foreach (var gone in new[] { "stands", "floodlights", "markings", "cages", "courtSurface", "pitchSurface" })
                if (v.Remove(gone)) n++;
            foreach (var id in new[] { "pitches", "tennis", "basketball", "fitness" }) step.Locked.Remove(id);
            return n;
        }
    }
}

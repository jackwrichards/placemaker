// Placemaker — park stats: what a vanilla park of this size and content would have (GPL-3.0).
//
// The game gives parks their effects through components of the park BUILDING prefab (docs/PARK-STATS.md):
//   Attraction.m_Attractiveness  → tourism (TourismSystem: city attractiveness from Σ a²/10000) and land value,
//   LeisureProvider.m_Efficiency → how much leisure a visit gives (LeisureSystem),
//   ServiceCoverage range/capacity/magnitude → park "entertainment" coverage → wellbeing (CitizenHappinessSystem),
//   ServiceConsumption.m_Upkeep  → monthly cost.
// Vanilla prices these by tier: they all follow the park's size and how "premium" it is. This estimator
// reproduces that from a composed plan (area, trees, water, features…), calibrated to VanillaParks, and keeps
// the result inside the range vanilla parks of a comparable size use. Pure and deterministic (engine-free).

using System;
using System.Collections.Generic;
using Placemaker.Core.Tool;

namespace Placemaker.Core.Build
{
    public static class ParkStatIds
    {
        public const string Attractiveness = "attractiveness";
        public const string Leisure = "leisure";
        public const string Range = "coverageRange";
        public const string Capacity = "coverageCapacity";
        public const string Entertainment = "entertainment";
        public const string Upkeep = "upkeep";

        /// <summary>ParkStatsRecord.Applied key: attractiveness as set on the owner lot (after the terrain bonus), absent = not applied.</summary>
        public const string InGameAttractiveness = "attractiveness.inGame";
        /// <summary>ParkStatsRecord.Applied key: 1 once the companion park building exists (it carries every stat).</summary>
        public const string Companion = "companion";

        /// <summary>Display order. Save format: never rename an id.</summary>
        public static readonly string[] All = { Attractiveness, Leisure, Range, Capacity, Entertainment, Upkeep };
    }

    /// <summary>Kind of build for park stats; drives the type multiplier and which vanilla parks it is compared with.</summary>
    public enum ParkStatsKind { NotApplicable = 0, Park = 1, Plaza = 2, Playground = 3, Sports = 4, Nature = 5 }

    /// <summary>The player's choices for one build (kept with the recipe and the build's receipt).</summary>
    public sealed class ParkStatsSettings
    {
        /// <summary>Clamp every value to the range vanilla parks of a comparable size use (default on).</summary>
        public bool KeepVanilla = true;
        /// <summary>Stat id → value the player set. Missing = suggested.</summary>
        public Dictionary<string, double> Overrides = new Dictionary<string, double>(StringComparer.Ordinal);

        public bool IsDefault => KeepVanilla && Overrides.Count == 0;

        public ParkStatsSettings Clone()
        {
            var c = new ParkStatsSettings { KeepVanilla = KeepVanilla };
            foreach (var kv in Overrides) c.Overrides[kv.Key] = kv.Value;
            return c;
        }

        public bool ContentEquals(ParkStatsSettings o)
        {
            if (o == null) return IsDefault;
            if (o.KeepVanilla != KeepVanilla || o.Overrides.Count != Overrides.Count) return false;
            foreach (var kv in Overrides)
                if (!o.Overrides.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }

    /// <summary>Park stats stored with a build (GroupRecord.ParkStats, receipt v2).</summary>
    public sealed class ParkStatsRecord
    {
        public ParkStatsSettings Settings = new ParkStatsSettings();
        /// <summary>Stat id → value the build got at build time (ParkStatIds; all stats, applied in game or not).</summary>
        public Dictionary<string, double> Applied = new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>(int)ParkStatsKind and (int)LeisureKind at build time.</summary>
        public int Kind, LeisureType;

        public static ParkStatsRecord Of(ParkStatsResult r, ParkStatsSettings settings)
        {
            var rec = new ParkStatsRecord { Settings = settings?.Clone() ?? new ParkStatsSettings(), Kind = (int)r.Kind, LeisureType = (int)r.LeisureType };
            foreach (var s in r.Stats) rec.Applied[s.Id] = s.Value;
            return rec;
        }
    }

    /// <summary>What the estimator looks at, extracted from a composed plan (or written by tests).</summary>
    public sealed class ParkStatsInput
    {
        public ParkStatsKind Kind = ParkStatsKind.Park;
        public double AreaM2;
        public int Trees, Greenery, Benches, Centerpieces, Amenities, PlayItems, SportsItems;
        public double WaterM2, PavedM2;
        /// <summary>Beach / waterfront types provide "beach" leisure like vanilla pools and water parks.</summary>
        public bool Beach;

        /// <summary>Counts what a composed plan contains. Null recipe or plan = not applicable.</summary>
        public static ParkStatsInput From(ComposedPlan plan, double areaM2, Recipe recipe)
        {
            var input = new ParkStatsInput { AreaM2 = Math.Max(0, areaM2) };
            string group;
            input.Kind = KindOf(recipe, out group);
            input.Beach = group == "water";
            if (plan == null) return input;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                foreach (var o in l.Objects)
                {
                    if (o == null) continue;
                    Count(input, o.Role, o.Category ?? "");
                }
                foreach (var s in l.Surfaces)
                {
                    if (s?.Ring == null || s.Ring.Length < 3) continue;
                    double a = Pricing.PolygonArea(s.Ring);
                    if (s.Role == SurfaceRole.Water || s.Category == AssetCategories.SurfaceWater) input.WaterM2 += a;
                    else if (s.Role == SurfaceRole.Paving || s.Role == SurfaceRole.Plaza || s.Role == SurfaceRole.Asphalt) input.PavedM2 += a;
                }
                foreach (var t in l.Terraform)
                    if (t?.Ring != null && t.Ring.Length >= 3 && !float.IsNaN(t.WaterLevel)) input.WaterM2 += Pricing.PolygonArea(t.Ring);
            }
            return input;
        }

        private static void Count(ParkStatsInput input, ObjectRole role, string cat)
        {
            switch (role)
            {
                case ObjectRole.Decal: case ObjectRole.Marker: case ObjectRole.AnimalSpawner: case ObjectRole.AccessPoint:
                case ObjectRole.ParkingBay: case ObjectRole.Charger: case ObjectRole.Arrow: case ObjectRole.CulDeSacCap:
                case ObjectRole.RoundaboutIsland: case ObjectRole.Lamp: case ObjectRole.Bin: case ObjectRole.FencePiece:
                    return;
                case ObjectRole.Tree: input.Trees++; return;
                case ObjectRole.Bush: case ObjectRole.Planter: input.Greenery++; return;
                case ObjectRole.Bench: input.Benches++; return;
                case ObjectRole.Centerpiece: input.Centerpieces++; return;
            }
            if (Starts(cat, AssetCategories.Tree)) input.Trees++;
            else if (Starts(cat, AssetCategories.Bush) || Starts(cat, AssetCategories.Planter) || Starts(cat, AssetCategories.Flowerbed)
                || Starts(cat, AssetCategories.Hedge) || cat == AssetCategories.GardenCrop) input.Greenery++;
            else if (Starts(cat, AssetCategories.Centerpiece) || cat == AssetCategories.WaterJets || cat == AssetCategories.Lookout) input.Centerpieces++;
            else if (Starts(cat, AssetCategories.Playground) || Starts(cat, "dog")) input.PlayItems++;
            else if (Starts(cat, AssetCategories.Sports)) input.SportsItems++;
            else if (cat == AssetCategories.Bench || cat == AssetCategories.CafeSeating || cat == AssetCategories.Picnic) input.Benches++;
            else if (Starts(cat, AssetCategories.Restroom) || Starts(cat, AssetCategories.Kiosk) || Starts(cat, AssetCategories.Shelter)
                || Starts(cat, AssetCategories.Stage) || Starts(cat, AssetCategories.Camping) || cat == AssetCategories.Glasshouse) input.Amenities++;
        }

        private static bool Starts(string cat, string prefix) =>
            cat.Length >= prefix.Length && string.CompareOrdinal(cat, 0, prefix, 0, prefix.Length) == 0
            && (cat.Length == prefix.Length || cat[prefix.Length] == '.');

        /// <summary>
        /// Park-stats kind of a recipe from its first fill layer: industrial, parking and road types (and loose
        /// builds) are not parks. <paramref name="group"/> = the layer's Make-row group.
        /// </summary>
        public static ParkStatsKind KindOf(Recipe recipe, out string group)
        {
            group = "";
            if (recipe?.Root == null) return ParkStatsKind.NotApplicable;
            string layer = FirstFill(recipe.Root, 0);
            if (string.IsNullOrEmpty(layer)) return ParkStatsKind.NotApplicable;
            var gen = GeneratorRegistry.Get(layer);
            if (gen != null && GeneratorRegistry.IsLoose(layer)) return ParkStatsKind.NotApplicable;
            group = gen != null ? GeneratorGroups.Of(gen) : "";
            switch (layer)
            {
                case "plaza": return ParkStatsKind.Plaza;
                case "park": return ParkStatsKind.Park;
                case "campground": return ParkStatsKind.Nature;
                case "adventurePlayground": return ParkStatsKind.Playground;
            }
            switch (group)
            {
                case "industrial": case "parking": case "roads": case "commercial": case "zoning": return ParkStatsKind.NotApplicable;
                case "squares": case "transport": return ParkStatsKind.Plaza;
                case "sports": return ParkStatsKind.Sports;
                case "nature": return ParkStatsKind.Nature;
                default: return gen != null ? ParkStatsKind.Park : ParkStatsKind.NotApplicable;
            }
        }

        private static string FirstFill(RecipeStep s, int depth)
        {
            if (s == null || depth > Composer.MaxDepth) return null;
            var gen = GeneratorRegistry.Get(s.Layer);
            if (gen != null && gen.Kind == LayerKind.Fill && s.Enabled) return s.Layer;
            foreach (var c in s.Children)
            {
                var r = FirstFill(c, depth + 1);
                if (r != null) return r;
            }
            return null;
        }
    }

    /// <summary>One stat of the result.</summary>
    public sealed class ParkStatEstimate
    {
        public string Id = "";
        /// <summary>The estimator's value, already inside the vanilla range.</summary>
        public double Suggested;
        /// <summary>What the build gets: override or suggested, clamped to the vanilla range when KeepVanilla.</summary>
        public double Value;
        /// <summary>Range vanilla parks of a comparable size use (scaled to this size).</summary>
        public double BandMin, BandMax;
        /// <summary>Slider limits when the vanilla range is off.</summary>
        public double Min, Max, Step;
        /// <summary>"count", "money" or "" (plain number; coverage range is a pedestrian path cost, roughly metres walked).</summary>
        public string Unit = "";
        /// <summary>True when the game uses this value for the build today (see docs/PARK-STATS.md).</summary>
        public bool Applied;
        public bool Overridden;
        /// <summary>The player's (or the raw) value was outside the vanilla range and got clamped.</summary>
        public bool Clamped;
        /// <summary>How it is calculated, one line each.</summary>
        public List<LocText> Formula = new List<LocText>();
        /// <summary>Comparable vanilla parks and their real value for this stat.</summary>
        public List<KeyValuePair<VanillaPark, double>> Similar = new List<KeyValuePair<VanillaPark, double>>();
    }

    /// <summary>(s17) Does the build get a companion park building (all stats real) or only lot attractiveness?</summary>
    /// <summary>Ungrouped (the Review switch "Group as one asset" off): no park building, so no upkeep and no park stats (the
    /// user, 26 Oct: a Placemaker park acts like one vanilla park; in pieces it is not one).</summary>
    public enum ParkCompanionState { Off = 0, Yes = 1, NoRoad = 2, Ungrouped = 3 }

    public sealed class ParkStatsResult
    {
        public bool Applicable;
        /// <summary>Companion building state (set by <see cref="ParkStatsEstimator.WithCompanion"/>; Off = lot attractiveness only).</summary>
        public ParkCompanionState Companion;
        public ParkCompanionSite Site;
        public ParkStatsKind Kind;
        public LeisureKind LeisureType = LeisureKind.CityPark;
        public bool KeepVanilla = true;
        public double AreaM2;
        /// <summary>Quality multiplier (0.55 … 2) and its parts.</summary>
        public double Quality, TreeScore, WaterScore, LandmarkScore, AmenityScore, ActivityScore, SeatScore;
        public List<ParkStatEstimate> Stats = new List<ParkStatEstimate>();

        public ParkStatEstimate Get(string id)
        {
            foreach (var s in Stats) if (s.Id == id) return s;
            return null;
        }

        public double ValueOf(string id, double fallback = 0) => Get(id)?.Value ?? fallback;
    }

    public static class ParkStatsEstimator
    {
        // Size base, fitted to the generic vanilla parks (Small Park … Community City Park): attractiveness ≈ 1.9 × (A/1000 m²)^0.85.
        public const double SizeFactor = 1.9, SizeExponent = 0.85;
        public const double QualityStart = 0.55;
        /// <summary>Highest suggested attractiveness (vanilla: Grand City Plaza 99; uniques 50–175).</summary>
        public const double MaxSuggestedAttractiveness = 100;

        /// <summary>Size scaling exponent per stat when comparing with vanilla parks of another size.</summary>
        private static double ScaleExponent(string id)
        {
            switch (id)
            {
                case ParkStatIds.Attractiveness: case ParkStatIds.Leisure: case ParkStatIds.Upkeep: return SizeExponent;
                case ParkStatIds.Capacity: return 0.5;
                case ParkStatIds.Range: return 0.25;
                default: return 0;
            }
        }

        private static void Limits(string id, out double min, out double max, out double step, out string unit)
        {
            switch (id)
            {
                case ParkStatIds.Attractiveness: min = 0; max = 500; step = 1; unit = "count"; return;
                case ParkStatIds.Leisure: min = 0; max = 300; step = 1; unit = "count"; return;
                case ParkStatIds.Range: min = 250; max = 20000; step = 50; unit = ""; return;   // pedestrian path cost, roughly metres walked
                case ParkStatIds.Capacity: min = 100; max = 50000; step = 50; unit = "count"; return;
                case ParkStatIds.Entertainment: min = 1; max = 30; step = 0.5; unit = ""; return;
                case ParkStatIds.Upkeep: min = 0; max = 500000; step = 50; unit = "money"; return;
                default: min = 0; max = 1; step = 1; unit = ""; return;
            }
        }

        public static double SizeBase(double areaM2) => SizeFactor * Math.Pow(Math.Max(0, areaM2) / 1000.0, SizeExponent);

        /// <summary>Below 1000 m² vanilla pocket parks reach less far and serve fewer people.</summary>
        public static double SmallFactor(double areaM2) => areaM2 >= 1000 ? 1 : Math.Max(0.3, Math.Sqrt(Math.Max(0, areaM2) / 1000.0));

        public static double KindMultiplier(ParkStatsKind kind)
        {
            switch (kind)
            {
                case ParkStatsKind.Plaza: return 1.25;
                case ParkStatsKind.Nature: return 0.85;
                default: return 1;
            }
        }

        public static ParkStatsResult Estimate(ParkStatsInput input, ParkStatsSettings settings = null)
        {
            settings = settings ?? new ParkStatsSettings();
            var r = new ParkStatsResult { Kind = input?.Kind ?? ParkStatsKind.NotApplicable, KeepVanilla = settings.KeepVanilla };
            if (input == null || input.Kind == ParkStatsKind.NotApplicable || !(input.AreaM2 > 1)) return r;
            r.Applicable = true;
            double area = input.AreaM2, ha = area / 10000.0;
            r.AreaM2 = area;
            r.LeisureType = input.Beach || input.WaterM2 / area >= 0.25 ? LeisureKind.CityBeach : LeisureKind.CityPark;

            // Quality: what makes vanilla's premium parks premium — dense trees, water, a landmark, things to do.
            r.TreeScore = Sat((input.Trees + 0.3 * input.Greenery) / Math.Max(1, ha * 50));
            if (input.Kind == ParkStatsKind.Plaza) r.TreeScore = Math.Max(r.TreeScore, Sat(input.PavedM2 / area / 0.6));
            r.WaterScore = Sat(input.WaterM2 / area / 0.15);
            r.LandmarkScore = Sat(input.Centerpieces / Math.Max(1, ha));
            r.AmenityScore = Sat(input.Amenities / Math.Max(2, ha * 4));
            r.ActivityScore = Sat((input.PlayItems + input.SportsItems) / Math.Max(3, ha * 10));
            if (input.Kind == ParkStatsKind.Sports || input.Kind == ParkStatsKind.Playground) r.ActivityScore = Math.Max(r.ActivityScore, 0.7);
            r.SeatScore = Sat(input.Benches / Math.Max(4, ha * 15));
            double tq = 0.35 * r.TreeScore, wq = 0.3 * r.WaterScore, lq = 0.25 * r.LandmarkScore, aq = 0.2 * r.AmenityScore,
                pq = 0.2 * r.ActivityScore, sq = 0.15 * r.SeatScore;
            r.Quality = QualityStart + tq + wq + lq + aq + pq + sq;
            double kindMul = KindMultiplier(input.Kind);
            double sizeBase = SizeBase(area);
            double small = SmallFactor(area);

            // Attractiveness drives the rest, like vanilla's tiers.
            double rawAttr = Math.Max(1, Math.Round(Math.Min(MaxSuggestedAttractiveness, sizeBase * r.Quality * kindMul)));
            var attr = Make(ParkStatIds.Attractiveness, rawAttr, input, settings, true);
            attr.Formula.Add(new LocText("Placemaker.ParkStats.F.Size", Math.Round(area), R1(sizeBase)));
            attr.Formula.Add(new LocText("Placemaker.ParkStats.F.Quality", R2(r.Quality), R2(tq), R2(wq), R2(lq), R2(aq), R2(pq), R2(sq)));
            attr.Formula.Add(new LocText("Placemaker.ParkStats.F.Type", R2(kindMul), rawAttr));
            attr.Formula.Add(new LocText("Placemaker.ParkStats.F.AttrGame"));
            r.Stats.Add(attr);
            double a = attr.Suggested;

            double activity = r.ActivityScore;
            double rawLeisure = Math.Max(1, Math.Round(a * (0.33 + 0.67 * activity)));
            var leisure = Make(ParkStatIds.Leisure, rawLeisure, input, settings, false);
            leisure.Formula.Add(new LocText("Placemaker.ParkStats.F.Leisure", a, R2(activity), rawLeisure));
            leisure.Formula.Add(new LocText("Placemaker.ParkStats.F.LeisureGame"));
            r.Stats.Add(leisure);

            double rawRange = Round(Math.Min(6000, 2200 + 150 * a) * small, 50);
            var range = Make(ParkStatIds.Range, rawRange, input, settings, false);
            range.Formula.Add(new LocText("Placemaker.ParkStats.F.Range", a, R2(small), rawRange));
            range.Formula.Add(new LocText("Placemaker.ParkStats.F.CoverageGame"));
            r.Stats.Add(range);

            double rawCapacity = Round((700 + 90 * a) * small, 50);
            var capacity = Make(ParkStatIds.Capacity, rawCapacity, input, settings, false);
            capacity.Formula.Add(new LocText("Placemaker.ParkStats.F.Capacity", a, R2(small), rawCapacity));
            capacity.Formula.Add(new LocText("Placemaker.ParkStats.F.CapacityGame"));
            r.Stats.Add(capacity);

            double bonus = input.Kind == ParkStatsKind.Sports ? 2 : 0;
            double rawMag = area < 200 ? 8 : Math.Round(Clamp(9 + 0.28 * a + bonus, 8, 16) * 2) / 2;
            var mag = Make(ParkStatIds.Entertainment, rawMag, input, settings, false);
            mag.Formula.Add(new LocText("Placemaker.ParkStats.F.Entertainment", a, bonus, rawMag));
            mag.Formula.Add(new LocText("Placemaker.ParkStats.F.EntertainmentGame"));
            r.Stats.Add(mag);

            double upkeepScale = area >= 1000 ? 1 : Math.Max(0.2, area / 1000.0);
            double rawUpkeep = Round((1500 + 650 * a) * upkeepScale, 50);
            var upkeep = Make(ParkStatIds.Upkeep, rawUpkeep, input, settings, false);
            upkeep.Formula.Add(new LocText("Placemaker.ParkStats.F.Upkeep", a, R2(upkeepScale), rawUpkeep));
            upkeep.Formula.Add(new LocText("Placemaker.ParkStats.F.UpkeepGame"));
            r.Stats.Add(upkeep);
            foreach (var st in r.Stats) st.Formula.Add(new LocText("Placemaker.ParkStats.F.Band", Fmt(st.BandMin), Fmt(st.BandMax)));
            return r;
        }

        /// <summary>
        /// Marks which stats reach the game: all of them with a companion building, else attractiveness only (on the
        /// owner lot). <paramref name="enabled"/> = setting "Real park stats (companion building)".
        /// </summary>
        public static ParkStatsResult WithCompanion(ParkStatsResult r, bool enabled, ParkCompanionSite site, bool ungrouped = false)
        {
            if (r == null || !r.Applicable) return r;
            r.Site = site;
            r.Companion = ungrouped ? ParkCompanionState.Ungrouped : !enabled ? ParkCompanionState.Off : site.Found ? ParkCompanionState.Yes : ParkCompanionState.NoRoad;
            foreach (var s in r.Stats) s.Applied = r.Companion == ParkCompanionState.Yes || (s.Id == ParkStatIds.Attractiveness && r.Companion != ParkCompanionState.Ungrouped);
            return r;
        }

        /// <summary>The monthly upkeep the game charges for the park building (its prefab's, quantised), 0 without one.</summary>
        public static int GameUpkeep(ParkStatsResult r) => r != null && r.Applicable && r.Companion == ParkCompanionState.Yes ? KeyOf(r).Upkeep : 0;

        /// <summary>Prefab key of the companion for these (final) values.</summary>
        public static ParkPrefabKey KeyOf(ParkStatsResult r)
        {
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var s in r.Stats) values[s.Id] = s.Value;
            return ParkPrefabKey.From(values, r.LeisureType == LeisureKind.CityBeach);
        }

        private static ParkStatEstimate Make(string id, double raw, ParkStatsInput input, ParkStatsSettings settings, bool applied)
        {
            Limits(id, out double min, out double max, out double step, out string unit);
            Band(id, input.AreaM2, out double bmin, out double bmax);
            var e = new ParkStatEstimate
            {
                Id = id, Min = min, Max = max, Step = step, Unit = unit, Applied = applied,
                BandMin = bmin, BandMax = bmax,
                Suggested = Snap(Clamp(raw, bmin, bmax), step),
            };
            double v = e.Suggested;
            if (settings.Overrides.TryGetValue(id, out var o) && !double.IsNaN(o) && !double.IsInfinity(o))
            {
                e.Overridden = true;
                v = Snap(Clamp(o, min, max), step);
            }
            if (settings.KeepVanilla && (v < bmin || v > bmax))
            {
                e.Clamped = true;
                v = Snap(Clamp(v, bmin, bmax), step);
            }
            e.Value = v;
            Similar(e, input);
            return e;
        }

        /// <summary>Vanilla parks within ×3 of this size (else the 4 nearest), each scaled to this size.</summary>
        public static List<VanillaPark> Comparables(double areaM2)
        {
            var list = new List<VanillaPark>();
            foreach (var p in VanillaParks.Regular)
            {
                double ratio = areaM2 / p.AreaM2;
                if (ratio >= 1.0 / 3 && ratio <= 3) list.Add(p);
            }
            if (list.Count >= 3) return list;
            var all = new List<VanillaPark>(VanillaParks.Regular);
            all.Sort((x, y) =>
            {
                int c = Math.Abs(Math.Log(areaM2 / x.AreaM2)).CompareTo(Math.Abs(Math.Log(areaM2 / y.AreaM2)));
                return c != 0 ? c : string.CompareOrdinal(x.Id, y.Id);
            });
            return all.GetRange(0, Math.Min(4, all.Count));
        }

        /// <summary>Range of a stat over the comparable vanilla parks, scaled to <paramref name="areaM2"/>, ±15 %.</summary>
        public static void Band(string id, double areaM2, out double min, out double max)
        {
            Limits(id, out double lo, out double hi, out double step, out _);
            min = double.MaxValue; max = double.MinValue;
            double e = ScaleExponent(id);
            foreach (var p in Comparables(Math.Max(1, areaM2)))
            {
                double v = p.Value(id) * Math.Pow(Math.Max(1, areaM2) / p.AreaM2, e);
                min = Math.Min(min, v); max = Math.Max(max, v);
            }
            if (min > max) { min = lo; max = hi; return; }
            double cap = id == ParkStatIds.Attractiveness ? MaxSuggestedAttractiveness : hi;
            min = Snap(Clamp(min * 0.85, Math.Max(lo, id == ParkStatIds.Attractiveness || id == ParkStatIds.Leisure ? 1 : lo), cap), step);
            max = Snap(Clamp(max * 1.15, min, cap), step);
        }

        private static void Similar(ParkStatEstimate e, ParkStatsInput input)
        {
            var mine = new List<VanillaPark>();
            var other = new List<VanillaPark>();
            foreach (var p in Comparables(input.AreaM2)) (SameKind(p.Kind, input.Kind) ? mine : other).Add(p);
            Comparison<VanillaPark> near = (x, y) =>
            {
                int c = Math.Abs(Math.Log(input.AreaM2 / x.AreaM2)).CompareTo(Math.Abs(Math.Log(input.AreaM2 / y.AreaM2)));
                return c != 0 ? c : string.CompareOrdinal(x.Id, y.Id);
            };
            mine.Sort(near); other.Sort(near);
            mine.AddRange(other);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in mine)
            {
                if (e.Similar.Count >= 3) break;
                if (!seen.Add(p.Name)) continue;   // look-alike variants (Tiny Park / Mini Park) once
                e.Similar.Add(new KeyValuePair<VanillaPark, double>(p, p.Value(e.Id)));
            }
        }

        private static bool SameKind(VanillaParkKind v, ParkStatsKind k)
        {
            switch (k)
            {
                case ParkStatsKind.Plaza: return v == VanillaParkKind.Plaza;
                case ParkStatsKind.Sports: return v == VanillaParkKind.Sports;
                case ParkStatsKind.Playground: return v == VanillaParkKind.Playground;
                case ParkStatsKind.Nature: return v == VanillaParkKind.Nature || v == VanillaParkKind.Park;
                default: return v == VanillaParkKind.Park || v == VanillaParkKind.Pocket;
            }
        }

        private static double Sat(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));
        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
        private static double Round(double v, double step) => Math.Round(v / step) * step;
        private static double Snap(double v, double step) => step > 0 ? Math.Round(v / step) * step : v;
        private static double R1(double v) => Math.Round(v, 1);
        private static double R2(double v) => Math.Round(v, 2);
        private static double Fmt(double v) => Math.Round(v, 1);
    }
}

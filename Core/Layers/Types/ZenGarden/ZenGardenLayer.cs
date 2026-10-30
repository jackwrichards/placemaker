// Zen garden (type wave 6b, group "gardens"): a Japanese garden. A stroll loop runs inside the
// outline (wall or bamboo screen beyond it); the inner garden holds the composition — a raked-gravel
// field with odd-numbered rock groupings on moss islands (dry garden, viewed from a veranda), a pond
// with zig-zag or straight bridges and crane/turtle islands, a tea house with its basin and lantern,
// stepping-stone trails through moss, stone lanterns at bridge ends, entrances and the shore, clipped
// shrub mounds in threes and fives, pruned specimen trees and bamboo.
//
// Subtypes (presets; the "style" control picks the composition):
//   karesansui  dry garden: a big raked field, five rock groups, a veranda with benches, no water
//   stroll      pond garden: a large pond, bridges, islands, lanterns around a loop
//   tea         roji: secluded tea house, basin + lantern, stepping stones through moss, dense planting
//   moss        moss garden: moss ground, maples, a small pond with a zig-zag bridge, many stepping trails
//   bamboo      bamboo grove: dense bamboo with a straight central alley lined by lanterns
//
// Raked gravel is painted into the terrain (gravel texture + thin strokes of a second texture), which is
// permanent like the pond; a gravel surface can be used instead (no raking then).

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.Wetland;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.ZenGarden
{
    [AreaGenerator(621, Group = "gardens")]
    public sealed class ZenGardenLayer : LeisureLayer
    {
        public const string LayerId = "zenGarden";
        public const string GravelPlace = "place:gravel";
        public const string WaterPlace = "place:water";
        public const string PondPermanentKey = "Placemaker.Zen.Issue.PondPermanent";
        public static readonly string[] Styles = { "karesansui", "stroll", "tea", "moss", "bamboo" };
        /// <summary>Most bamboo clumps in a grove (the floor is a surface; clumps are massing accents).</summary>
        public const int MaxGroveClumps = 2500;

        protected override string Area => "Zen";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsStone.svg";
        public override float MinArea => 1500f;
        protected override bool HostsStreetShops => false;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, GravelPlace, WaterPlace };

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(TeaHouseAddOn.LayerId), Feature("restrooms", ("byArea", true), ("haEach", 6.0)), WetlandLayer.KioskFeature(),
            Feature("decals", ("theme", "leaves"), ("density", 0.6)), Feature("rocks", ("clusters", 2.0), ("size", "small")),
            Feature("terrain", ("shape", "mounds"), ("height", 1.5)),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "karesansui", ControlTier.Essential, "layout", Styles).Tip();
            c.Slider("gravel", 45, 0, 80, 5, "%", ControlTier.Essential, "layout").Tip();
            c.Toggle("pond", false, ControlTier.Essential, "layout").Tip();

            c.Choice("fieldShape", "rect", ControlTier.More, "gravel", "rect", "organic").Tip();
            c.Choice("raking", "both", ControlTier.More, "gravel", "none", "lines", "ripples", "both").Tip();
            c.Slider("gravelLayer", 2, 1, 4, 1, "count", ControlTier.More, "gravel").Tip();
            c.Slider("rakeLayer", 3, 1, 4, 1, "count", ControlTier.More, "gravel").Tip();
            c.Toggle("gravelSurface", false, ControlTier.More, "gravel").Tip();

            c.Slider("rockGroups", 5, 1, 9, 2, "count", ControlTier.Essential, "rocks").Tip();
            c.Choice("groupSize", "mixed", ControlTier.More, "rocks", "three", "five", "mixed").Tip();
            c.Slider("rockScale", 100, 50, 200, 10, "%", ControlTier.More, "rocks");
            c.Toggle("mossRings", true, ControlTier.More, "rocks").Tip();
            c.AssetMulti("rockAssets", AssetCategories.Rock, ControlTier.More, "rocks");

            c.Slider("pondSize", 15, 5, 40, 1, "%", ControlTier.More, "water").When("pond=true").Tip();
            c.Slider("bridges", 2, 0, 4, 1, "count", ControlTier.More, "water").When("pond=true");
            c.Choice("bridgeStyle", "zigzag", ControlTier.More, "water", "zigzag", "straight").When("pond=true").Tip();
            c.Slider("islands", 2, 0, 3, 1, "count", ControlTier.More, "water").When("pond=true").Tip();

            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "paths");
            c.Toggle("steppingStones", true, ControlTier.More, "paths").Tip();
            c.Slider("stoneSpacing", 0.9, 0.6, 1.6, 0.1, "m", ControlTier.More, "paths").When("steppingStones=true");
            c.Asset("stoneAsset", AssetCategories.ZenSteppingStone, ControlTier.More, "paths").When("steppingStones=true");
            c.Slider("benches", 4, 0, 12, 1, "count", ControlTier.More, "paths");

            c.Slider("prunedTrees", 30, 0, 150, 5, "perHa", ControlTier.More, "planting").Tip();
            c.Slider("shrubs", 40, 0, 200, 10, "perHa", ControlTier.More, "planting").Tip();
            c.Choice("bamboo", "screen", ControlTier.More, "planting", "none", "screen", "grove").Tip();
            c.Toggle("mossPaint", true, ControlTier.More, "planting").Tip();
            c.Slider("mossLayer", 1, 1, 4, 1, "count", ControlTier.More, "planting").When("mossPaint=true");
            c.AssetMulti("treeSpecies", AssetCategories.ZenTree, ControlTier.More, "planting");
            c.AssetMulti("shrubSpecies", AssetCategories.Bush, ControlTier.More, "planting");

            c.Slider("lanterns", 6, 0, 20, 1, "count", ControlTier.More, "structures").Tip();
            c.Asset("lanternAsset", AssetCategories.ZenLantern, ControlTier.More, "structures");
            c.Toggle("teaHouse", false, ControlTier.More, "structures").Tip();
            c.Asset("teaHouseAsset", AssetCategories.ZenTeaHouse, ControlTier.More, "structures").When("teaHouse=true");
            c.Toggle("basin", true, ControlTier.More, "structures").Tip();
            c.Toggle("wall", true, ControlTier.More, "structures").Tip();
            c.Asset("wallAsset", AssetCategories.Fence, ControlTier.More, "structures").When("wall=true");
            EntranceMarkers(c);

            c.RandomRange("gravel", 20, 60);
            c.RandomRange("rockGroups", 3, 7);
            c.Fixed("gravelLayer");
            c.Fixed("rakeLayer");
            c.Fixed("mossLayer");
        }

        protected override void DefinePresets()
        {
            // rf-parks: the dry garden's field is a clean pale gravel surface (the painted gravel and rake strokes read as a patchy
            // lawn in the game) and its stones are larger, so the five groups read from the street.
            Preset("karesansui", ("style", "karesansui"), ("gravel", 55.0), ("pond", false), ("raking", "both"), ("rockGroups", 5.0), ("groupSize", "mixed"),
                ("prunedTrees", 20.0), ("shrubs", 40.0), ("bamboo", "screen"), ("lanterns", 3.0), ("benches", 4.0), ("gravelSurface", true), ("fieldShape", "organic"));
            // rf-parks: a smaller pond that fits a city block (22 % found no room on 150 x 140 m: NoPond) and no gravel field beside it.
            Preset("stroll", ("style", "stroll"), ("gravel", 0.0), ("fieldShape", "organic"), ("raking", "ripples"), ("pond", true), ("pondSize", 14.0),
                ("bridges", 2.0), ("bridgeStyle", "straight"), ("islands", 2.0), ("rockGroups", 5.0), ("groupSize", "three"), ("prunedTrees", 30.0),
                ("shrubs", 60.0), ("bamboo", "none"), ("lanterns", 10.0), ("benches", 6.0), ("wall", false));
            Preset("tea", ("style", "tea"), ("gravel", 0.0), ("pond", false), ("teaHouse", true), ("basin", true), ("rockGroups", 3.0), ("groupSize", "three"),
                ("prunedTrees", 35.0), ("shrubs", 80.0), ("bamboo", "screen"), ("lanterns", 6.0), ("benches", 2.0), ("stoneSpacing", 0.8));
            Preset("moss", ("style", "moss"), ("gravel", 0.0), ("pond", true), ("pondSize", 10.0), ("bridges", 1.0), ("bridgeStyle", "zigzag"), ("islands", 1.0),
                ("rockGroups", 3.0), ("groupSize", "three"), ("prunedTrees", 110.0), ("shrubs", 40.0), ("bamboo", "none"), ("lanterns", 5.0), ("wall", false));
            Preset("bamboo", ("style", "bamboo"), ("gravel", 0.0), ("pond", false), ("rockGroups", 1.0), ("groupSize", "five"), ("prunedTrees", 10.0),
                ("shrubs", 20.0), ("bamboo", "grove"), ("lanterns", 12.0), ("benches", 4.0), ("steppingStones", false), ("mossPaint", false));
        }

        private sealed class Garden
        {
            public LayerRegion Region, Inner;
            public Occupancy Occ;
            public List<Gate> Gates;
            public float2[] Field;            // the gravel field (null = none)
            public LayerRegion FieldRegion;
            public float2 FieldAxis;
            public readonly WaterBodies Water = new WaterBodies();
            public readonly List<(float2 c, float r)> Groups = new List<(float2, float)>();
            public readonly List<(float2 c, float r)> FieldGroups = new List<(float2, float)>();
            public readonly List<(float2 at, float2 along)> BridgeEnds = new List<(float2, float2)>();
            public AssetRef Path;
            public float LoopInset;

            public float GravelDistance(float2 p, float cap)
            {
                if (Field == null) return cap;
                if (FieldRegion.Contains(p)) return 0f;
                return math.min(cap, FieldRegion.BoundaryDistance(p, cap));
            }

            /// <summary>Nothing planted or walked on here: gravel (with margin) or water.</summary>
            public bool Blocked(float2 p, float r) => GravelDistance(p, r + 1f) < r + 0.3f || Water.InWater(p, r + 0.3f);
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var g = new Garden { Region = ctx.LayerRegion, Gates = Gates(ctx), Path = AssetMix.One(ctx.S("pathAsset")) };
            g.Occ = new Occupancy(g.Region, ctx.Input.Reserved, ctx.Site);
            string style = ctx.S("style");
            float ha = g.Region.Area / 10000f;

            // 1. Stroll loop inside the outline, the entrances joined to it; the bamboo grove gets its alley.
            Paths(ctx, g, style);
            ctx.Check();

            // 2. The raked field (veranda alongside in the dry garden).
            float gravelM2 = Field(ctx, g, style);
            ctx.Check();

            // 3. Pond, bridges, islands.
            int bridges = 0;
            if (ctx.B("pond")) bridges = Pond(ctx, g);
            g.Water.Freeze(g.Region);
            ctx.Check();

            // 4. Rock groupings: in the gravel first, then on the shore, then beside the loop.
            int stones = Rocks(ctx, g, style);
            ctx.Check();

            // 5. Structures, seats, lanterns, stepping stones.
            bool tea = false;
            if (ctx.B("teaHouse"))
            {
                string cat = LeisureKit.Category(ctx, ctx.S("teaHouseAsset"), AssetCategories.ZenTeaHouse, AssetCategories.Shelter);
                if (cat != null)
                    tea = ZenKit.TeaHouse(ctx, g.Occ, LeisureKit.Points(g.Gates), AssetMix.One(ctx.S("teaHouseAsset")), cat, true, ctx.B("basin"), true,
                        ctx.B("steppingStones"), g.Blocked, out _);
                if (!tea) ctx.Issue(IssueSeverity.Info, "Placemaker.Zen.Issue.NoTeaHouse");
            }
            if (ctx.B("basin") && !tea) Basin(ctx, g);
            int lanterns = Lanterns(ctx, g, ctx.I("lanterns"));
            Benches(ctx, g, ctx.I("benches"));
            int stepping = ctx.B("steppingStones") ? SteppingTrails(ctx, g, style) : 0;
            ctx.Check();

            // 6. Planting: bamboo, pruned trees, clipped shrub mounds, island pines.
            Plant(ctx, g, style, ha);
            ctx.Check();

            // 7. Ground: moss, gravel and its raking (painted last so it sits on top), moss under the rocks, pebble beach.
            Ground(ctx, g);
            if (ctx.B("wall")) Fence(ctx, g.Gates, AssetMix.One(ctx.S("wallAsset")));

            // 8. Regions: the field and the water are claimed places; features get the open moss and lawn.
            if (g.Field != null) ctx.AddRegion(g.Field, "gravel:0", GravelPlace);
            g.Water.EmitPlaces(ctx, WaterPlace);
            var pieces = FreeSpace.Pieces(g.Region, p => g.Occ.Corridors.Overlaps(p, 0.75f) || g.Occ.HitsPolygon(p, 0.5f) || g.Blocked(p, 1f), 2.5f, 120f, ctx.Cancel);
            if (pieces.Count == 0) ctx.AddRegion(g.Region.Ring, "green:0", RegionTags.Green, RegionTags.Core);
            for (int i = 0; i < pieces.Count && i < 24; i++)
                if (i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);

            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.Gravel", Math.Round(gravelM2), Keys.UnitM2);
            ctx.Stat("Placemaker.Stat.RockGroups", g.Groups.Count, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Rocks", stones, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Lanterns", lanterns, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Bridges", bridges, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.SteppingStones", stepping, Keys.UnitCount);
            if (g.Water.Ponds.Count > 0) ctx.Stat(Keys.StatLakeArea, Math.Round(g.Water.TotalArea), Keys.UnitM2);
            ctx.Plan.Facts["gravelArea"] = gravelM2;
            ctx.Plan.Facts["rockGroups"] = g.Groups.Count;
            ctx.Plan.Facts["fieldGroups"] = g.FieldGroups.Count;
            ctx.Plan.Facts["bridges"] = bridges;
            ctx.Plan.Facts["lanterns"] = lanterns;
            ctx.Plan.Facts["steppingStones"] = stepping;
            ctx.Plan.Facts["waterArea"] = g.Water.TotalArea;
            ctx.Plan.Facts["teaHouse"] = tea ? 1 : 0;
        }

        // ───────────────────────────── paths ─────────────────────────────

        private static void Paths(LayerContext ctx, Garden g, string style)
        {
            var region = g.Region;
            g.LoopInset = math.clamp(math.sqrt(region.Area) * 0.07f, 6f, 15f);
            float2[] loop = null;
            try
            {
                var rings = Poly.Offset(region.Ring, g.LoopInset);
                float best = 0f;
                foreach (var r in rings) { float a = Poly.Area(r); if (a > best) { best = a; loop = r; } }
                if (loop != null)
                {
                    // Rounded corners read as a stroll walk. Rounding a reflex corner pushes the curve outwards,
                    // so a variant is kept only while it stays well inside the outline.
                    var raw = loop;
                    foreach (var smooth in new[]
                             {
                                 Poly.Clean(Poly.Simplify(Poly.Chaikin(raw, 2), 0.4f), 0.5f),
                                 Poly.Clean(Poly.SmoothCorners(Poly.Simplify(raw, 0.5f), g.LoopInset * 0.8f, 3f), 0.5f),
                             })
                    {
                        bool inside = smooth.Length >= 3 && Poly.IsSimple(smooth);
                        if (inside) foreach (var v in smooth) if (!region.ContainsDisc(v, g.LoopInset * 0.6f)) { inside = false; break; }
                        if (inside) { loop = smooth; break; }
                    }
                    loop = Poly.Resample(loop, 7f);
                    if (loop.Length < 4 || Poly.Area(loop) < 300f) loop = null;
                }
            }
            catch (Exception) { loop = null; }
            if (loop == null)
            {
                // Too narrow for a loop: a natural path network instead.
                NaturalPaths(ctx, g.Occ, g.Gates, 0, 4f, 14f, "narrow", g.Path);
                g.Inner = region;
                return;
            }
            for (int i = 0; i < loop.Length; i++)
            {
                var a = loop[i]; var b = loop[(i + 1) % loop.Length];
                if (!region.ContainsDisc(a, 2f) || !region.ContainsDisc(b, 2f) || region.CrossesBoundary(a, b, false)) continue;
                LeisureKit.Path(ctx, g.Occ, a, b, NetRole.Pathway, g.Path);
            }
            foreach (var gate in g.Gates)
            {
                if (g.Occ.Paths.Nearest(gate.Position, 200f, out var foot) < 0) continue;
                if (math.distance(foot, gate.Position) < 1f || region.CrossesBoundary(gate.Position, foot, true)) continue;
                var net = ctx.StraightNet(gate.Position, foot, NetRole.Pathway, g.Path);
                if (gate.AttachId >= 0) { net.StartAttach = gate.AttachId; net.StartAttachT = gate.AttachT; }
                g.Occ.TakeCorridor(gate.Position, foot, Composer.DefaultHalfWidth(NetRole.Pathway), 100);
                g.Occ.Paths.Add(gate.Position, foot, Composer.DefaultHalfWidth(NetRole.Pathway), 100);
            }
            try
            {
                float innerInset = g.LoopInset + Composer.DefaultHalfWidth(NetRole.Pathway) + 2.5f;
                float best = 0f; float2[] inner = null;
                foreach (var r in Poly.Offset(region.Ring, innerInset)) { float a = Poly.Area(r); if (a > best) { best = a; inner = r; } }
                g.Inner = inner != null ? LayerRegion.From(inner) : null;
            }
            catch (Exception) { g.Inner = null; }
            if (g.Inner == null) g.Inner = region;

            if (style == "bamboo")
            {
                // The grove's straight alley along the long axis, from loop to loop.
                LeisureKit.Frame(g.Inner, out var centre, out var axis, out _, out _, out _);
                if (LeisureKit.Chord(g.Inner, centre, axis, 1f, out var a0, out var a1))
                {
                    var e0 = g.Occ.Paths.Nearest(a0, 12f, out var f0) >= 0 ? f0 : a0;
                    var e1 = g.Occ.Paths.Nearest(a1, 12f, out var f1) >= 0 ? f1 : a1;
                    if (!region.CrossesBoundary(e0, e1, false)) LeisureKit.Path(ctx, g.Occ, e0, e1, NetRole.Pathway, g.Path);
                }
            }
        }

        // ───────────────────────────── gravel ─────────────────────────────

        private static float Field(LayerContext ctx, Garden g, string style)
        {
            float share = ctx.F("gravel") / 100f;
            if (share <= 0.001f || g.Inner == null) return 0f;
            var inner = g.Inner;
            float target = inner.Area * share;
            LeisureKit.Frame(inner, out var frameCentre, out var axis, out _, out _, out _);
            var deep = inner.DeepestPoint(out _, null, 12);
            bool organic = ctx.S("fieldShape") == "organic";
            float2[] field = null;
            foreach (var c in new[] { frameCentre, deep })
            {
                for (float s = 1f; s > 0.15f && field == null; s *= 0.85f)
                {
                    float a = target * s * s;
                    if (organic)
                    {
                        var blob = Poly.EnsureCcw(LeisureKit.Blob(c, math.sqrt(a / math.PI), Hashing.Mix(ctx.Seed, 0x6EA7u), 18, 0.16f));
                        if (Fits(inner, g.Occ, blob)) field = blob;
                    }
                    else
                    {
                        float len = math.sqrt(a * 1.8f);
                        var rect = new OrientedRect { Center = c, Axis = axis, HalfLength = len * 0.5f, HalfWidth = len / 1.8f * 0.5f };
                        if (LeisureKit.RectFits(inner, g.Occ, rect, 1f)) field = rect.Ring;
                    }
                }
                if (field != null) break;
            }
            if (field == null || Poly.Area(field) < 40f) { ctx.Issue(IssueSeverity.Info, "Placemaker.Zen.Issue.NoField"); return 0f; }
            g.Field = Poly.EnsureCcw(field);
            g.FieldRegion = LayerRegion.From(g.Field);
            g.FieldAxis = axis;
            if (g.FieldRegion == null) { g.Field = null; return 0f; }

            if (style == "karesansui" && !organic) Veranda(ctx, g, axis);
            return Poly.Area(g.Field);
        }

        private static bool Fits(LayerRegion inner, Occupancy occ, float2[] ring)
        {
            foreach (var v in ring) if (!inner.ContainsDisc(v, 1f) || occ.Corridors.Overlaps(v, 0.5f)) return false;
            for (int i = 0; i < ring.Length; i++)
                foreach (var s in occ.Corridors.Segments)
                    if (Poly.SegmentsIntersect(s.A, s.B, ring[i], ring[(i + 1) % ring.Length])) return false;
            return true;
        }

        /// <summary>The dry garden is viewed from a wide straight veranda walk along its long side, facing the entrance.</summary>
        private static void Veranda(LayerContext ctx, Garden g, float2 axis)
        {
            var fr = g.FieldRegion;
            Poly.Bounds(g.Field, out var mn, out var mx);
            var c = fr.Centroid;
            var side = new float2(-axis.y, axis.x);
            float halfL = 0f, halfW = 0f;
            foreach (var v in g.Field) { halfL = math.max(halfL, math.abs(math.dot(v - c, axis))); halfW = math.max(halfW, math.abs(math.dot(v - c, side))); }
            float sign = g.Gates.Count > 0 && math.dot(g.Gates[0].Position - c, side) < 0f ? -1f : 1f;
            float off = halfW + Composer.DefaultHalfWidth(NetRole.PathwayWide) + 0.6f;
            var a = c - axis * (halfL + 1f) + side * (sign * off);
            var b = c + axis * (halfL + 1f) + side * (sign * off);
            if (!g.Region.ContainsDisc(a, 3f) || !g.Region.ContainsDisc(b, 3f) || g.Region.CrossesBoundary(a, b, false)) return;
            if (g.Occ.Corridors.Overlaps((a + b) * 0.5f, 2.5f) || g.Occ.Discs.Overlaps((a + b) * 0.5f, 2.5f)) return;
            LeisureKit.Path(ctx, g.Occ, a, b, NetRole.Pathway, g.Path);
            foreach (var end in new[] { a, b })
            {
                if (g.Occ.Paths.Nearest(end, 40f, out var foot) < 0) continue;
                // Nearest path other than the veranda itself.
                var probe = end + side * (sign * 3f);
                if (g.Occ.Paths.Nearest(probe, 40f, out foot) < 0 || math.distance(foot, end) < 1.5f) continue;
                if (Poly.ClipSegmentToPolygon(g.Field, end, foot).Count > 0 || g.Region.CrossesBoundary(end, foot, false)) continue;
                LeisureKit.Path(ctx, g.Occ, end, foot, NetRole.Pathway, g.Path);
            }
            ctx.Plan.Facts["veranda"] = 1;
        }

        // ───────────────────────────── water ─────────────────────────────

        private static int Pond(LayerContext ctx, Garden g)
        {
            float target = g.Region.Area * ctx.F("pondSize") / 100f;
            float blob = math.clamp(math.sqrt(target / math.PI) * 0.6f, 4f, 22f);
            var spec = new GrowSpec
            {
                Ponds = 1, TargetArea = target, BlobRadius = blob, MinRadius = 3f, EdgeMargin = g.LoopInset + 5f, PathMargin = 3f, PondGap = 4f,
                Wander = 0.5f, Jitter = 0.18f,
            };
            g.Water.Grow(ctx, g.Occ, spec, Hashing.Mix(ctx.Seed, 0x90D0u), p => g.GravelDistance(p, blob * 2f + 6f) < blob * 1.3f + 4f);
            if (g.Water.Ponds.Count == 0) { ctx.Issue(IssueSeverity.Info, "Placemaker.Zen.Issue.NoPond"); return 0; }
            g.Water.Freeze(g.Region);
            var pond = g.Water.Ponds[0];
            var crossings = new SegmentIndex(10f);
            var crossed = new List<float2>();
            bool zigzag = ctx.S("bridgeStyle") == "zigzag";
            int wanted = ctx.I("bridges"), built = 0;
            for (int i = 0; i < wanted + 2 && built < wanted; i++)
            {
                if (pond.Area < 100f) break;
                var line = g.Water.Crossing(pond, g.Occ, 25f, zigzag, Hashing.Mix(ctx.Seed, (uint)(i * 53 + 1)), out bool spur, crossed);
                if (line == null || spur) continue;
                bool clash = false;
                for (int s = 1; s < line.Count && !clash; s++)
                    if (crossings.Count > 0 && crossings.Overlaps((line[s - 1] + line[s]) * 0.5f, 3f, 2f)) clash = true;
                if (clash) continue;
                for (int s = 1; s < line.Count; s++)
                {
                    LeisureKit.Path(ctx, g.Occ, line[s - 1], line[s], NetRole.Pathway, g.Path);
                    crossings.Add(line[s - 1], line[s], 1.5f);
                }
                g.Water.KeepDry(line, Composer.DefaultHalfWidth(NetRole.Pathway) + 0.5f);
                g.BridgeEnds.Add((line[1], math.normalizesafe(line[1] - line[0])));
                g.BridgeEnds.Add((line[line.Count - 2], math.normalizesafe(line[line.Count - 2] - line[line.Count - 1])));
                crossed.Add(line[line.Count / 2]);
                built++;
            }
            g.Water.AddIslands(g.Region, ctx.I("islands"), 3f, 5f, Hashing.Mix(ctx.Seed, 0x15B0u), crossings);
            if (g.Water.EmitBasins(ctx, 1.0f, 0.3f, 1.5f) > 0) ctx.Issue(IssueSeverity.Warning, PondPermanentKey);
            return built;
        }

        // ───────────────────────────── rocks ─────────────────────────────

        /// <summary>(rf-parks) Vanilla boulders for the rock groups while the rock control is automatic (the automatic rock pool is small stones that read as specks).</summary>
        private static readonly string[] Boulders = { "Boulder03", "Boulder01", "Boulder05", "Boulder02", "Boulder06", "Boulder04" };

        private static int Rocks(LayerContext ctx, Garden g, string style)
        {
            int wanted = ctx.I("rockGroups");
            if ((wanted & 1) == 0) wanted++;   // odd, always
            var ids = ctx.L("rockAssets");
            if (ids == null || ids.Length == 0) ids = Boulders;
            if (wanted <= 0 || !AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, ids, Keys.Category(AssetCategories.Rock))) return 0;
            string mode = ctx.S("groupSize");
            float scale = ctx.F("rockScale") / 100f;
            int stones = 0, index = 0, even = 0;
            Func<float2, float, bool> wet = (p, r) => g.Water.InWater(p, r * 0.5f);

            // In the gravel: the dry garden puts every group there, the others about half.
            int inField = g.Field == null ? 0 : style == "karesansui" ? wanted : (wanted + 1) / 2;
            if (inField > 0)
            {
                var cands = Scatter.Candidates(g.FieldRegion, 1.6f, Hashing.Mix(ctx.Seed, 0x40C1u), 1200, ctx.Cancel);
                foreach (var c in cands)
                {
                    if (g.FieldGroups.Count >= inField) break;
                    int n = ZenKit.GroupSize(mode, index);
                    float R = ZenKit.GroupRadius(n, scale);
                    if (!g.FieldRegion.ContainsDisc(c, R + 1f)) continue;
                    bool far = true;
                    foreach (var (oc, or) in g.FieldGroups)
                        if (math.distance(oc, c) < R + or + 4.5f) { far = false; break; }
                    if (!far) continue;
                    int placed = ZenKit.RockGroup(ctx, g.Occ, c, n, scale, ids, AssetCategories.Rock, Hashing.Mix(ctx.Seed, 0x40C2u + (uint)index), wet);
                    if ((placed & 1) == 0) even++;
                    if (placed == 0) continue;
                    g.FieldGroups.Add((c, R)); g.Groups.Add((c, R));
                    stones += placed; index++;
                }
            }
            // On the shore (set back on dry land), then beside the loop facing the inner garden.
            var spots = new List<float2>();
            foreach (var (s, outward, island) in g.Water.Shore(6f))
                if (!island) spots.Add(s + outward * 2.5f);
            foreach (var a in PathAnchors.Beside(g.Occ, 3f, 14f, 1f))
                if (g.Inner.Contains(a.Position)) spots.Add(a.Position);
            int k = 0;
            foreach (var p0 in spots)
            {
                if (g.Groups.Count >= wanted) break;
                var p = p0;
                int n = ZenKit.GroupSize(mode, index);
                float R = ZenKit.GroupRadius(n, scale);
                uint h = Hashing.Mix(ctx.Seed, (uint)k++ * 7919u + 0x40C3u);
                if (Hashing.Unit(h) < 0.6f) continue;   // spread along the candidates
                if (!g.Occ.Free(p, R * 0.6f, 1f, 0.3f) || g.Blocked(p, R * 0.6f)) continue;
                bool far = true;
                foreach (var (oc, or) in g.Groups)
                    if (math.distance(oc, p) < R + or + 8f) { far = false; break; }
                if (!far) continue;
                int placed = ZenKit.RockGroup(ctx, g.Occ, p, n, scale, ids, AssetCategories.Rock, h, g.Blocked);
                if (placed == 0) continue;
                if ((placed & 1) == 0) even++;
                g.Groups.Add((p, R));
                stones += placed; index++;
            }
            ctx.Plan.Facts["evenGroups"] = even;
            if (g.Groups.Count < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Zen.Issue.FewerGroups", g.Groups.Count, wanted);
            return stones;
        }

        // ───────────────────────────── structures ─────────────────────────────

        private static string LanternCategory(LayerContext ctx) =>
            LeisureKit.Category(ctx, ctx.S("lanternAsset"), AssetCategories.ZenLantern, AssetCategories.Lamp);

        private static void Basin(LayerContext ctx, Garden g)
        {
            string cat = LeisureKit.Category(ctx, "", AssetCategories.ZenBasin);
            if (cat == null) return;
            float2 focus = g.Water.Ponds.Count > 0 ? g.Water.Ponds[0].Deepest(out _) : g.Groups.Count > 0 ? g.Groups[0].c : g.Inner.Centroid;
            var anchors = PathAnchors.Beside(g.Occ, 0.8f, 8f, 0.8f);
            anchors.RemoveAll(a => g.Blocked(a.Position, 1f));
            anchors.Sort((a, b) => math.distancesq(a.Position, focus).CompareTo(math.distancesq(b.Position, focus)));
            foreach (var a in anchors)
            {
                if (!LeisureKit.Put(ctx, g.Occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Furniture, AssetMix.One(""), cat, 0.8f,
                        Hashing.Mix(ctx.Seed, 0xBA51u), 0.5f, 0.2f)) continue;
                string lc = LanternCategory(ctx);
                var side = math.normalizesafe(a.Tangent) * 1.6f;
                if (lc != null)
                    LeisureKit.Put(ctx, g.Occ, a.Position + side, 0f, ObjectRole.Lamp, AssetMix.One(ctx.S("lanternAsset")), lc, 0.6f, Hashing.Mix(ctx.Seed, 0xBA52u), 0.4f, 0.2f);
                ctx.Plan.Facts["basin"] = 1;
                return;
            }
        }

        /// <summary>Lanterns where a Japanese garden puts them: bridge ends, entrances, the shore, beside rock groups, then along the walk.</summary>
        private static int Lanterns(LayerContext ctx, Garden g, int wanted)
        {
            if (wanted <= 0) return 0;
            string cat = LanternCategory(ctx);
            if (cat == null) return 0;
            var asset = AssetMix.One(ctx.S("lanternAsset"));
            float off = Composer.DefaultHalfWidth(NetRole.Pathway) + 0.9f;
            var cands = new List<float2>();
            int flip = 0;
            foreach (var (at, along) in g.BridgeEnds)
            {
                var side = new float2(-along.y, along.x) * ((flip++ & 1) == 0 ? 1f : -1f);
                cands.Add(at + along * 1.5f + side * off);
            }
            foreach (var gate in g.Gates)
            {
                if (g.Occ.Paths.Nearest(gate.Position + g.Region.InwardNormal(gate.Segment) * 4f, 6f, out var q) < 0) continue;
                var nrm = g.Region.InwardNormal(gate.Segment);
                var tan = new float2(-nrm.y, nrm.x);
                cands.Add(q + tan * off);
            }
            var anchors = PathAnchors.Beside(g.Occ, 0.6f, 9f, 0.5f);
            anchors.RemoveAll(a => g.Blocked(a.Position, 0.6f));
            var byWater = new List<PathAnchor>(anchors);
            byWater.RemoveAll(a => g.Water.Distance(a.Position, 10f) > 6f);
            foreach (var a in PathAnchors.Pick(byWater, 4, 14f, false)) cands.Add(a.Position);
            foreach (var (c, r) in g.Groups)
                foreach (var a in anchors)
                    if (math.distance(a.Position, c) < r + 8f) { cands.Add(a.Position); break; }
            foreach (var a in PathAnchors.Pick(anchors, wanted * 2, 22f, true)) cands.Add(a.Position);
            int placed = 0;
            foreach (var p in cands)
            {
                if (placed >= wanted) break;
                if (g.Blocked(p, 0.6f)) continue;
                if (LeisureKit.Put(ctx, g.Occ, p, Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)placed)) * 6.283f, ObjectRole.Lamp, asset, cat, 0.6f,
                        Hashing.Mix(ctx.Seed, 0x1A07u + (uint)placed), 0.4f, 0.25f)) placed++;
            }
            return placed;
        }

        /// <summary>Viewing benches beside the walk, facing the gravel, the pond or a rock group.</summary>
        private static void Benches(LayerContext ctx, Garden g, int wanted)
        {
            if (wanted <= 0) return;
            var focus = new List<float2>();
            if (g.Field != null) focus.Add(g.FieldRegion.Centroid);
            foreach (var p in g.Water.Ponds) focus.Add(p.Deepest(out _));
            foreach (var (c, _) in g.Groups) focus.Add(c);
            if (focus.Count == 0) focus.Add(g.Inner.Centroid);
            var anchors = PathAnchors.Beside(g.Occ, 1.3f, 6f, 0.4f);
            anchors.RemoveAll(a => g.Blocked(a.Position, 1.3f));
            float2 Focus(float2 p) { float best = float.MaxValue; var at = p; foreach (var f in focus) { float d = math.distancesq(f, p); if (d < best) { best = d; at = f; } } return at; }
            anchors.Sort((a, b) => math.distance(a.Position, Focus(a.Position)).CompareTo(math.distance(b.Position, Focus(b.Position))));
            int n = 0;
            var bench = AssetMix.One("");
            foreach (var a in PathAnchors.Pick(anchors, wanted, 14f, false))
                if (LeisureKit.Put(ctx, g.Occ, a.Position, LeisureKit.Yaw(a.Position, Focus(a.Position)), ObjectRole.Bench, bench, null, 1.3f, Hashing.Mix(ctx.Seed, 0xBE40u + (uint)n))) n++;
        }

        /// <summary>Stepping-stone trails across the inner garden between points beside the walk (never over gravel or water).</summary>
        private static int SteppingTrails(LayerContext ctx, Garden g, string style)
        {
            int trails = style == "moss" ? 5 : style == "stroll" || style == "tea" ? 3 : style == "karesansui" ? 1 : 0;
            trails = math.max(trails, (int)(g.Region.Area / 30000f));
            if (trails <= 0) return 0;
            var (asset, cat, scale) = ZenKit.SteppingAsset(ctx, ctx.S("stoneAsset"));
            if (cat == null) return 0;
            float spacing = math.max(0.6f, ctx.F("stoneSpacing"));
            var anchors = PathAnchors.Beside(g.Occ, 0.6f, 5f, 0.4f);
            // Only the inner side of the walk (the outer band belongs to the wall and the bamboo).
            anchors.RemoveAll(a => g.Region.BoundaryDistance(a.Position, g.LoopInset + 2f) < g.LoopInset + 1f || g.Blocked(a.Position, 1f));
            if (anchors.Count < 2) return 0;
            int stones = 0, made = 0;
            var used = new List<float2>();
            for (int i = 0; i < anchors.Count && made < trails; i++)
            {
                var a = anchors[(int)(Hashing.Mix(ctx.Seed, (uint)i + 0x57E0u) % (uint)anchors.Count)].Position;
                bool near = false;
                foreach (var u in used) if (math.distance(u, a) < 15f) { near = true; break; }
                if (near) continue;
                float2 best = a; float bestScore = float.MaxValue;
                foreach (var b in anchors)
                {
                    float d = math.distance(a, b.Position);
                    if (d < 15f || d > 60f) continue;
                    if (!Clear(g, a, b.Position)) continue;
                    float score = math.abs(d - 30f) + Hashing.Unit(Hashing.Mix(ctx.Seed, (int)b.Position.x, (int)b.Position.y)) * 6f;
                    if (score < bestScore) { bestScore = score; best = b.Position; }
                }
                if (bestScore == float.MaxValue) continue;
                stones += ZenKit.SteppingTrail(ctx, g.Occ, a, best, spacing, asset, cat, scale, Hashing.Mix(ctx.Seed, 0x57E1u + (uint)made), g.Blocked);
                used.Add(a); used.Add(best);
                made++;
            }
            return stones;
        }

        private static bool Clear(Garden g, float2 a, float2 b)
        {
            float len = math.distance(a, b);
            for (float t = 0f; t <= len; t += 2f)
            {
                var p = math.lerp(a, b, t / len);
                if (g.Blocked(p, 1.2f)) return false;
                bool interior = t > 3f && t < len - 3f;
                if (interior && (!g.Inner.Contains(p) || g.Occ.Corridors.Overlaps(p, 0.6f, 0.3f))) return false;
            }
            return true;
        }

        // ───────────────────────────── planting ─────────────────────────────

        // (makeover) explicit greens when the player chose none: the automatic bush pool gave pale teal wild flower bushes
        // along the edges (run show-23-builds-1030-033711-0b14); clipped city shrubs read as o-karikomi
        private static readonly string[] ClippedShrubs = { "GreenBushCity01", "GreenBushCity02", "GreenBushCity03" };
        private static readonly string[] ScreenShrubs = { "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildElderly01", "GreenBushWild01" };

        private static void Plant(LayerContext ctx, Garden g, string style, float ha)
        {
            var region = g.Region;
            var treeIds = ctx.L("treeSpecies"); var shrubIds = ctx.L("shrubSpecies");
            string bamboo = ctx.S("bamboo");
            string bambooCat = bamboo == "none" ? null : LeisureKit.Category(ctx, "", AssetCategories.ZenBamboo, AssetCategories.Bush);
            if (bambooCat != null && bamboo == "screen")
            {
                // Two staggered rows just inside the outline, open at the entrances.
                int n = 0;
                for (float along = 0f; along < region.Perimeter && n < 3000; along += 1.7f)
                {
                    var b = region.PointAt(along, out _, out var inward, out _);
                    bool gate = false;
                    foreach (var gt in g.Gates) if (math.distance(gt.Position, b) < 7f) { gate = true; break; }
                    if (gate) continue;
                    for (int row = 0; row < 2; row++)
                    {
                        var p = b + inward * (row == 0 ? 1.4f : 3.1f) + math.normalizesafe(new float2(-inward.y, inward.x)) * (row * 0.85f);
                        if (!g.Occ.Free(p, 0.9f, 0.8f, 0.3f) || g.Blocked(p, 0.9f)) continue;
                        uint v = Hashing.Mix(ctx.Seed, 0xBA3Bu + (uint)n);
                        ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Bush, bambooCat == AssetCategories.Bush ? AssetMix.Pick(ScreenShrubs, v) : AssetMix.One(""), v, -1, bambooCat == AssetCategories.Bush ? null : bambooCat);
                        g.Occ.Take(p, 0.9f, 4);
                        n++;
                    }
                }
            }
            else if (bambooCat != null && bamboo == "grove")
            {
                // Ground before props (GroundCover): the grove is a leaf-litter floor with bamboo THICKETS on it
                // (clumped, ~1 culm clump per 40 m²), not a clump every 3 m over the whole garden.
                if (g.Field == null && g.Water.Ponds.Count == 0)
                    ctx.Surface(g.Inner.Ring, SurfaceRole.Ground, AssetMix.One(""), SurfaceCategories.Bark);
                int n = math.min(MaxGroveClumps, (int)(region.Area / 40f));   // 250 per ha
                LeisureKit.Plant(ctx, g.Occ, region, n, ObjectRole.Bush, bambooCat == AssetCategories.Bush ? ScreenShrubs : null, 0.9f, 22f, 0.3f, 0xBA3Cu, p => g.Blocked(p, 1.5f), 1, 3,
                    bambooCat == AssetCategories.Bush ? null : bambooCat);
            }

            string treeCat = LeisureKit.Category(ctx, treeIds.Length > 0 ? "x" : "", AssetCategories.ZenTree, AssetCategories.Tree);
            if (treeCat != null)
            {
                string cat = treeCat == AssetCategories.Tree ? null : treeCat;
                int trees = math.min(6000, (int)(ha * ctx.F("prunedTrees")));
                LeisureKit.Plant(ctx, g.Occ, region, trees, ObjectRole.Tree, treeIds, 2.2f, 16f, 0.45f, 0x7E2u, p => g.Blocked(p, 2.5f), 2, 3, cat);
                // A pine on each island (crane and turtle islands).
                int k = 0;
                foreach (var pond in g.Water.Ponds)
                    foreach (var isl in pond.Islands)
                    {
                        var lr = LayerRegion.From(isl);
                        if (lr == null) continue;
                        var c = lr.DeepestPoint(out float ir, null, 10);
                        uint v = Hashing.Mix(ctx.Seed, 0x15C0u + (uint)k++);
                        if (ir >= 2.4f && g.Occ.Free(c, 1.5f, 0f, 0.2f))
                        {
                            ctx.Object(c, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), v, 2, cat);
                            g.Occ.Take(c, 1.5f, 3);
                        }
                    }
            }

            // Clipped shrub mounds (o-karikomi) in odd clusters of three or five.
            int clusters = math.min(1500, (int)(ha * ctx.F("shrubs") / 3.5f));
            if (clusters > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, shrubIds, Keys.Category(AssetCategories.Bush)))
            {
                int made = 0, i = 0;
                foreach (var c in Scatter.Candidates(region, 4f, Hashing.Mix(ctx.Seed, 0x5B0u), clusters * 12 + 50, ctx.Cancel))
                {
                    if (made >= clusters) break;
                    uint h = Hashing.Mix(ctx.Seed, (uint)i++ + 0x5B1u);
                    int n = Hashing.Unit(h) < 0.7f ? 3 : 5;
                    if (!g.Occ.Free(c, 2.6f, 1f, 0.3f) || g.Blocked(c, 3f)) continue;
                    float a0 = Hashing.Unit(Hashing.Mix(h, 1u)) * 6.283f;
                    for (int s = 0; s < n; s++)
                    {
                        float a = a0 + s * (6.283f / n) + (s & 1) * 0.3f;
                        var p = n == 5 && s == 0 ? c : c + new float2(math.cos(a), math.sin(a)) * (1.15f + 0.25f * (s & 1));
                        if (!g.Occ.Free(p, 0.9f, 0.8f, 0.2f)) continue;
                        uint v = Hashing.Mix(h, (uint)s + 3u);
                        ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Bush, AssetMix.Pick(shrubIds.Length > 0 ? shrubIds : ClippedShrubs, v), v);
                        g.Occ.Take(p, 0.9f, 4);
                    }
                    made++;
                }
            }
        }

        // ───────────────────────────── ground ─────────────────────────────

        private static void Ground(LayerContext ctx, Garden g)
        {
            int moss = ctx.I("mossLayer");
            if (ctx.B("mossPaint")) TerrainPaint.Patch(ctx, g.Inner.Ring, moss, 0.85f, 4f);
            if (g.Field != null)
            {
                if (ctx.B("gravelSurface"))
                {
                    string cat = AssetAvailability.Has(AssetCategories.SurfaceSand) ? AssetCategories.SurfaceSand : AssetCategories.SurfaceNatural;
                    ctx.Surface(g.Field, SurfaceRole.Ground, AssetMix.One(""), cat);
                }
                else
                {
                    TerrainPaint.Patch(ctx, g.Field, ctx.I("gravelLayer"), 1f, 0.8f);
                    int strokes = ZenKit.Rake(ctx, g.Field, g.FieldAxis, g.FieldGroups, ctx.S("raking"), 1.1f, ctx.I("rakeLayer"));
                    ctx.Plan.Facts["rakeStrokes"] = strokes;
                }
                if (ctx.B("mossRings"))
                    foreach (var (c, r) in g.FieldGroups)
                        TerrainPaint.Patch(ctx, LeisureKit.Blob(c, r * 0.75f + 0.4f, Hashing.Mix(ctx.Seed, (int)c.x, (int)c.y), 12, 0.2f), moss, 1f, 0.6f);
            }
            // A pebble beach (suhama) where the shore is widest open.
            if (g.Water.Ponds.Count > 0)
            {
                var shore = g.Water.Shore(8f);
                int made = 0;
                for (int i = 0; i < shore.Count && made < 2; i += math.max(1, shore.Count / 2))
                {
                    var (s, outward, island) = shore[(i + (int)(ctx.Seed % 7u)) % shore.Count];
                    if (island || g.Occ.Corridors.Overlaps(s + outward * 1.5f, 3f)) continue;
                    TerrainPaint.Patch(ctx, LeisureKit.Blob(s + outward * 0.5f, 3.5f, Hashing.Mix(ctx.Seed, (uint)i + 0x5A4u), 12, 0.25f), ctx.I("gravelLayer"), 1f, 1f);
                    made++;
                }
            }
        }
    }
}

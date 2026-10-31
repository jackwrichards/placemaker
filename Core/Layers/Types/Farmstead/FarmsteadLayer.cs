// Farmstead: a whole working farm, laid out the way farms actually are.
//
//  * Farmyard by the road (or centre / corner): a levelled gravel/dirt/paved yard with barns along
//    the back, a silo battery at one end, machinery parked nose-in along the front, hay stacks, a wind
//    pump and troughs. A driveway runs from the entrance to the yard front.
//  * Tracks: a spine track through the yard along the farm's long axis, optionally a grid of cross
//    tracks every second field.
//  * Fields on a lattice aligned with the spine (field size in ha), each shrunk by a headland. Fields
//    are arable (painted soil + painted row stripes in the chosen direction, optional crop props),
//    vegetable plots (crop props in tight rows), fallow, pasture (lone field trees, grazing animals) or
//    hay meadows (mown swaths and bales).
//  * Livestock paddocks around the yard: fenced rectangles with a gate facing the yard, trough,
//    field shelter, hay feeder and animal spawners; ranches/horse farms add a round pen.
//  * Hedgerows along the outline and along the field boundaries (gateways left in the middle of every
//    field side), with hedgerow trees; or a fence along the outline.
//  * A farm pond at the lowest ground (or by the yard / far away), with reeds and willows.
//
// Distinct from the allotments/urban farm (intensive city growing) and the animal park (visitor
// attraction): this is production farmland seen from the road. Fields, paddocks are "place:" regions
// so features (picnic, big trees, rocks) keep out of them; the yard is a "farmyard" region (decals).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Types.UrbanFarm;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Farmstead
{
    [AreaGenerator(69, Group = "farms")]
    public sealed partial class FarmsteadLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public const string YardTag = "farmyard";
        public const string FieldTag = "field";
        public const string FieldPlace = "place:field";
        public const string PaddockPlace = "place:paddock";

        protected override string Area => "Farmstead";
        public override string Id => "farmstead";
        public override string Icon => "Media/Game/Resources/Grain.svg";
        public override float MinArea => 3000f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core, YardTag };
        protected override bool HostsStreetShops => false;
        protected override int WantedEntrances(LayerRegion region) => region.Area > 60000f ? 2 : 1;

        private const int MaxCropItems = 500;
        private const float CropsPerHa = 150f;
        private const int MaxStripes = 1600;
        private const int MaxFields = 120;
        private const int MaxHedgeItems = 2500;
        private const float Headland = 3f;

        protected override List<FeatureInfo> BuildFeatures()
        {
            var yardDecals = Feature("decals", ("theme", "dirt"), ("density", 3.0));
            yardDecals.Target = YardTag;
            return new List<FeatureInfo>
            {
                Feature(FarmStandAddOn.LayerId, ("where", "entrances"), ("sells", "veg")),
                Feature(ApiaryAddOn.LayerId, ("hives", 6.0), ("where", "edge")),
                yardDecals,
                Feature("bigtrees", ("density", 1.0)),
                Feature("rocks", ("clusters", 2.0), ("size", "small")),
                Feature("picnic", ("groups", 1.0)),
                Feature("terrain", ("shape", "hills"), ("height", 2.0)),
            };
        }

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("subtype", "mixed", ControlTier.Essential, "identity", "dairy", "arable", "mixed", "hobby", "ranch").Tip();

            c.Choice("yardAt", "road", ControlTier.Essential, "layout", "road", "centre", "corner").Tip();
            c.Slider("yardSize", 8, 3, 25, 1, "%", ControlTier.Essential, "layout").Tip();
            c.Choice("tracks", "spine", ControlTier.Essential, "layout", "spine", "grid", "none").Tip();
            c.Asset("trackAsset", AssetCategories.Pathway, ControlTier.More, "layout");

            c.Slider("fieldSize", 1.2, 0.2, 5, 0.1, "ha", ControlTier.Essential, "fields").Tip();
            c.Slider("arablePct", 60, 0, 100, 5, "%", ControlTier.Essential, "fields").Tip();
            c.Choice("rowAngle", "auto", ControlTier.Essential, "fields", "auto", "along", "across", "alternate").Tip();
            c.Slider("rowSpacing", 4, 2, 10, 0.5, "m", ControlTier.More, "fields").Tip();
            c.Choice("cropProps", "edges", ControlTier.More, "fields", "none", "edges", "rows").Tip();
            c.Choice("season", "summer", ControlTier.Essential, "fields", "spring", "summer", "harvest", "autumn").Tip();
            c.Toggle("terraces", false, ControlTier.More, "fields").Tip();
            c.Toggle("hayMeadows", true, ControlTier.More, "fields").Tip();
            c.Toggle("fieldPaint", true, ControlTier.More, "fields").Tip();
            c.Slider("paintLayer", 2, 1, 4, 1, "count", ControlTier.More, "fields").When("fieldPaint=true").Tip();
            c.Slider("stripeLayer", 3, 1, 4, 1, "count", ControlTier.More, "fields").When("fieldPaint=true").Tip();
            c.AssetMulti("cropAssets", AssetCategories.FieldCrop, ControlTier.More, "fields");

            c.Slider("barns", 2, 0, 6, 1, "count", ControlTier.More, "farmyard");
            c.Slider("silos", 2, 0, 8, 1, "count", ControlTier.More, "farmyard");
            c.Toggle("machinery", true, ControlTier.More, "farmyard");
            c.Slider("vehicles", 2, 0, 6, 1, "count", ControlTier.Essential, "farmyard").Tip();
            c.Slider("yardMud", 50, 0, 100, 5, "%", ControlTier.Essential, "farmyard").Tip();
            c.Toggle("heaps", true, ControlTier.More, "farmyard").Tip();
            c.Toggle("hayStacks", true, ControlTier.More, "farmyard");
            c.Toggle("windpump", false, ControlTier.More, "farmyard").Tip();
            c.Choice("yardGround", "gravel", ControlTier.More, "farmyard", "gravel", "dirt", "paved");
            c.Toggle("levelYard", true, ControlTier.More, "farmyard").Tip();
            c.Asset("barnAsset", AssetCategories.FarmBarn, ControlTier.More, "farmyard");
            c.Asset("siloAsset", AssetCategories.FarmSilo, ControlTier.More, "farmyard");

            c.Slider("paddocks", 2, 0, 8, 1, "count", ControlTier.More, "livestock").Tip();
            c.Slider("paddockSize", 50, 20, 120, 5, "m", ControlTier.More, "livestock").Layout(15);
            c.Choice("livestock", "mixed", ControlTier.More, "livestock", "cattle", "sheep", "horses", "pigs", "mixed");
            c.Toggle("animals", true, ControlTier.More, "livestock").Tip();
            c.Toggle("shelters", true, ControlTier.More, "livestock");

            c.Choice("edge", "hedgerow", ControlTier.More, "hedgerows", "hedgerow", "fence", "both", "none");
            c.Toggle("fieldHedges", true, ControlTier.More, "hedgerows").Tip();
            c.Slider("hedgeTrees", 3, 0, 10, 1, "per100m", ControlTier.More, "hedgerows").Tip();
            c.AssetMulti("hedgeSpecies", AssetCategories.Bush, ControlTier.More, "hedgerows");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "hedgerows");
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "hedgerows");

            c.Choice("drainage", "none", ControlTier.Essential, "water", "none", "ditches", "ravine").Tip();
            c.Slider("tanks", 2, 0, 6, 1, "count", ControlTier.More, "water").Tip();
            c.Toggle("pond", true, ControlTier.More, "water");
            c.Slider("pondSize", 30, 12, 80, 2, "m", ControlTier.More, "water").Layout(8).When("pond=true").Tip();
            c.Choice("pondAt", "lowest", ControlTier.More, "water", "lowest", "nearYard", "far").When("pond=true").Tip();

            BiomeMix.Define(c, Biomes);
            EntranceMarkers(c);

            c.RandomRange("fieldSize", 0.6, 2.5);
            c.RandomRange("yardSize", 5, 12);
            c.RandomRange("paddocks", 0, 4);
            c.Fixed("fieldPaint");
            c.Fixed("paintLayer");
            c.Fixed("stripeLayer");
            c.Fixed("levelYard");
        }

        protected override void DefinePresets()
        {
            // traditional farms first (the user: "more traditional farms should be the first options"), then the animal steads
            Preset("mixed", BiomeMix.With(new (string, object)[] { ("subtype", "mixed"), ("arablePct", 60.0), ("paddocks", 2.0), ("livestock", "mixed"), ("fieldSize", 1.2),
                ("rowAngle", "alternate"), ("pond", true), ("season", "summer"), ("vehicles", 3.0), ("yardMud", 55.0), ("drainage", "ditches"), ("tanks", 2.0) },
                BiomeMix.Preset(Biomes, 70, ("fields", 78), ("orchard", 8), ("forest", 10), ("wetland", 4))));
            Preset("arable", BiomeMix.With(new (string, object)[] { ("subtype", "arable"), ("arablePct", 100.0), ("paddocks", 0.0), ("silos", 5.0), ("barns", 2.0), ("fieldSize", 3.0),
                ("rowSpacing", 5.0), ("fieldHedges", false), ("hedgeTrees", 1.0), ("tracks", "grid"), ("pond", false), ("cropProps", "edges"),
                ("rowAngle", "auto"), ("yardGround", "paved"), ("season", "harvest"), ("vehicles", 4.0), ("yardMud", 30.0), ("drainage", "ditches"), ("tanks", 3.0) },
                BiomeMix.Preset(Biomes, 80, ("fields", 92), ("forest", 8))));
            Preset("fruitFarm", BiomeMix.With(new (string, object)[] { ("subtype", "mixed"), ("arablePct", 40.0), ("paddocks", 0.0), ("fieldSize", 1.0), ("silos", 0.0), ("barns", 2.0),
                ("rowAngle", "auto"), ("pond", true), ("pondSize", 24.0), ("season", "summer"), ("vehicles", 2.0), ("yardMud", 35.0), ("drainage", "none"), ("tanks", 3.0),
                ("edge", "hedgerow"), ("hedgeTrees", 2.0), ("yardGround", "gravel") },
                BiomeMix.Preset(Biomes, 75, ("fields", 35), ("orchard", 55), ("forest", 6), ("wetland", 4))));
            Preset("hillFarm", BiomeMix.With(new (string, object)[] { ("subtype", "mixed"), ("arablePct", 30.0), ("paddocks", 3.0), ("livestock", "sheep"), ("fieldSize", 1.4),
                ("rowAngle", "auto"), ("terraces", true), ("edge", "fence"), ("fieldHedges", false), ("hedgeTrees", 0.0), ("silos", 0.0), ("barns", 2.0), ("pond", false),
                ("season", "autumn"), ("vehicles", 2.0), ("yardMud", 70.0), ("drainage", "ravine"), ("tanks", 2.0), ("yardGround", "dirt") },
                BiomeMix.Preset(Biomes, 70, ("fields", 48), ("pasture", 30), ("forest", 12), ("rocky", 10))));
            Preset("dairy", BiomeMix.With(new (string, object)[] { ("subtype", "dairy"), ("arablePct", 25.0), ("paddocks", 4.0), ("paddockSize", 60.0), ("livestock", "cattle"),
                ("silos", 3.0), ("barns", 3.0), ("fieldSize", 1.5), ("edge", "hedgerow"), ("hayMeadows", true), ("cropProps", "none"),
                ("rowAngle", "along"), ("pond", true), ("season", "summer"), ("vehicles", 3.0), ("yardMud", 80.0), ("drainage", "ditches"), ("tanks", 4.0) },
                BiomeMix.Preset(Biomes, 70, ("fields", 82), ("forest", 8), ("wetland", 10))));
            Preset("hobby", ("subtype", "hobby"), ("fieldSize", 0.3), ("arablePct", 70.0), ("cropProps", "rows"), ("paddocks", 1.0),
                ("paddockSize", 30.0), ("livestock", "horses"), ("barns", 1.0), ("silos", 0.0), ("machinery", false), ("edge", "fence"),
                ("pond", true), ("pondSize", 16.0), ("yardSize", 6.0), ("windpump", true), ("rowSpacing", 2.5), ("yardGround", "dirt"), ("vehicles", 1.0), ("tanks", 1.0));
            Preset("ranch", ("subtype", "ranch"), ("arablePct", 0.0), ("paddocks", 6.0), ("paddockSize", 100.0), ("livestock", "cattle"),
                ("edge", "fence"), ("fieldHedges", false), ("hedgeTrees", 0.0), ("barns", 2.0), ("silos", 1.0), ("hayStacks", true),
                ("yardGround", "dirt"), ("pond", true), ("pondSize", 40.0), ("windpump", true), ("tracks", "spine"), ("fieldSize", 3.0),
                ("season", "harvest"), ("vehicles", 3.0), ("yardMud", 60.0), ("drainage", "ravine"), ("tanks", 4.0));
        }

        // ───────────── shared geometry helpers ─────────────

        /// <summary>Exact footprints (yard, pond, paddocks) with a bounding-box prefilter; queried with a pad.</summary>
        internal sealed class Footprints
        {
            public readonly List<float2[]> Rings = new List<float2[]>();
            private readonly List<float4> _boxes = new List<float4>();

            public void Add(float2[] ring)
            {
                if (ring == null || ring.Length < 3) return;
                Poly.Bounds(ring, out var mn, out var mx);
                Rings.Add(ring);
                _boxes.Add(new float4(mn, mx));
            }

            public bool Hits(float2 p, float pad = 0f)
            {
                for (int i = 0; i < Rings.Count; i++)
                {
                    var b = _boxes[i];
                    if (p.x < b.x - pad || p.y < b.y - pad || p.x > b.z + pad || p.y > b.w + pad) continue;
                    if (Poly.Contains(Rings[i], p)) return true;
                    if (pad > 0f && Poly.DistanceToBoundary(Rings[i], p) < pad) return true;
                }
                return false;
            }

            /// <summary>True when the rectangle grown by <paramref name="pad"/> overlaps any footprint.</summary>
            public bool HitsRect(OrientedRect r, float pad)
            {
                var g = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength + pad, HalfWidth = r.HalfWidth + pad };
                for (float u = -g.HalfLength; u <= g.HalfLength + 0.01f; u += math.max(1f, g.HalfLength / 6f))
                    for (float v = -g.HalfWidth; v <= g.HalfWidth + 0.01f; v += math.max(1f, g.HalfWidth / 6f))
                        if (Hits(g.Local(u, v))) return true;
                foreach (var ring in Rings)
                    foreach (var q in ring)
                    {
                        var d = q - g.Center;
                        if (math.abs(math.dot(d, g.Axis)) <= g.HalfLength && math.abs(math.dot(d, g.Side)) <= g.HalfWidth) return true;
                    }
                return false;
            }

            public bool BoxOverlaps(float2 mn, float2 mx, int i)
            {
                var b = _boxes[i];
                return !(mx.x < b.x || mx.y < b.y || mn.x > b.z || mn.y > b.w);
            }
        }

        /// <summary>Rectangle fitted to a ring along the given axis (projection extents).</summary>
        internal static OrientedRect FitRect(float2[] ring, float2 axis)
        {
            axis = math.normalizesafe(axis, new float2(1, 0));
            var side = new float2(-axis.y, axis.x);
            var c0 = Poly.Centroid(ring);
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var p in ring)
            {
                var d = p - c0;
                minA = math.min(minA, math.dot(d, axis)); maxA = math.max(maxA, math.dot(d, axis));
                minB = math.min(minB, math.dot(d, side)); maxB = math.max(maxB, math.dot(d, side));
            }
            return new OrientedRect
            {
                Center = c0 + axis * (minA + maxA) * 0.5f + side * (minB + maxB) * 0.5f, Axis = axis,
                HalfLength = (maxA - minA) * 0.5f, HalfWidth = (maxB - minB) * 0.5f,
            };
        }

        /// <summary>Pieces of a→b (sampled every <paramref name="step"/> m) where <paramref name="blocked"/> is false.</summary>
        internal static List<(float2 a, float2 b)> Runs(float2 a, float2 b, Func<float2, bool> blocked, float step = 2f, float minLen = 8f)
        {
            var r = new List<(float2, float2)>();
            float len = math.distance(a, b);
            int n = math.max(1, (int)math.ceil(len / step));
            bool inRun = false; float2 start = a, last = a;
            for (int i = 0; i <= n; i++)
            {
                var p = math.lerp(a, b, i / (float)n);
                bool ok = !blocked(p);
                if (ok && !inRun) { start = p; inRun = true; }
                if (ok) last = p;
                if ((!ok || i == n) && inRun)
                {
                    if (math.distance(start, last) >= minLen) r.Add((start, last));
                    inRun = false;
                }
            }
            return r;
        }

        /// <summary>Clip a long line through p along dir to the region, as inside pieces.</summary>
        internal static List<(float2 a, float2 b)> Line(LayerRegion region, float2 p, float2 dir)
        {
            float reach = math.length(region.Max - region.Min) + 10f;
            return Poly.ClipSegmentToPolygon(region.Ring, p - dir * reach, p + dir * reach);
        }

        /// <summary>Thin paint rectangles along rows (direction <paramref name="dir"/>, every 2·spacing) clipped to a ring.</summary>
        internal static int Stripes(LayerContext ctx, float2[] ring, float2 dir, float spacing, int layer, float strength, int budget)
        {
            if (budget <= 0) return 0;
            var fr = LayerRegion.From(ring);
            if (fr == null) return 0;
            var perp = new float2(-dir.y, dir.x);
            var c = fr.Centroid;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var q in ring) { float t = math.dot(q - c, perp); lo = math.min(lo, t); hi = math.max(hi, t); }
            int n = 0;
            for (float t = lo + spacing * 0.75f; t < hi - spacing * 0.5f && n < budget; t += spacing * 2f)
                foreach (var (a, b) in Line(fr, c + perp * t, dir))
                {
                    float len = math.distance(a, b);
                    if (len < 4f) continue;
                    var rect = LayerRegion.Rect((a + b) * 0.5f, dir, len * 0.5f - 0.5f, spacing * 0.5f);
                    ctx.Plan.Paint.Add(new PlannedPaint { Ring = Poly.To3D(rect, ctx.H), Layer = math.clamp(layer, 1, 4), Strength = strength });
                    if (++n >= budget) break;
                }
            return n;
        }

        internal static AssetRef Spawner(string livestock, int index)
        {
            if (livestock == "mixed")
            {
                var kinds = new[] { "cattle", "sheep", "pigs", "horses" };
                livestock = kinds[index % kinds.Length];
            }
            // Spawner prefab names are a best guess (catalog-dump pending); automatic falls back to the role default.
            switch (livestock)
            {
                case "sheep": return new AssetRef("", "Sheep Spawner", "Farm Animal Spawner");
                case "horses": return new AssetRef("", "Horse Spawner", "Farm Animal Spawner");
                case "pigs": return new AssetRef("", "Pig Spawner", "Farm Animal Spawner");
                default: return new AssetRef("", "Cow Spawner", "Cattle Spawner", "Farm Animal Spawner");
            }
        }

        private enum FieldKind { Grain, Veg, Fallow, Pasture, Hay }

        private sealed class Field
        {
            public float2[] Ring;
            public LayerRegion Region;
            public int A, B;
            public FieldKind Kind;
            public float2 RowDir;
        }

        // ───────────── planning ─────────────

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            LeisureKit.Frame(region, out var centre, out var axis, out var side, out _, out _);
            var res = ctx.Input.Reserved ?? new ReservedSpace();
            var hard = new Footprints();     // exact yard / pond / paddock footprints
            string sub = ctx.S("subtype");
            string livestock = ctx.S("livestock");
            var trackAsset = AssetMix.One(ctx.S("trackAsset"));
            var treeIds = ctx.L("treeSpecies");
            var hedgeIds = ctx.L("hedgeSpecies");
            // (P2-3) a farm's fences are wooden post-and-rail (the animal park's paddock fence), never the city's railings
            var fenceAsset = string.IsNullOrEmpty(ctx.S("fenceAsset")) ? AnimalPark.AnimalParkArt.PenFence("", "paddocks") : AssetMix.One(ctx.S("fenceAsset"));
            int dirtLayer = ctx.I("paintLayer");
            bool paint = ctx.B("fieldPaint");

            // 1. Farmyard.
            bool hasYard = PlaceYard(ctx, region, res, gates, out var yard);
            if (!hasYard) ctx.Issue(IssueSeverity.Info, "Placemaker.Farmstead.Issue.NoYard");
            var origin = hasYard ? yard.Center : centre;
            float frontSign = 1f;
            if (hasYard)
            {
                hard.Add(yard.Ring);
                var toward = gates.Count > 0 ? gates[0].Position : region.ClosestOnBoundary(yard.Center, out _, out _);
                frontSign = math.dot(toward - yard.Center, yard.Side) >= 0f ? 1f : -1f;
            }
            ctx.Check();

            // 2. Driveway from each entrance to the yard front (claimed before paddocks and the pond).
            var drive = new Footprints();
            if (hasYard)
            {
                var front = yard.Center + yard.Side * frontSign * yard.HalfWidth;
                foreach (var g in gates)
                {
                    var target = front;
                    if (region.CrossesBoundary(g.Position, target, true) || math.distance(g.Position, target) < 2f) continue;
                    LeisureKit.Path(ctx, occ, g.Position, target, NetRole.Pathway, trackAsset);
                    var d = math.normalizesafe(target - g.Position);
                    drive.Add(LayerRegion.Rect((g.Position + target) * 0.5f, d, math.distance(g.Position, target) * 0.5f, 4f));
                }
            }

            // 2b. The track lines are claimed before the pond and the paddocks, so the spine (and the grid) run unbroken
            // from the yard to the outline: placed afterwards they were cut into stubs that ended in the grass (rf-farms).
            string trackMode = ctx.S("tracks");
            float fieldArea = ctx.F("fieldSize") * 10000f;
            // Small farms still get a few fields; huge ones never exceed the field budget.
            fieldArea = math.clamp(fieldArea, region.Area / MaxFields, math.max(1500f, region.Area / 4f));
            float Lf = math.sqrt(fieldArea * 1.6f), Wf = Lf / 1.6f;
            if (trackMode != "none")
            {
                foreach (var (a, b) in Line(region, origin, axis))
                    drive.Add(LayerRegion.Rect((a + b) * 0.5f, axis, math.distance(a, b) * 0.5f, 3.5f));
                if (trackMode == "grid")
                {
                    Lattice(region, origin, axis, side, Lf, Wf, out int ja0, out int ja1, out _, out _);
                    for (int ia = ja0 + 1; ia <= ja1; ia++)
                        if (ia % 2 == 0)
                            foreach (var (a, b) in Line(region, origin + axis * (ia * Lf), side))
                                drive.Add(LayerRegion.Rect((a + b) * 0.5f, side, math.distance(a, b) * 0.5f, 3.5f));
                }
            }

            // 3. Pond.
            float2 pondC = default; float pondR = 0f;
            if (ctx.B("pond") && FindPond(ctx, region, occ, hard, drive, hasYard ? yard.Center : centre, out pondC, out pondR))
            {
                var lake = LeisureKit.Blob(pondC, pondR, Hashing.Mix(ctx.Seed, 0x90DDu), 14, 0.18f);
                ParkLayer.EmitLake(ctx, lake, 2.5f);
                hard.Add(LayerRegion.Circle(pondC, pondR * 1.2f + 1f, 20));
                occ.Take(pondC, pondR * 1.2f + 0.5f, 0);
            }
            ctx.Check();

            // 4. Paddocks (and a round pen) around the yard.
            var paddocks = PlacePaddocks(ctx, region, hard, drive, origin, axis, out var pen, out float penR);
            if (hasYard) Heaps(ctx, region, occ, hard, yard, frontSign);
            ctx.Check();

            // 5. Tracks: spine along the farm's axis through the yard, cross tracks every second field.
            Func<float2, bool> trackBlocked = p => hard.Hits(p, 1.5f) || !region.ContainsDisc(p, 3f);
            var trackLinesB = new HashSet<int>();   // lattice lines along the axis carrying a track
            var trackLinesA = new HashSet<int>();   // lattice lines across the axis carrying a track
            if (trackMode != "none")
            {
                foreach (var (a, b) in Line(region, origin, axis))
                    foreach (var (ra, rb) in Runs(a, b, trackBlocked))
                        LeisureKit.Path(ctx, occ, ra, rb, NetRole.Pathway, trackAsset);
                trackLinesB.Add(0);
                if (trackMode == "grid")
                {
                    Lattice(region, origin, axis, side, Lf, Wf, out int ia0, out int ia1, out _, out _);
                    for (int ia = ia0 + 1; ia <= ia1; ia++)
                    {
                        if (ia % 2 != 0) continue;
                        var p = origin + axis * (ia * Lf);
                        foreach (var (a, b) in Line(region, p, side))
                            foreach (var (ra, rb) in Runs(a, b, trackBlocked))
                                LeisureKit.Path(ctx, occ, ra, rb, NetRole.Pathway, trackAsset);
                        trackLinesA.Add(ia);
                    }
                }
            }
            ctx.Check();

            // 5b. Drainage: field ditches along the boundaries, or a ravine down the fall line (the fields keep out of it).
            Drainage(ctx, region, occ, hard, origin, axis, side, Lf, Wf, trackLinesB, treeIds, out int channels);
            ctx.Check();
            // 5c. The landscape (Biome mix): orchards in rows, rough grazing, woods, wet ground, ponds, rock where the land wants
            // them; the fields take the rest (every area held out of the field lattice, so a field's edge follows a wood's).
            var gatePts = new List<float2>();
            foreach (var g in gates) gatePts.Add(g.Position);
            var biomes = BiomeHost.Plan(ctx, occ, Biomes, gatePts, p => hard.Hits(p, 3f) || drive.Hits(p, 1f), rowAxis: axis);
            if (biomes != null) foreach (var a in biomes.Areas) hard.Add(a);
            ctx.Check();

            // 6. Fields on the lattice.
            var fields = BuildFields(ctx, region, hard, origin, axis, side, Lf, Wf, fieldArea);
            AssignKinds(ctx, fields, axis, side, sub);
            int season = SeasonIndex(ctx.S("season"));
            ctx.Plan.Facts[Decals.Dynamic.FarmGround.SeasonFact] = season;
            ctx.Plan.Facts[Decals.Dynamic.FarmGround.MudFact] = ctx.F("yardMud") / 100.0;
            // hill land: terraced benches on the steep fields (the hill farm)
            if (ctx.B("terraces"))
            {
                var fr = new List<LayerRegion>();
                foreach (var f in fields) fr.Add(f.Region);
                Terraces(ctx, fr, occ, 2.2f);
            }
            ctx.Check();
            int crops = 0, stripes = 0, bales = 0, fieldTrees = 0, spawners = 0;
            var cropIds = ctx.L("cropAssets");
            string cropMode = ctx.S("cropProps");
            bool needCrops = cropMode != "none" || fields.Exists(f => f.Kind == FieldKind.Veg);
            // Real field crops only (installed or picked): no garden-crop fallback, which in vanilla is raised-bed corner
            // pieces (rows of grey boxes). Without crop plants a field is its crop surface and wheel ruts — no note.
            string cropCat = needCrops && (cropIds.Length > 0 || AssetAvailability.Has(AssetCategories.FieldCrop)) ? AssetCategories.FieldCrop : null;
            bool hasHay = fields.Exists(f => f.Kind == FieldKind.Hay) || ctx.B("hayStacks");
            string hayCat = hasHay ? LeisureKit.Category(ctx, "", AssetCategories.FarmHay) : null;
            float rs = ctx.F("rowSpacing");
            int stripeLayer = ctx.I("stripeLayer");
            // Ground before props (GroundCover): each field is one crop surface — stubble/straw for grain,
            // vegetable rows, ploughed fallow, green hay meadow — with terrain paint only as the fallback when
            // no farmland surface exists. Crop plants are accents on the row heads; wheel ruts show the rows.
            int cropBudget = GroundCover.AccentBudget(region.Area, CropsPerHa, 30, MaxCropItems), tracksBudget = 60;
            int arableFields = 0;
            foreach (var f in fields) if (f.Kind == FieldKind.Grain || f.Kind == FieldKind.Veg) arableFields++;
            int perField = math.max(8, cropBudget / math.max(1, arableFields));
            int fieldSurfaces = 0, irrigation = 0;
            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                uint v = Hashing.Mix(ctx.Seed, 0xF1E1u + (uint)i);
                // A growing crop and its stubble are the crop surface (the vanilla field rows read as a crop from 45°; a green crop
                // painted on grass did not show at all: run show-5-builds-1030-233210-474c). The bare soil of spring drilling,
                // autumn ploughing and fallow is terrain paint at full strength, with the rows striped in.
                bool bare = f.Kind == FieldKind.Fallow || (f.Kind == FieldKind.Grain || f.Kind == FieldKind.Veg) && (season == 0 || season == 3);
                if (bare && paint) stripes += PaintField(ctx, f.Ring, f.Kind == FieldKind.Fallow ? "Fallow" : "Grain", f.RowDir, rs, season, MaxStripes - stripes);
                else if (f.Kind == FieldKind.Grain && GroundCover.Field(ctx, f.Ring, GroundCover.Straw)) fieldSurfaces++;
                else if (f.Kind == FieldKind.Veg && GroundCover.Field(ctx, f.Ring, GroundCover.Rows)) fieldSurfaces++;
                else if (f.Kind == FieldKind.Hay && GroundCover.Field(ctx, f.Ring, GroundCover.Green)) fieldSurfaces++;
                else if (paint && f.Kind != FieldKind.Pasture) stripes += PaintField(ctx, f.Ring, f.Kind.ToString(), f.RowDir, rs, season, MaxStripes - stripes);
                switch (f.Kind)
                {
                    case FieldKind.Grain:
                        // Arable farms water their big fields: an irrigation rig standing across the rows.
                        if (sub == "arable" && irrigation < 3 && f.Region.Area > 2500f)
                        {
                            var at = f.Region.DeepestPoint(out float clear, null, 10);
                            if (clear > 9f && LeisureKit.Put(ctx, occ, at, FarmKit.Yaw(f.RowDir), ObjectRole.Furniture, AssetMix.One(FarmArt.Irrigation),
                                AssetCategories.FarmMachinery, 8.6f, Hashing.Mix(v, 0x1A6u), 0.5f, 0.3f)) irrigation++;
                        }
                        GroundCover.RowTracks(ctx, occ, f.Ring, f.RowDir, ref tracksBudget, v, 6);
                        if (cropCat != null && cropMode != "none")
                            crops += GroundCover.RowHeads(ctx, occ, f.Ring, f.RowDir, rs, 2.5f, cropMode == "rows" ? 6f : 2.5f, cropMode == "rows" ? 1 : 3,
                                CropRole(cropCat), cropCat, cropIds, perField, ref cropBudget, v);
                        break;
                    case FieldKind.Veg:
                        if (cropCat != null)
                            crops += GroundCover.RowHeads(ctx, occ, f.Ring, f.RowDir, math.max(2f, rs * 0.6f), 2.2f, 4.5f, 1,
                                CropRole(cropCat), cropCat, cropIds, perField, ref cropBudget, v);
                        break;
                    case FieldKind.Fallow:
                        GroundCover.RowTracks(ctx, occ, f.Ring, f.RowDir, ref tracksBudget, v, 4);
                        break;
                    case FieldKind.Hay:
                        GroundCover.RowTracks(ctx, occ, f.Ring, f.RowDir, ref tracksBudget, v, 4);
                        if (hayCat != null) bales += HayBales(ctx, occ, f, hayCat, v);
                        break;
                    case FieldKind.Pasture:
                    {
                        // A lone field tree (parkland oak) in some pastures, grazing animals in others.
                        if (Hashing.Unit(Hashing.Mix(v, 3u)) < 0.6f)
                        {
                            var p = f.Region.DeepestPoint(out float clear, null, 10);
                            p += new float2(Hashing.Unit(Hashing.Mix(v, 4u)) - 0.5f, Hashing.Unit(Hashing.Mix(v, 5u)) - 0.5f) * math.min(clear, 20f);
                            if (f.Region.Contains(p) && LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.5f, v, 1f, 0.4f, 3, 3))
                                fieldTrees++;
                        }
                        if (ctx.B("animals") && spawners < 8 && (sub != "arable"))
                        {
                            var p = f.Region.DeepestPoint(out _, null, 8);
                            if (occ.Free(p, 1f, 1f, 0.2f))
                            {
                                ctx.Object(p, 0f, ObjectRole.AnimalSpawner, Spawner(livestock, i), v);
                                spawners++;
                            }
                        }
                        break;
                    }
                }
                if ((i & 7) == 7) ctx.Check();
            }
            if (fields.Count == 0) ctx.Issue(IssueSeverity.Warning, "Placemaker.Farmstead.Issue.NoFields");

            // 7. Farmyard contents.
            int barns = 0, silos = 0;
            if (hasYard) DressYard(ctx, region, occ, res, yard, frontSign, hayCat, treeIds, out barns, out silos);
            ctx.Check();

            // 8. Paddock contents.
            for (int i = 0; i < paddocks.Count; i++)
                spawners += DressPaddock(ctx, occ, paddocks[i], origin, livestock, i, fenceAsset, paint ? dirtLayer : 0, hayCat);
            if (penR > 0f) DressPen(ctx, occ, pen, penR, origin, fenceAsset, paint ? dirtLayer : 0);
            ctx.Check();

            // 9. Pond planting: reeds/bushes around the bank (gaps for access), a couple of willows.
            if (pondR > 0f) PondPlanting(ctx, occ, pondC, pondR, treeIds, hedgeIds);

            // 10. Boundaries: hedgerows and/or fence on the outline, hedgerows between fields.
            string edge = ctx.S("edge");
            int hedgeItems = 0; float hedgeLen = 0f;
            float treeEvery = ctx.F("hedgeTrees") > 0f ? 100f / ctx.F("hedgeTrees") : float.MaxValue;
            string hedgeCat = edge == "hedgerow" || edge == "both" || ctx.B("fieldHedges")
                ? LeisureKit.Category(ctx, hedgeIds.Length > 0 ? "x" : "", AssetCategories.Hedge, AssetCategories.Bush) : null;
            if (hedgeIds.Length > 0) hedgeCat = AssetCategories.Bush;
            if (edge == "fence" || edge == "both") Fence(ctx, gates, fenceAsset, 4f);
            if (hedgeCat != null && (edge == "hedgerow" || edge == "both"))
                hedgeItems += EdgeHedge(ctx, occ, hard, gates, hedgeCat, hedgeIds, treeIds, treeEvery, MaxHedgeItems, ref hedgeLen);
            if (hedgeCat != null && ctx.B("fieldHedges"))
                hedgeItems += FieldHedges(ctx, region, occ, hard, origin, axis, side, Lf, Wf, trackLinesA, trackLinesB, hedgeCat, hedgeIds, treeIds,
                    treeEvery, MaxHedgeItems - hedgeItems, ref hedgeLen);
            ctx.Check();
            // Ranch: the pastures are fenced range, a gate toward the yard (the grass alone read as one empty lawn, rf-farms).
            if (sub == "ranch")
            {
                int fenced = 0;
                foreach (var f in fields)
                {
                    if ((f.Kind != FieldKind.Pasture && f.Kind != FieldKind.Hay) || fenced >= 24) continue;
                    var gp = f.Region.ClosestOnBoundary(origin, out int gs, out float gt);
                    foreach (var run in Fences.Runs(f.Region, new List<Gate> { new Gate { Position = gp, Segment = gs, T = gt } }, 2f))
                    {
                        ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fenceAsset);
                        occ.TakeCorridor(run.c0, run.c1, 0.45f, 5);
                    }
                    fenced++;
                }
                ctx.Plan.Facts["fencedPastures"] = fenced;
            }

            // 11. Regions for features: yard, fields and paddocks (places), free grass as green/core.
            if (hasYard)
            {
                ctx.AddRegion(yard.Ring, YardTag, YardTag);
                GateMark(ctx, yard.Center + yard.Side * frontSign * yard.HalfWidth, "yard", 0);
            }
            for (int i = 0; i < gates.Count; i++) GateMark(ctx, gates[i].Position, "road", i);
            var fieldPrints = new Footprints();
            for (int i = 0; i < fields.Count; i++)
            {
                var fk = fields[i];
                float deg = math.degrees(math.atan2(fk.RowDir.y, fk.RowDir.x));
                ctx.AddRegion(fk.Ring, "field:" + i, FieldPlace, FieldTag, Decals.Dynamic.FarmGround.KindTag + fk.Kind,
                    Decals.Dynamic.FarmGround.RowsTag + deg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                if (fk.Kind != FieldKind.Pasture || i % 2 == 0) GateMark(ctx, fk.Region.ClosestOnBoundary(origin, out _, out _), fk.Kind == FieldKind.Pasture ? "paddock" : "field", i);
                fieldPrints.Add(fields[i].Ring);
            }
            for (int i = 0; i < paddocks.Count; i++) ctx.AddRegion(paddocks[i].Ring, "paddock:" + i, PaddockPlace);
            float cell = math.max(2.5f, math.sqrt(region.Area / 12000f));
            var pieces = FreeSpace.Pieces(region, p => occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f) || hard.Hits(p) || fieldPrints.Hits(p),
                cell, 200f, ctx.Cancel);
            if (pieces.Count == 0) ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core);
            for (int i = 0; i < pieces.Count && i < 16; i++)
                if (i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);

            // 12. Stats.
            CommonStats(ctx);
            double fieldHa = 0; int arable = 0, pasture = 0;
            foreach (var f in fields)
            {
                fieldHa += f.Region.Area / 10000.0;
                if (f.Kind == FieldKind.Pasture || f.Kind == FieldKind.Hay) pasture++; else arable++;
            }
            ctx.Stat("Placemaker.Stat.Fields", fields.Count, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.FieldArea", Math.Round(fieldHa, 1), "ha");
            ctx.Stat("Placemaker.Stat.Paddocks", paddocks.Count + (penR > 0f ? 1 : 0), Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Barns", barns, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Silos", silos, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Hedgerow", Math.Round(hedgeLen), Keys.UnitM);
            ctx.Plan.Facts["fields"] = fields.Count;
            ctx.Plan.Facts["arableFields"] = arable;
            ctx.Plan.Facts["pastureFields"] = pasture;
            ctx.Plan.Facts["cropItems"] = crops;
            ctx.Plan.Facts["stripes"] = stripes;
            ctx.Plan.Facts["fieldSurfaces"] = fieldSurfaces;
            ctx.Plan.Facts["hayBales"] = bales;
            ctx.Plan.Facts["hedgeItems"] = hedgeItems;
            ctx.Plan.Facts["fieldTrees"] = fieldTrees;
            ctx.Plan.Facts["spawners"] = spawners;
        }

        private static bool PlaceYard(LayerContext ctx, LayerRegion region, ReservedSpace res, List<Gate> gates, out OrientedRect yard)
        {
            string at = ctx.S("yardAt");
            var rule = new PlaceRule
            {
                Shape = PlaceShape.Rectangle,
                Anchor = at == "corner" ? PlaceAnchor.Corner : at == "centre" ? PlaceAnchor.Centre : PlaceAnchor.Entrance,
                SizePct = ctx.F("yardSize"), MinM = 22f, MaxM = 120f, Aspect = 1.5f, Margin = 5f,
            };
            var placed = PlaceSolver.Solve(region, res.Polygons, rule, LeisureKit.Points(gates), Hashing.Mix(ctx.Seed, 0x7A4Du));
            yard = default;
            if (!placed.Ok) return false;
            yard = FitRect(placed.Ring, placed.Axis);
            return yard.HalfLength > 5f && yard.HalfWidth > 4f;
        }

        private static bool FindPond(LayerContext ctx, LayerRegion region, Occupancy occ, Footprints hard, Footprints drive, float2 yardC,
            out float2 best, out float r)
        {
            r = ctx.F("pondSize") * 0.5f;
            best = default;
            string at = ctx.S("pondAt");
            for (int attempt = 0; attempt < 4; attempt++, r *= 0.8f)
            {
                if (r < 5f) break;
                float step = math.max(4f, math.sqrt(region.Area / 900f));
                float bestScore = float.MaxValue; bool found = false;
                for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
                    for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                    {
                        var p = new float2(x, y);
                        if (!region.ContainsDisc(p, r * 1.25f + 6f)) continue;
                        if (!occ.Free(p, r * 1.25f + 2f, 6f, 1f)) continue;
                        if (hard.Hits(p, r * 1.25f + 5f) || drive.Hits(p, r * 1.25f + 2f)) continue;
                        float jitter = Hashing.Unit(Hashing.Mix(ctx.Seed, (int)x, (int)y)) * 0.01f;
                        float score = at == "nearYard" ? math.distance(p, yardC) : at == "far" ? -math.distance(p, yardC) : ctx.H(p) + math.distance(p, yardC) * 1e-4f;
                        score += jitter;
                        if (score < bestScore) { bestScore = score; best = p; found = true; }
                    }
                if (found) return true;
            }
            ctx.Issue(IssueSeverity.Info, "Placemaker.Farmstead.Issue.NoPond");
            r = 0f;
            return false;
        }

        private static List<OrientedRect> PlacePaddocks(LayerContext ctx, LayerRegion region, Footprints hard, Footprints drive, float2 origin, float2 axis,
            out float2 pen, out float penR)
        {
            var result = new List<OrientedRect>();
            int wanted = ctx.I("paddocks");
            float size = ctx.F("paddockSize");
            pen = default; penR = 0f;
            bool wantPen = ctx.S("subtype") == "ranch" || ctx.S("livestock") == "horses";
            float phase = Hashing.Unit(Hashing.Mix(ctx.Seed, 0x9ADu)) * 6.283f;
            bool Fits(OrientedRect r, float margin)
            {
                var g = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength + margin, HalfWidth = r.HalfWidth + margin };
                foreach (var q in g.Ring) if (!region.Contains(q)) return false;
                var ring = g.Ring;
                for (int i = 0; i < 4; i++) if (region.CrossesBoundary(ring[i], ring[(i + 1) % 4], false)) return false;
                return !hard.HitsRect(r, margin) && !drive.HitsRect(r, 1f);
            }
            for (int k = 0; k < wanted; k++)
            {
                bool placed = false;
                for (float s = size; s >= math.max(16f, size * 0.5f) && !placed; s *= 0.8f)
                {
                    float hl = s * 0.5f, hw = s * 0.5f / 1.3f;
                    for (int ringI = 0; ringI < 6 && !placed; ringI++)
                    {
                        float dist = 12f + hw + ringI * s * 0.6f;
                        for (int j = 0; j < 16 && !placed; j++)
                        {
                            float a = phase + j * math.PI / 8f;
                            var c = origin + new float2(math.cos(a), math.sin(a)) * (dist + hl * 0.5f);
                            var r = new OrientedRect { Center = c, Axis = axis, HalfLength = hl, HalfWidth = hw };
                            if (!Fits(r, 3f)) continue;
                            result.Add(r);
                            hard.Add(r.Ring);
                            placed = true;
                        }
                    }
                }
                if (!placed) break;
                ctx.Check();
            }
            if (result.Count < wanted) ctx.Issue(IssueSeverity.Info, "Placemaker.Farmstead.Issue.FewerPaddocks", result.Count, wanted);
            if (wantPen)
            {
                const float R = 9f;
                for (int ringI = 0; ringI < 8 && penR <= 0f; ringI++)
                    for (int j = 0; j < 12; j++)
                    {
                        float a = phase + 0.4f + j * math.PI / 6f;
                        var c = origin + new float2(math.cos(a), math.sin(a)) * (10f + R + ringI * 8f);
                        var r = new OrientedRect { Center = c, Axis = axis, HalfLength = R, HalfWidth = R };
                        if (!Fits(r, 2.5f)) continue;
                        pen = c; penR = R;
                        hard.Add(LayerRegion.Circle(c, R + 0.5f, 16));
                        break;
                    }
            }
            return result;
        }

        private static void Lattice(LayerRegion region, float2 origin, float2 axis, float2 side, float Lf, float Wf,
            out int ia0, out int ia1, out int ib0, out int ib1)
        {
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var v in region.Ring)
            {
                var d = v - origin;
                minA = math.min(minA, math.dot(d, axis)); maxA = math.max(maxA, math.dot(d, axis));
                minB = math.min(minB, math.dot(d, side)); maxB = math.max(maxB, math.dot(d, side));
            }
            ia0 = (int)math.floor(minA / Lf); ia1 = (int)math.ceil(maxA / Lf) - 1;
            ib0 = (int)math.floor(minB / Wf); ib1 = (int)math.ceil(maxB / Wf) - 1;
        }

        private static List<Field> BuildFields(LayerContext ctx, LayerRegion region, Footprints hard, float2 origin, float2 axis, float2 side,
            float Lf, float Wf, float fieldArea)
        {
            var fields = new List<Field>();
            Lattice(region, origin, axis, side, Lf, Wf, out int ia0, out int ia1, out int ib0, out int ib1);
            float minArea = math.max(500f, fieldArea * 0.12f);
            for (int ib = ib0; ib <= ib1; ib++)
                for (int ia = ia0; ia <= ia1; ia++)
                {
                    if (fields.Count >= MaxFields) return fields;
                    var cellRing = LayerRegion.Rect(origin + axis * ((ia + 0.5f) * Lf) + side * ((ib + 0.5f) * Wf), axis, Lf * 0.5f, Wf * 0.5f);
                    var work = new List<float2[]>();
                    foreach (var piece in Poly.Intersection(cellRing, region.Ring))
                        foreach (var r in Poly.CutHoles(piece)) work.Add(r);
                    // Subtract the yard, pond and paddocks (grown by the headland so tracks and fences fit).
                    for (int h = 0; h < hard.Rings.Count; h++)
                    {
                        var next = new List<float2[]>();
                        foreach (var w in work)
                        {
                            Poly.Bounds(w, out var mn, out var mx);
                            if (!hard.BoxOverlaps(mn - Headland, mx + Headland, h)) { next.Add(w); continue; }
                            foreach (var d in Poly.Difference(w, hard.Rings[h]))
                                foreach (var r in Poly.CutHoles(d)) next.Add(r);
                        }
                        work = next;
                    }
                    foreach (var w in work)
                    {
                        if (Poly.Area(w) < minArea) continue;
                        foreach (var inset in Poly.Offset(w, Headland))
                        {
                            if (inset == null || inset.Length < 3 || Poly.Area(inset) < minArea * 0.6f) continue;
                            var clean = Poly.Simplify(inset, 0.3f);
                            if (clean.Length < 3 || !Poly.IsSimple(clean)) clean = inset;
                            var fr = LayerRegion.From(clean);
                            if (fr == null) continue;
                            fields.Add(new Field { Ring = fr.Ring, Region = fr, A = ia, B = ib });
                        }
                    }
                    if ((fields.Count & 7) == 0) ctx.Check();
                }
            return fields;
        }

        private static void AssignKinds(LayerContext ctx, List<Field> fields, float2 axis, float2 side, string sub)
        {
            float arablePct = ctx.F("arablePct") / 100f;
            bool hay = ctx.B("hayMeadows");
            string rowAngle = ctx.S("rowAngle");
            string cropMode = ctx.S("cropProps");
            // Exact shares: fields ranked by a seeded hash, the first share arable, the rest grass; within
            // each group every third is a variant (fallow / hay) and small plots of hobby farms grow vegetables.
            var order = new List<int>();
            for (int i = 0; i < fields.Count; i++) order.Add(i);
            var keys = new uint[fields.Count];
            for (int i = 0; i < fields.Count; i++) keys[i] = Hashing.Mix(ctx.Seed, fields[i].A * 7919 + 13, fields[i].B * 104729 + 7);
            order.Sort((x, y) => keys[x] != keys[y] ? keys[x].CompareTo(keys[y]) : x.CompareTo(y));
            int arableCount = (int)math.round(arablePct * fields.Count);
            if (arablePct > 0f && arablePct < 1f && fields.Count >= 2) arableCount = math.clamp(arableCount, 1, fields.Count - 1);
            int ai = 0, gi = 0;
            for (int k = 0; k < order.Count; k++)
            {
                var f = fields[order[k]];
                if (k < arableCount)
                {
                    bool small = f.Region.Area < 4000f;
                    if ((cropMode == "rows" && small) || (sub == "hobby" && ai % 2 == 0)) f.Kind = FieldKind.Veg;
                    else if (ai % 7 == 6) f.Kind = FieldKind.Fallow;
                    else f.Kind = FieldKind.Grain;
                    ai++;
                }
                else
                {
                    f.Kind = hay && gi % 3 == 1 ? FieldKind.Hay : FieldKind.Pasture;
                    gi++;
                }
            }
            foreach (var f in fields)
                switch (rowAngle)
                {
                    case "along": f.RowDir = axis; break;
                    case "across": f.RowDir = side; break;
                    case "alternate": f.RowDir = ((f.A + f.B) & 1) == 0 ? axis : side; break;
                    default:
                        // on a slope the rows follow the contour (ploughing across the fall line holds the soil)
                        if (Contour(ctx, f.Region, out var cdir, out _)) { f.RowDir = cdir; break; }
                        f.Region.PrincipalAxes(out _, out var major, out _, out _);
                        f.RowDir = math.normalizesafe(major, axis);
                        break;
                }
        }

        /// <summary>Crop props along rows (every <paramref name="along"/> m); edges = only the outer two rows on each side.</summary>
        private static ObjectRole CropRole(string cat) =>
            cat == AssetCategories.FieldCrop || cat == AssetCategories.GardenCrop ? ObjectRole.Furniture : ObjectRole.Bush;

        private static int HayBales(LayerContext ctx, Occupancy occ, Field f, string cat, uint v)
        {
            var dir = f.RowDir;
            var perp = new float2(-dir.y, dir.x);
            var c = f.Region.Centroid;
            int n = 0;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var q in f.Ring) { float t = math.dot(q - c, perp); lo = math.min(lo, t); hi = math.max(hi, t); }
            for (float t = lo + 8f; t < hi - 4f && n < 80; t += 16f)
                foreach (var (a, b) in Line(f.Region, c + perp * t, dir))
                {
                    float len = math.distance(a, b);
                    var d = (b - a) / math.max(1e-3f, len);
                    for (float s = 6f + Hashing.Unit(Hashing.Mix(v, (int)t, 1)) * 8f; s < len - 4f && n < 80; s += 14f)
                        if (LeisureKit.Put(ctx, occ, a + d * s, math.atan2(d.x, d.y), ObjectRole.Furniture, AssetMix.One(""), cat, 1f, Hashing.Mix(v, (uint)n), 1f, 0.3f, 4))
                            n++;
                }
            return n;
        }

        private static void DressYard(LayerContext ctx, LayerRegion region, Occupancy occ, ReservedSpace res, OrientedRect yard, float frontSign,
            string hayCat, string[] treeIds, out int barns, out int silos)
        {
            barns = 0; silos = 0;
            var yardRing = yard.Ring;
            string ground = ctx.S("yardGround");
            string groundCat = ground == "paved" ? AssetCategories.SurfacePaving
                : LeisureKit.Category(ctx, "", ground == "dirt" ? AssetCategories.SurfaceDirt : AssetCategories.SurfaceNatural, AssetCategories.SurfaceNatural);
            // (P2-3) A dirt or gravel yard is the ground itself, worked to earth: painted (earth, and the dark forest floor for mud),
            // its mud, puddles and tyre tracks laid by the ground design. The beige sand surface read as a slab dropped on the lawn
            // (run show-10-builds-1030-231154-0c65). Only a paved yard keeps a surface.
            if (ground == "paved" && groundCat != null) ctx.Surface(yardRing, SurfaceRole.Paving, AssetMix.One(""), groundCat);
            else
            {
                float mud = ctx.F("yardMud") / 100f;
                TerrainPaint.Patch(ctx, yardRing, TerrainMaterials.Dirt, 1f, 3f);
                TerrainPaint.Patch(ctx, yardRing, TerrainMaterials.Lush, ground == "dirt" ? 0.55f + 0.4f * mud : 0.25f + 0.4f * mud, 3f);
            }
            var yardRegion = LayerRegion.From(yardRing);
            if (yardRegion == null) return;
            if (ctx.B("levelYard"))
                TerrainShaper.Shape(ctx, yardRegion, new TerrainSpec { Mode = TerrainMode.Flatten, MaxSlopePct = 15f, Falloff = 8f, Seed = ctx.Seed });
            var yo = new Occupancy(yardRegion, res, ctx.Site);
            foreach (var s in occ.Corridors.Segments) yo.TakeCorridor(s.A, s.B, s.HalfWidth, 100);
            float back = -frontSign;
            var face = yard.Side * frontSign;
            float yawFront = math.atan2(face.x, face.y), yawBack = math.atan2(-face.x, -face.y);

            // Silo battery at the +u end (claims its strip first so the barns stop short of it).
            int wantSilos = ctx.I("silos");
            // Vanilla has no silo prop: a battery of tall water tanks (feed bins) stands in, without a "missing" note.
            string siloCat = null; var siloAsset = AssetMix.One(ctx.S("siloAsset"));
            if (wantSilos > 0)
            {
                if (!string.IsNullOrEmpty(ctx.S("siloAsset")) || AssetAvailability.Has(AssetCategories.FarmSilo)) siloCat = AssetCategories.FarmSilo;
                else if (AssetAvailability.Has(AssetCategories.GardenShed)) { siloCat = AssetCategories.GardenShed; siloAsset = FarmArt.SiloStandIn; }
            }
            float siloR = AssetMix.FootprintRadius(ctx.Site, siloAsset, 2.6f);
            float siloStrip = 0f;
            if (siloCat != null)
            {
                float pitch = siloR * 2f + 1.2f;
                int perCol = math.max(1, (int)math.floor((yard.HalfWidth * 2f - 2f) / pitch));
                for (int k = 0; k < wantSilos; k++)
                {
                    int col = k / perCol, row = k % perCol;
                    float u = yard.HalfLength - siloR - 1.2f - col * pitch;
                    float vv = back * (yard.HalfWidth - siloR - 1f - row * pitch);
                    var p = yard.Local(u, vv);
                    if (LeisureKit.Put(ctx, yo, p, yawFront, ObjectRole.Furniture, siloAsset, siloCat, siloR, Hashing.Mix(ctx.Seed, 0x5110u + (uint)k), 0.3f, 0.2f))
                    {
                        silos++;
                        siloStrip = math.max(siloStrip, (col + 1) * pitch + 1.2f);
                    }
                }
            }

            // Barns along the back, facing the yard.
            int wantBarns = ctx.I("barns");
            string barnCat = wantBarns > 0 ? LeisureKit.Category(ctx, ctx.S("barnAsset"), AssetCategories.FarmBarn, AssetCategories.GardenShed, AssetCategories.Shelter) : null;
            var barnAsset = AssetMix.One(ctx.S("barnAsset"));
            float barnR = math.min(AssetMix.FootprintRadius(ctx.Site, barnAsset, 7f), math.max(3f, yard.HalfWidth - 1.5f));
            if (barnCat != null)
                for (int k = 0; k < wantBarns; k++)
                {
                    float u = -yard.HalfLength + barnR + 1f + k * (barnR * 2f + 3f);
                    if (u + barnR > yard.HalfLength - siloStrip - 0.5f) break;
                    float vv = back * math.max(0f, yard.HalfWidth - barnR - 1f);
                    var p = yard.Local(u, vv);
                    if (LeisureKit.Put(ctx, yo, p, yawFront, ObjectRole.Furniture, barnAsset, barnCat, barnR * 0.9f, Hashing.Mix(ctx.Seed, 0xBA27u + (uint)k), 0.2f, 0.3f))
                        barns++;
                }

            Homestead(ctx, region, yo, occ, yard, frontSign, out int vehicles);
            ctx.Plan.Facts["farmVehicles"] = vehicles;

            // Machinery parked nose-in along the front.
            if (ctx.B("machinery"))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.FarmMachinery);
                int want = ctx.S("subtype") == "arable" ? 4 : 2;
                if (cat != null)
                    for (int k = 0, n = 0; k < 8 && n < want; k++)
                    {
                        var p = yard.Local(-yard.HalfLength + 4f + k * 6.5f, frontSign * (yard.HalfWidth - 3.2f));
                        uint mv = Hashing.Mix(ctx.Seed, 0x7EC0u + (uint)k);
                        // Tractor trailers with their loads, never the 16 m irrigation rig (that one stands in the fields).
                        var machine = AssetMix.Pick(FarmArt.Trailers, mv);
                        if (LeisureKit.Put(ctx, yo, p, yawBack, ObjectRole.Furniture, machine, cat, 2.5f, mv, 0.3f, 0.3f))
                            n++;
                    }
            }

            // Hay stack: a 2 × 3 block of bales in the open middle-front of the yard.
            if (ctx.B("hayStacks") && hayCat != null)
            {
                var hc = yard.Local(yard.HalfLength * 0.25f, frontSign * yard.HalfWidth * 0.15f);
                for (int k = 0; k < 6; k++)
                {
                    var p = hc + yard.Axis * ((k % 3 - 1) * 2.3f) + yard.Side * ((k / 3) * 2.3f);
                    LeisureKit.Put(ctx, yo, p, yard.Yaw, ObjectRole.Furniture, AssetMix.One(""), hayCat, 1f, Hashing.Mix(ctx.Seed, 0x4A70u + (uint)k), 0.3f, 0.1f);
                }
                HeapMark(ctx, hc, "bales", 0);
            }

            // Wind pump in a front corner, troughs between the barns and the yard.
            if (ctx.B("windpump"))
            {
                string cat = LeisureKit.Category(ctx, "", AssetCategories.FarmWindpump);
                if (cat != null)
                    LeisureKit.Put(ctx, yo, yard.Local(yard.HalfLength - 2.5f, frontSign * (yard.HalfWidth - 2.5f)), 0f, ObjectRole.Furniture, AssetMix.One(""), cat, 1.5f,
                        Hashing.Mix(ctx.Seed, 0x3D11u), 0.3f, 0.3f);
            }
            if (AssetAvailability.Has(AssetCategories.FarmTrough))
                for (int k = 0; k < 2; k++)
                    LeisureKit.Put(ctx, yo, yard.Local(-yard.HalfLength * 0.4f + k * 4f, 0f), yard.Yaw, ObjectRole.Furniture, AssetMix.One(""), AssetCategories.FarmTrough,
                        1.2f, Hashing.Mix(ctx.Seed, 0x7206u + (uint)k), 0.3f, 0.2f);

            // Two old trees flanking the yard's front, outside it.
            for (int k = -1; k <= 1; k += 2)
            {
                var p = yard.Local(k * (yard.HalfLength + 3f), frontSign * (yard.HalfWidth - 2f));
                uint v = Hashing.Mix(ctx.Seed, 0x7E3Eu + (uint)(k + 2));
                if (region.Contains(p)) LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.5f, v, 1f, 0.4f, 3, 3);
            }
            // Everything placed in the yard becomes taken in the main occupancy too.
            foreach (var d in yo.Discs.Items) if (d.Kind != Occupancy.KindReserved) occ.Take(d.P, d.R, d.Kind);
        }

        private static int DressPaddock(LayerContext ctx, Occupancy occ, OrientedRect r, float2 origin, string livestock, int index, AssetRef fence,
            int dirtLayer, string hayCat)
        {
            uint v = Hashing.Mix(ctx.Seed, 0xADD0u + (uint)index);
            var ring = r.Ring;
            // Gate on the side facing the yard.
            int gateEdge = 0; float best = float.MaxValue;
            for (int e = 0; e < 4; e++)
            {
                var mid = (ring[e] + ring[(e + 1) % 4]) * 0.5f;
                float d = math.distance(mid, origin);
                if (d < best) { best = d; gateEdge = e; }
            }
            float2 gateMid = (ring[gateEdge] + ring[(gateEdge + 1) % 4]) * 0.5f;
            for (int e = 0; e < 4; e++)
            {
                var a = ring[e]; var b = ring[(e + 1) % 4];
                if (e != gateEdge) { ctx.StraightNet(a, b, NetRole.Fence, fence); continue; }
                var d = math.normalizesafe(b - a);
                var m = (a + b) * 0.5f;
                if (math.distance(a, m) > 3f) ctx.StraightNet(a, m - d * 2f, NetRole.Fence, fence);
                if (math.distance(m, b) > 3f) ctx.StraightNet(m + d * 2f, b, NetRole.Fence, fence);
            }
            var inward = math.normalizesafe(r.Center - gateMid);
            GateMark(ctx, gateMid, "paddock", 100 + index);
            HeapMark(ctx, gateMid + inward * 3f, "trough", 100 + index);
            var along = new float2(-inward.y, inward.x);
            // Worn ground at the gate, trough beside it, hay feeder in the middle, shelter in the far corner.
            if (dirtLayer > 0) TerrainPaint.Patch(ctx, LeisureKit.Blob(gateMid + inward * 4f, 5f, v, 12, 0.25f), dirtLayer, 0.6f, 2f);
            if (AssetAvailability.Has(AssetCategories.FarmTrough))
                LeisureKit.Put(ctx, occ, gateMid + inward * 3f + along * 4f, math.atan2(along.x, along.y), ObjectRole.Furniture, AssetMix.One(""), AssetCategories.FarmTrough,
                    1.2f, Hashing.Mix(v, 1u), 0.5f, 0.2f);
            if (hayCat != null)
                LeisureKit.Put(ctx, occ, r.Center, r.Yaw, ObjectRole.Furniture, AssetMix.One(""), hayCat, 1.2f, Hashing.Mix(v, 2u), 0.5f, 0.2f);
            if (ctx.B("shelters"))
            {
                // A field shelter (installed animal shelter, else the wooden campground shelter): never a gazebo.
                var (shelter, cat) = AnimalPark.AnimalParkArt.Prop("", AssetCategories.AnimalParkShelter, AnimalPark.AnimalParkArt.ShelterStandIn("paddocks"), AssetCategories.Shelter);
                var far = r.Center + inward * (math.min(r.HalfLength, r.HalfWidth) - 4f) + along * (math.max(r.HalfLength, r.HalfWidth) * 0.5f);
                LeisureKit.Put(ctx, occ, far, math.atan2(-inward.x, -inward.y), ObjectRole.Furniture, shelter, cat, 3f, Hashing.Mix(v, 3u), 0.5f, 0.3f);
            }
            int spawners = 0;
            if (ctx.B("animals"))
            {
                int n = math.clamp((int)(r.HalfLength * r.HalfWidth * 4f / 1500f), 1, 3);
                for (int k = 0; k < n; k++)
                {
                    var p = r.Local((k - (n - 1) * 0.5f) * r.HalfLength * 0.6f, r.HalfWidth * 0.25f);
                    if (!occ.Free(p, 1f, 1f, 0.2f)) continue;
                    ctx.Object(p, 0f, ObjectRole.AnimalSpawner, Spawner(livestock, index + k), Hashing.Mix(v, 10u + (uint)k));
                    spawners++;
                }
            }
            return spawners;
        }

        private static void DressPen(LayerContext ctx, Occupancy occ, float2 c, float r, float2 origin, AssetRef fence, int dirtLayer)
        {
            var toward = math.normalizesafe(origin - c, new float2(0, 1));
            foreach (var run in Fences.Ring(c, r, 14, toward, 3f)) ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            if (dirtLayer > 0) TerrainPaint.Patch(ctx, LayerRegion.Circle(c, r - 0.5f, 16), dirtLayer, 0.9f, 1.5f);
            occ.Take(c, r, 0);
        }

        private static void PondPlanting(LayerContext ctx, Occupancy occ, float2 c, float r, string[] treeIds, string[] bushIds)
        {
            float bank = r * 1.2f + 1.6f;
            int n = math.max(6, (int)(2f * math.PI * bank / 4f));
            for (int i = 0; i < n; i++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x9E3Du + (uint)i);
                if (Hashing.Unit(v) < 0.3f) continue;   // open bank for access
                float a = i * 2f * math.PI / n;
                var p = c + new float2(math.cos(a), math.sin(a)) * (bank + Hashing.Unit(Hashing.Mix(v, 1u)) * 1.5f);
                LeisureKit.Put(ctx, occ, p, a, ObjectRole.Bush, AssetMix.Pick(bushIds, v), null, 1f, v, 1f, 0.2f, 4);
            }
            for (int k = 0; k < 2; k++)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x3170u + (uint)k);
                float a = Hashing.Unit(v) * 6.283f;
                var p = c + new float2(math.cos(a), math.sin(a)) * (bank + 3.5f);
                LeisureKit.Put(ctx, occ, p, a, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.5f, v, 1f, 0.4f, 3, 3);
            }
        }

        /// <summary>
        /// A hedgerow as the game's own hedge net (one continuous hedge per straight run), not 2.6 m hedge pieces that
        /// the declutter pass thinned into a dotted line (rf-farms). Runs break at gates, gaps and corners.
        /// </summary>
        private sealed class HedgeLine
        {
            private readonly LayerContext _ctx;
            private readonly Occupancy _occ;
            private bool _open;
            private float2 _start, _last, _dir;
            public float Length;
            public static readonly AssetRef Net = AnimalPark.AnimalParkArt.Boundary("", true);

            public HedgeLine(LayerContext ctx, Occupancy occ) { _ctx = ctx; _occ = occ; }

            public void Add(float2 p, float2 tangent, bool ok)
            {
                if (!ok) { Flush(); return; }
                if (_open && math.dot(math.normalizesafe(tangent), _dir) < 0.985f) Flush();
                if (!_open) { _start = p; _dir = math.normalizesafe(tangent); _open = true; }
                _last = p;
            }

            public void Flush()
            {
                if (!_open) return;
                _open = false;
                float len = math.distance(_start, _last);
                if (len < 4f) return;
                // Half a sample step past each end closes the gap to the next run, never past the outline.
                var region = _ctx.LayerRegion;
                var a = region.ContainsDisc(_start - _dir * 1.2f, 1f) ? _start - _dir * 1.2f : _start;
                var b = region.ContainsDisc(_last + _dir * 1.2f, 1f) ? _last + _dir * 1.2f : _last;
                _ctx.StraightNet(a, b, NetRole.Fence, Net);
                _occ.TakeCorridor(a, b, 0.8f, 5);
                Length += math.distance(a, b);
            }
        }

        private static bool HedgeTree(LayerContext ctx, Occupancy occ, float2 p, string[] treeIds, uint v) =>
            ctx.LayerRegion.Contains(p) && LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.4f, v, 0.5f, 0.3f, 3, 2);

        private static int HedgeItem(LayerContext ctx, Occupancy occ, float2 p, float2 tangent, string cat, string[] bushIds, string[] treeIds,
            bool tree, uint v)
        {
            if (tree && LeisureKit.Put(ctx, occ, p, Hashing.Unit(v) * 6.28f, ObjectRole.Tree, AssetMix.Pick(treeIds, v), null, 2.4f, v, 1f, 0.3f, 3, 2))
                return 2;
            return LeisureKit.Put(ctx, occ, p, math.atan2(tangent.x, tangent.y), ObjectRole.Bush, AssetMix.Pick(bushIds, v),
                cat == AssetCategories.Bush ? null : cat, 1f, v, 1f, 0.1f, 4) ? 1 : 0;
        }

        private static int EdgeHedge(LayerContext ctx, Occupancy occ, Footprints hard, List<Gate> gates, string cat, string[] bushIds, string[] treeIds,
            float treeEvery, int budget, ref float length)
        {
            var region = ctx.LayerRegion;
            int n = 0;
            float nextTree = treeEvery * 0.5f;
            var line = bushIds.Length == 0 ? new HedgeLine(ctx, occ) : null;
            for (float s = 1.3f; s < region.Perimeter && n < budget; s += 2.6f)
            {
                var p = region.PointAt(s, out var tangent, out var inward, out _);
                p += inward * 1.6f;
                bool nearGate = false;
                foreach (var g in gates) if (math.distancesq(g.Position, p) < 49f) { nearGate = true; break; }
                bool ok = !nearGate && !hard.Hits(p, 0.5f);
                uint v = Hashing.Mix(ctx.Seed, 0xED6Eu + (uint)n);
                if (line != null)
                {
                    line.Add(p, tangent, ok);
                    if (ok && s >= nextTree && HedgeTree(ctx, occ, p + inward * 2.8f, treeIds, v)) nextTree = s + treeEvery;
                    if (ok) n++;
                    continue;
                }
                if (!ok) continue;
                bool tree = s >= nextTree;
                int r = HedgeItem(ctx, occ, p, tangent, cat, bushIds, treeIds, tree, v);
                if (r == 2) nextTree = s + treeEvery;
                if (r > 0) { n++; length += 2.6f; }
            }
            if (line != null) { line.Flush(); length += line.Length; }
            return n;
        }

        private static int FieldHedges(LayerContext ctx, LayerRegion region, Occupancy occ, Footprints hard, float2 origin, float2 axis, float2 side,
            float Lf, float Wf, HashSet<int> trackA, HashSet<int> trackB, string cat, string[] bushIds, string[] treeIds, float treeEvery, int budget,
            ref float length)
        {
            if (budget <= 0) return 0;
            Lattice(region, origin, axis, side, Lf, Wf, out int ia0, out int ia1, out int ib0, out int ib1);
            int n = 0; float walked = 0f;
            void Walk(float2 through, float2 dir, float pitch, float offset)
            {
                var nrm = new float2(-dir.y, dir.x);
                foreach (var (a, b) in Line(region, through + nrm * offset, dir))
                {
                    float len = math.distance(a, b);
                    var d = (b - a) / math.max(1e-3f, len);
                    float nextTree = treeEvery * 0.5f;
                    var hedge = bushIds.Length == 0 ? new HedgeLine(ctx, occ) : null;
                    for (float s = 1.3f; s < len && n < budget; s += 2.6f)
                    {
                        var p = a + d * s;
                        // Field gateway: a gap in the middle of every field side.
                        float k = math.dot(p - origin, dir) / pitch;
                        float frac = k - math.floor(k);
                        bool ok = math.abs(frac - 0.5f) * pitch >= 3.5f && region.BoundaryDistance(p, 4f) >= 3.5f && !hard.Hits(p, 1f);
                        uint v = Hashing.Mix(ctx.Seed, 0xF1EDu + (uint)n);
                        if (hedge != null)
                        {
                            // Never across a track or a field gate: the hedge stops 1.5 m short of any path.
                            ok = ok && !occ.Corridors.Overlaps(p, 1.5f);
                            hedge.Add(p, d, ok);
                            if (ok && s >= nextTree && HedgeTree(ctx, occ, p + nrm * 2.8f, treeIds, v)) nextTree = s + treeEvery;
                            if (ok) n++;
                            continue;
                        }
                        if (!ok) continue;
                        bool tree = s >= nextTree;
                        int r = HedgeItem(ctx, occ, p, d, cat, bushIds, treeIds, tree, v);
                        if (r == 2) nextTree = s + treeEvery;
                        if (r > 0) { n++; walked += 2.6f; }
                    }
                    if (hedge != null) { hedge.Flush(); walked += hedge.Length; }
                }
            }
            // Lines carrying a track are already a boundary (the track itself): no hedge there.
            for (int ib = ib0 + 1; ib <= ib1 && n < budget; ib++)
                if (!trackB.Contains(ib)) Walk(origin + side * (ib * Wf), axis, Lf, 0f);
            for (int ia = ia0 + 1; ia <= ia1 && n < budget; ia++)
                if (!trackA.Contains(ia)) Walk(origin + axis * (ia * Lf), side, Wf, 0f);
            length += walked;
            return n;
        }
    }
}

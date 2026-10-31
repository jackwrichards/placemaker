// Landfill: waste sites as a player would screenshot them —
//   active     a great terraformed mound (benched sides, a flat top) with the working face cut into
//              the flank that faces the gate: raw garbage, compactors, soil cover piles, litter
//              fences round the face, a haul road that swings round the toe and climbs onto the face,
//              gas wells on the capped part, a lined leachate lagoon at the low toe, gatehouse and
//              weighbridge;
//   capped     the same mound finished: a smooth grassy dome with gas wells in a grid, scrub on the
//              cap, trees only round the toe (roots must not pierce the cap), and a footpath that
//              spirals to a summit viewpoint with benches;
//   compost    a levelled dirt pad with long windrows in parallel, a turner at the row ends, the
//              green-waste reception heap by the gate and finished compost heaps;
//   recycling  a concrete pad with a drive-through loop, a public drop-off row of skips along it,
//              material bays of bales/scrap/glass round the fence, a baler and sorting shed;
//   flytip     an unfenced scrubby lot with worn tracks, garbage dumped in clusters where a van can
//              stop, a burnt-out car, puddles and rubble humps.
// Every mound/pad is terraformed with the shared yard sculptor, blended back to the original
// ground at the outline.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Landfill
{
    [AreaGenerator(311, Group = "industrial")]
    public sealed class LandfillLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public const string LayerId = "landfill";
        public static readonly string[] Styles = { "active", "capped", "compost", "recycling", "flytip" };
        public const string MoundTooSmallKey = "Placemaker.Landfill.Issue.MoundTooSmall";

        protected override string Area => "Landfill";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Garbage.svg";
        public override float MinArea => 1500f;
        protected override int WantedEntrances(LayerRegion region) => 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(SkipRowAddOn.LayerId), Feature(JunkPilesAddOn.LayerId, ("mix", "garbage")),
            Feature("decals", ("theme", "dirt"), ("density", 1.0), ("clustering", 70.0)),
            Feature("picnic", ("groups", 2.0)), YardKit.Labelled(Feature("bigtrees", ("density", 1.0)), "Placemaker.Landfill.Feature.bigtrees"), Feature("restrooms", ("count", 1.0), ("byArea", false)),
            StreetShopsFeature(Area),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", "active", ControlTier.Essential, "site", Styles).Tip();
            c.Slider("moundHeight", 12, 0, 30, 0.5, "m", ControlTier.Essential, "site").Tip();
            c.Slider("moundSize", 70, 30, 95, 5, "%", ControlTier.Essential, "site").Tip();
            c.Slider("fill", 70, 20, 100, 5, "%", ControlTier.Essential, "site").Tip();

            c.Toggle("workingFace", true, ControlTier.More, "operations").Tip();
            c.Slider("machines", 3, 0, 10, 1, "count", ControlTier.More, "operations");
            c.Slider("coverPiles", 4, 0, 16, 1, "count", ControlTier.More, "operations").Tip();
            c.Toggle("litterFences", true, ControlTier.More, "operations").Tip();
            c.Slider("windrows", 8, 2, 24, 1, "count", ControlTier.More, "operations").When("style=compost").Tip();
            c.Slider("skips", 8, 0, 24, 1, "count", ControlTier.More, "operations").Tip();

            c.Slider("gasVents", 6, 0, 20, 1, "perHa", ControlTier.More, "cap").Tip();
            c.Toggle("summitPath", false, ControlTier.More, "cap").Tip();
            c.Slider("meadow", 40, 0, 200, 10, "perHa", ControlTier.More, "cap").Tip();
            c.Toggle("toeTrees", true, ControlTier.More, "cap").Tip();
            c.Toggle("leachatePond", true, ControlTier.More, "cap").Tip();

            c.Choice("fence", "fence", ControlTier.More, "edge", "none", "fence", "broken").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "edge").When("fence=fence|broken");
            c.Toggle("screenTrees", true, ControlTier.More, "edge").Tip();
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "edge");
            c.Slider("weeds", 25, 0, 150, 5, "perHa", ControlTier.More, "edge");
            c.Toggle("office", true, ControlTier.More, "edge");
            c.Toggle("weighbridge", true, ControlTier.More, "edge");
            c.Toggle("floodlights", true, ControlTier.More, "edge");

            c.Toggle("paint", true, ControlTier.More, "ground").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "ground").When("paint=true").Tip();
            c.Slider("grime", 1.5, 0, 6, 0.25, "per100m2", ControlTier.More, "ground").Tip();

            c.AssetMulti("garbageAssets", AssetCategories.LandfillGarbage, ControlTier.More, "assets");
            c.AssetMulti("pileAssets", AssetCategories.LandfillPile, ControlTier.More, "assets");
            c.AssetMulti("machineAssets", AssetCategories.ScrapMachine, ControlTier.More, "assets");
            c.AssetMulti("skipAssets", AssetCategories.LandfillSkip, ControlTier.More, "assets");
            c.Asset("pathAsset", AssetCategories.Pathway, ControlTier.More, "assets").When("summitPath=true");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets").When("summitPath=true");
            EntranceMarkers(c);
            DefineStreetShopControls(c);

            c.RandomRange("moundHeight", 4, 18);
            c.RandomRange("fill", 50, 90);
            c.Fixed("paintLayer");
        }

        protected override void DefinePresets()
        {
            Preset("active", ("style", "active"), ("moundHeight", 14.0), ("moundSize", 75.0), ("workingFace", true), ("litterFences", true),
                ("machines", 3.0), ("coverPiles", 5.0), ("gasVents", 4.0), ("summitPath", false), ("leachatePond", true));
            Preset("capped", ("style", "capped"), ("moundHeight", 16.0), ("moundSize", 80.0), ("workingFace", false), ("litterFences", false),
                ("machines", 0.0), ("coverPiles", 0.0), ("gasVents", 8.0), ("summitPath", true), ("meadow", 60.0), ("fence", "none"),
                ("office", false), ("weighbridge", false), ("floodlights", false), ("grime", 0.25), ("skips", 0.0));
            Preset("compost", ("style", "compost"), ("moundHeight", 0.0), ("windrows", 10.0), ("machines", 2.0), ("coverPiles", 0.0),
                ("gasVents", 0.0), ("litterFences", false), ("skips", 0.0), ("grime", 1.5));
            Preset("recycling", ("style", "recycling"), ("moundHeight", 0.0), ("skips", 12.0), ("machines", 2.0), ("litterFences", false),
                ("gasVents", 0.0), ("leachatePond", false), ("coverPiles", 0.0), ("grime", 1.0));
            Preset("flytip", ("style", "flytip"), ("moundHeight", 0.0), ("fence", "broken"), ("office", false), ("weighbridge", false),
                ("floodlights", false), ("screenTrees", false), ("weeds", 90.0), ("fill", 50.0), ("machines", 0.0), ("grime", 2.5),
                ("gasVents", 0.0), ("leachatePond", false), ("skips", 0.0), ("coverPiles", 0.0), ("litterFences", false));
        }

        // ───────────── planning ─────────────

        private sealed class Site
        {
            public LayerContext Ctx;
            public LayerRegion Region;
            public Occupancy Occ, ReservedOnly;
            public List<Gate> Gates;
            public float2 Centre, Axis, Side, Start;
            public float Major, Minor, Fill, EdgeInset = 1.5f;
            public bool HasMound;
            /// <summary>Older, already-capped phases beside the main mound (big or long sites).</summary>
            public readonly List<Mound> Phases = new List<Mound>();
            public IEnumerable<Mound> Hills() { if (HasMound) yield return Mound; foreach (var p in Phases) yield return p; }
            public bool OnHill(float2 p, float fraction) { foreach (var h in Hills()) if (h.Covers(p, fraction)) return true; return false; }
            public Mound Mound;
            public readonly List<Mound> Shapes = new List<Mound>();
            public readonly List<(float2 p, float r)> Dirty = new List<(float2, float)>();
            public readonly List<float2> Wet = new List<float2>();
            public bool Level;
            public int Garbage, Vents, Machines, Skips, Windrows, Bales, Piles;
            public uint Salt(uint k) => Hashing.Mix(Ctx.Seed, k);
        }

        /// <summary>Type-specific curated defaults (over GritAssets' shared sets); null = shared only.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtObjects = null, ArtSurfaces = null;

        /// <summary>Haul roads: compacted grey gravel (vanilla ore ground as the fallback), not mud.</summary>
        private static AssetRef HaulGround => new AssetRef("Terrain Surfaces Pack Rock gaga TTP - Smaller Pebbles grey M Surface", "Ore Surface 01");
        /// <summary>Tipped waste: the game's own landfill ground.</summary>
        private static AssetRef WasteGround => new AssetRef("Landfill Surface 01", "Placemaker Overlay (Landfill Surface 01)", "Oil Surface 01");
        /// <summary>Daily cover on the tip: brown soil (P2-4: the vanilla fallback was the oil field's pale sand, a bleached disc from 45°).</summary>
        private static AssetRef CoverGround => new AssetRef("Terrain Surfaces Pack Ground gaga TTP - Soil M Surface", "EAI - Surface Pack Ground Mud Surface", "Forestry Surface 01");
        /// <summary>Compost windrows: dark ploughed soil.</summary>
        private static AssetRef CompostGround => new AssetRef("Farming Surfaces & Decals Ground Haristess - 1-2 - MudPlowed1 Surface", "Farming Surfaces & Decals Ground Haristess - 1-1 - MudSoil1 Brown Surface", "Forestry Surface 01");
        private static readonly GritAssets.Set CompostPiles = new GritAssets.Set(false, "CoalTruck01_OrePile01", "Prop_CoalTruck01_CoalPile01_Partial2|CoalTruck01_CoalPile01", "CoalTruck01_OrePile01", "Prop_CoalTruck01_OrePile01_Full|CoalTruck01_OrePile01");

        // Art pass: whatever the player left automatic gets the curated gritty defaults (GritAssets).
        protected override void PlanCore(LayerContext ctx) { PlanArt(ctx); GritAssets.Apply(ctx.Plan, ctx.Seed, ArtObjects, ArtSurfaces); }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var s = new Site
            {
                Ctx = ctx, Region = region,
                Occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site),
                ReservedOnly = new Occupancy(region, ctx.Input.Reserved, ctx.Site),
                Gates = Gates(ctx), Fill = math.saturate(ctx.F("fill") / 100f),
            };
            YardKit.GridFrame(region, out s.Centre, out s.Axis, out s.Side, out s.Major, out s.Minor);
            string style = ctx.S("style");

            YardKit.Fence(ctx, s.Gates, ctx.S("fence"), AssetMix.One(ctx.S("fenceAsset")), s.Salt(0xFE7Cu));
            // Room for the screening belt now; the trees go in after the site so nothing has to dodge them.
            bool screen = ctx.B("screenTrees") && style != "capped";
            if (screen) s.EdgeInset = 6.2f;
            s.Start = region.Centroid;
            if (s.Gates.Count > 0)
            {
                var g = s.Gates[0];
                s.Start = YardKit.Inside(region, g.Position + region.InwardNormal(g.Segment) * 14f, region.Centroid, 2f);
                if (style != "flytip" && style != "capped")
                    YardKit.GateCompound(ctx, s.Occ, g, 8f, ctx.B("office"), ctx.B("weighbridge"), ctx.B("floodlights"), null, s.Salt(0x6A7Eu), out _);
            }
            ctx.Check();

            switch (style)
            {
                case "capped": Mound(s, false); Capped(s); break;
                case "compost": Compost(s); break;
                case "recycling": Recycling(s); break;
                case "flytip": FlyTip(s); break;
                default: Mound(s, ctx.B("workingFace")); Active(s); break;
            }
            ctx.Check();

            if (screen) YardKit.Screen(ctx, s.Occ, s.Gates, 2.6f, 8f, ctx.L("treeSpecies"), s.Salt(0x5C2Eu));
            if (ctx.B("leachatePond") && (style == "active" || style == "capped" || style == "compost")) Lagoon(s);
            if (style == "active" || style == "capped") ToeTrees(s);
            YardKit.Weeds(ctx, s.Occ, ctx.F("weeds"), 5f + s.EdgeInset, s.Salt(0x3EEDu));
            Grime(s, ctx.F("grime"));
            if (ctx.B("paint")) Paint(s);
            if (ctx.B("paint"))
            {
                // The marks of work (YardWear); water seeps out of the toe of a live tip
                var brief = Yards.YardWear.Of(ctx, Id, style, style == "recycling" ? "concrete" : "dirt", s.Gates);
                brief.Markings = style == "recycling";
                if (s.HasMound && style == "active")
                    for (int i = 0; i < 5; i++)
                        brief.Seeps.Add(s.Mound.At(Dir(i * 1.256f + Hashing.Unit(s.Salt(0x5EE9u + (uint)i)) * 0.8f), 1.03f));
                Yards.YardWear.Lay(ctx, brief);
            }
            ctx.Check();

            var terrain = Shape(s);
            EmitGreenRegions(ctx, s.Occ, 150f);
            YardKit.Settle(ctx, terrain);

            CommonStats(ctx);
            ctx.Stat("Placemaker.Stat.GarbageItems", s.Garbage, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Vents", s.Vents, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Machines", s.Machines, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Skips", s.Skips, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Windrows", s.Windrows, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Bales", s.Bales, Keys.UnitCount);
            if (terrain.Cells > 0) ctx.Stat("Placemaker.Stat.TerrainChange", System.Math.Round(math.max(terrain.MaxOffset, -terrain.MinOffset), 1), Keys.UnitM);
        }

        // ───────────── the mound ─────────────

        /// <summary>Elliptical mound along the long axis, shrunk until it sits inside with a toe band to spare.</summary>
        private static void Mound(Site s, bool face)
        {
            var ctx = s.Ctx; var region = s.Region;
            float height = ctx.F("moundHeight");
            var c = region.DeepestPoint(out float ins, (q, cap) => s.Occ.Clearance(q, cap), 16);
            float toe = s.EdgeInset + 8f;
            float ratio = math.clamp(s.Major / math.max(1f, s.Minor), 1f, 2.2f);
            float R = (ins - toe) * math.saturate(ctx.F("moundSize") / 100f + 0.15f);
            var m = new Mound { Center = c, Axis = s.Axis, Ratio = ratio, Flat = 0.4f };
            for (int tries = 0; tries < 12 && R >= 10f; tries++)
            {
                m.Radius = R;
                bool fits = true;
                for (int i = 0; i < 24 && fits; i++)
                {
                    float a = i * math.PI * 2f / 24f;
                    var p = m.At(new float2(math.cos(a), math.sin(a)), 1f);
                    if (!region.ContainsDisc(p, toe * 0.6f)) fits = false;
                }
                if (fits) break;
                if (m.Ratio > 1.05f) m.Ratio = math.max(1f, m.Ratio - 0.25f); else R *= 0.88f;
            }
            if (R < 10f) { ctx.Issue(IssueSeverity.Info, MoundTooSmallKey); return; }
            m.Radius = R;
            // Landfill sides: about 1 in 3, so the height is capped by the footprint.
            m.Height = math.min(height, R * (1f - m.Flat) * 0.33f);
            if (face)
            {
                // The working face is cut into the flank facing the way in, so the haul road is short.
                var toGate = math.normalizesafe(s.Start - c, s.Side);
                float rot = 0.5f;
                m.CutDir = new float2(toGate.x * math.cos(rot) - toGate.y * math.sin(rot), toGate.x * math.sin(rot) + toGate.y * math.cos(rot));
                m.CutHalfAngle = 0.75f;
                m.CutDepth = 0.5f;
                m.Bench = 5f;
            }
            s.Mound = m;
            s.HasMound = m.Height > 0.5f || face;
            if (m.Height > 0.5f) s.Shapes.Add(m);
            ctx.Plan.Facts["moundRadius"] = R;
            ctx.Plan.Facts["moundHeight"] = m.Height;
        }

        private static float2 Dir(float a) => new float2(math.cos(a), math.sin(a));

        /// <summary>Points of a fan sector of the mound (angles a0±half, radii f0..f1 of the footprint).</summary>
        private static float2[] Fan(Mound m, float a0, float half, float f0, float f1, int steps = 8)
        {
            var pts = new List<float2>();
            for (int i = 0; i <= steps; i++) pts.Add(m.At(Dir(a0 - half + 2f * half * i / steps), f1));
            for (int i = steps; i >= 0; i--) pts.Add(m.At(Dir(a0 - half + 2f * half * i / steps), f0));
            return Poly.EnsureCcw(pts.ToArray());
        }

        private static void Active(Site s)
        {
            var ctx = s.Ctx;
            if (!s.HasMound) { Compost(s); return; }
            var m = s.Mound;
            bool face = ctx.B("workingFace") && m.CutHalfAngle > 0f;
            float a0 = math.atan2(m.CutDir.y, m.CutDir.x);
            float half = m.CutHalfAngle * 0.85f;
            var haulCat = YardKit.CatQuiet(AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural;

            // Haul road: gate → round the toe → up into the face.
            var faceTop = m.At(Dir(a0), 0.45f);
            var faceToe = m.At(Dir(a0), 1.08f);
            var startOnToe = m.At(math.normalizesafe(s.Start - m.Center), 1.08f);
            var line = new List<float2> { s.Gates.Count > 0 ? s.Gates[0].Position + s.Region.InwardNormal(s.Gates[0].Segment) * 0.3f : s.Start };
            foreach (var p in YardKit.ArcAround(startOnToe, faceToe, m.Center, math.distance(startOnToe, m.Center))) line.Add(YardKit.Inside(s.Region, p, m.Center, 4f));
            if (face) line.Add(faceTop);
            YardKit.Haul(ctx, s.Occ, line, 8f, SurfaceRole.Ground, HaulGround, haulCat);
            foreach (var p in line) s.Wet.Add(p);

            // Daily-cover soil on the top and down the working side; the far flanks are older cells, capped and grassed (P2-4: soil
            // over the whole tip read as one pale disc). A ragged edge, as the cover is pushed out load by load.
            {
                var ring = new float2[48];
                uint ws = s.Salt(0xC0AEu);
                for (int i = 0; i < ring.Length; i++)
                {
                    float ang = i * math.PI * 2f / ring.Length;
                    float off = face ? math.abs(AngleTo(m.At(Dir(ang), 1f), m.Center, a0)) : math.PI;
                    float work = 1f - math.smoothstep(half + 0.7f, half + 1.3f, off);
                    float r = math.lerp(0.58f, 0.97f, work) * (1f + 0.07f * (ValueNoise.Sample(new float2(ang * 2.2f, 0.5f), ws) - 0.5f) * 2f);
                    ring[i] = m.At(Dir(ang), math.min(0.98f, r));
                }
                YardKit.Patch(ctx, Poly.EnsureCcw(ring), SurfaceRole.Ground, CoverGround, haulCat);
            }
            if (face)
            {
                // Machines on the face.
                int machines = ctx.I("machines");
                string mc = machines > 0 ? YardKit.Cat(ctx, ctx.L("machineAssets"), AssetCategories.ScrapMachine) : null;
                for (int k = 0; k < machines * 6 && s.Machines < machines && mc != null; k++)
                {
                    uint v = s.Salt(0x3AC0u + (uint)k);
                    var p = m.At(Dir(a0 + (Hashing.Unit(v) - 0.5f) * 2f * half * 0.8f), 0.4f + 0.5f * Hashing.Unit(Hashing.Mix(v, 1u)));
                    if (LeisureKit.Put(ctx, s.Occ, p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(ctx.L("machineAssets"), v), mc, 2.6f, v, 1f, 0.3f))
                        s.Machines++;
                }
                // Raw garbage on the face, compactors working it.
                var fan = Fan(m, a0, half, 0.3f, 0.95f);
                YardKit.Patch(ctx, fan, SurfaceRole.Ground, WasteGround, haulCat);
                // The focal point: a refuse truck tipping at the top of the face (when the playset has one).
                if (ctx.L("machineAssets") == null || ctx.L("machineAssets").Length == 0)
                {
                    var tip = m.At(Dir(a0), 0.5f);
                    LeisureKit.Put(ctx, s.Occ, tip, LeisureKit.Yaw(tip, m.Center), ObjectRole.Furniture,
                        new AssetRef("Prop_EU_GarbageTruck01", "Prop_NA_GarbageTruck01", "Prop_CoalTruck01"), AssetCategories.ScrapMachine, 3f, s.Salt(0x7177u), 0.5f, 0.3f);
                }
                var fanRegion = LayerRegion.From(fan);
                var ids = ctx.L("garbageAssets");
                string cat = YardKit.Cat(ctx, ids, AssetCategories.LandfillGarbage, AssetCategories.ScrapHeap);
                if (fanRegion != null && cat != null)
                {
                    int wanted = (int)(fanRegion.Area / 11f * s.Fill);
                    int i = 0;
                    foreach (var p in Scatter.Candidates(fanRegion, 2.2f, s.Salt(0x6A2Bu), wanted * 4 + 40, ctx.Cancel))
                    {
                        if (s.Garbage >= wanted) break;
                        uint v = s.Salt(0x6A2Cu + (uint)i++);
                        var o = Put(s, p, Hashing.Unit(v) * 6.283f, AssetMix.Pick(ids, v), cat, 1.0f, v, 0.05f);
                        if (o != null) { o.Scale = 0.8f + 0.6f * Hashing.Unit(Hashing.Mix(v, 1u)); s.Garbage++; }
                    }
                    s.Dirty.Add((m.At(Dir(a0), 0.65f), math.sqrt(fanRegion.Area / math.PI)));
                }
                // Daily cover: soil piles just beyond the face edges.
                int piles = ctx.I("coverPiles");
                string pc = piles > 0 ? YardKit.Cat(ctx, ctx.L("pileAssets"), AssetCategories.LandfillPile, AssetCategories.Rock) : null;
                for (int k = 0; k < piles * 4 && s.Piles < piles && pc != null; k++)
                {
                    uint v = s.Salt(0xC0E7u + (uint)k);
                    float side = (k & 1) == 0 ? 1f : -1f;
                    var p = m.At(Dir(a0 + side * (half + 0.15f + 0.25f * Hashing.Unit(v))), 0.45f + 0.4f * Hashing.Unit(Hashing.Mix(v, 1u)));
                    int got = YardKit.Heap(ctx, s.Occ, p, 3f, 6, ctx.L("pileAssets"), pc, 1.6f, v);
                    if (got > 0) { s.Piles++; s.Dirty.Add((p, 3.5f)); }
                }
                // Litter fences: portable screens round the downwind half of the face.
                if (ctx.B("litterFences"))
                {
                    var fence = AssetMix.One(ctx.S("fenceAsset"));
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        float from = a0 + sgn * half * 0.35f, to = a0 + sgn * (half + 0.25f);
                        for (int k = 0; k < 3; k++)
                        {
                            var p0 = m.At(Dir(math.lerp(from, to, k / 3f)), 1.0f);
                            var p1 = m.At(Dir(math.lerp(from, to, (k + 1) / 3f)), 1.0f);
                            var mid = math.lerp(p0, p1, 0.5f);
                            if (!s.Region.ContainsDisc(p0, 1f) || !s.Region.ContainsDisc(p1, 1f) || s.Region.CrossesBoundary(p0, p1, false)) continue;
                            if (s.Occ.Corridors.Overlaps(mid, 0.5f) || s.Occ.Corridors.Overlaps(p0, 0.5f) || s.Occ.Corridors.Overlaps(p1, 0.5f)) continue;
                            ctx.StraightNet(p0, p1, NetRole.Fence, fence);
                            s.Occ.TakeCorridor(p0, p1, 0.4f, 5);
                        }
                    }
                }
            }
            Phases(s);
            // Cap scrub on the finished part.
            GasVents(s, ctx.F("gasVents"));
            Meadow(s, ctx.F("meadow") * 0.5f, face ? a0 : float.NaN, half);
        }

        private static void Capped(Site s)
        {
            var ctx = s.Ctx;
            if (!s.HasMound) { Meadow(s, s.Ctx.F("meadow"), float.NaN, 0f); return; }
            var m = s.Mound;
            m.Flat = 0.25f;   // a rounder dome than an active site's working platform
            s.Shapes[s.Shapes.Count - 1] = m;
            s.Mound = m;
            Phases(s);
            if (ctx.B("summitPath")) SummitPath(s);
            GasVents(s, ctx.F("gasVents"));
            Meadow(s, ctx.F("meadow"), float.NaN, 0f);
        }

        /// <summary>A footpath that spirals from the gate up to a summit viewpoint with benches.</summary>
        private static void SummitPath(Site s)
        {
            var ctx = s.Ctx; var m = s.Mound;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            var g = s.Gates.Count > 0 ? s.Gates[0].Position : s.Region.ClosestOnBoundary(m.Center, out _, out _);
            float a = math.atan2(g.y - m.Center.y, g.x - m.Center.x);
            var toe = YardKit.Inside(s.Region, m.At(Dir(a), 1.02f), m.Center, 2f);
            var prev = g + math.normalizesafe(m.Center - g) * 0.3f;
            if (s.Region.Contains(prev) && !s.Region.CrossesBoundary(prev, toe, true)) LeisureKit.Path(ctx, s.Occ, prev, toe, NetRole.Pathway, asset);
            prev = toe;
            // 1.4 turns from the toe to the summit ring: a gentle climb, like the paths on Mount Trashmore.
            const int steps = 28;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float ang = a + t * math.PI * 2f * 1.4f;
                var p = m.At(Dir(ang), math.lerp(1.02f, 0.12f, t));
                if (!s.Region.ContainsDisc(p, 1.5f)) continue;
                LeisureKit.Path(ctx, s.Occ, prev, p, NetRole.Pathway, asset);
                prev = p;
            }
            // Summit: benches looking out, a viewpoint if one is installed.
            var bench = AssetMix.One(ctx.S("benchAsset"));
            for (int k = 0; k < 4; k++)
            {
                var dir = Dir(a + k * math.PI * 0.5f + 0.4f);
                var p = m.Center + dir * 5f;
                LeisureKit.Put(ctx, s.Occ, p, LeisureKit.Yaw(m.Center, p), ObjectRole.Bench, bench, null, 1.3f, s.Salt(0x5E0u + (uint)k), 1f);
            }
            string look = YardKit.CatQuiet(AssetCategories.Lookout);
            if (look != null) LeisureKit.Put(ctx, s.Occ, m.Center + Dir(a + math.PI) * 2.2f, 0f, ObjectRole.Furniture, AssetMix.One(""), look, 1.5f, s.Salt(0x5E9u), 1f);
        }

        /// <summary>
        /// Older phases: where the site has room beside the main mound (long or L-shaped sites), lower
        /// capped mounds fill it — landfills grow cell by cell, and the finished cells stay as hills.
        /// </summary>
        private static void Phases(Site s)
        {
            if (!s.HasMound || s.Mound.Height < 1f) return;
            var region = s.Region;
            float toe = s.EdgeInset + 8f;
            for (int k = 0; k < 3; k++)
            {
                var hills = new List<Mound>(s.Hills());
                var c = region.DeepestPoint(out float ins, (q, cap) =>
                {
                    float cl = s.Occ.Clearance(q, cap);
                    foreach (var h in hills) cl = math.min(cl, h.Distance(q) - h.Radius * 1.1f);
                    return cl;
                }, 16);
                float R = (ins - toe * 0.6f) * 0.9f;
                if (R < 16f) break;
                var m = new Mound { Center = c, Radius = R, Flat = 0.35f, Ratio = 1f };
                m.Height = math.min(s.Mound.Height * (0.75f - 0.15f * k), R * (1f - m.Flat) * 0.33f);
                if (m.Height < 1.5f) break;
                s.Phases.Add(m);
                s.Shapes.Add(m);
            }
            s.Ctx.Plan.Facts["phases"] = s.Phases.Count;
        }

        private static float AngleTo(float2 p, float2 centre, float a0)
        {
            float a = math.atan2(p.y - centre.y, p.x - centre.x) - a0;
            while (a > math.PI) a -= 2f * math.PI;
            while (a < -math.PI) a += 2f * math.PI;
            return a;
        }

        /// <summary>Low scrub on the caps (no trees: their roots would breach the cap).</summary>
        private static void Meadow(Site s, float perHa, float faceAngle, float half)
        {
            if (perHa <= 0f || !AssetAvailability.Has(AssetCategories.Bush)) return;
            var main = s.Mound; bool any = s.HasMound || s.Phases.Count > 0;
            int wanted = (int)(s.Region.Area / 10000f * perHa);
            System.Func<float2, bool> reject = p =>
            {
                if (!any) return false;
                if (!s.OnHill(p, 0.95f)) return true;
                return !float.IsNaN(faceAngle) && main.Covers(p, 1f) && math.abs(AngleTo(p, main.Center, faceAngle)) < half + 0.3f;
            };
            LeisureKit.Plant(s.Ctx, s.Occ, s.Region, wanted, ObjectRole.Bush, null, 1.0f, 14f, 0.25f, 0xCA9u, reject);
        }

        /// <summary>Gas wells in a grid on every capped part (never on the open face).</summary>
        private static void GasVents(Site s, float perHa)
        {
            if (perHa <= 0f || (!s.HasMound && s.Phases.Count == 0)) return;
            var ctx = s.Ctx;
            string cat = YardKit.Cat(ctx, null, AssetCategories.LandfillVent);
            if (cat == null) return;
            float spacing = math.sqrt(10000f / perHa);
            int i = 0;
            foreach (var m in s.Hills())
            {
                float a0 = math.atan2(m.CutDir.y, m.CutDir.x);
                float reach = m.Radius * math.max(1f, m.Ratio);
                for (float u = -reach; u <= reach; u += spacing)
                    for (float v = -reach; v <= reach; v += spacing)
                    {
                        var p = m.Center + s.Axis * u + s.Side * v;
                        if (!m.Covers(p, 0.88f)) continue;
                        if (m.CutHalfAngle > 0f && math.abs(AngleTo(p, m.Center, a0)) < m.CutHalfAngle + 0.1f && !m.Covers(p, 0.25f)) continue;
                        uint h = s.Salt(0x6A5u + (uint)i++);
                        // Wells follow a grid, with the small offsets of a real drilling plan.
                        var q = p + new float2(Hashing.Unit(h) - 0.5f, Hashing.Unit(Hashing.Mix(h, 1u)) - 0.5f) * 2f;
                        if (LeisureKit.Put(ctx, s.Occ, q, Hashing.Unit(Hashing.Mix(h, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), cat, 0.6f, h, 1f, 0.2f))
                            s.Vents++;
                    }
            }
        }

        /// <summary>A loose ring of trees round the foot of every mound.</summary>
        private static void ToeTrees(Site s)
        {
            if (!s.Ctx.B("toeTrees") || !AssetAvailability.Has(AssetCategories.Tree)) return;
            var ids = s.Ctx.L("treeSpecies");
            int i = 0;
            foreach (var m in s.Hills())
            {
                float circ = 2f * math.PI * m.Radius * (1f + math.max(1f, m.Ratio)) * 0.5f;
                int n = (int)(circ / 11f);
                for (int k = 0; k < n; k++, i++)
                {
                    uint v = s.Salt(0x70E0u + (uint)i);
                    float a = k * 2f * math.PI / n + (Hashing.Unit(v) - 0.5f) * 0.1f;
                    var p = m.At(Dir(a), 1.1f + 0.08f * Hashing.Unit(Hashing.Mix(v, 1u)));
                    if (s.OnHill(p, 1.02f)) continue;
                    LeisureKit.Put(s.Ctx, s.Occ, p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ids, v), null, 2.4f, v, 2f, 0.4f, 3, 1 + (int)(v % 3u));
                }
            }
        }

        /// <summary>Lined leachate/run-off lagoon at the lowest free spot of the toe band, fenced.</summary>
        private static void Lagoon(Site s)
        {
            var ctx = s.Ctx;
            string cat = YardKit.CatQuiet(AssetCategories.SurfaceWater, AssetCategories.SurfaceNatural);
            if (cat == null) return;
            float best = float.MaxValue; float2 spot = default; float2 tangent = s.Axis; bool found = false;
            int i = 0;
            foreach (var p in Scatter.Candidates(s.Region, 6f, s.Salt(0x1A6u), 300, ctx.Cancel))
            {
                if (s.OnHill(p, 1.12f)) continue;
                if (!s.Region.ContainsDisc(p, 11f + s.EdgeInset * 0.3f)) continue;
                if (s.Occ.Clearance(p, 12f) < 10.5f) continue;
                float h = ctx.H(p) + Hashing.Unit(s.Salt(0x1A7u + (uint)i++)) * 0.01f;
                if (h < best) { best = h; spot = p; found = true; }
            }
            if (!found) return;
            if (s.HasMound) { var d = math.normalizesafe(spot - s.Mound.Center); tangent = new float2(-d.y, d.x); }
            var rect = LayerRegion.Rect(spot, tangent, 9f, 5f);
            if (!ctx.Surface(rect, SurfaceRole.Water, AssetMix.One(""), cat)) return;
            var fence = AssetMix.One(ctx.S("fenceAsset"));
            var outer = LayerRegion.Rect(spot, tangent, 10.2f, 6.2f);
            for (int k = 0; k < 4; k++)
            {
                var a = outer[k]; var b = outer[(k + 1) % 4];
                if (k == 0) { ctx.StraightNet(a, math.lerp(a, b, 0.4f), NetRole.Fence, fence); ctx.StraightNet(math.lerp(a, b, 0.6f), b, NetRole.Fence, fence); }
                else ctx.StraightNet(a, b, NetRole.Fence, fence);
            }
            s.Occ.TakeCorridor(spot - tangent * 9f, spot + tangent * 9f, 6.4f, 5);
            s.Shapes.Add(new Mound { Center = spot, Radius = 7f, Height = -1.5f, Flat = 0.5f });
            s.Wet.Add(spot);
            ctx.Plan.Facts["lagoon"] = 1;
        }

        // ───────────── other subtypes ─────────────

        private static void Compost(Site s)
        {
            var ctx = s.Ctx; var region = s.Region;
            s.Level = true;
            string dirt = YardKit.CatQuiet(AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural;
            ctx.Surface(region.Ring, SurfaceRole.Ground, AssetMix.One(""), dirt);
            var ids = ctx.L("pileAssets");
            string pile = YardKit.Cat(ctx, ids, AssetCategories.LandfillPile, AssetCategories.Rock);

            // Reception: the green-waste heap and a shredder by the gate.
            if (pile != null)
            {
                var c = s.Start;
                if (s.Gates.Count > 0)
                {
                    var g = s.Gates[0];
                    var inward = region.InwardNormal(g.Segment);
                    c = YardKit.Inside(region, g.Position + inward * 16f + new float2(inward.y, -inward.x) * 12f, s.Start, 7f);
                }
                int got = YardKit.Heap(ctx, s.Occ, c, 6f, 22, ids, pile, 1.8f, s.Salt(0x6E1u));
                if (got > 0) { s.Piles++; s.Dirty.Add((c, 6f)); }
                // Brush and cuttings waiting for the shredder.
                if (AssetAvailability.Has(AssetCategories.Bush))
                    for (int k = 0; k < 6; k++)
                    {
                        uint v = s.Salt(0x6E2u + (uint)k);
                        var p = c + Dir(Hashing.Unit(v) * 6.283f) * (7.5f + 2f * Hashing.Unit(Hashing.Mix(v, 1u)));
                        LeisureKit.Put(ctx, s.Occ, p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Bush, AssetMix.One(""), null, 1f, v, 0.6f, 0f);
                    }
            }
            // A gravel lane from the gate straight through the pad: the turner and the trucks use it,
            // the windrows break for it (art pass — the rows used to run solid across the way in).
            if (s.Gates.Count > 0)
            {
                var g = s.Gates[0];
                var inward = region.InwardNormal(g.Segment);
                var a = g.Position + inward * 0.3f;
                var b = YardKit.Inside(region, a + inward * math.max(s.Major, s.Minor), region.Centroid, s.EdgeInset + 4f);
                if (!region.CrossesBoundary(a, b, true)) YardKit.Haul(ctx, s.Occ, new[] { a, b }, 6f, SurfaceRole.Ground, HaulGround, dirt);
            }
            // Windrows: long parallel rows along the main axis.
            int want = ctx.I("windrows");
            const float rowWidth = 4f, aisle = 6.5f;
            float pitch = rowWidth + aisle;
            var rows = YardKit.Lines(region, s.Centre, s.Axis, pitch, pitch * 0.5f, s.EdgeInset + 8f);
            rows.Sort((a, b) => math.abs(a.k).CompareTo(math.abs(b.k)));
            var turners = new List<float2>();
            foreach (var (a, b, _) in rows)
            {
                if (s.Windrows >= want) break;
                if (pile == null) break;
                if (math.distance(a, b) < 14f) continue;
                YardKit.Strip(ctx, a, b, rowWidth * 0.5f, SurfaceRole.Decorative, CompostGround, dirt);
                // Loads 5.8 m long every 4.4 m: one continuous windrow, half the old prop count.
                int n = WindrowRow(s, a, b, ids, pile, s.Salt(0x31Du + (uint)s.Windrows));
                if (n == 0) continue;
                s.Windrows++;
                s.Dirty.Add((math.lerp(a, b, 0.5f), 4f));
                turners.Add(b + math.normalizesafe(b - a) * 4f);
            }
            int machines = ctx.I("machines");
            string mc = machines > 0 ? YardKit.Cat(ctx, ctx.L("machineAssets"), AssetCategories.ScrapMachine) : null;
            for (int k = 0; k < turners.Count && s.Machines < machines && mc != null; k += math.max(1, turners.Count / math.max(1, machines)))
                if (LeisureKit.Put(ctx, s.Occ, turners[k], LeisureKit.Yaw(turners[k], s.Centre), ObjectRole.Furniture, AssetMix.Pick(ctx.L("machineAssets"), (uint)k), mc, 2.6f, s.Salt(0x7E2u + (uint)k), 1f, 0.3f))
                    s.Machines++;
            // Finished compost: three big heaps where the trucks load.
            if (pile != null)
                for (int k = 0; k < 3; k++)
                    if (Spots.Find(s.Occ, 5f, Spots.Preference.NearBoundary, s.Salt(0xF1u + (uint)k), out var spot, null, 0f, s.EdgeInset + 1f, 900))
                    {
                        int got = YardKit.Heap(ctx, s.Occ, spot, 5f, 16, ids, pile, 1.8f, s.Salt(0xF2u + (uint)k));
                        if (got > 0) { s.Piles++; s.Dirty.Add((spot, 5f)); s.Shapes.Add(new Mound { Center = spot, Radius = 6f, Height = 1.5f, Flat = 0.3f }); }
                    }
        }

        private static int WindrowRow(Site s, float2 a, float2 b, string[] ids, string pile, uint salt)
        {
            if (ids != null && ids.Length > 0) return YardKit.Row(s.Ctx, s.Occ, a, b, 2.6f, ids, pile, 1.5f, salt, 0.6f);
            int n = 0, i = 0;
            float yaw = LeisureKit.Yaw(a, b);
            foreach (var p in LeisureKit.Along(a, b, 4.4f))
            {
                uint v = Hashing.Mix(salt, (uint)i++);
                if (!s.Occ.Free(p, 1.5f, 0.4f, -0.2f) || s.Occ.Corridors.Overlaps(p, 2.8f)) continue;
                s.Ctx.Object(p, yaw + (Hashing.Unit(v) - 0.5f) * 0.12f, ObjectRole.Furniture, GritAssets.Ref(CompostPiles, v), v, -1, pile);
                s.Occ.Take(p, 1.5f, YardKit.KindStuff);
                n++;
            }
            return n;
        }

        /// <summary>Bays only along the back half of the outline (the gate side stays open for trucks).</summary>
        private static System.Func<float2, bool> BackHalf(Site s)
        {
            if (s.Gates.Count == 0) return null;
            var g = s.Gates[0].Position;
            float far = 0f;
            foreach (var v in s.Region.Ring) far = math.max(far, math.distance(v, g));
            return c => math.distance(c, g) > far * 0.55f;
        }

        private static void Recycling(Site s)
        {
            var ctx = s.Ctx; var region = s.Region;
            s.Level = true;
            ctx.Surface(region.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
            const float road = 7f;
            // Drive-through loop for the public, entered from the gate.
            float loopAt = s.EdgeInset + 12f + road * 0.5f;
            var loop = region.Area > 4000f ? region.InsetVertices(loopAt, 2.5f) : null;
            bool ok = loop != null && Poly.SignedArea(loop) > 500f && Poly.IsSimple(loop);
            if (ok) foreach (var v in loop) if (!region.Contains(v) || region.BoundaryDistance(v, loopAt + 1f) < loopAt * 0.8f) { ok = false; break; }
            var asphalt = YardKit.CatQuiet(AssetCategories.SurfaceAsphalt, AssetCategories.SurfacePaving) ?? AssetCategories.SurfacePaving;
            var lines = new List<(float2 a, float2 b)>();
            if (ok)
            {
                var closed = new List<float2>(loop) { loop[0] };
                YardKit.Haul(ctx, s.Occ, closed, road, SurfaceRole.Asphalt, AssetMix.One(""), asphalt);
                for (int i = 0; i < loop.Length; i++) lines.Add((loop[i], loop[(i + 1) % loop.Length]));
                foreach (var v in loop) s.Wet.Add(v);
            }
            if (s.Gates.Count > 0)
            {
                var g = s.Gates[0];
                var a = g.Position + region.InwardNormal(g.Segment) * 0.3f;
                var b = ok ? LayerRegion.From(loop).ClosestOnBoundary(a, out _, out _) : s.Start;
                if (!region.CrossesBoundary(a, b, true)) YardKit.Haul(ctx, s.Occ, new[] { a, b }, road, SurfaceRole.Asphalt, AssetMix.One(""), asphalt);
            }
            // Public drop-off: skips along the inside of the loop, open side to the road.
            int skips = ctx.I("skips");
            var skipIds = ctx.L("skipAssets");
            string sc = skips > 0 ? YardKit.Cat(ctx, skipIds, AssetCategories.LandfillSkip, AssetCategories.ScrapContainer) : null;
            if (sc != null && ok)
                foreach (var (a, b) in lines)
                {
                    var dir = math.normalizesafe(b - a);
                    var inward = new float2(-dir.y, dir.x);
                    foreach (var p in LeisureKit.Along(a, b, 4f))
                    {
                        if (s.Skips >= skips) break;
                        var q = p + inward * (road * 0.5f + 2f);
                        uint v = s.Salt(0x5C1Bu + (uint)s.Skips * 17u + (uint)(p.x * 3f));
                        if (LeisureKit.Put(ctx, s.Occ, q, LeisureKit.Yaw(q, p), ObjectRole.Furniture, AssetMix.Pick(skipIds, v), sc, 1.6f, v, 0.5f, 0.2f)) s.Skips++;
                    }
                }
            // Material bays round the fence: bales, scrap, glass, garbage, pallets.
            string[] cycle = { AssetCategories.LandfillBale, AssetCategories.ScrapHeap, AssetCategories.LandfillSkip, AssetCategories.LandfillGarbage, AssetCategories.ScrapPallet, AssetCategories.ScrapTire };
            YardKit.Bays(ctx, s.Occ, s.EdgeInset, 9f, 9f, s.Gates, 14f, BackHalf(s), (i, c, inward, hl, hw) =>
            {
                string cat = YardKit.CatQuiet(cycle[i % cycle.Length], AssetCategories.LandfillBale, AssetCategories.ScrapHeap);
                if (cat == null) return;
                int got = YardKit.Heap(ctx, s.Occ, c - inward * hw * 0.15f, math.min(hl, hw) * 0.75f, 7, null, cat, 1.6f, s.Salt(0xBA0u + (uint)i));
                if (cat == AssetCategories.LandfillBale) s.Bales += got;
                if (cat == AssetCategories.LandfillGarbage) s.Garbage += got;
                s.Dirty.Add((c, hw));
            }, s.Salt(0xBA75u));
            // Core: bale stacks in a grid, a baler and the sorting shed.
            string bale = YardKit.CatQuiet(AssetCategories.LandfillBale, AssetCategories.ScrapPallet);
            if (bale != null)
            {
                var core = ok ? LayerRegion.From(LayerRegion.From(loop).InsetVertices(road * 0.5f + 5f, 2.5f)) : region;
                if (core != null && Poly.IsSimple(core.Ring))
                {
                    var stacks = LeisureKit.Pack(core, s.Occ, s.Centre, s.Axis, 6f, 3f, 2.5f, 4f, (int)(core.Area / 60f * s.Fill), 0.5f);
                    int k = 0;
                    foreach (var r in stacks)
                        foreach (var p in new[] { r.Local(-1.6f, 0f), r.Local(0f, 0f), r.Local(1.6f, 0f) })
                        {
                            uint v = s.Salt(0xBA1u + (uint)k++);
                            if (Hashing.Unit(v) > 0.5f + 0.5f * s.Fill) continue;
                            ctx.Object(p, r.Yaw + (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.1f, ObjectRole.Furniture, AssetMix.One(""), v, -1, bale);
                            s.Bales++;
                        }
                }
            }
            int machines = ctx.I("machines");
            string mc = machines > 0 ? YardKit.Cat(ctx, ctx.L("machineAssets"), AssetCategories.ScrapMachine) : null;
            for (int k = 0; k < machines && mc != null; k++)
                if (Spots.Find(s.Occ, 3f, Spots.Preference.Open, s.Salt(0x3A0u + (uint)k), out var spot, null, 0f, 2f, 600)
                    && LeisureKit.Put(ctx, s.Occ, spot, LeisureKit.Yaw(spot, s.Centre), ObjectRole.Furniture, AssetMix.Pick(ctx.L("machineAssets"), (uint)k), mc, 2.6f, s.Salt(0x3A1u + (uint)k), 1f, 0.3f))
                    { s.Machines++; s.Dirty.Add((spot, 3f)); }
            string shed = YardKit.CatQuiet(AssetCategories.ScrapCabin, AssetCategories.GardenShed);
            if (shed != null && Spots.Find(s.Occ, 4.5f, Spots.Preference.Open, s.Salt(0x5ED0u), out var shedAt, null, 0f, 2f, 600))
                LeisureKit.Put(ctx, s.Occ, shedAt, LeisureKit.Yaw(shedAt, s.Start), ObjectRole.Furniture, AssetMix.One(""), shed, 4f, s.Salt(0x5ED1u), 1f, 0.3f);
        }

        private static void FlyTip(Site s)
        {
            var ctx = s.Ctx; var region = s.Region;
            var dirt = YardKit.CatQuiet(AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural) ?? AssetCategories.SurfaceNatural;
            // Worn tracks: from the way in across to the far side, with a kink where people cut the corner.
            var from = s.Gates.Count > 0 ? s.Gates[0].Position + region.InwardNormal(s.Gates[0].Segment) * 0.3f : region.Ring[0];
            float bestD = -1f; float2 far = region.Centroid;
            foreach (var v in region.Ring) { float d = math.distance(v, from); if (d > bestD) { bestD = d; far = v; } }
            far = YardKit.Inside(region, far, region.Centroid, 6f);
            var bend = YardKit.Inside(region, math.lerp(from, far, 0.5f) + s.Side * (s.Minor * 0.12f), region.Centroid, 4f);
            var track = new List<float2> { from, bend, far };
            YardKit.Haul(ctx, s.Occ, track, 3.5f, SurfaceRole.Ground, AssetMix.One(""), dirt);
            foreach (var p in track) s.Wet.Add(p);
            s.Wet.Add(math.lerp(from, bend, 0.5f)); s.Wet.Add(math.lerp(bend, far, 0.5f));

            // Dumps: where a van can stop — near the outline and the track.
            var ids = ctx.L("garbageAssets");
            string cat = YardKit.Cat(ctx, ids, AssetCategories.LandfillGarbage, AssetCategories.ScrapHeap);
            int wanted = math.max(2, (int)(region.Area / 2500f * s.Fill));
            var centres = new List<float2>();
            string[] extras = { AssetCategories.ScrapTire, AssetCategories.ScrapBarrel, AssetCategories.ScrapPallet, AssetCategories.ScrapHeap };
            int idx = 0;
            foreach (var p in Scatter.Candidates(region, 5f, s.Salt(0xF17u), 600, ctx.Cancel))
            {
                if (cat == null || centres.Count >= wanted) break;
                float edge = region.BoundaryDistance(p, 30f);
                float toTrack = s.Occ.Corridors.Clearance(p, 30f);
                if (math.min(edge, toTrack) > 14f) continue;
                bool close = false;
                foreach (var q in centres) if (math.distance(p, q) < 14f) { close = true; break; }
                if (close) continue;
                uint v = s.Salt(0xF18u + (uint)idx++);
                float r = 2f + 2.5f * Hashing.Unit(v);
                int got = YardKit.Heap(ctx, s.Occ, p, r, (int)(r * r * 0.9f) + 3, ids, cat, 1.1f, v);
                if (got == 0) continue;
                s.Garbage += got;
                centres.Add(p);
                s.Dirty.Add((p, r));
                for (int k = 0; k < 2; k++)
                {
                    string ec = YardKit.CatQuiet(extras[(int)((v >> (k * 3)) % (uint)extras.Length)]);
                    float a = Hashing.Unit(Hashing.Mix(v, 40u + (uint)k)) * 6.283f;
                    if (ec != null) LeisureKit.Put(ctx, s.Occ, p + Dir(a) * (r + 1.5f), a, ObjectRole.Furniture, AssetMix.One(""), ec, 0.8f, Hashing.Mix(v, 50u + (uint)k), 0.5f, 0.2f);
                }
                if (Hashing.Unit(Hashing.Mix(v, 60u)) < 0.4f) s.Shapes.Add(new Mound { Center = p, Radius = r + 3f, Height = 0.8f, Flat = 0.3f });
            }
            // A burnt-out car off the track.
            string wreck = YardKit.CatQuiet(AssetCategories.ScrapWreck);
            if (wreck != null && Spots.Find(s.Occ, 3f, Spots.Preference.Open, s.Salt(0xB0Fu), out var at, centres, 10f, 2f, 600))
                if (LeisureKit.Put(ctx, s.Occ, at, Hashing.Unit(s.Salt(0xB10u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), wreck, 2.4f, s.Salt(0xB11u), 1f, 0.3f))
                    s.Dirty.Add((at, 3f));
            // Scrub and self-seeded trees.
            if (AssetAvailability.Has(AssetCategories.Tree))
                LeisureKit.Plant(ctx, s.Occ, region, (int)(region.Area / 10000f * 25f), ObjectRole.Tree, ctx.L("treeSpecies"), 2.4f, 18f, 0.2f, 0xF1Eu, null, 0, 2);
        }

        // ───────────── shared details ─────────────

        private static PlannedObject Put(Site s, float2 p, float yaw, AssetRef asset, string cat, float r, uint v, float gap)
        {
            if (!s.Occ.Free(p, r, 0.6f, gap)) return null;
            var o = s.Ctx.Object(p, yaw, ObjectRole.Furniture, asset, v, -1, cat);
            s.Occ.Take(p, r, YardKit.KindStuff);
            return o;
        }

        private static void Grime(Site s, float per100)
        {
            if (per100 <= 0f) return;
            var ctx = s.Ctx;
            // Art pass: capped at ~110 decals per hectare (flytip used to stamp 280).
            int total = (int)math.min(s.Region.Area / 100f * per100, s.Region.Area / 10000f * 110f);
            string dirt = YardKit.CatQuiet(AssetCategories.DecalLitter, AssetCategories.DecalDirt, AssetCategories.Decal);
            string puddle = YardKit.CatQuiet(AssetCategories.DecalPuddles, AssetCategories.DecalDirt);
            if (dirt == null && puddle == null) { AssetAvailability.Require(ctx.Plan, AssetCategories.DecalDirt, null, Keys.Category(AssetCategories.DecalDirt)); return; }
            int nearStuff = (int)(total * 0.65f), wet = (int)(total * 0.25f), loose = total - nearStuff - wet;
            int n = 0;
            if (s.Dirty.Count > 0)
            {
                int each = math.max(1, nearStuff / s.Dirty.Count);
                for (int i = 0; i < s.Dirty.Count && i < 200; i++)
                    n += YardKit.Grime(ctx, s.ReservedOnly, s.Dirty[i].p, s.Dirty[i].r + 3f, each, dirt, s.Salt(0x9E1u + (uint)i));
            }
            if (s.Wet.Count > 0)
            {
                int each = math.max(1, wet / s.Wet.Count);
                for (int i = 0; i < s.Wet.Count && i < 200; i++)
                    n += YardKit.Grime(ctx, s.ReservedOnly, s.Wet[i], 5f, each, puddle, s.Salt(0x9E2u + (uint)i));
            }
            n += YardKit.Grime(ctx, s.ReservedOnly, s.Region.Centroid, math.sqrt(s.Region.Area), loose, dirt, s.Salt(0x9E3u));
            ctx.Plan.Facts["decals"] = n;
        }

        private static void Paint(Site s)
        {
            int layer = s.Ctx.I("paintLayer");
            int n = 0;
            foreach (var (p, r) in s.Dirty)
            {
                if (n++ >= 24) break;
                TerrainPaint.Patch(s.Ctx, LeisureKit.Blob(p, r + 2.5f, s.Salt(0xFA1u + (uint)n), 12, 0.25f), layer, 0.9f, 2f);
            }
        }

        private static TerrainResult Shape(Site s)
        {
            var ctx = s.Ctx;
            if (!s.Level && s.Shapes.Count == 0) return new TerrainResult { HeightAfter = ctx.H };
            float mean = s.Level ? TerrainShaper.MeanHeight(ctx, s.Region) : 0f;
            var list = s.Shapes;
            bool level = s.Level;
            // A big mound needs bigger cells (brush dabs); pads and humps keep 8 m.
            float cell = s.HasMound ? 10f : 8f;
            return YardKit.Sculpt(ctx, s.Region, (p, h0) =>
            {
                float up = 0f, down = 0f;
                for (int i = 0; i < list.Count; i++)
                {
                    float o = list[i].Offset(p);
                    if (o > up) up = o; else if (o < down) down = o;
                }
                return (level ? mean - h0 : 0f) + up + down;
            }, 34f, 14f, cell);
        }
    }
}

// Construction site (industrial): a fenced site entered through a gate, a haul road from the gate to
// the works, a site compound (cabins, toilets, containers, generator) beside the road, material
// storage (heaps, stacks) around the works, machinery, safety lines (barriers, cones, signs),
// floodlights, and mud everywhere (terrain paint + dirt/puddle decals). The works zone gives each
// subtype its own spatial structure:
//   foundation  a rectangular dig with a ramp inside one long side, barriers on the rim, a crane
//   roadworks   a road strip under construction in sections (milled, trench, base, dirt), coned lanes
//   compound    a big compound and a storage yard of bays in rows with aisles
//   demolition  a rubble field (raised mounds) over cracked foundation slabs, skips, a wrecking crane
//   earthworks  cut & fill: a gentle excavation on one side, spoil mounds on the other, a haul loop
// Terrain changes are always blended back to the original ground at the outline (SiteKit.Sculpt).

using Placemaker.Core.Layers.Types.Scrapyard;
using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.ConstructionSite
{
    [AreaGenerator(72, Group = "industrial")]
    public sealed class ConstructionSiteLayer : LeisureLayer
    {
        /// <summary>Vehicle / non-park site: plain entrances, no pedestrian/bike/main kinds, never blocks Build.</summary>
        protected override bool TypedEntrances => false;

        public const string LayerId = "constructionSite";
        public const string WorksPlace = "place:works";
        public static readonly string[] Subtypes = { "foundation", "roadworks", "compound", "demolition", "earthworks" };
        private const float MaxSlopePct = 80f;

        protected override string Area => "ConstructionSite";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Build.svg";
        public override float MinArea => 600f;
        protected override bool HostsStreetShops => false;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core };
        protected override int WantedEntrances(LayerRegion region) => region.Area > 30000f ? 2 : 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature(SpoilHeapsAddOn.LayerId), Feature(WorkLightsAddOn.LayerId), Feature("decals", ("theme", "dirt"), ("density", 3.0)),
            Named(Feature("kiosk"), "Placemaker.AddOn.Kiosk.Name"), Feature("restrooms"),
        };

        /// <summary>A feature labelled with another key (e.g. the add-on's own name).</summary>
        public static FeatureInfo Named(FeatureInfo f, string labelKey) { f.LabelKey = labelKey; return f; }

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("subtype", "foundation", ControlTier.Essential, "site", Subtypes).Tip();
            c.Slider("workSize", 55, 20, 85, 5, "%", ControlTier.Essential, "site").Tip();
            c.Slider("depth", 6, 1, 15, 0.5, "m", ControlTier.Essential, "site").Tip();
            c.Toggle("fence", true, ControlTier.Essential, "site").Tip();

            c.Choice("compound", "small", ControlTier.More, "compound", "none", "small", "large").Tip();
            c.Slider("cabins", 4, 0, 16, 1, "count", ControlTier.More, "compound");
            c.Slider("toilets", 2, 0, 8, 1, "count", ControlTier.More, "compound");
            c.Slider("containers", 3, 0, 16, 1, "count", ControlTier.More, "compound");

            c.Slider("piles", 12, 0, 40, 1, "perHa", ControlTier.More, "materials").Tip();
            c.Slider("stacks", 10, 0, 40, 1, "perHa", ControlTier.More, "materials").Tip();
            c.Choice("pileSize", "medium", ControlTier.More, "materials", "small", "medium", "large");

            c.Slider("machines", 4, 0, 16, 1, "count", ControlTier.More, "machinery");
            c.Toggle("crane", true, ControlTier.More, "machinery").Tip();
            c.Toggle("lights", true, ControlTier.More, "machinery");

            c.Toggle("barriers", true, ControlTier.More, "safety").Tip();
            c.Slider("coneSpacing", 6, 2, 20, 1, "m", ControlTier.More, "safety").Layout(1.5).Tip();
            c.Toggle("signs", true, ControlTier.More, "safety");

            c.Choice("groundCover", "none", ControlTier.More, "ground", "dirt", "gravel", "none").Tip();
            c.Toggle("mudPaint", true, ControlTier.More, "ground").Tip();
            c.Slider("paintLayer", 3, 1, 4, 1, "count", ControlTier.More, "ground").When("mudPaint=true").Tip();
            c.Slider("mudDecals", 3, 0, 10, 0.5, "per100m2", ControlTier.More, "ground").Tip();
            c.Toggle("puddles", true, ControlTier.More, "ground");

            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "assets");
            c.AssetMulti("cabinAssets", AssetCategories.ConstructionCabin, ControlTier.More, "assets");
            c.AssetMulti("machineAssets", AssetCategories.ConstructionMachine, ControlTier.More, "assets");
            c.AssetMulti("materialAssets", AssetCategories.ConstructionMaterial, ControlTier.More, "assets");
            c.AssetMulti("pileAssets", AssetCategories.ConstructionPile, ControlTier.More, "assets");
            c.AssetMulti("barrierAssets", AssetCategories.ConstructionBarrier, ControlTier.More, "assets");
            c.Asset("groundSurface", AssetCategories.SurfaceNatural, ControlTier.More, "assets");
            EntranceMarkers(c);

            c.RandomRange("depth", 3, 9);
            c.RandomRange("piles", 6, 20);
            c.RandomRange("stacks", 4, 16);
            c.RandomRange("machines", 2, 8);
            c.Fixed("paintLayer");
            c.Chance("fence", 0.85f);
            c.Chance("mudPaint", 0.8f);
        }

        protected override void DefinePresets()
        {
            Preset("foundation", ("subtype", "foundation"), ("depth", 6.0), ("workSize", 55.0), ("crane", true), ("machines", 4.0));
            Preset("roadworks", ("subtype", "roadworks"), ("depth", 2.0), ("workSize", 60.0), ("crane", false), ("machines", 6.0),
                ("coneSpacing", 6.0), ("groundCover", "none"), ("piles", 8.0), ("stacks", 6.0));
            Preset("compound", ("subtype", "compound"), ("compound", "large"), ("cabins", 10.0), ("containers", 10.0), ("toilets", 4.0),
                ("crane", false), ("stacks", 20.0), ("piles", 8.0), ("groundCover", "gravel"), ("machines", 6.0));
            Preset("demolition", ("subtype", "demolition"), ("depth", 3.0), ("workSize", 60.0), ("piles", 24.0), ("stacks", 4.0),
                ("crane", true), ("machines", 3.0), ("mudDecals", 4.0));
            Preset("earthworks", ("subtype", "earthworks"), ("depth", 5.0), ("workSize", 75.0), ("machines", 8.0), ("piles", 6.0),
                ("stacks", 2.0), ("crane", false), ("compound", "small"), ("coneSpacing", 10.0));
        }

        /// <summary>Everything the works zone of one subtype decided (geometry the rest of the plan works around).</summary>
        private sealed class Works
        {
            public float2[] Ring;                                   // place:works (reserved for features), may be null
            public float2 Entry;                                    // where the haul road ends
            public bool HasEntry;
            public Func<float2, float, float> Target;               // terraform target (NaN = leave)
            public readonly List<float2[]> Surfaces = new List<float2[]>();   // own surfaces (ground cover leaves them out)
            public readonly List<float2[]> Mud = new List<float2[]>();        // mud paint / decal zones
            public readonly List<float2> Anchors = new List<float2>();        // spots near the works for materials/machines
            public Func<float2, bool> Inside = _ => false;
        }

        private sealed class Site
        {
            public LayerContext Ctx;
            public LayerRegion Region;
            public Occupancy Occ;
            public float2 Centre, Axis, Side;
            public float Major, Minor;
            public float2 Gate, Inward;
            public bool HasGate;
            public string Subtype;
            public float Depth, Ha;
            public string[] Machines, Materials, Piles, Barriers, Cabins;
            public int MachinesPlaced, PilesPlaced, StacksPlaced, CabinsPlaced, ConesPlaced;
            public readonly List<List<float2>> Tracks = new List<List<float2>>();
        }

        /// <summary>Type-specific curated defaults (over GritAssets' shared sets); null = shared only.</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtObjects = new Dictionary<string, GritAssets.Set>
        {
            // Site lighting is 10 m work-light poles, not 20 m industrial masts.
            [AssetCategories.FloodLight] = new GritAssets.Set(true, "LightpoleCommercial01|LightpoleCommercial02|LightpoleIndustrial01", "LightpoleCommercial02|LightpoleCommercial01|LightpoleIndustrial01"),
            // Welfare and office units are containers; a shed only when none are installed.
            [AssetCategories.ConstructionCabin] = new GritAssets.Set(false, "ShippingContainer01|ToolShed03", "ShippingContainer02|ToolShed03", "ShippingContainer03|ToolShed03"),
        };

        /// <summary>(rundown) Old slabs and road strips weathered grey, never the bright white Concrete Surface 02 (it read as fresh plaza paving).</summary>
        private static readonly Dictionary<string, GritAssets.Set> ArtSurfaces = new Dictionary<string, GritAssets.Set>(StringComparer.Ordinal)
        {
            [AssetCategories.SurfacePaving] = new GritAssets.Set(true, "Pavement Surface 01|Concrete Surface 01"),
            [AssetCategories.SurfaceAsphalt] = new GritAssets.Set(true, "Pavement Surface 01|Concrete Surface 01"),
        };

        // Art pass: whatever the player left automatic gets the curated gritty defaults (GritAssets).
        protected override void PlanCore(LayerContext ctx) { PlanArt(ctx); GritAssets.Apply(ctx.Plan, ctx.Seed, ArtObjects, ArtSurfaces); GritAssets.CapDecals(ctx.Plan, ctx.LayerRegion.Area, 120f); }

        private void PlanArt(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var s = new Site
            {
                Ctx = ctx, Region = region, Occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site), Subtype = ctx.S("subtype"),
                Depth = math.max(0.5f, ctx.F("depth")), Ha = region.Area / 10000f,
                Machines = ctx.L("machineAssets"), Materials = ctx.L("materialAssets"), Piles = ctx.L("pileAssets"),
                Barriers = ctx.L("barrierAssets"), Cabins = ctx.L("cabinAssets"),
            };
            // Works follow the plot's dominant edges (a square plot used to get diagonal road strips).
            YardKit.GridFrame(region, out s.Centre, out s.Axis, out s.Side, out s.Major, out s.Minor);
            var gates = Gates(ctx);
            if (gates.Count > 0) { s.Gate = gates[0].Position; s.Inward = region.InwardNormal(gates[0].Segment); s.HasGate = true; }
            else { s.Gate = region.PointAt(0f, out _, out s.Inward, out _); }

            // 1. Perimeter hoarding with gate openings wide enough for trucks.
            if (ctx.B("fence")) Fence(ctx, gates, AssetMix.One(ctx.S("fenceAsset")), 4f);
            ctx.Check();

            // 2. Works zone (geometry first, so the road and compound plan around it).
            Works w;
            switch (s.Subtype)
            {
                case "roadworks": w = Roadworks(s); break;
                case "compound": w = StorageYard(s); break;
                case "demolition": w = Demolition(s); break;
                case "earthworks": w = Earthworks(s); break;
                default: w = Foundation(s); break;
            }
            ctx.Check();

            // 3. Terrain: one sculpt for the whole site.
            if (w.Target != null) SiteKit.Sculpt(ctx, region, w.Target, MaxSlopePct);
            ctx.Check();

            // 4. Haul road from the gate to the works.
            int paint = ctx.B("mudPaint") ? math.clamp(ctx.I("paintLayer"), 1, 4) : 0;
            bool ownTrackSurface = ctx.S("groundCover") == "none";
            var trackCat = ownTrackSurface ? SurfaceCategory(ctx, "gravel") : null;
            var gateIn = s.Gate + s.Inward * 1.5f;
            if (w.HasEntry && math.distance(gateIn, w.Entry) > 6f)
            {
                var line = new List<float2> { gateIn };
                var bend = s.Gate + s.Inward * math.min(14f, math.distance(gateIn, w.Entry) * 0.4f);
                if (region.Contains(bend) && !region.CrossesBoundary(bend, w.Entry, false)) line.Add(bend);
                line.Add(w.Entry);
                if (!region.CrossesBoundary(line[line.Count - 2], w.Entry, false))
                {
                    var rings = SiteKit.Track(ctx, s.Occ, region, line, 7f, trackCat, AssetMix.One(ctx.S("groundSurface")), paint);
                    if (ownTrackSurface) w.Surfaces.AddRange(rings);
                    w.Mud.AddRange(rings);
                    s.Tracks.Add(line);
                }
            }
            // Gate apron: mud tracked out of the site.
            var apron = LayerRegion.Rect(s.Gate + s.Inward * 6f, s.Inward, 6f, 5f);
            var apronIn = LeisureKit.Intersect(region, apron);
            if (apronIn != null) w.Mud.Add(apronIn.Ring);
            ctx.Check();

            // 5. Site compound beside the road.
            string compound = s.Subtype == "compound" ? "large" : ctx.S("compound");
            if (compound != "none") Compound(s, compound == "large", w);
            ctx.Check();

            // 6. Gate signs, materials, remaining machines, crane, lights.
            if (ctx.B("signs") && s.HasGate)
            {
                string sign = SiteKit.Cat(ctx, null, AssetCategories.ConstructionSign);
                var along = new float2(s.Inward.y, -s.Inward.x);
                foreach (var k in new[] { -1f, 1f })
                    SiteKit.Put(ctx, s.Occ, s.Gate + s.Inward * 2.5f + along * k * 5.5f, LeisureKit.Yaw(s.Gate, s.Gate - s.Inward), sign, null, 0.6f, 0x5160u + (uint)(k + 2));
            }
            Materials(s, w);
            MoreMachines(s, w);
            if (ctx.B("lights")) Lights(s);
            ctx.Check();

            // 7. Mud: paint and decals over the works, the road and the gate apron.
            // (rundown) no ground cover by default: the whole site is painted earth (a grey surface over all of it read as a flat
            // plastic sheet), the mud zones darker on top, and the designed ground (8b) lays the ruts, spills and water
            if (paint > 0 && ctx.S("groundCover") == "none")
            {
                var site = SiteKit.Inset(region.Ring, 1f);
                if (site != null && site.Length >= 3) TerrainPaint.Patch(ctx, site, paint, 0.85f, 5f);
            }
            if (paint > 0) foreach (var m in w.Mud) TerrainPaint.Patch(ctx, m, paint, 0.9f, 2f);
            float mud = ctx.F("mudDecals");
            if (mud > 0f)
            {
                string dirt = SiteKit.Cat(ctx, null, AssetCategories.DecalDirt, AssetCategories.Decal);
                string puddle = ctx.B("puddles") ? SiteKit.Cat(ctx, null, AssetCategories.DecalPuddles, AssetCategories.DecalDirt) : null;
                uint salt = 0xD1D0u;
                foreach (var m in w.Mud)
                {
                    SiteKit.Decals(ctx, s.Occ, m, mud, dirt, salt++, null, 160);
                    if (puddle != null) SiteKit.Decals(ctx, s.Occ, m, mud * 0.25f, puddle, salt++, null, 40);
                }
            }
            ctx.Check();

            // 8. Ground cover over everything not already surfaced.
            string cover = ctx.S("groundCover");
            if (cover != "none") GroundCover(ctx, region, w.Surfaces, SurfaceCategory(ctx, cover), AssetMix.One(ctx.S("groundSurface")));

            // 8b. (rundown) The designed ground: ruts, mud out of the gate, spills at the heaps, water in the dig (library decals).
            var worksC = w.Ring != null ? Poly.Centroid(w.Ring) : s.Centre;
            float worksR = w.Ring != null ? math.sqrt(math.abs(Poly.SignedArea(w.Ring)) / math.PI) : math.sqrt(region.Area) * 0.3f;
            ConstructionGround.Dress(ctx, s.Subtype, s.Gate, s.Inward, s.HasGate, s.Tracks, w.Ring, w.Mud, worksC, worksR * 1.2f);

            // 9. Regions for features: the works are a place (features keep out), the free ground is the yard.
            if (w.Ring != null) ctx.AddRegion(w.Ring, WorksPlace, WorksPlace);
            EmitYard(ctx, s.Occ, w.Inside);

            ctx.Stat(Keys.StatArea, Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat("Placemaker.Stat.Machines", s.MachinesPlaced, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Heaps", s.PilesPlaced, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Cabins", s.CabinsPlaced, Keys.UnitCount);
            if (ctx.Plan.Facts.TryGetValue("terrainMaxLower", out var cut) && cut > 0.2) ctx.Stat("Placemaker.Stat.PitDepth", Math.Round(cut, 1), Keys.UnitM);
            ctx.Plan.Facts["subtype"] = Array.IndexOf(Subtypes, s.Subtype);
        }

        // ───────────────────────── works zones ─────────────────────────

        /// <summary>Largest rectangle along the site's axis that fits around the given centre (shrinks until it does).</summary>
        private static bool FitRect(Site s, float2 centre, float2 axis, float halfL, float halfW, float margin, out OrientedRect rect)
        {
            for (int i = 0; i < 14; i++)
            {
                rect = new OrientedRect { Center = centre, Axis = axis, HalfLength = halfL, HalfWidth = halfW };
                if (halfW >= 4f && LeisureKit.RectFits(s.Region, s.Occ, rect, margin)) return true;
                halfL *= 0.88f; halfW *= 0.88f;
                if (halfW < 4f) break;
            }
            rect = default;
            return false;
        }

        private static float2 WorkCentre(Site s)
        {
            // Deepest point, nudged away from the gate so the compound and road fit in front of the works.
            var p = s.Region.DeepestPoint(out float ins, (q, cap) => s.Occ.Clearance(q, cap), 16);
            var q2 = p + math.normalizesafe(p - s.Gate) * ins * 0.25f;
            return s.Region.ContainsDisc(q2, ins * 0.6f) ? q2 : p;
        }

        private static Works Foundation(Site s)
        {
            var ctx = s.Ctx;
            var w = new Works();
            float size = math.sqrt(ctx.F("workSize") / 100f);
            float margin = s.Depth / (MaxSlopePct / 100f) + 6f;
            var c = WorkCentre(s);
            if (!FitRect(s, c, s.Axis, s.Major * 0.5f * size, s.Minor * 0.5f * size, margin, out var pit))
            {
                ctx.Issue(IssueSeverity.Info, "Placemaker.ConstructionSite.Issue.NoRoomForWorks");
                return w;
            }
            w.Ring = pit.Ring;
            var grown = new OrientedRect { Center = pit.Center, Axis = pit.Axis, HalfLength = pit.HalfLength + 3f, HalfWidth = pit.HalfWidth + 3f };
            w.Mud.Add(grown.Ring);
            w.Inside = p => Poly.Contains(grown.Ring, p);

            // Ramp inside one long side: from the rim (top, u = -L) down to full depth.
            float rampLen = math.min(s.Depth / 0.18f, pit.HalfLength * 1.6f);
            float rampW = math.min(7f, pit.HalfWidth * 0.5f);
            float rampSide = s.Region.BoundaryDistance(pit.Local(0, pit.HalfWidth)) > s.Region.BoundaryDistance(pit.Local(0, -pit.HalfWidth)) ? 1f : -1f;
            float refH = float.MaxValue;
            foreach (var q in pit.Ring) refH = math.min(refH, ctx.H(q));
            refH = math.min(refH, ctx.H(pit.Center));
            float depth = s.Depth;
            var pr = pit;
            w.Target = (p, h0) =>
            {
                var d = p - pr.Center;
                float u = math.dot(d, pr.Axis), v = math.dot(d, pr.Side) * rampSide;
                if (math.abs(u) > pr.HalfLength || math.abs(v) > pr.HalfWidth) return float.NaN;
                float k = 1f;
                if (v > pr.HalfWidth - rampW) k = math.saturate((u + pr.HalfLength) / rampLen);
                return refH - depth * k;
            };
            var rampTop = pit.Local(-pit.HalfLength, rampSide * (pit.HalfWidth - rampW * 0.5f));
            w.Entry = rampTop - pit.Axis * 3f; w.HasEntry = s.Region.ContainsDisc(w.Entry, 2f);

            // Rim: barriers all round (gap at the ramp mouth), crane at the far corner, cones at the mouth.
            string barrier = ctx.B("barriers") ? SiteKit.Cat(ctx, s.Barriers, AssetCategories.ConstructionBarrier, AssetCategories.Fence) : null;
            var corners = new OrientedRect { Center = pit.Center, Axis = pit.Axis, HalfLength = pit.HalfLength + 1.6f, HalfWidth = pit.HalfWidth + 1.6f }.Ring;
            if (barrier != null)
                for (int i = 0; i < 4; i++)
                {
                    var a = corners[i]; var b = corners[(i + 1) % 4];
                    // The short edge at u = -L holds the ramp mouth: leave the ramp half open.
                    if (math.dot((a + b) * 0.5f - pit.Center, pit.Axis) < -pit.HalfLength)
                    {
                        var mouth = pit.Local(-pit.HalfLength - 1.6f, rampSide * (pit.HalfWidth - rampW * 0.5f));
                        var dir = math.normalizesafe(b - a);
                        float t = math.dot(mouth - a, dir);
                        float len = math.distance(a, b);
                        if (t - rampW * 0.6f > 1f) SiteKit.Row(ctx, s.Occ, a, a + dir * (t - rampW * 0.6f), 2.2f, barrier, s.Barriers, 0.8f, 0xBA00u + (uint)i * 97u);
                        if (len - (t + rampW * 0.6f) > 1f) SiteKit.Row(ctx, s.Occ, a + dir * (t + rampW * 0.6f), b, 2.2f, barrier, s.Barriers, 0.8f, 0xBA40u + (uint)i * 97u);
                        Cones(s, a + dir * (t - rampW * 0.6f) - pit.Axis * 1.5f, a + dir * (t + rampW * 0.6f) - pit.Axis * 1.5f);
                    }
                    else SiteKit.Row(ctx, s.Occ, a, b, 2.2f, barrier, s.Barriers, 0.8f, 0xBA80u + (uint)i * 97u);
                }
            if (ctx.B("crane"))
            {
                string crane = SiteKit.Cat(ctx, null, AssetCategories.ConstructionCrane, AssetCategories.ConstructionMachine);
                var spot = pit.Local(pit.HalfLength * 0.6f, -rampSide * (pit.HalfWidth + 6f));
                if (!SiteKit.Put(ctx, s.Occ, spot, pit.Yaw, crane, null, 3f, 0xC4A0u))
                    SiteKit.Put(ctx, s.Occ, pit.Local(pit.HalfLength * 0.3f, 0f), pit.Yaw, crane, null, 3f, 0xC4A1u);
            }

            // Floor: excavator(s), rebar stacks, formwork / scaffold along the far wall, concrete mixer at the ramp top.
            string machine = SiteKit.Cat(ctx, s.Machines, AssetCategories.ConstructionMachine);
            if (SiteKit.Put(ctx, s.Occ, pit.Local(pit.HalfLength * 0.4f, -rampSide * pit.HalfWidth * 0.2f), pit.Yaw + 0.6f, machine, s.Machines, 3f, 0xE0E0u)) s.MachinesPlaced++;
            if (pit.HalfLength > 25f && SiteKit.Put(ctx, s.Occ, pit.Local(-pit.HalfLength * 0.1f, -rampSide * pit.HalfWidth * 0.4f), pit.Yaw - 0.9f, machine, s.Machines, 3f, 0xE0E1u)) s.MachinesPlaced++;
            string material = SiteKit.Cat(ctx, s.Materials, AssetCategories.ConstructionMaterial);
            s.StacksPlaced += SiteKit.Row(ctx, s.Occ, pit.Local(-pit.HalfLength * 0.3f, 0f), pit.Local(pit.HalfLength * 0.2f, 0f), 3.2f, material, s.Materials, 1.2f, 0x5EBu);
            string scaffold = SiteKit.Cat(ctx, null, AssetCategories.ConstructionScaffold, AssetCategories.ConstructionMaterial);
            SiteKit.Row(ctx, s.Occ, pit.Local(-pit.HalfLength + 3f, -rampSide * (pit.HalfWidth - 2f)), pit.Local(pit.HalfLength - 3f, -rampSide * (pit.HalfWidth - 2f)), 4f, scaffold, null, 1.2f, 0x5CAu);
            if (SiteKit.Put(ctx, s.Occ, rampTop - pit.Axis * 8f + pit.Side * rampSide * 5f, pit.Yaw, machine, s.Machines, 2.5f, 0xE0E2u)) s.MachinesPlaced++;
            LeisureKit.TakeRect(s.Occ, pit);
            w.Anchors.Add(pit.Local(0f, rampSide * (pit.HalfWidth + 9f)));
            w.Anchors.Add(pit.Local(0f, -rampSide * (pit.HalfWidth + 9f)));
            w.Anchors.Add(pit.Local(pit.HalfLength + 9f, 0f));
            return w;
        }

        private static Works Roadworks(Site s)
        {
            var ctx = s.Ctx;
            var w = new Works();
            var c = WorkCentre(s);
            float width = 8f + 8f * ctx.F("workSize") / 100f;
            if (!LeisureKit.Chord(s.Region, c, s.Axis, 6f + width * 0.5f, out var a, out var b) || math.distance(a, b) < 24f)
            {
                ctx.Issue(IssueSeverity.Info, "Placemaker.ConstructionSite.Issue.NoRoomForWorks");
                return w;
            }
            var strips = new List<(float2 a, float2 b, float hw)> { (a, b, width * 0.5f) };
            // A crossing strip on wide sites (junction works).
            if (s.Minor > 70f)
            {
                var at = math.lerp(a, b, 0.33f);
                if (LeisureKit.Chord(s.Region, at, s.Side, 6f + width * 0.4f, out var a2, out var b2) && math.distance(a2, b2) > 30f)
                    strips.Add((a2, b2, width * 0.4f));
            }
            var kinds = new[] { "milled", "trench", "base", "dirt" };
            float trench = math.min(s.Depth, 3f);
            var trenchRects = new List<OrientedRect>();
            string machine = SiteKit.Cat(ctx, s.Machines, AssetCategories.ConstructionMachine);
            string barrier = ctx.B("barriers") ? SiteKit.Cat(ctx, s.Barriers, AssetCategories.ConstructionBarrier, AssetCategories.Fence) : null;
            string sign = ctx.B("signs") ? SiteKit.Cat(ctx, null, AssetCategories.ConstructionSign) : null;
            string material = SiteKit.Cat(ctx, s.Materials, AssetCategories.ConstructionMaterial);
            var stripRings = new List<float2[]>();
            for (int si = 0; si < strips.Count; si++)
            {
                var (p0, p1, hw) = strips[si];
                var dir = math.normalizesafe(p1 - p0);
                var nrm = new float2(-dir.y, dir.x);
                float len = math.distance(p0, p1);
                int sections = math.max(1, (int)math.round(len / 32f));
                for (int k = 0; k < sections; k++)
                {
                    float t0 = k / (float)sections, t1 = (k + 1) / (float)sections;
                    var q0 = math.lerp(p0, p1, t0); var q1 = math.lerp(p0, p1, t1);
                    var rect = new OrientedRect { Center = (q0 + q1) * 0.5f, Axis = dir, HalfLength = math.distance(q0, q1) * 0.5f, HalfWidth = hw };
                    var inRegion = LeisureKit.Intersect(s.Region, rect.Ring);
                    if (inRegion == null) continue;
                    string kind = kinds[(k + si + (int)(Hashing.Mix(ctx.Seed, 0x5EC7u) % 4u)) % kinds.Length];
                    string cat = kind == "milled" ? SurfaceCategory(ctx, "asphalt") : kind == "base" ? SurfaceCategory(ctx, "gravel") : SurfaceCategory(ctx, "dirt");
                    if (cat != null && ctx.Surface(inRegion.Ring, SurfaceRole.Ground, AssetMix.One(""), cat)) w.Surfaces.Add(inRegion.Ring);
                    if (kind != "milled") w.Mud.Add(inRegion.Ring);
                    if (kind == "trench") trenchRects.Add(rect);
                    if (kind == "milled")
                    {
                        string cracks = SiteKit.Cat(ctx, null, AssetCategories.DecalCracks, AssetCategories.Decal);
                        SiteKit.Decals(ctx, s.Occ, inRegion.Ring, 2f, cracks, 0xC7Au + (uint)k, null, 60);
                    }
                    // One machine per section, by what the section needs.
                    var mpos = rect.Center + nrm * hw * 0.3f * (((k & 1) == 0) ? 1f : -1f);
                    if (SiteKit.Put(ctx, s.Occ, mpos, rect.Yaw, machine, s.Machines, 2.5f, 0x7A00u + (uint)(si * 31 + k))) s.MachinesPlaced++;
                    // Lane closure: barriers across half the strip at every section boundary.
                    if (barrier != null && k > 0)
                        SiteKit.Row(ctx, s.Occ, q0 + nrm * (hw - 0.8f), q0 + nrm * 0.5f, 2f, barrier, s.Barriers, 0.7f, 0xB100u + (uint)(si * 31 + k), false);
                    // Pipes / kerbs stacked beside trench and base sections.
                    if (kind == "trench" || kind == "base")
                        s.StacksPlaced += SiteKit.Row(ctx, s.Occ, q0 + dir * 4f + nrm * (hw + 3.5f), q1 - dir * 4f + nrm * (hw + 3.5f), 3.5f, material, s.Materials, 1.2f, 0x9190u + (uint)k);
                }
                // Cones along both edges, barriers across both ends, signs before the ends.
                float spacing = math.max(2f, ctx.F("coneSpacing"));
                Cones(s, p0 + nrm * (hw + 0.9f), p1 + nrm * (hw + 0.9f), spacing);
                Cones(s, p0 - nrm * (hw + 0.9f), p1 - nrm * (hw + 0.9f), spacing);
                if (barrier != null)
                {
                    SiteKit.Row(ctx, s.Occ, p0 - nrm * hw + dir * 0.8f, p0 + nrm * hw + dir * 0.8f, 2f, barrier, s.Barriers, 0.7f, 0xB1E0u + (uint)si);
                    SiteKit.Row(ctx, s.Occ, p1 - nrm * hw - dir * 0.8f, p1 + nrm * hw - dir * 0.8f, 2f, barrier, s.Barriers, 0.7f, 0xB1F0u + (uint)si);
                }
                if (sign != null)
                {
                    SiteKit.Put(ctx, s.Occ, p0 + nrm * (hw + 2.5f) + dir * 2f, LeisureKit.Yaw(p0, p0 - dir), sign, null, 0.6f, 0x5161u + (uint)si);
                    SiteKit.Put(ctx, s.Occ, p1 - nrm * (hw + 2.5f) - dir * 2f, LeisureKit.Yaw(p1, p1 + dir), sign, null, 0.6f, 0x5171u + (uint)si);
                }
                s.Occ.TakeCorridor(p0, p1, hw, SiteKit.KindTrack);
                var full = LeisureKit.Intersect(s.Region, new OrientedRect { Center = (p0 + p1) * 0.5f, Axis = dir, HalfLength = len * 0.5f, HalfWidth = hw }.Ring);
                if (full != null) stripRings.Add(full.Ring);
                w.Anchors.Add(math.lerp(p0, p1, 0.25f) + nrm * (hw + 9f));
                w.Anchors.Add(math.lerp(p0, p1, 0.75f) - nrm * (hw + 9f));
            }
            if (stripRings.Count > 0) w.Ring = stripRings[0];
            w.Inside = p => { foreach (var r in stripRings) if (Poly.Contains(r, p)) return true; return false; };
            // Haul road joins the strip at its end nearest the gate.
            w.Entry = math.distance(a, s.Gate) < math.distance(b, s.Gate) ? a : b;
            w.HasEntry = true;
            if (trenchRects.Count > 0 && trench > 0.3f)
            {
                float refH = float.MaxValue;
                foreach (var r in trenchRects) foreach (var q in r.Ring) refH = math.min(refH, ctx.H(q));
                var rects = trenchRects;
                w.Target = (p, h0) =>
                {
                    foreach (var r in rects)
                    {
                        var d = p - r.Center;
                        float u = math.dot(d, r.Axis), v = math.dot(d, r.Side);
                        if (math.abs(u) <= r.HalfLength && math.abs(v) <= r.HalfWidth + 2f)
                            return math.min(h0, refH) - trench * SiteKit.Smooth(r.HalfLength, r.HalfLength - 6f, math.abs(u));
                    }
                    return float.NaN;
                };
            }
            return w;
        }

        private static Works StorageYard(Site s)
        {
            var ctx = s.Ctx;
            var w = new Works();
            // Bays (10 × 6 m) in rows with 6 m aisles over the deep part of the site.
            var c = WorkCentre(s);
            float size = math.sqrt(ctx.F("workSize") / 100f);
            if (!FitRect(s, c, s.Axis, s.Major * 0.5f * size, s.Minor * 0.5f * size, 6f, out var yard))
            {
                ctx.Issue(IssueSeverity.Info, "Placemaker.ConstructionSite.Issue.NoRoomForWorks");
                return w;
            }
            var yardRegion = LayerRegion.From(yard.Ring);
            var bays = LeisureKit.Pack(yardRegion, new Occupancy(yardRegion, null), yard.Center, yard.Axis, 10f, 6f, 1.5f, 6f, 120, 0.5f);
            string material = SiteKit.Cat(ctx, s.Materials, AssetCategories.ConstructionMaterial);
            string pile = SiteKit.Cat(ctx, s.Piles, AssetCategories.ConstructionPile, AssetCategories.ConstructionRubble, AssetCategories.Rock);
            string container = SiteKit.Cat(ctx, null, AssetCategories.ConstructionContainer, AssetCategories.ConstructionSkip);
            string machine = SiteKit.Cat(ctx, s.Machines, AssetCategories.ConstructionMachine);
            int i = 0;
            foreach (var bay in bays)
            {
                uint h = Hashing.Mix(ctx.Seed, 0xBA7u + (uint)i);
                switch (h % 5u)
                {
                    case 0: case 1:
                        s.StacksPlaced += SiteKit.Row(ctx, s.Occ, bay.Local(-bay.HalfLength + 1.5f, -1.4f), bay.Local(bay.HalfLength - 1.5f, -1.4f), 2.4f, material, s.Materials, 1f, h);
                        s.StacksPlaced += SiteKit.Row(ctx, s.Occ, bay.Local(-bay.HalfLength + 1.5f, 1.4f), bay.Local(bay.HalfLength - 1.5f, 1.4f), 2.4f, material, s.Materials, 1f, h + 7u);
                        break;
                    case 2:
                        s.PilesPlaced += SiteKit.Cluster(ctx, s.Occ, bay.Center, 2, 2.5f, pile, s.Piles, 2.2f, h);
                        break;
                    case 3:
                        SiteKit.Row(ctx, s.Occ, bay.Local(-bay.HalfLength + 1.5f, 0f), bay.Local(bay.HalfLength - 1.5f, 0f), 2.9f, container, null, 1.3f, h, true);
                        break;
                    default:
                        if (SiteKit.Put(ctx, s.Occ, bay.Center, bay.Yaw, machine, s.Machines, 2.6f, h)) s.MachinesPlaced++;
                        break;
                }
                i++;
            }
            string gravel = SurfaceCategory(ctx, "gravel");
            if (gravel != null && ctx.S("groundCover") != "gravel" && ctx.Surface(yard.Ring, SurfaceRole.Ground, AssetMix.One(ctx.S("groundSurface")), gravel))
                w.Surfaces.Add(yard.Ring);
            w.Mud.Add(yard.Ring);
            w.Inside = p => Poly.Contains(yard.Ring, p);
            w.Entry = yard.Local(-yard.HalfLength, 0f);
            if (math.distance(yard.Local(yard.HalfLength, 0f), s.Gate) < math.distance(w.Entry, s.Gate)) w.Entry = yard.Local(yard.HalfLength, 0f);
            w.HasEntry = true;
            LeisureKit.TakeRect(s.Occ, yard);
            w.Anchors.Add(yard.Local(0f, yard.HalfWidth + 8f));
            w.Anchors.Add(yard.Local(0f, -yard.HalfWidth - 8f));
            return w;
        }

        private static Works Demolition(Site s)
        {
            var ctx = s.Ctx;
            var w = new Works();
            var c = s.Region.DeepestPoint(out float ins, (q, cap) => s.Occ.Clearance(q, cap), 16);
            float r = math.min(math.sqrt(s.Region.Area * ctx.F("workSize") / 100f / math.PI), ins * 0.92f);
            if (r < 10f) { ctx.Issue(IssueSeverity.Info, "Placemaker.ConstructionSite.Issue.NoRoomForWorks"); return w; }
            var field = LeisureKit.Blob(c, r, Hashing.Mix(ctx.Seed, 0xDE30u), 18, 0.18f);
            var fieldRegion = LeisureKit.Intersect(s.Region, field);
            if (fieldRegion == null) return w;
            field = fieldRegion.Ring;
            w.Ring = field;
            w.Inside = p => Poly.Contains(field, p);
            w.Mud.Add(field);

            // Cracked slabs of the demolished buildings, partly buried by the rubble.
            string slab = SurfaceCategory(ctx, "paving");
            string cracks = SiteKit.Cat(ctx, null, AssetCategories.DecalCracks, AssetCategories.Decal);
            int slabs = math.clamp((int)(r / 12f), 1, 4);
            var rng = Hashing.Rng(ctx.Seed, 0x51ABu);
            for (int i = 0; i < slabs; i++)
            {
                var sc = c + new float2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)) * r * 0.45f;
                var sr = new OrientedRect { Center = sc, Axis = s.Axis, HalfLength = rng.NextFloat(6f, 12f), HalfWidth = rng.NextFloat(5f, 9f) };
                var inField = LeisureKit.Intersect(fieldRegion, sr.Ring);
                if (inField == null || inField.Area < 30f) continue;
                if (slab != null && ctx.Surface(inField.Ring, SurfaceRole.Paving, AssetMix.One(""), slab)) w.Surfaces.Add(inField.Ring);
                SiteKit.Decals(ctx, s.Occ, inField.Ring, 3f, cracks, 0xC8Au + (uint)i, null, 40);
                // A retained facade wall braced by scaffold along one slab edge.
                if (i == 0)
                {
                    string scaffold = SiteKit.Cat(ctx, null, AssetCategories.ConstructionScaffold, AssetCategories.ConstructionMaterial);
                    SiteKit.Row(ctx, s.Occ, sr.Local(-sr.HalfLength + 1f, sr.HalfWidth - 1f), sr.Local(sr.HalfLength - 1f, sr.HalfWidth - 1f), 3.5f, scaffold, null, 1.1f, 0x5CBu);
                }
            }

            // Rubble mounds (raised, noisy) and rubble heaps on them; machines working the heaps.
            float height = s.Depth;
            uint noise = Hashing.Mix(ctx.Seed, 0x40B5u);
            var fr = fieldRegion;
            w.Target = (p, h0) =>
            {
                if (!fr.Contains(p)) return float.NaN;
                float d = fr.BoundaryDistance(p, 20f);
                float n = ValueNoise.Clumps(p, 16f, noise);
                return h0 + height * math.saturate((n - 0.35f) / 0.65f) * SiteKit.Smooth(0f, 10f, d);
            };
            string rubble = SiteKit.Cat(ctx, s.Piles, AssetCategories.ConstructionRubble, AssetCategories.ConstructionPile, AssetCategories.Rock);
            int wanted = math.clamp((int)(ctx.F("piles") * s.Ha * 1.5f), 4, 120);
            int placed = 0;
            foreach (var p in Scatter.Candidates(fieldRegion, 3f, Hashing.Mix(ctx.Seed, 0x4B1u), wanted * 12 + 60, ctx.Cancel))
            {
                if (placed >= wanted) break;
                if (ValueNoise.Clumps(p, 16f, noise) < 0.4f) continue;
                if (SiteKit.Put(ctx, s.Occ, p, Hashing.Unit(Hashing.Mix(ctx.Seed, (int)p.x, (int)p.y)) * 6.283f, rubble, s.Piles, PileRadius(ctx) * 0.8f, 0x4B2u + (uint)placed)) placed++;
            }
            s.PilesPlaced += placed;
            string machine = SiteKit.Cat(ctx, s.Machines, AssetCategories.ConstructionMachine);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + 0.4f;
                var p = c + new float2(math.cos(a), math.sin(a)) * r * 0.55f;
                if (SiteKit.Put(ctx, s.Occ, p, a + 3.14f, machine, s.Machines, 3f, 0xDE40u + (uint)i)) s.MachinesPlaced++;
            }
            if (ctx.B("crane"))
            {
                string crane = SiteKit.Cat(ctx, null, AssetCategories.ConstructionCrane, AssetCategories.ConstructionMachine);
                SiteKit.Put(ctx, s.Occ, c + math.normalizesafe(c - s.Gate) * r * 0.2f, 0f, crane, null, 3f, 0xDE50u);
            }
            // Skips lined up at the field's edge nearest the gate (where the trucks load).
            var edge = c + math.normalizesafe(s.Gate - c) * (r + 5f);
            var along = new float2(-math.normalizesafe(s.Gate - c).y, math.normalizesafe(s.Gate - c).x);
            string skip = SiteKit.Cat(ctx, null, AssetCategories.ConstructionSkip, AssetCategories.ConstructionContainer);
            SiteKit.Row(ctx, s.Occ, edge - along * 9f, edge + along * 9f, 4.5f, skip, null, 1.8f, 0x5C1Bu, true);
            w.Entry = edge + math.normalizesafe(s.Gate - c) * 5f; w.HasEntry = s.Region.ContainsDisc(w.Entry, 2f);
            w.Anchors.Add(c + s.Axis * (r + 9f)); w.Anchors.Add(c - s.Axis * (r + 9f));
            return w;
        }

        private static Works Earthworks(Site s)
        {
            var ctx = s.Ctx;
            var w = new Works();
            var c = WorkCentre(s);
            float size = ctx.F("workSize") / 100f;
            // Cut on the side away from the gate, fill (spoil mounds) on the gate side.
            float sideSign = math.dot(s.Gate - c, s.Axis) > 0f ? -1f : 1f;
            var cutC = c + s.Axis * sideSign * s.Major * 0.18f;
            float cutR = math.min(math.sqrt(s.Region.Area * size * 0.5f / math.PI), s.Region.BoundaryDistance(cutC) - s.Depth / (MaxSlopePct / 100f) - 4f);
            cutR = math.min(cutR, s.Occ.Clearance(cutC, 200f) - 2f);
            cutR = math.min(cutR, math.max(s.Major * 0.24f, s.Minor * 0.3f));
            if (cutR < 10f) { ctx.Issue(IssueSeverity.Info, "Placemaker.ConstructionSite.Issue.NoRoomForWorks"); return w; }
            var cut = LeisureKit.Intersect(s.Region, LeisureKit.Blob(cutC, cutR, Hashing.Mix(ctx.Seed, 0xC07u), 16, 0.15f));
            if (cut == null) return w;
            w.Ring = cut.Ring;
            w.Mud.Add(cut.Ring);
            float refH = float.MaxValue;
            foreach (var q in cut.Ring) refH = math.min(refH, ctx.H(q));
            float depth = s.Depth;
            // Spoil mounds on the other side.
            var heaps = new List<(float2 c, float r, float h)>();
            var fillC = c - s.Axis * sideSign * s.Major * 0.24f;
            var rng = Hashing.Rng(ctx.Seed, 0xF111u);
            int heapCount = math.clamp((int)(s.Ha * 2f), 1, 5);
            for (int i = 0; i < heapCount * 10 && heaps.Count < heapCount; i++)
            {
                var p = fillC + new float2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)) * s.Minor * 0.3f;
                float hr = rng.NextFloat(9f, 16f);
                if (!s.Region.ContainsDisc(p, hr + 4f) || cut.Contains(p) || cut.BoundaryDistance(p, hr + 8f) < hr + 6f) continue;
                bool clash = false;
                foreach (var hp in heaps) if (math.distance(hp.c, p) < hp.r + hr + 4f) clash = true;
                if (clash || s.Occ.Clearance(p, hr + 2f) < hr) continue;
                heaps.Add((p, hr, depth * rng.NextFloat(0.5f, 0.9f)));
            }
            var cr = cut;
            w.Target = (p, h0) =>
            {
                if (cr.Contains(p)) return math.min(h0, refH) - depth * SiteKit.Smooth(0f, 14f, cr.BoundaryDistance(p, 20f));
                float best = float.NaN;
                foreach (var hp in heaps)
                {
                    float d = math.distance(p, hp.c);
                    if (d >= hp.r) continue;
                    float t = h0 + hp.h * SiteKit.Smooth(hp.r, hp.r * 0.25f, d);
                    best = float.IsNaN(best) ? t : math.max(best, t);
                }
                return best;
            };
            w.Inside = p => cr.Contains(p);

            // Haul loop: cut → every heap → back, machines along it.
            var entry = cut.ClosestOnBoundary(s.Gate, out _, out _);
            entry += math.normalizesafe(s.Gate - entry) * 3f;
            w.Entry = entry; w.HasEntry = s.Region.ContainsDisc(entry, 2f);
            string machine = SiteKit.Cat(ctx, s.Machines, AssetCategories.ConstructionMachine);
            if (SiteKit.Put(ctx, s.Occ, cutC, rng.NextFloat(0f, 6.28f), machine, s.Machines, 3f, 0xEA00u)) s.MachinesPlaced++;
            var loop = new List<float2> { entry };
            foreach (var hp in heaps)
            {
                var foot = hp.c + math.normalizesafe(entry - hp.c) * (hp.r + 4f);
                if (!s.Region.ContainsDisc(foot, 4f) || s.Region.CrossesBoundary(loop[loop.Count - 1], foot, false)) continue;
                loop.Add(foot);
                if (SiteKit.Put(ctx, s.Occ, hp.c, rng.NextFloat(0f, 6.28f), machine, s.Machines, 3f, 0xEA10u + (uint)loop.Count)) s.MachinesPlaced++;
                w.Mud.Add(LayerRegion.Circle(hp.c, hp.r, 14));
            }
            if (loop.Count > 1)
            {
                int paint = ctx.B("mudPaint") ? math.clamp(ctx.I("paintLayer"), 1, 4) : 0;
                var cat = ctx.S("groundCover") == "none" ? SurfaceCategory(ctx, "dirt") : null;
                var rings = SiteKit.Track(ctx, s.Occ, s.Region, loop, 7f, cat, AssetMix.One(ctx.S("groundSurface")), paint);
                if (cat != null) w.Surfaces.AddRange(rings);
                w.Mud.AddRange(rings);
                // Dump trucks queued along the loop.
                for (int i = 0; i + 1 < loop.Count; i++)
                {
                    var mid = math.lerp(loop[i], loop[i + 1], 0.5f);
                    var nrm = math.normalizesafe(new float2(-(loop[i + 1] - loop[i]).y, (loop[i + 1] - loop[i]).x));
                    if (SiteKit.Put(ctx, s.Occ, mid + nrm * 7f, LeisureKit.Yaw(loop[i], loop[i + 1]), machine, s.Machines, 2.8f, 0xEA40u + (uint)i)) s.MachinesPlaced++;
                }
            }
            // Survey stakes (cones) round the cut.
            float spacing = math.max(4f, ctx.F("coneSpacing") * 1.5f);
            var cutRing = cut.Ring;
            for (int i = 0; i < cutRing.Length; i++)
            {
                var a = cutRing[i]; var b = cutRing[(i + 1) % cutRing.Length];
                var o = math.normalizesafe(a + b - 2f * cutC) * 2f;
                Cones(s, a + o, b + o, spacing);
            }
            s.Occ.Take(cut.DeepestPoint(out float cins, null, 10), math.max(1f, cins), SiteKit.KindTrack);
            foreach (var hp in heaps) { s.Occ.Take(hp.c, hp.r * 0.8f, SiteKit.KindTrack); w.Anchors.Add(hp.c + s.Side * (hp.r + 7f)); }
            w.Anchors.Add(cutC + s.Side * (cutR + 8f));
            return w;
        }

        // ───────────────────────── compound, materials, machines, lights ─────────────────────────

        private static void Compound(Site s, bool large, Works w)
        {
            var ctx = s.Ctx;
            float depth = large ? 22f : 13f, width = large ? 44f : 24f;
            float fit = math.sqrt(s.Region.Area) * (large ? 0.55f : 0.4f);
            width = math.min(width, fit); depth = math.min(depth, fit * 0.6f);
            if (depth < 8f) return;
            var along = new float2(s.Inward.y, -s.Inward.x);
            OrientedRect rect = default; bool found = false;
            foreach (var off in new[] { 1f, -1f, 1.6f, -1.6f, 2.4f, -2.4f })
            {
                if (found) break;
                foreach (var shrink in new[] { 1f, 0.8f, 0.65f })
                {
                    float hl = width * 0.5f * shrink, hw = depth * 0.5f * shrink;
                    var cc = s.Gate + s.Inward * (hw + 2.5f) + along * math.sign(off) * (hl + 5f) * math.abs(off);
                    var r = new OrientedRect { Center = cc, Axis = along, HalfLength = hl, HalfWidth = hw };
                    if (w.Inside(cc)) continue;
                    if (LeisureKit.RectFits(s.Region, s.Occ, r, 1f)) { rect = r; found = true; break; }
                }
            }
            if (!found)
            {
                // Anywhere near the outline.
                float rad = depth * 0.5f;
                if (!Spots.Find(s.Occ, rad + 2f, Spots.Preference.NearBoundary, Hashing.Mix(ctx.Seed, 0xC0Fu), out var spot, null, 0f, 1f, 600)) return;
                rect = new OrientedRect { Center = spot, Axis = s.Axis, HalfLength = rad, HalfWidth = rad };
                if (!LeisureKit.RectFits(s.Region, s.Occ, rect, 0.5f)) return;
            }
            // Back side = the long side farther from the outline.
            float back = s.Region.BoundaryDistance(rect.Local(0f, rect.HalfWidth)) > s.Region.BoundaryDistance(rect.Local(0f, -rect.HalfWidth)) ? 1f : -1f;
            string cabin = SiteKit.Cat(ctx, s.Cabins, AssetCategories.ConstructionCabin, AssetCategories.GardenShed, AssetCategories.Shelter);
            int cabins = ctx.I("cabins");
            float hL = rect.HalfLength, hW = rect.HalfWidth;
            float yawFront = LeisureKit.Yaw(rect.Local(0f, back), rect.Local(0f, -back));
            for (int row = 0; row < (large ? 2 : 1) && s.CabinsPlaced < cabins; row++)
            {
                float v = back * (hW - 2.5f - row * 8f);
                foreach (var p in LeisureKit.Along(rect.Local(-hL + 5f, v), rect.Local(hL - 9f, v), 7f))
                {
                    if (s.CabinsPlaced >= cabins) break;
                    if (SiteKit.Put(ctx, s.Occ, p, yawFront + (row == 1 ? math.PI : 0f), cabin, s.Cabins, 2.6f, 0xCAB0u + (uint)s.CabinsPlaced, 0.3f)) s.CabinsPlaced++;
                }
            }
            string toilet = SiteKit.Cat(ctx, null, AssetCategories.ConstructionToilet, AssetCategories.Restroom);
            int toilets = ctx.I("toilets");
            if (toilets > 0)
                SiteKit.Row(ctx, s.Occ, rect.Local(hL - 2f, back * (hW - 1.5f)), rect.Local(hL - 2f, back * (hW - 1.5f - toilets * 1.9f)), 1.9f, toilet, null, 0.8f, 0x7011u);
            string container = SiteKit.Cat(ctx, null, AssetCategories.ConstructionContainer, AssetCategories.ConstructionSkip);
            int containers = ctx.I("containers");
            if (containers > 0)
            {
                float v = -back * (hW - 2f);
                int n = 0;
                foreach (var p in LeisureKit.Along(rect.Local(-hL + 2f, v), rect.Local(hL - 2f, v), 2.9f))
                {
                    if (n >= containers) break;
                    if (SiteKit.Put(ctx, s.Occ, p, LeisureKit.Yaw(rect.Local(0, -back), rect.Local(0, back)), container, null, 1.3f, 0xC0A0u + (uint)n, 0.3f)) n++;
                }
            }
            string gen = SiteKit.Cat(ctx, null, AssetCategories.ConstructionGenerator, AssetCategories.ConstructionMachine);
            SiteKit.Put(ctx, s.Occ, rect.Local(-hL + 2f, -back * 1f), 0f, gen, null, 1.2f, 0x6E1u);
            string bin = SiteKit.Cat(ctx, null, AssetCategories.ConstructionSkip, AssetCategories.Bin);
            SiteKit.Put(ctx, s.Occ, rect.Local(-hL + 2f, back * 3f), 0f, bin, null, 1.5f, 0x6E2u);
            if (ctx.S("groundCover") == "none")
            {
                string gravel = SurfaceCategory(ctx, "gravel");
                if (gravel != null && ctx.Surface(rect.Ring, SurfaceRole.Ground, AssetMix.One(ctx.S("groundSurface")), gravel)) w.Surfaces.Add(rect.Ring);
            }
            LeisureKit.TakeRect(s.Occ, rect);
            ctx.Plan.Facts["compound"] = large ? 2 : 1;
        }

        private static float PileRadius(LayerContext ctx)
        {
            switch (ctx.S("pileSize")) { case "small": return 1.6f; case "large": return 4f; default: return 2.6f; }
        }

        private static void Materials(Site s, Works w)
        {
            var ctx = s.Ctx;
            int piles = math.min(80, (int)math.round(ctx.F("piles") * s.Ha));
            int stacks = math.min(80, (int)math.round(ctx.F("stacks") * s.Ha));
            if (s.Subtype == "demolition") piles = math.max(0, piles / 3);
            string pile = piles > 0 ? SiteKit.Cat(ctx, s.Piles, AssetCategories.ConstructionPile, AssetCategories.ConstructionRubble, AssetCategories.Rock) : null;
            string material = stacks > 0 ? SiteKit.Cat(ctx, s.Materials, AssetCategories.ConstructionMaterial) : null;
            float pr = PileRadius(ctx);
            var centres = new List<float2>();
            int anchor = 0;
            // Heaps in clusters of 3–4 by the works, stacks in short rows of pallets.
            for (int i = 0; i < 24 && (s.PilesPlaced < piles || s.StacksPlaced < stacks); i++)
            {
                ctx.Check();
                bool doPile = pile != null && s.PilesPlaced < piles && (material == null || s.StacksPlaced >= stacks || (i & 1) == 0);
                float r = doPile ? pr * 2.5f : 5f;
                float2 c;
                if (anchor < w.Anchors.Count && s.Occ.Clearance(w.Anchors[anchor], r + 1f) >= r) c = w.Anchors[anchor++];
                else
                {
                    anchor = math.max(anchor, w.Anchors.Count);
                    if (!Spots.Find(s.Occ, r, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x3A7u + (uint)i), out c, centres, r * 2f + 6f, 1f, 500)) break;
                }
                if (w.Inside(c)) continue;
                centres.Add(c);
                if (doPile) s.PilesPlaced += SiteKit.Cluster(ctx, s.Occ, c, math.min(4, piles - s.PilesPlaced), pr * 2f, pile, s.Piles, pr, 0x9100u + (uint)i * 53u);
                else if (material != null)
                {
                    var dir = Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i)) > 0.5f ? s.Axis : s.Side;
                    int row = math.min(4, stacks - s.StacksPlaced);
                    s.StacksPlaced += SiteKit.Row(ctx, s.Occ, c - dir * row * 1.3f, c + dir * row * 1.3f, 2.6f, material, s.Materials, 1.1f, 0x9200u + (uint)i * 53u);
                }
            }
        }

        private static void MoreMachines(Site s, Works w)
        {
            var ctx = s.Ctx;
            int wanted = ctx.I("machines");
            if (s.MachinesPlaced >= wanted) return;
            string machine = SiteKit.Cat(ctx, s.Machines, AssetCategories.ConstructionMachine);
            if (machine == null) return;
            var spots = new List<float2>();
            for (int i = 0; s.MachinesPlaced < wanted && i < wanted * 2; i++)
            {
                if (!Spots.Find(s.Occ, 3.5f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0xAC0u + (uint)i), out var p, spots, 10f, 1f, 400)) break;
                spots.Add(p);
                if (w.Inside(p)) continue;
                if (SiteKit.Put(ctx, s.Occ, p, Hashing.Unit(Hashing.Mix(ctx.Seed, 0xAC1u + (uint)i)) * 6.283f, machine, s.Machines, 3f, 0xAC2u + (uint)i)) s.MachinesPlaced++;
            }
        }

        private static void Lights(Site s)
        {
            var ctx = s.Ctx;
            string light = SiteKit.Cat(ctx, null, AssetCategories.FloodLight, AssetCategories.Lamp);
            if (light == null) return;
            int n = math.clamp((int)(s.Region.Perimeter / 70f), 2, 10);
            for (int i = 0; i < n; i++)
            {
                var p = s.Region.PointAt(s.Region.Perimeter * (i + 0.25f) / n, out _, out var inward, out _);
                var q = p + inward * 3f;
                SiteKit.Put(ctx, s.Occ, q, LeisureKit.Yaw(q, s.Centre), light, null, 0.6f, 0x1A00u + (uint)i, 1f);
            }
        }

        private static void Cones(Site s, float2 a, float2 b, float spacing = 0f)
        {
            var ctx = s.Ctx;
            if (spacing <= 0f) spacing = math.max(1.5f, ctx.F("coneSpacing") * 0.4f);
            string cone = SiteKit.Cat(ctx, null, AssetCategories.ConstructionCone, AssetCategories.Bollard);
            s.ConesPlaced += SiteKit.Row(ctx, s.Occ, a, b, spacing, cone, null, 0.35f, 0xC0E0u + (uint)s.ConesPlaced * 13u);
        }

        // ───────────────────────── ground ─────────────────────────

        /// <summary>Surface category for a ground kind, with fallbacks (Info when nothing is installed).</summary>
        public static string SurfaceCategory(LayerContext ctx, string kind)
        {
            switch (kind)
            {
                case "dirt": return LeisureKit.Category(ctx, "", AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural);
                case "asphalt": return LeisureKit.Category(ctx, "", AssetCategories.SurfaceAsphalt, AssetCategories.SurfacePaving);
                case "paving": return LeisureKit.Category(ctx, "", AssetCategories.SurfacePaving, AssetCategories.SurfaceAsphalt);
                case "sand": return LeisureKit.Category(ctx, "", AssetCategories.SurfaceSand, AssetCategories.SurfaceNatural);
                default: return LeisureKit.Category(ctx, "", AssetCategories.SurfaceNatural, AssetCategories.SurfaceDirt);
            }
        }

        /// <summary>Region minus the surfaces already laid, as ground surfaces (CS2 z-fights stacked surfaces).</summary>
        public static void GroundCover(LayerContext ctx, LayerRegion region, List<float2[]> own, string category, AssetRef asset)
        {
            if (category == null) return;
            List<Region> pieces;
            try
            {
                if (own.Count == 0) pieces = new List<Region> { new Region(region.Ring) };
                else
                {
                    var holes = Poly.UnionAll(own);
                    var holeRings = new List<IReadOnlyList<float2>>();
                    foreach (var h in holes) if (h.Outer != null && h.Outer.Length >= 3) holeRings.Add(h.Outer);
                    pieces = Poly.Boolean(new IReadOnlyList<float2>[] { region.Ring }, holeRings, BoolOp.Difference);
                }
            }
            catch (Exception) { pieces = new List<Region> { new Region(region.Ring) }; }
            foreach (var piece in pieces)
            {
                if (piece.Outer == null || piece.Outer.Length < 3 || Poly.Area(piece.Outer) < 20f) continue;
                if (piece.Holes.Count == 0) ctx.Surface(piece.Outer, SurfaceRole.Ground, asset, category);
                else
                    foreach (var cutRing in SurfaceAcceptance.CutHoles(piece.Outer, piece.Holes, 0.5f))
                        if (cutRing != null && cutRing.Length >= 3 && Poly.Area(cutRing) >= 20f) ctx.Surface(cutRing, SurfaceRole.Ground, asset, category);
            }
        }

        /// <summary>Free ground (not the works, not taken) as "green" regions for features; the largest is "core".</summary>
        public static void EmitYard(LayerContext ctx, Occupancy occ, Func<float2, bool> works)
        {
            var region = ctx.LayerRegion;
            var pieces = FreeSpace.Pieces(region, p => works(p) || occ.Corridors.Overlaps(p, 0.75f) || occ.HitsPolygon(p, 0.5f), 3f, 150f, ctx.Cancel);
            if (pieces.Count == 0) { ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core); return; }
            for (int i = 0; i < pieces.Count && i < 16; i++)
                if (i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
        }
    }
}

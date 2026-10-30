// Hiking trail: a trail that reads the terrain. The route is found by TrailRouter (A* that hates
// grades above the maximum), so on slopes it follows the contours and climbs in switchbacks; on
// ridges it keeps to the crest, in canyons to the valley floor. Route shapes: a loop back to the
// trailhead, a through trail between two entrances, or a summit trail (up one way, down another).
//
// Along it: a trailhead at the entrance (gravel car park with bay markings, map board, bench and
// bin), rest spots every few hundred metres (bench facing the downhill view, bin, picnic table),
// viewpoints at the most prominent points (platform with benches, or a lookout tower), way markers,
// and footbridges wherever the route crosses water. Landscape presets plant forest, meadow, open
// ridge or rocky canyon, and can carve a gorge (canyon) or lift a ridge under the trail — always
// blended back to the original ground at the outline.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Hiking
{
    [AreaGenerator(270, Group = "nature")]
    public sealed class HikingTrailLayer : LeisureLayer
    {
        public const string LayerId = "hikingTrail";
        public static readonly string[] Landscapes = { "forest", "meadow", "ridge", "canyon", "lakeshore" };

        protected override string Area => "HikingTrail";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/Pathways.svg";
        public override float MinArea => 3000f;
        public override string[] Emits => new[] { RegionTags.Green, RegionTags.Core };
        protected override bool HostsStreetShops => false;
        protected override int WantedEntrances(LayerRegion region) => region.Area > 40000f ? 2 : 1;

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            // Picnic, rocks and leaf decals in the glades ("core"), not across the whole outline (and any lake in it).
            CoreFeature(TrailCampAddOn.LayerId),
            CoreFeature("picnic", ("groups", 2.0), ("tables", 2.0)),
            CoreFeature("rocks", ("clusters", 4.0), ("size", "medium")),
            Feature("restrooms", ("count", 1.0), ("byArea", false)),
            CoreFeature("decals", ("theme", "leaves"), ("density", 2.0), ("ground", "lawn")),
        };

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("landscape", "forest", ControlTier.Essential, "trailRoute", Landscapes).Tip();
            c.Choice("route", "loop", ControlTier.Essential, "trailRoute", "loop", "through", "summit").Tip();
            c.Slider("maxGrade", 10, 4, 25, 1, "%", ControlTier.Essential, "trailRoute").Tip();
            c.Slider("wander", 40, 0, 100, 5, "%", ControlTier.Essential, "trailRoute").Tip();
            c.Choice("trailWidth", "narrow", ControlTier.More, "trailRoute", "narrow", "wide");

            c.Slider("restSpacing", 220, 0, 600, 20, "m", ControlTier.Essential, "trailStops").Layout(40).Tip();
            c.Slider("viewpoints", 2, 0, 6, 1, "count", ControlTier.Essential, "trailStops").Tip();
            c.Toggle("lookoutTower", false, ControlTier.More, "trailStops").Tip();
            c.Toggle("picnicTables", true, ControlTier.More, "trailStops").Tip();
            c.Slider("waymarkers", 150, 0, 400, 25, "m", ControlTier.More, "trailStops").Layout(25).Tip();

            c.Choice("trailheads", "main", ControlTier.More, "trailheads", "main", "all", "none").Tip();
            // (rf-nature) Off by default: the gravel "car park" has no driveway from the street, so on every site it read as
            // a bare sand rectangle by the entrance. The slider still adds one.
            c.Slider("parking", 0, 0, 30, 1, "count", ControlTier.More, "trailheads").Tip();
            c.Toggle("mapBoard", true, ControlTier.More, "trailheads").Tip();
            c.Choice("trailheadGround", "gravel", ControlTier.More, "trailheads", "gravel", "asphalt");

            c.Toggle("bridges", true, ControlTier.More, "water").Tip();

            c.Slider("treeDensity", 180, 0, 450, 10, "perHa", ControlTier.More, "planting").Tip();
            c.Slider("bushDensity", 60, 0, 300, 10, "perHa", ControlTier.More, "planting");
            c.Slider("trailClear", 3, 1, 10, 0.5, "m", ControlTier.More, "planting").Tip();
            c.Slider("clearings", 1, 0, 6, 1, "count", ControlTier.More, "planting").Tip();
            c.Toggle("animals", true, ControlTier.More, "planting").Tip();
            c.Toggle("wornTrail", false, ControlTier.More, "planting").Tip();
            c.Slider("paintLayer", 1, 1, 4, 1, "count", ControlTier.More, "planting").When("wornTrail=true");

            c.Choice("shapeTerrain", "auto", ControlTier.More, "terrain", "auto", "none", "gorge", "ridge").Tip();
            c.Slider("terrainDepth", 4, 1, 12, 0.5, "m", ControlTier.More, "terrain").Tip();
            c.Slider("rocks", 3, 0, 12, 1, "count", ControlTier.More, "terrain").Tip();

            c.Asset("trailAsset", AssetCategories.Pathway, ControlTier.More, "assets");
            c.Asset("bridgeAsset", AssetCategories.Pathway, ControlTier.More, "assets").Tip();
            c.AssetMulti("signAssets", AssetCategories.TrailSign, ControlTier.More, "assets");
            c.Asset("benchAsset", AssetCategories.Bench, ControlTier.More, "assets");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "assets");
            c.AssetMulti("bushSpecies", AssetCategories.Bush, ControlTier.More, "assets");
            EntranceMarkers(c);

            c.RandomRange("maxGrade", 6, 14);
            c.RandomRange("treeDensity", 40, 300);
            c.Fixed("wornTrail");
            c.Fixed("shapeTerrain");
        }

        protected override void DefinePresets()
        {
            Preset("forestTrail", ("landscape", "forest"), ("route", "loop"), ("treeDensity", 300.0), ("bushDensity", 100.0), ("wander", 50.0), ("viewpoints", 1.0));
            // Art pass a3: trails are narrow everywhere ("wide" is the 8 m urban walk, never a hiking trail).
            Preset("meadowWalk", ("landscape", "meadow"), ("route", "loop"), ("treeDensity", 12.0), ("bushDensity", 70.0), ("wander", 70.0), ("maxGrade", 8.0),
                ("trailWidth", "narrow"), ("restSpacing", 150.0), ("wornTrail", true), ("viewpoints", 2.0));
            Preset("ridgeHike", ("landscape", "ridge"), ("route", "through"), ("treeDensity", 50.0), ("bushDensity", 40.0), ("maxGrade", 14.0),
                ("viewpoints", 4.0), ("rocks", 5.0), ("terrainDepth", 3.0));
            Preset("canyonTrail", ("landscape", "canyon"), ("route", "through"), ("treeDensity", 40.0), ("bushDensity", 90.0), ("maxGrade", 12.0),
                ("rocks", 8.0), ("terrainDepth", 6.0), ("viewpoints", 1.0));
            Preset("summitClimb", ("landscape", "ridge"), ("route", "summit"), ("treeDensity", 120.0), ("maxGrade", 8.0), ("lookoutTower", true),
                ("viewpoints", 3.0), ("restSpacing", 160.0));
            Preset("lakeshoreLoop", ("landscape", "lakeshore"), ("route", "loop"), ("treeDensity", 90.0), ("bushDensity", 120.0), ("restSpacing", 160.0),
                ("viewpoints", 3.0), ("trailWidth", "narrow"));
        }

        // ───────────── art direction (art pass a3) ─────────────
        // Explicit vanilla species and furniture per landscape while the asset controls are automatic. The lookout
        // pool is a 56 m fire-station upgrade and a 32 m water-tower deck, so towers/viewpoints never use it: the
        // "tower" is an open timber shelter on the viewpoint, the viewpoint itself benches and a map board.

        private static string[] Trees(string landscape)
        {
            switch (landscape)
            {
                case "meadow": return new[] { "OakTree01", "NA_LindenTree01", "EU_ChestnutTree01" };
                case "ridge": return new[] { "PineTree01", "SpruceTree01", "BirchTree01" };
                case "canyon": return new[] { "PineTree01", "OakTree01", "BirchTree01" };
                case "lakeshore": return new[] { "EU_AlderTree01", "BirchTree01", "EU_PoplarTree01", "OakTree01" };
                default: return new[] { "OakTree01", "SpruceTree01", "EU_ChestnutTree01", "PineTree01", "BirchTree01" };
            }
        }

        private static string[] Bushes(string landscape) => landscape == "meadow"
            ? new[] { "GreenBushWildChild01", "FlowerBushWildChild02", "GreenBushWildTeen01", "GreenBushWild01", "GreenBushWildElderly01" }
            : new[] { "GreenBushWild01", "GreenBushWild02", "GreenBushWildAdult01", "GreenBushWildTeen01" };

        private static readonly string[] TrailBenches = { "CampgroundBench01", "CampgroundBench02" };
        private static readonly string[] TrailBins = { "Trashbin01", "Trashbin04" };
        private static readonly string[] MapBoards = { "ParkInfosign03", "ParkInfosign01" };
        private static readonly string[] Waymarks = { "ParkSignpost01", "ParkSignpost02" };
        private static readonly string[] Shelters = { "CampgroundShelter02", "CampgroundShelter01" };
        private static readonly string[] PicnicTables = { "CampgroundTableset01", "CampgroundTableset02" };

        private static AssetRef Bench(LayerContext ctx) => AssetMix.Pick(ctx.S("benchAsset").Length > 0 ? new[] { ctx.S("benchAsset") } : TrailBenches, 0u);

        // ───────────── planning ─────────────

        private sealed class Trail
        {
            public LayerContext Ctx;
            public LayerRegion Region;
            public Occupancy Occ;
            public TrailGrid Grid;
            public NetRole Role;
            public float Hw;
            public readonly List<List<float2>> Lines = new List<List<float2>>();   // simplified legs, in walking order
            public readonly List<PlannedNet> Nets = new List<PlannedNet>();
            public readonly List<(float2 p, float2 dir, float s)> Samples = new List<(float2, float2, float)>();
            public float Length;
            public int Bridges, Rests, Views, Heads;
            public float MaxGrade;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var gates = Gates(ctx);
            string landscape = ctx.S("landscape");
            var t = new Trail
            {
                Ctx = ctx, Region = region, Occ = occ,
                Role = ctx.S("trailWidth") == "wide" ? NetRole.PathwayWide : NetRole.Pathway,
            };
            t.Hw = Composer.DefaultHalfWidth(t.Role);

            t.Grid = TrailGrid.Build(ctx, region, occ, 3.5f + t.Hw, Hashing.Mix(ctx.Seed, 0x7A11u));
            t.Grid.MaxGrade = ctx.F("maxGrade") / 100f;
            t.Grid.Wander = ctx.F("wander") / 100f;
            t.Grid.Bridges = ctx.B("bridges");
            t.Grid.Preference = landscape == "ridge" ? TrailPreference.High : landscape == "canyon" || landscape == "lakeshore" ? TrailPreference.Low : TrailPreference.None;
            ctx.Check();

            Route(t, gates);
            ctx.Check();
            if (t.Lines.Count == 0)
            {
                ctx.Issue(IssueSeverity.Warning, "Placemaker.HikingTrail.Issue.NoRoute");
                EmitGreenRegions(ctx, occ);
                return;
            }
            Emit(t, gates);
            ctx.Check();

            bool shaped = ShapeTerrain(t, landscape);
            Trailheads(t, gates);
            Viewpoints(t, gates);
            RestSpots(t, gates);
            Waymarkers(t);
            ctx.Check();
            var clearings = Clearings(t);
            Rocks(t, landscape);
            Planting(t, landscape);
            ctx.Check();
            if (ctx.B("wornTrail")) WornTrail(t);
            if (ctx.B("animals"))
            {
                int n = math.clamp((int)(region.Area / 10000f), 1, 8);
                var spots = new List<float2>();
                for (int i = 0; i < n; i++)
                {
                    if (!Spots.Find(occ, 3f, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0xA41Au + (uint)i), out var s, spots, 30f, 1f, 1200)) break;
                    spots.Add(s);
                    ctx.Object(s, 0f, ObjectRole.AnimalSpawner, new AssetRef("", "Forest Animal Spawner"), Hashing.Mix(ctx.Seed, (uint)i + 0xA41Bu));
                }
            }

            // Clearings are where features go (core = the largest); otherwise the free lawn pieces.
            if (clearings.Count > 0)
                for (int i = 0; i < clearings.Count; i++)
                    if (i == 0) ctx.AddRegion(LayerRegion.Circle(clearings[i].c, clearings[i].r, 20), "glade:0", RegionTags.Green, RegionTags.Core);
                    else ctx.AddRegion(LayerRegion.Circle(clearings[i].c, clearings[i].r, 20), "glade:" + i, RegionTags.Green);
            else EmitGreenRegions(ctx, occ);
            // The trail was routed and emitted on the original ground: put it (and everything since) onto the shaped one.
            if (shaped) Sculpt.Resnap(ctx);
            CommonStats(ctx);
            ctx.Stat(Keys.StatBushes, Count(ctx.Plan, ObjectRole.Bush), Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Viewpoints", t.Views, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.RestSpots", t.Rests, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Footbridges", t.Bridges, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.SteepestGrade", System.Math.Round(t.MaxGrade * 100f), "%");
            ctx.Plan.Facts["trailLength"] = t.Length;
            ctx.Plan.Facts["footbridges"] = t.Bridges;
            ctx.Plan.Facts["trailheads"] = t.Heads;
        }

        // ───────────── route ─────────────

        private static void Route(Trail t, List<Gate> gates)
        {
            var ctx = t.Ctx; var grid = t.Grid; var region = t.Region;
            if (gates.Count == 0) return;
            var g0 = gates[0];
            int start = grid.NearestOk(g0.Position, true);
            if (start < 0) start = grid.NearestOk(g0.Position);
            if (start < 0) return;
            var startP = grid.Centre(start);
            string route = ctx.S("route");

            // Interesting cells.
            int far = -1, summit = -1; float bestFar = -1f, bestSummit = float.MinValue;
            for (int i = 0; i < grid.Count; i++)
            {
                if (!grid.Ok[i] || grid.Wet(i)) continue;
                var c = grid.Centre(i);
                float interior = math.min(grid.Edge[i], 25f);
                float df = math.distance(c, startP) + interior * 0.5f;
                if (df > bestFar) { bestFar = df; far = i; }
                float hs = grid.H[i] + interior * 0.02f + (grid.HMax - grid.HMin < 1f ? interior : 0f);
                if (hs > bestSummit) { bestSummit = hs; summit = i; }
            }
            if (far < 0) return;

            var waypoints = new List<int> { start };
            var mainLegsEndAtGate = false;
            int second = -1;
            if (route == "through")
            {
                // To the farthest other entrance, else to the far end (out and back ends at a viewpoint).
                float best = 0f;
                for (int i = 1; i < gates.Count; i++)
                {
                    float d = math.distance(gates[i].Position, g0.Position);
                    if (d > best) { best = d; second = i; }
                }
                int mid = MidWaypoint(grid, startP, second >= 0 ? gates[second].Position : grid.Centre(far), t.Grid.Preference == TrailPreference.High ? summit : -1);
                if (mid >= 0) waypoints.Add(mid);
                int end = second >= 0 ? grid.NearestOk(gates[second].Position, true) : far;
                if (end >= 0 && end != waypoints[waypoints.Count - 1]) waypoints.Add(end);
            }
            else if (route == "summit")
            {
                int top = summit >= 0 && math.distance(grid.Centre(summit), startP) > 40f ? summit : far;
                waypoints.Add(top);
                waypoints.Add(start);
                mainLegsEndAtGate = true;
            }
            else
            {
                // Loop: out along one side of the line to the far point, back along the other.
                var dir = math.normalizesafe(grid.Centre(far) - startP);
                int w1 = Side(grid, startP, dir, 1f), w2 = Side(grid, startP, dir, -1f);
                if (w1 >= 0) waypoints.Add(w1);
                if (region.Area > 20000f || w1 < 0 || w2 < 0) waypoints.Add(far);
                if (w2 >= 0) waypoints.Add(w2);
                waypoints.Add(start);
                mainLegsEndAtGate = true;
            }

            // Legs.
            var exempt = new List<float2> { startP };
            var cellsAll = new List<int>();
            var mask = new bool[grid.Count];
            var legCells = new List<List<int>>();
            for (int i = 0; i + 1 < waypoints.Count; i++)
            {
                if (waypoints[i] == waypoints[i + 1]) continue;
                var cells = grid.Route(waypoints[i], waypoints[i + 1], null, ctx.Cancel);
                if (cells == null) continue;
                legCells.Add(cells);
                exempt.Add(grid.Centre(waypoints[i + 1]));
                grid.Stamp(cells, 12f, 2.5f, exempt, 14f);
                foreach (var c in cells) { mask[c] = true; cellsAll.Add(c); }
            }
            if (legCells.Count == 0) return;

            // Chain legs into one walking line starting (and for loops ending) at the gate.
            var main = new List<float2> { g0.Position };
            var keep = new List<bool> { true };
            for (int l = 0; l < legCells.Count; l++)
            {
                var cells = legCells[l];
                for (int k = 0; k < cells.Count; k++)
                {
                    var p = grid.Centre(cells[k]);
                    if (math.distancesq(p, main[main.Count - 1]) < 1e-4f) continue;
                    main.Add(p);
                    keep.Add(k == cells.Count - 1 || grid.Wet(cells[k]) != (k + 1 < cells.Count && grid.Wet(cells[k + 1])) || (k > 0 && grid.Wet(cells[k]) != grid.Wet(cells[k - 1])));
                }
            }
            if (mainLegsEndAtGate) { main.Add(g0.Position); keep.Add(true); }
            else if (second >= 0) { main.Add(gates[second].Position); keep.Add(true); }
            t.Lines.Add(TrailLine.Simplify(main, keep.ToArray(), grid.Cell * 0.7f));

            // Other entrances join the trail at its nearest vertex.
            for (int i = 1; i < gates.Count; i++)
            {
                if (i == second) continue;
                int s = grid.NearestOk(gates[i].Position, true);
                if (s < 0 || mask[s]) continue;
                var cells = grid.Route(s, -1, mask, ctx.Cancel);
                if (cells == null) continue;
                var line = new List<float2> { gates[i].Position };
                var kp = new List<bool> { true };
                foreach (var c in cells) { line.Add(grid.Centre(c)); kp.Add(grid.Wet(c)); }
                // End exactly on an existing vertex so the nets share a node.
                var endP = line[line.Count - 1];
                float bd = float.MaxValue; float2 snap = endP;
                foreach (var v in t.Lines[0]) { float d = math.distancesq(v, endP); if (d < bd) { bd = d; snap = v; } }
                line[line.Count - 1] = snap; kp[kp.Count - 1] = true;
                t.Lines.Add(TrailLine.Simplify(line, kp.ToArray(), grid.Cell * 0.7f));
            }
        }

        /// <summary>Interior cell near the middle between a and b (or the summit when given), for through trails.</summary>
        private static int MidWaypoint(TrailGrid grid, float2 a, float2 b, int summit)
        {
            if (summit >= 0 && math.distance(grid.Centre(summit), a) > 30f && math.distance(grid.Centre(summit), b) > 30f) return summit;
            var mid = math.lerp(a, b, 0.5f);
            var side = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
            int best = -1; float bs = float.MinValue;
            for (int i = 0; i < grid.Count; i++)
            {
                if (!grid.Ok[i] || grid.Wet(i)) continue;
                var c = grid.Centre(i);
                // Off the straight line by a fair amount: the trail swings through the land instead of beelining.
                float off = math.abs(math.dot(c - mid, side));
                float s = math.min(grid.Edge[i], 30f) + math.min(off, math.distance(a, b) * 0.25f) * 0.6f - math.distance(c, mid) * 0.5f;
                if (s > bs) { bs = s; best = i; }
            }
            return best;
        }

        /// <summary>Loop waypoint on one side of the gate→far line, well inside and away from the line.</summary>
        private static int Side(TrailGrid grid, float2 start, float2 dir, float sign)
        {
            var n = new float2(-dir.y, dir.x) * sign;
            int best = -1; float bs = 8f;
            for (int i = 0; i < grid.Count; i++)
            {
                if (!grid.Ok[i] || grid.Wet(i)) continue;
                var d = grid.Centre(i) - start;
                float lateral = math.dot(d, n);
                if (lateral <= 0f) continue;
                float s = lateral * 0.8f + math.dot(d, dir) * 0.3f + math.min(grid.Edge[i], 20f) * 0.5f;
                if (s > bs) { bs = s; best = i; }
            }
            return best;
        }

        // ───────────── emission ─────────────

        private static void Emit(Trail t, List<Gate> gates)
        {
            var ctx = t.Ctx; var grid = t.Grid;
            var asset = AssetMix.One(ctx.S("trailAsset"));
            var bridgeAsset = AssetMix.One(ctx.S("bridgeAsset"), "Wooden Pedestrian Bridge", "Pedestrian Bridge", "Arc Pedestrian Bridge");
            // Deck height at a route vertex: the ground on a bank, else 1 m over the water surface (a vertex can lie
            // in the stream; using the bed height there put the footbridge under water, art pass a3).
            var depthAt = ctx.Site?.WaterDepthAt;
            float DeckY(float2 p)
            {
                float d = depthAt != null ? depthAt(p) : 0f;
                return d > TrailGrid.WetDepth ? ctx.H(p) + d + 1f : ctx.H(p);
            }
            var gateAt = new Dictionary<long, Gate>();
            foreach (var line in t.Lines)
            {
                int n = line.Count;
                for (int i = 0; i + 1 < n; i++)
                {
                    var a = line[i]; var b = line[i + 1];
                    float len = math.distance(a, b);
                    if (len < 0.5f) continue;
                    bool wet = Wet(ctx, a, b);
                    PlannedNet net;
                    if (wet)
                    {
                        // Footbridge: straight, level between the banks, not following the terrain (or the water).
                        net = ctx.StraightNet(a, b, t.Role, bridgeAsset);
                        float ha = DeckY(a), hb = DeckY(b);
                        net.A.y = ha; net.D.y = hb;
                        net.B.y = math.lerp(ha, hb, 1f / 3f); net.C.y = math.lerp(ha, hb, 2f / 3f);
                        net.FollowTerrain = false;
                        t.Bridges++;
                    }
                    else
                    {
                        var p0 = i > 0 ? line[i - 1] : a; var p3 = i + 2 < n ? line[i + 2] : b;
                        var cb = a + (b - p0) / 6f; var cc = b - (p3 - a) / 6f;
                        net = CurveInside(t, a, cb, cc, b) ? ctx.CurvedNet(a, cb, cc, b, t.Role, asset) : ctx.StraightNet(a, b, t.Role, asset);
                        float grade = math.abs(ctx.H(b) - ctx.H(a)) / len;
                        t.MaxGrade = math.max(t.MaxGrade, grade);
                    }
                    foreach (var g in gates)
                    {
                        if (g.AttachId < 0) continue;
                        if (math.distancesq(g.Position, a) < 1e-4f) { net.StartAttach = g.AttachId; net.StartAttachT = g.AttachT; }
                        if (math.distancesq(g.Position, b) < 1e-4f) { net.EndAttach = g.AttachId; net.EndAttachT = g.AttachT; }
                    }
                    t.Nets.Add(net);
                    // Index the built footprint and walk samples.
                    var prev = a;
                    for (int k = 1; k <= 8; k++)
                    {
                        var q = Composer.Bezier(net.A, net.B, net.C, net.D, k / 8f).xz;
                        t.Occ.TakeCorridor(prev, q, t.Hw, 100);
                        t.Occ.Paths.Add(prev, q, t.Hw, 100);
                        float seg = math.distance(prev, q);
                        if (seg > 1e-3f) t.Samples.Add((q, (q - prev) / seg, t.Length + seg));
                        t.Length += seg;
                        prev = q;
                    }
                }
            }
        }

        private static bool Wet(LayerContext ctx, float2 a, float2 b)
        {
            var w = ctx.Site?.WaterDepthAt;
            if (w == null) return false;
            for (int i = 1; i < 6; i++) if (w(math.lerp(a, b, i / 6f)) > TrailGrid.WetDepth) return true;
            return false;
        }

        private static bool CurveInside(Trail t, float2 a, float2 b, float2 c, float2 d)
        {
            for (int i = 1; i < 8; i++)
            {
                float u = i / 8f, v = 1f - u;
                var p = v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * d;
                if (!t.Region.ContainsDisc(p, t.Hw + 0.5f)) return false;
                if (Wet(t.Ctx, p, p + 0.01f)) return false;
            }
            return true;
        }

        // ───────────── terrain ─────────────

        /// <summary>
        /// Gorge (canyon) or ridge under the trail. (rf-nature) On a sloped node grid (Common/Sculpt) instead of one
        /// levelled 12 m square per cell: on any ground that was not flat the old cells became a staircase of flat
        /// squares (the ridge and summit trails on hills), and the 4-12 m steps between them left the game pits up to
        /// 80 m deep beside the trail. The change follows the ground's own shape, dies out towards the outline, is never
        /// steeper than 30 %, and the trail, its furniture and its planting are put back onto the new ground.
        /// </summary>
        private static bool ShapeTerrain(Trail t, string landscape)
        {
            var ctx = t.Ctx;
            string mode = ctx.S("shapeTerrain");
            if (mode == "auto") mode = landscape == "canyon" ? "gorge" : landscape == "ridge" ? "ridge" : "none";
            if (mode != "gorge" && mode != "ridge") return false;
            float sign = mode == "gorge" ? -1f : 1f;
            float depth = ctx.F("terrainDepth");
            var region = t.Region;
            // About 2000 cells at most: the build rasters every patch, and ~4800 on a 10 ha trail took the whole 6 s
            // terraform budget before a texel moved ("terraform incomplete (time limit)", nothing shaped).
            float cell = math.max(Sculpt.DefaultCell, math.sqrt(region.Area / 2000f));
            float floor = 6f + t.Hw, wall = 10f + depth * 3f, falloff = 20f, slope = 0.3f;
            var wet = ctx.Site?.WaterDepthAt;
            var sc = Sculpt.Over(ctx, region, null, cell, p => wet != null && wet(p) > 0.05f);
            sc.Shape((p, h0, cur) =>
            {
                float d = t.Occ.Paths.Clearance(p, floor + wall + 1f);
                float x = math.saturate((d - floor) / wall);
                float profile = 1f - x * x * (3f - 2f * x);
                float edge = Sculpt.Smooth01(region.BoundaryDistance(p, falloff + 1f) / falloff);
                return h0 + sign * depth * profile * edge;
            });
            sc.Smooth(2);
            sc.LimitGrade(slope);
            sc.CapToBorder(slope);
            var r = sc.Emit(ctx, new[] { region.Ring });
            return r != null && r.Cells > 0;
        }

        // ───────────── stops ─────────────

        private static string SignCategory(LayerContext ctx) =>
            ctx.L("signAssets").Length > 0 ? AssetCategories.TrailSign : LeisureKit.Category(ctx, "", AssetCategories.TrailSign);

        private static void Trailheads(Trail t, List<Gate> gates)
        {
            var ctx = t.Ctx;
            string mode = ctx.S("trailheads");
            if (mode == "none") return;
            int count = mode == "all" ? gates.Count : math.min(1, gates.Count);
            string sign = ctx.B("mapBoard") ? SignCategory(ctx) : null;
            var bench = Bench(ctx);
            var boards = ctx.L("signAssets").Length > 0 ? ctx.L("signAssets") : MapBoards;
            for (int gi = 0; gi < count; gi++)
            {
                var g = gates[gi];
                var n = t.Region.InwardNormal(g.Segment);
                var tg = new float2(n.y, -n.x);
                t.Heads++;
                // Car park beside the trail entry.
                int stalls = ctx.I("parking");
                float side = 1f;
                if (stalls > 0)
                {
                    bool two = stalls > 12;
                    int perRow = two ? (stalls + 1) / 2 : stalls;
                    float len = perRow * 2.6f + 1f, depth = two ? 16f : 11f;
                    bool done = false;
                    for (int k = 0; k < 8 && !done; k++)
                    {
                        side = (k & 1) == 0 ? 1f : -1f;
                        float along = t.Hw + 2.5f + len * 0.5f + (k / 2) * 6f;
                        var r = new OrientedRect { Center = g.Position + tg * side * along + n * (depth * 0.5f + 0.6f), Axis = tg, HalfLength = len * 0.5f, HalfWidth = depth * 0.5f };
                        if (!LeisureKit.RectFits(t.Region, t.Occ, r, 0.3f)) continue;
                        done = true;
                        bool asphalt = ctx.S("trailheadGround") == "asphalt";
                        ctx.Surface(r.Ring, asphalt ? SurfaceRole.Asphalt : SurfaceRole.Ground, AssetMix.One(""), asphalt ? AssetCategories.SurfaceAsphalt : AssetCategories.SurfaceNatural);
                        LeisureKit.TakeRect(t.Occ, r, 6);
                        // Bay markings only on an asphalt lot: a gravel trailhead is a field car park, and road
                        // markings never go into leisure places (art pass a3).
                        string marks = asphalt && AssetAvailability.Has(AssetCategories.DecalMarkings) ? AssetCategories.DecalMarkings : null;
                        if (marks != null)
                            for (int row = 0; row < (two ? 2 : 1); row++)
                            {
                                float v = two ? (row == 0 ? -r.HalfWidth + 2.5f : r.HalfWidth - 2.5f) : -r.HalfWidth + 2.5f;
                                for (int s = 0; s <= perRow; s++)
                                {
                                    var p = r.Local(-r.HalfLength + 0.5f + s * 2.6f, v);
                                    ctx.Object(p, math.atan2(n.x, n.y), ObjectRole.Decal, AssetMix.One(""), Hashing.Mix(ctx.Seed, 0x9A4Bu + (uint)(s + row * 40 + gi * 100)), -1, marks);
                                }
                            }
                        ctx.Plan.Facts["parkingStalls"] = (ctx.Plan.Facts.TryGetValue("parkingStalls", out var ps) ? ps : 0) + stalls;
                    }
                }
                // Map board, bench and bin on the other side of the entry.
                var basePt = g.Position + n * 5f - tg * side * (t.Hw + 1.4f);
                if (sign != null)
                    LeisureKit.Put(ctx, t.Occ, basePt, LeisureKit.Yaw(basePt, basePt + tg * side), ObjectRole.Furniture, AssetMix.Pick(boards, 0u), sign, 0.6f,
                        Hashing.Mix(ctx.Seed, 0x5160u + (uint)gi), 0.5f, 0.2f);
                var bp = basePt + n * 3f - tg * side * 0.6f;
                LeisureKit.Put(ctx, t.Occ, bp, LeisureKit.Yaw(bp, bp + tg * side), ObjectRole.Bench, bench, null, 1.3f, Hashing.Mix(ctx.Seed, 0x5161u + (uint)gi), 0.5f, 0.2f);
                LeisureKit.Put(ctx, t.Occ, basePt - n * 1.8f - tg * side * 0.6f, 0f, ObjectRole.Bin, AssetMix.Pick(TrailBins, 0u), null, 0.5f, Hashing.Mix(ctx.Seed, 0x5162u + (uint)gi), 0.5f, 0.2f);
            }
        }

        private static bool NearGate(float2 p, List<Gate> gates, float d)
        {
            foreach (var g in gates) if (math.distance(g.Position, p) < d) return true;
            return false;
        }

        /// <summary>Side of the trail that looks downhill (+1 = left normal), and the drop seen that way.</summary>
        private static float ViewSide(Trail t, float2 p, float2 dir, out float drop)
        {
            var nn = new float2(-dir.y, dir.x);
            float h = t.Ctx.H(p);
            float l = h - t.Ctx.H(p + nn * 25f), r = h - t.Ctx.H(p - nn * 25f);
            // Ties (flat ground) look out towards the open interior.
            if (math.abs(l - r) < 0.05f)
            {
                l = t.Region.BoundaryDistance(p + nn * 15f, 60f); r = t.Region.BoundaryDistance(p - nn * 15f, 60f);
                drop = 0f;
                return l >= r ? 1f : -1f;
            }
            drop = math.max(l, r);
            return l > r ? 1f : -1f;
        }

        private static void Viewpoints(Trail t, List<Gate> gates)
        {
            var ctx = t.Ctx;
            int wanted = ctx.I("viewpoints");
            bool tower = ctx.B("lookoutTower");
            if (wanted <= 0 && !tower) return;
            wanted = math.max(wanted, tower ? 1 : 0);
            // Score samples every ~10 m by prominence (height above the ring 30 m around) and absolute height.
            var cand = new List<(int i, float score)>();
            float lastS = -100f;
            for (int i = 0; i < t.Samples.Count; i++)
            {
                var (p, _, s) = t.Samples[i];
                if (s - lastS < 10f || NearGate(p, gates, 50f)) continue;
                lastS = s;
                float h = ctx.H(p), ring = 0f;
                for (int k = 0; k < 8; k++) ring += ctx.H(p + new float2(math.cos(k * 0.785f), math.sin(k * 0.785f)) * 30f);
                float prom = h - ring / 8f;
                float score = prom + t.Grid.Rank(h) * 3f + Hashing.Unit(Hashing.Mix(ctx.Seed, (uint)i)) * 0.1f;
                cand.Add((i, score));
            }
            cand.Sort((a, b) => b.score.CompareTo(a.score));
            var chosen = new List<float>();
            var bench = Bench(ctx);
            string scope = LeisureKit.Category(ctx, "", AssetCategories.Shelter);
            string board = SignCategory(ctx);
            foreach (var (i, _) in cand)
            {
                if (t.Views >= wanted) break;
                var (p, dir, s) = t.Samples[i];
                bool spaced = true;
                foreach (var c in chosen) if (math.abs(c - s) < math.max(60f, t.Length / (wanted + 2f))) { spaced = false; break; }
                if (!spaced) continue;
                float side = ViewSide(t, p, dir, out _);
                var nn = new float2(-dir.y, dir.x) * side;
                var centre = p + nn * (t.Hw + 4.2f);
                if (!t.Occ.Free(centre, 3.4f, 4.3f, 0.2f)) continue;
                // Never out on the water: the platform and its seats stand on the bank (art pass a3).
                if (Wet(ctx, centre - nn * 4.5f, centre + nn * 4.5f) || Wet(ctx, centre - new float2(nn.y, -nn.x) * 4.5f, centre + new float2(nn.y, -nn.x) * 4.5f)) continue;
                bool isTower = tower && t.Views == 0 && scope != null;
                uint v = Hashing.Mix(ctx.Seed, 0x7140u + (uint)i);
                ctx.Surface(LeisureKit.Blob(centre, 3.8f, v, 12, 0.15f), SurfaceRole.Decorative, new AssetRef("", "Forestry Surface 01"), AssetCategories.SurfaceNatural);
                if (isTower)
                {
                    var shelter = AssetMix.Pick(Shelters, 0u);
                    float r = AssetMix.FootprintRadius(ctx.Site, shelter, 3.2f);
                    ctx.Object(centre, LeisureKit.Yaw(centre, centre + nn), ObjectRole.Furniture, shelter, v, -1, scope);
                    t.Occ.Take(centre, r, 0);
                }
                else
                {
                    var tg = new float2(nn.y, -nn.x);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var bp = centre + tg * (k * 1.5f) - nn * 0.4f;
                        ctx.Object(bp, LeisureKit.Yaw(bp, bp + nn), ObjectRole.Bench, bench, Hashing.Mix(v, (uint)k + 3u));
                    }
                    // A low map board at the back of the viewpoint, facing the view (what you look at is named).
                    if (board != null)
                        ctx.Object(centre - nn * 2.2f, LeisureKit.Yaw(centre, centre + nn), ObjectRole.Furniture, new AssetRef("", "ParkInfosign01", "ParkInfosign03"), Hashing.Mix(v, 9u), -1, board);
                    t.Occ.Take(centre, 3.4f, 0);
                }
                chosen.Add(s);
                t.Views++;
            }
        }

        private static void RestSpots(Trail t, List<Gate> gates)
        {
            var ctx = t.Ctx;
            float spacing = ctx.F("restSpacing");
            if (spacing <= 0f || t.Samples.Count == 0) return;
            var bench = Bench(ctx);
            string picnic = ctx.B("picnicTables") ? LeisureKit.Category(ctx, "", AssetCategories.Picnic) : null;
            int si = 0;
            for (float target = spacing * 0.6f; target < t.Length - 20f; target += spacing)
            {
                bool placed = false;
                for (int tryOff = 0; tryOff < 5 && !placed; tryOff++)
                {
                    float want = target + (tryOff == 0 ? 0f : ((tryOff & 1) == 0 ? 1f : -1f) * 12f * ((tryOff + 1) / 2));
                    while (si + 1 < t.Samples.Count && t.Samples[si].s < want) si++;
                    while (si > 0 && t.Samples[si].s > want) si--;
                    var (p, dir, _) = t.Samples[si];
                    if (NearGate(p, gates, 30f)) continue;
                    float side = ViewSide(t, p, dir, out _);
                    var nn = new float2(-dir.y, dir.x) * side;
                    var bp = p + nn * (t.Hw + 1.7f);
                    uint v = Hashing.Mix(ctx.Seed, 0x4E57u + (uint)t.Rests * 7u + (uint)tryOff);
                    if (!LeisureKit.Put(ctx, t.Occ, bp, LeisureKit.Yaw(bp, bp + nn), ObjectRole.Bench, bench, null, 1.3f, v, 0.8f, 0.2f)) continue;
                    placed = true;
                    t.Rests++;
                    LeisureKit.Put(ctx, t.Occ, p + nn * (t.Hw + 1.2f) + dir * 2.4f, 0f, ObjectRole.Bin, AssetMix.Pick(TrailBins, 0u), null, 0.5f, Hashing.Mix(v, 1u), 0.8f, 0.2f);
                    if (picnic != null)
                    {
                        var pp = p + nn * (t.Hw + 4.6f) - dir * 2.5f;
                        // A timber picnic table (the picnic pool is blankets and grills).
                        LeisureKit.Put(ctx, t.Occ, pp, LeisureKit.Yaw(pp, pp + dir), ObjectRole.Furniture, AssetMix.Pick(PicnicTables, 0u), picnic, 1.4f, Hashing.Mix(v, 2u), 0.8f, 0.2f);
                    }
                }
            }
        }

        private static void Waymarkers(Trail t)
        {
            var ctx = t.Ctx;
            float every = ctx.F("waymarkers");
            if (every <= 0f) return;
            string cat = SignCategory(ctx);
            if (cat == null) return;
            var ids = ctx.L("signAssets").Length > 0 ? ctx.L("signAssets") : Waymarks;
            int si = 0, n = 0;
            for (float target = every * 0.5f; target < t.Length - 10f; target += every)
            {
                while (si + 1 < t.Samples.Count && t.Samples[si].s < target) si++;
                var (p, dir, _) = t.Samples[si];
                var nn = new float2(-dir.y, dir.x) * ((n & 1) == 0 ? 1f : -1f);
                var q = p + nn * (t.Hw + 1f);
                uint v = Hashing.Mix(ctx.Seed, 0x3A7Eu + (uint)n);
                if (LeisureKit.Put(ctx, t.Occ, q, LeisureKit.Yaw(q, p), ObjectRole.Furniture, AssetMix.Pick(ids, v), cat, 0.4f, v, 0.5f, 0.2f)) n++;
            }
            ctx.Plan.Facts["waymarkers"] = n;
        }

        // ───────────── nature ─────────────

        /// <summary>
        /// Open glades beside the trail kept free of planting: the player's count plus one for each
        /// feature that needs room (camp, picnic). Largest first.
        /// </summary>
        private static List<(float2 c, float r)> Clearings(Trail t)
        {
            var ctx = t.Ctx;
            var result = new List<(float2 c, float r)>();
            int wanted = ctx.I("clearings");
            bool camp = ctx.Input.HasChild(TrailCampAddOn.LayerId), picnic = ctx.Input.HasChild("picnic");
            int extra = (camp ? 1 : 0) + (picnic ? 1 : 0);
            var avoid = new List<float2>();
            var bench = Bench(ctx);
            for (int i = 0; i < wanted + extra; i++)
            {
                // Feature glades first and big enough for their contents (a camp of 8 pitches needs ~13 m).
                float r = i < extra ? 13.5f : 9f + 5f * Hashing.Unit(Hashing.Mix(ctx.Seed, 0x61ADu + (uint)i));
                if (!Spots.Find(t.Occ, r, Spots.Preference.NearPaths, Hashing.Mix(ctx.Seed, 0x61A0u + (uint)i), out var c, avoid, r * 2.5f, 2f, 1500))
                {
                    r *= 0.75f;
                    if (!Spots.Find(t.Occ, r, Spots.Preference.Open, Hashing.Mix(ctx.Seed, 0x61B0u + (uint)i), out c, avoid, r * 2f, 2f, 1500)) break;
                }
                avoid.Add(c);
                result.Add((c, r));
                // Player glades get a bench at the edge facing in; feature glades stay empty for the feature.
                if (i >= extra)
                {
                    var toPath = t.Occ.Paths.Nearest(c, 300f, out var q) >= 0 ? q : t.Region.Centroid;
                    var b = c + math.normalizesafe(toPath - c) * (r * 0.55f);
                    ctx.Object(b, LeisureKit.Yaw(b, c), ObjectRole.Bench, bench, Hashing.Mix(ctx.Seed, 0x61C0u + (uint)i));
                }
                t.Occ.Take(c, r, 200);
            }
            result.Sort((a, b) => b.r.CompareTo(a.r));
            ctx.Plan.Facts["clearings"] = result.Count;
            return result;
        }

        private static void Rocks(Trail t, string landscape)
        {
            var ctx = t.Ctx;
            int clusters = ctx.I("rocks");
            if (clusters <= 0 || t.Samples.Count == 0) return;
            if (!AssetAvailability.Require(ctx.Plan, AssetCategories.Rock, null, Keys.Category(AssetCategories.Rock))) return;
            for (int c = 0; c < clusters; c++)
            {
                uint h = Hashing.Mix(ctx.Seed, 0x40C7u + (uint)c);
                var (p, dir, _) = t.Samples[(int)(Hashing.Unit(h) * (t.Samples.Count - 1))];
                float side = (h >> 5 & 1) == 0 ? 1f : -1f;
                var nn = new float2(-dir.y, dir.x) * side;
                // Canyons: boulders at the foot of the walls; ridges: outcrops just off the crest.
                var centre = p + nn * (t.Hw + (landscape == "canyon" ? 6f : 4.5f) + Hashing.Unit(h >> 3) * 5f);
                int n = 3 + (int)(h % 5u);
                for (int k = 0; k < n; k++)
                {
                    float a = k * 2.39996f + Hashing.Unit(h >> 7) * 6.283f;
                    var q = centre + new float2(math.cos(a), math.sin(a)) * (1.2f + k * 0.9f);
                    uint v = Hashing.Mix(h, (uint)k);
                    float scale = 0.7f + 0.8f * Hashing.Unit(v);
                    if (!t.Occ.Free(q, 1.1f * scale, 0.8f, 0.3f)) continue;
                    var o = ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.One(""), v, -1, AssetCategories.Rock);
                    o.Scale = scale;
                    t.Occ.Take(q, 1.1f * scale, 2);
                }
            }
        }

        private static void Planting(Trail t, string landscape)
        {
            var ctx = t.Ctx; var region = t.Region;
            float ha = region.Area / 10000f;
            float clear = ctx.F("trailClear");
            var water = ctx.Site.WaterDepthAt;
            System.Func<float2, bool> reject = p =>
            {
                if (water != null && water(p) > 0.05f) return true;
                if (t.Occ.Paths.Clearance(p, clear + t.Hw + 2f) < clear + t.Hw) return true;
                float rank = t.Grid.Rank(ctx.H(p));
                switch (landscape)
                {
                    case "ridge": return rank > 0.78f;          // open crest, wooded flanks
                    case "canyon": return rank > 0.85f && Hashing.Unit(Hashing.Mix(ctx.Seed, (int)p.x, (int)p.y)) < 0.7f;
                    default: return false;
                }
            };
            var treeIds = ctx.L("treeSpecies"); var bushIds = ctx.L("bushSpecies");
            if (treeIds.Length == 0 && AssetAvailability.Has(AssetCategories.Tree)) treeIds = Trees(landscape);
            if (bushIds.Length == 0 && AssetAvailability.Has(AssetCategories.Bush)) bushIds = Bushes(landscape);
            int trees = math.min(12000, (int)(ha * ctx.F("treeDensity")));
            int bushes = math.min(12000, (int)(ha * ctx.F("bushDensity")));
            float clump = landscape == "meadow" ? 40f : landscape == "forest" ? 24f : 18f;
            float floor = landscape == "meadow" ? 0.15f : landscape == "forest" ? 0.5f : 0.35f;
            // (nature makeover) groves, not rows: the even scatter stopped early along the candidates' diagonal bands
            if (trees > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Tree, treeIds, Keys.Category(AssetCategories.Tree)))
                NatureKit.Plant(ctx, t.Occ, region, trees, ObjectRole.Tree, treeIds, 2.1f, clump * 1.3f, 0x7AEEu, reject, floor * 0.4f, landscape == "forest" ? 0.03f : 0f);
            ctx.Check();
            if (bushes > 0 && AssetAvailability.Require(ctx.Plan, AssetCategories.Bush, bushIds, Keys.Category(AssetCategories.Bush)))
                NatureKit.Plant(ctx, t.Occ, region, bushes, ObjectRole.Bush, bushIds, 0.9f, landscape == "lakeshore" ? 12f : 16f, 0x7AEBu,
                    p => (water != null && water(p) > 0.05f) || t.Occ.Paths.Clearance(p, t.Hw + 3f) < t.Hw + 1.2f, 0.1f);
            ctx.Check();
            // boulders where the ground is steep (the gorge's walls, the ridge's flanks) and fallen trunks in the wood
            NatureKit.Rocks(ctx, t.Occ, region, math.clamp((int)math.round(ha * (landscape == "canyon" || landscape == "ridge" ? 2.5f : 0.8f)), 1, 40), 0x40C2u, p =>
            {
                if (t.Occ.Paths.Clearance(p, t.Hw + 4f) < t.Hw + 2f) return 0f;
                var e = new float2(3f, 0f); var f = new float2(0f, 3f);
                float slope = math.length(new float2(ctx.H(p + e) - ctx.H(p - e), ctx.H(p + f) - ctx.H(p - f))) / 6f;
                return (landscape == "canyon" || landscape == "ridge" ? 0.45f : 0.15f) + math.saturate(slope * 3f);
            });
            if (landscape == "forest" || landscape == "canyon") NatureKit.Fallen(ctx, t.Occ, region, (int)(ha * 3f), 0x1066u, p => t.Occ.Paths.Clearance(p, t.Hw + 4f) >= t.Hw + 3f);
        }

        /// <summary>Worn tread painted along the trail (permanent terrain paint), one strip per straight-ish piece.</summary>
        private static void WornTrail(Trail t)
        {
            var ctx = t.Ctx;
            int layer = ctx.I("paintLayer"), n = 0;
            foreach (var net in t.Nets)
            {
                if (!net.FollowTerrain || n >= 150) continue;
                var a = net.A.xz; var d = net.D.xz;
                var axis = math.normalizesafe(d - a);
                if (math.lengthsq(axis) < 0.5f) continue;
                var r = LayerRegion.Rect(math.lerp(a, d, 0.5f), axis, math.distance(a, d) * 0.5f + 0.5f, t.Hw + 0.8f);
                bool inside = true;
                foreach (var q in r) if (!t.Region.Contains(q)) { inside = false; break; }
                if (inside && TerrainPaint.Patch(ctx, r, layer, 0.8f, 0.5f)) n++;
            }
        }
    }
}

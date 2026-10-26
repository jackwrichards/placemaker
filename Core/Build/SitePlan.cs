// Placemaker — the site plan: the plan drawn as what gets built (sketch study, docs/sketch-study).
//
// The user (25 Oct): the sketch "doesn't really tell me what the build is going to look like", the legend doesn't line up
// with what is drawn. One flat list of what to draw, in world metres, read by both the map overlay
// (Game/Overlay/PlacemakerOverlaySystem.SitePlan.cs) and the panel's top view (UI SitePlanCard, binding "sitePlan"), so the
// map, the picture and the legend can never disagree. ParkingLotTool's lessons: fill what is built (one tone per material,
// no outlines), no road bands over the asphalt (roads are their own surface at their real width), stalls as the painted
// lines open to the aisle, every size in metres.
// Pure: Core + Unity.Mathematics only; tested by SitePlanTests.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public sealed partial class SitePlan
    {
        // ── Row ids (the legend; hover / hide use PreviewLegend.Visible with these ids) ──
        public const string Roads = "roads";
        public const string Parking = PreviewLegend.Parking;
        public const string Paths = PreviewLegend.Paths;
        public const string Ground = PreviewLegend.Surfaces;
        public const string Water = PreviewLegend.Water;
        public const string Plots = PreviewLegend.Cutouts;
        public const string Trees = PreviewLegend.Trees;
        public const string Planting = PreviewLegend.Planting;
        public const string Places = PreviewLegend.Features;
        public const string Furniture = PreviewLegend.Objects;
        public const string Entrances = PreviewLegend.Entrances;
        public const string Issues = PreviewLegend.Issues;
        /// <summary>The ground change (the terrain heat map: red raised, blue lowered).</summary>
        public const string Terrain = PreviewLegend.Terrain;

        /// <summary>Fill materials, drawn in this order (later on top).</summary>
        /// <summary>The ground paints (the game's terrain textures: dry grass, lush grass, dirt, sand) come first, under the surfaces.</summary>
        public static readonly string[] Materials = { "paint:dry", "paint:lush", "paint:dirt", "paint:sand", "ground", "grass", "deco", "paving", "plaza", "asphalt", "water" };

        // ── The palette (one place: the overlay, the panel picture and its legend swatches all use it) ──
        public static class Palette
        {
            public const string Asphalt = "#474b52", RoadMark = "#f3f1e8", Path = "#eadfc2", Grass = "#8fd16a", Paving = "#e6d8b8",
                Plaza = "#efd9a8", Water = "#3f86d8", Deco = "#e3c99a", GroundSurf = "#b39a6c", Tree = "#3b7533", TreeHi = "#5a9a44",
                Shadow = "#08140a", Bush = "#5f9e48", Place = "#f0a868", PlaceEdge = "#a95f24", Bench = "#7a5a3a", Lamp = "#fff3c4",
                Bin = "#26272a", Stall = "#ffffff", Charger = "#3fd28a", Fence = "#6b5440", Outline = "#d6ecff", Accent = "#4fa3ff",
                Issue = "#e5322d", Street = "#8d9196", Kerb = "#c9ccd0", Land = "#6f8a4c";

            /// <summary>The ground of the whole site under everything else (the user, 25 Oct: the blue selection haze read as "blue
            /// grass"): a soft lawn tone, so open land in the build reads as ground, not as a selection.</summary>
            public const string Base = "#9cc76f";
            public const float BaseAlpha = 0.28f;

            /// <summary>Opacity of a fill material (one tone each, quiet enough for the ground to read through grass).</summary>
            public static float Alpha(string material)
            {
                switch (material)
                {
                    case "asphalt": return 0.88f;
                    case "grass": return 0.30f;
                    case "paving": return 0.75f;
                    case "plaza": return 0.8f;
                    case "water": return 0.8f;
                    case "deco": return 0.7f;
                    case "paint:dry": case "paint:lush": return 0.6f;
                    case "paint:dirt": case "paint:sand": return 0.85f;
                    default: return 0.3f;
                }
            }

            public static string Fill(string material)
            {
                switch (material)
                {
                    case "asphalt": return Asphalt;
                    case "grass": return Grass;
                    case "paving": return Paving;
                    case "plaza": return Plaza;
                    case "water": return Water;
                    case "deco": return Deco;
                    case "paint:dry": return "#b5a86a";
                    case "paint:lush": return "#4f8f3c";
                    case "paint:dirt": return "#9c7d58";
                    case "paint:sand": return "#e3d2a0";
                    default: return GroundSurf;
                }
            }

            public static string Zone(string zoneId)
            {
                var id = (zoneId ?? "").ToLowerInvariant();
                if (id.Contains("office")) return "#a77fe6";
                if (id.Contains("industr")) return "#e6c64f";
                if (id.Contains("resid") || id.Contains("row") || id.Contains("rent")) return "#5cc25c";
                if (id.Contains("high")) return "#2f6fd0";
                return "#4f8fe6";
            }
        }

        public struct Stroke
        {
            public float2[] Points;
            public float Width;
            /// <summary>A real road (centre marking drawn), not a parking aisle.</summary>
            public bool Marked;
            /// <summary>The net's own curve (the map draws it smooth, one piece per net); null for the city's streets.</summary>
            public float3[] Curve;
        }

        public struct Disc
        {
            public float2 At;
            public float R;
        }

        public struct Badge
        {
            public float2 At;
            /// <summary>Entrances: the direction into the site (radians, atan2(z, x)). Issues: 0.</summary>
            public float Angle;
            public string Kind;
        }

        public sealed class Row
        {
            public string Id = "";
            /// <summary>Swatch shape: road, bays, path, grass, paving, water, zone, tree, bush, place, furniture, entrance, issue.</summary>
            public string Sym = "";
            public string LabelKey = "";
            public string Label = "";
            public double Count;
            /// <summary>"m" (a length), "count", "" (no number).</summary>
            public string Unit = "count";
            /// <summary>Where an annotation for this row goes (inside the outline).</summary>
            public float2 Anchor;
        }

        public float2[] Outline = Array.Empty<float2>();
        public float2 Min, Max;
        public double AreaM2;
        /// <summary>A point well inside the outline for its label (pole of inaccessibility: right for a T, a C or a ring).</summary>
        public float2 Centre;
        public readonly List<(string material, float2[] ring)> Fills = new List<(string, float2[])>();
        public readonly List<Stroke> Cars = new List<Stroke>();
        public readonly List<Stroke> Walks = new List<Stroke>();
        public readonly List<Disc> Caps = new List<Disc>();
        public readonly List<float2[]> Fences = new List<float2[]>();
        public readonly List<(float2 a, float2 b)> Stalls = new List<(float2, float2)>();
        public readonly List<float2> Chargers = new List<float2>();
        public readonly List<Disc> TreeCrowns = new List<Disc>();
        public readonly List<Disc> Bushes = new List<Disc>();
        public readonly List<(string zone, float2[] quad)> PlotCells = new List<(string, float2[])>();
        public readonly List<Badge> EntranceBadges = new List<Badge>();
        public readonly List<Badge> IssueBadges = new List<Badge>();
        /// <summary>The city's own streets near the build (context in the panel picture).</summary>
        public readonly List<Stroke> Streets = new List<Stroke>();
        public readonly List<Row> Legend = new List<Row>();
        /// <summary>The ground change in bands (Core TerrainHeat: band &gt; 0 raised, &lt; 0 lowered), clipped to the outline.</summary>
        public readonly List<(int band, float2[] ring)> Heat = new List<(int, float2[])>();

        // Budgets for the panel picture (the overlay draws everything): far more than a panel can show.
        public const int MaxTrees = 4000, MaxBushes = 3000, MaxStalls = 12000, MaxSmall = 3000, MaxHeat = 2000, MaxProps = 8000, MaxPaint = 400;
        public const float MaxStallSpan = 320f;

        public sealed class Input
        {
            public ComposedPlan Plan;
            public float2[] Outline;
            /// <summary>Asset metadata (net widths, object bounds); null = the role defaults.</summary>
            public IReadOnlyDictionary<string, AssetInfo> Assets;
            /// <summary>Placed entrances (markers are not part of the plan).</summary>
            public IReadOnlyList<float2> Entrances;
        }

        public static SitePlan Compute(Input input)
        {
            var sp = new SitePlan();
            var plan = input?.Plan;
            var outline = input?.Outline;
            if (outline == null || outline.Length < 3) return sp;
            sp.Outline = outline;
            Poly.Bounds(outline, out sp.Min, out sp.Max);
            sp.AreaM2 = Poly.Area(outline);
            try { sp.Centre = Poly.PoleOfInaccessibility(outline, out _); }
            catch (Exception) { sp.Centre = Poly.Centroid(outline); }
            if (plan == null) return sp;
            var assets = input.Assets;

            double roadLen = 0, walkLen = 0;
            var anchors = new Dictionary<string, List<float2>>(StringComparer.Ordinal);
            void Anchor(string id, float2 p)
            {
                if (!anchors.TryGetValue(id, out var l)) anchors[id] = l = new List<float2>();
                if (l.Count < 400) l.Add(p);
            }
            var aisles = new List<float2[]>();
            int paintRings = 0;
            var bayRects = new List<float2[]>();
            int bays = 0;
            bool grass = false, paved = false, water = false;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                // ground paint (the beach's sand, a yard's dirt): its outline in the texture's colour, strong paints only
                foreach (var pt in l.Paint)
                {
                    if (pt?.Ring == null || pt.Ring.Length < 3 || pt.Strength < 0.4f || pt.Layer < 1 || pt.Layer > 4 || paintRings++ >= MaxPaint) continue;
                    string pm = pt.Layer == 1 ? "paint:dry" : pt.Layer == 2 ? "paint:lush" : pt.Layer == 3 ? "paint:dirt" : "paint:sand";
                    sp.Fills.Add((pm, Poly.To2D(pt.Ring)));
                    if (pt.Layer >= 3) paved = true;
                }
                foreach (var s in l.Surfaces)
                {
                    if (s?.Ring == null || s.Ring.Length < 3) continue;
                    string m = MaterialOf(s.Role);
                    sp.Fills.Add((m, Poly.To2D(s.Ring)));
                    if (m == "grass") grass = true; else if (m == "water") water = true; else if (m != "asphalt" && m != "ground") paved = true;
                    if (m != "asphalt") Anchor(m == "water" ? Water : Ground, Poly.Centroid(Poly.To2D(s.Ring)));
                }
                foreach (var n in l.Nets)
                {
                    if (n == null) continue;
                    var pts = Flatten(n);
                    if (n.Role == NetRole.Fence) { sp.Fences.Add(pts); continue; }
                    float hw = HalfWidth(n, assets);
                    string layer = PreviewLegend.Of(n, l.GeneratorId);
                    bool walk = layer == PreviewLegend.Paths || layer == PreviewLegend.Road(RoadRoles.CarFree);
                    double len = Length(pts);
                    if (walk)
                    {
                        sp.Walks.Add(new Stroke { Points = pts, Width = math.max(1.2f, hw * 2f), Curve = new[] { n.A, n.B, n.C, n.D } });
                        walkLen += len;
                        Anchor(Paths, pts[pts.Length / 2]);
                        continue;
                    }
                    bool road = PreviewLegend.IsRoad(layer);
                    sp.Cars.Add(new Stroke { Points = pts, Width = hw * 2f, Marked = road && hw >= 3f, Curve = new[] { n.A, n.B, n.C, n.D } });
                    if (road) { roadLen += len; Anchor(Roads, pts[pts.Length / 2]); }
                    else aisles.Add(pts);
                }
                foreach (var o in l.Objects)
                {
                    if (o == null) continue;
                    var p = o.Position.xz;
                    switch (o.Role)
                    {
                        case ObjectRole.Tree:
                            sp.TreeCrowns.Add(new Disc { At = p, R = Crown(o, assets) });
                            Anchor(Trees, p);
                            continue;
                        case ObjectRole.ParkingBay:
                            bays++;
                            Anchor(Parking, p);
                            bayRects.Add(Footprint(o, assets));
                            continue;
                        case ObjectRole.Charger: sp.Chargers.Add(p); continue;
                        case ObjectRole.CulDeSacCap: case ObjectRole.RoundaboutIsland:
                            sp.Caps.Add(new Disc { At = p, R = o.Role == ObjectRole.CulDeSacCap ? 10f : math.max(6f, Radius(o, assets)) });
                            continue;
                        case ObjectRole.Decal: case ObjectRole.Arrow: case ObjectRole.Marker: case ObjectRole.AccessPoint: case ObjectRole.AnimalSpawner:
                            continue;
                    }
                    string lay = PreviewLegend.Of(o);
                    string ocat = RoleCategories.ForObject(o) ?? "";
                    if (ocat.StartsWith(AssetCategories.Hedge, StringComparison.Ordinal))
                    {
                        // a hedge is a dark green bar along its piece
                        sp.Props.Add(new Prop { Poly = Footprint(o, assets), Color = "#3f7a35", Row = Planting, Height = 1.5f });
                        Anchor(Planting, p);
                    }
                    else if (lay == PreviewLegend.Planting)
                    {
                        sp.Bushes.Add(new Disc { At = p, R = math.clamp(Radius(o, assets), 0.5f, 1.6f) });
                        Anchor(Planting, p);
                    }
                    else if (ToProp(o, assets) is Prop prop)
                    {
                        sp.Props.Add(prop);
                        Anchor(prop.Row, p);
                    }
                }
            }
            // Stalls once every aisle is known (an aisle grid: a 3,573-space lot asked every aisle for every space)
            if (bayRects.Count > 0)
            {
                var grid = new AisleGrid(aisles);
                // a separator is shared by two neighbours: drawn once (half the lines on a big lot)
                var seen = new HashSet<(long, long)>();
                long K(float2 p) => ((long)math.round(p.x * 5f) << 32) ^ (uint)(int)math.round(p.y * 5f);
                foreach (var r in bayRects)
                    foreach (var seg in BayLines(r, grid))
                    {
                        long ka = K(seg.a), kb = K(seg.b);
                        if (seen.Add(ka < kb ? (ka, kb) : (kb, ka))) sp.Stalls.Add(seg);
                    }
            }

            // Shop plots: the zone cells the build zones (else the planned quads).
            if (plan.ZoneCells != null && plan.ZoneCells.Count > 0)
            {
                foreach (var c in plan.ZoneCells)
                    if (c.State == ZoneCellState.Zoned)
                    {
                        var q = new[] { c.A, c.B, c.C, c.D };
                        sp.PlotCells.Add((c.ZoneId ?? "", q));
                        Anchor(Plots, (c.A + c.C) * 0.5f);
                    }
            }
            else
                foreach (var l in plan.Layers)
                    if (l != null)
                        foreach (var z in l.Zoning)
                            if (z != null) { sp.PlotCells.Add((z.ZoneId ?? "", new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz })); Anchor(Plots, (z.A.xz + z.C.xz) * 0.5f); }

            var centre = sp.Centre;
            if (input.Entrances != null)
                foreach (var e in input.Entrances)
                {
                    var dir = centre - e;
                    sp.EntranceBadges.Add(new Badge { At = e, Angle = math.atan2(dir.y, dir.x), Kind = "entrance" });
                    Anchor(Entrances, e);
                }
            int red = 0;
            foreach (var i in plan.AllIssues())
            {
                if (i?.At == null || !(i.Dropped || i.Severity == IssueSeverity.Error)) continue;
                sp.IssueBadges.Add(new Badge { At = i.At.Value.xz, Kind = "issue" });
                Anchor(Issues, i.At.Value.xz);
                red++;
            }
            if (plan.ExistingNets != null)
            {
                float2 lo = sp.Min - 80f, hi = sp.Max + 80f;
                foreach (var r in plan.ExistingNets)
                {
                    if (r.Category != NetCategory.Road && r.Category != NetCategory.Pathway) continue;
                    var pts = Flatten(r.A, r.B, r.C, r.D);
                    bool near = false;
                    foreach (var q in pts) if (q.x >= lo.x && q.x <= hi.x && q.y >= lo.y && q.y <= hi.y) { near = true; break; }
                    if (near) sp.Streets.Add(new Stroke { Points = pts, Width = math.max(2f, r.Width), Marked = r.Category == NetCategory.Road });
                }
            }

            // The legend: one row per thing drawn, in plain words, with the number that matters.
            void Row(string id, string sym, string key, string label, double count, string unit)
            {
                sp.Legend.Add(new Row { Id = id, Sym = sym, LabelKey = key, Label = label, Count = unit == "delta" ? count : Math.Round(count), Unit = unit, Anchor = AnchorOf(anchors, id, outline, centre) });
            }
            if (roadLen > 0) Row(Roads, "road", "Placemaker.SitePlan.Roads", "Roads", roadLen, "m");
            if (bays > 0) Row(Parking, "bays", "Placemaker.SitePlan.Parking", "Parking", bays, "count");
            else if (aisles.Count > 0) Row(Parking, "bays", "Placemaker.SitePlan.CarPark", "Car park", 0, "");
            if (walkLen > 0) Row(Paths, "path", "Placemaker.SitePlan.Paths", "Footpaths", walkLen, "m");
            if (grass) Row(Ground, "grass", "Placemaker.SitePlan.Lawn", "Lawn", 0, "");
            else if (paved) Row(Ground, "paving", "Placemaker.SitePlan.Paving", "Paving", 0, "");
            if (water) Row(Water, "water", "Placemaker.SitePlan.Water", "Water", 0, "");
            var heat = plan.TerrainHeat;
            if (heat != null && heat.Rings.Count > 0)
            {
                foreach (var hr in heat.Rings) sp.Heat.Add(hr);
                // the number: the biggest change either way, in metres
                Row(Terrain, "terrain", "Placemaker.SitePlan.Terrain", "Terrain", Math.Round(Math.Max(Math.Abs(heat.MinDelta), Math.Abs(heat.MaxDelta)), 1), "delta");
            }
            if (sp.PlotCells.Count > 0) Row(Plots, "zone", "Placemaker.SitePlan.Plots", "Shop plots", sp.PlotCells.Count, "cells");
            if (sp.TreeCrowns.Count > 0) Row(Trees, "tree", "Placemaker.SitePlan.Trees", "Trees", sp.TreeCrowns.Count, "count");
            int hedges = 0;
            foreach (var pr in sp.Props) if (pr.Row == Planting) hedges++;
            if (sp.Bushes.Count + hedges > 0) Row(Planting, "bush", "Placemaker.SitePlan.Planting", "Planting", sp.Bushes.Count + hedges, "count");
            var perRow = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pr in sp.Props) if (pr.Row != Planting) { perRow.TryGetValue(pr.Row, out int c0); perRow[pr.Row] = c0 + 1; }
            foreach (var (row, sym, key, label) in PropRows)
                if (perRow.TryGetValue(row, out int c1)) Row(row, sym, key, label, c1, "count");
            if (sp.EntranceBadges.Count > 0) Row(Entrances, "entrance", "Placemaker.SitePlan.Entrances", "Entrances", sp.EntranceBadges.Count, "count");
            if (red > 0) Row(Issues, "issue", "Placemaker.SitePlan.Issues", "Problems", red, "count");
            return sp;
        }

        /// <summary>The fill material of a planned surface.</summary>
        public static string MaterialOf(SurfaceRole r)
        {
            switch (r)
            {
                case SurfaceRole.Asphalt: return "asphalt";
                case SurfaceRole.Grass: return "grass";
                case SurfaceRole.Paving: return "paving";
                case SurfaceRole.Plaza: return "plaza";
                case SurfaceRole.Water: return "water";
                case SurfaceRole.Decorative: return "deco";
                default: return "ground";
            }
        }

        /// <summary>The legend row a fill belongs to (the overlay's hover / hide per row).</summary>
        public static string RowOfMaterial(string material) => material == "water" ? Water : material == "asphalt" ? Parking : Ground;

        /// <summary>
        /// Where a row's annotation goes: the member nearest the middle of its members, pulled inside the outline. Rows with
        /// no members (ground) use the outline's label point.
        /// </summary>
        private static float2 AnchorOf(Dictionary<string, List<float2>> anchors, string id, float2[] outline, float2 centre)
        {
            if (!anchors.TryGetValue(id, out var l) || l.Count == 0) return centre;
            float2 mid = float2.zero;
            foreach (var p in l) mid += p;
            mid /= l.Count;
            float2 best = l[0];
            float bd = float.MaxValue;
            foreach (var p in l)
            {
                float d = math.distancesq(p, mid);
                if (d < bd && (id == Entrances || id == Issues || Poly.Contains(outline, p))) { bd = d; best = p; }
            }
            return best;
        }

        // ───────────── geometry ─────────────

        public static float2[] Flatten(PlannedNet n) => Flatten(n.A, n.B, n.C, n.D);

        public static float2[] Flatten(float3 a, float3 b, float3 c, float3 d)
        {
            int seg = math.clamp((int)(math.distance(a.xz, d.xz) / 3f), 1, 24);
            if (math.lengthsq(math.cross(b - a, d - a)) < 1e-3f && math.lengthsq(math.cross(c - a, d - a)) < 1e-3f) seg = 1;
            var pts = new float2[seg + 1];
            for (int i = 0; i <= seg; i++) pts[i] = Composer.Bezier(a, b, c, d, i / (float)seg).xz;
            return pts;
        }

        private static double Length(float2[] p)
        {
            double s = 0;
            for (int i = 1; i < p.Length; i++) s += math.distance(p[i - 1], p[i]);
            return s;
        }

        public static float HalfWidth(PlannedNet n, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            if (assets != null && !string.IsNullOrEmpty(n.Asset.Id) && assets.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.5f) return info.NetWidth * 0.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        private static bool TryInfo(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets, out AssetInfo info)
        {
            info = default;
            return assets != null && !string.IsNullOrEmpty(o.Asset.Id) && assets.TryGetValue(o.Asset.Id, out info) && math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) > 0.05f;
        }

        /// <summary>
        /// Crown radius: bounds near the trunk, so the height counts too (0.28 × height), 1..7 m × scale (as the sketch), and
        /// never less than a tree of its growth stage shows in the game (the first site plan drew the car parks' adult trees
        /// half their built size).
        /// </summary>
        public static float Crown(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            float s = o.Scale > 0.05f ? o.Scale : 1f;
            float floor = o.Age == PlannedAge.Young ? 1.8f : o.Age == PlannedAge.Teen ? 2.6f : o.Age == PlannedAge.Dead ? 1.5f : 3.4f;
            if (TryInfo(o, assets, out var info))
            {
                float r = math.cmax((info.BoundsMax - info.BoundsMin).xz) * 0.5f, h = info.BoundsMax.y - info.BoundsMin.y;
                return math.clamp(math.max(math.max(r, h * 0.28f), floor), 1f, 7f) * s;
            }
            return math.max(Composer.DefaultRadius(o.Role) * 1.4f, floor) * s;
        }

        private static float Radius(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            float s = o.Scale > 0.05f ? o.Scale : 1f;
            return TryInfo(o, assets, out var info) ? math.cmax(info.BoundsMax.xz - info.BoundsMin.xz) * 0.5f * s : Composer.DefaultRadius(o.Role) * s;
        }

        /// <summary>The object's footprint: its bounds × scale, turned by its yaw (corners in order).</summary>
        public static float2[] Footprint(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            float s = o.Scale > 0.05f ? o.Scale : 1f;
            float2 bmin, bmax;
            if (TryInfo(o, assets, out var info)) { bmin = info.BoundsMin.xz * s; bmax = info.BoundsMax.xz * s; }
            else if (o.Role == ObjectRole.ParkingBay) { bmin = new float2(-1.25f, -2.6f); bmax = new float2(1.25f, 2.6f); }
            else { float r = Composer.DefaultRadius(o.Role) * s; bmin = new float2(-r); bmax = new float2(r); }
            float c = math.cos(o.Yaw), sn = math.sin(o.Yaw);
            float2 p = o.Position.xz;
            float2 Rot(float2 v) => new float2(v.x * c + v.y * sn, -v.x * sn + v.y * c) + p;
            return new[] { Rot(bmin), Rot(new float2(bmax.x, bmin.y)), Rot(bmax), Rot(new float2(bmin.x, bmax.y)) };
        }

        /// <summary>
        /// A stall as painted: the two long sides (the separators shared with the neighbours) and the back line; the side to the
        /// nearest aisle stays open. No aisle known: both short sides are left out.
        /// </summary>
        public static IEnumerable<(float2 a, float2 b)> BayLines(float2[] p, List<float2[]> aisles) => BayLines(p, new AisleGrid(aisles));

        public static IEnumerable<(float2 a, float2 b)> BayLines(float2[] p, AisleGrid aisles)
        {
            if (p == null || p.Length != 4) yield break;
            bool long01 = math.distance(p[0], p[1]) >= math.distance(p[1], p[2]);
            if (long01) { yield return (p[0], p[1]); yield return (p[3], p[2]); }
            else { yield return (p[1], p[2]); yield return (p[0], p[3]); }
            if (aisles == null || aisles.Empty) yield break;
            var s0 = long01 ? (p[1], p[2]) : (p[0], p[1]);
            var s1 = long01 ? (p[3], p[0]) : (p[2], p[3]);
            float d0 = aisles.Nearest((s0.Item1 + s0.Item2) * 0.5f), d1 = aisles.Nearest((s1.Item1 + s1.Item2) * 0.5f);
            yield return d0 > d1 ? s0 : s1;
        }

        /// <summary>Aisle segments bucketed in 16 m cells: the nearest aisle to a stall end looks at its own and the next cells only.</summary>
        public sealed class AisleGrid
        {
            private const float Cell = 16f;
            private readonly Dictionary<long, List<(float2 a, float2 b)>> _cells = new Dictionary<long, List<(float2, float2)>>();
            public bool Empty => _cells.Count == 0;
            private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

            public AisleGrid(List<float2[]> aisles)
            {
                if (aisles == null) return;
                foreach (var a in aisles)
                    for (int i = 1; i < a.Length; i++)
                    {
                        float2 lo = math.min(a[i - 1], a[i]), hi = math.max(a[i - 1], a[i]);
                        int2 c0 = (int2)math.floor(lo / Cell), c1 = (int2)math.floor(hi / Cell);
                        for (int x = c0.x; x <= c1.x; x++)
                            for (int z = c0.y; z <= c1.y; z++)
                            {
                                if (!_cells.TryGetValue(Key(x, z), out var l)) _cells[Key(x, z)] = l = new List<(float2, float2)>();
                                l.Add((a[i - 1], a[i]));
                            }
                    }
            }

            /// <summary>Distance to the nearest aisle within about 32 m (farther counts as far).</summary>
            public float Nearest(float2 m)
            {
                float best = 1e6f;
                int2 c = (int2)math.floor(m / Cell);
                for (int x = c.x - 1; x <= c.x + 1; x++)
                    for (int z = c.y - 1; z <= c.y + 1; z++)
                    {
                        if (!_cells.TryGetValue(Key(x, z), out var l)) continue;
                        foreach (var (a, b) in l)
                        {
                            float2 d = b - a;
                            float len = math.lengthsq(d);
                            float t = len < 1e-9f ? 0f : math.saturate(math.dot(m - a, d) / len);
                            best = math.min(best, math.distance(m, a + d * t));
                        }
                    }
                return best;
            }
        }

        // ───────────── the panel picture (binding "sitePlan") ─────────────

        /// <summary>
        /// Compact JSON for the panel's top view: numbers rounded to 0.1 m, point lists flattened ([x, z, x, z, …]), clutter
        /// thinned to the budgets above (the map draws all of it). <paramref name="stamp"/> changes with every new plan.
        /// </summary>
        public JsonValue ToUi(string stamp)
        {
            var o = JsonValue.Obj();
            o.Set("stamp", stamp ?? "");
            o.Set("min", Pt(Min));
            o.Set("max", Pt(Max));
            o.Set("areaM2", Math.Round(AreaM2));
            o.Set("centre", Pt(Centre));
            o.Set("outline", Flat(Outline));
            var fills = JsonValue.Arr();
            foreach (var m in Materials)
            {
                var rings = JsonValue.Arr();
                foreach (var f in Fills) if (f.material == m) rings.Add(Flat(f.ring));
                if (rings.Count == 0) continue;
                fills.Add(JsonValue.Obj().Set("m", m).Set("color", Palette.Fill(m)).Set("alpha", Math.Round(Palette.Alpha(m), 2)).Set("row", RowOfMaterial(m)).Set("rings", rings));
            }
            o.Set("fills", fills);
            o.Set("cars", Strokes(Cars, true));
            o.Set("walks", Strokes(Walks, false));
            o.Set("streets", Strokes(Streets, true));
            o.Set("caps", Discs(Caps, int.MaxValue));
            var fences = JsonValue.Arr();
            foreach (var f in Fences) fences.Add(Flat(f));
            o.Set("fences", fences);
            var stalls = JsonValue.Arr();
            int n = 0;
            // a stall is under a pixel in the panel beyond about 320 m: not sent (the map still draws every one)
            bool small = math.cmax(Max - Min) <= MaxStallSpan;
            foreach (var (a, b) in small ? Stalls : new List<(float2, float2)>())
            {
                if (n++ >= MaxStalls) break;
                stalls.Add(R(a.x)).Add(R(a.y)).Add(R(b.x)).Add(R(b.y));
            }
            o.Set("stalls", stalls);
            o.Set("chargers", Points(Chargers, MaxSmall));
            // props grouped by row and colour: { row, color, rings: [[x, z, …]], discs: [x, z, r, …] }; shadows: rings of the tall ones
            var groups = new Dictionary<(string, string), (JsonValue rings, JsonValue discs)>();
            var shadows = JsonValue.Arr();
            int pn = 0;
            foreach (var pr in Props)
            {
                if (pn++ >= MaxProps) break;
                if (!groups.TryGetValue((pr.Row, pr.Color), out var g)) groups[(pr.Row, pr.Color)] = g = (JsonValue.Arr(), JsonValue.Arr());
                if (pr.Round) g.discs.Add(R(pr.At.x)).Add(R(pr.At.y)).Add(R(pr.R));
                else g.rings.Add(Flat(pr.Poly));
                if (pr.Height >= 1.5f && !pr.Round)
                {
                    var off = ShadowOffset(pr.Height);
                    var sh = new float2[pr.Poly.Length];
                    for (int i = 0; i < sh.Length; i++) sh[i] = pr.Poly[i] + off;
                    shadows.Add(Flat(sh));
                }
            }
            var props = JsonValue.Arr();
            foreach (var kv in groups)
                props.Add(JsonValue.Obj().Set("row", kv.Key.Item1).Set("color", kv.Key.Item2).Set("rings", kv.Value.rings).Set("discs", kv.Value.discs));
            o.Set("props", props);
            o.Set("propShadows", shadows);
            o.Set("trees", Discs(TreeCrowns, MaxTrees));
            o.Set("bushes", Discs(Bushes, MaxBushes));
            var plots = JsonValue.Arr();
            foreach (var (zone, quad) in PlotCells) plots.Add(JsonValue.Obj().Set("color", Palette.Zone(zone)).Set("q", Flat(quad)));
            o.Set("plots", plots);
            var heatJ = JsonValue.Arr();
            for (int band = -TerrainHeat.MaxBand; band <= TerrainHeat.MaxBand; band++)
            {
                if (band == 0) continue;
                var rings = JsonValue.Arr();
                int hn = 0;
                foreach (var (b, ring) in Heat) if (b == band && hn++ < MaxHeat) rings.Add(Flat(ring));
                if (rings.Count == 0) continue;
                var (hex, alpha) = TerrainHeat.Style(band);
                heatJ.Add(JsonValue.Obj().Set("band", band).Set("color", hex).Set("alpha", Math.Round(Math.Min(1.0, alpha * 1.6), 2)).Set("rings", rings));
            }
            o.Set("heat", heatJ);
            o.Set("entrances", Badges(EntranceBadges));
            o.Set("issues", Badges(IssueBadges));
            var legend = JsonValue.Arr();
            foreach (var r in Legend)
                legend.Add(JsonValue.Obj().Set("id", r.Id).Set("sym", r.Sym).Set("label", JsonValue.Obj().Set("key", r.LabelKey)).Set("en", r.Label)
                    .Set("count", r.Count).Set("unit", r.Unit).Set("anchor", Pt(r.Anchor)));
            o.Set("legend", legend);
            return o;
        }

        private static double R(float v) => Math.Round(v, 1);
        private static JsonValue Pt(float2 p) => JsonValue.Arr().Add(R(p.x)).Add(R(p.y));

        private static JsonValue Flat(float2[] pts)
        {
            var a = JsonValue.Arr();
            if (pts != null) foreach (var p in pts) a.Add(R(p.x)).Add(R(p.y));
            return a;
        }

        private static JsonValue Strokes(List<Stroke> list, bool marks)
        {
            var a = JsonValue.Arr();
            foreach (var s in list)
            {
                var o = JsonValue.Obj().Set("w", R(s.Width)).Set("p", Flat(s.Points));
                if (marks && s.Marked) o.Set("mark", true);
                a.Add(o);
            }
            return a;
        }

        /// <summary>[x, z, r, …], every k-th when over the budget (an even thinning, not the first n).</summary>
        private static JsonValue Discs(List<Disc> list, int budget)
        {
            var a = JsonValue.Arr();
            int stride = list.Count > budget ? (int)Math.Ceiling(list.Count / (double)budget) : 1;
            for (int i = 0; i < list.Count; i += stride) a.Add(R(list[i].At.x)).Add(R(list[i].At.y)).Add(R(list[i].R));
            return a;
        }

        private static JsonValue Points(List<float2> list, int budget)
        {
            var a = JsonValue.Arr();
            int stride = list.Count > budget ? (int)Math.Ceiling(list.Count / (double)budget) : 1;
            for (int i = 0; i < list.Count; i += stride) a.Add(R(list[i].x)).Add(R(list[i].y));
            return a;
        }

        private static JsonValue Badges(List<Badge> list)
        {
            var a = JsonValue.Arr();
            foreach (var b in list) a.Add(R(b.At.x)).Add(R(b.At.y)).Add(Math.Round(b.Angle, 2));
            return a;
        }
    }
}

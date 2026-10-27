// AI builder (docs/AI-BUILDER.md): the problems a placement causes, in words the AI can act on. Every placing tool answers
// with these at once, and the check tool runs them over the whole plan. Pure: the site (outline, roads, buildings) and the
// asset sizes are a snapshot taken when the session started.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Ai
{
    /// <summary>One asset the AI can use: its prefab name and real size.</summary>
    public sealed class AiAsset
    {
        public string Id;
        public string Name;
        public string Category;
        /// <summary>The game's own menu group the asset is listed in (e.g. "PropsPark", "SignsAndPosters"), "" = none.</summary>
        public string Group = "";
        /// <summary>The prefab's own icon url (coui:// or assetdb://), "" = none: one of the sources of its picture.</summary>
        public string Icon = "";
        /// <summary>Set by the game layer when the index is made: a picture can be shown (look_at_assets).</summary>
        public bool HasPicture = true;
        /// <summary>A line asset (a path, a fence net): placed with place_path, not place_objects.</summary>
        public bool Line;
        /// <summary>Rank among the mod's own automatic choices for its category (0 = the default; -1 = not one of them):
        /// what the premade types use, vanilla first. Search lists these first.</summary>
        public int Pick = -1;
        public string[] Tags = Array.Empty<string>();
        /// <summary>Width (x), depth (z), height (y) in metres, from the object's bounds.</summary>
        public float3 Size;
        /// <summary>Centre of the bounds relative to the asset's origin (x, z).</summary>
        public float2 Offset;
        public bool Vanilla;

        public float Radius => math.max(0.3f, 0.5f * math.cmax(Size.xz));

        public bool Plant => Category == AssetCategories.Tree || Category == AssetCategories.Bush || Category == AssetCategories.Hedge;

        /// <summary>How far it reaches sideways: a tree's crown (its bounds hold only the trunk; as the site plan sketch
        /// sizes it, SitePlan.Crown), else its footprint.</summary>
        public float Reach => Category == AssetCategories.Tree ? math.clamp(math.max(math.max(Radius, Size.y * 0.28f), 3.4f), 1f, 7f)
            : Category == AssetCategories.Bush || Category == AssetCategories.Hedge ? math.clamp(math.max(Radius, Size.y * 0.5f), 0.5f, 3f) : Radius;
    }

    public sealed class AiSite
    {
        public float2[] Ring;
        public float2 Origin;   // the brief's local origin (world)
        public List<NearbyNet> Nets = new List<NearbyNet>();
        public List<Obstacle> Buildings = new List<Obstacle>();
        public Dictionary<string, AiAsset> Assets = new Dictionary<string, AiAsset>(StringComparer.Ordinal);

        /// <summary>The ground height before anything the AI shaped (world xz -> m above sea level).</summary>
        public Func<float2, float> Ground = _ => 0f;

        /// <summary>Water depth (world xz -> m, 0 = dry) when the session started.</summary>
        public Func<float2, float> Water = _ => 0f;
        /// <summary>The open water's surface (m above sea level), NaN without water.</summary>
        public float WaterLevel = float.NaN;
        /// <summary>The map's sand paint layer (1-4), 0 = none.</summary>
        public int SandLayer;

        /// <summary>A point past the selection that shore work may reach: within 40 m of the outline and on or within 15 m
        /// of water (sand into the water, the beach face, piers and jetties over it).</summary>
        public bool ByWater(float2 p)
        {
            if (Water(p) > 0.05f) return Poly.DistanceToBoundary(Ring, p) < 80f;
            if (Poly.DistanceToBoundary(Ring, p) > 40f) return false;
            for (int k = 0; k < 8; k++)
            {
                var d = new float2(math.cos(k * 0.785f), math.sin(k * 0.785f));
                for (float r = 5f; r <= 15f; r += 5f) if (Water(p + d * r) > 0.05f) return true;
            }
            return false;
        }

        /// <summary>The first finish of a request has been turned into a review (the next one ends it).</summary>
        public bool Reviewed;

        /// <summary>A finish was sent back once for the problems left (the next one ends it).</summary>
        public bool ProblemsShown;

        /// <summary>The detail pass was asked for (after the review, before the end).</summary>
        public bool DetailShown;

        /// <summary>The AI's vision for this site (the vision tool), null until it has written one: the placing tools wait
        /// for it once, and the second look holds the build against it.</summary>
        public string Vision;
        /// <summary>The placing tools sent the AI to write its vision first (once).</summary>
        public bool VisionAsked;

        public float2 World(float2 local) => local + Origin;
        public float2 Local(float2 world) => world - Origin;
    }

    public static class AiChecks
    {
        private static string F(float v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>Distance from p to the net's centre line and the net's half width.</summary>
        private static float NetDistance(NearbyNet n, float2 p)
        {
            float best = float.MaxValue;
            var prev = SiteBrief.Bez(n, 0f).xz;
            for (int k = 1; k <= 16; k++)
            {
                var cur = SiteBrief.Bez(n, k / 16f).xz;
                best = math.min(best, SegDist(p, prev, cur));
                prev = cur;
            }
            return best;
        }

        public static float SegDist(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float t = math.lengthsq(ab) < 1e-6f ? 0f : math.saturate(math.dot(p - a, ab) / math.lengthsq(ab));
            return math.distance(p, a + t * ab);
        }

        private static bool IsFlat(AiPiece p) => p.Role == ObjectRole.Decal;

        public static float RadiusOf(AiSite s, AiPiece p) =>
            s.Assets.TryGetValue(p.Asset ?? "", out var a) ? a.Reach : p.Role == ObjectRole.Tree ? 3.4f : 0.75f;

        /// <summary>The problems of one piece against the site and the other pieces.</summary>
        public static List<string> Problems(AiSite s, AiPiece p, IReadOnlyList<AiPiece> all)
        {
            var list = new List<string>();
            if (p.Kind == AiPieceKind.Object)
            {
                if (!string.IsNullOrEmpty(p.Asset) && !s.Assets.ContainsKey(p.Asset)) list.Add($"unknown asset '{p.Asset}' (search_assets lists the ids)");
                float r = RadiusOf(s, p);
                if (!Poly.Contains(s.Ring, p.Position)) list.Add("outside the selection");
                else
                {
                    float edge = Poly.DistanceToBoundary(s.Ring, p.Position);
                    if (edge < r * (p.Role == ObjectRole.Tree ? 0.5f : 0.8f) && !IsFlat(p)) list.Add($"sticks out of the selection ({F(edge)} m from the edge, radius {F(r)} m)");
                }
                for (int i = 0; i < s.Nets.Count; i++)
                {
                    var n = s.Nets[i];
                    float d = NetDistance(n, p.Position) - n.Width * 0.5f;
                    if (d < r * 0.7f) list.Add($"on or against an existing {(n.Category == NetCategory.Road ? "road" : "path")} ({F(math.max(0f, d))} m from its edge)");
                }
                foreach (var b in s.Buildings)
                    if (Poly.Contains(new[] { b.A, b.B, b.C, b.D }, p.Position)) { list.Add("inside an existing building or lot"); break; }
                if (!IsFlat(p)) { var taste = Taste(s, p, all); if (taste != null) list.Add(taste); }
                if (p.Elevation < 0.5f && UnderWater(s, all, p.Position, out float wd)) list.Add($"under water ({F(wd)} m deep after your ground shaping: move it onto dry ground)");
                if (!IsFlat(p))
                    foreach (var o in all)
                    {
                        if (o.Id == p.Id || IsFlat(o)) continue;
                        if (o.Kind == AiPieceKind.Object)
                        {
                            if (math.abs(o.Elevation - p.Elevation) > 1.5f) continue;
                            float ro = RadiusOf(s, o);
                            float d = math.distance(o.Position, p.Position);
                            // trees and bushes: crowns may touch and overlap a little, not grow into each other
                            bool soft = (p.Role == ObjectRole.Tree || p.Role == ObjectRole.Bush) && (o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush);
                            float need = soft ? (r + ro) * 0.6f : (r + ro) * 0.75f;
                            if (d < need) list.Add($"overlaps #{o.Id} ({o.Asset}) by {F(need - d)} m");
                        }
                        else if (o.Points != null)
                        {
                            for (int k = 0; k + 1 < o.Points.Length; k++)
                            {
                                float d = SegDist(p.Position, o.Points[k], o.Points[k + 1]) - PathHalfWidth(o);
                                if (d < r * 0.7f) { list.Add($"on path #{o.Id} ({F(math.max(0f, d))} m from its edge)"); break; }
                            }
                        }
                    }
            }
            else if (p.Ring != null)
            {
                int outside = p.Ring.Count(q => !Poly.Contains(s.Ring, q) && Poly.DistanceToBoundary(s.Ring, q) > 1f && !s.ByWater(q));
                if (outside > 0) list.Add($"{outside} of its {p.Ring.Length} corners are outside the selection");
                // a surface drawn over the AI's own paths hides them in the game (the paths are built, but under it)
                if (p.Kind == AiPieceKind.Surface)
                    foreach (var o in all)
                        if (HiddenUnder(o, p.Ring) > 4f)
                        { list.Add($"covers path #{o.Id}: a surface hides the paths under it (use paint_ground for a grass base, and keep surfaces to plazas and pads beside the paths)"); break; }
                if (p.Kind == AiPieceKind.Surface)
                    foreach (var o in all)
                        if (o.Id != p.Id && o.Kind == AiPieceKind.Surface && o.Ring != null && SurfacesOverlap(p.Ring, o.Ring))
                        { list.Add($"overlaps surface #{o.Id}: the game shows only one surface where two overlap"); break; }
                if (p.Kind == AiPieceKind.Surface)
                    foreach (var n in s.Nets)
                        if (p.Ring.Any(q => NetDistance(n, q) < n.Width * 0.5f) || Poly.Contains(p.Ring, SiteBrief.Bez(n, 0.5f).xz)) { list.Add($"covers an existing {(n.Category == NetCategory.Road ? "road" : "path")}"); break; }
            }
            else if (p.Points != null)
            {
                foreach (var o in all)
                    if (o.Kind == AiPieceKind.Surface && o.Ring != null && HiddenUnder(p, o.Ring) > 4f)
                    { list.Add($"runs under surface #{o.Id}, which hides it"); break; }
                // an end may reach just past the outline to meet a road there (an entrance)
                bool AtRoad(float2 q) => s.Nets.Any(n => NetDistance(n, q) - n.Width * 0.5f < 4f);
                int outside = p.Points.Where((q, i) => !Poly.Contains(s.Ring, q) && !s.ByWater(q) && !((i == 0 || i == p.Points.Length - 1) && AtRoad(q))).Count();
                if (outside > 0) list.Add($"{outside} of its {p.Points.Length} points are outside the selection");
                for (int k = 0; k + 1 < p.Points.Length; k++)
                    foreach (var b in s.Buildings)
                    {
                        var quad = new[] { b.A, b.B, b.C, b.D };
                        var m = (p.Points[k] + p.Points[k + 1]) * 0.5f;
                        if (Poly.Contains(quad, m)) { list.Add($"segment {k} runs through an existing building or lot"); goto doneBuildings; }
                    }
                doneBuildings:;
                // does it reach anything: a road, another path, or its own loop
                // ...or ends at a destination: on or by a surface (a terrace, a plaza)
                bool Joined(float2 end) =>
                    s.Nets.Any(n => NetDistance(n, end) - n.Width * 0.5f < 3f) ||
                    all.Any(o => o.Kind == AiPieceKind.Surface && o.Ring != null && (Poly.Contains(o.Ring, end) || Poly.DistanceToBoundary(o.Ring, end) < 2f)) ||
                    all.Any(o => o.Id != p.Id && o.Kind == AiPieceKind.Path && o.Points != null && Enumerable.Range(0, o.Points.Length - 1).Any(k => SegDist(end, o.Points[k], o.Points[k + 1]) < 2f));
                var a = p.Points[0];
                var z = p.Points[p.Points.Length - 1];
                bool loop = math.distance(a, z) < 1f;
                if (!loop && !Joined(a)) list.Add("its start joins nothing (no road, path or surface within 3 m): a dead end");
                if (!loop && !Joined(z)) list.Add("its end joins nothing (no road, path or surface within 3 m): a dead end");
            }
            return list;
        }

        /// <summary>How many metres of a visible path or road run inside the ring (the invisible car lanes of a parking lot belong under its surface).</summary>
        public static float HiddenUnder(AiPiece path, float2[] ring)
        {
            if (path.Kind != AiPieceKind.Path || path.Points == null || path.NetRole == NetRole.Fence) return 0f;
            if (path.NetRole == NetRole.DriveAisle && (path.Asset ?? "").IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0) return 0f;
            float inside = 0f;
            for (int k = 0; k + 1 < path.Points.Length; k++)
            {
                float len = math.distance(path.Points[k], path.Points[k + 1]);
                int n = math.max(1, (int)math.ceil(len));
                for (int i = 0; i < n; i++) if (Poly.Contains(ring, math.lerp(path.Points[k], path.Points[k + 1], (i + 0.5f) / n))) inside += len / n;
            }
            return inside;
        }

        /// <summary>Do two surface rings overlap by more than a sliver (the game shows only one surface where they do)?</summary>
        public static bool SurfacesOverlap(float2[] a, float2[] b)
        {
            float2 alo = new float2(float.MaxValue), ahi = new float2(float.MinValue), blo = alo, bhi = ahi;
            foreach (var q in a) { alo = math.min(alo, q); ahi = math.max(ahi, q); }
            foreach (var q in b) { blo = math.min(blo, q); bhi = math.max(bhi, q); }
            var lo = math.max(alo, blo); var hi = math.min(ahi, bhi);
            if (hi.x - lo.x < 0.6f || hi.y - lo.y < 0.6f) return false;
            // sample the shared box: any point inside both (inset so touching edges do not count)
            int n = 0;
            for (float x = lo.x + 0.3f; x < hi.x - 0.29f; x += math.max(0.5f, (hi.x - lo.x) / 12f))
                for (float z = lo.y + 0.3f; z < hi.y - 0.29f; z += math.max(0.5f, (hi.y - lo.y) / 12f))
                    if (Poly.Contains(a, new float2(x, z)) && Poly.Contains(b, new float2(x, z)) && ++n >= 2) return true;
            return false;
        }

        public static float PathHalfWidth(AiPiece p) => p.NetRole == NetRole.Fence ? 0.3f : p.NetRole == NetRole.PathwayWide ? 3f : p.NetRole == NetRole.DriveAisle ? 3.5f : 2f;

        /// <summary>
        /// The build in numbers for the review: what it holds per hectare, how much ground is treated, and the empty spots
        /// (the places farthest from anything placed: nothing there but bare or painted ground). Facts, no verdict.
        /// </summary>
        public static string Stats(AiSite s, IReadOnlyList<AiPiece> all)
        {
            float area = math.abs(Poly.SignedArea(s.Ring));
            float ha = math.max(0.01f, area / 10000f);
            var objs = all.Where(p => p.Kind == AiPieceKind.Object).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"The build in numbers (site {F(area)} m², {F(ha)} ha):");
            int trees = objs.Count(o => o.Role == ObjectRole.Tree), bushes = objs.Count(o => o.Role == ObjectRole.Bush), flat = objs.Count(IsFlat);
            sb.AppendLine($"- objects {objs.Count}: trees {trees} ({F(trees / ha)}/ha), bushes {bushes}, decals {flat}, everything else {objs.Count - trees - bushes - flat} in {objs.Where(o => o.Role != ObjectRole.Tree && o.Role != ObjectRole.Bush && !IsFlat(o)).Select(o => o.Asset).Distinct().Count()} kinds");
            float pathLen = all.Where(p => p.Kind == AiPieceKind.Path && p.NetRole != NetRole.Fence && p.Points != null).Sum(p => Enumerable.Range(0, p.Points.Length - 1).Sum(k => math.distance(p.Points[k], p.Points[k + 1])));
            float fenceLen = all.Where(p => p.Kind == AiPieceKind.Path && p.NetRole == NetRole.Fence && p.Points != null).Sum(p => Enumerable.Range(0, p.Points.Length - 1).Sum(k => math.distance(p.Points[k], p.Points[k + 1])));
            sb.AppendLine($"- paths and roads {F(pathLen)} m, fences and hedges {F(fenceLen)} m");
            var ground = all.Where(p => p.Kind == AiPieceKind.Ground).ToList();
            sb.AppendLine($"- ground shaped in {ground.Count} patches, {ground.Count(g => math.isfinite(g.WaterLevel))} with water");

            // a 3 m grid over the site: painted, surfaced, and the clearance to the nearest placed thing
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var q in s.Ring) { lo = math.min(lo, q); hi = math.max(hi, q); }
            int cells = 0, painted = 0, surfaced = 0;
            var spots = new List<(float2 p, float clear)>();
            for (float x = lo.x + 1.5f; x < hi.x; x += 3f)
                for (float z = lo.y + 1.5f; z < hi.y; z += 3f)
                {
                    var q = new float2(x, z);
                    if (!Poly.Contains(s.Ring, q)) continue;
                    cells++;
                    if (all.Any(p => p.Kind == AiPieceKind.Paint && p.Layer > 0 && p.Ring != null && Poly.Contains(p.Ring, q))) painted++;
                    bool onSurface = all.Any(p => p.Kind == AiPieceKind.Surface && p.Ring != null && Poly.Contains(p.Ring, q));
                    if (onSurface) surfaced++;
                    float clear = math.min(Poly.DistanceToBoundary(s.Ring, q) * 1.5f, 999f);
                    // open water is a feature; bare paving is not (eval: a plaza of one grey slab read as full)
                    if (all.Any(p => p.Kind == AiPieceKind.Ground && math.isfinite(p.WaterLevel) && Poly.Contains(p.Ring, q))) clear = 0f;
                    foreach (var p in all)
                    {
                        if (clear <= 0f) break;
                        if (p.Kind == AiPieceKind.Object) clear = math.min(clear, math.distance(p.Position, q) - RadiusOf(s, p));
                        else if (p.Kind == AiPieceKind.Path && p.Points != null)
                            for (int k = 0; k + 1 < p.Points.Length; k++) clear = math.min(clear, SegDist(q, p.Points[k], p.Points[k + 1]) - PathHalfWidth(p));
                    }
                    spots.Add((q, math.max(0f, clear)));
                }
            sb.AppendLine($"- ground paint on {F(100f * painted / math.max(1, cells))} % of the site, surfaces on {F(100f * surfaced / math.max(1, cells))} %");
            if (math.isfinite(s.WaterLevel)) sb.AppendLine("- shore: " + Shore(s, all));
            var chosen = new List<(float2 p, float clear)>();
            foreach (var c in spots.OrderByDescending(c => c.clear))
            {
                if (c.clear < 7f || chosen.Count >= 5) break;
                if (chosen.Any(o => math.distance(o.p, c.p) < o.clear + c.clear)) continue;
                chosen.Add(c);
            }
            sb.AppendLine(chosen.Count == 0 ? "- empty spots: none wider than 14 m"
                : "- empty spots (nothing placed within the radius; fine if meant as a lawn, meadow or open square, else fill them): " + string.Join(", ", chosen.Select(c => { var l = s.Local(c.p); return $"({F(l.x)},{F(l.y)}) {F(c.clear)} m"; })));
            return sb.ToString();
        }

        /// <summary>
        /// The close-range detail in numbers (the user, 27 Oct: "B- from high up, D- zoomed in: it needs intricate details"):
        /// how dense and varied the small things are, and the places that look bare up close, with where they are. Facts.
        /// </summary>
        public static string Details(AiSite s, IReadOnlyList<AiPiece> all)
        {
            float area = math.abs(Poly.SignedArea(s.Ring));
            float ha = math.max(0.01f, area / 10000f);
            var objs = all.Where(p => p.Kind == AiPieceKind.Object).ToList();
            var small = objs.Where(o => o.Role != ObjectRole.Tree).ToList();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Close-up detail in numbers: {objs.Count} objects ({F(objs.Count / ha)}/ha), {small.Count} of them not trees ({F(small.Count / ha)}/ha) in {small.Select(o => o.Asset).Distinct().Count()} different assets. The premade types place 200-800 objects on a site of this size, most of them small.");
            bool Near(float2 p, float r, Func<AiPiece, bool> what) => objs.Any(o => what(o) && math.distance(o.Position, p) <= r);
            string L(float2 p) { var l = s.Local(p); return $"({F(l.x)},{F(l.y)})"; }
            // benches and seats without company (a bin, a lamp or planting within 6 m)
            var lonely = objs.Where(o => o.Role == ObjectRole.Bench).Where(b => !Near(b.Position, 6f, o => o.Id != b.Id && (o.Role == ObjectRole.Bin || o.Role == ObjectRole.Lamp || o.Role == ObjectRole.Bush || o.Role == ObjectRole.Planter))).ToList();
            if (lonely.Count > 0) sb.AppendLine($"- benches with nothing beside them (no bin, lamp, bush or planter within 6 m): {string.Join(" ", lonely.Take(8).Select(b => "#" + b.Id + " " + L(b.Position)))}");
            // features without a setting: few things within 10 m of a feature (anything not a tree, bush, bench, bin or lamp)
            bool Feature(AiPiece o) => o.Role == ObjectRole.Centerpiece || (o.Role == ObjectRole.Furniture && s.Assets.TryGetValue(o.Asset ?? "", out var a) && math.cmax(a.Size.xz) >= 3f);
            var bare = objs.Where(Feature).Where(f => objs.Count(o => o.Id != f.Id && math.distance(o.Position, f.Position) <= 10f) < 4).ToList();
            if (bare.Count > 0) sb.AppendLine($"- features standing alone (under 4 things within 10 m: give each a setting of paving, seats, planting, lamps, props): {string.Join(" ", bare.Take(8).Select(f => "#" + f.Id + " " + f.Asset + " " + L(f.Position)))}");
            // bare stretches of path: 15 m or more with nothing within 5 m of either side
            var stretches = new List<(float len, float2 at)>();
            foreach (var p in all.Where(p => p.Kind == AiPieceKind.Path && p.NetRole != NetRole.Fence && p.Points != null && float.IsNaN(p.LineHeight)))
            {
                float run = 0f; float2 start = p.Points[0];
                for (int k = 0; k + 1 < p.Points.Length; k++)
                {
                    float len = math.distance(p.Points[k], p.Points[k + 1]);
                    for (float d = 0f; d < len; d += 2f)
                    {
                        var q = math.lerp(p.Points[k], p.Points[k + 1], d / math.max(0.01f, len));
                        bool company = objs.Any(o => math.distance(o.Position, q) <= PathHalfWidth(p) + 5f);
                        if (company) { if (run >= 15f) stretches.Add((run, start)); run = 0f; start = q; }
                        else run += 2f;
                    }
                }
                if (run >= 15f) stretches.Add((run, start));
            }
            if (stretches.Count > 0) sb.AppendLine($"- bare path stretches (nothing within 5 m of either side): {string.Join(", ", stretches.OrderByDescending(t => t.len).Take(8).Select(t => F(t.len) + " m from " + L(t.at)))}");
            // paths without light: path length per lamp
            float pathLen = all.Where(p => p.Kind == AiPieceKind.Path && p.NetRole != NetRole.Fence && p.Points != null).Sum(p => Enumerable.Range(0, p.Points.Length - 1).Sum(k => math.distance(p.Points[k], p.Points[k + 1])));
            int lamps = objs.Count(o => o.Role == ObjectRole.Lamp);
            if (pathLen > 40f) sb.AppendLine($"- lamps: {lamps} for {F(pathLen)} m of path ({(lamps > 0 ? F(pathLen / lamps) + " m per lamp" : "none")}; parks light a lamp every 20-30 m)");
            // paved areas and the shapes of the ground with nothing on them
            foreach (var p in all.Where(p => p.Kind == AiPieceKind.Surface && p.Ring != null))
            {
                float a = math.abs(Poly.SignedArea(p.Ring));
                int on = objs.Count(o => Poly.Contains(p.Ring, o.Position) || Poly.DistanceToBoundary(p.Ring, o.Position) < 2f);
                if (a > 150f && on < a / 120f) sb.AppendLine($"- surface #{p.Id} {p.Asset}: {F(a)} m² with {on} things on or at its edge (a used place has seating, planters, bollards, lamps, props every 8-12 m)");
            }
            // single kinds repeated many times: variety
            var top = small.GroupBy(o => o.Asset).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top != null && small.Count > 20 && top.Count() > small.Count * 0.4f) sb.AppendLine($"- {top.Key} is {F(100f * top.Count() / small.Count)} % of the small things: mix in other kinds");
            return sb.ToString();
        }

        /// <summary>
        /// Placement taste (the user, 27 Oct: shrubs and a climbing frame on crop fields, umbrellas on the lawn, shrubs on the
        /// beach): a thing where it does not belong, in words; null when it fits. Crop fields hold only crops and farm things;
        /// beach things stand on sand; trees and bushes stay off the beach sand.
        /// </summary>
        public static string Taste(AiSite s, AiPiece p, IReadOnlyList<AiPiece> all)
        {
            string cat = p.Category ?? "";
            bool farmThing = cat.StartsWith("farm", StringComparison.Ordinal) || cat == AssetCategories.GardenCrop || cat.StartsWith("animal", StringComparison.Ordinal) || cat == AssetCategories.Fence;
            foreach (var o in all)
            {
                if (o.Kind != AiPieceKind.Surface || o.Ring == null || farmThing) continue;
                string a = o.Asset ?? "";
                bool field = a.IndexOf("Agricultur", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("Crop", StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf("Field", StringComparison.OrdinalIgnoreCase) >= 0;
                if (field && Poly.Contains(o.Ring, p.Position)) return $"on crop field #{o.Id} (only crops, hay, troughs and farm machinery belong in a field: move it to the yard, a hedgerow or the field edge)";
            }
            bool onSand = OnSand(s, all, p.Position);
            if (cat.StartsWith("beach.", StringComparison.Ordinal) && cat != AssetCategories.BeachHut && cat != AssetCategories.BeachShower && !onSand)
                return "a beach thing off the sand (umbrellas, loungers, lifeguard towers, boats and volleyball stand on the beach sand)";
            if ((p.Role == ObjectRole.Tree || p.Role == ObjectRole.Bush) && onSand && NearWater(s, p.Position, 40f))
                return "a plant on the beach sand (keep the beach open: plant behind it, on the dunes or the lawn)";
            return null;
        }

        private static bool OnSand(AiSite s, IReadOnlyList<AiPiece> all, float2 q)
        {
            foreach (var o in all)
            {
                if (o.Ring == null) continue;
                if (o.Kind == AiPieceKind.Surface && ((o.Category ?? "") == AssetCategories.SurfaceSand || (o.Asset ?? "").IndexOf("Sand", StringComparison.OrdinalIgnoreCase) >= 0) && Poly.Contains(o.Ring, q)) return true;
                if (o.Kind == AiPieceKind.Paint && s.SandLayer > 0 && o.Layer == s.SandLayer && o.Strength >= 0.5f && Poly.Contains(o.Ring, q)) return true;
            }
            return false;
        }

        private static bool NearWater(AiSite s, float2 q, float r)
        {
            if (s.Water(q) > 0.05f) return true;
            for (int k = 0; k < 8; k++)
            {
                var d = new float2(math.cos(k * 0.785f), math.sin(k * 0.785f));
                for (float t = 5f; t <= r; t += 5f) if (s.Water(q + d * t) > 0.05f) return true;
            }
            return false;
        }

        /// <summary>The ground at p after every shaping piece, in order (soft, line and hard shapes alike).</summary>
        public static float GroundAfter(AiSite s, IReadOnlyList<AiPiece> all, float2 p)
        {
            float g = s.Ground(p);
            foreach (var o in all)
            {
                if (o.Kind != AiPieceKind.Ground || o.Ring == null) continue;
                if (AiTerrain.IsField(o)) { g = AiTerrain.HeightAt(o, p, g); continue; }
                if (!Poly.Contains(o.Ring, p)) continue;
                AiTerrain.Core(o, p, out float t, out _);
                g = o.Shape == TerraformKind.Raise ? math.max(g, t) : o.Shape == TerraformKind.Lower ? math.min(g, t) : t;
            }
            return g;
        }

        /// <summary>Is p under water once the shaping is built: below a pond's water in its basin, or below the open water in
        /// ground lowered next to it (the lake flows in)?</summary>
        public static bool UnderWater(AiSite s, IReadOnlyList<AiPiece> all, float2 p, out float depth)
        {
            depth = 0f;
            var shaping = all.Where(o => o.Kind == AiPieceKind.Ground && o.Ring != null).ToList();
            if (shaping.Count == 0) return false;
            float g = GroundAfter(s, shaping, p);
            foreach (var o in shaping)
            {
                bool inside = Poly.Contains(o.Ring, p) || (AiTerrain.IsField(o) && Poly.DistanceToBoundary(o.Ring, p) < o.Feather);
                if (!inside) continue;
                if (math.isfinite(o.WaterLevel) && g < o.WaterLevel - 0.05f) { depth = o.WaterLevel - g; return true; }
                if (math.isfinite(s.WaterLevel) && g < s.WaterLevel - 0.05f && o.Ring.Any(q => s.ByWater(q) || s.Water(q) > 0.05f)) { depth = s.WaterLevel - g; return true; }
            }
            return false;
        }

        /// <summary>The shore in numbers: whether the ground was carved below the water (a bay), and how far each paint stops short of the water.</summary>
        private static string Shore(AiSite s, IReadOnlyList<AiPiece> all)
        {
            var parts = new List<string>();
            bool bay = all.Any(p => p.Kind == AiPieceKind.Ground && p.Ring != null && (p.CornerHeights != null ? p.CornerHeights.Min() : p.Height) < s.WaterLevel - 0.3f
                && p.Ring.Any(q => s.ByWater(q) || s.Water(q) > 0.05f));
            parts.Add(bay ? "ground carved below the water level next to the water (the lake flows in)" : "no ground carved below the water level next to the water: the shore stays the straight line it was");
            foreach (var g in all.Where(p => p.Kind == AiPieceKind.Paint && p.Layer > 0 && p.Ring != null).GroupBy(p => p.Layer))
            {
                // the paint's reach toward the water: the nearest water from any of its corners
                float best = float.MaxValue;
                foreach (var p in g)
                    foreach (var q in p.Ring)
                    {
                        if (s.Water(q) > 0.05f) { best = 0f; break; }
                        for (int k = 0; k < 8; k++)
                        {
                            var d = new float2(math.cos(k * 0.785f), math.sin(k * 0.785f));
                            for (float r = 2f; r <= 40f; r += 2f) if (s.Water(q + d * r) > 0.05f) { best = math.min(best, r); break; }
                        }
                    }
                parts.Add(best <= 0f ? $"paint layer {g.Key} reaches into the water" : best < float.MaxValue ? $"paint layer {g.Key} stops {F(best)} m short of the water" : $"paint layer {g.Key} is far from the water");
            }
            return string.Join("; ", parts);
        }

        /// <summary>Every piece's problems, one line each ("#12 bench_a at (40,22): overlaps #9 ..."), problem-free pieces left out.</summary>
        public static List<string> All(AiSite s, IReadOnlyList<AiPiece> all)
        {
            var lines = new List<string>();
            foreach (var p in all)
            {
                var pr = Problems(s, p, all);
                if (pr.Count == 0) continue;
                var at = s.Local(p.Kind == AiPieceKind.Object ? p.Position : p.Points != null ? p.Points[0] : p.Ring != null ? p.Ring[0] : p.Position);
                string what = p.Kind == AiPieceKind.Object ? (string.IsNullOrEmpty(p.Asset) ? "auto " + p.Role : p.Asset) : p.Kind.ToString().ToLowerInvariant();
                lines.Add($"#{p.Id} {what} at ({F(at.x)},{F(at.y)}): {string.Join("; ", pr)}");
            }
            return lines;
        }
    }
}

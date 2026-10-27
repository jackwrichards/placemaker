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
                int outside = p.Ring.Count(q => !Poly.Contains(s.Ring, q) && Poly.DistanceToBoundary(s.Ring, q) > 1f);
                if (outside > 0) list.Add($"{outside} of its {p.Ring.Length} corners are outside the selection");
                if (p.Kind == AiPieceKind.Surface)
                    foreach (var n in s.Nets)
                        if (p.Ring.Any(q => NetDistance(n, q) < n.Width * 0.5f) || Poly.Contains(p.Ring, SiteBrief.Bez(n, 0.5f).xz)) { list.Add($"covers an existing {(n.Category == NetCategory.Road ? "road" : "path")}"); break; }
            }
            else if (p.Points != null)
            {
                // an end may reach just past the outline to meet a road there (an entrance)
                bool AtRoad(float2 q) => s.Nets.Any(n => NetDistance(n, q) - n.Width * 0.5f < 4f);
                int outside = p.Points.Where((q, i) => !Poly.Contains(s.Ring, q) && !((i == 0 || i == p.Points.Length - 1) && AtRoad(q))).Count();
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

        public static float PathHalfWidth(AiPiece p) => p.NetRole == NetRole.Fence ? 0.3f : p.NetRole == NetRole.PathwayWide ? 3f : p.NetRole == NetRole.DriveAisle ? 3.5f : 2f;

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

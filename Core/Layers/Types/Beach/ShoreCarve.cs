// Shore carving (the user, 27 Oct: "advanced carving abilities ... beaches"): a bay cut into the land along the shore, below
// the water level, so the lake flows in and the waterline becomes a crescent instead of the straight edge the player drew
// along. The layer then plans against that waterline: the site it sees has the carved ground and water in the bay (a
// swapped AreaInput), and Shoreline.Analyze finds the bay as water inside the outline (ShoreMode.Inside). The carve itself
// is emitted first, as permanent Lower terraform cells, so the beach face and the sand are laid on top of it.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public static class ShoreCarve
    {
        /// <summary>Water depth at the old shore line and at the bay's inland edge (m).</summary>
        public const float DeepEnd = 1.8f, ShallowEnd = 0.35f;

        /// <summary>
        /// Carves a bay up to <paramref name="depth"/> m into the land, its ends <paramref name="endInset"/> m from the ends
        /// of the shore. Returns false (and changes nothing) without real water in front of the shore or without room.
        /// On success the layer's input carries the carved site and the terraform is in the plan.
        /// </summary>
        public static bool Bay(LayerContext ctx, Shoreline shore, float depth, float endInset, uint seed, out float2[] ring)
        {
            ring = null;
            if (shore == null || depth < 3f || !math.isfinite(shore.WaterLevel) || shore.Mode != ShoreMode.Edge || shore.Length < 40f) return false;
            float level = shore.WaterLevel;
            // never more than a third of the land's depth: the sand, the path and what is behind them need the rest
            depth = math.min(depth, shore.MaxDepth * 0.33f);
            float a = math.min(endInset, shore.Length * 0.2f), b = shore.Length - a;
            if (depth < 3f || b - a < 30f) return false;
            float phase = Hashing.Unit(seed) * 6.283f;
            float Reach(float s)
            {
                float u = math.saturate((s - a) / (b - a));
                // a crescent, a little lopsided, with a slow wobble so it does not read as drawn with a compass
                float shape = math.pow(math.sin(math.PI * math.pow(u, 0.9f)), 0.75f);
                return depth * shape * (0.92f + 0.08f * math.sin(u * 9.4f + phase));
            }
            var inner = new List<float2>();
            var outer = new List<float2>();
            int steps = math.max(8, (int)math.ceil((b - a) / 3f));
            for (int i = 0; i <= steps; i++)
            {
                float s = math.lerp(a, b, i / (float)steps);
                var c = shore.At(s, out var inw, out _);
                inner.Add(c + inw * Reach(s));
                outer.Add(shore.WaterAt(s, out _) - inw * 4f);   // into the real water, so the bay joins the lake
            }
            var pts = new List<float2>(outer);
            for (int i = inner.Count - 1; i >= 0; i--) pts.Add(inner[i]);
            var r = pts.ToArray();
            if (Poly.SignedArea(r) < 0f) Array.Reverse(r);
            ring = r;

            // the floor: deep at the old shore, shelving up to the inland edge
            var site = ctx.Site;
            Func<float2, float> h0 = site.HeightAt ?? (_ => 0f);
            Func<float2, float> d0 = site.WaterDepthAt ?? (_ => 0f);
            var bay = r;
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var q in bay) { lo = math.min(lo, q); hi = math.max(hi, q); }
            float Floor(float2 p)
            {
                var q = shore.Nearest(p, out var inw, out float s);
                float d = math.dot(p - q, inw);
                float reach = math.max(1f, Reach(s));
                float t = math.pow(math.saturate(d / reach), 0.7f);
                return level - math.lerp(DeepEnd, ShallowEnd, t);
            }
            bool In(float2 p) => p.x >= lo.x && p.y >= lo.y && p.x <= hi.x && p.y <= hi.y && Poly.Contains(bay, p);
            Func<float2, float> h1 = p => In(p) ? math.min(h0(p), Floor(p)) : h0(p);
            var carved = site.WithHeight(h1);
            carved.WaterDepthAt = p => In(p) ? math.max(d0(p), level - h1(p)) : d0(p);

            // the carve, as permanent Lower cells clipped to the bay (3 m: finer than the heightmap's 3.5 m texel would waste work)
            const float cell = 3f;
            int cells = 0;
            for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
            {
                ctx.Check();
                for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                {
                    var c = new float2(x, y);
                    if (!In(c)) continue;
                    float target = Floor(c);
                    if (h0(c) - target < 0.15f) continue;
                    var square = LayerRegion.Rect(c, new float2(1, 0), cell * 0.5f + 0.25f, cell * 0.5f + 0.25f);
                    float2[] piece = square;
                    bool whole = true;
                    foreach (var q in square) if (!Poly.Contains(bay, q)) { whole = false; break; }
                    if (!whole)
                    {
                        var parts = Poly.Intersection(square, bay);
                        if (parts.Count == 0 || parts[0].Outer == null || parts[0].Outer.Length < 3) continue;
                        piece = parts[0].Outer;
                    }
                    ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(piece, h0), Kind = TerraformKind.Lower, TargetHeight = target, Permanent = true });
                    cells++;
                }
            }
            if (cells == 0) { ring = null; return false; }
            ShoreTerrain.Warn(ctx);
            ctx.Input = ctx.Input.WithSite(carved);
            ctx.Plan.Facts["bayDepth"] = depth;
            ctx.Plan.Facts["bayCells"] = cells;
            ctx.Plan.Facts["bayArea"] = math.abs(Poly.SignedArea(bay));
            return true;
        }
    }
}

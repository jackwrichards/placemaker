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
                var w = shore.WaterAt(s, out _);
                if (!math.all(math.isfinite(w))) w = c;   // no measured waterline here: the shore itself
                inner.Add(c + inw * Reach(s));
                outer.Add(w - inw * 4f);   // into the real water, so the bay joins the lake
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
            ctx.Plan.Facts["bayArea"] = math.abs(Poly.SignedArea(r));
            return true;
        }

        /// <summary>
        /// (p2-beaches) A pocket beach cut into a cliff: a cove opened down to the water in the middle of the longest cliff
        /// stretch, rock walls on three sides, and a gully climbing from the back of the sand to the cliff top at a walkable
        /// grade (the path down runs on its floor). Null (nothing changed) on a shore that is not mostly cliff, or without
        /// room for the gully. On success the layer's input carries the carved site and the terraform is in the plan.
        /// </summary>
        public static Pocket CutPocket(LayerContext ctx, Shoreline shore, float landDepth)
        {
            if (shore == null || shore.Kinds == null || !math.isfinite(shore.WaterLevel) || shore.Mode != ShoreMode.Edge) return null;
            if (shore.CliffShare < ShoreGround.CliffShoreShare) return null;
            // the longest cliff stretch
            float best = 0f, s0 = 0f, s1 = 0f, start = float.NaN;
            for (float s = 0f; s <= shore.Length + 1e-3f; s += 2f)
            {
                bool cliff = shore.KindAt(math.min(s, shore.Length)) == ShoreKind.Cliff;
                if (cliff && float.IsNaN(start)) start = s;
                if ((!cliff || s + 2f > shore.Length) && !float.IsNaN(start))
                {
                    float end = cliff ? shore.Length : s - 1f;
                    if (end - start > best) { best = end - start; s0 = start; s1 = end; }
                    start = float.NaN;
                }
            }
            if (best < 46f) return null;
            var site = ctx.Site;
            Func<float2, float> h0 = site.HeightAt ?? (_ => 0f);
            Func<float2, float> d0 = site.WaterDepthAt ?? (_ => 0f);
            var pk = new Pocket { Level = shore.WaterLevel };
            pk.O = shore.WaterAt((s0 + s1) * 0.5f, out var inw);
            if (!math.all(math.isfinite(pk.O))) return null;
            pk.Inw = inw;
            pk.Tan = new float2(inw.y, -inw.x);
            // (the game runner: an 11 m gully and a 17 m deep cove blurred into a green dip on the 3.5 m heightmap)
            pk.HalfW = math.clamp(best * 0.25f, 22f, 36f);
            pk.Sand = math.clamp(landDepth * 0.25f, 16f, 24f);
            // the gully: a 12 % climb (15 % on shallow land) from the back of the sand until it meets the cliff top
            pk.End = float.NaN;
            foreach (float grade in new[] { TerrainSurvey.RampGrade, 0.15f })
            {
                pk.Grade = grade;
                for (float d = pk.Sand + 4f; d < landDepth - 8f; d += 1f)
                    if (pk.Base(d) >= h0(pk.O + inw * d) - 0.2f) { pk.End = d; break; }
                if (math.isfinite(pk.End)) break;
            }
            if (!math.isfinite(pk.End) || !shore.Land.ContainsDisc(pk.Top, 4f)) return null;
            pk.TopH = h0(pk.Top);

            Func<float2, float> h1 = p => math.min(h0(p), pk.Floor(p));
            var carved = site.WithHeight(h1);
            float level = pk.Level;
            carved.WaterDepthAt = p => math.max(d0(p), level - h1(p));
            // sloped 2 m quads (corner heights on the carved surface): the walls stay continuous rock faces, not terraces
            const float cell = 2f;
            int cells = 0;
            float reach = math.max(pk.HalfW, pk.Gully) + 12f;
            for (float d = -10f; d < pk.End + 10f; d += cell)
            {
                ctx.Check();
                for (float x = -reach; x < reach; x += cell)
                {
                    var c = pk.O + pk.Tan * (x + cell * 0.5f) + inw * (d + cell * 0.5f);
                    if (h0(c) - h1(c) < 0.15f) continue;
                    var ring = new[] { pk.O + pk.Tan * x + inw * d, pk.O + pk.Tan * (x + cell) + inw * d, pk.O + pk.Tan * (x + cell) + inw * (d + cell), pk.O + pk.Tan * x + inw * (d + cell) };
                    if (Poly.SignedArea(ring) < 0f) Array.Reverse(ring);
                    ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(ring, h1), Kind = TerraformKind.Lower, TargetHeight = h1(c), Permanent = true, Sloped = true });
                    cells++;
                }
            }
            if (cells == 0) return null;
            ShoreTerrain.Warn(ctx);
            ctx.Input = ctx.Input.WithSite(carved);
            ctx.Plan.Facts["pocketCells"] = cells;
            ctx.Plan.Facts["pocketWidth"] = pk.HalfW * 2f;
            ctx.Plan.Facts["pocketGully"] = pk.End - pk.Sand;
            ctx.Plan.Facts["pocketGrade"] = pk.Grade;
            return pk;
        }

        /// <summary>(p2-beaches) The pocket beach's shape, in a frame on the waterline at its middle (x along the shore, d inland).</summary>
        public sealed class Pocket
        {
            public float2 O, Inw, Tan;
            public float Level, HalfW, Sand, End, Grade, TopH;
            /// <summary>Half-width of the gully floor (a 3 m path and its verges).</summary>
            public float Gully = 8f;
            /// <summary>Grade of the rock walls (about 60°).</summary>
            public const float Wall = 1.7f;
            public float2 Bottom => O + Inw * (Sand - 3f);
            public float2 Top => O + Inw * (End + 3f);

            /// <summary>The floor along the axis: sand shelving out of the water, then the gully's climb.</summary>
            public float Base(float d) => d < 0f ? math.max(Level - 1.4f, Level + 0.3f + 0.2f * d)
                : Level + 0.3f + 0.045f * math.min(d, Sand) + Grade * math.max(0f, d - Sand);

            /// <summary>Distance outside the carved shape (negative inside): a half-ellipse cove open to the water, and the gully.</summary>
            public float Outside(float2 p, out float x, out float d)
            {
                var v = p - O; x = math.dot(v, Tan); d = math.dot(v, Inw);
                float depth = Sand + 3f;
                float ex = x / HalfW, ed = math.max(0f, d) / depth;
                float cove = (math.sqrt(ex * ex + ed * ed) - 1f) * math.min(HalfW, depth);
                float gully = math.max(math.abs(x) - Gully, d - End);
                return math.min(cove, gully);
            }

            public float Floor(float2 p)
            {
                float o = Outside(p, out _, out float d);
                return Base(math.clamp(d, -10f, End)) + Wall * math.max(0f, o);
            }

            /// <summary>On the open sand of the cove (not the walls, not the gully).</summary>
            public bool OnSand(float2 p, float margin)
            {
                float o = Outside(p, out _, out float d);
                return o < -margin && d < Sand + 1f;
            }

            /// <summary>The outline of the carved shape grown by k m (negative: shrunk), as rings: the cove and the gully.</summary>
            public List<float2[]> Rings(float k)
            {
                float depth = Sand + 3f + k, hw = HalfW + k;
                var cove = new List<float2> { At(hw, -8f) };
                for (int i = 0; i <= 24; i++) { float a = math.PI * i / 24f; cove.Add(At(hw * math.cos(a), depth * math.sin(a))); }
                cove.Add(At(-hw, -8f));
                var gully = new[] { At(-(Gully + k), Sand * 0.5f), At(Gully + k, Sand * 0.5f), At(Gully + k, End + k), At(-(Gully + k), End + k) };
                var list = new List<float2[]> { cove.ToArray(), gully };
                foreach (var r in list) if (Poly.SignedArea(r) < 0f) Array.Reverse(r);
                return list;
            }

            public float2 At(float x, float d) => O + Tan * x + Inw * d;

            /// <summary>On the rock walls (or their foot and lip): left natural, nothing placed there.</summary>
            public bool OnWall(float2 p)
            {
                float o = Outside(p, out _, out float d);
                return o > -1.5f && o < (TopH - Level) / Wall + 2f && d > -6f;
            }
        }
    }
}

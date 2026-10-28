// Placemaker — fitting planned net curves to CS2 courses (GPL-3.0).
// Terrain-split idea adapted from ParkManager's ParkPathCourseBuilder (tomkpunkt, GPL-3.0):
// a course only knows the terrain height at its ends, so on hills its curve can run metres
// above or below the ground and CS2 then carves or fills terrain along it. We split where the
// deviation is worst and pin the new node to the ground.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    /// <summary>Engine-free cubic Bézier (same control-point convention as Colossal's Bezier4x3).</summary>
    public struct Cubic
    {
        public float3 A, B, C, D;
        public Cubic(float3 a, float3 b, float3 c, float3 d) { A = a; B = b; C = c; D = d; }

        public static Cubic Straight(float3 a, float3 d) => new Cubic(a, math.lerp(a, d, 1f / 3f), math.lerp(a, d, 2f / 3f), d);

        public float3 Position(float t)
        {
            float u = 1 - t;
            return u * u * u * A + 3 * u * u * t * B + 3 * u * t * t * C + t * t * t * D;
        }

        public float3 Tangent(float t)
        {
            float u = 1 - t;
            return 3 * u * u * (B - A) + 6 * u * t * (C - B) + 3 * t * t * (D - C);
        }

        public float Length(int samples = 16)
        {
            float len = 0; float3 prev = A;
            for (int i = 1; i <= samples; i++) { var p = Position(i / (float)samples); len += math.distance(prev, p); prev = p; }
            return len;
        }

        public float LengthXZ(int samples = 16)
        {
            float len = 0; float2 prev = A.xz;
            for (int i = 1; i <= samples; i++) { var p = Position(i / (float)samples).xz; len += math.distance(prev, p); prev = p; }
            return len;
        }

        /// <summary>De Casteljau split: both halves trace exactly the original curve.</summary>
        public void Split(float t, out Cubic left, out Cubic right)
        {
            float3 ab = math.lerp(A, B, t), bc = math.lerp(B, C, t), cd = math.lerp(C, D, t);
            float3 abc = math.lerp(ab, bc, t), bcd = math.lerp(bc, cd, t);
            float3 m = math.lerp(abc, bcd, t);
            left = new Cubic(A, ab, abc, m);
            right = new Cubic(m, bcd, cd, D);
        }

        /// <summary>Closest XZ distance from p to the curve (sampled; good to a few cm for park-sized curves).</summary>
        public float DistanceXZ(float2 p, out float t, int samples = 24)
        {
            float best = float.MaxValue; t = 0;
            float2 prev = A.xz; float prevT = 0;
            for (int i = 1; i <= samples; i++)
            {
                float ti = i / (float)samples;
                float2 cur = Position(ti).xz;
                var ab = cur - prev;
                float l = math.lengthsq(ab);
                float s = l > 1e-9f ? math.saturate(math.dot(p - prev, ab) / l) : 0;
                float d = math.distance(p, prev + ab * s);
                if (d < best) { best = d; t = math.lerp(prevT, ti, s); }
                prev = cur; prevT = ti;
            }
            return best;
        }
    }

    public struct CourseFitSettings
    {
        /// <summary>Allowed vertical gap between curve and terrain before splitting (m).</summary>
        public float MaxTerrainDeviation;
        /// <summary>Pieces shorter than this are never split for terrain (CS2 rejects tiny edges and adds node meshes).</summary>
        public float MinSplitLength;
        /// <summary>Maximum piece length (prefab edge length range max). 0 = unlimited.</summary>
        public float MaxLength;
        public int MaxDepth;
        /// <summary>False = keep the planned heights (bridges, ramps).</summary>
        public bool FollowTerrain;

        public static CourseFitSettings Default => new CourseFitSettings
        {
            MaxTerrainDeviation = 0.35f, MinSplitLength = 6f, MaxLength = 0f, MaxDepth = 10, FollowTerrain = true,
        };
    }

    public static class CourseFitting
    {
        /// <summary>Pieces shorter than this become a node without an edge in CS2 — callers drop them.</summary>
        public const float MinCourseLength = 1f;

        /// <summary>
        /// Splits a planned curve into courses that hug the terrain and respect the prefab's edge
        /// length. Pieces join exactly (end of one == start of the next). Never returns null.
        /// </summary>
        public static List<Cubic> Fit(Cubic curve, Func<float2, float> heightAt, CourseFitSettings s)
        {
            var result = new List<Cubic>();
            if (!IsFinite(curve)) return result;
            if (s.MaxDepth <= 0) s.MaxDepth = 8;
            bool follow = s.FollowTerrain && heightAt != null;
            if (follow) curve = PinEnds(curve, heightAt, true, true);
            // Length first, then terrain: every piece CS2 gets is fitted to the ground it crosses.
            var pieces = new List<Cubic>();
            if (s.MaxLength > 0.5f) SplitByLength(curve, s.MaxLength, heightAt, follow, pieces);
            else pieces.Add(curve);
            foreach (var c in pieces) FitRecursive(c, heightAt, s, 0, result);
            return result;
        }

        private static void FitRecursive(Cubic c, Func<float2, float> heightAt, CourseFitSettings s, int depth, List<Cubic> output)
        {
            if (s.FollowTerrain && heightAt != null) c = FitProfile(c, heightAt);
            if (!s.FollowTerrain || heightAt == null || depth >= s.MaxDepth || c.Length() < s.MinSplitLength)
            {
                output.Add(c);
                return;
            }
            float worst = TerrainDeviation(c, heightAt, out float worstT);
            if (worst <= s.MaxTerrainDeviation)
            {
                output.Add(c);
                return;
            }
            c.Split(worstT, out var left, out var right);
            left = PinEnds(left, heightAt, false, true);
            right = PinEnds(right, heightAt, true, false);
            FitRecursive(left, heightAt, s, depth + 1, output);
            FitRecursive(right, heightAt, s, depth + 1, output);
        }

        /// <summary>
        /// Bridges, piers, boardwalks (PlannedNet.FollowTerrain == false): pieces of at most
        /// <paramref name="maxPiece"/> metres that keep the PLANNED heights exactly (never pinned to
        /// the ground). Why pieces: CS2's CourseSplitSystem re-derives a top-level course's heights as
        /// terrain + a LINEAR blend of the two end elevations (decompiled CourseHeightData), so one long
        /// course over a lake basin that has no water yet sinks to the bed between its raised ends. Each
        /// node carries its own elevation (planned y − ground), so short pieces follow the planned deck.
        /// </summary>
        /// <summary>
        /// A raised line's (bridge, pier, boardwalk) end on ground higher than its deck rises to the ground: the game digs
        /// the ground down to a deck end below it (a notch in the island where a bridge lands; the AI builder's fix, for
        /// every type). The lift fades out linearly towards the other end, so the deck keeps its arch.
        /// </summary>
        public static Cubic LandEnds(Cubic c, Func<float2, float> heightAt)
        {
            if (heightAt == null || !IsFinite(c)) return c;
            float ga = heightAt(c.A.xz), gd = heightAt(c.D.xz);
            float ra = math.isfinite(ga) ? math.max(0f, ga - c.A.y) : 0f;
            float rd = math.isfinite(gd) ? math.max(0f, gd - c.D.y) : 0f;
            if (ra <= 0f && rd <= 0f) return c;
            c.A.y += ra;
            c.B.y += math.lerp(ra, rd, 1f / 3f);
            c.C.y += math.lerp(ra, rd, 2f / 3f);
            c.D.y += rd;
            return c;
        }

        public static List<Cubic> FitElevated(Cubic curve, float maxPiece)
        {
            var result = new List<Cubic>();
            if (!IsFinite(curve)) return result;
            if (!(maxPiece > 1f)) { result.Add(curve); return result; }
            SplitByLength(curve, maxPiece, null, false, result);
            return result;
        }

        /// <summary>Largest vertical gap between the curve and the terrain, sampled at 15 interior points.</summary>
        public static float TerrainDeviation(Cubic c, Func<float2, float> heightAt, out float worstT)
        {
            worstT = 0.5f;
            float worst = 0;
            for (int i = 1; i < 24; i++)
            {
                float t = i / 24f;
                var p = c.Position(t);
                float g = heightAt(p.xz);
                if (!math.isfinite(g)) continue;
                float d = math.abs(p.y - g);
                if (d > worst) { worst = d; worstT = t; }
            }
            return worst;
        }

        private static void SplitByLength(Cubic c, float maxLen, Func<float2, float> heightAt, bool follow, List<Cubic> output)
        {
            float len = c.Length();
            int parts = (int)math.ceil(len / maxLen);
            if (parts <= 1) { output.Add(c); return; }
            var rest = c;
            for (int i = 0; i < parts - 1; i++)
            {
                // Split the remainder so each piece takes 1/(remaining parts) of it: equal parameter
                // steps are close enough to equal lengths for the gentle curves layers emit.
                float t = 1f / (parts - i);
                rest.Split(t, out var piece, out var tail);
                if (follow && heightAt != null)
                {
                    piece = PinEnds(piece, heightAt, false, true);
                    tail = PinEnds(tail, heightAt, true, false);
                }
                output.Add(piece);
                rest = tail;
            }
            output.Add(rest);
        }

        /// <summary>
        /// Playtest 4b ("paths clip into the ground"): a course only knew the ground at its ends; its
        /// inner control heights came from the plan (often the ground BEFORE terraform). Re-solves the
        /// two inner control heights so the curve meets the ground at t = 1/3 and 2/3 (ends kept).
        /// XZ is untouched, so the planned shape and joins stay exact.
        /// </summary>
        public static Cubic FitProfile(Cubic c, Func<float2, float> heightAt)
        {
            if (heightAt == null) return c;
            float g1 = heightAt(c.Position(1f / 3f).xz), g2 = heightAt(c.Position(2f / 3f).xz);
            if (!math.isfinite(g1) || !math.isfinite(g2)) return c;
            // 27·P(1/3) = 8A + 12B + 6C + D, 27·P(2/3) = A + 6B + 12C + 8D (y only).
            float r1 = 27f * g1 - 8f * c.A.y - c.D.y, r2 = 27f * g2 - c.A.y - 8f * c.D.y;
            float b = (12f * r1 - 6f * r2) / 108f, cc = (12f * r2 - 6f * r1) / 108f;
            if (!math.isfinite(b) || !math.isfinite(cc)) return c;
            c.B.y = b; c.C.y = cc;
            return c;
        }

        /// <summary>
        /// Moves the chosen endpoints onto the terrain and spreads the height change over the
        /// adjacent control points so the vertical profile stays smooth.
        /// </summary>
        public static Cubic PinEnds(Cubic c, Func<float2, float> heightAt, bool start, bool end)
        {
            if (start)
            {
                float g = heightAt(c.A.xz);
                if (math.isfinite(g)) { float dy = g - c.A.y; c.A.y = g; c.B.y += dy * (2f / 3f); c.C.y += dy * (1f / 3f); }
            }
            if (end)
            {
                float g = heightAt(c.D.xz);
                if (math.isfinite(g)) { float dy = g - c.D.y; c.D.y = g; c.C.y += dy * (2f / 3f); c.B.y += dy * (1f / 3f); }
            }
            return c;
        }

        public static bool IsFinite(Cubic c) =>
            math.all(math.isfinite(c.A)) && math.all(math.isfinite(c.B)) && math.all(math.isfinite(c.C)) && math.all(math.isfinite(c.D));

        /// <summary>
        /// Construction cost of a net piece the way CS2 charges it: per started 8 m cell
        /// (NetUtils.GetConstructionCost: max(1, round(length/8)) × cost per cell). Elevation cost ignored.
        /// </summary>
        public static double NetCost(float length, double costPer8m) => math.max(1, (int)math.round(length / 8f)) * costPer8m;
    }
}

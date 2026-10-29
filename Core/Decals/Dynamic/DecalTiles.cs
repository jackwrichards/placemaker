// Placemaker — dynamic decals: a stroke cut into decal boxes (docs/DYNAMIC-DECALS.md). GPL-3.0.
//
// A decal is drawn by a box the game's decal shader projects down onto the ground, so the box must be a box (a bent mesh
// would not project). Big boxes and outlines become a grid of tiles in their own frame; a path becomes boxes chained along
// it, each aligned to its chord. Every texel is generated from world coordinates, so tiles join without a seam; along a
// path each texel belongs to the box whose arc range holds its nearest path point (+5 cm overlap for filtering).

using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Decals.Dynamic
{
    public sealed class DecalTile
    {
        public int Stroke, Index;
        public float2 Center;
        /// <summary>The game's RotateY yaw (radians): local +x → world (cos, −sin).</summary>
        public float Yaw;
        public float2 Size;
        public int W, H;
        /// <summary>Path ownership: texels whose nearest arc length lies outside [S0, S1] belong to another tile.</summary>
        public float S0 = float.MinValue, S1 = float.MaxValue;

        public float2 ToWorld(float2 local)
        {
            float c = math.cos(Yaw), s = math.sin(Yaw);
            return Center + new float2(local.x * c + local.y * s, -local.x * s + local.y * c);
        }

        public float2 ToLocal(float2 world)
        {
            float c = math.cos(Yaw), s = math.sin(Yaw);
            var d = world - Center;
            return new float2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        public float2[] Corners()
        {
            var h = Size * 0.5f;
            return new[] { ToWorld(new float2(-h.x, -h.y)), ToWorld(new float2(h.x, -h.y)), ToWorld(new float2(h.x, h.y)), ToWorld(new float2(-h.x, h.y)) };
        }
    }

    public static class DecalTiler
    {
        public const float MaxTileMetres = 40f;

        /// <summary>The tiles of one stroke (empty tiles skipped). Texel size from the kind; each side a multiple of 4.</summary>
        public static List<DecalTile> Tiles(StrokeFrame f, int strokeIndex)
        {
            var list = new List<DecalTile>();
            var s = f.S; var k = f.K;
            float texel = k.Texel;
            float maxSide = math.min(MaxTileMetres, k.MaxTexels * texel);
            float pad = k.Warp * 1.3f + 0.3f;
            if (s.Shape == StrokeShape.Path)
            {
                if (s.Points.Length < 2 || f.Length < 0.05f) return list;
                float maxW = f.MaxWidth() * (1f + math.max(0f, k.WidthNoise)) * 0.5f;
                float seg = math.max(2f, maxSide - 2f * pad);
                int n = math.max(1, (int)math.ceil(f.Length / seg));
                for (int i = 0; i < n; i++)
                {
                    float s0 = f.Length * i / n, s1 = f.Length * (i + 1) / n;
                    f.PathAt(s0, out var a, out _);
                    f.PathAt(s1, out var e, out _);
                    f.PathAt((s0 + s1) * 0.5f, out var mid, out _);
                    var dir = math.normalizesafe(e - a, new float2(1f, 0f));
                    var c = (a + e) * 0.5f;
                    // the widest bulge of the arc between the ends
                    float sag = 0f;
                    for (int j = 1; j < 8; j++)
                    {
                        f.PathAt(math.lerp(s0, s1, j / 8f), out var pj, out _);
                        float2 d = pj - c;
                        sag = math.max(sag, math.abs(math.dot(d, new float2(-dir.y, dir.x))));
                    }
                    float endPad = (i == 0 || i == n - 1) ? maxW + pad : pad;
                    float len = math.distance(a, e) + 2f * endPad;
                    float wid = 2f * (maxW + pad + sag) + 0.5f;
                    float yaw = math.atan2(-dir.y, dir.x);
                    var t = new DecalTile
                    {
                        Stroke = strokeIndex, Index = i, Center = c, Yaw = yaw, Size = new float2(len, wid),
                        S0 = i == 0 ? float.MinValue : s0, S1 = i == n - 1 ? float.MaxValue : s1,
                    };
                    Res(t, texel, k.MaxTexels);
                    list.Add(t);
                }
                return list;
            }
            // box (in its own frame) or outline (axis aligned)
            float2 centre, ext; float yawT;
            if (s.Shape == StrokeShape.Box) { centre = s.Center; ext = s.Size + 2f * pad; yawT = s.Yaw; }
            else
            {
                s.Bounds(out var lo, out var hi);
                centre = (lo + hi) * 0.5f; ext = hi - lo + 2f * pad; yawT = 0f;
            }
            int nx = math.max(1, (int)math.ceil(ext.x / maxSide)), nz = math.max(1, (int)math.ceil(ext.y / maxSide));
            var size = new float2(ext.x / nx, ext.y / nz);
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var local = new float2((i + 0.5f) * size.x - ext.x * 0.5f, (j + 0.5f) * size.y - ext.y * 0.5f);
                    float c = math.cos(yawT), sn = math.sin(yawT);
                    var t = new DecalTile { Stroke = strokeIndex, Index = j * nx + i, Yaw = yawT, Size = size, Center = centre + new float2(local.x * c + local.y * sn, -local.x * sn + local.y * c) };
                    if (nx * nz > 1 && Empty(f, t)) continue;
                    Res(t, texel, k.MaxTexels);
                    list.Add(t);
                }
            return list;
        }

        private static void Res(DecalTile t, float texel, int maxTexels)
        {
            t.W = math.clamp((int)math.ceil(t.Size.x / texel / 4f) * 4, 8, maxTexels);
            t.H = math.clamp((int)math.ceil(t.Size.y / texel / 4f) * 4, 8, maxTexels);
        }

        /// <summary>No sample of a coarse grid comes near the footprint.</summary>
        private static bool Empty(StrokeFrame f, DecalTile t)
        {
            const int N = 9;
            float reach = math.length(t.Size) / N + 0.5f;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    var p = t.ToWorld(new float2(((i + 0.5f) / N - 0.5f) * t.Size.x, ((j + 0.5f) / N - 0.5f) * t.Size.y));
                    if (f.Eval(p, out _, out _, out _, out _) > -reach) return false;
                }
            return true;
        }
    }
}

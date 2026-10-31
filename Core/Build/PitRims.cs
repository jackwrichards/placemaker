// Pit rims (p1-ground): no fence round a hole, no pile hanging over its edge, for every type that digs one.
//
// The user (2026-10-30): "Any industrial thing that has like a big hole, sometimes it has a fence going around it. I get rid of
// that fence. It doesn't look good ... sometimes the rubble piles like stick out over the edge of the hole." Seen in the game
// runner on both quarry styles (a chain-link and a black fence along the rim), construction sites and the rundown types.
//
// There is no shared pit type: each layer digs its own hole through its own sculptor (SiteKit / YardKit cells, terrain shaper,
// basements). Every one of them ends as planned terraform, so this pass reads the hole from there, after every layer and the
// site harmony: wherever the plan lowers the ground by PitDepth or more is the hole; the build adds a smooth bank of
// RasterBank m around every terraform ring, and the sculptors' square cells reach past their rings, so the hole's edge zone is
// the dug ground grown by Reach. Then:
//  * fences, walls, hedges and bollards (Cutouts.IsBorder) inside that zone go: nets are clipped to the part outside it;
//  * piles (rubble, spoil, gravel, sand, ore heaps) and vehicle props whose footprint straddles the hole's edge (part dug, part
//    not, or a step of more than PileStep under them) go; piles wholly on the floor or wholly on the rim stay.
// Pure and deterministic.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class PitRims
    {
        /// <summary>A cut this deep (m) is a hole, not grading.</summary>
        public const float PitDepth = 1.5f;
        /// <summary>The hole's edge zone: the build's bank (TerraformRaster, 4 m) plus a raster cell and a fence's half width.</summary>
        public const float Reach = SiteHarmony.RasterBank + SiteHarmony.RasterCell + 0.5f;
        /// <summary>A pile with more than this change of ground height under its footprint hangs over an edge (m).</summary>
        public const float PileStep = 1.2f;
        private const float Cell = 2f;
        private const int MaxCells = 400000;

        private static readonly string[] PileWords = { "pile", "heap", "rubble", "spoil", "gravel", "sand", "aggregate", "debris", "skip" };

        /// <summary>Piles and heaps (by asset id or category) and Placemaker's vehicle props.</summary>
        public static bool IsPile(PlannedObject o)
        {
            if (o == null) return false;
            string id = o.Asset.Id ?? "", cat = o.Category ?? "";
            if (id.StartsWith(VehiclePropPrefix, StringComparison.Ordinal)) return true;
            foreach (var w in PileWords)
                if (id.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || cat.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Game/Assets/VehiclePropSystem's slot names (VehicleProps.Prefix).</summary>
        public const string VehiclePropPrefix = Placemaker.Core.Layers.Types.Yards.VehicleProps.Prefix;

        public sealed class Field
        {
            public float2 Origin; public float C; public int Nx, Ny;
            public float[] Delta;   // planned change of the ground (m), NaN outside every terraform ring
            public float[] Dist;    // distance to the nearest dug cell (m)
            public int Dug;

            private int Index(float2 p)
            {
                int i = (int)math.floor((p.x - Origin.x) / C), j = (int)math.floor((p.y - Origin.y) / C);
                return i < 0 || j < 0 || i >= Nx || j >= Ny ? -1 : j * Nx + i;
            }

            public float DistanceAt(float2 p) { int k = Index(p); return k < 0 ? float.MaxValue : Dist[k]; }
            public float DeltaAt(float2 p) { int k = Index(p); return k < 0 || float.IsNaN(Delta[k]) ? 0f : Delta[k]; }
            public bool InZone(float2 p) => DistanceAt(p) <= Reach;
            public bool IsDug(float2 p) => DeltaAt(p) <= -PitDepth;
        }

        /// <summary>The planned holes of a plan (null when nothing is dug <see cref="PitDepth"/> or more).</summary>
        public static Field Measure(ComposedPlan plan, Func<float2, float> ground)
        {
            if (plan?.Layers == null || ground == null) return null;
            var items = new List<(PlannedTerraform t, float2[] r, float2 mn, float2 mx)>();
            float2 bmin = new float2(float.MaxValue), bmax = new float2(float.MinValue);
            bool deep = false;
            foreach (var l in plan.Layers)
            {
                if (l?.Terraform == null) continue;
                foreach (var t in l.Terraform)
                {
                    if (t?.Ring == null || t.Ring.Length < 3 || t.Kind == TerraformKind.Raise) continue;
                    var r = Poly.To2D(t.Ring);
                    Poly.Bounds(r, out var mn, out var mx);
                    items.Add((t, r, mn, mx));
                    bmin = math.min(bmin, mn); bmax = math.max(bmax, mx);
                    if (!deep)
                    {
                        var c = (mn + mx) * 0.5f;
                        deep = TerraformSurface.TargetAt(t, c) - ground(c) <= -PitDepth;
                    }
                }
            }
            if (!deep) return null;
            bmin -= Reach + Cell; bmax += Reach + Cell;
            var size = bmax - bmin;
            float cell = Cell;
            while ((size.x / cell) * (size.y / cell) > MaxCells) cell *= 1.25f;
            var f = new Field { Origin = bmin, C = cell, Nx = math.max(1, (int)math.ceil(size.x / cell)), Ny = math.max(1, (int)math.ceil(size.y / cell)) };
            int n = f.Nx * f.Ny;
            f.Delta = new float[n];
            for (int k = 0; k < n; k++) f.Delta[k] = float.NaN;
            var groundAt = new float[n];
            for (int k = 0; k < n; k++) groundAt[k] = float.NaN;
            // in plan order: the last ring covering a cell wins (as the build's raster and TerrainHeat)
            foreach (var (t, r, mn, mx) in items)
            {
                int i0 = math.max(0, (int)math.floor((mn.x - bmin.x) / cell)), i1 = math.min(f.Nx - 1, (int)math.floor((mx.x - bmin.x) / cell));
                int j0 = math.max(0, (int)math.floor((mn.y - bmin.y) / cell)), j1 = math.min(f.Ny - 1, (int)math.floor((mx.y - bmin.y) / cell));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        var p = bmin + (new float2(i, j) + 0.5f) * cell;
                        if (!Poly.Contains(r, p)) continue;
                        int k = j * f.Nx + i;
                        if (float.IsNaN(groundAt[k])) groundAt[k] = ground(p);
                        f.Delta[k] = math.min(0f, TerraformSurface.TargetAt(t, p) - groundAt[k]);
                    }
            }
            // distance to the dug cells (two-pass chamfer)
            f.Dist = new float[n];
            const float big = 1e9f;
            for (int k = 0; k < n; k++)
            {
                bool dug = f.Delta[k] <= -PitDepth;
                if (dug) f.Dug++;
                f.Dist[k] = dug ? 0f : big;
            }
            if (f.Dug == 0) return null;
            float d1 = cell, d2 = cell * 1.41421356f;
            for (int j = 0; j < f.Ny; j++)
                for (int i = 0; i < f.Nx; i++)
                {
                    int k = j * f.Nx + i; float v = f.Dist[k];
                    if (i > 0) v = math.min(v, f.Dist[k - 1] + d1);
                    if (j > 0)
                    {
                        v = math.min(v, f.Dist[k - f.Nx] + d1);
                        if (i > 0) v = math.min(v, f.Dist[k - f.Nx - 1] + d2);
                        if (i < f.Nx - 1) v = math.min(v, f.Dist[k - f.Nx + 1] + d2);
                    }
                    f.Dist[k] = v;
                }
            for (int j = f.Ny - 1; j >= 0; j--)
                for (int i = f.Nx - 1; i >= 0; i--)
                {
                    int k = j * f.Nx + i; float v = f.Dist[k];
                    if (i < f.Nx - 1) v = math.min(v, f.Dist[k + 1] + d1);
                    if (j < f.Ny - 1)
                    {
                        v = math.min(v, f.Dist[k + f.Nx] + d1);
                        if (i < f.Nx - 1) v = math.min(v, f.Dist[k + f.Nx + 1] + d2);
                        if (i > 0) v = math.min(v, f.Dist[k + f.Nx - 1] + d2);
                    }
                    f.Dist[k] = v;
                }
            return f;
        }

        /// <summary>Whether a pile at p with footprint radius r straddles the hole's edge (part dug, part not, or a step under it).</summary>
        public static bool Straddles(Field f, float2 p, float r)
        {
            if (f.DistanceAt(p) > r + Reach) return false;
            bool dug = false, dry = false;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int s = -1; s < 12; s++)
            {
                float2 q = s < 0 ? p : p + r * new float2(math.cos(s * math.PI / 6f), math.sin(s * math.PI / 6f));
                float d = f.DeltaAt(q);
                if (d <= -PitDepth) dug = true; else dry = true;
                lo = math.min(lo, d); hi = math.max(hi, d);
            }
            return (dug && dry) || hi - lo > PileStep;
        }

        /// <summary>Clears the hole's edges of every layer; returns how many fence pieces and piles were cut or removed.</summary>
        public static int Apply(ComposedPlan plan, SiteContext site)
        {
            var f = Measure(plan, site?.HeightAt);
            if (f == null) return 0;
            int fences = 0, piles = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                fences += l.Objects.RemoveAll(o => Layers.Common.Cutouts.IsBorder(o) && f.InZone(o.Position.xz));
                piles += l.Objects.RemoveAll(o => IsPile(o) && Straddles(f, o.Position.xz, SiteHarmony.FootprintRadius(o, site)));
                fences += Layers.Common.Cutouts.ClipBorderNets(l, (p, pad) => f.InZone(p));
            }
            if (plan.Layers.Count > 0 && plan.Layers[0] != null && fences + piles > 0)
            {
                plan.Layers[0].Facts["pitFencesCut"] = fences;
                plan.Layers[0].Facts["pitPilesRemoved"] = piles;
            }
            return fences + piles;
        }
    }
}

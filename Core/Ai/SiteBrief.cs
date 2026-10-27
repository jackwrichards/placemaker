// AI builder (docs/AI-BUILDER.md): the site brief, the selection and what is in and around it as text an LLM reads well.
// Local metres from the south-west corner of the selection's bounding box, x east, z north; heights above sea level.
// Pure: the game layer samples the ground and captures the roads and buildings, this only writes them down.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Ai
{
    /// <summary>A height (and water) grid over the selection's box plus a margin, cell centres at Origin + (i + 0.5, j + 0.5) · Cell (world).</summary>
    public sealed class BriefGround
    {
        public float2 Origin;
        public float Cell;
        public int Nx, Nz;
        public float[] Height;   // index i + j · Nx, metres above sea level
        public float[] Water;    // water depth, metres (null = dry everywhere)

        public float2 CellCentre(int i, int j) => Origin + new float2((i + 0.5f) * Cell, (j + 0.5f) * Cell);
        public float HeightAt(int i, int j) => Height[math.clamp(j, 0, Nz - 1) * Nx + math.clamp(i, 0, Nx - 1)];
        public float WaterAt(int i, int j) => Water == null ? 0f : Water[math.clamp(j, 0, Nz - 1) * Nx + math.clamp(i, 0, Nx - 1)];
    }

    public sealed class SiteBriefInput
    {
        public float2[] Ring;                 // the selection, world xz, counter-clockwise
        public BriefGround Ground;
        public List<NearbyNet> Nets = new List<NearbyNet>();
        public List<Obstacle> Buildings = new List<Obstacle>();
        public string Prompt = "";
        /// <summary>The map's ground paints, index 1..4 (null = unknown): what paint_ground layer N looks like.</summary>
        public string[] Paints;
        /// <summary>Metres per pixel of the map that goes with the brief (0 = no map).</summary>
        public float MapMetresPerPixel;
    }

    public static class SiteBrief
    {
        /// <summary>The selection's bounding box, world xz: the local frame's origin is its low corner.</summary>
        public static (float2 lo, float2 hi) Box(float2[] ring)
        {
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (var p in ring) { lo = math.min(lo, p); hi = math.max(hi, p); }
            return (lo, hi);
        }

        /// <summary>A ground grid's cell size and extent for a selection: about 3.5 m (the game's heightmap), coarser past
        /// <paramref name="maxCells"/> per side.</summary>
        public static (float2 origin, float cell, int nx, int nz) GroundGrid(float2[] ring, float margin, float cell = 3.5f, int maxCells = 120)
        {
            var (lo, hi) = Box(ring);
            lo -= margin; hi += margin;
            cell = math.max(cell, math.cmax(hi - lo) / maxCells);
            int nx = math.max(2, (int)math.ceil((hi.x - lo.x) / cell)), nz = math.max(2, (int)math.ceil((hi.y - lo.y) / cell));
            return (lo, cell, nx, nz);
        }

        private static string F(float v, int digits = 1) => Math.Round(v, digits).ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
        private static string P(float2 p) => "(" + F(p.x) + "," + F(p.y) + ")";

        public static string Write(SiteBriefInput s)
        {
            var sb = new StringBuilder();
            var (lo, hi) = Box(s.Ring);
            float2 L(float2 w) => w - lo;
            var size = hi - lo;
            var ring = s.Ring;
            float area = math.abs(Poly.SignedArea(ring));
            var g = s.Ground;

            sb.AppendLine("# Site brief");
            sb.AppendLine("Coordinates: metres, origin = the south-west corner of the selection's bounding box, x east, z north. Heights: metres above sea level.");
            sb.AppendLine();
            sb.AppendLine("## Selection");
            sb.AppendLine($"Bounding box {F(size.x)} x {F(size.y)} m, {ring.Length} corners, area {F(area, 0)} m².");
            sb.AppendLine("Outline (counter-clockwise): " + string.Join(" ", ring.Select(p => P(L(p)))));

            if (g != null)
            {
                sb.AppendLine();
                sb.AppendLine("## Ground");
                float gx0 = g.Origin.x - lo.x, gz0 = g.Origin.y - lo.y;
                var inside = new List<float>();
                var all = new List<float>();
                float steepest = 0f; float2 steepAt = 0;
                bool anyWater = false;
                for (int j = 0; j < g.Nz; j++)
                    for (int i = 0; i < g.Nx; i++)
                    {
                        float h = g.HeightAt(i, j);
                        all.Add(h);
                        var c = g.CellCentre(i, j);
                        if (Poly.Contains(ring, c))
                        {
                            inside.Add(h);
                            float dx = (g.HeightAt(i + 1, j) - g.HeightAt(i - 1, j)) / (2f * g.Cell), dz = (g.HeightAt(i, j + 1) - g.HeightAt(i, j - 1)) / (2f * g.Cell);
                            float slope = math.sqrt(dx * dx + dz * dz);
                            if (slope > steepest) { steepest = slope; steepAt = L(c); }
                        }
                        anyWater |= g.WaterAt(i, j) > 0.05f;
                    }
                if (inside.Count == 0) inside = all;
                inside.Sort();
                float baseH = (float)Math.Floor(all.Min());
                sb.AppendLine($"Inside the selection: lowest {F(inside[0])} m, highest {F(inside[inside.Count - 1])} m, median {F(inside[inside.Count / 2])} m; steepest {F(steepest * 100f, 0)} % near {P(steepAt)}.");
                sb.AppendLine($"Grid: {g.Nx} x {g.Nz} cells of {F(g.Cell, 2)} m, the selection's box plus a margin. Cell (col, row) centre = ({F(gx0)} + (col + 0.5) · {F(g.Cell, 2)}, {F(gz0)} + (row + 0.5) · {F(g.Cell, 2)}). Rows are listed north (top) to south.");
                sb.AppendLine();
                sb.AppendLine("Mask (# = inside the selection, . = outside):");
                for (int j = g.Nz - 1; j >= 0; j--)
                {
                    var row = new StringBuilder();
                    for (int i = 0; i < g.Nx; i++) row.Append(Poly.Contains(ring, g.CellCentre(i, j)) ? '#' : '.');
                    sb.AppendLine(row.ToString());
                }
                sb.AppendLine();
                sb.AppendLine($"Heights, metres above {F(baseH, 0)} m (add {F(baseH, 0)} for the height above sea level), one row per line, north to south:");
                for (int j = g.Nz - 1; j >= 0; j--)
                {
                    var row = new StringBuilder();
                    row.Append("r").Append(j.ToString(CultureInfo.InvariantCulture)).Append(':');
                    for (int i = 0; i < g.Nx; i++) row.Append(' ').Append(F(g.HeightAt(i, j) - baseH));
                    sb.AppendLine(row.ToString());
                }
                if (anyWater)
                {
                    sb.AppendLine();
                    sb.AppendLine("Water depth in metres (0 = dry), same grid, north to south:");
                    for (int j = g.Nz - 1; j >= 0; j--)
                    {
                        var row = new StringBuilder();
                        row.Append("r").Append(j.ToString(CultureInfo.InvariantCulture)).Append(':');
                        for (int i = 0; i < g.Nx; i++) row.Append(' ').Append(F(g.WaterAt(i, j)));
                        sb.AppendLine(row.ToString());
                    }
                }
                else sb.AppendLine("No water in or near the selection.");
            }

            sb.AppendLine();
            sb.AppendLine("## Roads, paths and tracks in and around the selection");
            if (s.Nets.Count == 0) sb.AppendLine("None.");
            int n = 0;
            foreach (var net in s.Nets)
            {
                var pts = new List<float2>();
                for (int k = 0; k <= 4; k++) pts.Add(L(Bez(net, k / 4f).xz));
                int inPts = pts.Count(p => Poly.Contains(ring, p + lo));
                string where = inPts == pts.Count ? "inside" : inPts > 0 ? "crosses the edge" : "outside";
                string kind = net.Category == NetCategory.Road ? (net.IsHighway ? "highway" : "road") : net.Category == NetCategory.Pathway ? "path" : net.Category == NetCategory.Track ? "track" : "other";
                var extra = new List<string>();
                if (net.IsOneWay) extra.Add("one-way");
                if (net.HasSidewalk) extra.Add("sidewalks");
                if (net.HasBikeLane) extra.Add("bike lane");
                if (net.SpeedLimit > 0) extra.Add(F(net.SpeedLimit, 0) + " km/h");
                sb.AppendLine($"N{++n}: {kind}, {F(net.Width)} m wide{(extra.Count > 0 ? ", " + string.Join(", ", extra) : "")}, {where}; along " +
                              string.Join(" ", pts.Select(P)) + $", height {F(net.A.y)} -> {F(net.D.y)} m.");
            }

            sb.AppendLine();
            sb.AppendLine("## Buildings and lots in and around the selection");
            if (s.Buildings.Count == 0) sb.AppendLine("None.");
            n = 0;
            foreach (var b in s.Buildings)
            {
                var c = (b.A + b.B + b.C + b.D) / 4f;
                float w = math.distance(b.A, b.B), d = math.distance(b.B, b.C);
                float deg = math.degrees(math.atan2(b.B.y - b.A.y, b.B.x - b.A.x));
                int inC = new[] { b.A, b.B, b.C, b.D }.Count(p => Poly.Contains(ring, p));
                string where = inC == 4 ? "inside" : inC > 0 ? "on the edge" : Poly.Contains(ring, c) ? "inside" : "outside";
                sb.AppendLine($"B{++n}: {F(w)} x {F(d)} m centred at {P(L(c))}, turned {F(deg, 0)}°, {where}; corners {P(L(b.A))} {P(L(b.B))} {P(L(b.C))} {P(L(b.D))}.");
            }

            sb.AppendLine();
            sb.AppendLine("## Ground paints of this map (paint_ground layer)");
            for (int i = 1; i <= 4; i++)
                sb.AppendLine($"{i}: {(s.Paints != null && i < s.Paints.Length && !string.IsNullOrEmpty(s.Paints[i]) ? s.Paints[i] : DefaultPaint(i))}");
            sb.AppendLine("0: erase (back to the map's natural grass, dirt and rock)");

            if (s.MapMetresPerPixel > 0f)
            {
                sb.AppendLine();
                sb.AppendLine("## The map picture");
                sb.AppendLine($"Top-down, north up, {F(s.MapMetresPerPixel, 2)} m per pixel, covering the ground grid's box. Ground coloured by height (blue low, red high) with hillshade and contour lines; " +
                              "outside the selection dimmed; the selection outlined in white; roads dark grey, paths tan, tracks brown; buildings and lots black; water blue. " +
                              "Grid lines every 10 m, darker every 50 m, labelled in local metres along the bottom (x) and the left (z).");
            }

            sb.AppendLine();
            sb.AppendLine("## What the player asked for");
            sb.AppendLine(string.IsNullOrWhiteSpace(s.Prompt) ? "(nothing written)" : s.Prompt.Trim());
            return sb.ToString();
        }

        public static string DefaultPaint(int i) => i == 1 ? "dry grass" : i == 2 ? "lush grass" : i == 3 ? "dirt" : i == 4 ? "sand" : "erase";

        public static float3 Bez(NearbyNet n, float t)
        {
            float u = 1f - t;
            return u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D;
        }
    }
}

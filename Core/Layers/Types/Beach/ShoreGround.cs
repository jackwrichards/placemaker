// Shore ground (s20): the terrain step every water type runs first.
//
//   survey     slope classes of the site (TerrainSurvey) and the bank kind along the shore (Shoreline.Kinds:
//              low / bank / cliff, from the real waterline's profile)
//   sculpt     one Sculpt over the land plus the foreshore (the strip out to and under the water); cliff faces
//              are locked (left natural: no terraform, paint, paths or props on them)
//   profile    targets as a function of the distance from the REAL waterline (dw), the arc length and the
//              original ground, then smoothing, earthwork limits and a fade to the locked border
//   level      pads where things stand (huts, kiosks, landings, plazas) and level corridors for walks
//   finish     grade relaxation, emission as sloped patches clipped to land ∪ foreshore, objects/nets resnapped
//
// Replaces ShoreTerrain.Shape (flat 6 m cells → terraces; playtests 30–34).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public sealed class ShoreGround
    {
        public const string CliffKey = "Placemaker.Shore.Issue.Cliff";
        public const string EarthworkKey = "Placemaker.Shore.Issue.Earthwork";
        /// <summary>Shores more than this share cliff get the cliff-top treatment instead of a beach / quay.</summary>
        public const float CliffShoreShare = 0.7f;

        public readonly LayerContext Ctx;
        public readonly Shoreline Shore;
        public readonly Sculpt Sculpt;
        public readonly TerrainSurvey Survey;
        public readonly float Level;
        public readonly bool HasLevel;
        /// <summary>Land ring + foreshore rings: where the sculpt may emit.</summary>
        public readonly List<float2[]> Clips = new List<float2[]>();
        public readonly List<float2[]> Foreshore = new List<float2[]>();
        /// <summary>Depth of the cliff zone kept natural (m from the water).</summary>
        public float CliffBand = 30f;
        public TerrainResult Result;
        private readonly float[] _dw, _along;

        private ShoreGround(LayerContext ctx, Shoreline shore, List<float2[]> foreshore, float cell)
        {
            Ctx = ctx; Shore = shore;
            Level = shore.WaterLevel;
            HasLevel = shore.LevelTrusted && math.isfinite(Level);
            Survey = TerrainSurvey.Of(ctx, shore.Land, 12f);
            Foreshore = foreshore ?? new List<float2[]>();
            Clips.Add(shore.Land.Ring);
            Clips.AddRange(Foreshore);
            // The cliff zone reaches as far as the cliffs' own face (to where the plateau flattens), not a fixed band.
            if (shore.CliffShare > 0f)
            {
                var faces = new List<float>();
                foreach (var run in Runs(k => k == ShoreKind.Cliff, 4f)) faces.Add(CliffTop.Setback(this, run.x, run.y));
                if (faces.Count > 0) { faces.Sort(); CliffBand = faces[faces.Count - 1] + 2f; }
            }
            Sculpt = Sculpt.Over(ctx, shore.Land, Foreshore, cell);
            // One pass over the grid: arc length, distance from the water and the cliff mask per node (cached: layout
            // asks OnCliff / FromWater thousands of times).
            _dw = new float[Sculpt.Count];
            _along = new float[Sculpt.Count];
            _cliff = new bool[Sculpt.Count];
            for (int k = 0; k < Sculpt.Count; k++)
            {
                var p = Sculpt.Pos(k);
                // One nearest-point query per node: distance and side (seaward of the nearest shore point = negative).
                var q = shore.Nearest(p, out var qin, out float along);
                _along[k] = along;
                float dd = math.distance(p, q);
                _dw[k] = (math.dot(p - q, qin) >= -0.01f ? dd : -dd) + shore.GapAt(along);
                _cliff[k] = CliffAt(p, along, _dw[k]);
                if (_cliff[k]) Sculpt.Free[k] = false;
            }
        }

        private readonly bool[] _cliff;
        private List<float2> _cliffRuns;

        private Func<float2, bool> _wall;

        /// <summary>
        /// (p2-beaches) Extra natural ground the layer cut itself (a pocket beach's rock walls): cliff, locked, nothing placed
        /// there; and ground kept as carved but not cliff (the gully floor its path climbs).
        /// </summary>
        public void Lock(Func<float2, bool> wall, Func<float2, bool> keep)
        {
            _wall = wall;
            for (int k = 0; k < Sculpt.Count; k++)
            {
                var p = Sculpt.Pos(k);
                if (wall(p)) { _cliff[k] = true; Sculpt.Free[k] = false; }
                else if (keep != null && keep(p)) Sculpt.Free[k] = false;
            }
        }

        private bool CliffAt(float2 p, float along, float dw)
        {
            if (_wall != null && _wall(p)) return true;
            if (Survey.GradeAt(p) >= 0.6f) return true;
            if (Shore.Kinds == null || Shore.CliffShare <= 0f) return false;
            // (s30) Only a real cliff stretch (≥ 16 m of cliff stations): one misread station at the shore's end locked a small island
            // of nodes in the middle of a graded bank, and the field stepped round it.
            if (dw >= CliffBand) return false;
            if (_cliffRuns == null) _cliffRuns = Runs(k => k == ShoreKind.Cliff, 16f);
            foreach (var r in _cliffRuns) if (along >= r.x - 2f && along <= r.y + 2f) return true;
            return false;
        }

        /// <summary>Nearest grid node of p, or −1 outside the grid.</summary>
        private int NodeOf(float2 p)
        {
            int i = (int)math.round((p.x - Sculpt.Origin.x) / Sculpt.Cell), j = (int)math.round((p.y - Sculpt.Origin.y) / Sculpt.Cell);
            if (i < 0 || j < 0 || i >= Sculpt.Nx || j >= Sculpt.Ny) return -1;
            return j * Sculpt.Nx + i;
        }

        /// <summary>(s30) Shore arc length of the sculpt node k (cached).</summary>
        public float AlongOf(int k) => _along[k];

        /// <summary>Distance from the real waterline (bilinear on the cached grid; exact outside it).</summary>
        public float DwAt(float2 p)
        {
            float v = Sculpt.Sample(_dw, p);
            return math.isfinite(v) ? v : Shore.FromWater(p);
        }

        /// <param name="intoWater">How far past the real waterline the ground (and sand) is shaped (m); 0 = no foreshore.</param>
        public static ShoreGround Create(LayerContext ctx, Shoreline shore, float intoWater, float cell = Sculpt.DefaultCell)
        {
            var fore = intoWater > 0f ? shore.Foreshore(Shoreline.MaxGap, intoWater, k => shore.Kinds == null || shore.Kinds[k] != ShoreKind.Cliff) : new List<float2[]>();
            return new ShoreGround(ctx, shore, fore, cell);
        }

        /// <summary>The cliff zone (a cliff station's face out to where the plateau flattens, or any ground steeper than 60 %): left natural.</summary>
        public bool OnCliff(float2 p)
        {
            int k = NodeOf(p);
            if (k >= 0) return _cliff[k];
            Shore.Nearest(p, out _, out float along);
            return CliffAt(p, along, Shore.FromWater(p));
        }

        /// <summary>Sets the target of every free node: (dw from the water, arc length, original ground, position) → target (NaN keeps).</summary>
        public void Profile(Func<float, float, float, float2, float> target)
        {
            for (int k = 0; k < Sculpt.Count; k++)
            {
                if (!Sculpt.Free[k]) continue;
                float t = target(_dw[k], _along[k], Sculpt.Base[k], Sculpt.Pos(k));
                if (math.isfinite(t)) Sculpt.Target[k] = t;
            }
        }

        /// <summary>
        /// Height the shore profile starts from at arc length s: the water level when the terrain agrees with it, else the
        /// ground at the waterline point (fixtures, stale snapshots).
        /// </summary>
        public float Ref(float along) => HasLevel ? Level : Ctx.H(Shore.WaterAt(along, out _));

        /// <summary>Planned ground so far (before emission).</summary>
        public float H(float2 p) => Sculpt.HeightAt(p);

        /// <summary>A prop of radius r can stand at p: not on the cliff zone, planned ground not steeper than maxGrade.</summary>
        public bool CanStand(float2 p, float r, float maxGrade = TerrainSurvey.FurnitureGrade)
        {
            if (OnCliff(p)) return false;
            return TerrainSurvey.Grade(H, p, math.max(0.8f, r)) <= maxGrade;
        }

        /// <summary>
        /// Makes a level spot for something of radius r at c: already level → true; uneven by at most
        /// <paramref name="maxRelief"/> → a pad at the mean height blending over 3 m → true; else false (too steep: skip it).
        /// </summary>
        public bool MakeLevel(float2 c, float r, float maxRelief, out float height)
        {
            height = H(c);
            if (OnCliff(c)) return false;
            float relief = TerrainSurvey.Relief(H, c, r + 0.5f);
            if (relief <= 0.2f) return true;
            if (relief > maxRelief) return false;
            float sum = H(c); int n = 1;
            for (int a = 0; a < 8; a++)
            {
                float ang = a * math.PI / 4f;
                sum += H(c + new float2(math.cos(ang), math.sin(ang)) * (r + 0.5f)); n++;
            }
            height = sum / n;
            if (HasLevel) height = math.max(height, Level + 0.4f);
            Sculpt.PadDisc(c, r + 0.6f, height, 3f);
            return true;
        }

        /// <summary>A level pad under a polygon (landing, plaza) at its mean height (above the water).</summary>
        public float PadPolygon(IReadOnlyList<float2> ring, float blend = 3f)
        {
            float sum = 0f;
            foreach (var q in ring) sum += H(q);
            float h = sum / ring.Count;
            if (HasLevel) h = math.max(h, Level + 0.3f);
            Sculpt.Pad(ring, h, blend);
            return h;
        }

        /// <summary>
        /// The level profile of a walk along <paramref name="line"/>: the planned ground (mean across the walk), smoothed
        /// along it (±<paramref name="smoothing"/> m), its grade limited to <paramref name="maxGrade"/>, kept above the water.
        /// </summary>
        public Func<float, float> ProfileAlong(float2[] line, float halfWidth, float smoothing = 14f, float maxGrade = TerrainSurvey.WalkGrade, float minAboveWater = 0.6f)
        {
            int n = line.Length;
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + math.distance(line[i - 1], line[i]);
            var h = new float[n];
            for (int i = 0; i < n; i++)
            {
                var t = i + 1 < n ? math.normalizesafe(line[i + 1] - line[i]) : math.normalizesafe(line[i] - line[math.max(0, i - 1)]);
                var nrm = new float2(-t.y, t.x) * halfWidth;
                // Median across the walk (centre and both edges): mid-slope on a bank, and a cliff edge beside a cliff-top walk
                // does not drag it into a trench.
                float c0 = H(line[i]), c1 = H(line[i] + nrm), c2 = H(line[i] - nrm);
                h[i] = math.max(math.min(c0, c1), math.min(math.max(c0, c1), c2));
            }
            var sm = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int j = 0; j < n; j++)
                {
                    float d = math.abs(along[j] - along[i]);
                    if (d > smoothing) continue;
                    float wgt = 1f - d / (smoothing + 1e-3f);
                    sum += h[j] * wgt; wsum += wgt;
                }
                sm[i] = wsum > 0f ? sum / wsum : h[i];
                if (HasLevel) sm[i] = math.max(sm[i], Level + minAboveWater);
            }
            // Grade limit both ways.
            for (int i = 1; i < n; i++) sm[i] = math.clamp(sm[i], sm[i - 1] - maxGrade * (along[i] - along[i - 1]), sm[i - 1] + maxGrade * (along[i] - along[i - 1]));
            for (int i = n - 2; i >= 0; i--) sm[i] = math.clamp(sm[i], sm[i + 1] - maxGrade * (along[i + 1] - along[i]), sm[i + 1] + maxGrade * (along[i + 1] - along[i]));
            return s =>
            {
                if (s <= 0f) return sm[0];
                for (int i = 1; i < n; i++)
                    if (s <= along[i]) return math.lerp(sm[i - 1], sm[i], (s - along[i - 1]) / math.max(1e-4f, along[i] - along[i - 1]));
                return sm[n - 1];
            };
        }

        private readonly List<(float2[] line, Func<float, float> profile, float hw, float blend)> _benches = new List<(float2[], Func<float, float>, float, float)>();

        /// <summary>
        /// (s30) Walk benches are laid again just before emission: pads, ramps, smoothing and grade relaxation placed after a
        /// walk tilted the band under it (a net flattens its own width, so a tilted band left a lip along its edge).
        /// </summary>
        private void ReapplyBenches()
        {
            foreach (var (line, profile, hw, blend) in _benches)
            {
                // Its banks as wide as the height it now has to make up (≤ ~30 %), never a lip.
                float dev = 0f, s = 0f;
                for (int i = 0; i < line.Length; i++)
                {
                    if (i > 0) s += math.distance(line[i - 1], line[i]);
                    var t = math.normalizesafe(line[math.min(i + 1, line.Length - 1)] - line[math.max(0, i - 1)]);
                    var nn = new float2(-t.y, t.x) * (hw + blend);
                    float h = profile(s);
                    dev = math.max(dev, math.max(math.abs(Sculpt.HeightAt(line[i] + nn) - h), math.abs(Sculpt.HeightAt(line[i] - nn) - h)));
                }
                Sculpt.Corridor(line, profile, hw, math.max(blend, dev / 0.18f));
            }
        }

        /// <summary>A walk's level bench: <see cref="ProfileAlong"/>, flattened across the walk, blended back over <paramref name="blend"/> m.</summary>
        public Func<float, float> Bench(float2[] line, float halfWidth, float blend, float smoothing = 14f, float maxGrade = TerrainSurvey.WalkGrade, float minAboveWater = 0.6f)
        {
            var profile = ProfileAlong(line, halfWidth, smoothing, maxGrade, minAboveWater);
            Sculpt.Corridor(line, profile, halfWidth, blend);
            _benches.Add((line, profile, halfWidth, blend));
            return profile;
        }

        /// <summary>
        /// The walkable pieces of a walk line: a walk must end where its level would meet ground it may not change (the
        /// border away from the water, a cliff) more steeply than ~25 %; it is trimmed there (and split around a cliff)
        /// instead of climbing at its ends. Each piece gets its own level bench; returns (line, profile) pairs.
        /// </summary>
        public List<(float2[] line, Func<float, float> profile)> Walk(float2[] line, float halfWidth, float blend, float smoothing, float maxGrade, float minAboveWater, float minLength,
            bool cliffTop = false)
        {
            var result = new List<(float2[], Func<float, float>)>();
            if (line == null || line.Length < 2) return result;
            EnsureLocked(cliffTop);
            var profile = ProfileAlong(line, halfWidth, smoothing, maxGrade, minAboveWater);
            var run = new List<float2>();
            float s = 0f;
            void Flush()
            {
                if (run.Count >= 2 && Shoreline.LengthOf(run.ToArray()) >= minLength)
                {
                    var piece = run.ToArray();
                    result.Add((piece, Bench(piece, halfWidth, blend, smoothing, maxGrade, minAboveWater)));
                }
                run.Clear();
            }
            for (int i = 0; i < line.Length; i++)
            {
                if (i > 0) s += math.distance(line[i - 1], line[i]);
                var p = line[i];
                float d = Sculpt.Sample(_lockedDist, p), hl = Sculpt.Sample(_lockedH, p);
                float dh = math.abs(profile(s) - hl);
                // (the bench reaches halfWidth + blend from the line, round its ends too; the border cap allows ~25 % from a cell in)
                bool ok = !math.isfinite(d) || dh < 0.3f || d >= halfWidth + blend + Sculpt.Cell + dh / 0.23f;
                if (ok) run.Add(p); else Flush();
            }
            Flush();
            return result;
        }

        private float[] _lockedDist, _lockedH;
        private bool _lockedIgnoresCliffs;

        /// <summary>Per node: distance to the nearest locked node and that node's height (two-pass chamfer carrying the height).</summary>
        private void EnsureLocked(bool ignoreCliffs)
        {
            if (_lockedDist != null && _lockedIgnoresCliffs == ignoreCliffs) return;
            _lockedIgnoresCliffs = ignoreCliffs;
            int nx = Sculpt.Nx, ny = Sculpt.Ny, n = Sculpt.Count;
            var d = new float[n]; var h = new float[n];
            // Seeds: locked DRY ground (the outline away from the water, cliff faces: a cliff-top walk is beside their top edge,
            // at its own level). The deep water beyond the foreshore is locked too, but a walk need not blend to the sea bed.
            for (int k = 0; k < n; k++) { d[k] = Sculpt.Free[k] || (ignoreCliffs && _cliff[k]) || Shore.IsWet(Sculpt.Pos(k)) ? 1e9f : 0f; h[k] = Sculpt.Target[k]; }
            float a = Sculpt.Cell, b = Sculpt.Cell * 1.41421356f;
            void Relax(int k, int m, float c) { if (d[m] + c < d[k]) { d[k] = d[m] + c; h[k] = h[m]; } }
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int k = j * nx + i;
                    if (i > 0) Relax(k, k - 1, a);
                    if (j > 0) Relax(k, k - nx, a);
                    if (i > 0 && j > 0) Relax(k, k - nx - 1, b);
                    if (i < nx - 1 && j > 0) Relax(k, k - nx + 1, b);
                }
            for (int j = ny - 1; j >= 0; j--)
                for (int i = nx - 1; i >= 0; i--)
                {
                    int k = j * nx + i;
                    if (i < nx - 1) Relax(k, k + 1, a);
                    if (j < ny - 1) Relax(k, k + nx, a);
                    if (i < nx - 1 && j < ny - 1) Relax(k, k + nx + 1, b);
                    if (i > 0 && j < ny - 1) Relax(k, k + nx - 1, b);
                }
            _lockedDist = d; _lockedH = h;
        }

        /// <summary>
        /// Settles the shaped profile before anything is laid out on it: smoothing, earthwork limits, the fade to the
        /// locked border and grade relaxation — so what the layout reads (H, CanStand) is what will be built.
        /// </summary>
        public void Settle(float maxCut, float maxFill, float maxGrade, float fade)
        {
            Sculpt.Smooth(2, 0.45f);
            Sculpt.LimitEarthwork(maxCut, maxFill);
            Sculpt.FadeOut(fade);
            Sculpt.LimitGrade(maxGrade, 24, true);
            _lockedDist = null;
        }

        /// <summary>A straight ramp from a (height ha) to b (height hb): a level-across strip with a constant grade.</summary>
        public void Ramp(float2 a, float ha, float2 b, float hb, float halfWidth, float blend)
        {
            float len = math.max(0.1f, math.distance(a, b));
            Func<float, float> rp = s => math.lerp(ha, hb, math.saturate(s / len));
            Sculpt.Corridor(new[] { a, b }, rp, halfWidth, blend);
            _benches.Add((new[] { a, b }, rp, halfWidth, blend));
        }


        /// <summary>
        /// Joins an entrance to the walk spine (GateApproach route) as ONE constant-grade ramp: the strip along the route
        /// is levelled across and graded linearly from the ground at the gate to the walk's planned height at the
        /// junction, so a connector never runs over humps or makes cones. Returns the route (null when none).
        /// </summary>
        public List<float2> Connect(Occupancy occ, float2 gate, IReadOnlyList<(float2 a, float2 b)> spine, NetRole role, AssetRef asset, float landing = 0f)
        {
            var ctx = Ctx;
            var route = GateApproach.Route(ctx.LayerRegion, gate, spine, Leisure.LeisureLayer.ApproachLength(ctx));
            if (route == null || route.Count < 2) return null;
            float hw = Composer.DefaultHalfWidth(role);
            float ha = Ctx.H(route[0]);
            float Len(List<float2> r) { float l = 0f; for (int i = 1; i < r.Count; i++) l += math.distance(r[i - 1], r[i]); return l; }
            float len = Len(route);
            // A connector is a short way in, not a tour across the park: when the approach found nothing near, go straight to
            // the nearest walk point instead (or not at all).
            float nearest = float.MaxValue; float2 nearQ = gate;
            foreach (var (a, b) in spine) { var c = SegmentIndex.ClosestPoint(gate, a, b); float dd = math.distance(c, gate); if (dd < nearest) { nearest = dd; nearQ = c; } }
            if (len > nearest * 1.6f + 20f)
            {
                if (ctx.LayerRegion.CrossesBoundary(gate + (nearQ - gate) * 0.02f, nearQ, true)) return null;
                route = new List<float2> { gate, nearQ };
                len = nearest;
            }
            float hb = H(route[route.Count - 1]);
            // Too steep for one straight ramp (a bank between the street and the promenade): keep the straight approach,
            // then ONE diagonal ramp down the slope to a spine point far enough along for ≤ 12 % — no zig-zags.
            if (len > 1f && math.abs(ha - hb) / len > TerrainSurvey.RampGrade && route.Count >= 2)
            {
                var from = route.Count > 2 ? route[route.Count - 2] : route[0];
                float hf = route.Count > 2 ? math.lerp(ha, hb, Len(route.GetRange(0, route.Count - 1)) / len) : ha;
                float best = float.MaxValue; float2 bestQ = default; bool found = false;
                foreach (var (a, b) in spine)
                    for (int k = 0; k <= 4; k++)
                    {
                        var q = math.lerp(a, b, k / 4f);
                        float need = math.abs(hf - H(q)) / (TerrainSurvey.RampGrade * 0.9f);
                        float l = math.distance(from, q);
                        if (l < need || l > need * 1.3f + 4f) continue;
                        // A diagonal, not a detour: within ~50° of the straight way in.
                        var straight = math.normalizesafe(route[route.Count - 1] - from);
                        if (math.dot(math.normalizesafe(q - from), straight) < 0.64f) continue;
                        if (ctx.LayerRegion.CrossesBoundary(from, q, true) || !ctx.LayerRegion.Contains(math.lerp(from, q, 0.5f))) continue;
                        if (l < best) { best = l; bestQ = q; found = true; }
                    }
                if (found)
                {
                    var r2 = route.Count > 2 ? route.GetRange(0, route.Count - 1) : new List<float2> { route[0] };
                    r2.Add(bestQ);
                    route = r2;
                    len = Len(route);
                    hb = H(bestQ);
                }
            }
            if (len > 1f)
            {
                // The ramp follows the planned ground smoothed along the way (≤ 12 %), pinned to both ends; where it must cut or
                // fill, its banks widen with the depth (a broad graded cutting, never a crease).
                var pts = Poly.ResamplePolyline(route, 2f);
                var along = ProfileAlong(pts, hw, 10f, TerrainSurvey.RampGrade, 0.3f);
                float total = Shoreline.LengthOf(pts);
                // (s30) The last <landing> metres (across a promenade band) stay level at the walk's height: the ramp ends at the
                // band's edge instead of tilting the band beside the junction.
                float rampLen = math.max(1f, total - math.min(landing, total * 0.6f));
                float a0 = along(0f), a1 = along(rampLen);
                Func<float, float> prof = s => s >= rampLen ? hb : along(s) + math.lerp(ha - a0, hb - a1, math.saturate(s / rampLen));
                float dev = 0f, run = 0f;
                for (int i = 0; i < pts.Length; i++) { if (i > 0) run += math.distance(pts[i - 1], pts[i]); dev = math.max(dev, math.abs(prof(run) - H(pts[i]))); }
                // (s30) On a cross slope the level strip stands proud on one side and cut on the other: the banks widen with that too.
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    var t = math.normalizesafe(pts[i + 1] - pts[i]); var nn = new float2(-t.y, t.x) * (hw + 1.5f);
                    dev = math.max(dev, math.abs(H(pts[i] + nn) - H(pts[i] - nn)));   // (a full cross difference: the banks ≤ ~15 % over the natural slope)
                }
                // (s30) Level across well past the path edges: on the 2–4 m node grid a strip only 0.4 m wider than the path kept a
                // cross slope under it, and a net flattens its own width, leaving a lip along its edge.
                Sculpt.Corridor(pts, prof, hw + 1.5f, math.clamp(dev / 0.16f, 4f, 20f));
            }
            for (int i = 1; i < route.Count; i++) LeisureKit.Path(ctx, occ, route[i - 1], route[i], role, asset);
            return route;
        }


        /// <summary>
        /// Safety net after emission: furniture (benches, lamps, bins, umbrellas, kiosks…) whose final ground turned out
        /// steeper than ~15 % (smoothing, border caps) is dropped rather than built tilted. Plants and rocks may stand on slopes.
        /// </summary>
        private void DropTiltedFurniture()
        {
            int dropped = 0;
            for (int i = Ctx.Plan.Objects.Count - 1; i >= 0; i--)
            {
                var o = Ctx.Plan.Objects[i];
                if (o == null || o.Role == ObjectRole.Tree || o.Role == ObjectRole.Bush || o.Role == ObjectRole.Decal || o.Category == AssetCategories.Rock) continue;
                if (TerrainSurvey.Grade(Ctx.G, o.Position.xz, 1f) <= 0.15f) continue;
                Ctx.Plan.Objects.RemoveAt(i);
                dropped++;
            }
            if (dropped > 0) Ctx.Plan.Facts["droppedTilted"] = dropped;
        }

        /// <summary>
        /// Every walk reachable from an entrance: each gate joins its nearest walk; a walk no gate reached (a beach walk
        /// beside a cliff-top walk) gets its own connector from the nearest gate when that can stay under ~15 %.
        /// </summary>
        public void ConnectAll(Occupancy occ, IReadOnlyList<float2> gates, IReadOnlyList<float2[]> lines, NetRole role, AssetRef asset, float landing = 0f)
        {
            if (gates.Count == 0 || lines.Count == 0) return;
            var spines = new List<List<(float2, float2)>>();
            foreach (var l in lines)
            {
                var sp = new List<(float2, float2)>();
                for (int i = 1; i < l.Length; i++) sp.Add((l[i - 1], l[i]));
                spines.Add(sp);
            }
            float Dist(float2 p, List<(float2 a, float2 b)> sp, out float2 at)
            {
                float best = float.MaxValue; at = p;
                foreach (var (a, b) in sp) { var c = SegmentIndex.ClosestPoint(p, a, b); float d = math.distance(c, p); if (d < best) { best = d; at = c; } }
                return best;
            }
            var reached = new bool[lines.Count];
            var all = new List<(float2, float2)>();
            foreach (var sp in spines) all.AddRange(sp);
            var routes = new List<List<float2>>();
            foreach (var gt in gates)
            {
                int bi = -1; float bd = float.MaxValue;
                for (int i = 0; i < spines.Count; i++) { float d = Dist(gt, spines[i], out _); if (d < bd) { bd = d; bi = i; } }
                var r = bi >= 0 ? Connect(occ, gt, all, role, asset, landing) : null;
                if (r == null) continue;
                reached[bi] = true;
                routes.Add(r);
            }
            // A walk no gate reached: one spur from the nearest point of an entrance connector (it joins that path), a
            // straight graded ramp of ≤ 12 % or nothing — never a second path doubling a connector from the same gate.
            float hw = Composer.DefaultHalfWidth(role);
            for (int i = 0; i < lines.Count; i++)
            {
                if (reached[i] || routes.Count == 0) continue;
                float best = float.MaxValue; float2 from = default, to = default;
                foreach (var r in routes)
                    for (int k = 1; k < r.Count; k++)
                        for (int s = 0; s <= 8; s++)
                        {
                            var p = math.lerp(r[k - 1], r[k], s / 8f);
                            if (Ctx.LayerRegion.BoundaryDistance(p, 6f) < 5f) continue;   // not off the gate itself
                            float d = Dist(p, spines[i], out var a);
                            if (d < best) { best = d; from = p; to = a; }
                        }
                if (best > 300f || best < 2f) continue;
                float hf = H(from), ht = H(to);
                if (math.abs(hf - ht) / best > TerrainSurvey.RampGrade) continue;
                if (Ctx.LayerRegion.CrossesBoundary(from, to, true)) continue;
                Func<float, float> sp = s => math.lerp(hf, ht, math.saturate(s / best));
                Sculpt.Corridor(new[] { from, to }, sp, hw + 1.5f, 5f);
                LeisureKit.Path(Ctx, occ, from, to, role, asset);
                reached[i] = true;
            }
        }
        /// <summary>Arc-length runs [from, to] (≥ minLength) of the shore whose kind passes <paramref name="ok"/>.</summary>
        public List<float2> Runs(Func<ShoreKind, bool> ok, float minLength = 8f)
        {
            var runs = new List<float2>();
            float start = float.NaN;
            float step = 2f;
            for (float s = 0f; s <= Shore.Length + 1e-3f; s += step)
            {
                bool good = ok(Shore.KindAt(math.min(s, Shore.Length)));
                if (good && float.IsNaN(start)) start = s;
                if ((!good || s + step > Shore.Length + 1e-3f) && !float.IsNaN(start))
                {
                    float end = good ? Shore.Length : s - step * 0.5f;
                    if (end - start >= minLength) runs.Add(new float2(start, end));
                    start = float.NaN;
                }
            }
            return runs;
        }
        /// <summary>
        /// (s30) Emits a sculpt whose targets are ONE designed field (ShoreWalk): no smoothing, grade relaxation or earthwork
        /// clamps after the layout was placed on it (each of those bent the level band under a walk into a cross slope and
        /// left pads and benches at different heights from the ground beside them — the in-game curtains). Only the cap to
        /// the locked border is re-applied (pads placed later may have pushed against it).
        /// </summary>
        public TerrainResult FinishField(float borderGrade)
        {
            Sculpt.CapToBorder(borderGrade);
            ReapplyBenches();
            var e = Sculpt.Measure();
            Ctx.Plan.Facts["earthMaxCut"] = e.MaxCut;
            Ctx.Plan.Facts["earthMaxFill"] = e.MaxFill;
            Ctx.Plan.Facts["earthCut"] = e.CutVolume;
            Ctx.Plan.Facts["earthFill"] = e.FillVolume;
            Ctx.Plan.Facts["terrainRoughness"] = Sculpt.Roughness();
            Result = Sculpt.Emit(Ctx, Clips, Sculpt.MinChange, 2);
            Sculpt.Resnap(Ctx);
            DropTiltedFurniture();
            return Result;
        }

        /// <summary>
        /// Emits the sculpt: earthwork limits (an Info issue when they bound the design), grade relaxation, the sloped
        /// patches clipped to land ∪ foreshore, then every object/net/surface of the plan onto the new ground.
        /// </summary>
        public TerrainResult Finish(float maxCut, float maxFill, float maxGrade, float borderGrade = 0.25f)
        {
            // Round the creases where pads, benches and ramps meet the slope (a 4 m bilinear mesh kinks at cell edges),
            // relax grades, then hold the earthwork limits (last, so nothing pushes past them).
            Sculpt.Smooth(2, 0.3f);
            Sculpt.CapToBorder(borderGrade);
            Sculpt.LimitGrade(maxGrade);
            if (Sculpt.LimitEarthwork(maxCut, maxFill)) Ctx.Issue(IssueSeverity.Info, EarthworkKey, (int)math.round(maxCut), (int)math.round(maxFill));
            ReapplyBenches();
            Sculpt.CapToBorder(borderGrade);
            var e = Sculpt.Measure();
            Ctx.Plan.Facts["earthMaxCut"] = e.MaxCut;
            Ctx.Plan.Facts["earthMaxFill"] = e.MaxFill;
            Ctx.Plan.Facts["earthCut"] = e.CutVolume;
            Ctx.Plan.Facts["earthFill"] = e.FillVolume;
            Ctx.Plan.Facts["terrainRoughness"] = Sculpt.Roughness();
            Result = Sculpt.Emit(Ctx, Clips, Sculpt.MinChange, 2);
            Sculpt.Resnap(Ctx);
            DropTiltedFurniture();
            return Result;
        }
    }
}

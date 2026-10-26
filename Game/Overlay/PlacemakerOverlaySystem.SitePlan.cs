// Placemaker — the sketch as a site plan (setting Sketch style: Site plan; sketch study, docs/sketch-study): the plan drawn as
// what gets built, from the tool's Core SitePlan, the same list the panel's top view draws. Roads and paths are surfaces at
// their real width with round joins, so a junction is one surface; stalls are the painted lines; trees are crowns at their
// real size with a soft shadow. Every size is in metres. Under the Real preview only the ground stays (the game draws planned
// surfaces as outlines only, so without it Real shows no asphalt and no new roads); the ghost shows the rest.
//
// Speed (the user, 25 Oct: "I'm lagging pretty bad in sketch view" on a 3,573-space car park): everything but the roads and
// paths is a filled mesh built once per site plan (FilledPreviewSystem: one draw call per layer, as ParkingLotTool draws its
// plants and bay lines), not thousands of overlay lines and circles every frame. Showing, hiding and the hover highlight only
// change a layer's colour.
//
// Hover (the user: "just blink or highlight the thing that you select ... not hide everything but it"): the hovered legend
// row blinks in the highlight colour; nothing else is hidden.

using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using Placemaker.Core.Build;
using Placemaker.Core.Tool;
using Placemaker.Game.Tool;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Overlay
{
    public sealed partial class PlacemakerOverlaySystem
    {
        private struct SpCurve
        {
            public Bezier4x3 Curve;
            public float Width;
            public bool Mark;
        }

        /// <summary>
        /// A prebuilt mesh layer: its rings, signature, colour, legend row and draw order. The order is a whole number per layer
        /// (the game sorts these meshes by whole steps: at -93.1 and -93.35 the asphalt drew over the stall lines at random). The
        /// ground stays at -95..-94, under the game's own decals (the Real preview's stall lines show through it).
        /// </summary>
        private sealed class SpShape
        {
            public List<float3[]> Rings = new List<float3[]>();
            public long Sig;
            public string Color;
            public float Alpha;
            public string Row;
            public float Priority;
            /// <summary>Drawn under the Real preview too (ground), or only in the sketch (what the ghost shows itself).</summary>
            public bool Ground;
            /// <summary>Every ring a small convex shape (discs, bars): the fill mesh's fast path.</summary>
            public bool Convex;
        }

        private int _spVersion = -1;
        private TerrainSystem _spTerrain;
        private readonly List<(string key, SpShape shape)> _spShapes = new List<(string, SpShape)>();
        private readonly List<SpCurve> _spCars = new List<SpCurve>(), _spWalks = new List<SpCurve>();
        private readonly List<(float3 at, float r)> _spCaps = new List<(float3, float)>();
        private readonly List<float3> _spIssues = new List<float3>();

        private const string Highlight = "#ffe14d";
        /// <summary>The asphalt under the Real preview: the built lots' mid grey (the sketch's darker tone read as black there).</summary>
        private const string RealAsphalt = "#6b6e73";

        private static bool SitePlanStyle => (ToolRegistration.Settings?.SketchStyle ?? Placemaker.Game.Settings.SketchStyle.SitePlan) == Placemaker.Game.Settings.SketchStyle.SitePlan;

        private static Color SpColor(string hex, float a)
        {
            var c = PreviewLegend.Hex(hex);
            return new Color(c.x, c.y, c.z, a);
        }

        private static float3[] Flat(float2[] r)
        {
            var o = new float3[r.Length];
            for (int i = 0; i < r.Length; i++) o[i] = new float3(r[i].x, 0f, r[i].y);   // the fill mesh samples the ground itself
            return o;
        }

        /// <summary>A disc as a polygon: 8 sides for small things, up to 16 for big crowns.</summary>
        private static float3[] DiscRing(float2 c, float r)
        {
            int n = r < 1f ? 6 : r < 3f ? 10 : 14;
            var o = new float3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * math.PI * 2f / n;
                o[i] = new float3(c.x + math.cos(a) * r, 0f, c.y + math.sin(a) * r);
            }
            return o;
        }

        /// <summary>A line of the given width as a thin rectangle.</summary>
        private static float3[] BarRing(float2 a, float2 b, float w)
        {
            float2 d = math.normalizesafe(b - a, new float2(1, 0)), n = new float2(-d.y, d.x) * (w * 0.5f);
            return new[] { new float3(a.x + n.x, 0, a.y + n.y), new float3(b.x + n.x, 0, b.y + n.y), new float3(b.x - n.x, 0, b.y - n.y), new float3(a.x - n.x, 0, a.y - n.y) };
        }

        private SpShape Shape(string key, string color, float alpha, string row, float priority, bool ground)
        {
            var s = new SpShape { Color = color, Alpha = alpha, Row = row, Priority = priority, Ground = ground };
            _spShapes.Add((key, s));
            return s;
        }

        private void BuildSitePlan3D(SitePlan sp)
        {
            _spTerrain ??= World.GetOrCreateSystemManaged<TerrainSystem>();
            var heights = _spTerrain.GetHeightData();
            float3 G(float2 p)
            {
                var v = new float3(p.x, 0f, p.y);
                v.y = TerrainUtils.SampleHeight(ref heights, v);
                return v;
            }
            _spShapes.Clear();

            // ground: the site's own lawn tone, the materials, shop plots (kept under the Real preview)
            Shape("sp:base", SitePlan.Palette.Base, SitePlan.Palette.BaseAlpha, "", -95f, true).Rings.Add(Flat(sp.Outline));
            for (int m = 0; m < SitePlan.Materials.Length; m++)
            {
                string mat = SitePlan.Materials[m];
                SpShape s = null;
                foreach (var f in sp.Fills)
                    if (f.material == mat) (s ??= Shape("sp:" + mat, SitePlan.Palette.Fill(mat), SitePlan.Palette.Alpha(mat), SitePlan.RowOfMaterial(mat), -94f, true)).Rings.Add(Flat(f.ring));
            }
            var byZone = new Dictionary<string, SpShape>();
            SpShape grid = null;
            foreach (var (zone, quad) in sp.PlotCells)
            {
                string c = SitePlan.Palette.Zone(zone);
                if (!byZone.TryGetValue(c, out var s)) byZone[c] = s = Shape("sp:plot:" + c, c, 0.5f, SitePlan.Plots, -94f, true);
                s.Rings.Add(Flat(quad));
                grid ??= Shape("sp:plotGrid", "#ffffff", 0.45f, SitePlan.Plots, -92f, true);
                for (int i = 0; i < quad.Length; i++) grid.Rings.Add(BarRing(quad[i], quad[(i + 1) % quad.Length], 0.3f));
            }

            // what the ghost shows itself (sketch only): stalls, places, furniture, planting, crowns last
            if (sp.Stalls.Count > 0)
            {
                var s = Shape("sp:stalls", SitePlan.Palette.Stall, 1f, SitePlan.Parking, -91f, false);
                // 0.3 m: a mesh line has no smoothing, at the real 0.15 m it faded to grey from the usual camera height
                foreach (var (a, b) in sp.Stalls) s.Rings.Add(BarRing(a, b, 0.3f));
            }
            if (sp.Chargers.Count > 0) { var s = Shape("sp:chargers", SitePlan.Palette.Charger, 1f, SitePlan.Parking, -90f, false); foreach (var p in sp.Chargers) s.Rings.Add(DiscRing(p, 0.7f)); }
            if (sp.PlaceFootprints.Count > 0)
            {
                var s = Shape("sp:places", SitePlan.Palette.Place, 1f, SitePlan.Places, -90f, false);
                var e = Shape("sp:placeEdges", SitePlan.Palette.PlaceEdge, 1f, SitePlan.Places, -89f, false);
                foreach (var q in sp.PlaceFootprints)
                {
                    s.Rings.Add(Flat(q));
                    for (int i = 0; i < q.Length; i++) e.Rings.Add(BarRing(q[i], q[(i + 1) % q.Length], 0.3f));
                }
            }
            if (sp.Fences.Count > 0)
            {
                var s = Shape("sp:fences", SitePlan.Palette.Fence, 0.9f, SitePlan.Furniture, -88f, false);
                foreach (var f in sp.Fences) for (int i = 1; i < f.Length; i++) s.Rings.Add(BarRing(f[i - 1], f[i], 0.35f));
            }
            if (sp.Benches.Count > 0) { var s = Shape("sp:benches", SitePlan.Palette.Bench, 1f, SitePlan.Furniture, -88f, false); foreach (var q in sp.Benches) s.Rings.Add(Flat(q)); }
            if (sp.Bins.Count > 0) { var s = Shape("sp:bins", SitePlan.Palette.Bin, 1f, SitePlan.Furniture, -87f, false); foreach (var p in sp.Bins) s.Rings.Add(DiscRing(p, 0.35f)); }
            if (sp.Lamps.Count > 0) { var s = Shape("sp:lamps", SitePlan.Palette.Lamp, 1f, SitePlan.Furniture, -87f, false); foreach (var p in sp.Lamps) s.Rings.Add(DiscRing(p, 0.5f)); }
            if (sp.Bushes.Count > 0) { var s = Shape("sp:bushes", SitePlan.Palette.Bush, 0.85f, SitePlan.Planting, -86f, false); foreach (var d in sp.Bushes) s.Rings.Add(DiscRing(d.At, d.R)); }
            if (sp.TreeCrowns.Count > 0)
            {
                // a soft shadow to the north (the sun is south), the crown, its lit side
                var sh = Shape("sp:treeShadow", SitePlan.Palette.Shadow, 0.28f, SitePlan.Trees, -85f, false);
                var cr = Shape("sp:tree", SitePlan.Palette.Tree, 0.95f, SitePlan.Trees, -84f, false);
                var lit = Shape("sp:treeLit", SitePlan.Palette.TreeHi, 0.95f, SitePlan.Trees, -83f, false);
                foreach (var t in sp.TreeCrowns)
                {
                    sh.Rings.Add(DiscRing(t.At + new float2(t.R * 0.25f, t.R * 0.3f), t.R));
                    cr.Rings.Add(DiscRing(t.At, t.R));
                    lit.Rings.Add(DiscRing(t.At + new float2(-t.R * 0.22f, -t.R * 0.22f), t.R * 0.55f));
                }
            }
            foreach (var (key, s) in _spShapes)
            {
                s.Sig = FilledPreviewSystem.Signature(s.Rings);
                // generated discs and bars (everything but the planned surfaces and the outline) are small and convex
                s.Convex = key != "sp:base" && !s.Ground || key == "sp:plotGrid";
            }

            // roads and paths stay overlay curves (a few hundred): one smooth piece per net, round ends
            void Curves(List<SitePlan.Stroke> from, List<SpCurve> to)
            {
                to.Clear();
                foreach (var s in from)
                {
                    if (s.Curve == null || s.Curve.Length != 4) continue;
                    var c = new Bezier4x3(s.Curve[0], s.Curve[1], s.Curve[2], s.Curve[3]);
                    c.a.y = G(c.a.xz).y; c.d.y = G(c.d.xz).y; c.b.y = math.lerp(c.a.y, c.d.y, 1f / 3f); c.c.y = math.lerp(c.a.y, c.d.y, 2f / 3f);
                    to.Add(new SpCurve { Curve = c, Width = s.Width, Mark = s.Marked });
                }
            }
            Curves(sp.Cars, _spCars);
            Curves(sp.Walks, _spWalks);
            _spCaps.Clear(); foreach (var d in sp.Caps) _spCaps.Add((G(d.At), d.R));
            _spIssues.Clear(); foreach (var b in sp.IssueBadges) _spIssues.Add(G(b.At));
        }

        /// <summary>The opacity of a ground layer under the Real preview (0 = left to the ground and the ghost).</summary>
        private static float RealAlpha(string key)
        {
            switch (key)
            {
                case "sp:asphalt": return 0.92f;
                case "sp:paving": case "sp:plaza": return 0.95f;
                case "sp:water": return 0.85f;
                case "sp:plotGrid": return 0f;
                default: return key.StartsWith("sp:plot:", System.StringComparison.Ordinal) ? 0.35f : 0f;
            }
        }

        /// <summary>A site-plan row shown? The player's show / hide only: hovering highlights, it never hides the rest.</summary>
        private bool SpShows(string row)
        {
            if (string.IsNullOrEmpty(row)) return true;
            var v = _tool.LegendVisibility;
            return v != null && v.TryGetValue(row, out var on) ? on : PreviewLegend.DefaultVisible(row);
        }

        /// <summary>0..1 blink of the hovered row (1 = full highlight).</summary>
        private float SpPulse(string row) =>
            !string.IsNullOrEmpty(row) && row == _tool.LegendHover ? 0.55f + 0.45f * math.sin(UnityEngine.Time.realtimeSinceStartup * 7f) : 0f;

        /// <summary>
        /// The site plan on the map. <paramref name="alpha"/>: 1, or less while the plan is catching up; <paramref name="objects"/>:
        /// what the Real preview leaves to the sketch (Hidden = only the ground under the ghost).
        /// </summary>
        private void DrawSitePlan(OverlayRenderSystem.Buffer buffer, float w, float alpha, SketchObjects objects)
        {
            var sp = _tool.SitePlan;
            if (sp == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool rebuilt = false;
            if (_spVersion != _tool.SitePlanVersion)
            {
                _spVersion = _tool.SitePlanVersion;
                BuildSitePlan3D(sp);
                rebuilt = true;
            }
            long buildMs = sw.ElapsedMilliseconds;
            bool underGhost = objects == SketchObjects.Hidden;
            float objAlpha = objects == SketchObjects.Dimmed ? alpha * 0.3f : underGhost ? 0f : alpha;
            var P = OverlayRenderSystem.StyleFlags.Projected;
            var up = new float2(0, 1);
            var hi = PreviewLegend.Hex(Highlight);

            // 1. the prebuilt layers: only their colours change from frame to frame
            foreach (var (key, s) in _spShapes)
            {
                bool on = SpShows(s.Row);
                float a = s.Alpha * (s.Ground ? alpha : objAlpha);
                // under the Real preview only what the game cannot show itself, solid, so it reads as the real thing: asphalt,
                // paving, water and the shop plots; no lawn tone or grass tint over the ground (the user: "it has this weird haze")
                if (underGhost && s.Ground) a = RealAlpha(key) * alpha;
                var c = PreviewLegend.Hex(underGhost && key == "sp:asphalt" ? RealAsphalt : s.Color);
                float pulse = SpPulse(s.Row);
                if (pulse > 0f) { c = math.lerp(c, hi, pulse * 0.85f); a = math.max(a, 0.9f * pulse); }
                _fill.SetLayer(key, s.Rings, new Color(c.x, c.y, c.z, on ? math.min(1f, a) : 0f), s.Priority, s.Sig, s.Convex);
            }
            if (rebuilt && sw.ElapsedMilliseconds > 50)
            {
                int rings = 0;
                foreach (var (_, s) in _spShapes) rings += s.Rings.Count;
                Mod.Log.Info($"Placemaker site plan: map layers built in {sw.ElapsedMilliseconds} ms (geometry {buildMs} ms, meshes {sw.ElapsedMilliseconds - buildMs} ms; {_spShapes.Count} layers, {rings} shapes)");
            }

            // 2. roads, aisles and paths: their real width, round ends so joins merge (flat under the see-through ghost)
            float carA = (underGhost ? 0.55f : 1f) * alpha;
            bool roads = SpShows(SitePlan.Roads), lot = SpShows(SitePlan.Parking);
            var ends = underGhost ? default : new float2(1, 1);
            if (roads || lot)
            {
                var asphalt = SpColor(SitePlan.Palette.Asphalt, carA);
                foreach (var l in _spCars)
                    if (l.Mark ? roads : lot) buffer.DrawCurve(asphalt, asphalt, 0f, P, l.Curve, l.Width, ends);
                if (roads && !underGhost) foreach (var c in _spCaps) buffer.DrawCircle(asphalt, asphalt, 0f, P, up, c.at, c.r * 2f);
                if (!underGhost)
                {
                    // markings, so roads read as roads and not grey bands (the user): a dashed centre line and solid edge lines on
                    // roads, a fainter dashed centre line along car-park aisles
                    var mark = SpColor(SitePlan.Palette.RoadMark, 0.8f * alpha);
                    var edge = SpColor(SitePlan.Palette.RoadMark, 0.55f * alpha);
                    var aisle = SpColor(SitePlan.Palette.RoadMark, 0.4f * alpha);
                    float mw = math.max(0.2f, w * 0.3f);
                    foreach (var l in _spCars)
                    {
                        if (l.Mark && roads)
                        {
                            buffer.DrawDashedCurve(mark, mark, 0f, P, l.Curve, mw, 3f, 3f);
                            float off = l.Width * 0.5f - 0.9f;
                            if (off > 1.5f)
                            {
                                buffer.DrawCurve(edge, edge, 0f, P, OffsetCurve(l.Curve, off), mw);
                                buffer.DrawCurve(edge, edge, 0f, P, OffsetCurve(l.Curve, -off), mw);
                            }
                        }
                        else if (!l.Mark && lot && l.Width >= 5f) buffer.DrawDashedCurve(aisle, aisle, 0f, P, l.Curve, mw, 2f, 3f);
                    }
                }
            }
            bool paths = SpShows(SitePlan.Paths);
            if (paths)
            {
                var path = SpColor(SitePlan.Palette.Path, (underGhost ? 0.45f : 0.95f) * alpha);
                foreach (var l in _spWalks) buffer.DrawCurve(path, path, 0f, P, l.Curve, l.Width, ends);
            }
            // the hovered road / path / aisle row blinks over its surface
            foreach (var (row, list, show) in new[] { (SitePlan.Roads, _spCars, roads), (SitePlan.Parking, _spCars, lot), (SitePlan.Paths, _spWalks, paths) })
            {
                float pulse = SpPulse(row);
                if (pulse <= 0f || !show) continue;
                var glow = new Color(hi.x, hi.y, hi.z, 0.75f * pulse);
                foreach (var l in list)
                    if (row == SitePlan.Paths || l.Mark == (row == SitePlan.Roads)) buffer.DrawCurve(glow, glow, 0f, P, l.Curve, l.Width + 1f, new float2(1, 1));
            }
            DrawSitePlanIssues(buffer, w, alpha);
        }

        /// <summary>Only what needs a look: a red badge where something was left out or blocked (warnings stay in the panel).</summary>
        private void DrawSitePlanIssues(OverlayRenderSystem.Buffer buffer, float w, float alpha)
        {
            if (!SpShows(SitePlan.Issues)) return;
            float pulse = SpPulse(SitePlan.Issues);
            var red = SpColor(SitePlan.Palette.Issue, 0.95f * alpha);
            var rim = pulse > 0f ? SpColor(Highlight, alpha) : new Color(1f, 1f, 1f, 0.95f * alpha);
            foreach (var p in _spIssues) buffer.DrawCircle(rim, red, math.max(0.4f, w * (0.6f + pulse)), 0, new float2(0, 1), p, math.max(6f, w * 12f));
        }
    }
}

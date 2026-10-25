// Placemaker — the sketch as a site plan (setting Sketch style: Site plan; sketch study, docs/sketch-study): the plan drawn as
// what gets built, from the tool's Core SitePlan, the same list the panel's top view draws. Ground fills (asphalt, lawn,
// paving, water, shop plots) are filled meshes (FilledPreviewSystem); roads and paths are surfaces at their real width with
// round joins, so a junction is one surface; stalls are the painted lines; trees are crowns at their real size with a soft
// shadow. Every size is in metres. Under the Real preview only the ground stays (the game draws planned surfaces as outlines
// only, so without it Real shows no asphalt and no new roads); the ghost shows the rest.
//
// The 3D geometry (ground heights) is built once per site plan, not per frame.

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

        private struct SpLine
        {
            public Line3.Segment Seg;
            public float Width;
            public bool Mark;
        }

        private int _spVersion = -1;
        private TerrainSystem _spTerrain;
        private readonly Dictionary<string, (List<float3[]> rings, long sig)> _spFills = new Dictionary<string, (List<float3[]>, long)>();
        private readonly Dictionary<string, (List<float3[]> rings, long sig)> _spPlots = new Dictionary<string, (List<float3[]>, long)>();
        private (List<float3[]> rings, long sig) _spPlaces, _spBase;
        private readonly List<SpCurve> _spCars = new List<SpCurve>(), _spWalks = new List<SpCurve>();
        private readonly List<SpLine> _spStalls = new List<SpLine>(), _spBenches = new List<SpLine>(), _spFences = new List<SpLine>();
        private readonly List<(float3 at, float r)> _spCaps = new List<(float3, float)>(), _spTrees = new List<(float3, float)>(), _spBushes = new List<(float3, float)>();
        private readonly List<float3> _spLamps = new List<float3>(), _spBins = new List<float3>(), _spChargers = new List<float3>(), _spIssues = new List<float3>();

        private static bool SitePlanStyle => (ToolRegistration.Settings?.SketchStyle ?? Placemaker.Game.Settings.SketchStyle.SitePlan) == Placemaker.Game.Settings.SketchStyle.SitePlan;

        private static Color SpColor(string hex, float a)
        {
            var c = PreviewLegend.Hex(hex);
            return new Color(c.x, c.y, c.z, a);
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
            float3[] Flat(float2[] r)
            {
                var o = new float3[r.Length];
                for (int i = 0; i < r.Length; i++) o[i] = new float3(r[i].x, 0f, r[i].y);   // the fill mesh samples the ground itself
                return o;
            }
            // one smooth piece per net with round ends: only the joins between nets overlap (no darker beads along a road)
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
            var site = new List<float3[]> { Flat(sp.Outline) };
            _spBase = (site, FilledPreviewSystem.Signature(site));
            _spFills.Clear();
            foreach (var m in SitePlan.Materials)
            {
                var rings = new List<float3[]>();
                foreach (var f in sp.Fills) if (f.material == m) rings.Add(Flat(f.ring));
                if (rings.Count > 0) _spFills[m] = (rings, FilledPreviewSystem.Signature(rings));
            }
            _spPlots.Clear();
            var byZone = new Dictionary<string, List<float3[]>>();
            foreach (var (zone, quad) in sp.PlotCells)
            {
                string c = SitePlan.Palette.Zone(zone);
                if (!byZone.TryGetValue(c, out var l)) byZone[c] = l = new List<float3[]>();
                l.Add(Flat(quad));
            }
            foreach (var kv in byZone) _spPlots[kv.Key] = (kv.Value, FilledPreviewSystem.Signature(kv.Value));
            var places = new List<float3[]>();
            foreach (var p in sp.PlaceFootprints) places.Add(Flat(p));
            _spPlaces = (places, FilledPreviewSystem.Signature(places));
            Curves(sp.Cars, _spCars);
            Curves(sp.Walks, _spWalks);
            _spFences.Clear();
            foreach (var f in sp.Fences)
                for (int i = 1; i < f.Length; i++) _spFences.Add(new SpLine { Seg = new Line3.Segment(G(f[i - 1]), G(f[i])), Width = 0.35f });
            _spStalls.Clear();
            foreach (var (a, b) in sp.Stalls) _spStalls.Add(new SpLine { Seg = new Line3.Segment(G(a), G(b)), Width = 0.15f });
            _spBenches.Clear();
            foreach (var q in sp.Benches)
            {
                // a bench is a short bar along its long side
                bool long01 = math.distance(q[0], q[1]) >= math.distance(q[1], q[2]);
                float2 a = long01 ? (q[0] + q[3]) * 0.5f : (q[0] + q[1]) * 0.5f, b = long01 ? (q[1] + q[2]) * 0.5f : (q[3] + q[2]) * 0.5f;
                float wdt = long01 ? math.distance(q[0], q[3]) : math.distance(q[0], q[1]);
                _spBenches.Add(new SpLine { Seg = new Line3.Segment(G(a), G(b)), Width = math.max(0.4f, wdt) });
            }
            _spCaps.Clear(); foreach (var d in sp.Caps) _spCaps.Add((G(d.At), d.R));
            _spTrees.Clear(); foreach (var d in sp.TreeCrowns) _spTrees.Add((G(d.At), d.R));
            _spBushes.Clear(); foreach (var d in sp.Bushes) _spBushes.Add((G(d.At), d.R));
            _spLamps.Clear(); foreach (var p in sp.Lamps) _spLamps.Add(G(p));
            _spBins.Clear(); foreach (var p in sp.Bins) _spBins.Add(G(p));
            _spChargers.Clear(); foreach (var p in sp.Chargers) _spChargers.Add(G(p));
            _spIssues.Clear(); foreach (var b in sp.IssueBadges) _spIssues.Add(G(b.At));
        }

        /// <summary>
        /// The site plan on the map. <paramref name="alpha"/>: 1, or less while the plan is catching up; <paramref name="objects"/>:
        /// what the Real preview leaves to the sketch (Hidden = only the ground under the ghost).
        /// </summary>
        private void DrawSitePlan(OverlayRenderSystem.Buffer buffer, float w, float alpha, SketchObjects objects)
        {
            var sp = _tool.SitePlan;
            if (sp == null) return;
            if (_spVersion != _tool.SitePlanVersion)
            {
                _spVersion = _tool.SitePlanVersion;
                BuildSitePlan3D(sp);
            }
            bool underGhost = objects == SketchObjects.Hidden;
            float objAlpha = objects == SketchObjects.Dimmed ? alpha * 0.3f : underGhost ? 0f : alpha;
            bool isolating = !string.IsNullOrEmpty(_tool.LegendHover);
            var P = OverlayRenderSystem.StyleFlags.Projected;
            var up = new float2(0, 1);
            var none = new Color(0, 0, 0, 0);

            // 0. the site's own ground under everything (no blue selection cast on the grass)
            _fill.SetLayer("sp:base", _spBase.rings, SpColor(SitePlan.Palette.Base, SitePlan.Palette.BaseAlpha * alpha), -93.6f, _spBase.sig);

            // 1. ground fills: one quiet tone per material (kept under the ghost: the game shows planned areas as outlines only)
            foreach (var kv in _spFills)
            {
                bool on = Shows(SitePlan.RowOfMaterial(kv.Key));
                float a = SitePlan.Palette.Alpha(kv.Key) * alpha * (underGhost && kv.Key != "asphalt" ? 0.8f : 1f) * (isolating && on ? 1.2f : 1f);
                _fill.SetLayer("sp:" + kv.Key, kv.Value.rings, SpColor(SitePlan.Palette.Fill(kv.Key), on ? math.min(1f, a) : 0f), -93.4f + System.Array.IndexOf(SitePlan.Materials, kv.Key) * 0.01f, kv.Value.sig);
            }
            bool plots = Shows(SitePlan.Plots);
            foreach (var kv in _spPlots)
                _fill.SetLayer("sp:plot:" + kv.Key, kv.Value.rings, SpColor(kv.Key, plots ? 0.5f * alpha : 0f), -93.3f, kv.Value.sig);
            if (plots)
                foreach (var kv in _spPlots)
                    foreach (var q in kv.Value.rings) Ring(buffer, Grounded(q), new Color(1f, 1f, 1f, 0.45f * alpha), math.max(0.25f, w * 0.3f));
            bool places = Shows(SitePlan.Places);
            _fill.SetLayer("sp:places", _spPlaces.rings, SpColor(SitePlan.Palette.Place, places ? objAlpha : 0f), -93.2f, _spPlaces.sig);

            // 2. roads, aisles and paths: their real width, round ends so joins merge
            float carA = (underGhost ? 0.55f : 1f) * alpha;
            if (Shows(SitePlan.Roads) || Shows(SitePlan.Parking))
            {
                var asphalt = SpColor(SitePlan.Palette.Asphalt, carA);
                bool roads = Shows(SitePlan.Roads), lot = Shows(SitePlan.Parking);
                foreach (var l in _spCars)
                    if (l.Mark ? roads : lot) buffer.DrawCurve(asphalt, asphalt, 0f, P, l.Curve, l.Width, new float2(1, 1));
                foreach (var c in _spCaps)
                    if (roads) buffer.DrawCircle(asphalt, asphalt, 0f, P, up, c.at, c.r * 2f);
                if (roads && !underGhost)
                {
                    var mark = SpColor(SitePlan.Palette.RoadMark, 0.7f * alpha);
                    float mw = math.max(0.25f, w * 0.35f);
                    foreach (var l in _spCars) if (l.Mark) buffer.DrawDashedCurve(mark, mark, 0f, P, l.Curve, mw, 3f, 3f);
                }
            }
            if (Shows(SitePlan.Paths))
            {
                var path = SpColor(SitePlan.Palette.Path, (underGhost ? 0.45f : 0.95f) * alpha);
                foreach (var l in _spWalks) buffer.DrawCurve(path, path, 0f, P, l.Curve, l.Width, new float2(1, 1));
            }
            if (objAlpha <= 0.001f) { DrawSitePlanIssues(buffer, w, alpha); return; }

            // 3. what the ghost shows itself: stalls, places, planting, furniture
            if (Shows(SitePlan.Parking))
            {
                var stall = SpColor(SitePlan.Palette.Stall, 0.9f * objAlpha);
                float sw = math.max(0.15f, w * 0.3f);
                foreach (var l in _spStalls) buffer.DrawLine(stall, stall, 0f, P, l.Seg, sw, default);
                var ch = SpColor(SitePlan.Palette.Charger, objAlpha);
                foreach (var p in _spChargers) buffer.DrawCircle(ch, ch, 0f, P, up, p, 1.4f);
            }
            if (places)
            {
                var edge = SpColor(SitePlan.Palette.PlaceEdge, objAlpha);
                foreach (var r in _spPlaces.rings) Ring(buffer, Grounded(r), edge, math.max(0.25f, w * 0.3f));
            }
            if (Shows(SitePlan.Furniture))
            {
                var fence = SpColor(SitePlan.Palette.Fence, 0.9f * objAlpha);
                foreach (var l in _spFences) buffer.DrawLine(fence, fence, 0f, P, l.Seg, math.max(0.35f, w * 0.4f), default);
                var bench = SpColor(SitePlan.Palette.Bench, objAlpha);
                foreach (var l in _spBenches) buffer.DrawLine(bench, bench, 0f, P, l.Seg, l.Width, default);
                var lamp = SpColor(SitePlan.Palette.Lamp, objAlpha);
                var rim = new Color(0.2f, 0.2f, 0.2f, objAlpha);
                foreach (var p in _spLamps) buffer.DrawCircle(rim, lamp, 0.15f, P, up, p, math.max(0.9f, w * 1.6f));
                var bin = SpColor(SitePlan.Palette.Bin, objAlpha);
                foreach (var p in _spBins) buffer.DrawCircle(bin, bin, 0f, P, up, p, math.max(0.7f, w * 1.2f));
            }
            if (Shows(SitePlan.Planting))
            {
                var bush = SpColor(SitePlan.Palette.Bush, 0.85f * objAlpha);
                foreach (var b in _spBushes) buffer.DrawCircle(bush, bush, 0f, P, up, b.at, b.r * 2f);
            }
            if (Shows(SitePlan.Trees))
            {
                // crowns last, over everything: a soft shadow to the north (the sun is south), the crown, a lit side
                var shadow = SpColor(SitePlan.Palette.Shadow, 0.28f * objAlpha);
                var crown = SpColor(SitePlan.Palette.Tree, 0.95f * objAlpha);
                var lit = SpColor(SitePlan.Palette.TreeHi, 0.95f * objAlpha);
                foreach (var t in _spTrees) buffer.DrawCircle(shadow, shadow, 0f, P, up, t.at + new float3(t.r * 0.25f, 0f, t.r * 0.3f), t.r * 2f);
                foreach (var t in _spTrees) buffer.DrawCircle(crown, crown, 0f, P, up, t.at, t.r * 2f);
                foreach (var t in _spTrees) buffer.DrawCircle(lit, lit, 0f, P, up, t.at + new float3(-t.r * 0.22f, 0f, -t.r * 0.22f), t.r * 1.1f);
            }
            DrawSitePlanIssues(buffer, w, alpha);
        }

        /// <summary>Only what needs a look: a red badge where something was left out or blocked (warnings stay in the panel).</summary>
        private void DrawSitePlanIssues(OverlayRenderSystem.Buffer buffer, float w, float alpha)
        {
            if (!Shows(SitePlan.Issues)) return;
            var red = SpColor(SitePlan.Palette.Issue, 0.95f * alpha);
            var white = new Color(1f, 1f, 1f, 0.95f * alpha);
            foreach (var p in _spIssues) buffer.DrawCircle(white, red, math.max(0.4f, w * 0.6f), 0, new float2(0, 1), p, math.max(6f, w * 12f));
        }

        private float3[] Grounded(float3[] ring)
        {
            var o = new float3[ring.Length];
            for (int i = 0; i < ring.Length; i++) o[i] = OnGround(ring[i].xz);
            return o;
        }
    }
}

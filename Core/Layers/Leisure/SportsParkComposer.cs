// The sports park's composition (s10): laid out the way a landscape architect would draw one.
//
//  1. Frame & margins. A tree belt runs inside the outline, a promenade loop inside the belt; the
//     facilities live in the "play field" inside the loop. Every distance grows with the size of the
//     site and the "Breathing room" slider (dynamic padding): small parks stay compact, big ones breathe.
//  2. Parking only when asked, or when the park is large and has road frontage: one tidy lot planned by
//     the Parking layer on the street edge, next to the main entrance (never scattered).
//  3. Hierarchy. One hero field (track, cricket, pitch, diamond — or a tennis court block for a club)
//     hugs the far side of the play field, aligned with the dominant edge (or N–S / E–W), with generous
//     buffers. Further big fields line up beside it. A spectator terrace with the clubhouse (changing
//     pavilion, kiosk) faces the hero's long side; a touchline walk runs along it so the field's stands
//     face the terrace. Kids' play sits in the clubhouse yard.
//  4. Secondary courts cluster by type (tennis blocks shoulder to shoulder, basketball courts in a row…),
//     each cluster anchored where it belongs (tennis by the clubhouse, street sports by the entrance,
//     fitness along the loop), with zone gaps between clusters.
//  5. Circulation: the loop, gate spurs through the belt, an entrance plaza with bike racks at the main
//     gate, a tree-lined spine from the plaza to the clubhouse, short connectors to every facility.
//  6. Landscape between zones: groves in the gaps, low berms in the larger gaps, shade trees along the
//     terrace and the spine, benches, lamps and bins along the walks.
//
// Places are emitted as "place:<id>" + SizedTag regions: the field add-on lays itself out inside.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.AddOns.Fields;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Transport;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    internal sealed class SportsParkComposer
    {
        private enum Kind { Big = 0, Court = 1, Small = 2, Structure = 3 }

        private sealed class Item
        {
            public string Layer;      // feature layer id, or "" for the host's own structures
            public string Group;      // cluster id (same group = tight spacing)
            public Kind Kind;
            public OrientedRect Rect;
            public bool Place;        // emitted as a place region for a feature
        }

        private readonly LayerContext _ctx;
        private List<Gate> _gates;
        private LayerRegion _region;
        private Occupancy _occ;
        private LayerRegion _play;               // where facilities go (inside the loop)
        private LayerRegion _playBig;            // the play field less a buffer: big fields keep well clear of the loop
        private float2 _axis, _side, _playCentre;
        private float _span, _belt, _hw, _bigGap, _zoneGap, _breath;
        private readonly List<Item> _items = new List<Item>();
        private readonly List<float2> _grid = new List<float2>();
        private AssetRef _path, _bench;
        private Gate _main;
        private bool _hasMain;
        private float2 _plazaCentre;
        private OrientedRect _plaza;
        private bool _hasPlaza;
        private Item _hero, _terrace, _kids, _hub;
        private float2 _heroFront;               // unit normal of the hero's long side facing the park
        private float _tDepth, _cDepth;          // terrace (front) and clubhouse (back) depths of the terrace item
        private readonly List<(float2 a, float2 b)> _loopSegs = new List<(float2, float2)>();
        private readonly List<(float2 a, float2 b)> _walks = new List<(float2, float2)>();
        private int _lotStalls;
        private SubLot _lot;

        public SportsParkComposer(LayerContext ctx, List<Gate> gates)
        {
            _ctx = ctx;
            _gates = gates ?? new List<Gate>();
        }

        private float S(float design, float min) => _ctx.M(design, min);

        public void Run()
        {
            var ctx = _ctx;
            _region = ctx.LayerRegion;
            _path = AssetMix.One(ctx.S("pathAsset"));
            _bench = AssetMix.One(ctx.S("benchAsset"));
            _breath = math.clamp(ctx.F("spacing") / 100f, 0.5f, 2.5f);
            _hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            PickMainGate();

            Parking();
            _region = ctx.LayerRegion;
            _occ = new Occupancy(_region, ctx.Input.Reserved, ctx.Site);
            if (_lotStalls > 0)
            {
                TransportKit.Occupy(_occ, ctx.Plan);
                // The lot's own footways are walks too (nothing of ours may stand on them).
                foreach (var n in ctx.Plan.Nets)
                    if (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide)
                    {
                        float hw = Composer.DefaultHalfWidth(n.Role);
                        _occ.TakeCorridor(n.A.xz, n.D.xz, hw, 100);
                        _occ.Paths.Add(n.A.xz, n.D.xz, hw, 100);
                    }
            }
            Lawn.Cover(ctx, _region.Ring, AssetMix.One(""));
            ctx.Check();

            Frame();
            EntrancePlaza();
            ctx.Check();

            PlaceHero();
            ctx.Check();
            PlaceTerrace(1f);
            PlanSpine();
            ctx.Check();
            PlaceBigFields();
            var missing = PlaceCourts(CourtOrder);
            if (missing.Count > 0 && _terrace != null)
            {
                // The facilities the player asked for come before the terrace: retry without it, then fit a slimmer one.
                _items.Remove(_terrace);
                _terrace = null; _hub = null;
                missing = PlaceCourts(missing);
                _spine.Clear();
                PlaceTerrace(0.65f);
                if (_terrace != null) PlanSpine(false);
            }
            foreach (var m in missing) NoRoom(m);
            PlaceKids();
            ctx.Check();
            LevelTerrace();

            Paths();
            if (_lot != null) TransportKit.ClearPlantsFromPaths(ctx.Plan, _lot, _occ);
            ctx.Check();
            Dress();
            foreach (var it in _items) Amenities.Take(_occ, it.Rect, it == _terrace ? 0.3f : 0.6f, 102);
            if (_hasPlaza) LeisureKit.TakeRect(_occ, Shrink(_plaza, 1.2f), 5);
            ctx.Check();
            Landscape();
            PathFurniture();
            ctx.Check();
            Regions();

            int places = 0;
            foreach (var it in _items) if (it.Place) places++;
            ctx.Stat("Placemaker.Stat.Facilities", places, Keys.UnitCount);
            ctx.Plan.Facts["facilities"] = places;
            ctx.Plan.Facts["hero"] = _hero != null ? 1 : 0;
            ctx.Plan.Facts["terrace"] = _terrace != null ? 1 : 0;
            if (_terrace != null) { ctx.Plan.Facts["terraceX"] = _terrace.Rect.Center.x; ctx.Plan.Facts["terraceZ"] = _terrace.Rect.Center.y; }
            ctx.Plan.Facts["loop"] = _loopSegs.Count > 0 ? 1 : 0;
            ctx.Plan.Facts["spine"] = _spine.Count;
        }

        // ───────────────────────── frame ─────────────────────────

        private void PickMainGate()
        {
            _hasMain = false;
            foreach (var g in _gates)
                if (EntranceKinds.Normalize(g.Kind) == EntranceKinds.Main) { _main = g; _hasMain = true; break; }
            if (!_hasMain)
                foreach (var g in _gates)
                    if (EntranceKinds.Walks(g.Kind)) { _main = g; _hasMain = true; break; }
        }

        private void Frame()
        {
            var ctx = _ctx;
            _span = math.sqrt(_region.Area);
            // Dynamic padding: margins grow with the site and the breathing-room slider.
            string belt = ctx.S("treeBelt");
            _belt = belt == "none" ? S(6f, 3f) : math.clamp(_span * 0.055f, 6f, 15f) * (belt == "double" ? 1.35f : 1f) * math.lerp(1f, _breath, 0.5f);
            _bigGap = math.clamp(_span * 0.06f, 6f, 18f) * _breath;
            _zoneGap = math.clamp(_span * 0.045f, 4.5f, 13f) * _breath;

            // Field axis: the dominant (longest) edge, or north–south / east–west.
            string ax = ctx.S("fieldAxis");
            if (ax == "northSouth") _axis = new float2(0, 1);
            else if (ax == "eastWest") _axis = new float2(1, 0);
            else
            {
                int n = _region.Ring.Length; float best = -1f;
                _axis = new float2(1, 0);
                for (int i = 0; i < n; i++)
                {
                    var a = _region.Ring[i]; var b = _region.Ring[(i + 1) % n];
                    float len = math.distance(a, b);
                    if (len > best + 0.01f) { best = len; _axis = math.normalizesafe(b - a, new float2(1, 0)); }
                }
                if (_axis.y < 0f || (math.abs(_axis.y) < 1e-4f && _axis.x < 0f)) _axis = -_axis;
            }
            _side = new float2(-_axis.y, _axis.x);

            // Play field: inside the belt and the loop (with its own clearance).
            bool loop = ctx.S("promenade") != "spine";
            float inset = _belt + (loop ? 2f * _hw + 1.5f : 1f);
            _play = Inset(_region, inset) ?? Inset(_region, math.max(2f, inset * 0.5f)) ?? _region;
            _playBig = Inset(_play, math.max(2f, _bigGap * 0.5f)) ?? _play;
            _playCentre = _play.Contains(_play.Centroid) ? _play.Centroid : _play.DeepestPoint(out _, null, 16);

            // Candidate centres (a lattice in the field frame), shared by every search.
            float step = math.clamp(_span / 50f, 2f, 4.5f);
            Bounds(_play, out float u0, out float u1, out float v0, out float v1);
            for (float u = u0; u <= u1; u += step)
                for (float v = v0; v <= v1; v += step)
                {
                    var p = _playCentre + _axis * u + _side * v;
                    if (_play.Contains(p)) _grid.Add(p);
                }
        }

        private static LayerRegion Inset(LayerRegion r, float d)
        {
            try
            {
                var pieces = Poly.Offset(r.Ring, d);
                float2[] best = null; float area = 0f;
                foreach (var p in pieces) { float a = Poly.Area(p); if (a > area) { area = a; best = p; } }
                return best == null || area < 200f ? null : LayerRegion.From(best);
            }
            catch (Exception) { return null; }
        }

        private void Bounds(LayerRegion r, out float u0, out float u1, out float v0, out float v1)
        {
            u0 = v0 = float.MaxValue; u1 = v1 = float.MinValue;
            foreach (var q in r.Ring)
            {
                var d = q - _playCentre;
                float u = math.dot(d, _axis), v = math.dot(d, _side);
                u0 = math.min(u0, u); u1 = math.max(u1, u); v0 = math.min(v0, v); v1 = math.max(v1, v);
            }
        }

        // ───────────────────────── parking ─────────────────────────

        private void Parking()
        {
            var ctx = _ctx;
            string mode = ctx.S("parking");
            var full = ctx.LayerRegion;
            // "On" still needs a park big enough to give a lot away (1.5 ha); auto waits for 2.5 ha.
            if (mode == "off" || full.Area < (mode == "auto" ? 25000f : 15000f)) return;
            var roads = StreetFrontage.Roads(ctx.Input);
            var edges = StreetFrontage.Edges(full.Ring, roads, 30f);
            if (edges.Count == 0) { if (mode == "on") ctx.Issue(IssueSeverity.Info, TransportKit.NoLotKey); return; }
            // The longest frontage; prefer the one the main entrance is on.
            int pick = 0; float best = -1f;
            for (int i = 0; i < edges.Count; i++)
            {
                float len = math.distance(edges[i].a, edges[i].b);
                if (_hasMain && SegmentIndex.DistSq(_main.Position, edges[i].a, edges[i].b) < 16f) len *= 1.6f;
                if (len > best) { best = len; pick = i; }
            }
            var (ea, eb) = edges[pick];
            float elen = math.distance(ea, eb);
            var tangent = math.normalizesafe(eb - ea);
            float share = math.clamp(ctx.F("lotShare"), 3f, 20f) / 100f;
            float depth = 34f;
            float width = math.clamp(full.Area * share / depth, 36f, math.min(130f, elen - 16f));
            if (width < 30f) { if (mode == "on") ctx.Issue(IssueSeverity.Info, TransportKit.NoLotKey); return; }
            // Next to the main entrance (not on it), else in the middle of the frontage.
            float t = elen * 0.5f;
            if (_hasMain && SegmentIndex.DistSq(_main.Position, ea, eb) < 16f)
            {
                float g = math.dot(_main.Position - ea, tangent), off = width * 0.5f + S(26f, 15f);   // room for the entrance plaza
                t = g + off <= elen - width * 0.5f - 4f ? g + off : g - off;
            }
            t = math.clamp(t, width * 0.5f + 4f, elen - width * 0.5f - 4f);
            var at = ea + tangent * t;
            full.ClosestOnBoundary(at, out int seg, out _);
            var inward = full.InwardNormal(seg);
            var cut = LayerRegion.Rect(at + inward * (depth * 0.5f - 2f), tangent, width * 0.5f, depth * 0.5f + 2f);
            var lot = TransportKit.PlanLot(ctx, cut, at, TransportKit.Bag(("lotGreen", "edges")), 0x5107u);
            if (lot == null) return;
            var rest = TransportKit.Subtract(full.Ring, cut, 1200f);
            if (rest == null) return;
            ctx.LayerRegion = LayerRegion.From(rest);
            _lotStalls = lot.Stalls;
            _lot = lot;
            // People walk from the lot into the park at the middle of its inner edge.
            var walkIn = ctx.LayerRegion.ClosestOnBoundary(at + inward * (depth + 2f), out int s2, out float t2);
            var lotGate = new Gate { Position = walkIn, Segment = s2, T = t2, Kind = EntranceKinds.Pedestrian, AttachId = -1 };
            _gates = new List<Gate>(_gates) { lotGate };
            // A main entrance the lot swallowed moves to the lot's walk-in point.
            if (_hasMain && ctx.LayerRegion.BoundaryDistance(_main.Position, 3f) > 2f || _hasMain && Poly.Contains(cut, _main.Position)) _main = lotGate;
            ctx.Plan.Facts["lotStalls"] = _lotStalls;
            ctx.Stat("Placemaker.Stat.Parking", _lotStalls, Keys.UnitCount);
        }

        // ───────────────────────── fitting ─────────────────────────

        private float Gap(Item other, string group, Kind kind)
        {
            if (other.Group == group)
                switch (group)
                {
                    case "tennisCourts": return 1.2f;
                    case "tableTennis": return 2f;
                    case "pitch": case "baseballDiamond": return math.max(4f, _bigGap * 0.6f);
                    default: return 3f;
                }
            if (other.Kind == Kind.Big || kind == Kind.Big) return _bigGap;
            if (other.Kind == Kind.Structure || kind == Kind.Structure) return _zoneGap * 0.6f;
            return _zoneGap;
        }

        private bool Fits(OrientedRect r, string group, Kind kind, string ignoreGroup = null, LayerRegion within = null)
        {
            var area = within ?? (kind == Kind.Big ? _playBig : _play);
            var ring = r.Ring;
            foreach (var q in ring) if (!area.Contains(q)) return false;
            for (int i = 0; i < 4; i++) if (area.CrossesBoundary(ring[i], ring[(i + 1) % 4], false)) return false;
            foreach (var it in _items)
            {
                if (ignoreGroup != null && it.Group == ignoreGroup) continue;
                if (RectGap(r, it.Rect) < Gap(it, group, kind) - 0.01f) return false;
            }
            if (_hasPlaza && RectGap(r, _plaza) < _zoneGap * 0.5f) return false;
            // The spine's line (with room for its avenue trees) stays free.
            if (_spine.Count > 0)
            {
                float m = Composer.DefaultHalfWidth(NetRole.PathwayWide) + S(5f, 3.5f);
                var g = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength + m, HalfWidth = r.HalfWidth + m };
                foreach (var (a, b) in _spine) if (SegHitsRect(a, b, g)) return false;
            }
            return FieldKit.QuickFree(_occ, r, 0.5f) && LeisureKit.RectFits(_region, _occ, r, 0.3f);
        }

        /// <summary>Separation of two rectangles (0 when they overlap).</summary>
        private static float RectGap(OrientedRect a, OrientedRect b)
        {
            if (math.distance(a.Center, b.Center) > a.HalfLength + a.HalfWidth + b.HalfLength + b.HalfWidth + 60f) return 999f;
            var ra = a.Ring; var rb = b.Ring;
            if (Overlap(ra, rb)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    best = math.min(best, SegmentIndex.DistSq(ra[i], rb[j], rb[(j + 1) % 4]));
                    best = math.min(best, SegmentIndex.DistSq(rb[i], ra[j], ra[(j + 1) % 4]));
                }
            return math.sqrt(best);
        }

        private static bool Overlap(float2[] a, float2[] b)
        {
            foreach (var poly in new[] { a, b })
                for (int i = 0; i < 4; i++)
                {
                    var e = poly[(i + 1) % 4] - poly[i];
                    var n = new float2(-e.y, e.x);
                    float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                    foreach (var p in a) { float d = math.dot(p, n); amin = math.min(amin, d); amax = math.max(amax, d); }
                    foreach (var p in b) { float d = math.dot(p, n); bmin = math.min(bmin, d); bmax = math.max(bmax, d); }
                    if (amax <= bmin || bmax <= amin) return false;
                }
            return true;
        }

        /// <summary>
        /// The best spot for an L×W rectangle: lattice candidates ordered by score (distance to the anchor
        /// plus <paramref name="extra"/>), field axis first and turned 90° with a penalty when allowed.
        /// </summary>
        private bool Search(float L, float W, float2 anchor, Func<float2, float> extra, bool turn, string group, Kind kind, out OrientedRect rect, float turnPenalty = 12f)
        {
            rect = default;
            var cands = new List<(float score, int i, int t)>(_grid.Count * 2);
            for (int i = 0; i < _grid.Count; i++)
            {
                var p = _grid[i];
                float sc = math.distance(p, anchor) + (extra != null ? extra(p) : 0f);
                cands.Add((sc, i, 0));
                if (turn && math.abs(L - W) > 0.5f) cands.Add((sc + turnPenalty, i, 1));
            }
            cands.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.i != b.i ? a.i.CompareTo(b.i) : a.t.CompareTo(b.t));
            int tests = 0;
            float need = math.min(L, W) * 0.5f;
            foreach (var (_, i, t) in cands)
            {
                var c = _grid[i];
                if (_play.BoundaryDistance(c, need + 1f) < need - 0.01f) continue;
                var ax = t == 0 ? _axis : _side;
                var r = new OrientedRect { Center = c, Axis = ax, HalfLength = L * 0.5f, HalfWidth = W * 0.5f };
                if (++tests > 2500) return false;
                if (!Fits(r, group, kind)) continue;
                rect = r;
                return true;
            }
            return false;
        }

        /// <summary>Next member of a cluster: shoulder to shoulder with the members already placed (rows, then rings).</summary>
        private bool Next(Item first, List<Item> members, float L, float W, string group, Kind kind, out OrientedRect rect)
        {
            rect = default;
            float gap = Gap(first, group, kind);
            var a = first.Rect.Axis; var s = first.Rect.Side;
            var cands = new List<(float score, float2 c)>();
            foreach (var m in members)
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                    {
                        if (i == 0 && j == 0) continue;
                        var c = m.Rect.Center + a * (i * (m.Rect.HalfLength + L * 0.5f + gap)) + s * (j * (m.Rect.HalfWidth + W * 0.5f + gap));
                        float score = math.distance(c, first.Rect.Center) + (i != 0 && j != 0 ? 1000f : 0f) + (i != 0 ? 0.5f : 0f);
                        cands.Add((score, c));
                    }
            cands.Sort((x, y) => x.score.CompareTo(y.score));
            foreach (var (_, c) in cands)
            {
                var r = new OrientedRect { Center = c, Axis = a, HalfLength = L * 0.5f, HalfWidth = W * 0.5f };
                if (Fits(r, group, kind)) { rect = r; return true; }
            }
            return false;
        }

        private Item Add(string layer, string group, Kind kind, OrientedRect r, bool place)
        {
            var it = new Item { Layer = layer, Group = group, Kind = kind, Rect = r, Place = place };
            _items.Add(it);
            return it;
        }

        private bool Wants(string layer) => _ctx.Input.HasChildTarget(Amenities.Place(layer));

        /// <summary>
        /// (rf-sports) A single pitch is already drawn inside a running track's infield: with a track as the hero
        /// the pitch feature is only its stand-in for when the track does not fit (a stadium on a small block).
        /// </summary>
        private bool InInfield(string hero, string layer) => hero == "runningTrack" && layer == "pitch" && Count("pitch") <= 1 && _ctx.S("subtype") == "stadium";

        /// <summary>(rf-sports) The track's ladder ends in a compact oval (bends shrunk to the place) so a stadium fits a city block.</summary>
        private static readonly FieldFormat[] TrackLadder = Append(FieldSpecs.Track, new FieldFormat("compact", 84f, 50f));

        private static FieldFormat[] Append(FieldFormat[] a, FieldFormat extra)
        {
            var r = new FieldFormat[a.Length + 1];
            Array.Copy(a, r, a.Length);
            r[a.Length] = extra;
            return r;
        }

        private static FieldFormat[] Ladder(string layer)
        {
            switch (layer)
            {
                case "pitch": return FieldSpecs.Pitch;
                case "baseballDiamond": return FieldSpecs.Baseball;
                case "runningTrack": return TrackLadder;
                case "cricketGround": return FieldSpecs.Cricket;
                case "tennisCourts": return FieldSpecs.Tennis;
                case "basketballCourt": return FieldSpecs.Basketball;
                case "skateBowl": return FieldSpecs.Skate;
                default:
                {
                    var spec = Amenities.Spec(layer);
                    return spec == null ? Array.Empty<FieldFormat>() : new[] { new FieldFormat("place", spec.Length, spec.Width) };
                }
            }
        }

        private int Count(string layer)
        {
            switch (layer)
            {
                case "runningTrack": case "cricketGround": case "skateBowl": return 1;
                default:
                {
                    int n = math.max(1, _ctx.I(layer + "Count"));
                    // (P2-4) "Grow with the site": a big site multiplies the repeatable facilities (about 2.6 x on 15 ha), so it
                    // fills as a sports complex instead of a few fields in a wood; a block-sized site keeps the counts as set
                    if (_ctx.B("growWithSite")) n = math.min(GrowCap(layer), (int)math.round(n * GrowFactor));
                    return n;
                }
            }
        }

        /// <summary>The site's area against a 4.5 ha park (a 220 x 200 m block), softened: 1 up to it, ~2.6 on 15 ha, at most 4.</summary>
        private float GrowFactor => math.clamp(math.pow(_region.Area / 45000f, 0.8f), 1f, 4f);

        private static int GrowCap(string layer)
        {
            switch (layer)
            {
                case "pitch": return 8;
                case "baseballDiamond": return 4;
                case "tennisCourts": return 6;
                case "basketballCourt": return 8;
                case "fitness": return 4;
                default: return 8;
            }
        }

        /// <summary>Index of the first rung to try: the player's size, or 0 for "auto"; tennis: courts per block.</summary>
        private int FirstRung(string layer, FieldFormat[] ladder)
        {
            string size = _ctx.S(layer + "Size");
            if (string.IsNullOrEmpty(size) || size == "auto") return 0;
            for (int i = 0; i < ladder.Length; i++) if (ladder[i].Id == size) return i;
            return 0;
        }

        /// <summary>Places one facility from its ladder (first rung down to the smallest), capped to a share of the play field for "auto".</summary>
        private Item PlaceOne(string layer, Kind kind, float2 anchor, Func<float2, float> extra, float shareCap, bool turn = true, bool report = true)
        {
            var ladder = Ladder(layer);
            if (ladder.Length == 0) return null;
            int first = FirstRung(layer, ladder);
            bool auto = (_ctx.S(layer + "Size") ?? "auto") == "auto" || layer == "basketballCourt" || layer == "skateBowl";
            for (int k = first; k < ladder.Length; k++)
            {
                var f = ladder[k];
                if (auto && k < ladder.Length - 1 && f.Length * f.Width > shareCap * _play.Area) continue;
                if (Search(f.Length, f.Width, anchor, extra, turn, layer, kind, out var r))
                {
                    if (k > first && !auto) _ctx.Issue(IssueSeverity.Info, FieldAddOn.SmallerKey, "Placemaker.Feature." + layer);
                    return Add(layer, layer, kind, r, true);
                }
            }
            if (report) NoRoom(layer);
            return null;
        }

        /// <summary>The remaining members of a cluster next to its first member (same size).</summary>
        private void Cluster(Item first, int count, Kind kind)
        {
            var members = new List<Item> { first };
            for (int i = 1; i < count; i++)
            {
                if (!Next(first, members, first.Rect.HalfLength * 2f, first.Rect.HalfWidth * 2f, first.Group, kind, out var r))
                {
                    // Elsewhere, near the cluster.
                    if (!Search(first.Rect.HalfLength * 2f, first.Rect.HalfWidth * 2f, first.Rect.Center, null, true, first.Group, kind, out r))
                    {
                        _ctx.Issue(IssueSeverity.Info, "Placemaker.Sports.Issue.Fewer", members.Count, count);
                        return;
                    }
                }
                members.Add(Add(first.Layer, first.Group, kind, r, true));
            }
        }

        private void NoRoom(string layer) =>
            _ctx.Issue(IssueSeverity.Warning, "Placemaker.Park.Issue.NoRoomForAmenity", "Placemaker.Feature." + layer);

        // ───────────────────────── entrance plaza ─────────────────────────

        private void EntrancePlaza()
        {
            if (!_hasMain || !_ctx.B("entrancePlaza")) return;
            var g = _main.Position;
            _region.ClosestOnBoundary(g, out int seg, out _);
            var inward = _region.InwardNormal(seg);
            var n = _region.Ring.Length;
            var tangent = math.normalizesafe(_region.Ring[(seg + 1) % n] - _region.Ring[seg]);
            float depth = math.clamp(_belt + 2f * _hw + S(10f, 6f), 10f, 30f), width = S(28f, 14f) * math.lerp(1f, _breath, 0.3f);
            // P2-4 (the user: the plaza doesn't grow when the park is drawn very big): the square grows with the park, up to a
            // grand forecourt of about 75 x 45 m on a 450 m site; it gives back size where the outline will not take it.
            float grow = math.clamp(_span / 150f, 1f, 2.7f);
            // in real metres: the layout scale shrinks design sizes on a very big site (a 15 ha park started from 14 m)
            float bigW = math.min(math.max(width, 28f) * grow, 76f), bigD = math.min(math.max(depth, 18f) * math.pow(grow, 0.75f), 46f);
            foreach (float f in new[] { 1f, 0.82f, 0.66f, 0.5f, 0f })
            {
                float w = math.lerp(width, bigW, f), d = math.lerp(depth, bigD, f);
                // slid along the edge where a corner is near: the gate stays on the square's front, 4 m or more from its end
                OrientedRect r = default; bool inside = false;
                foreach (float t in new[] { 0f, 0.25f, -0.25f, 0.5f, -0.5f })
                {
                    float shift = math.clamp(t * w, -(w * 0.5f - 4f), w * 0.5f - 4f);
                    r = new OrientedRect { Center = g + tangent * shift + inward * (d * 0.5f + 0.6f), Axis = tangent, HalfLength = w * 0.5f, HalfWidth = d * 0.5f };
                    inside = true;
                    foreach (var q in r.Ring) if (!_region.Contains(q)) { inside = false; break; }
                    if (inside) break;
                }
                if (!inside) continue;
                _plaza = r;
                _hasPlaza = true;
                _plazaCentre = _plaza.Center;
                _ctx.Plan.Facts["plazaWidth"] = Math.Round(w, 1);
                _ctx.Plan.Facts["plazaDepth"] = Math.Round(d, 1);
                return;
            }
        }

        // ───────────────────────── hierarchy ─────────────────────────

        private string HeroLayer()
        {
            string h = _ctx.S("hero");
            if (h == "none") return null;
            if (h != "auto" && !string.IsNullOrEmpty(h)) return Wants(h) ? h : null;
            foreach (var f in SportsParkLayer.BigFields) if (Wants(f)) return f;
            return null;
        }

        private void PlaceHero()
        {
            string layer = HeroLayer();
            if (layer == null) return;
            // The far side of the play field from the main entrance: the hero hugs it, the rest of the park opens towards the gate.
            Bounds(_play, out float u0, out float u1, out float v0, out float v1);
            float2 gate = _hasMain ? _main.Position : _playCentre - _side * 1000f;
            var dg = gate - _playCentre;
            float gu = math.dot(dg, _axis) / math.max(1f, u1 - u0), gv = math.dot(dg, _side) / math.max(1f, v1 - v0);
            float2 anchor;
            if (math.abs(gv) >= math.abs(gu) * 0.8f) anchor = _playCentre + _side * (gv > 0f ? v0 : v1) + _axis * ((u0 + u1) * 0.5f);
            else anchor = _playCentre + _axis * (gu > 0f ? u0 : u1) + _side * ((v0 + v1) * 0.5f);
            // Several big fields: the hero takes the far corner so the others can line up beside it.
            int bigTotal = 0;
            foreach (var f in SportsParkLayer.BigFields) if (Wants(f) && !InInfield(layer, f)) bigTotal += Count(f);
            int others = 0;
            foreach (var f in SportsParkLayer.Facilities()) if (Wants(f) && f != layer && !InInfield(layer, f)) others++;
            float cap = others == 0 ? 0.8f : others <= 2 ? 0.45f : 0.36f;
            if (bigTotal > Count(layer)) cap = math.min(cap, 0.25f);   // other big fields share the ground
            // …unless the hero is so long that the others cannot sit beside it anyway (a running track): then centred.
            var heroLadder = Ladder(layer);
            float heroLen = 0f;
            bool autoSize = (_ctx.S(layer + "Size") ?? "auto") == "auto";
            for (int k = FirstRung(layer, heroLadder); k < heroLadder.Length; k++)
                if (!autoSize || heroLadder[k].Length * heroLadder[k].Width <= cap * _play.Area || k == heroLadder.Length - 1) { heroLen = heroLadder[k].Length; break; }
            bool alongU = math.abs(gv) >= math.abs(gu) * 0.8f;
            float extent = alongU ? u1 - u0 : v1 - v0;
            if (bigTotal > 1 && heroLen < extent * 0.6f)
            {
                if (math.abs(gv) >= math.abs(gu) * 0.8f) anchor += _axis * ((gu > 0f ? u0 : u1) - (u0 + u1) * 0.5f);
                else anchor += _side * ((gv > 0f ? v0 : v1) - (v0 + v1) * 0.5f);
            }
            var kind = layer == "tennisCourts" ? Kind.Court : Kind.Big;
            _hero = PlaceOne(layer, kind, anchor, null, cap);
            // The chosen hero does not fit (a running track on 1 ha): the next big field takes its role.
            if (_hero == null)
                foreach (var f in SportsParkLayer.BigFields)
                {
                    if (f == layer || !Wants(f)) continue;
                    _hero = PlaceOne(f, Kind.Big, anchor, null, cap, true, false);
                    if (_hero != null) { layer = f; kind = Kind.Big; break; }
                }
            if (_hero == null) return;
            Cluster(_hero, Count(layer), kind);
            // The side of the hero (group) facing the park: a long side, unless the field is nearly square.
            var box = GroupBox(_hero.Group, _hero.Rect.Axis);
            var toward = _playCentre - box.Center;
            if (math.lengthsq(toward) < 1f) toward = gate - box.Center;
            float best = float.MinValue;
            foreach (var n in new[] { box.Side, -box.Side, box.Axis, -box.Axis })
            {
                bool longSide = math.abs(math.dot(n, box.Side)) > 0.7f;
                float score = math.dot(math.normalizesafe(toward), n) + (longSide && box.HalfLength > box.HalfWidth * 1.25f ? 0.35f : 0f);
                if (score > best) { best = score; _heroFront = n; }
            }
        }

        private void PlaceBigFields()
        {
            foreach (var f in SportsParkLayer.BigFields)
            {
                if (!Wants(f) || (_hero != null && (f == _hero.Layer || InInfield(_hero.Layer, f)))) continue;
                var anchor = _hero != null ? _hero.Rect.Center : _playCentre;
                Func<float2, float> extra = null;
                if (_hero != null)
                {
                    // Stay in the hero's band (same side of the park), line up beside it.
                    float hv = math.dot(_hero.Rect.Center - _playCentre, _heroFront);
                    var hb = GroupBox(_hero.Group, new float2(-_heroFront.y, _heroFront.x));
                    var hc = hb.Center; var fr = _heroFront; float reach = hb.HalfLength;
                    // …and leave the hero's park-facing side to the terrace and clubhouse.
                    extra = p => 1.5f * math.abs(math.dot(p - _playCentre, fr) - hv)
                        + (math.dot(p - hc, fr) > 0f && math.abs(math.dot(p - hc, new float2(-fr.y, fr.x))) < reach ? 120f : 0f);
                }
                var first = PlaceOne(f, Kind.Big, anchor, extra, 0.18f);
                if (first != null) Cluster(first, Count(f), Kind.Big);
            }
        }

        private void PlaceTerrace(float slim)
        {
            var ctx = _ctx;
            bool club = ctx.B("changingHut"), terrace = ctx.B("terrace");
            if (!club && !terrace) return;
            // Front: the paved spectator terrace (touchline walk + benches); back: the clubhouse strip.
            // Depths grow with the site (a 1 ha park gets a slim terrace, a 6 ha park a generous one).
            float k = math.clamp(_span / 170f, 0.6f, 1.4f) * slim;
            _tDepth = (terrace ? math.max(S(16f, 9f) * k, 6f) : 5f);
            // (2026-10-29) Deep enough for the game's small stand behind the touchline walk when the site allows it.
            if (terrace && slim >= 1f) _tDepth = math.max(_tDepth, 2f * _hw + 1.2f + SportsProps.Small02.Depth + 1.2f);
            // (2026-10-29) A stadium's terrace takes a covered grandstand on a big site.
            if (terrace && ctx.S("subtype") == "stadium") _tDepth *= 1.45f;
            _cDepth = club ? math.max(S(14f, 9f) * k, 7f) : 0f;
            float depth = _tDepth + _cDepth, length;
            if (_hero != null)
            {
                // The hero's sides in order of preference (the park-facing long side first); the first with room wins.
                var hb = GroupBox(_hero.Group, _hero.Rect.Axis);
                var toward = math.normalizesafe(_playCentre - hb.Center, _heroFront);
                var sides = new List<(float score, float2 n)>();
                foreach (var n in new[] { hb.Side, -hb.Side, hb.Axis, -hb.Axis })
                {
                    bool longSide = math.abs(math.dot(n, hb.Side)) > 0.7f && hb.HalfLength > hb.HalfWidth * 1.25f;
                    sides.Add((math.dot(toward, n) + (longSide ? 0.6f : 0f) + (math.all(n == _heroFront) ? 0.3f : 0f), n));
                }
                sides.Sort((x, y) => y.score.CompareTo(x.score));
                foreach (var (_, n) in sides)
                {
                    if (_terrace != null) break;
                    var along = new float2(-n.y, n.x);
                    var box = GroupBox(_hero.Group, along);
                    float front = math.abs(math.dot(n, box.Side)) > 0.7f ? box.HalfWidth : box.HalfLength;
                    float hl = math.abs(math.dot(along, box.Axis)) > 0.7f ? box.HalfLength : box.HalfWidth;
                    length = math.clamp(hl * 1.6f, S(34f, 20f), S(120f, 60f));
                    for (int tries = 0; tries < 3 && _terrace == null; tries++, length *= 0.75f)
                        foreach (float slide in new[] { 0f, 0.25f, -0.25f, 0.5f, -0.5f })
                        {
                            var c = box.Center + n * (front + 0.4f + depth * 0.5f) + along * (slide * hl);
                            var r = new OrientedRect { Center = c, Axis = along, HalfLength = length * 0.5f, HalfWidth = depth * 0.5f };
                            if (Fits(r, "terrace", Kind.Structure, _hero.Group)) { _terrace = Add("", "terrace", Kind.Structure, r, false); _heroFront = n; break; }
                        }
                }
            }
            if (_terrace == null)
            {
                // No hero (neighbourhood courts) or no room beside it: a hub square in the middle of the park.
                length = S(32f, 18f); depth = S(24f, 14f);
                var anchor = _hasPlaza ? math.lerp(_plazaCentre, _playCentre, 0.65f) : _playCentre;
                if (Search(length, depth, anchor, null, true, "terrace", Kind.Structure, out var r))
                {
                    _terrace = Add("", "terrace", Kind.Structure, r, false);
                    _hub = _terrace;
                }
            }
        }

        private readonly List<(float2 a, float2 b)> _spine = new List<(float2, float2)>();

        /// <summary>The rectangle (axis <paramref name="axis"/>) enclosing every item of a group.</summary>
        private OrientedRect GroupBox(string group, float2 axis)
        {
            axis = math.normalizesafe(axis, _axis);
            var side = new float2(-axis.y, axis.x);
            float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
            foreach (var it in _items)
            {
                if (it.Group != group) continue;
                foreach (var q in it.Rect.Ring)
                {
                    float a = math.dot(q, axis), b = math.dot(q, side);
                    a0 = math.min(a0, a); a1 = math.max(a1, a); b0 = math.min(b0, b); b1 = math.max(b1, b);
                }
            }
            if (a0 > a1) return default;
            return new OrientedRect { Center = axis * (a0 + a1) * 0.5f + side * (b0 + b1) * 0.5f, Axis = axis, HalfLength = (a1 - a0) * 0.5f, HalfWidth = (b1 - b0) * 0.5f };
        }

        /// <summary>The spine's line from the entrance plaza (or main gate) to the clubhouse door, kept free for the courts.</summary>
        private void PlanSpine(bool reserve = true)
        {
            if (_terrace == null) return;
            float2 start = _hasPlaza ? _plaza.Center : _hasMain ? _main.Position : float2.zero;
            if (!_hasPlaza && !_hasMain) return;
            var r = _terrace.Rect;
            // The clubhouse door: the back edge's middle (hub: the side facing the entrance).
            float2 end = _hub != null ? Edge(r, start) : r.Center + _heroFront * (r.HalfWidth + 0.6f);
            var options = new List<List<(float2, float2)>> { new List<(float2, float2)> { (start, end) } };
            foreach (var dir in new[] { _axis, _side })
            {
                var v = start + dir * math.dot(end - start, dir);
                options.Add(new List<(float2, float2)> { (start, v), (v, end) });
                var w = end - dir * math.dot(end - start, dir);
                options.Add(new List<(float2, float2)> { (start, w), (w, end) });
            }
            foreach (var o in options)
            {
                bool ok = true;
                foreach (var (a, b) in o) if (math.distance(a, b) > 0.5f && !ClearPath(a, b, true)) { ok = false; break; }
                if (!ok) continue;
                foreach (var (a, b) in o)
                {
                    if (math.distance(a, b) < 0.5f) continue;
                    _spine.Add((a, b));

                }
                return;
            }
        }

        private void PlaceKids()
        {
            if (!_ctx.Input.HasChildTarget(RegionTags.Core)) return;
            // Real playground sets are big (a lone climbing frame needs a ~34 m clear disc with its benches).
            float L = math.max(S(60f, 30f), 38f), W = math.max(S(44f, 24f), 34f);
            var anchor = _terrace != null ? _terrace.Rect.Center : _hasPlaza ? _plazaCentre : _playCentre;
            Func<float2, float> extra = null;
            if (_terrace != null && _hero != null)
            {
                // Beside the clubhouse, never between the terrace and the hero.
                extra = p => math.dot(p - _terrace.Rect.Center, _heroFront) < 0f ? 60f : 0f;
            }
            if (Search(L, W, anchor, extra, true, "kids", Kind.Structure, out var r)) _kids = Add("", "kids", Kind.Structure, r, false);
        }

        private static readonly string[] CourtOrder = { "tennisCourts", "basketballCourt", "skateBowl", "volleyball", "tableTennis", "fitness" };

        /// <summary>Places the court clusters; returns the layers that found no room (not reported yet).</summary>
        private List<string> PlaceCourts(IEnumerable<string> layers)
        {
            var missing = new List<string>();
            float2 club = _terrace != null ? _terrace.Rect.Center : _hero != null ? _hero.Rect.Center + _heroFront * (_hero.Rect.HalfWidth + 12f) : _playCentre;
            float2 entry = _hasPlaza ? _plazaCentre : _hasMain ? _main.Position : _playCentre;
            foreach (var layer in layers)
            {
                if (!Wants(layer) || (_hero != null && _hero.Layer == layer)) continue;
                bool small = Array.IndexOf(SportsParkLayer.Small, layer) >= 0;
                var kind = small ? Kind.Small : Kind.Court;
                float2 anchor;
                Func<float2, float> extra = null;
                switch (layer)
                {
                    case "tennisCourts": anchor = club; break;                                         // club courts by the clubhouse
                    case "basketballCourt": case "skateBowl": anchor = math.lerp(entry, club, 0.35f); break;   // street sports by the entrance
                    case "volleyball": anchor = _kids != null ? _kids.Rect.Center : club; break;
                    case "tableTennis": anchor = _terrace != null ? _terrace.Rect.Center : entry; break;
                    default:
                        // Fitness stations along the loop: near the play field's edge.
                        anchor = entry;
                        extra = p => 2.5f * _play.BoundaryDistance(p, 60f);
                        break;
                }
                // Keep the hero's front (terrace side, spectators) clear of courts.
                if (_hero != null && _terrace != null && !small)
                {
                    var t = _terrace.Rect.Center; var fr = _heroFront;
                    var prev = extra;
                    extra = p => (prev != null ? prev(p) : 0f) + (math.abs(math.dot(p - t, fr)) < _terrace.Rect.HalfWidth + 6f
                        && math.abs(math.dot(p - t, new float2(-fr.y, fr.x))) < _terrace.Rect.HalfLength + 10f ? 40f : 0f);
                }
                Item first = PlaceOne(layer, kind, anchor, extra, 0.25f, true, false);
                if (first != null) Cluster(first, Count(layer), kind);
                else missing.Add(layer);
            }
            return missing;
        }

        // ───────────────────────── paths ─────────────────────────

        private void Paths()
        {
            var ctx = _ctx;
            string mode = ctx.S("promenade");
            bool loop = mode != "spine";
            if (loop) Loop();

            // Entrance plaza (paved) at the main gate.
            if (_hasPlaza)
            {
                ctx.Surface(_plaza.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);

            }


            // Spine: plaza → clubhouse, wide and tree-lined (its line was kept free before the courts were placed).
            if (_terrace != null && _spine.Count > 0)
                foreach (var (a, b) in _spine) Walk(a, b, NetRole.PathwayWide);
            else if (_terrace != null && (mode != "loop" || _hub != null))
                Connect(_terrace.Rect, NetRole.PathwayWide);
            // Touchline walk along the terrace's front edge (the hero's stands face it).
            if (_terrace != null && _hero != null && _hub == null)
            {
                var r = _terrace.Rect;
                var a = r.Local(-r.HalfLength + 1.5f, 0f); var b = r.Local(r.HalfLength - 1.5f, 0f);
                // The terrace's long axis runs along the hero; the walk is on its hero side.
                var toHero = -_heroFront;
                a += toHero * (r.HalfWidth - _hw - 0.4f); b += toHero * (r.HalfWidth - _hw - 0.4f);
                Walk(a, b, NetRole.Pathway);
            }
            // Gate spurs through the belt to the loop; without a loop, to the nearest walk (straight or with one bend).
            foreach (var g in _gates)
            {
                if (!EntranceKinds.Walks(g.Kind)) continue;
                if (_hasPlaza && math.distance(g.Position, _main.Position) < 0.5f && _spine.Count > 0 && _loopSegs.Count == 0) continue;
                _region.ClosestOnBoundary(g.Position, out int seg, out _);
                var inward = _region.InwardNormal(seg);
                // Straight in along the edge normal first (GateApproach, as every park path builder does).
                if (Approach(g.Position, _loopSegs.Count > 0 ? _loopSegs : _walks)) continue;
                if (_loopSegs.Count > 0 && NearestOn(_loopSegs, g.Position, out var foot) && math.distance(foot, g.Position) < _belt * 3f + 8f
                    && !_region.CrossesBoundary(g.Position, foot, true) && !HitsItems(g.Position, foot, 0.8f))
                    Walk(g.Position, foot, NetRole.Pathway);
                else if (!loop && _walks.Count > 0) JoinGate(g.Position, inward);
            }
            // Short connectors to every facility not already beside a walk.
            foreach (var it in _items)
            {
                if (!it.Place && it != _kids) continue;
                if (NearestWalk(it.Rect) <= 7f) continue;
                Connect(it.Rect, NetRole.Pathway);
            }
            if (_kids != null && NearestWalk(_kids.Rect) > 7f) Connect(_kids.Rect, NetRole.Pathway);

            if (ctx.B("fence"))
            {
                var fence = AssetMix.One(ctx.S("fenceAsset"));
                foreach (var run in Fences.Runs(_region, _gates, 3.75f)) ctx.StraightNet(run.c0, run.c1, NetRole.Fence, fence);
            }
        }

        /// <summary>The shared gate approach (s9): straight in along the edge normal, then a gentle leg to the network.</summary>
        private bool Approach(float2 gate, List<(float2 a, float2 b)> network)
        {
            if (network.Count == 0) return false;
            var route = GateApproach.Route(_region, gate, new List<(float2, float2)>(network), math.min(_belt + 4f, 18f), (p, q) => ClearPath(p, q, false));
            if (route == null || route.Count < 2) return route != null;
            for (int i = 1; i < route.Count; i++) Walk(route[i - 1], route[i], NetRole.Pathway);
            return true;
        }

        /// <summary>A gate without a loop joins the nearest reachable walk: straight, or in through the belt and along the field axes.</summary>
        private void JoinGate(float2 gate, float2 inward)
        {
            var into = gate + inward * math.min(_belt + 4f, 18f);
            var targets = new List<(float d, float2 q)>();
            foreach (var (a, b) in new List<(float2, float2)>(_walks))
            {
                var q = SegmentIndex.ClosestPoint(gate, a, b);
                targets.Add((math.distance(q, gate), q));
            }
            targets.Sort((x, y) => x.d.CompareTo(y.d));
            for (int i = 0; i < targets.Count && i < 8; i++)
            {
                var w = targets[i].q;
                if (targets[i].d < 1f) return;
                if (ClearPath(gate, w, false)) { Walk(gate, w, NetRole.Pathway); return; }
                if (!ClearPath(gate, into, false)) continue;
                foreach (var dir in new[] { _axis, _side })
                {
                    var v = into + dir * math.dot(w - into, dir);
                    if (ClearPath(into, v, false) && ClearPath(v, w, false)) { Walk(gate, into, NetRole.Pathway); Walk(into, v, NetRole.Pathway); Walk(v, w, NetRole.Pathway); return; }
                }
                if (ClearPath(into, w, false)) { Walk(gate, into, NetRole.Pathway); Walk(into, w, NetRole.Pathway); return; }
            }
        }

        private static OrientedRect Shrink(OrientedRect r, float d) =>
            new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = math.max(0.5f, r.HalfLength - d), HalfWidth = math.max(0.5f, r.HalfWidth - d) };

        private void Loop()
        {
            var ring = Inset(_region, _belt + _hw);
            if (ring == null) return;
            var pts = FreeSpace.DouglasPeuckerClosed(ring.Ring, 1.2f);
            if (pts == null || pts.Length < 3) pts = ring.Ring;
            pts = Poly.EnsureCcw(Poly.Clean(pts, 2f));
            int n = pts.Length;
            if (n < 3) return;
            float rad = S(16f, 7f);
            var cutIn = new float2[n]; var cutOut = new float2[n];
            for (int i = 0; i < n; i++)
            {
                var prev = pts[(i + n - 1) % n]; var p = pts[i]; var next = pts[(i + 1) % n];
                float c = math.min(rad, math.min(math.distance(prev, p), math.distance(p, next)) * 0.45f);
                cutIn[i] = p + math.normalizesafe(prev - p) * c;
                cutOut[i] = p + math.normalizesafe(next - p) * c;
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                // Rounded corner at i, then the straight to the next corner.
                var p = pts[i];
                if (math.distance(cutIn[i], cutOut[i]) > 0.8f && !Blocked(cutIn[i], cutOut[i]))
                {
                    var b = math.lerp(cutIn[i], p, 0.55f); var c = math.lerp(cutOut[i], p, 0.55f);
                    _ctx.CurvedNet(cutIn[i], b, c, cutOut[i], NetRole.Pathway, _path);
                    float2 prev = cutIn[i];
                    for (int k = 1; k <= 4; k++)
                    {
                        float t = k / 4f, u = 1f - t;
                        var q = u * u * u * cutIn[i] + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * cutOut[i];
                        Index(prev, q, NetRole.Pathway);
                        _loopSegs.Add((prev, q));
                        prev = q;
                    }
                }
                if (math.distance(cutOut[i], cutIn[j]) > 0.8f && !Blocked(cutOut[i], cutIn[j]))
                {
                    _ctx.StraightNet(cutOut[i], cutIn[j], NetRole.Pathway, _path);
                    Index(cutOut[i], cutIn[j], NetRole.Pathway);
                    _loopSegs.Add((cutOut[i], cutIn[j]));
                }
            }
        }

        private void Index(float2 a, float2 b, NetRole role)
        {
            float hw = Composer.DefaultHalfWidth(role);
            _occ.TakeCorridor(a, b, hw, 100);
            _occ.Paths.Add(a, b, hw, 100);
            _walks.Add((a, b));
        }

        private void Walk(float2 a, float2 b, NetRole role)
        {
            if (math.distance(a, b) < 1.5f) return;
            _ctx.StraightNet(a, b, role, role == NetRole.PathwayWide ? AssetMix.One("") : _path);
            Index(a, b, role);
        }

        private static bool NearestOn(List<(float2 a, float2 b)> segs, float2 p, out float2 foot)
        {
            foot = p; float best = float.MaxValue;
            foreach (var (a, b) in segs)
            {
                var q = SegmentIndex.ClosestPoint(p, a, b);
                float d = math.distancesq(q, p);
                if (d < best) { best = d; foot = q; }
            }
            return best < float.MaxValue;
        }

        private float NearestWalk(OrientedRect r)
        {
            float best = float.MaxValue;
            var ring = r.Ring;
            foreach (var (a, b) in _walks)
                for (int i = 0; i < 4; i++)
                {
                    var m = math.lerp(ring[i], ring[(i + 1) % 4], 0.5f);
                    best = math.min(best, math.sqrt(SegmentIndex.DistSq(m, a, b)));
                }
            return best;
        }

        private static float2 Edge(OrientedRect r, float2 from)
        {
            // Middle of the side facing "from", pulled 0.5 m out.
            var ring = r.Ring; float best = float.MaxValue; float2 m = r.Center, n = float2.zero;
            for (int i = 0; i < 4; i++)
            {
                var mid = math.lerp(ring[i], ring[(i + 1) % 4], 0.5f);
                float d = math.distance(mid, from);
                if (d < best) { best = d; m = mid; n = math.normalizesafe(mid - r.Center); }
            }
            return m + n * 0.6f;
        }

        private bool HitsItems(float2 a, float2 b, float margin, OrientedRect? except = null)
        {
            foreach (var it in _items)
            {
                if (except.HasValue && it.Rect.Center.Equals(except.Value.Center)) continue;
                var g = new OrientedRect { Center = it.Rect.Center, Axis = it.Rect.Axis, HalfLength = it.Rect.HalfLength + margin, HalfWidth = it.Rect.HalfWidth + margin };
                if (SegHitsRect(a, b, g)) return true;
            }
            return false;
        }

        private static bool SegHitsRect(float2 a, float2 b, OrientedRect r)
        {
            bool Inside(float2 p) => math.abs(math.dot(p - r.Center, r.Axis)) < r.HalfLength && math.abs(math.dot(p - r.Center, r.Side)) < r.HalfWidth;
            if (Inside(a) || Inside(b) || Inside(math.lerp(a, b, 0.5f))) return true;
            var ring = r.Ring;
            for (int i = 0; i < 4; i++) if (Poly.SegmentsIntersect(a, b, ring[i], ring[(i + 1) % 4])) return true;
            return false;
        }

        /// <summary>A walk a→b avoiding the facilities: straight, else with one bend along the field axes.</summary>
        private bool Route(float2 a, float2 b, NetRole role, bool endOnTarget)
        {
            if (ClearPath(a, b, endOnTarget)) { Walk(a, b, role); return true; }
            foreach (var dir in new[] { _axis, _side })
            {
                var v1 = a + dir * math.dot(b - a, dir);
                if (ClearPath(a, v1, false) && ClearPath(v1, b, endOnTarget)) { Walk(a, v1, role); Walk(v1, b, role); return true; }
            }
            return false;
        }

        private bool HitsItemsExcept(float2 a, float2 b, bool endOnTarget) => HitsItems(a, b, 0.6f, endOnTarget ? _terrace?.Rect : null);

        private bool ClearPath(float2 a, float2 b, bool endOnTarget)
        {
            if (math.distance(a, b) < 0.5f) return true;
            if (_region.CrossesBoundary(a, b, true) || !_region.Contains(math.lerp(a, b, 0.5f))) return false;
            if (Blocked(a, b)) return false;
            // The last metre may touch the target's own rectangle.
            var bb = endOnTarget ? math.lerp(a, b, math.max(0f, 1f - 1.2f / math.max(1f, math.distance(a, b)))) : b;
            return !HitsItems(a, bb, 0.6f);
        }

        /// <summary>True when a walk a→b would cross something reserved or standing (earlier layers, buildings).</summary>
        private bool Blocked(float2 a, float2 b)
        {
            float len = math.distance(a, b);
            for (float t = 0f; t <= len; t += 1.5f)
            {
                var p = math.lerp(a, b, len < 1e-3f ? 0f : t / len);
                if (_occ.Discs.Overlaps(p, _hw, 0f) || _occ.HitsPolygon(p, _hw)) return true;
            }
            return false;
        }

        /// <summary>Connector from a facility's nearest side to the nearest walk, when it stays clear.</summary>
        private bool Connect(OrientedRect r, NetRole role)
        {
            if (_walks.Count == 0) return false;
            var ring = r.Ring;
            var options = new List<(float d, float2 from, float2 to)>();
            for (int i = 0; i < 4; i++)
            {
                var mid = math.lerp(ring[i], ring[(i + 1) % 4], 0.5f);
                var outward = math.normalizesafe(mid - r.Center);
                var from = mid + outward * 0.6f;
                if (NearestOn(_walks, from, out var foot)) options.Add((math.distance(from, foot), from, foot));
            }
            options.Sort((x, y) => x.d.CompareTo(y.d));
            foreach (var (d, from, to) in options)
            {
                if (d < 1f) return true;
                if (d > _span * 0.6f) break;
                if (ClearPath(to, from, true)) { Walk(to, from, role); return true; }
                // One bend along the field axes.
                foreach (var dir in new[] { _axis, _side })
                {
                    var v = to + dir * math.dot(from - to, dir);
                    if (ClearPath(to, v, false) && ClearPath(v, from, true)) { Walk(to, v, role); Walk(v, from, role); return true; }
                }
            }
            return false;
        }

        // ───────────────────────── terrace ground ─────────────────────────

        private FieldGround _terraceGround;

        /// <summary>
        /// (2026-10-29) The spectator terrace is one level platform (its stands, kiosk and clubhouse stand level on rough
        /// ground: before, its benches stood on 12-13 % slopes of the smoothed paving), blended back to the ground at its edge.
        /// </summary>
        private void LevelTerrace()
        {
            if (_terrace == null) return;
            var r = _terrace.Rect;
            float apron = math.clamp(math.min(r.HalfLength, r.HalfWidth) * 0.2f, 1.5f, 3.5f);
            var pad = new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = math.max(1f, r.HalfLength - apron), HalfWidth = math.max(1f, r.HalfWidth - apron) };
            var g = FieldGround.Level(_ctx, pad, r, true);
            if (!g.Levelled) return;
            _terraceGround = g;
            var ring = r.Ring;
            _ctx.Ground.Override(math.min(math.min(ring[0], ring[1]), math.min(ring[2], ring[3])), math.max(math.max(ring[0], ring[1]), math.max(ring[2], ring[3])),
                g.HeightAfter, p => math.abs(math.dot(p - r.Center, r.Axis)) <= r.HalfLength && math.abs(math.dot(p - r.Center, r.Side)) <= r.HalfWidth);
            _ctx.Plan.Facts["terraceLevelled"] = 1;
        }

        // ───────────────────────── dressing ─────────────────────────

        private void Dress()
        {
            var ctx = _ctx;
            uint seed = ctx.Seed;
            // Terrace / hub: paving, pavilion, kiosk, spectator benches facing the field, shade trees behind.
            if (_terrace != null)
            {
                var r = _terrace.Rect;
                bool hub = _hub != null;
                // "back" points away from the field (hub: across the square); the terrace is the front strip.
                var back = hub ? r.Side : _heroFront;
                var along = new float2(-back.y, back.x);
                float front = hub ? r.HalfWidth * 2f : _tDepth;
                var pave = new OrientedRect { Center = r.Center - back * (r.HalfWidth - front * 0.5f), Axis = along, HalfLength = r.HalfLength, HalfWidth = front * 0.5f };
                ctx.Surface(pave.Ring, SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving);
                // (2026-10-29) Grandstands on the terrace facing the field: the game's own stands, as deep as the front strip
                // behind the touchline walk allows (covered grandstands on a stadium's big terrace); benches only on a hub.
                int placedStands = 0;
                if (!hub && _hero != null && string.IsNullOrEmpty(ctx.S("benchAsset")))
                {
                    float walk = 2f * _hw + 1.2f;
                    var frontC = r.Center - back * (r.HalfWidth - walk);
                    float depth = front - walk - 0.4f;
                    var toField = r.Center - back * (r.HalfWidth + 10f);
                    placedStands = SportsProps.StandRow(ctx, _occ, frontC - along * (r.HalfLength - 1f), frontC + along * (r.HalfLength - 1f), toField, depth,
                        ctx.S("subtype") == "stadium", Hashing.Mix(seed, 0x57A5u));
                    ctx.Plan.Facts["terraceStands"] = placedStands;
                }
                // Clubhouse (changing pavilion) in the middle of the back strip, kiosk beside it.
                // The spine arrives at the middle of the back edge: pavilion on one side, kiosk on the other.
                float flank = math.min(r.HalfLength * 0.4f, S(11f, 6.5f));
                float2 club = (hub ? r.Center + back * (r.HalfWidth * 0.45f) : r.Center + back * (r.HalfWidth - _cDepth * 0.5f)) - along * flank;
                if (ctx.B("changingHut"))
                {
                    string pav = LeisureKit.Category(ctx, "", AssetCategories.Shelter, AssetCategories.Restroom, AssetCategories.Kiosk);
                    if (pav != null)
                        foreach (float shift in new[] { 0f, -0.6f * flank, 2f * flank, -1.2f * flank })
                        {
                            var at = club + along * shift;
                            if (!LeisureKit.Put(ctx, _occ, at, LeisureKit.Yaw(at, at - back), ObjectRole.Furniture, AssetMix.One(""), pav, 2.5f, Hashing.Mix(seed, 0xC1Bu), 0.3f, 0.3f)) continue;
                            ctx.Plan.Facts["clubhouse"] = 1;
                            club = at;
                            break;
                        }
                    // Changing rooms / toilets beside the pavilion.
                    string wc = LeisureKit.Category(ctx, "", AssetCategories.Restroom);
                    var q = club - along * math.min(r.HalfLength * 0.35f, S(9f, 5f));
                    if (wc != null) LeisureKit.Put(ctx, _occ, q, LeisureKit.Yaw(q, q - back), ObjectRole.Furniture, AssetMix.One(""), wc, 1.5f, Hashing.Mix(seed, 0xC1Cu), 0.3f, 0.3f);
                }
                if (ctx.B("kiosk"))
                {
                    string k = LeisureKit.Category(ctx, "", AssetCategories.Kiosk);
                    var p = club + along * (2f * flank);
                    if (k != null) LeisureKit.Put(ctx, _occ, p, LeisureKit.Yaw(p, p - back), ObjectRole.Furniture, AssetMix.One(""), k, 1.5f, Hashing.Mix(seed, 0x105Au), 0.3f, 0.3f);
                }
                if (placedStands == 0)
                {
                    int benches = math.clamp((int)(r.HalfLength * 2f / S(6f, 3.5f)), 2, 16);
                    float bv = hub ? -r.HalfWidth * 0.55f : -(r.HalfWidth - front) - front * 0.25f;
                    for (int i = 0; i < benches; i++)
                    {
                        float t = (i / (float)math.max(1, benches - 1) - 0.5f) * (r.HalfLength * 1.7f);
                        var p = r.Center + along * t + back * bv;
                        var face = hub ? p + back : p - back;
                        LeisureKit.Put(ctx, _occ, p, LeisureKit.Yaw(p, face), ObjectRole.Bench, _bench, null, 0.9f, Hashing.Mix(seed, 0xBE5u + (uint)i), 0.2f, 0.2f);
                    }
                }
                // Shade trees along the terrace's back edge, framing the clubhouse.
                int shade = math.clamp((int)(r.HalfLength * 2f / S(12f, 7f)), 2, 10);
                for (int i = 0; i < shade; i++)
                {
                    float t = (i / (float)math.max(1, shade - 1) - 0.5f) * (r.HalfLength * 1.9f);
                    if (math.abs(t + flank) < S(8f, 5f) && ctx.B("changingHut")) continue;
                    Tree(r.Center + back * (r.HalfWidth + 1.5f) + along * t, 0x5AD0u + (uint)i, 3);
                }
                Bins(r.Center - back * (r.HalfWidth - front * 0.5f) + along * (r.HalfLength * 0.92f), 0xB10u);
                Bins(r.Center - back * (r.HalfWidth - front * 0.5f) - along * (r.HalfLength * 0.92f), 0xB12u);
            }

            // Entrance plaza: bike racks in a row, a noticeboard, bins, four corner trees.
            if (_hasPlaza)
            {
                var r = _plaza;
                int racks = ctx.I("bikeRacks");
                if (racks > 0)
                {
                    string cat = LeisureKit.Category(ctx, "", AssetCategories.BikeRack);
                    if (cat != null)
                    {
                        int placed = 0;
                        for (int i = 0; i < racks; i++)
                        {
                            float u = -r.HalfLength + 2f + i * 1.9f;   // (rf-sports) 1.9 m: the game's rack is 1.5 m long
                            if (u > -1.5f) u += 3f;   // keep the middle (the walk) clear
                            if (u > r.HalfLength - 1.5f) break;
                            var p = r.Local(u, -r.HalfWidth + 1.4f);
                            // (rf-sports) Single racks 1.4 m apart: the automatic bike pool starts with the big bicycle shelter,
                            // and six shelters overlapped each other (every build kept them only by anarchy).
                            if (LeisureKit.Put(ctx, _occ, p, r.Yaw + math.PI * 0.5f, ObjectRole.Furniture, AssetMix.One("", "BikeRack01", "BikeRack02", "CommercialBikeRack01"), cat, 0.5f, Hashing.Mix(seed, 0xB1Cu), 0.1f, 0.1f)) placed++;
                        }
                        ctx.Plan.Facts["bikeRacks"] = placed;
                    }
                }
                string board = LeisureKit.Category(ctx, "", AssetCategories.SignNoticeboard, AssetCategories.Wayfinding);
                if (board != null) LeisureKit.Put(ctx, _occ, r.Local(r.HalfLength - 2.5f, r.HalfWidth - 2f), r.Yaw, ObjectRole.Furniture, AssetMix.One(""), board, 0.6f, Hashing.Mix(seed, 0xB0Au), 0.1f, 0.2f);
                Bins(r.Local(-r.HalfLength + 1.5f, r.HalfWidth - 1.5f), 0xB11u);
                foreach (var (u, w) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                    Tree(r.Local(u * (r.HalfLength + 3f), w * (r.HalfWidth - 1f)), 0x9A0u + (uint)(u * 3 + w + 5), 2);
                if (r.HalfLength >= 15f && r.HalfWidth >= 9f) GrandPlaza(r, seed);
            }



        }

        /// <summary>
        /// A big park's forecourt (P2-4): the walks cross its middle, so each flank becomes a grove of trees in a grid (benches
        /// between them facing the middle), with a fountain in each flank on the biggest squares and lamps round the edge.
        /// Everything goes through the occupancy, so nothing lands on a walk.
        /// </summary>
        private void GrandPlaza(OrientedRect r, uint seed)
        {
            var ctx = _ctx;
            float mid = math.max(7f, r.HalfLength * 0.18f);   // the clear middle the walks cross
            float u0 = mid + 2.5f, u1 = r.HalfLength - 3f, v0 = -r.HalfWidth + 4.5f, v1 = r.HalfWidth - 3f;
            if (u1 - u0 < 6f) return;
            bool fountains = r.HalfLength >= 30f && r.HalfWidth >= 15f && AssetAvailability.Has(AssetCategories.CenterpieceFountain);
            float fu = (u0 + u1) * 0.5f, fv = (v0 + v1) * 0.5f;
            int trees = 0, benches = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                if (fountains)
                {
                    var c = r.Local(side * fu, fv);
                    if (LeisureKit.Put(ctx, _occ, c, r.Yaw, ObjectRole.Centerpiece, AssetMix.One("", "LargeFountain01", "LargeFountain03"), AssetCategories.CenterpieceFountain, 5.2f, Hashing.Mix(seed, 0xF0A0u + (uint)(side + 1)), 0.5f, 0.3f))
                        ctx.Plan.Facts["plazaFountains"] = (ctx.Plan.Facts.TryGetValue("plazaFountains", out var nf) ? nf : 0) + 1;
                }
                const float step = 8f;
                for (float v = v0; v <= v1 + 0.1f; v += step)
                    for (float u = u0; u <= u1 + 0.1f; u += step)
                    {
                        var p = r.Local(side * u, v);
                        if (fountains && math.distance(p, r.Local(side * fu, fv)) < 8.5f) continue;
                        if (Tree(p, 0x9B00u + (uint)(trees * 7 + side + 3), 2)) trees++;
                        // a bench between this tree and the next one toward the middle, looking at the walks
                        var b = r.Local(side * (u - step * 0.5f), v);
                        if (u - step * 0.5f > mid + 1f && LeisureKit.Put(ctx, _occ, b, LeisureKit.Yaw(b, r.Local(0f, v)), ObjectRole.Bench, _bench, null, 1.1f, Hashing.Mix(seed, 0xBE70u + (uint)benches), 0.3f, 0.3f))
                            benches++;
                    }
            }
            string lamp = LeisureKit.Category(ctx, "", AssetCategories.Lamp);
            if (lamp != null)
                foreach (var (u, v) in new[] { (-mid, -r.HalfWidth + 1.2f), (mid, -r.HalfWidth + 1.2f), (-mid, r.HalfWidth - 1.2f), (mid, r.HalfWidth - 1.2f),
                    (-r.HalfLength + 1.2f, 0f), (r.HalfLength - 1.2f, 0f) })
                    LeisureKit.Put(ctx, _occ, r.Local(u, v), r.Yaw, ObjectRole.Lamp, AssetMix.One(""), lamp, 0.4f, Hashing.Mix(seed, 0x1A40u + (uint)(u * 3f + v)), 0.2f, 0.2f);
            ctx.Plan.Facts["plazaTrees"] = trees;
            ctx.Plan.Facts["plazaBenches"] = benches;
        }

        private void PathFurniture()
        {
            var ctx = _ctx;
            float interval = S(42f, 18f);
            float total = 0f;
            foreach (var (a, b) in _walks) total += math.distance(a, b);
            int wanted = math.clamp((int)(total / interval), 0, 160);
            var picks = PathAnchors.Pick(PathAnchors.Beside(_occ, 1.3f, math.clamp(interval * 0.35f, 2f, 6f), 0.4f), wanted, interval * 0.6f, false);
            int n = 0;
            foreach (var a in picks)
                if (LeisureKit.Put(ctx, _occ, a.Position, LeisureKit.Yaw(a.Position, a.Toward), ObjectRole.Bench, _bench, null, 1.3f, Hashing.Mix(ctx.Seed, 0xBE50u + (uint)n))) n++;
            if (ctx.B("lamps"))
            {
                float li = S(34f, 16f), next = li * 0.5f;
                int k = 0;
                foreach (var (a, b) in _walks)
                {
                    float len = math.distance(a, b);
                    if (len < 1e-3f) continue;
                    var d = (b - a) / len; var nrm = new float2(-d.y, d.x);
                    for (; next <= len; next += li)
                    {
                        var p = a + d * next + nrm * ((k++ & 1) == 0 ? 1f : -1f) * (_hw + 0.9f);
                        LeisureKit.Put(ctx, _occ, p, 0f, ObjectRole.Lamp, AssetMix.One(""), null, 0.5f, Hashing.Mix(ctx.Seed, 0x1A40u + (uint)k), 0.4f, 0.2f);
                    }
                    next -= len;
                }
            }
        }

        private void Bins(float2 p, uint salt)
        {
            if (!AssetAvailability.Has(AssetCategories.Bin)) return;
            LeisureKit.Put(_ctx, _occ, p, 0f, ObjectRole.Bin, AssetMix.One(""), null, 0.6f, Hashing.Mix(_ctx.Seed, salt), 0.3f, 0.2f);
        }

        /// <summary>The trees a sports park plants when the player chose none (the clean game's broadleaf trees).</summary>
        public static readonly string[] DefaultTrees = { "OakTree01", "NA_LondonPlaneTree01", "NA_LindenTree01", "BirchTree01", "EU_ChestnutTree01" };

        private bool Tree(float2 p, uint salt, int age, float radius = 2.4f)
        {
            var ids = _ctx.L("treeSpecies");
            if (!AssetAvailability.Has(AssetCategories.Tree) && (ids == null || ids.Length == 0)) return false;
            uint v = Hashing.Mix(_ctx.Seed, salt * 31u + 7u);
            if (!_occ.Free(p, radius, 1.5f, 0.4f)) return false;
            // grove trees stand close (radius 1.6) but a crown never hangs over space the recipe reserved
            if (_ctx.Input.Reserved != null && _ctx.Input.Reserved.Blocks(p, 2.4f)) return false;
            // (2026-10-29, the user: "maybe the trees need more variety") No species chosen: the game's own broadleaf trees in
            // clumps of one species (about 28 m), the odd one mixed in, instead of one automatic species for the whole park.
            // The designed ground drops each tree's own leaves (Decals/Dynamic/Weathering).
            uint pick = v;
            if (ids == null || ids.Length == 0)
            {
                ids = DefaultTrees;
                pick = Hashing.Unit(Hashing.Mix(v, 0x7EEu)) < 0.8f ? Hashing.Mix(_ctx.Seed, (int)math.floor(p.x / 28f), (int)math.floor(p.y / 28f)) : v;
            }
            _ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ids, pick), v, age);
            _occ.Take(p, radius, 3);
            return true;
        }

        // ───────────────────────── landscape ─────────────────────────

        private void Landscape()
        {
            var ctx = _ctx;
            int age = 0;
            // Tree belt: rows along the outline (staggered double row for "double").
            string belt = ctx.S("treeBelt");
            if (belt != "none")
            {
                float spacing = S(13f, 7f);
                var rows = belt == "double" ? new[] { 0.32f, 0.72f } : new[] { 0.5f };
                for (int ri = 0; ri < rows.Length; ri++)
                {
                    var ring = Inset(_region, _belt * rows[ri]);
                    if (ring == null) continue;
                    float carry = ri * spacing * 0.5f;
                    var pts = ring.Ring;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                        float len = math.distance(a, b);
                        if (len < 0.01f) continue;
                        float t = carry;
                        for (; t < len; t += spacing)
                        {
                            var p = math.lerp(a, b, t / len);
                            // Gentle irregularity: a planted belt, not a hedge row.
                            uint h = Hashing.Mix(ctx.Seed, (int)(p.x * 3f), (int)(p.y * 5f));
                            p += new float2(Hashing.Unit(h) - 0.5f, Hashing.Unit(h * 7u + 1u) - 0.5f) * spacing * 0.3f;
                            Tree(p, h, 1 + (age++ % 3));
                        }
                        carry = t - len;
                    }
                }
            }

            // Spine avenue: trees both sides.
            foreach (var n in ctx.Plan.Nets)
            {
                if (n.Role != NetRole.PathwayWide) continue;
                var a = n.A.xz; var b = n.D.xz;
                float len = math.distance(a, b);
                if (len < 8f) continue;
                var d = (b - a) / len; var nrm = new float2(-d.y, d.x);
                float step = S(11f, 6.5f);
                for (float t = step * 0.5f; t < len - 2f; t += step)
                    for (int s = -1; s <= 1; s += 2)
                        Tree(a + d * t + nrm * s * (Composer.DefaultHalfWidth(NetRole.PathwayWide) + 2.6f), 0xA7E0u + (uint)(t * 10f) + (uint)(s + 1), 2);
            }

            // Gaps between zones: berms and groves in the free pieces of the play field.
            var free = FreeSpace.Pieces(_region, p => _occ.Corridors.Overlaps(p, 1.5f) || _occ.HitsPolygon(p, 1f) || InItem(p, 2f), 3f, 120f, ctx.Cancel);
            int bermCount = 0, groveTrees = 0;
            float density = ctx.F("treeDensity");
            for (int i = 0; i < free.Count && i < 30; i++)
            {
                var piece = LayerRegion.From(free[i]);
                if (piece == null) continue;
                piece.PrincipalAxes(out var c, out var major, out float majorSpan, out float minorSpan);
                bool inBelt = _region.BoundaryDistance(c, _belt + 3f) < _belt;
                if (ctx.B("berms") && !inBelt && piece.Area > 260f && minorSpan > 9f && bermCount < 8)
                {
                    if (Berm(piece, c, major, majorSpan, minorSpan, (uint)i)) bermCount++;
                }
                groveTrees += Groves(piece, density, (uint)i);
            }
            ctx.Plan.Facts["berms"] = bermCount;
            ctx.Plan.Facts["groveTrees"] = groveTrees;
        }

        /// <summary>
        /// Groves: tree groups of 3–9 around spaced centres (open mown lawn between them), not an even scatter.
        /// Group spacing and size follow the density slider; centres keep off the facilities' buffers.
        /// </summary>
        private int Groves(LayerRegion piece, float density, uint salt)
        {
            if (density <= 0f) return 0;
            var ctx = _ctx;
            int wantTrees = (int)(piece.Area / 10000f * density * 2.2f);
            if (wantTrees <= 0) return 0;
            float spacing = math.clamp(S(46f, 24f) * math.sqrt(30f / math.max(5f, density)), 18f, 70f);
            var centres = new List<float2>();
            uint seed = Hashing.Mix(ctx.Seed, 0x6A00u + salt * 13u);
            foreach (var c in Scatter.Candidates(piece, 4f, seed, 600, ctx.Cancel))
            {
                if (InItem(c, math.min(_zoneGap * 0.5f, 5f) + 3f)) continue;
                bool far = true;
                foreach (var q in centres) if (math.distancesq(q, c) < spacing * spacing) { far = false; break; }
                if (far) centres.Add(c);
            }
            int placed = 0, k = 0;
            foreach (var c in centres)
            {
                if (placed >= wantTrees) break;
                uint h = Hashing.Mix(seed, (uint)k++);
                int n = 3 + (int)(Hashing.Unit(h) * 7f);
                float r0 = 3.3f;
                for (int j = 0; j < n && placed < wantTrees; j++)
                {
                    // Phyllotaxis with jitter: a natural clump, the biggest tree in the middle.
                    float a = j * 2.39996f + Hashing.Unit(h + 1u) * 6.28f, rr = r0 * math.sqrt(j) * (0.85f + 0.3f * Hashing.Unit(Hashing.Mix(h, (uint)j)));
                    var p = c + new float2(math.cos(a), math.sin(a)) * rr;
                    if (!piece.Contains(p) || InItem(p, math.min(_zoneGap * 0.35f, 3.5f))) continue;
                    if (Tree(p, h + (uint)j * 7u, j == 0 ? 3 : 1 + (int)((h >> (j & 7)) % 3u), 1.6f)) placed++;
                }
            }
            return placed;
        }

        private bool InItem(float2 p, float margin)
        {
            foreach (var it in _items)
            {
                var r = it.Rect;
                if (math.abs(math.dot(p - r.Center, r.Axis)) < r.HalfLength + margin && math.abs(math.dot(p - r.Center, r.Side)) < r.HalfWidth + margin) return true;
            }
            if (_hasPlaza && math.abs(math.dot(p - _plaza.Center, _plaza.Axis)) < _plaza.HalfLength + margin && math.abs(math.dot(p - _plaza.Center, _plaza.Side)) < _plaza.HalfWidth + margin) return true;
            return false;
        }

        /// <summary>A low landscaped berm along a free gap: two nested Level rings rising to ~1.2 m, blended at its foot.</summary>
        private bool Berm(LayerRegion piece, float2 c, float2 major, float majorSpan, float minorSpan, uint salt)
        {
            var ctx = _ctx;
            float b = math.min(minorSpan * 0.3f, 7f), a = math.min(math.min(majorSpan * 0.36f, 40f), b * 2.6f);
            if (a < 6f || b < 3f) return false;
            var side = new float2(-major.y, major.x);
            float2[] Ellipse(float k)
            {
                var pts = new float2[16];
                for (int i = 0; i < 16; i++)
                {
                    float t = i * 2f * math.PI / 16f;
                    pts[i] = c + major * (math.cos(t) * a * k) + side * (math.sin(t) * b * k);
                }
                return pts;
            }
            var outer = Ellipse(1f);
            foreach (var q in outer) if (!piece.Contains(q)) return false;
            float h0 = ctx.H(c);
            float height = math.clamp(b * 0.22f, 0.6f, 1.4f);
            float edge = _region.BoundaryDistance(c, 40f);
            height = math.min(height, edge * 0.25f);
            if (height < 0.4f) return false;
            foreach (var (k, f) in new[] { (1f, 0.4f), (0.62f, 0.75f), (0.3f, 1f) })
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(Poly.EnsureCcw(Ellipse(k)), ctx.H), Kind = TerraformKind.Level, TargetHeight = h0 + height * f, Permanent = true });
            bool warned = false;
            foreach (var i in ctx.Plan.Issues) if (i.Key == TerrainShaper.PermanentKey) { warned = true; break; }
            if (!warned) ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
            // A few trees on the crest.
            for (int i = -1; i <= 1; i++) Tree(c + major * (i * a * 0.5f), 0xBE40u + salt * 5u + (uint)(i + 1), 2);
            return true;
        }

        // ───────────────────────── regions ─────────────────────────

        private void Regions()
        {
            var ctx = _ctx;
            int k = 0;
            foreach (var it in _items)
            {
                if (!it.Place) continue;
                string place = Amenities.Place(it.Layer);
            {
                // (rf-sports) The park's court surface (a tennis club plays on clay) rides on the place as a tag.
                string court = ctx.S("tennisSurface");
                if (it.Layer == "tennisCourts" && !string.IsNullOrEmpty(court) && court != "hard")
                    ctx.AddRegion(it.Rect.Ring, place + ":" + k++, place, SportsParkLayer.SizedTag, FieldAddOn.SurfaceTagPrefix + court);
                else if (it == _hero && _terrace != null && _hub == null && ctx.Plan.Facts.TryGetValue("terraceStands", out var ts) && ts > 0)
                    ctx.AddRegion(it.Rect.Ring, place + ":" + k++, place, SportsParkLayer.SizedTag, FieldAddOn.HostStandsTag);
                else ctx.AddRegion(it.Rect.Ring, place + ":" + k++, place, SportsParkLayer.SizedTag);
            }
            }
            if (_kids != null) ctx.AddRegion(_kids.Rect.Ring, "core:clubyard", RegionTags.Core, RegionTags.Green);
            var pieces = FreeSpace.Pieces(_region, p => _occ.Corridors.Overlaps(p, 0.75f) || _occ.HitsPolygon(p, 0.5f) || InItem(p, 0.5f), 3f, 150f, ctx.Cancel);
            for (int i = 0; i < pieces.Count && i < 24; i++)
            {
                if (_kids == null && i == 0) ctx.AddRegion(pieces[i], "green:0", RegionTags.Green, RegionTags.Core);
                else ctx.AddRegion(pieces[i], "green:" + i, RegionTags.Green);
            }
            if (_kids == null && pieces.Count == 0) ctx.AddRegion(_region.Ring, "green:0", RegionTags.Green, RegionTags.Core);
        }
    }
}

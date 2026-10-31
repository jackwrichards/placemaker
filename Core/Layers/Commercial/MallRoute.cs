// rf-shops3 — the player's entrances routed into the site's own network (ShopEntrances). A composition in entrance mode
// lays out an internal network (a ring drive, rungs, rows held, kept land) and attaches nothing to the streets itself;
// every way in comes from a placed entrance: straight in from the outline to the first lane it meets, cutting a gap in
// whatever kept land or held row is in the way (the drive through the planted frontage, the alley through a row of
// shops). Car kinds: an alley kind is a real lane from the street (a junction); entrance / entry / exit an invisible drive
// from the kerb (the street is left as it is; one-way kinds get arrows) on a strip of asphalt; footpaths a walk from the
// street to the lane (a crosswalk at the node) or to the spine's front end when one is near.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Parking;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        /// <summary>The player's entrances (entrance mode), null otherwise: the composition then attaches its own lanes to the streets as before.</summary>
        public List<ShopEntrances.Gate> Gates;
        public bool EntranceMode => Gates != null;
        /// <summary>(shops4) Laid out by LotCentre (the lot first, the shops behind it).</summary>
        public bool LotFirstMode;

        /// <summary>Row bands held before the lots (released before the rows are placed); a gate may cut a gap through one.</summary>
        public readonly List<float2[]> RowHolds = new List<float2[]>();

        /// <summary>Drives made from car gates (kerb → lane), for the flowerbeds beside them.</summary>
        public readonly List<(float2 outer, float2 inner)> Drives = new List<(float2, float2)>();

        /// <summary>
        /// (rf-shops3) The invisible drives from the kerb (Drive): the kerb point, the lane node, the street and its curve parameter,
        /// the lane. MallGrading ramps the ground along each from the street's height to the lane's (a drive left a street on a
        /// fill or in a cut at the raw ground: a step at the kerb).
        /// </summary>
        public readonly List<(float2 kerb, float2 at, long street, float t, Lane lane)> KerbDrives = new List<(float2, float2, long, float, Lane)>();

        /// <summary>Spine front ends (where a walk from the street should arrive): point on the lane and the lane.</summary>
        public readonly List<(float2 at, Lane lane)> SpineFronts = new List<(float2, Lane)>();

        public const string NoLaneKey = "Placemaker.Commercial.Issue.EntranceNoLane";
        public const string NoStreetKey = "Placemaker.Commercial.Issue.EntranceNoStreet";

        /// <summary>A row band held for later (LaneRows after the lots): its land is taken meanwhile.</summary>
        public float2[] HoldRow(float2[] rect)
        {
            if (rect == null || !Free(rect)) return null;
            var h = Hold(rect);
            RowHolds.Add(h);
            return h;
        }

        public void ReleaseRows()
        {
            foreach (var h in RowHolds) Release(h);
            RowHolds.Clear();
        }

        /// <summary>Routes every placed entrance (see the file header). Call after the network and holds, before the lots.</summary>
        public void RouteGates()
        {
            if (Gates == null) return;
            int lanesBefore = Lanes.Count;
            foreach (var g in Gates)
            {
                Ctx.Check();
                g.Landed = "none";
                if (g.Kind == ShopEntrances.BusStop) { BusStop(g); continue; }
                if (!g.Cars && g.Kind != ShopEntrances.Footpath) continue;
                // The street it joins: within the usual reach, else up to 30 m further out (a site drawn short of the kerb).
                var (street, hasStreet) = StreetFor(g);
                if (!hasStreet)
                {
                    Ctx.Plan.Facts["gateFail" + g.Index] = 1;
                    Ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = NoStreetKey, Args = new object[] { g.Index + 1 }, At = Ctx.P3(g.At) });
                    continue;
                }
                bool walk = g.Kind == ShopEntrances.Footpath;
                // A walk heads for the nearest spine front within reach (diagonally across the frontage: the way people go).
                Lane target = null; float2 hit = default;
                if (walk)
                {
                    float best = 40f;
                    foreach (var (at, lane) in SpineFronts)
                    {
                        float d = math.distance(at, g.At);
                        if (d < best && math.dot(at - g.At, g.In) > 2f) { best = d; target = lane; hit = at; }
                    }
                }
                // The candidates: the spine front (a walk), the first lane straight ahead, then the nearest points of every lane
                // (a way in that meets a held corner, a pad or a pond straight ahead bends to the closest lane it can reach).
                var cands = new List<(Lane lane, float2 at)>();
                if (target != null) cands.Add((target, hit));
                if (FirstLane(g.At, g.In, 260f, out var ahead, out var aheadAt)) cands.Add((ahead, aheadAt));
                var near = new List<(Lane lane, float2 at, float d)>();
                foreach (var l in Lanes)
                {
                    if (l.Role == NetRole.DriveConnector) continue;
                    var q = Project(l, g.At, out float dq);
                    if (dq < 90f && math.dot(q - g.At, g.In) > 0.3f * dq) near.Add((l, q, dq));
                }
                near.Sort((x, y) => x.d.CompareTo(y.d));
                for (int k = 0; k < near.Count && k < 4; k++) cands.Add((near[k].lane, near[k].at));
                // the bends of the lanes near it too (a ring's corner: no shops there to cut through)
                foreach (var l in Lanes)
                    for (int k = 1; k + 1 < l.Points.Count; k++)
                    {
                        float dq = math.distance(l.Points[k], g.At);
                        if (dq < 70f && math.dot(l.Points[k] - g.At, g.In) > 0.5f * dq) cands.Add((l, l.Points[k]));
                    }
                // (shops4) the free ends of open lanes near it, whichever way they lie (an entrance beside the strip mall's row of
                // shops, behind the lane's end: its drive runs past the end plot to the lane's end node)
                foreach (var l in Lanes)
                {
                    if (l.Closed || l.Role == NetRole.DriveConnector || l.Points.Count < 2) continue;
                    if (l.StartAttach == -1 && l.StartOn == null && math.distance(l.Points[0], g.At) < 70f) cands.Add((l, l.Points[0]));
                    var e = l.Points[l.Points.Count - 1];
                    if (l.EndAttach == -1 && l.EndOn == null && math.distance(e, g.At) < 70f) cands.Add((l, e));
                }
                if (cands.Count == 0)
                {
                    Ctx.Plan.Facts["gateFail" + g.Index] = 2;
                    Ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = NoLaneKey, Args = new object[] { g.Index + 1 }, At = Ctx.P3(g.At) });
                    continue;
                }
                // The gap: through held rows a paseo wide enough to keep whole zone columns either side, right up to the lane's
                // edge (a sliver of the row left along the lane blocked a driveway's junction lane: it fell back to a drive); else
                // the drive's width, a driveway's lane width (its junction lane needs its whole corridor).
                bool alley = ShopEntrances.IsAlley(g.Kind);
                float half = walk ? 2f : 4.5f;
                float gapHalf = alley ? math.max(half, ShopLane(Ctx).half + 0.5f) : half;
                float rowHalf = walk ? PaseoWidth * 0.5f : math.max(5f, gapHalf + 0.5f);
                // Of the ways that are free, the one cutting the fewest held rows of shops (each costs 30 m of way), then the shortest;
                // the first candidate (the spine front for a walk, else straight ahead) wins ties.
                float2[] corridor = null, rowCorridor = null, keptGap = null;
                float bestCost = float.MaxValue;
                for (int ci = 0; ci < cands.Count; ci++)
                {
                    var (cl, cat) = cands[ci];
                    var h0 = OnNode(cl, Project(cl, cat, out _));
                    var c0 = Strip(g.At - g.In * 1f, h0, half, cl.Half + 1.2f);
                    if (c0 == null || !Passable(c0, cl)) continue;
                    var r0 = Strip(g.At - g.In * 1f, h0, rowHalf, cl.Half - 0.5f) ?? c0;
                    int cuts = 0;
                    foreach (var rh in RowHolds) if (StreetShops.ConvexOverlap(rh, r0, 0.05f)) cuts++;
                    // (shops4) A walk never cuts through a row of shops (its paseo split a plot's zone columns): it takes a way round or none.
                    if (walk && cuts > 0 && LotFirstMode) continue;
                    float cost = math.distance(g.At, h0) + 30f * cuts + (ci == 0 ? -0.5f : 0f);
                    if (cost >= bestCost) continue;
                    bestCost = cost; target = cl; hit = h0; corridor = c0; rowCorridor = r0;
                    keptGap = alley ? Strip(g.At - g.In * 1f, h0, gapHalf, cl.Half + 1.2f) ?? c0 : c0;
                }
                if (corridor == null)
                {
                    Ctx.Plan.Facts["gateFail" + g.Index] = 3;
                    Ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = NoLaneKey, Args = new object[] { g.Index + 1 }, At = Ctx.P3(g.At) });
                    continue;
                }
                CutHolds(rowCorridor, keptGap);
                if (!FreeExcept(corridor, target))
                {
                    Ctx.Plan.Facts["gateFail" + g.Index] = 3;
                    Ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = NoLaneKey, Args = new object[] { g.Index + 1 }, At = Ctx.P3(g.At) });
                    continue;
                }
                if (walk)
                {
                    var pts = new List<float2> { street.from, hit };
                    if (PathChain(pts, -g.In, target) != null) { Taken.Add(corridor); g.Landed = "walk"; }
                    continue;
                }
                if (ShopEntrances.IsAlley(g.Kind))
                {
                    var (asset, h) = ShopLane(Ctx);
                    if (ShopEntrances.OneWay(g.Kind)) asset = new AssetRef(ParkingAssets.AlleyOneWay, asset.Id);
                    var a = End.Street(street.from, -g.In);
                    var b = End.OnLane(target, hit);
                    var lane = ShopEntrances.Outbound(g.Kind) ? Connect(b, null, a, NetRole.Alley, asset, h) : Connect(a, null, b, NetRole.Alley, asset, h);
                    if (lane != null) { g.Landed = "lane"; Drives.Add((street.from, hit)); continue; }
                    // A junction did not fit (a street end too close to another): an invisible drive instead.
                }
                Drive(g, street, target, hit, corridor);
            }
            Ctx.Plan.Facts["gates"] = Gates.Count;
            int landed = 0; foreach (var g in Gates) if (g.Landed != "none") landed++;
            Ctx.Plan.Facts["gatesLanded"] = landed;
            Ctx.Plan.Facts["gateLanes"] = Lanes.Count - lanesBefore;
        }

        /// <summary>
        /// (rf-shops3) A bus stop on the street in front of the entrance (as the parking type's: the stop stands on the site-side
        /// sidewalk, facing the way the kerb lane runs; the game picks the vanilla stop).
        /// </summary>
        private void BusStop(ShopEntrances.Gate g)
        {
            var (street, has) = StreetFor(g);
            if (!has) { Ctx.Plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = NoStreetKey, Args = new object[] { g.Index + 1 }, At = Ctx.P3(g.At) }); return; }
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            float2 dir = new float2(1f, 0f); float hw = 6f; float best = float.MaxValue;
            foreach (var rd in roads)
            {
                float dd = SegmentIndex.DistSq(street.kerb, rd.A, rd.B);
                if (dd < best) { best = dd; dir = math.normalizesafe(rd.B - rd.A); hw = rd.HalfWidth; }
            }
            var toSite = math.normalizesafe(g.At - street.kerb);
            var centre = street.kerb - toSite * hw;
            var pos = centre + toSite * math.max(0f, hw - 1.5f);
            bool siteLeft = Poly.Cross(dir, toSite) > 0f;
            if (siteLeft != (Ctx.Site?.LeftHandTraffic ?? false)) dir = -dir;
            var o = Ctx.Object(pos, math.atan2(dir.x, dir.y), ObjectRole.Marker, new AssetRef(ParkingAssets.BusStop), (uint)g.Index);
            g.Landed = "stop";
            Ctx.Plan.Facts["busStops"] = (Ctx.Plan.Facts.TryGetValue("busStops", out var b) ? b : 0) + 1;
        }

        /// <summary>An invisible drive from the kerb to a node on the lane (PLT's Zufahrt), asphalt under it, arrows when one-way.</summary>
        private void Drive(ShopEntrances.Gate g, (float2 from, float2 kerb, long id, float t) street, Lane target, float2 hit, float2[] corridor)
        {
            bool oneWay = ShopEntrances.OneWay(g.Kind), outbound = ShopEntrances.Outbound(g.Kind);
            var asset = oneWay ? ParkingAssets.OneWay(7) : ParkingAssets.Drive(7);
            // The drive runs from the kerb (a dead end there: the game connects it to the street's lanes) to the lane's node.
            var kerb = street.kerb;
            if (outbound) Ctx.StraightNet(hit, kerb, NetRole.DriveConnector, asset);
            else Ctx.StraightNet(kerb, hit, NetRole.DriveConnector, asset);
            target.Joins.Add(hit);
            Taken.Add(corridor);
            // The asphalt it drives on (from the outline to the lane's edge).
            var len = math.distance(g.At, hit);
            if (len > target.Half + 1f)
            {
                var d = (hit - g.At) / len; var n = new float2(-d.y, d.x);
                float e = len - target.Half + 0.3f;
                Ctx.Surface(Poly.EnsureCcw(new[] { g.At - n * 3.6f, g.At + d * e - n * 3.6f, g.At + d * e + n * 3.6f, g.At + n * 3.6f }), SurfaceRole.Asphalt, new AssetRef(""), AssetCategories.SurfaceAsphalt);
            }
            if (oneWay)
            {
                var dir = math.normalizesafe(outbound ? kerb - hit : hit - kerb);
                var mid = math.lerp(g.At, hit, 0.4f);
                Ctx.Object(mid, math.atan2(dir.x, dir.y), ObjectRole.Arrow, new AssetRef(ParkingAssets.Arrows[0], ParkingAssets.Arrows[1], ParkingAssets.Arrows[2]), (uint)g.Index);
            }
            Drives.Add((g.At, hit));
            KerbDrives.Add((kerb, hit, street.id, street.t, target));
            g.Landed = "drive";
        }

        /// <summary>
        /// The street a gate joins: (the outline point the lane or walk starts from, the kerb point, street id, t). Within the
        /// usual reach of the outline; else the point is moved up to 30 m out along the gate's outward normal until a street is in
        /// reach (a site drawn short of the kerb: the drive is built out to it).
        /// </summary>
        private ((float2 from, float2 kerb, long id, float t), bool) StreetFor(ShopEntrances.Gate g)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            var along = new float2(g.In.y, -g.In.x);
            foreach (float side in new[] { 0f, 3f, -3f, 6f, -6f, 9f, -9f })
            for (float out_ = 0f; out_ <= 30f; out_ += 3f)
            {
                var p = g.At - g.In * out_ + along * side;
                if (side != 0f && !Region.Contains(g.At + along * side + g.In * 0.5f)) continue;
                if (!StreetShops.StreetEnd(Ctx.Input, p, -g.In, out var hit, out long id, out float t)) continue;
                float hw = 6f;
                foreach (var r in roads) if (math.sqrt(SegmentIndex.DistSq(hit, r.A, r.B)) < 0.6f) { hw = r.HalfWidth; break; }
                if (side != 0f) g.At += along * side;   // a road node right at the gate: the drive moves beside it
                var kerb = hit + math.normalizesafe(g.At - hit) * hw;
                return ((p, kerb, id, t), true);
            }
            return ((g.At, g.At, -1, 0f), false);
        }

        /// <summary>
        /// Could a gate's way run here once the held rows and the planted frontage give way to it (<see cref="CutHolds"/>)? Free of
        /// everything else taken but the lane's own corridors.
        /// </summary>
        private bool Passable(float2[] poly, Lane lane)
        {
            var soft = new HashSet<float2[]>(RowHolds);
            foreach (var k in KeptLand) if (k.Kind == KeepKind.Frontage && math.lengthsq(k.Frame.Dir) > 0.5f) soft.Add(k.Ring);
            foreach (var t in Taken)
            {
                if (soft.Contains(t)) continue;
                if (lane?.Land != null && lane.Land.Contains(t)) continue;
                bool laneLand = false;
                foreach (var l in Lanes) if (l.Land != null && l.Land.Contains(t)) { laneLand = true; break; }
                if (laneLand && StreetShops.ConvexOverlap(t, Shrink(poly, 2.5f), 0.05f) == false) continue;
                if (StreetShops.ConvexOverlap(t, poly, 0.05f)) return false;
            }
            return true;
        }

        /// <summary>Free of everything taken but the lanes' own corridors at a junction end (a drive meets its lane at an angle).</summary>
        private bool FreeExcept(float2[] poly, Lane lane)
        {
            foreach (var t in Taken)
            {
                if (lane?.Land != null && lane.Land.Contains(t)) continue;
                bool laneLand = false;
                foreach (var l in Lanes) if (l.Land != null && l.Land.Contains(t)) { laneLand = true; break; }
                if (laneLand && StreetShops.ConvexOverlap(t, Shrink(poly, 2.5f), 0.05f) == false) continue;
                if (StreetShops.ConvexOverlap(t, poly, 0.05f)) return false;
            }
            return true;
        }

        /// <summary>The first lane of this mall the ray from <paramref name="a"/> along <paramref name="dir"/> meets (its centre line point).</summary>
        public bool FirstLane(float2 a, float2 dir, float max, out Lane lane, out float2 hit)
        {
            lane = null; hit = a;
            float best = float.MaxValue;
            var b = a + dir * max;
            foreach (var l in Lanes)
                for (int i = 0; i + 1 < l.Points.Count; i++)
                {
                    if (!Poly.SegmentIntersection(a, b, l.Points[i], l.Points[i + 1], out var p, out float t, out _)) continue;
                    float d = t * max;
                    if (d > 0.5f && d < best) { best = d; lane = l; hit = p; }
                }
            return lane != null;
        }

        /// <summary>A strip from <paramref name="a"/> to the edge of a lane at <paramref name="b"/> (its centre point), half width <paramref name="half"/>.</summary>
        private static float2[] Strip(float2 a, float2 b, float half, float laneHalf)
        {
            float len = math.distance(a, b);
            if (len < laneHalf + 1f) return null;
            var d = (b - a) / len; var n = new float2(-d.y, d.x);
            float e = len - laneHalf - 0.1f;
            return Poly.EnsureCcw(new[] { a - n * half, a + d * e - n * half, a + d * e + n * half, a + n * half });
        }

        /// <summary>
        /// Cuts a gap through the held rows (<paramref name="rowGap"/>) and the kept land (<paramref name="keptGap"/>) a gate's way
        /// crosses: each piece in the way is replaced by what is left of it (kept frontage bands are split along their frame).
        /// </summary>
        private void CutHolds(float2[] rowGap, float2[] keptGap)
        {
            for (int i = RowHolds.Count - 1; i >= 0; i--)
            {
                var h = RowHolds[i];
                if (!StreetShops.ConvexOverlap(h, rowGap, 0.05f)) continue;
                Release(h);
                RowHolds.RemoveAt(i);
                foreach (var part in Minus(h, rowGap))
                    if (Poly.Area(part) >= 8f * Cell) { Taken.Add(part); RowHolds.Add(part); }
            }
            for (int i = KeptLand.Count - 1; i >= 0; i--)
            {
                var k = KeptLand[i];
                if (!StreetShops.ConvexOverlap(k.Ring, keptGap, 0.05f)) continue;
                if (k.Kind != KeepKind.Frontage || math.lengthsq(k.Frame.Dir) < 0.5f)
                {
                    // A spine or plaza in the way stays (the gate stops at it): only the frontage gives way.
                    continue;
                }
                float c0 = float.MaxValue, c1 = float.MinValue;
                foreach (var v in keptGap) { float s = math.dot(v - k.Frame.A, k.Frame.Dir); c0 = math.min(c0, s); c1 = math.max(c1, s); }
                Unkeep(k);
                if (c0 - 0.5f - k.S0 >= 6f) { var a = KeepRect(k.Frame, k.S0, c0 - 0.5f, k.T0, k.T1, KeepKind.Frontage, k.Salt); if (a != null) foreach (var an in k.Anchors) if (math.dot(an - k.Frame.A, k.Frame.Dir) < c0) a.Anchors.Add(an); }
                if (k.S1 - (c1 + 0.5f) >= 6f) { var b = KeepRect(k.Frame, c1 + 0.5f, k.S1, k.T0, k.T1, KeepKind.Frontage, k.Salt + 11u); if (b != null) foreach (var an in k.Anchors) if (math.dot(an - k.Frame.A, k.Frame.Dir) > c1) b.Anchors.Add(an); }
            }
        }

        /// <summary>Convex parts of <paramref name="a"/> minus <paramref name="b"/>.</summary>
        private static List<float2[]> Minus(float2[] a, float2[] b)
        {
            var res = new List<float2[]>();
            try
            {
                foreach (var r in Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(a) }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(b) }, BoolOp.Difference))
                {
                    if (r?.Outer == null || r.Outer.Length < 3) continue;
                    var ring = Poly.EnsureCcw(Poly.Clean(r.Outer, 0.05f));
                    if (ring.Length < 3) continue;
                    if (Poly.IsConvex(ring)) res.Add(ring);
                    else { var parts = ConvexDecomposition.Decompose(ring); if (parts.Count > 0) res.AddRange(parts); else res.Add(ring); }
                }
            }
            catch (Exception) { }
            return res;
        }
    }
}

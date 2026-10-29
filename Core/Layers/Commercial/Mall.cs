// s14 Commercial types: the shared kit. A commercial site is a composition of
//   • lanes the type plans itself (vanilla "Alley" 8 m, or "Pedestrian Street" for outdoor malls), joined to
//     an EXISTING street at their ends (a street a parent planned cannot take a mid-segment junction);
//   • shop rows = zoning cutouts, whole 8 m cells deep, each touching a street or one of those lanes so CS2's
//     zone blocks (which belong to a road) reach them — zoned with the edgeZoning planner itself;
//   • parking lots planned by the Parking layer (aisles, bays, lamps, planting, chargers) on a cut of the
//     site, entered from the street (connector to the kerb) or from a lane ("alley" entrance kind: a short
//     alley stub from the lane's centre line to the lot, sharing a node with the lane — PLT's Gasse);
//   • the rest planned by the Park / Plaza / Market square planners as sub-plans on what is left.
// Everything is kept apart by construction (one "Taken" list of convex pieces), the cutouts are tagged
// "shops" + "place:shops" so siblings keep out and Cutouts.Enforce guarantees they stay empty.

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        public const float Cell = StreetFrontage.Cell;
        public const float AlleyHalf = StreetShops.AlleyHalf;
        /// <summary>Lane junctions keep this far from a street corner (m).</summary>
        public const float CornerClear = 12f;
        /// <summary>Synthetic NearbyNet ids for planned lanes (only inside parking sub-plans; never built).</summary>
        private const long SyntheticBase = -700000;

        public static AssetRef AlleyAsset => new AssetRef(StreetShops.AlleyAsset, StreetShops.AlleyFallback);

        /// <summary>
        /// (s26) The lane shops FRONT: zoned plots need the lane's zone blocks. The vanilla Alley is used when it carries
        /// zone blocks (or nobody knows: tests, older catalogs); when the game's catalog says it has none
        /// (AssetInfo.ZoneBlocks = 2), the Two-Lane Road (16 m) — playtest s26: every shop grew off the street behind
        /// the plaza, none off its alley ring. Service lanes behind shops keep <see cref="AlleyAsset"/>.
        /// </summary>
        public static (AssetRef asset, float half) ShopLane(LayerContext ctx)
        {
            var assets = ctx?.Site?.Assets;
            if (assets != null && assets.TryGetValue(StreetShops.AlleyAsset, out var alley) && alley.ZoneBlocks == 2)
            {
                float w = assets.TryGetValue(StreetShops.AlleyFallback, out var road) && road.NetWidth > 0.5f ? road.NetWidth : 16f;
                return (new AssetRef(StreetShops.AlleyFallback), w * 0.5f);
            }
            return (AlleyAsset, AlleyHalf);
        }
        public static AssetRef PedestrianStreet => new AssetRef("Pedestrian Street", "Pedestrian Street Small", StreetShops.AlleyAsset);
        public static AssetRef PedestrianStreetSmall => new AssetRef("Pedestrian Street Small", "Pedestrian Street", StreetShops.AlleyAsset);

        public sealed class Lane
        {
            public readonly List<float2> Points = new List<float2>();
            public NetRole Role = NetRole.Alley;
            public AssetRef Asset;
            public float Half = AlleyHalf;
            public long StartAttach = -1, EndAttach = -1;
            public float StartT, EndT;
            /// <summary>Points on the lane where something joins it (lot alley stubs, paths): the lane is split there.</summary>
            public readonly List<float2> Joins = new List<float2>();
            /// <summary>(s32) The lane this lane's start / end node sits on (a T or X junction), null at a street or a free end.</summary>
            public Lane StartOn, EndOn;
            /// <summary>(s32) A closed loop: the last point is the first (one node).</summary>
            public bool Closed;
            /// <summary>(s32) Level shoulder beside the lane for the grading (m); negative = MallGrading.Shoulder.</summary>
            public float Shoulder = -1f;
            /// <summary>(s32) Steepest longitudinal grade the grading gives the lane; 0 = MallGrading.LaneMaxGrade (8 %).</summary>
            public float MaxGrade;
            /// <summary>(s32) The corridor pieces Connect took for it (so a lane can be taken back).</summary>
            internal List<float2[]> Land;
            internal int Index;
        }

        public readonly LayerContext Ctx;
        public readonly LayerRegion Region;
        /// <summary>Everything planned so far (convex pieces): shops, lots, lane corridors, places.</summary>
        public readonly List<float2[]> Taken = new List<float2[]>();
        public readonly List<float2[]> Shops = new List<float2[]>();
        public readonly List<Lane> Lanes = new List<Lane>();
        public readonly List<float2[]> Lots = new List<float2[]>();

        /// <summary>(shops2) The lengths of the mall's lists (a composition that gives up late is taken back with <see cref="Rewind"/>,
        /// together with CommercialLayer.PlanMark for the plan). Only for lists appended to since the mark.</summary>
        public int[] Mark() => new[] { Taken.Count, Shops.Count, Lanes.Count, Lots.Count, Doors.Count, _lotGround.Count, Walks.Count, Stalls, EvStalls, KeptLand.Count, RowHolds.Count, SpineFronts.Count, Drives.Count, KerbDrives.Count };

        public void Rewind(int[] m)
        {
            void Cut<T>(List<T> l, int n) { if (l.Count > n) l.RemoveRange(n, l.Count - n); }
            for (int i = m[2]; i < Lanes.Count; i++)
            {
                var lane = Lanes[i];
                if (lane.StartOn != null) lane.StartOn.Joins.Remove(lane.Points[0]);
                if (lane.EndOn != null) lane.EndOn.Joins.Remove(lane.Points[lane.Points.Count - 1]);
            }
            for (int i = m[1]; i < Shops.Count; i++) { ShopZone.Remove(Shops[i]); CellPlots.Remove(Shops[i]); }
            Cut(Taken, m[0]); Cut(Shops, m[1]); Cut(Lanes, m[2]); Cut(Lots, m[3]); Cut(Doors, m[4]); Cut(_lotGround, m[5]); Cut(Walks, m[6]);
            Stalls = m[7]; EvStalls = m[8];
            if (m.Length > 12) { Cut(KeptLand, m[9]); Cut(RowHolds, m[10]); Cut(SpineFronts, m[11]); Cut(Drives, m[12]); }
            if (m.Length > 13) Cut(KerbDrives, m[13]);
        }
        /// <summary>(shops) True while another frontage is composed (CommercialLayer.ComposeMore): a composition that does not fit there leaves nothing behind (no fallback street row).</summary>
        public bool Extra;
        /// <summary>(shops) Most land (m²) all the lots together may take: leftover lots (CommercialLayer.RestLots) stop here and the rest is planted.</summary>
        public float LotBudget = float.MaxValue;
        /// <summary>(shops) Land the lots take so far (m²).</summary>
        public float LotArea() { float a = 0f; foreach (var l in Lots) a += Poly.Area(l); return a; }
        public int Stalls, EvStalls;
        /// <summary>Extra outline points green pieces should open towards (lane dead ends of a shop layout).</summary>
        public readonly List<float2> Doors = new List<float2>();
        private readonly List<(float2[] ring, Func<float2, float> height)> _lotGround = new List<(float2[], Func<float2, float>)>();
        /// <summary>(s25) Off: the mall keeps the ground as it is (tests compare against the ungraded plan).</summary>
        public bool Grading = true;
        /// <summary>(s32) Smallest height change the grading emits (m). The s32 types emit finer (lanes running up a slope would
        /// otherwise keep 0.1 m lumps of the original ground in every cell the profile happens to come close to).</summary>
        public float GradeTolerance = Sculpt.MinChange;
        /// <summary>(s25) Planned ground height of every lane at its centre line (after grading); empty before Finish.</summary>
        public readonly Dictionary<Lane, float[]> LaneProfiles = new Dictionary<Lane, float[]>();

        public Mall(LayerContext ctx)
        {
            Ctx = ctx;
            Region = ctx.LayerRegion;
        }

        // ───────────── frontage ─────────────

        /// <summary>Street-facing outline runs (existing streets only can take lane junctions), longest first.</summary>
        public List<StreetShops.Run> Runs()
        {
            var roads = StreetFrontage.Roads(Ctx.Input);
            var runs = roads.Count == 0 ? new List<StreetShops.Run>() : StreetShops.Runs(Region, roads, null, Ctx.Site?.WaterDepthAt, true);
            runs.Sort((a, b) => b.Length != a.Length ? b.Length.CompareTo(a.Length) : a.A.x != b.A.x ? a.A.x.CompareTo(b.A.x) : a.A.y.CompareTo(b.A.y));
            return runs;
        }

        /// <summary>How far the area reaches inward from a run point (ray march, m).</summary>
        public float ReachAt(StreetShops.Run r, float s, float max = 600f)
        {
            float extent = 0f;
            for (float t = 2f; t < max; t += 2f)
            {
                var p = r.At(s, t);
                if (!Region.Contains(p) || (Ctx.Site?.WaterDepthAt != null && Ctx.Site.WaterDepthAt(p) > 0.05f)) break;
                extent = t;
            }
            return extent;
        }

        /// <summary>(shops) How far the free land (inside the area, not taken) reaches inward from a run point (m).</summary>
        public float FreeReachAt(StreetShops.Run r, float s, float max = 600f)
        {
            float extent = 0f;
            for (float t = 2f; t < max; t += 2f)
            {
                var p = r.At(s, t);
                if (!Region.Contains(p) || (Ctx.Site?.WaterDepthAt != null && Ctx.Site.WaterDepthAt(p) > 0.05f)) break;
                bool taken = false;
                foreach (var q in Taken) if (Poly.Contains(q, p)) { taken = true; break; }
                if (taken) break;
                extent = t;
            }
            return extent;
        }

        /// <summary>(shops) Smallest free reach across [s0, s1] (9 samples).</summary>
        public float FreeReach(StreetShops.Run r, float s0, float s1)
        {
            float m = float.MaxValue;
            for (int k = 0; k <= 8; k++) m = math.min(m, FreeReachAt(r, math.lerp(s0, s1, k / 8f)));
            return m;
        }

        /// <summary>Smallest reach across [s0, s1] (5 samples).</summary>
        public float Reach(StreetShops.Run r, float s0, float s1)
        {
            float m = float.MaxValue;
            for (int k = 0; k <= 4; k++) m = math.min(m, ReachAt(r, math.lerp(s0, s1, k / 4f)));
            return m;
        }

        /// <summary>
        /// (shops) The stretch of a run that holds the largest rectangle reaching straight in (span × smallest reach, reach
        /// sampled every 4 m), at least <paramref name="minLen"/> long and <paramref name="minReach"/> deep. On a block with an
        /// angled or curved side the whole run's smallest reach can be a fraction of the land behind most of it (the mall
        /// district on a 3 ha half-curved block saw 52 m of 150 and fell back to a street row). The whole run is kept when it
        /// holds at least 85 % of the best. Null when no stretch qualifies.
        /// </summary>
        public (float s0, float s1, float reach)? BestSpan(StreetShops.Run r, float minLen, float minReach, float from, float to, bool free = false)
        {
            float len = to - from;
            if (len < minLen) return null;
            int n = math.max(2, (int)math.ceil(len / 4f));
            var reach = new float[n + 1];
            for (int i = 0; i <= n; i++) reach[i] = free ? FreeReachAt(r, math.lerp(from, to, (float)i / n)) : ReachAt(r, math.lerp(from, to, (float)i / n));
            float whole = float.MaxValue; foreach (var x in reach) whole = math.min(whole, x);
            float bestArea = -1f; int bi = 0, bj = n; float bReach = whole;
            for (int i = 0; i < n; i++)
            {
                float m = reach[i];
                for (int j = i + 1; j <= n; j++)
                {
                    m = math.min(m, reach[j]);
                    float span = len * (j - i) / n;
                    if (span < minLen || m < minReach) continue;
                    float a = span * m;
                    if (a > bestArea) { bestArea = a; bi = i; bj = j; bReach = m; }
                }
            }
            if (bestArea < 0f) return null;
            if (whole >= minReach && len * whole >= 0.85f * bestArea) return (from, to, whole);
            return (math.lerp(from, to, (float)bi / n), math.lerp(from, to, (float)bj / n), bReach);
        }

        public static float2[] Rect(StreetShops.Run r, float s0, float s1, float t0, float t1) =>
            Poly.EnsureCcw(new[] { r.At(s0, t0), r.At(s1, t0), r.At(s1, t1), r.At(s0, t1) });

        // ───────────── taking land ─────────────

        public bool Free(float2[] poly)
        {
            foreach (var t in Taken) if (StreetShops.ConvexOverlap(t, poly, 0.05f)) return false;
            return true;
        }

        public bool Fits(float2[] poly) => StreetShops.Fits(Region, poly) && Free(poly);

        /// <summary>
        /// (rf-shops3) Takes a polygon of any shape: Taken is tested piece by piece as convex (separating axes), so a concave piece
        /// (an L-shaped park, a planted leftover) goes in as its convex parts — whole, its hull took the land in its inner corner.
        /// </summary>
        public void Take(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return;
            var r = Poly.EnsureCcw(ring);
            if (Poly.IsConvex(r)) { Taken.Add(r); return; }
            List<float2[]> parts = null;
            try { parts = ConvexDecomposition.Decompose(r); } catch (Exception) { parts = null; }
            if (parts == null || parts.Count == 0) Taken.Add(r); else Taken.AddRange(parts);
        }

        /// <summary>(s46) Area of everything planned so far (the pieces of Taken are disjoint, up to the 5 cm touching tolerance).</summary>
        public float TakenArea() { float a = 0f; foreach (var t in Taken) a += Poly.Area(t); return a; }

        /// <summary>One shop plot (whole cells; must touch a street or a lane — the caller's geometry guarantees it).</summary>
        public bool Shop(float2[] rect)
        {
            if (!Fits(rect) || !Faces(rect)) return false;
            Shops.Add(rect);
            Taken.Add(rect);
            return true;
        }

        /// <summary>
        /// True when one side of the plot runs along a street or a lane of this mall (as the edgeZoning planner tests it):
        /// zone blocks belong to a road, so a plot facing none would stay an empty cutout.
        /// </summary>
        public bool Faces(float2[] rect)
        {
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            bool Near(float2 p)
            {
                foreach (var r in roads)
                    if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) <= r.HalfWidth + StreetFrontage.Tolerance) return true;
                foreach (var l in Lanes)
                    for (int i = 0; i + 1 < l.Points.Count; i++)
                        if (math.sqrt(SegmentIndex.DistSq(p, l.Points[i], l.Points[i + 1])) <= l.Half + StreetFrontage.Tolerance) return true;
                return false;
            }
            for (int i = 0; i < rect.Length; i++)
            {
                float2 a = rect[i], b = rect[(i + 1) % rect.Length];
                if (Near(math.lerp(a, b, 0.5f)) && Near(math.lerp(a, b, 0.2f)) && Near(math.lerp(a, b, 0.8f))) return true;
            }
            return false;
        }

        private List<StreetFrontage.RoadLine> _roads;

        /// <summary>
        /// A row of shop plots along a run between s0 and s1, facing the line t = t0 (a street or lane edge) and reaching
        /// <paramref name="cells"/> cells further (sign = direction). Pieces of ≤ 3 cells; each as deep as fits.
        /// </summary>
        public int ShopRow(StreetShops.Run r, float s0, float s1, float t0, int cells, float dir = 1f)
        {
            int made = 0;
            float len = s1 - s0;
            if (len < Cell - 0.01f) return 0;
            // (shops) A join on the lane the row faces (a lot's alley stub, a footpath) splits that lane into edges, each with its
            // own cell grid: a plot straddling one keeps a lone column of each, which the game blocks (the strip mall's middle
            // plots were never zoned: "CutoutNotZonable"). The row is cut at every join along its front, as LaneRows does.
            var cuts = new List<float>();
            foreach (var l in Lanes)
                foreach (var j in l.Joins)
                {
                    float u = math.dot(j - r.A, r.Dir), t = math.dot(j - r.A, r.In);
                    if (u <= s0 + 0.5f || u >= s1 - 0.5f) continue;
                    // the join lies on the lane whose edge is the row's front (the lane centre is half a lane off t0, on the far side)
                    if (math.abs(t - (t0 - dir * l.Half)) <= 1.0f) cuts.Add(u);
                }
            if (cuts.Count == 0)
            {
                int n = math.max(1, (int)math.ceil(len / (3 * Cell)));
                for (int k = 0; k < n; k++)
                {
                    float a = math.lerp(s0, s1, (float)k / n), b = math.lerp(s0, s1, (float)(k + 1) / n);
                    for (int d = cells; d >= 1; d--)
                        if (Shop(Rect(r, a, b, t0, t0 + dir * d * Cell))) { made++; break; }
                }
                return made;
            }
            cuts.Sort();
            float from = s0;
            foreach (var u in cuts)
            {
                if (u - 0.25f - from >= MinStretch) made += Row(r, from, u - 0.25f, t0, cells, dir, null, 3, math.min(2, cells));
                from = math.max(from, u + 0.25f);
            }
            if (s1 - from >= MinStretch) made += Row(r, from, s1, t0, cells, dir, null, 3, math.min(2, cells));
            return made;
        }

        /// <summary>Plot depth across (perpendicular) rows: whole cells, clamped 1–6.</summary>
        public static int Cells(float metres) => math.clamp((int)math.floor(metres / Cell + 0.01f), 1, 6);

        // ───────────── lanes ─────────────

        /// <summary>
        /// A lane from the street at outline point <paramref name="start"/> through <paramref name="inner"/> points,
        /// optionally back to the street at <paramref name="end"/>. Null when a street end finds no existing street.
        /// </summary>
        public Lane AddLane(float2 start, float2 startOutward, IList<float2> inner, float2? end, float2 endOutward, NetRole role, AssetRef asset, float half)
        {
            if (!StreetShops.StreetEnd(Ctx.Input, start, startOutward, out var hitA, out long idA, out float tA)) return null;
            var lane = new Lane { Role = role, Asset = asset, Half = half, StartAttach = idA, StartT = tA };
            lane.Points.Add(hitA);
            foreach (var p in inner) lane.Points.Add(p);
            if (end.HasValue)
            {
                if (!StreetShops.StreetEnd(Ctx.Input, end.Value, endOutward, out var hitB, out long idB, out float tB)) return null;
                lane.Points.Add(hitB);
                lane.EndAttach = idB; lane.EndT = tB;
            }
            var corridors = Corridors(lane);
            foreach (var c in corridors) if (!Free(c)) return null;
            lane.Index = Lanes.Count;
            Lanes.Add(lane);
            lane.Land = corridors;
            Taken.AddRange(corridors);
            return lane;
        }

        /// <summary>A lane planned elsewhere (StreetShops layouts); its land is already in Taken.</summary>
        public void AdoptLane(Lane lane)
        {
            lane.Index = Lanes.Count;
            Lanes.Add(lane);
        }

        /// <summary>How far from its end on <paramref name="host"/> a lane leaving it along <paramref name="dir"/> keeps its corridor (m).</summary>
        private static float Clearance(Lane host, float2 at, float2 dir, float half)
        {
            float best = float.MaxValue; float2 e = dir;
            for (int k = 0; k + 1 < host.Points.Count; k++)
            {
                float dd = SegmentIndex.DistSq(at, host.Points[k], host.Points[k + 1]);
                if (dd < best) { best = dd; e = math.normalizesafe(host.Points[k + 1] - host.Points[k]); }
            }
            float sin = math.max(0.25f, math.abs(dir.x * e.y - dir.y * e.x)), cos = math.abs(math.dot(dir, e));
            return host.Half / sin + half * cos / sin + 0.1f;
        }

        /// <summary>Lane footprint pieces (the road width; plots may start right at its edge, where its zone blocks begin).</summary>
        private List<float2[]> Corridors(Lane lane)
        {
            var list = new List<float2[]>();
            float h = lane.Half;
            for (int i = 0; i + 1 < lane.Points.Count; i++)
            {
                var a = lane.Points[i]; var b = lane.Points[i + 1];
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                // Square ends overlap at bends so the union is closed; at an attached end only the part inside the area matters.
                float ea = i == 0 ? 0f : h, eb = i + 2 == lane.Points.Count ? (lane.EndAttach >= 0 ? 0f : 0.5f) : h;
                // (s32) An end on another lane stops at that lane's edge (its corridor is already taken).
                // (rf-shops3) Met at an angle (a chord of a round ring) the end stops further back: the host's band is wider along the lane, and the lane's own corners reach out sideways.
                if (i == 0 && lane.StartOn != null) ea = -Clearance(lane.StartOn, a, d, h);
                if (i + 2 == lane.Points.Count && lane.EndOn != null) eb = -Clearance(lane.EndOn, b, -d, h);
                list.Add(Poly.EnsureCcw(new[] { a - d * ea - n * h, b + d * eb - n * h, b + d * eb + n * h, a - d * ea + n * h }));
            }
            return list;
        }

        // ───────────── parking ─────────────

        /// <summary>
        /// A parking lot on <paramref name="ring"/> (∩ the site), with entrances at the given outline points: "entrance"
        /// for one facing an existing street, "alley" for one facing a lane of this mall. Merged into the plan.
        /// </summary>
        public bool Lot(float2[] ring, List<(float2 at, string kind)> entrances, SettingsBag parking, uint salt, bool leftover = false)
        {
            var t = PlanLot(ring, entrances, parking, salt, leftover);
            return t != null && CommitLot(t, leftover);
        }

        /// <summary>(s50) A planned but not yet merged lot (so several variants can be tried and the best kept).</summary>
        public sealed class LotTry
        {
            public AreaPlan Sub;
            public float2[] Ring;
            public float Capacity, Ev;
            public Dictionary<long, (Lane lane, int seg)> Synthetic;
            public SiteContext Site;
        }

        /// <summary>Plans a lot on <paramref name="ring"/> (see <see cref="Lot"/>) without merging it; null when it cannot be planned or holds no stall.</summary>
        public LotTry PlanLot(float2[] ring, List<(float2 at, string kind)> entrances, SettingsBag parking, uint salt, bool leftover = false)
        {
            var gen = GeneratorRegistry.Get("parking");
            if (gen == null || ring == null) return null;
            float2[] lot;
            try { lot = Largest(Poly.Intersection(Region.Ring, Poly.EnsureCcw(ring)), 300f); }
            catch (Exception) { lot = null; }
            if (lot == null || (!leftover && !Free(Shrink(lot, 0.3f)))) return null;
            var bag = gen.Defaults();
            foreach (var kv in parking.Values) bag.Values[kv.Key] = kv.Value;
            // The lot's own frontage zoning stays off: the mall zones its shop rows itself.
            bag.Values["edgeZoning"] = "none";
            var markers = new List<EdgeMarker>();
            foreach (var (at, kind) in entrances) markers.Add(new EdgeMarker { Kind = kind, Position = Ctx.P3(at) });
            var site = Ctx.Site.WithHeight(Ctx.Site.HeightAt);
            site.NearbyNets = new List<NearbyNet>(Ctx.Site.NearbyNets);
            var synthetic = new Dictionary<long, (Lane, int)>();
            foreach (var lane in Lanes)
            {
                // (s41) The lane at the height it will be graded to (street-pinned ends, ≤ 8 % ramps), not the raw terrain: the
                // lot ties its edges to these heights.
                MallGrading.Profile pre = null;
                if (lane.Points.Count >= 2)
                    try { pre = MallGrading.Profile.Of(lane, Ctx, Ctx.H, new List<(float2[], Func<float2, float>)>()); }
                    catch (Exception) { pre = null; }
                float3 At(float2 p)
                {
                    if (pre == null) return Ctx.P3(p);
                    pre.Distance(p, out float s);
                    return new float3(p.x, pre.At(s), p.y);
                }
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                {
                    long id = SyntheticBase - lane.Index * 100 - i;
                    synthetic[id] = (lane, i);
                    var pa = lane.Points[i]; var pb = lane.Points[i + 1];
                    float3 a = At(pa), b = At(pb);
                    site.NearbyNets.Add(new NearbyNet { ExternalId = id, A = a, B = At(math.lerp(pa, pb, 1f / 3f)), C = At(math.lerp(pa, pb, 2f / 3f)), D = b, Width = lane.Half * 2f, Category = NetCategory.Road, HasSidewalk = true });
                }
            }
            var input = new AreaInput
            {
                Shape = new AreaShape { Ring = Poly.To3D(lot, Ctx.G), Source = AreaSource.Drawn },
                Site = site, Markers = markers, Settings = bag, Seed = Hashing.Mix(Ctx.Seed, salt),
                RegionTags = new[] { RegionTags.Lot }, Reserved = Ctx.Input.Reserved,
            };
            AreaPlan sub;
            try { sub = gen.Plan(input, Ctx.Cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { sub = null; }
            if (sub == null || sub.HasErrors) return null;
            sub.Facts.TryGetValue("capacity", out var cap);
            sub.Facts.TryGetValue("evCapacity", out var ev);
            if (cap <= 0) return null;
            return new LotTry { Sub = sub, Ring = lot, Capacity = (float)cap, Ev = (float)ev, Synthetic = synthetic, Site = site };
        }

        /// <summary>
        /// (shops4) An asphalt ring as pieces the game draws whole: cut into 30 m tiles square to the main street, each tile's part made
        /// convex; parts the game would refuse are left out when tiny or a sliver (else the part stays as it is). Null when the ring
        /// is small and convex already.
        /// </summary>
        private List<float2[]> SafeAsphalt(float2[] ring)
        {
            if (Poly.IsConvex(ring) && Poly.Area(ring) <= 900f) return null;
            bool Good(float2[] p) => SurfaceAcceptance.IsAccepted(p) && Build.AreaRings.IsValid(p, Build.AreaRings.SurfaceMinNodeDistance + Build.AreaRings.Margin);
            var d = MainDir ?? new float2(1f, 0f); var n = new float2(-d.y, d.x);
            float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
            foreach (var p in ring) { float a = math.dot(p, d), b = math.dot(p, n); a0 = math.min(a0, a); a1 = math.max(a1, a); b0 = math.min(b0, b); b1 = math.max(b1, b); }
            const float T = 30f;
            var res = new List<float2[]>();
            try
            {
                for (float a = a0; a < a1; a += T)
                    for (float b = b0; b < b1; b += T)
                    {
                        var tile = Poly.EnsureCcw(new[] { d * a + n * b, d * (a + T) + n * b, d * (a + T) + n * (b + T), d * a + n * (b + T) });
                        foreach (var reg in Poly.Intersection(ring, tile))
                        {
                            if (reg?.Outer == null || reg.Outer.Length < 3) continue;
                            var piece = Poly.EnsureCcw(Poly.Clean(reg.Outer, 0.05f));
                            if (piece.Length < 3 || Poly.Area(piece) < 0.5f) continue;
                            var parts = Poly.IsConvex(piece) ? new List<float2[]> { piece } : ConvexDecomposition.Decompose(piece, true);
                            if (parts == null || parts.Count == 0) parts = new List<float2[]> { piece };
                            foreach (var p in parts)
                            {
                                if (Good(p)) { res.Add(p); continue; }
                                // A tile's corner cut a few centimetres from a vertex (an edge the game refuses): that vertex merged away.
                                var fixedPart = Poly.Clean(p, 2f * SurfaceAcceptance.MinEdge);
                                if (fixedPart.Length >= 3 && Good(fixedPart)) { res.Add(fixedPart); continue; }
                                // a sliver under a metre wide (a seam along a lane's edge, which the lane covers) is left out, and so is a
                                // piece under 40 m² the game would refuse anyway (the build skips it: nothing is lost by leaving it out)
                                float per = 0f; for (int k = 0; k < p.Length; k++) per += math.distance(p[k], p[(k + 1) % p.Length]);
                                if (Poly.Area(p) >= 40f && 2f * Poly.Area(p) / math.max(per, 0.01f) >= 0.5f) res.Add(p);
                            }
                        }
                    }
            }
            catch (Exception) { return null; }
            return res.Count > 0 ? res : null;
        }

        /// <summary>Merges a planned lot into the plan and takes its land.</summary>
        public bool CommitLot(LotTry t, bool leftover = false)
        {
            var sub = t.Sub; var lot = t.Ring;
            // Alley stubs into a lane: CS2 joins nets only at identical nodes — drop the synthetic attachment and
            // split the lane at the stub's end instead.
            foreach (var n in sub.Nets)
            {
                if (t.Synthetic.TryGetValue(n.StartAttach, out var s0)) { SnapOnto(s0.lane, s0.seg, ref n.A, ref n.B); OntoNode(s0.lane, ref n.A, ref n.B); AddJoin(s0.lane, n.A.xz); n.StartAttach = -1; }
                if (t.Synthetic.TryGetValue(n.EndAttach, out var s1)) { SnapOnto(s1.lane, s1.seg, ref n.D, ref n.C); OntoNode(s1.lane, ref n.D, ref n.C); AddJoin(s1.lane, n.D.xz); n.EndAttach = -1; }
            }
            sub.Regions.Clear();
            // (shops4) The lot's planted islands and edge strips as painted ground, not grass surfaces (the user: "paint all the grass
            // and then texture it in different areas"): mulch and dirt with green tufts round the trees. The asphalt is laid in spans
            // that leave the islands out, so the bare ground shows there.
            if (LotFirstMode)
            {
                // The asphalt as small convex pieces: the game split a big many-sided lot ring into triangles with a long sliver
                // missing, grass showing under a row of stalls (game runner, 300 x 280 m site). 30 m tiles, each made convex.
                for (int i = sub.Surfaces.Count - 1; i >= 0; i--)
                {
                    var sf = sub.Surfaces[i];
                    if (sf.Role != SurfaceRole.Asphalt || sf.Ring.Length <= 4) continue;
                    var parts = SafeAsphalt(Poly.To2D(sf.Ring));
                    if (parts == null) continue;
                    sub.Surfaces.RemoveAt(i);
                    foreach (var p in parts)
                        sub.Surfaces.Insert(i, new PlannedSurface { Ring = Poly.To3D(p, q => sub.HeightAfter != null ? sub.HeightAfter(q) : Ctx.H(q)), Role = sf.Role, Asset = sf.Asset, Control = sf.Control, Category = sf.Category });
                }
                var kit = PaintKit();
                for (int i = sub.Surfaces.Count - 1; i >= 0; i--)
                {
                    var sf = sub.Surfaces[i];
                    if (sf.Role != SurfaceRole.Grass) continue;
                    var r2 = Poly.To2D(sf.Ring);
                    if (r2.Length >= 3 && Poly.Area(r2) >= 2f)
                    {
                        kit.Fill(r2, TerrainMaterials.Dirt, 1f, 0.6f, 0.6f, 4f);
                        kit.Speckle(r2, TerrainMaterials.Lush, 0.85f, 3f, 0.35f, 0.3f, 0.6f);
                        kit.Speckle(r2, TerrainMaterials.Dry, 0.7f, 5f, 0.2f, 0.3f, 0.6f);
                    }
                    sub.Surfaces.RemoveAt(i);
                }
            }
            sub.Issues.RemoveAll(i => i.Severity == IssueSeverity.Error || i.Key == "Placemaker.Parking.Issue.NoEntrance");
            Merge(sub);
            // (s25) The lot graded itself (level tiers on a slope): the rest of the mall plans on that ground.
            if (sub.HeightAfter != null) _lotGround.Add((lot, sub.HeightAfter));
            // (s58) The aprons of the lot's kerb connectors (lot edge to the street's kerb) are fixed ground as well: the sculpt must not bank over them.
            foreach (var ap in sub.Terraform)
                if (ap.Apron && ap.Ring.Length >= 3) { var ar = ap.Ring; _lotGround.Add((Poly.EnsureCcw(Poly.To2D(ar)), p => Build.TerraformSurface.At(ar, p))); }
            Stalls += (int)t.Capacity; EvStalls += (int)t.Ev;
            Lots.Add(lot);
            if (leftover && !Poly.IsConvex(lot)) { var parts = ConvexDecomposition.Decompose(lot); if (parts.Count > 0) Taken.AddRange(parts); else Taken.Add(lot); }
            else Taken.Add(lot);
            return true;
        }

        /// <summary>A lot on a frontage run (centred, up to <paramref name="width"/> wide), entered from that street; shrinks to fit.</summary>
        public bool FrontLot(StreetShops.Run r, float width, int aisles, SettingsBag parking, uint salt)
        {
            float from = r.From + (r.StartCorner ? CornerClear : 2f), to = r.To - (r.EndCorner ? CornerClear : 2f);
            float c = 0.5f * (from + to);
            for (float w = math.min(width, to - from); w >= 28f; w -= 8f)
                for (int a = aisles; a >= 1; a--)
                {
                    float d = LotDepth(a);
                    var rect = Rect(r, c - w * 0.5f, c + w * 0.5f, 0f, d);
                    if (!Fits(rect)) continue;
                    var e = new List<(float2, string)>();
                    if (w > 90f) { e.Add((r.At(c - w * 0.25f, 0f), "entrance")); e.Add((r.At(c + w * 0.25f, 0f), "entrance")); }
                    else e.Add((r.At(c, 0f), "entrance"));
                    if (Lot(rect, e, parking, salt)) return true;
                }
            return false;
        }

        public static SettingsBag Parking(LayerContext ctx)
        {
            var b = new SettingsBag();
            b.Values["stallAngle"] = ctx.S("stallAngle");
            b.Values["greenery"] = ctx.S("lotGreen");
            b.Values["evShare"] = (double)ctx.Raw("evShare");
            b.Values["lanterns"] = ctx.B("lamps");
            b.Values["perimeterRoad"] = false;
            b.Values["setback"] = 1.0;
            // (rf-shops3) Wider planted medians between the rows of bays (real-3: long tree-lined medians), when the type asks.
            if (ctx.Settings.Values.ContainsKey("lotMedian") && ctx.Raw("lotMedian") > 2.5f) b.Values["medianDepth"] = (double)ctx.Raw("lotMedian");
            return b;
        }

        /// <summary>Lot depth for a number of double-loaded aisles (two 5.9 m bays + a 7 m aisle, 2.5 m medians).</summary>
        public static float LotDepth(int aisles) => aisles <= 0 ? 0f : aisles * 18.8f + (aisles - 1) * 2.5f + 2.5f;

        public static int Aisles(string size) => size == "small" ? 1 : size == "large" ? 3 : 2;

        // ───────────── green / plaza sub-plans ─────────────

        /// <summary>The site minus everything taken: pieces of at least <paramref name="minArea"/>, largest first.</summary>
        public List<float2[]> Rest(float minArea)
        {
            var list = new List<float2[]>();
            if (Taken.Count == 0) { list.Add(Region.Ring); return list; }
            var clip = new List<IReadOnlyList<float2>>();
            foreach (var t in Taken) clip.Add(Poly.EnsureCcw(t));
            List<Region> pieces;
            try { pieces = Poly.Boolean(new[] { (IReadOnlyList<float2>)Region.Ring }, clip, BoolOp.Difference); }
            catch (Exception) { return list; }
            list.AddRange(Simple(pieces, minArea));
            list.Sort((a, b) => Poly.Area(b).CompareTo(Poly.Area(a)));
            return list;
        }

        /// <summary>Simple rings of boolean pieces (holes slit open), at least <paramref name="minArea"/>.</summary>
        private static List<float2[]> Simple(List<Region> pieces, float minArea)
        {
            var list = new List<float2[]>();
            // A piece wrapping something (a lot with green all round) has a hole: one ring cannot say that, and a
            // planner given the outer ring would plan over the hole. Slit such pieces through the hole until simple.
            var queue = new Queue<(Region r, int depth)>();
            foreach (var p in pieces) queue.Enqueue((p, 0));
            var simple = new List<Region>();
            while (queue.Count > 0)
            {
                var (p, depth) = queue.Dequeue();
                if (p?.Outer == null || p.Outer.Length < 3) continue;
                if (p.Holes.Count == 0 || depth >= 6) { if (p.Holes.Count == 0) simple.Add(p); continue; }
                var hc = Poly.Centroid(p.Holes[0]);
                float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
                foreach (var v in p.Outer) { lo = math.min(lo, v); hi = math.max(hi, v); }
                var slit = new[] { new float2(lo.x - 1f, hc.y - 0.15f), new float2(hi.x + 1f, hc.y - 0.15f), new float2(hi.x + 1f, hc.y + 0.15f), new float2(lo.x - 1f, hc.y + 0.15f) };
                var cut = new List<IReadOnlyList<float2>> { slit };
                foreach (var h in p.Holes) cut.Add(Poly.EnsureCcw(h));
                try { foreach (var q in Poly.Boolean(new[] { (IReadOnlyList<float2>)p.Outer }, cut, BoolOp.Difference)) queue.Enqueue((q, depth + 1)); }
                catch (Exception) { }
            }
            foreach (var p in simple)
            {
                var ring = Poly.EnsureCcw(Poly.Clean(p.Outer, 0.05f));
                if (ring.Length >= 3 && Poly.Area(ring) >= minArea) list.Add(ring);
            }
            return list;
        }

        /// <summary>
        /// The rest split into wide parts (a morphological opening: shrink by <paramref name="half"/>, grow back) and the thin
        /// arms left over (strips beside lane legs, behind rows) — parks on the wide parts, planted buffers on the arms, so
        /// no park path runs along a thin arm hugging the outline.
        /// </summary>
        public List<(float2[] ring, bool strip)> RestSplit(float minArea, float half = 9f)
        {
            var result = new List<(float2[], bool)>();
            foreach (var piece in Rest(minArea))
            {
                List<Region> core;
                try { core = Poly.Offset(Poly.Offset(piece, half, OffsetJoin.Miter), -half, OffsetJoin.Miter); }
                catch (Exception) { core = null; }
                var wide = new List<float2[]>();
                if (core != null)
                    foreach (var c in core)
                    {
                        if (c?.Outer == null || c.Outer.Length < 3) continue;
                        List<Region> clipped;
                        try { clipped = Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(c.Outer) }, BoolOp.Intersection); }
                        catch (Exception) { continue; }
                        wide.AddRange(Simple(clipped, minArea));
                    }
                if (wide.Count == 0) { result.Add((piece, true)); continue; }
                foreach (var w in wide) result.Add((w, false));
                var clip = new List<IReadOnlyList<float2>>();
                foreach (var w in wide) clip.Add(w);
                try { foreach (var t in Simple(Poly.Boolean(new[] { (IReadOnlyList<float2>)piece }, clip, BoolOp.Difference), 40f)) result.Add((t, true)); }
                catch (Exception) { }
            }
            result.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : Poly.Area(b.Item1).CompareTo(Poly.Area(a.Item1)));
            return result;
        }

        /// <summary>
        /// Plans <paramref name="ring"/> with another type's planner (park, plaza, marketSquare…) and merges the result.
        /// <paramref name="doors"/>: outline points where the piece should open (a lane end, the lot) besides its own suggestions.
        /// </summary>
        public bool Fill(float2[] ring, string layerId, string preset, IReadOnlyList<float2> doors, uint salt, bool suggested = true, Action<SettingsBag> edit = null, float doorSnap = 2.5f, Func<float2, bool> keepSuggested = null,
            string[] childLayers = null, string[] childTargets = null, List<PlannedRegion> places = null)
        {
            var gen = GeneratorRegistry.Get(layerId) as LayerBase;
            var region = ring == null ? null : LayerRegion.From(ring);
            if (gen == null || region == null || region.Area < gen.MinArea) return false;
            var settings = gen.Defaults();
            if (!string.IsNullOrEmpty(preset))
                foreach (var p in gen.Presets)
                    if (p.Id == preset) { settings = p.Values.Clone(); break; }
            edit?.Invoke(settings);
            var input = new AreaInput
            {
                Shape = new AreaShape { Ring = Poly.To3D(ring, Ctx.G), Source = AreaSource.Drawn },
                Site = Ctx.Site, Settings = settings, Seed = Hashing.Mix(Ctx.Seed, salt), RegionTags = new[] { RegionTags.Green },
                Reserved = Ctx.Input.Reserved, Markers = new List<EdgeMarker>(),
                // (s32) Features the caller will plan itself: the planner carves their places (playground clearing, plaza).
                ChildLayers = childLayers ?? Array.Empty<string>(), ChildTargets = childTargets ?? Array.Empty<string>(),
            };
            string kind = null;
            if (suggested)
                foreach (var m in gen.SuggestMarkers(input) ?? new List<EdgeMarker>())
                {
                    // (s32) Only where people arrive: an entrance on a shop's back wall would end a walk at a plot.
                    if (keepSuggested != null && !keepSuggested(m.Position.xz)) continue;
                    input.Markers.Add(m);
                    kind = kind ?? m.Kind;
                }
            if (doors != null)
            {
                if (kind == null)
                    foreach (var c in gen.Controls)
                        if (c.Kind == ControlKind.Markers && c.MarkerKinds != null && c.MarkerKinds.Length > 0) { kind = c.MarkerKinds[0]; break; }
                if (kind == EntranceKinds.Main || kind == EntranceKinds.Service || kind == EntranceKinds.Bike) kind = EntranceKinds.Pedestrian;
                foreach (var d in doors)
                {
                    var p = region.ClosestOnBoundary(d, out int seg, out float t);
                    if (math.distance(p, d) > math.max(6f, doorSnap + 0.5f)) continue;
                    bool near = false;
                    foreach (var m in input.Markers) if (math.distance(m.Position.xz, p) < 8f) near = true;
                    if (!near) input.Markers.Add(new EdgeMarker { Kind = kind ?? "entrance", RingSegment = seg, RingT = t, Position = Ctx.P3(p) });
                }
            }
            AreaPlan sub;
            try { sub = gen.Plan(input, Ctx.Cancel); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { sub = null; }
            if (sub == null || sub.HasErrors) return false;
            // Its places / cores stay its own business: only plain green / paved tags are offered to the mall's features.
            if (places != null) foreach (var g in sub.Regions) if (Composer.IsPlace(g)) places.Add(g);
            sub.Regions.RemoveAll(g => Composer.IsPlace(g) || Array.IndexOf(g.Tags, StreetFrontage.ShopsTag) >= 0);
            // CS2 joins nets only at identical nodes: a path ending at a door ends exactly on it (a lane's end node).
            if (doors != null)
            {
                float snap2 = doorSnap * doorSnap;
                foreach (var n in sub.Nets)
                {
                    if (!PathJoins.IsPath(n)) continue;
                    foreach (var d in doors)
                    {
                        if (n.StartAttach < 0 && math.distancesq(n.A.xz, d) < snap2) { var dd = Ctx.P3(d) - n.A; n.A += dd; n.B += dd; }
                        if (n.EndAttach < 0 && math.distancesq(n.D.xz, d) < snap2) { var dd = Ctx.P3(d) - n.D; n.D += dd; n.C += dd; }
                    }
                }
            }
            sub.Issues.RemoveAll(i => i.Severity == IssueSeverity.Error);
            Merge(sub);
            Take(ring);
            return true;
        }

        /// <summary>A leftover narrower than ~18 m (deepest point under 9 m from its edge).</summary>
        public bool IsStrip(float2[] ring)
        {
            var r = LayerRegion.From(ring);
            if (r == null) return true;
            r.DeepestPoint(out float clearance, null, 16);
            return clearance < 9f;
        }

        /// <summary>
        /// (shops4) The ground of a planted piece. The lot-first strip mall paints it (terrain paint, never a lawn surface: the user,
        /// "grass patches going onto the roads ... we've moved on to terrain paint"); the other layouts keep their lawn surface.
        /// </summary>
        public void Lawn(float2[] ring)
        {
            if (!LotFirstMode) { Ctx.Surface(ring, SurfaceRole.Grass, new AssetRef(""), AssetCategories.SurfaceGrass); return; }
            // Heavy, textured ground (the user: "texture terrain across the board, even heavy amounts, like sand or dirt"): dry grass over
            // the whole piece, dirt and sand patches, lusher tufts. It also covers the flat stripes the zone block strips leave along the
            // city streets (a lot area takes the grass detail off the ground under it).
            try
            {
                var kit = PaintKit();
                foreach (var part in Poly.Offset(Poly.EnsureCcw(ring), 0.5f, OffsetJoin.Miter))
                {
                    if (part?.Outer == null || part.Outer.Length < 3 || Poly.Area(part.Outer) < 20f) continue;
                    var p = part.Outer;
                    kit.Fill(p, TerrainMaterials.Dry, 1f, 1f, 1.6f, 9f);
                    kit.Speckle(p, TerrainMaterials.Dirt, 1f, 8f, 0.5f, 0.3f, 1f);
                    kit.Speckle(p, TerrainMaterials.Sand, 0.9f, 14f, 0.22f, 0.35f, 1f);
                    kit.Speckle(p, TerrainMaterials.Lush, 0.8f, 5f, 0.22f, 0.3f, 1f);
                }
            }
            catch (Exception) { }
        }

        /// <summary>A planted buffer: lawn and a loose line of trees, no paths (strips beside lanes and rows).</summary>
        public void Buffer(float2[] ring, uint salt)
        {
            if (BackOfHouse) { BackYard(ring, salt); return; }
            var region = LayerRegion.From(ring);
            if (region == null) return;
            Lawn(ring);
            var rng = Hashing.Rng(Hashing.Mix(Ctx.Seed, salt), "buffer");
            var placed = new List<float2>();
            float step = 4f;
            for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
                for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                {
                    var p = new float2(x, y) + new float2(rng.NextFloat(-1.2f, 1.2f), rng.NextFloat(-1.2f, 1.2f));
                    if (!region.Contains(p) || region.BoundaryDistance(p, 4f) < 3.2f) continue;
                    bool near = false;
                    foreach (var q in placed) if (math.distancesq(q, p) < 81f) { near = true; break; }
                    if (near) continue;
                    placed.Add(p);
                    Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Tree, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + (uint)placed.Count), 1 + (int)(rng.NextUInt() % 2));
                }
            Take(ring);
        }

        private void Merge(AreaPlan sub)
        {
            var plan = Ctx.Plan;
            int offset = plan.Nets.Count;
            plan.Nets.AddRange(sub.Nets);
            foreach (var o in sub.Objects)
            {
                if (o.AttachedNet >= 0) o.AttachedNet += offset;
                plan.Objects.Add(o);
            }
            plan.Surfaces.AddRange(sub.Surfaces);
            plan.Areas.AddRange(sub.Areas);
            plan.Terraform.AddRange(sub.Terraform);
            plan.Paint.AddRange(sub.Paint);
            plan.Zoning.AddRange(sub.Zoning);
            plan.Regions.AddRange(sub.Regions);
            plan.Stats.AddRange(sub.Stats);
            plan.Issues.AddRange(sub.Issues);
            foreach (var kv in sub.Facts)
                plan.Facts[kv.Key] = plan.Facts.TryGetValue(kv.Key, out var v) && kv.Key != "parking.fee" ? v + kv.Value : kv.Value;
        }

        // ───────────── finish: zoning, lanes, regions ─────────────

        /// <summary>Zones every shop plot from the street / lane it faces, emits the lanes and tags the cutouts.</summary>
        public void Finish(string zoneId)
        {
            var plan = Ctx.Plan;
            if (Grading) MallGrading.Grade(this, _lotGround);
            var zoning = new EdgeZoningAddOn();
            var reserved = new ReservedSpace();
            foreach (var lane in Lanes)
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                    reserved.AddCorridor(lane.Points[i], lane.Points[i + 1], lane.Half, lane.Role == NetRole.Alley ? nameof(NetRole.Alley) : nameof(NetRole.Street));
            int cells = 0;
            for (int i = 0; i < Shops.Count; i++)
            {
                var ring = Shops[i];
                var bag = new SettingsBag();
                // (s32) A plot may carry its own zone (big high-density plots on the street, small low-density ones on the lanes).
                bag.Values[EdgeZoningAddOn.ZoneControl] = ShopZone.TryGetValue(ring, out var own) && !string.IsNullOrEmpty(own) ? (ZoneKinds.IsKind(own) ? ZoneKinds.PrefabId(own) : own) : zoneId;
                bag.Values[EdgeZoningAddOn.CutoutSetting] = true;
                bool cellPlot = CellPlots.Contains(ring);
                var input = new AreaInput
                {
                    Shape = new AreaShape { Ring = Poly.To3D(ring, Ctx.G), Source = AreaSource.Drawn },
                    Site = Ctx.Site, Settings = bag, Reserved = reserved,
                    RegionTags = cellPlot ? new[] { StreetFrontage.ShopsTag, Build.ZoneLots.CellsTag } : new[] { StreetFrontage.ShopsTag },
                };
                var z = zoning.Plan(input, Ctx.Cancel);
                plan.Zoning.AddRange(z.Zoning);
                foreach (var q in z.Zoning)
                    cells += (int)math.round(Pricing.PolygonArea(new[] { q.A, q.B, q.C, q.D }) / (Cell * Cell));
                if (cellPlot) Ctx.AddRegion(ring, StreetFrontage.ShopsTag + ":" + i, StreetFrontage.ShopsTag, LayerBase.ShopsPlace, Build.ZoneLots.CellsTag);
                else Ctx.AddRegion(ring, StreetFrontage.ShopsTag + ":" + i, StreetFrontage.ShopsTag, LayerBase.ShopsPlace);
            }
            foreach (var lane in Lanes) EmitLane(lane);
            plan.Facts["shopPlots"] = Shops.Count;
            plan.Facts["lanes"] = Lanes.Count;
            plan.Facts["lots"] = Lots.Count;
            plan.Facts["capacity"] = Stalls;
            plan.Facts["evCapacity"] = EvStalls;
            Ctx.Stat("Placemaker.Commercial.Stat.Plots", Shops.Count, Keys.UnitCount);
            Ctx.Stat("Placemaker.Commercial.Stat.ZoneCells", cells, Keys.UnitCount);
        }

        private void EmitLane(Lane lane)
        {
            var pts = LanePieces(lane);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (math.distance(pts[i], pts[i + 1]) < 0.3f) continue;
                var net = Ctx.StraightNet(pts[i], pts[i + 1], lane.Role, lane.Asset);
                if (i == 0 && lane.StartAttach >= 0) { net.StartAttach = lane.StartAttach; net.StartAttachT = lane.StartT; }
                if (i + 2 == pts.Count && lane.EndAttach >= 0) { net.EndAttach = lane.EndAttach; net.EndAttachT = lane.EndT; }
            }
        }

        /// <summary>The node points of a lane as it is built: its corners plus the joins on it (CS2 splits the edge there).</summary>
        private static List<float2> LanePieces(Lane lane)
        {
            // Split points per segment (joins projected onto the segment they lie on).
            var pts = new List<float2>();
            for (int i = 0; i + 1 < lane.Points.Count; i++)
            {
                var a = lane.Points[i]; var b = lane.Points[i + 1];
                pts.Add(a);
                var cut = new List<float>();
                float len = math.distance(a, b);
                foreach (var j in lane.Joins)
                {
                    float s = math.dot(j - a, math.normalizesafe(b - a));
                    if (s <= 1f || s >= len - 1f) continue;
                    if (math.distance(a + math.normalizesafe(b - a) * s, j) > 0.5f) continue;
                    cut.Add(s);
                }
                cut.Sort();
                foreach (var s in cut) pts.Add(a + math.normalizesafe(b - a) * s);
            }
            pts.Add(lane.Points[lane.Points.Count - 1]);
            // Joins exactly on a node move that node onto the join (identical nodes).
            for (int k = 0; k < pts.Count; k++)
                foreach (var j in lane.Joins)
                    if (math.distance(pts[k], j) < 1.01f && !(k == 0 && lane.StartAttach >= 0) && !(k == pts.Count - 1 && lane.EndAttach >= 0)) pts[k] = j;
            return pts;
        }

        /// <summary>
        /// (s40) Zoning first, for the plots facing an EXISTING street: each becomes the exact cells the build will zone
        /// for it (Build/ZoneLots: the street's real — or, with nothing captured, predicted — grid, the game's depth rule,
        /// ≥ 2 columns), never cells of land the mall already gave to a lot, a lane, a walk or a place; a plot that gets
        /// none is given up. Called before the rest is planned, so parks and verges fit the cells exactly.
        /// Plots facing this mall's own lanes keep their rows: a lane's cells depend on where the rest's walks will
        /// join (and split) it, which is not known yet — ZoneGuard snaps them at the end.
        /// </summary>
        /// <summary>(s40) Shop plots that are exactly cells (zoned as they are).</summary>
        public readonly HashSet<float2[]> CellPlots = new HashSet<float2[]>();

        /// <summary>
        /// (core) Plots that face no lane of this mall (rows along the city streets: StreetRows, the shop rows at t = 0 of a run, the
        /// retail park's pads) are not zoned unless <paramref name="streetShops"/>: the user's playtest, "the shops got put on the wrong
        /// side ... they should have been put on the alleyway". The lanes and lane rows stay where the composition put them; the
        /// street plots' land goes back to the rest of the site (lots, planting), and the city street's zoning along the build is
        /// blocked (kerb strips, LaneSides), so every shop that grows faces the build's own lanes. Returns how many were given back.
        /// </summary>
        public int PlantStreetPlots(bool streetShops)
        {
            if (streetShops) return 0;
            var street = new List<float2[]>();
            foreach (var s in Shops) if (!RunsAlongLane(s)) street.Add(s);
            // A composition with no lane plot at all (a style that only fronts the streets) keeps its street plots: a shopping
            // site with no shop is not the answer. Its kerb strips spare those columns (ZoneGuard.KerbStrips).
            if (street.Count == 0 || street.Count == Shops.Count) return 0;
            int n = 0;
            foreach (var s in street)
            {
                // The land goes back to the site: the rest of the composition (FillRest) makes it a lot entered from the street (a
                // strip centre's lot lies between the street and the shops) or planting; the city's cells along it are blocked.
                Shops.Remove(s);
                ShopZone.Remove(s);
                Taken.Remove(s);
                n++;
            }
            Ctx.Plan.Facts["streetPlotsPlanted"] = n;
            return n;
        }



        /// <summary>(core) True when one side of the plot runs along a lane of this mall (Faces, lanes only).</summary>
        public bool RunsAlongLane(float2[] rect)
        {
            bool Near(float2 p)
            {
                foreach (var l in Lanes)
                    for (int i = 0; i + 1 < l.Points.Count; i++)
                        if (math.sqrt(SegmentIndex.DistSq(p, l.Points[i], l.Points[i + 1])) <= l.Half + StreetFrontage.Tolerance) return true;
                return false;
            }
            for (int i = 0; i < rect.Length; i++)
            {
                float2 a = rect[i], b = rect[(i + 1) % rect.Length];
                if (Near(math.lerp(a, b, 0.5f)) && Near(math.lerp(a, b, 0.2f)) && Near(math.lerp(a, b, 0.8f))) return true;
            }
            return false;
        }

        public void SnapStreetPlotsToCells()
        {
            if (Shops.Count == 0 || !Build.ZoneGuard.Enabled) return;
            var planned = new ComposedPlan();
            var lanes = new AreaPlan { GeneratorId = Ctx.Plan.GeneratorId };
            foreach (var lane in Lanes)
            {
                var pts = LanePieces(lane);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    float2 a = pts[i], b = pts[i + 1];
                    if (math.distance(a, b) < 0.3f) continue;
                    lanes.Nets.Add(new PlannedNet { A = Ctx.P3(a), B = Ctx.P3(math.lerp(a, b, 1f / 3f)), C = Ctx.P3(math.lerp(a, b, 2f / 3f)), D = Ctx.P3(b), Role = lane.Role, Asset = lane.Asset });
                }
            }
            planned.Layers.Add(lanes);
            planned.Layers.Add(Ctx.Plan);   // walks and lot aisles close the cells they cross
            var grids = Build.ZoneLots.SiteGrids(Ctx.Site, planned);
            if (grids.Count == 0) return;
            // Land already given away (lots, places, walks, lane corridors): a cell reaching into it is not the plot's.
            var shopSet = new HashSet<float2[]>(Shops);
            var others = new List<float2[]>();
            foreach (var t in Taken)
            {
                if (shopSet.Contains(t)) continue;
                // A shop's own carve (the shop pushed to the kerb) is the shop's land, not someone else's.
                bool carveOfShop = false;
                foreach (var sh in Shops) if (Poly.Contains(t, Poly.Centroid(sh))) { carveOfShop = true; break; }
                if (!carveOfShop) others.Add(Shrink(t, 0.3f));
            }
            // Plots facing a lane keep their rows (below) but are zoned in the same computation: the game's depth rule
            // sees their cells beside the street plots' (a neighbour's depth decides whether a column survives).
            var snapped = Build.ZoneLots.Snap(new[] { Region.Ring }, Shops, grids, Ctx.Site?.NearbyNets, Build.ZoneGuard.OwnNets(planned, Ctx.Site), others);
            if (snapped == null) return;
            var keep = new List<float2[]>();
            var pads = new List<float2[]>();
            for (int i = 0; i < Shops.Count; i++)
            {
                var old = Shops[i];
                if (FacesLane(old)) { keep.Add(old); continue; }
                ShopZone.TryGetValue(old, out var zone);
                if (snapped[i].Count == 0) { pads.Add(old); continue; }
                Taken.Remove(old);
                foreach (var rect in snapped[i])
                {
                    // A lane plot keeps its land (its rows are not cells yet): a street run reaching into it is left out.
                    bool clash = false;
                    foreach (var sh in Shops) if (sh != old && FacesLane(sh) && StreetShops.ConvexOverlap(rect, sh, 0.05f)) { clash = true; break; }
                    if (clash) continue;
                    keep.Add(rect);
                    CellPlots.Add(rect);
                    // The rest keeps 1 m off the cells (a walk edge touching a cell closes it and every cell behind).
                    Taken.Add(Build.ZoneLots.Grow(rect, Build.ZoneLots.Clearance));
                    if (zone != null) ShopZone[rect] = zone;
                }
            }
            // (s46) A plot no cell can be zoned for is not given back to the park: it keeps its land as a paved forecourt
            // (a pad the layout planned for shops), with a warning.
            int padsKept = 0;
            foreach (var pad in pads)
            {
                bool clash = false;
                foreach (var o in keep) if (StreetShops.ConvexOverlap(pad, o, 0.05f)) { clash = true; break; }
                if (clash) { Taken.Remove(pad); continue; }
                Ctx.Surface(Poly.EnsureCcw(pad), SurfaceRole.Paving, new AssetRef(""), AssetCategories.SurfacePaving);
                padsKept++;
            }
            if (padsKept > 0) Ctx.Issue(IssueSeverity.Warning, CommercialLayer.PadKey);
            Shops.Clear();
            Shops.AddRange(keep);
        }

        /// <summary>Does a plot touch one of this mall's lanes (its zoning comes from the lane)?</summary>
        private bool FacesLane(float2[] plot)
        {
            foreach (var lane in Lanes)
                for (int i = 0; i + 1 < lane.Points.Count; i++)
                    foreach (var p in plot)
                        if (math.sqrt(SegmentIndex.DistSq(p, lane.Points[i], lane.Points[i + 1])) < lane.Half + StreetFrontage.Tolerance) return true;
            return false;
        }

        // ───────────── helpers ─────────────

        public static float2[] Largest(List<Region> pieces, float minArea)
        {
            float2[] best = null; float bestArea = minArea;
            if (pieces == null) return null;
            foreach (var r in pieces)
            {
                if (r?.Outer == null || r.Outer.Length < 3) continue;
                float a = Poly.Area(r.Outer);
                if (a > bestArea) { bestArea = a; best = r.Outer; }
            }
            return best == null ? null : Poly.EnsureCcw(Poly.Clean(best, 0.05f));
        }

        public static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            var r = new float2[q.Length];
            for (int i = 0; i < q.Length; i++) r[i] = q[i] + math.normalizesafe(c - q[i]) * by;
            return r;
        }
    }
}

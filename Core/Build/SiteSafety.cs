// Placemaker — existing buildings are never destroyed by a build (s15, playtest: "when it builds, it
// destroys the buildings because it's putting stuff on them").
//
// Why buildings die (decompiled 1.6.2f1, docs/RESEARCH-NOTES-ADDENDUM "Existing buildings"):
//   * Spawnable (zoned) buildings get GeometryFlags.Overridable | DeleteOverridden
//     (BuildingInitializeSystem). A new net or physical area that overlaps one gives the BUILDING an
//     ErrorSeverity.Warning (Net/Areas.ValidationHelpers) and Apply deletes it — that is how vanilla
//     roads demolish houses. A non-overridable new object (or an optional one) does the same.
//   * A new ground net (CompositionState.BlockZone) or physical area blocks the zone cells under it;
//     ZoneCheckSystem then marks a building whose lot cells are no longer zoned as Condemned.
//   * Zoning quads that rezone the cells under a building condemn it as well.
// So nothing we build may touch a building LOT (which also covers the zone cells it lives on).
//
// This pass runs at the end of Composer.Compose on the site's building obstacles (SiteCapture: lot
// quads and, where larger, the collision footprint) and again for objects after Declutter (nudges):
//   * nets touching a lot (centre line within half width + margin) are dropped, with the objects
//     attached to them (AttachedNet indices are remapped);
//   * objects whose footprint touches a lot are dropped (markers never collide and stay);
//   * surfaces, areas, terraform and paint rings are cut around lots (CS2-acceptable pieces);
//   * zoning quads touching a lot are dropped.
// Review shows "N buildings in the way, kept" when the selection covers buildings, plus what was
// left out. Pure and deterministic.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Build
{
    public static class SiteSafety
    {
        public const string IssueKept = "Placemaker.Issue.BuildingsKept";
        public const string IssueLeftOut = "Placemaker.Issue.BuildingsLeftOut";
        /// <summary>Clearance (m) kept between anything planned and a building lot.</summary>
        public const float Margin = 0.5f;

        public sealed class Report
        {
            /// <summary>Distinct buildings whose lot overlaps the selection.</summary>
            public int BuildingsInArea;
            public int Nets, Objects, Surfaces, Areas, Terraform, Paint, Zoning;
            /// <summary>Rings that were cut (kept smaller) rather than dropped.</summary>
            public int Cut;
            public float3? FirstAt;
            public int LeftOut => Nets + Objects + Surfaces + Areas + Terraform + Paint + Zoning;

            internal void At(float2 p, float y = 0f)
            {
                if (!FirstAt.HasValue) FirstAt = new float3(p.x, y, p.y);
            }
        }

        /// <summary>Polygons nothing may touch: every building obstacle of the site (lots and footprints).</summary>
        public static List<float2[]> Protected(SiteContext site)
        {
            var list = new List<float2[]>();
            if (site?.Obstacles == null) return list;
            foreach (var o in site.Obstacles)
            {
                if (o.Kind != ObstacleKind.Building) continue;
                var q = new[] { o.A, o.B, o.C, o.D };
                bool ok = true;
                foreach (var p in q) if (!Poly.IsFinite(p)) { ok = false; break; }
                if (!ok || Poly.Area(q) < 0.5f) continue;
                list.Add(Poly.EnsureCcw(q));
            }
            return list;
        }

        /// <summary>(s59) The captured city nets (not the synthetic lanes a composite adds to its child sites: negative ids).</summary>
        public static List<NearbyNet> ExistingNets(SiteContext site)
        {
            var list = new List<NearbyNet>();
            if (site?.NearbyNets == null) return list;
            foreach (var n in site.NearbyNets)
                if (n.ExternalId >= 0 && n.Width > 0f && math.all(math.isfinite(n.A)) && math.all(math.isfinite(n.D))) list.Add(n);
            return list;
        }

        /// <summary>Number of protected polygons overlapping the ring.</summary>
        public static int CountOverlapping(IReadOnlyList<float2> ring, IReadOnlyList<float2[]> protectedPolys)
        {
            int n = 0;
            if (ring == null || ring.Count < 3 || protectedPolys == null) return 0;
            foreach (var p in protectedPolys) if (ObstacleCut.Overlaps(ring, p)) n++;
            return n;
        }

        /// <summary>
        /// Composer hook: records the protected polygons on the plan (Declutter re-checks objects after
        /// nudging), removes/cuts what would touch them and adds the Review issues. No-op without buildings.
        /// </summary>
        public static Report Apply(ComposedPlan plan, AreaShape area, SiteContext site)
        {
            var report = new Report();
            if (plan == null) return report;
            var prot = Protected(site);
            plan.Protected = prot;
            plan.ExistingNets = ExistingNets(site);
            if (prot.Count == 0) return report;

            // Buildings counted once even when both the lot and the footprint quad overlap.
            if (area?.Ring != null && area.Ring.Length >= 3)
            {
                var ring = area.Ring2D();
                var seen = new List<float2>();
                foreach (var p in prot)
                {
                    if (!ObstacleCut.Overlaps(ring, p)) continue;
                    float2 c = Poly.Centroid(p);
                    bool dup = false;
                    foreach (var s in seen) if (math.distance(s, c) < 4f) { dup = true; break; }
                    if (dup) continue;
                    seen.Add(c);
                    report.BuildingsInArea++;
                }
            }

            var grown = ObstacleCut.Grow(prot, Margin);
            Func<float2, float> height = site?.HeightAt ?? (_ => 0f);
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                FilterNets(l, prot, site?.Assets, report);
                FilterObjectsOf(l, prot, site?.Assets, report);
                CutSurfaces(l, grown, height, report);
                CutAreas(l, grown, height, report);
                CutTerraform(l, grown, report);
                CutPaint(l, grown, report);
                FilterZoning(l, prot, report);
            }

            // a Zoning look polishes the buildings in the area: them being there is the point, not news
            bool looks = plan.Layers.Count > 0 && plan.Layers.All(l => l != null && GeneratorRegistry.Get(l.GeneratorId) is Placemaker.Core.Layers.Types.Zoning.PolishLookLayer);
            if (report.BuildingsInArea > 0 && !looks)
                plan.Issues.Add(new PlanIssue
                {
                    Severity = IssueSeverity.Info, Key = IssueKept, Args = new object[] { report.BuildingsInArea },
                });
            if (report.LeftOut > 0 || report.Cut > 0)
                plan.Issues.Add(new PlanIssue
                {
                    Severity = IssueSeverity.Info, Key = IssueLeftOut, Args = new object[] { report.LeftOut + report.Cut }, At = report.FirstAt,
                });
            return report;
        }

        /// <summary>
        /// Objects only (after Declutter moved some): drops those touching <see cref="ComposedPlan.Protected"/>.
        /// Returns how many were dropped.
        /// </summary>
        public static int FilterObjects(ComposedPlan plan, IReadOnlyDictionary<string, AssetInfo> assets)
        {
            if (plan?.Protected == null || plan.Protected.Count == 0) return 0;
            var report = new Report();
            foreach (var l in plan.Layers) if (l != null) FilterObjectsOf(l, plan.Protected, assets, report);
            return report.Objects;
        }

        /// <summary>
        /// Build-time guard: the work items (nets, physical areas, objects, zoning) that touch any of the
        /// polygons — used when the game still marks an existing building for removal (Warning) after our
        /// temporaries materialised, so the build leaves those items out and retries.
        /// </summary>
        public static List<WorkItem> ItemsTouching(ComposedPlan plan, BuildWork work, IReadOnlyList<float2[]> polys,
            IReadOnlyDictionary<string, AssetInfo> assets, float extra = Margin)
        {
            var hits = new List<WorkItem>();
            if (plan?.Layers == null || work == null || polys == null || polys.Count == 0) return hits;
            foreach (var w in work.All())
            {
                if (w.Layer < 0 || w.Layer >= plan.Layers.Count) continue;
                var l = plan.Layers[w.Layer];
                if (l == null) continue;
                bool hit = false;
                foreach (var p in polys)
                {
                    switch (w.Kind)
                    {
                        case WorkKind.Net:
                            if (w.Index < l.Nets.Count) hit = NetTouches(l.Nets[w.Index], Declutter.NetHalfWidth(l.Nets[w.Index], assets), p, extra);
                            break;
                        case WorkKind.Object:
                            if (w.Index < l.Objects.Count) hit = ObjectTouches(l.Objects[w.Index], assets, p, extra);
                            break;
                        case WorkKind.Area:
                            if (w.Index < l.Areas.Count && l.Areas[w.Index]?.Ring != null) hit = ObstacleCut.Overlaps(Poly.To2D(l.Areas[w.Index].Ring), p);
                            break;
                        case WorkKind.Zoning:
                            if (w.Index < l.Zoning.Count && l.Zoning[w.Index] != null)
                            {
                                var z = l.Zoning[w.Index];
                                hit = ObstacleCut.Overlaps(Shrink(new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz }, 0.5f), p);
                            }
                            break;
                    }
                    if (hit) break;
                }
                if (hit) hits.Add(w);
            }
            return hits;
        }

        // ───────────────────────── nets ─────────────────────────

        /// <summary>True when the net (centre curve ± half width + extra) touches the polygon.</summary>
        public static bool NetTouches(PlannedNet n, float half, IReadOnlyList<float2> poly, float extra = Margin)
        {
            if (n == null || poly == null || poly.Count < 3) return false;
            float reach = half + extra;
            Poly.Bounds(poly, out var pmin, out var pmax);
            float2 nmin = math.min(math.min(n.A.xz, n.B.xz), math.min(n.C.xz, n.D.xz)) - reach;
            float2 nmax = math.max(math.max(n.A.xz, n.B.xz), math.max(n.C.xz, n.D.xz)) + reach;
            if (math.any(nmax < pmin) || math.any(pmax < nmin)) return false;
            float approx = math.distance(n.A.xz, n.B.xz) + math.distance(n.B.xz, n.C.xz) + math.distance(n.C.xz, n.D.xz);
            if (!math.isfinite(approx)) return false;
            int count = math.clamp((int)math.ceil(approx / 2f), 1, 256);
            float2 prev = n.A.xz;
            if (Poly.Contains(poly, prev)) return true;
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                float2 p = Composer.Bezier(n.A, n.B, n.C, n.D, t).xz;
                if (Poly.Contains(poly, p) || SegmentPolygonDistance(prev, p, poly) <= reach) return true;
                prev = p;
            }
            return false;
        }

        private static void FilterNets(AreaPlan l, IReadOnlyList<float2[]> prot, IReadOnlyDictionary<string, AssetInfo> assets, Report r)
        {
            if (l.Nets == null || l.Nets.Count == 0) return;
            int[] map = null;
            var kept = new List<PlannedNet>(l.Nets.Count);
            for (int i = 0; i < l.Nets.Count; i++)
            {
                var n = l.Nets[i];
                bool hit = false;
                if (n != null)
                {
                    float half = Declutter.NetHalfWidth(n, assets);
                    foreach (var p in prot) if (NetTouches(n, half, p)) { hit = true; break; }
                }
                if (hit)
                {
                    if (map == null) { map = new int[l.Nets.Count]; for (int k = 0; k < i; k++) map[k] = k; }
                    map[i] = -1;
                    r.Nets++;
                    r.At(Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz, n.A.y);
                    continue;
                }
                if (map != null) map[i] = kept.Count;
                kept.Add(n);
            }
            if (map == null) return;
            l.Nets.Clear();
            l.Nets.AddRange(kept);
            // Objects hang on nets by index: remap, and drop the ones whose net is gone.
            if (l.Objects == null) return;
            for (int i = l.Objects.Count - 1; i >= 0; i--)
            {
                var o = l.Objects[i];
                if (o == null || o.AttachedNet < 0) continue;
                int to = o.AttachedNet < map.Length ? map[o.AttachedNet] : -1;
                if (to < 0) { l.Objects.RemoveAt(i); r.Objects++; }
                else o.AttachedNet = to;
            }
        }

        // ───────────────────────── objects ─────────────────────────

        /// <summary>True when the object's footprint (asset bounds, else its role's default radius) touches the polygon.</summary>
        public static bool ObjectTouches(PlannedObject o, IReadOnlyDictionary<string, AssetInfo> assets, IReadOnlyList<float2> poly, float extra = 0.3f)
        {
            if (o == null || poly == null || poly.Count < 3) return false;
            float2 c;
            float radius;
            if (Declutter.TryFootprint(o, assets, out var f))
            {
                c = f.Center;
                // Exact box test: any box corner inside the polygon, the centre inside, or an edge within extra.
                var corners = new[]
                {
                    f.Center + f.AxisX * f.Half.x + f.AxisZ * f.Half.y, f.Center - f.AxisX * f.Half.x + f.AxisZ * f.Half.y,
                    f.Center - f.AxisX * f.Half.x - f.AxisZ * f.Half.y, f.Center + f.AxisX * f.Half.x - f.AxisZ * f.Half.y,
                };
                if (Poly.Contains(poly, c)) return true;
                for (int i = 0; i < 4; i++)
                    if (SegmentPolygonDistance(corners[i], corners[(i + 1) % 4], poly) <= extra) return true;
                // The polygon entirely inside the box.
                return Poly.Contains(corners, poly[0]);
            }
            c = o.Position.xz;
            radius = Composer.DefaultRadius(o.Role) * (o.Scale > 0.05f && math.isfinite(o.Scale) ? o.Scale : 1f);
            if (!Poly.IsFinite(c)) return false;
            return Poly.Contains(poly, c) || Poly.DistanceToBoundary(poly, c) <= radius + extra;
        }

        private static void FilterObjectsOf(AreaPlan l, IReadOnlyList<float2[]> prot, IReadOnlyDictionary<string, AssetInfo> assets, Report r)
        {
            if (l.Objects == null || l.Objects.Count == 0) return;
            for (int i = l.Objects.Count - 1; i >= 0; i--)
            {
                var o = l.Objects[i];
                if (o == null || o.Role == ObjectRole.Marker) continue;   // markers never collide in game mode
                foreach (var p in prot)
                {
                    if (!ObjectTouches(o, assets, p)) continue;
                    l.Objects.RemoveAt(i);
                    // Decals keep off lots too, but are dressing, not something the player planned (s21): not counted.
                    if (o.Role != ObjectRole.Decal) { r.Objects++; r.At(o.Position.xz, o.Position.y); }
                    break;
                }
            }
        }

        // ───────────────────────── rings ─────────────────────────

        private static void CutSurfaces(AreaPlan l, List<float2[]> grown, Func<float2, float> height, Report r)
        {
            if (l.Surfaces == null || l.Surfaces.Count == 0) return;
            var result = new List<PlannedSurface>(l.Surfaces.Count);
            foreach (var s in l.Surfaces)
            {
                if (s?.Ring == null || s.Ring.Length < 3) { result.Add(s); continue; }
                var pieces = AcceptablePieces(Poly.To2D(s.Ring), grown, out bool touched);
                if (!touched) { result.Add(s); continue; }
                if (pieces.Count == 0) { r.Surfaces++; r.At(Poly.Centroid(Poly.To2D(s.Ring))); continue; }
                r.Cut++;
                foreach (var p in pieces)
                    result.Add(new PlannedSurface { Ring = Poly.To3D(p, height), Role = s.Role, Asset = s.Asset, Category = s.Category });
            }
            l.Surfaces.Clear();
            l.Surfaces.AddRange(result);
        }

        private static void CutAreas(AreaPlan l, List<float2[]> grown, Func<float2, float> height, Report r)
        {
            if (l.Areas == null || l.Areas.Count == 0) return;
            var result = new List<PlannedArea>(l.Areas.Count);
            foreach (var a in l.Areas)
            {
                if (a?.Ring == null || a.Ring.Length < 3) { result.Add(a); continue; }
                var pieces = AcceptablePieces(Poly.To2D(a.Ring), grown, out bool touched);
                if (!touched) { result.Add(a); continue; }
                if (pieces.Count == 0) { r.Areas++; r.At(Poly.Centroid(Poly.To2D(a.Ring))); continue; }
                r.Cut++;
                foreach (var p in pieces) result.Add(new PlannedArea { Ring = Poly.To3D(p, height), Role = a.Role, Asset = a.Asset });
            }
            l.Areas.Clear();
            l.Areas.AddRange(result);
        }

        private static void CutTerraform(AreaPlan l, List<float2[]> grown, Report r)
        {
            if (l.Terraform == null || l.Terraform.Count == 0) return;
            var result = new List<PlannedTerraform>(l.Terraform.Count);
            foreach (var t in l.Terraform)
            {
                if (t?.Ring == null || t.Ring.Length < 3) { result.Add(t); continue; }
                var pieces = HoleFreePieces(Poly.To2D(t.Ring), grown, out bool touched);
                if (!touched) { result.Add(t); continue; }
                if (pieces.Count == 0) { r.Terraform++; r.At(Poly.Centroid(Poly.To2D(t.Ring))); continue; }
                r.Cut++;
                float y = t.Ring[0].y;
                var src = t;
                // Sloped (s20): the cut piece keeps the same surface (vertex heights re-interpolated).
                Func<float2, float> h = t.Sloped ? (Func<float2, float>)(q => TerraformSurface.At(src.Ring, q)) : _ => y;
                foreach (var p in pieces)
                    result.Add(new PlannedTerraform
                    {
                        Ring = Poly.To3D(p, h), Kind = t.Kind, TargetHeight = t.TargetHeight, Permanent = t.Permanent,
                        WaterLevel = t.WaterLevel, WaterAnchor = t.WaterAnchor, Sloped = t.Sloped,
                    });
            }
            l.Terraform.Clear();
            l.Terraform.AddRange(result);
        }

        private static void CutPaint(AreaPlan l, List<float2[]> grown, Report r)
        {
            if (l.Paint == null || l.Paint.Count == 0) return;
            var result = new List<PlannedPaint>(l.Paint.Count);
            foreach (var p in l.Paint)
            {
                if (p?.Ring == null || p.Ring.Length < 3) { result.Add(p); continue; }
                var pieces = HoleFreePieces(Poly.To2D(p.Ring), grown, out bool touched);
                if (!touched) { result.Add(p); continue; }
                if (pieces.Count == 0) { r.Paint++; continue; }
                r.Cut++;
                if (p.Shape != null)
                {
                    // (s33) A shape paint keeps its outline; the lots are cut out of its field.
                    var holes = new List<Paint.PaintShape>();
                    foreach (var g in grown) holes.Add(Paint.PaintShape.Polygon(g, 0.25f));
                    var cut = p.WithRing(p.Ring);
                    cut.Shape = p.Shape.Subtract(Paint.PaintShape.Union(holes));
                    result.Add(cut);
                    continue;
                }
                float y = p.Ring[0].y;
                foreach (var q in pieces) result.Add(p.WithRing(Poly.To3D(q, _ => y)));
            }
            l.Paint.Clear();
            l.Paint.AddRange(result);
        }

        private static void FilterZoning(AreaPlan l, IReadOnlyList<float2[]> prot, Report r)
        {
            if (l.Zoning == null || l.Zoning.Count == 0) return;
            for (int i = l.Zoning.Count - 1; i >= 0; i--)
            {
                var z = l.Zoning[i];
                if (z == null) continue;
                var quad = new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz };
                foreach (var p in prot)
                {
                    // Shrunk a little: a zoning quad that only shares the lot's edge does not rezone its cells.
                    if (!ObstacleCut.Overlaps(Shrink(quad, 0.5f), p)) continue;
                    l.Zoning.RemoveAt(i);
                    r.Zoning++;
                    break;
                }
            }
        }

        /// <summary>Ring minus obstacles as CS2-acceptable, hole-free pieces (≥ 2 m² each). touched = an obstacle overlapped.</summary>
        public static List<float2[]> AcceptablePieces(float2[] ring, IReadOnlyList<float2[]> obstacles, out bool touched)
        {
            var result = new List<float2[]>();
            var regions = ObstacleCut.Pieces(ring, obstacles, out int n);
            touched = n > 0;
            if (regions == null) return result;
            foreach (var reg in regions)
            {
                List<float2[]> pieces;
                try { pieces = SurfaceAcceptance.MakeAcceptable2D(reg, out _); }
                catch (Exception) { continue; }
                foreach (var p in pieces) if (p != null && p.Length >= 3 && Poly.Area(p) >= 2f) result.Add(Poly.EnsureCcw(p));
            }
            return result;
        }

        private static List<float2[]> HoleFreePieces(float2[] ring, IReadOnlyList<float2[]> obstacles, out bool touched)
        {
            var result = new List<float2[]>();
            var regions = ObstacleCut.Pieces(ring, obstacles, out int n);
            touched = n > 0;
            if (regions == null) return result;
            foreach (var reg in regions)
            {
                List<float2[]> pieces;
                try { pieces = reg.HasHoles ? Poly.CutHoles(reg, ObstacleCut.Corridor) : new List<float2[]> { reg.Outer }; }
                catch (Exception) { continue; }
                foreach (var p in pieces) if (p != null && p.Length >= 3 && Poly.Area(p) >= 2f) result.Add(Poly.EnsureCcw(p));
            }
            return result;
        }

        private static float2[] Shrink(float2[] quad, float by)
        {
            float2 c = Poly.Centroid(quad);
            var r = new float2[quad.Length];
            for (int i = 0; i < quad.Length; i++)
            {
                float2 d = quad[i] - c;
                float len = math.length(d);
                r[i] = len > by ? c + d * ((len - by) / len) : c;
            }
            return r;
        }

        private static float SegmentPolygonDistance(float2 a, float2 b, IReadOnlyList<float2> poly)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float2 p = poly[i], q = poly[(i + 1) % poly.Count];
                if (Poly.SegmentsIntersect(a, b, p, q)) return 0f;
                best = math.min(best, math.min(math.min(PointSeg(a, p, q), PointSeg(b, p, q)), math.min(PointSeg(p, a, b), PointSeg(q, a, b))));
            }
            return best;
        }

        private static float PointSeg(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float l2 = math.lengthsq(ab);
            float t = l2 > 1e-12f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distance(p, a + ab * t);
        }
    }
}

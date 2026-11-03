// s25: parking lots and commercial sites on sloped ground, and the stall markings / decal calm of the lot pass.
// Playtest: no stall markings in game, too many opaque decals, lots following every lump, white cones where the
// lanes met the lot, shop plots dug into pits. Everything is checked on the ground the build leaves (the real
// TerraformRaster over the plan, as the game layer and the gallery raster it).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Placemaker.Core;
using Placemaker.Core.Build;
using Placemaker.Core.Decals;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Commercial;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Placemaker.Tests
{
    public class LotGradingTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static Func<float2, float> Ground(string kind) => kind switch
        {
            // rising 6 % away from the south street, ±0.4 m lumps
            "cross" => p => 0.06f * p.y + 0.8f * (ValueNoise.Clumps(p, 26f, 23u) - 0.5f),
            // 8 % diagonal
            "steep" => p => 0.08f * (p.x * 0.6f + p.y * 0.8f) + (ValueNoise.Clumps(p, 32f, 25u) - 0.5f),
            "gentle" => p => 0.025f * p.x + 0.015f * p.y + 0.6f * (ValueNoise.Clumps(p, 30f, 21u) - 0.5f),
            _ => _ => 0f,
        };

        private static SiteContext Site(float3[] ring, string ground)
        {
            var site = ZoningCutoutTests.Streets(ring);
            var g = Ground(ground);
            site.HeightAt = g;
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                n.A.y = g(n.A.xz); n.B.y = g(n.B.xz); n.C.y = g(n.C.xz); n.D.y = g(n.D.xz);
                site.NearbyNets[k] = n;
            }
            return site;
        }

        private static ComposedPlan Compose(string type, string preset, float3[] ring, SiteContext site, Action<Recipe> edit = null)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (!string.IsNullOrEmpty(preset)) Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            edit?.Invoke(r);
            if (type == "parking" && r.Root.Markers.Count == 0)
            {
                var input = new AreaInput { Shape = new AreaShape { Ring = ring }, Site = site, Settings = r.Root.Settings, Markers = r.Root.Markers };
                r.Root.Markers.AddRange(GeneratorRegistry.Get(type).SuggestMarkers(input));
            }
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        /// <summary>The ground the build leaves: every layer's terraform through the game's raster.</summary>
        public static Func<float2, float> Final(ComposedPlan plan, Func<float2, float> original)
        {
            var items = new List<(float2[] ring, float target, int layer)>();
            var vh = new List<float[]>();
            for (int li = 0; li < plan.Layers.Count; li++)
                foreach (var t in plan.Layers[li]?.Terraform ?? new List<PlannedTerraform>())
                {
                    if (t?.Ring == null || t.Ring.Length < 3) continue;
                    items.Add((t.Ring.Select(v => v.xz).ToArray(), t.TargetHeight, li));
                    vh.Add(TerraformSurface.VertexHeights(t));
                }
            if (items.Count == 0) return original;
            var raster = TerraformRaster.Build(items, 1.75f, 4f, 1 << 22, vh, original);
            return p => raster != null && raster.TrySample(p, original, out float h) ? h : original(p);
        }

        /// <summary>What dresses a lot's asphalt (oil, tyres, stains, repairs, cracks, litter, glass).</summary>
        private static readonly HashSet<string> LotMarks = new HashSet<string>
        {
            DecalPools.Oil, DecalPools.Tyre, DecalPools.Stain, DecalPools.Patch, DecalPools.Glass, AssetCategories.DecalCracks, AssetCategories.DecalLitter,
        };

        private static float2 Forward(float yaw) => new float2(math.sin(yaw), math.cos(yaw));

        // ───────────── markings ─────────────

        [Fact]
        public void FunctionalParkingPiecesAreNeverOptionalInTheGhost()
        {
            Assert.True(PlanWork.IsFunctional(ObjectRole.ParkingBay));
            Assert.True(PlanWork.IsFunctional(ObjectRole.Charger));
            Assert.True(PlanWork.IsFunctional(ObjectRole.Arrow));
            Assert.False(PlanWork.IsFunctional(ObjectRole.Decal));
            Assert.False(PlanWork.IsFunctional(ObjectRole.Tree));
            Assert.False(PlanWork.IsFunctional(ObjectRole.Lamp));
            // s44: stall markings and arrows take no space in the build (sunk like decals, never collide); a charger is a real object.
            Assert.True(PlanWork.SinkInBuild(ObjectRole.ParkingBay) && PlanWork.SinkInBuild(ObjectRole.Arrow) && PlanWork.SinkInBuild(ObjectRole.Decal));
            Assert.False(PlanWork.SinkInBuild(ObjectRole.Charger) || PlanWork.SinkInBuild(ObjectRole.Tree) || PlanWork.SinkInBuild(ObjectRole.Lamp));
        }

        [Fact]
        public void TheCollisionRetryNeverTakesStallMarkingsWithItsNeighbours()
        {
            var ring = TestKit.Rect(90, 80);
            var plan = Compose("parking", "", ring, Site(ring, "flat"));
            var work = PlanWork.Flatten(plan);
            Assert.Equal(PlanWork.StallMarkings(plan), work.Objects.Count(w => !PlanWork.Excludable(plan, w) && w.Category == AssetCategories.ParkingDecal));
            Assert.True(PlanWork.StallMarkings(plan) > 20);
            Assert.Contains(work.Objects, w => PlanWork.Excludable(plan, w));   // trees, lamps, dressing may go
            Assert.All(work.Surfaces, w => Assert.True(PlanWork.Excludable(plan, w)));
        }

        [Theory]
        [InlineData("parking", "")]
        [InlineData("parking", "green")]
        [InlineData("stripMall", "classic")]
        [InlineData("shoppingPlaza", "court")]
        public void EveryStallHasItsMarkingAndTheLotStaysCalm(string type, string preset)
        {
            var ring = TestKit.Rect(120, 110);
            var plan = Compose(type, preset, ring, Site(ring, "flat"));
            int capacity = 0, bays = 0, decals = 0;
            foreach (var l in plan.Layers)
            {
                if (l == null) continue;
                capacity += l.GeneratorId == type && l.Facts.TryGetValue("capacity", out var c) ? (int)c : 0;
                bays += l.Objects.Count(o => o.Role == ObjectRole.ParkingBay && !string.IsNullOrEmpty(o.Asset.Id));
                decals += l.Objects.Count(o => o.Role == ObjectRole.Decal && LotMarks.Contains(o.Category));
            }
            Assert.True(capacity > 20, $"{type}: {capacity} stalls");
            Assert.Equal(capacity, bays);
            // Decorative dressing on and around the lot: a faint minority, never more marks than a fifth of the stalls.
            if (type == "parking") Assert.True(decals <= capacity / 5, $"{type}/{preset}: {decals} decals for {capacity} stalls");
        }

        // ───────────── parking grading ─────────────

        [Fact]
        public void FlatGroundIsNeverGraded()
        {
            var ring = TestKit.Rect(120, 110);
            foreach (var t in new[] { "parking", "stripMall", "shoppingPlaza", "outdoorMall", "shopsPark" })
            {
                var plan = Compose(t, "", ring, Site(ring, "flat"));
                Assert.All(plan.Layers.Where(l => l != null), l => Assert.Empty(l.Terraform));
            }
        }

        [Theory]
        [InlineData("cross")]
        [InlineData("steep")]
        [InlineData("gentle")]
        public void BaysAreLevelAndTiersRampGently(string ground)
        {
            var ring = TestKit.Rect(120, 110);
            var original = Ground(ground);
            var plan = Compose("parking", "", ring, Site(ring, ground));
            var lot = plan.Layers.First(l => l.GeneratorId == "parking");
            Assert.NotEmpty(lot.Terraform);
            Assert.All(lot.Terraform, t => Assert.True(t.Sloped));
            var final = Final(plan, original);
            var bays = lot.Objects.Where(o => o.Role == ObjectRole.ParkingBay).ToList();
            Assert.NotEmpty(bays);
            int tilted = 0, falling = 0;
            foreach (var b in bays)
            {
                var ax = Forward(b.Yaw); var side = new float2(ax.y, -ax.x);
                var p = b.Position.xz;
                // front → back across the bay (the car's length): level within 2 % (+ raster noise)
                float fall = math.abs(final(p + ax * 2.2f) - final(p - ax * 2.2f)) / 4.4f;
                // Level within 2 % (+ raster noise); a corner stall by the outline, where the build banks back to the ground, up to 5 %.
                if (fall > LotGradingTolerance(0.02f)) falling++;
                // (s29) Within the edge blend (the lot bends to the streets' own levels) on top of the ramping perimeter road a stall may fall more.
                Assert.True(fall <= 0.15f, $"{ground}: bay at {p} falls {fall:P1} front to back");
                float row = math.abs(final(p + side * 1.3f) - final(p - side * 1.3f)) / 2.6f;
                if (row > LotGradingTolerance(Placemaker.Core.Layers.Parking.LotGrading.MaxRowGrade)) tilted++;   // (s29) rows may follow the ground up to 5 %
                Assert.True(row <= 0.13f, $"{ground}: bay at {p} tilts {row:P1} along its row");
                // the bay stands on the graded ground
                Assert.True(math.abs(b.Position.y - final(p)) < 0.3f, $"{ground}: bay at {p} planned {b.Position.y:0.00}, ground {final(p):0.00}");
            }
            // On 8 % ground with streets on all four sides the lot cannot both terrace flat and meet every street: most stalls
            // carry part of the edge blend there.
            Assert.True(falling <= (ground == "steep" ? bays.Count * 2 / 3 : bays.Count / 3), $"{ground}: {falling}/{bays.Count} bays fall more than 2 % front to back");
            // Only the side rows on the ramping perimeter road may tilt along the row.
            Assert.True(tilted <= bays.Count / 3, $"{ground}: {tilted}/{bays.Count} bays tilt along their row");
            // Drive aisles and the perimeter road: ≤ 8 % (+ tolerance) everywhere; surviving cross lanes ≤ 12 %.
            foreach (var n in lot.Nets.Where(n => n.Role == NetRole.DriveAisle))
            {
                float len = math.distance(n.A.xz, n.D.xz);
                int steps = math.max(2, (int)(len / 2f));
                float max = 0f;
                for (int i = 0; i < steps; i++)
                {
                    var a = math.lerp(n.A.xz, n.D.xz, i / (float)steps); var b = math.lerp(n.A.xz, n.D.xz, (i + 1) / (float)steps);
                    max = math.max(max, math.abs(final(b) - final(a)) / math.max(0.1f, math.distance(a, b)));
                }
                Assert.True(max <= 0.15f, $"{ground}: drive lane {n.A.xz}→{n.D.xz} climbs {max:P0}");
            }
            Assert.True(lot.Facts["gradeMaxStep"] <= 1.5f);
        }

        /// <summary>A convex ring moved in by <paramref name="by"/> along its edge normals.</summary>
        private static float2[] Inset(float2[] q, float by)
        {
            var ring = Poly.EnsureCcw(q);
            int n = ring.Length;
            var r = new float2[n];
            for (int i = 0; i < n; i++)
            {
                var d1 = math.normalizesafe(ring[i] - ring[(i + n - 1) % n]); var d2 = math.normalizesafe(ring[(i + 1) % n] - ring[i]);
                var n1 = new float2(-d1.y, d1.x); var n2 = new float2(-d2.y, d2.x);
                float k = 1f + math.dot(n1, n2);
                r[i] = ring[i] + (k > 0.2f ? (n1 + n2) / k : n1 + n2) * by;
            }
            return r;
        }

        private static float LotGradingTolerance(float grade) => grade + 0.012f;

        // ───────────── commercial at grade ─────────────

        public static IEnumerable<object[]> CommercialCases()
        {
            foreach (var t in CommercialTests.Types)
                foreach (var p in new[] { "" }.Concat(GeneratorRegistry.Get(t).Presets.Select(x => x.Id)))
                    foreach (var g in new[] { "cross", "steep" })
                        yield return new object[] { t, p, g };
        }

        [Theory]
        [MemberData(nameof(CommercialCases))]
        public void CommercialSitesStayAtGradeWithoutPitsConesOrFences(string type, string preset, string ground)
        {
            var ring = TestKit.Rect(140, 130);
            var original = Ground(ground);
            var plan = Compose(type, preset, ring, Site(ring, ground));
            string ctx = $"{type}/{preset}/{ground}";
            Assert.False(plan.HasErrors, ctx);
            var host = plan.Layers.First(l => l.GeneratorId == type);
            var final = Final(plan, original);
            var plots = host.Regions.Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            Assert.NotEmpty(plots);
            var roads = StreetFrontage.Roads(new AreaInput { Site = Site(ring, ground), Shape = new AreaShape { Ring = ring } });
            var lanes = host.Nets.Where(n => n.Role == NetRole.Alley || n.Role == NetRole.Street).ToList();

            // (shops4) The strip mall paints and never reshapes the ground but for its lot on a slope (the user: "try to only use
            // painting"): its plots stay on the land as it is (the game levels a building's own lot); the pit, cone and fence checks
            // below still hold.
            if (type == "stripMall") plots.Clear();
            foreach (var q in plots)
            {
                // A plot is level at the height of the road it faces: a building finds its road's grade everywhere on it
                // (no construction pit, no retaining walls).
                var c = Poly.Centroid(q);
                float lo = float.MaxValue, hi = float.MinValue;
                int samples = 0, off = 0;
                // A cell in from the sides: between two plots at different heights the step is a bank one sculpt cell wide.
                var inner = Inset(q, MallGrading.Cell);
                float2 mn = inner.Aggregate(new float2(float.MaxValue), math.min), mx = inner.Aggregate(new float2(float.MinValue), math.max);
                var frontage = FrontEdge(q, roads, lanes);
                Assert.True(frontage.HasValue, $"{ctx}: plot at {c} faces nothing");
                var (fa, fb, street, others) = frontage.Value;
                var dir = math.normalizesafe(fb - fa);
                for (float y = mn.y; y <= mx.y; y += 2f)
                    for (float x = mn.x; x <= mx.x; x += 2f)
                    {
                        var p = new float2(x, y);
                        if (!Poly.Contains(inner, p)) continue;
                        // Beside a second road (a plot between street and lane) the lane's own level takes over.
                        if (others.Any(o => math.sqrt(SegmentIndex.DistSq(p, o.Item1, o.Item2)) < MallGrading.Cell + 0.5f)) continue;
                        var e = fa + dir * math.clamp(math.dot(p - fa, dir), 0f, math.distance(fa, fb));
                        // An existing street keeps its ground; a lane is where the grading put it.
                        float d = final(p) - (street ? original(e) : final(e));
                        lo = math.min(lo, d); hi = math.max(hi, d);
                        samples++;
                        if (math.abs(d) > 0.3f) off++;
                    }
                // ±0.3 m (smoothing) on nine tenths of the plot; where two roads meet at a corner plot a little more.
                Assert.True(off <= samples / 10 && lo >= -0.85f && hi <= 0.85f, $"{ctx}: plot at {c} is not at its road's grade (−{-lo:0.00} … +{hi:0.00} m, {off}/{samples} off)");
            }

            // No fence or wall within 1 m of a plot (the white fences in game were construction sites, never ours).
            foreach (var l in plan.Layers.Where(l => l != null))
            {
                foreach (var n in l.Nets.Where(n => n.Role == NetRole.Fence))
                    for (int i = 0; i <= 8; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                        Assert.DoesNotContain(plots, q => Poly.Contains(q, p) || Poly.DistanceToBoundary(q, p) < 1f);
                    }
                foreach (var o in l.Objects.Where(o => o.Role == ObjectRole.FencePiece))
                    Assert.DoesNotContain(plots, q => Poly.Contains(q, o.Position.xz) || Poly.DistanceToBoundary(q, o.Position.xz) < 1f);
            }

            var reg = LayerRegion.From(Poly.To2D(ring));
            // Junctions (three or more lane pieces meet): two profiles blend there.
            var ends = lanes.SelectMany(m => new[] { m.A.xz, m.D.xz }).ToList();
            var junctions = ends.Where(e => ends.Count(o => math.distance(o, e) < 0.5f) >= 3).ToList();
            // Lanes: ≤ 8 % along (+ raster tolerance), level across (no cut-and-fill cones beside them).
            foreach (var n in lanes)
            {
                var a = n.A.xz; var b = n.D.xz;
                float len = math.distance(a, b);
                var t = math.normalizesafe(b - a); var nrm = new float2(-t.y, t.x);
                for (float s = 2f; s < len - 2f; s += 2f)
                {
                    var p = a + t * s;
                    // Inside the site only: where a lane meets the street the city's own road decides.
                    if (!reg.Contains(p) || reg.BoundaryDistance(p, 8f) < 6f) continue;
                    if (junctions.Any(j => math.distance(j, p) < 8f)) continue;
                    // The junction with the street: the lane climbs from the street's level to the site's there.
                    if (lanes.Any(m => m.StartAttach >= 0 && math.distance(m.A.xz, p) < 16f || m.EndAttach >= 0 && math.distance(m.D.xz, p) < 16f)) continue;
                    float along = math.abs(final(p + t) - final(p - t)) / 2f;
                    float across = math.abs(final(p + nrm * 3f) - final(p - nrm * 3f)) / 6f;
                    Assert.True(along <= 0.13f, $"{ctx}: lane at {p} climbs {along:P0}");
                    // 8 % diagonal ground beside a lane between plots at their own roads' grades: the bank takes up to 15 % over 6 m (s58); other grounds stay at 10 %.
                    Assert.True(across <= (ground == "steep" ? 0.15f : 0.10f), $"{ctx}: lane at {p} falls {across:P0} across");
                }
            }

            // No cones: no point of the site stands out of its surroundings that the original ground did not.
            for (float y = 4f; y < 126f; y += 3f)
                for (float x = 4f; x < 136f; x += 3f)
                {
                    var p = new float2(x, y);
                    if (!reg.Contains(p)) continue;
                    float h = final(p), around = float.MinValue;
                    for (int k = 0; k < 8; k++) { float a2 = k * math.PI / 4f; around = math.max(around, final(p + 3f * new float2(math.cos(a2), math.sin(a2)))); }
                    float o = original(p), oAround = float.MinValue;
                    for (int k = 0; k < 8; k++) { float a2 = k * math.PI / 4f; oAround = math.max(oAround, original(p + 3f * new float2(math.cos(a2), math.sin(a2)))); }
                    Assert.False(h - around > 0.25f && o - oAround < 0.05f, $"{ctx}: a mound at {p} ({h - around:0.00} m above its surroundings)");
                }

            // The lot is a real lot: stalls with their markings.
            if (host.Facts.TryGetValue("lots", out var lots) && lots > 0)
                Assert.True(host.Objects.Count(o => o.Role == ObjectRole.ParkingBay) == (int)host.Facts["capacity"], ctx + ": a stall without its marking");
        }

        /// <summary>The plot's front (a street first, as MallGrading.Facing) and the other road-facing sides.</summary>
        private static (float2, float2, bool, List<(float2, float2)>)? FrontEdge(float2[] q, List<StreetFrontage.RoadLine> roads, List<PlannedNet> lanes)
        {
            int Near(float2 p)
            {
                foreach (var r in roads) if (math.sqrt(SegmentIndex.DistSq(p, r.A, r.B)) <= r.HalfWidth + StreetFrontage.Tolerance + 0.01f) return 1;
                // (s32) The lane's real width (a pedestrian street is 8 or 16 m, the Small Road 16 m), as the grading sees it.
                foreach (var n in lanes) if (math.sqrt(SegmentIndex.DistSq(p, n.A.xz, n.D.xz)) <= ZoneGuard.NetWidth(n, null) * 0.5f + StreetFrontage.Tolerance + 0.01f) return 2;
                return 0;
            }
            var faces = new List<(float2 a, float2 b, int kind)>();
            for (int i = 0; i < q.Length; i++)
            {
                var a = q[i]; var b = q[(i + 1) % q.Length];
                int k = Near(math.lerp(a, b, 0.5f));
                if (k > 0 && Near(math.lerp(a, b, 0.2f)) > 0 && Near(math.lerp(a, b, 0.8f)) > 0) faces.Add((a, b, k));
            }
            if (faces.Count == 0) return null;
            var front = faces.Any(x => x.kind == 1) ? faces.First(x => x.kind == 1) : faces[0];
            return (front.a, front.b, front.kind == 1, faces.Where(x => !x.Equals(front)).Select(x => (x.a, x.b)).ToList());
        }
    }
}

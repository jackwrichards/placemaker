// Turns the parking layout into an AreaPlan (world space): surfaces through SurfaceAcceptance, drive
// nets split at every junction, stall/charger/lamp/plant/arrow objects, the lot owner area,
// regions for add-ons, stats and economy facts.
//
// Conventions for the game layer (from ParkingLotTool, KruemelGames, GPL-3.0 — see docs/proposals/parking.md):
//  * ParkingBay object at the stall centre; +Z (yaw 0 = +Z) points from the aisle to the back of the
//    stall. PLT measured BayDecalFacesAisle=false: the vanilla decal's +Z must point away from the aisle.
//  * Nets are straight Béziers; endpoints of joining nets are identical (split at every junction).
//  * DriveConnector A→B is the driving direction for one-way entrances.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    internal static class PlanBuilder
    {
        private const string K = "Placemaker.Parking.";

        private sealed class Seg
        {
            public double2 A, B;
            public NetRole Role;
            public AssetRef Asset;
            public long StartAttach = -1, EndAttach = -1;
            public float StartT, EndT;
            /// <summary>(P2-1) A line the player drew: split where it crosses another.</summary>
            public bool Sketch;
        }

        public static AreaPlan Build(AreaInput input, ParkingSettings s, ParkingLayer gen, CancellationToken cancel, Action<Layout> inspect = null)
        {
            var plan = new AreaPlan { GeneratorId = ParkingLayer.LayerId };
            var site = input.Site ?? new SiteContext();
            Func<float2, float> h = site.HeightAt ?? (_ => 0f);
            Func<double2, float> ground = p => { try { return h(new float2((float)p.x, (float)p.y)); } catch (Exception) { return 0f; } };
            Func<double2, float> H = ground;

            // ── outline ──
            var world = new List<double2>(input.Shape.Ring.Length);
            foreach (var p in input.Shape.Ring)
            {
                if (!math.all(math.isfinite(p)))
                {
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = K + "Issue.BadShape" });
                    return plan;
                }
                world.Add(new double2(p.x, p.z));
            }
            world = PGeo.EnsureCcw(PGeo.Simplify(world, 1e-3, 1e-6));
            double area = world.Count >= 3 ? PGeo.Area(world) : 0;
            if (world.Count < 3 || !PGeo.IsSimple(world))
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = K + "Issue.BadShape" });
                return plan;
            }
            if (area < gen.MinArea)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = K + "Issue.TooSmall", Args = new object[] { Math.Round(area), gen.MinArea } });
                return plan;
            }
            if (area > gen.MaxArea)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = K + "Issue.TooLarge", Args = new object[] { Math.Round(area), gen.MaxArea } });
                return plan;
            }

            // ── markers ──
            // Only what the player (or the tool, from SuggestMarkers) placed is built: the plan never
            // invents entrances. Readiness blocks Build while there is no vehicle entrance.
            var entrances = new List<EntranceRequest>();
            var busStops = new List<double2>();
            var patches = new List<double2>();
            int dropped = 0;
            if (input.Markers != null)
                foreach (var m in input.Markers)
                {
                    if (!math.all(math.isfinite(m.Position))) continue;
                    var p = new double2(m.Position.x, m.Position.z);
                    if (m.Kind == ParkingIds.KindBusStop) { busStops.Add(p); continue; }
                    if (m.Kind == ParkingIds.KindZoningPatch) { patches.Add(p); continue; }
                    if (!EntranceKinds.TryParse(m.Kind, out var kind)) continue;
                    if (entrances.Count >= ParkingIds.MaxEntrances) { dropped++; continue; }
                    entrances.Add(new EntranceRequest { Kind = EntranceKinds.WithLegacyStyle(kind, s.Alleys), World = p });
                }
            if (dropped > 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.TooManyEntrances", Args = new object[] { ParkingIds.MaxEntrances, dropped } });
            bool vehicle = false;
            foreach (var e in entrances) if (EntranceKinds.ForCars(e.Kind)) vehicle = true;
            if (!vehicle)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.NoEntrance" });

            // ── zoning (frontage bands are cut from the lot before the layout) ──
            var roads = Roads.From(site);
            var openings = new List<ParkingZoning.Opening>();
            foreach (var e in entrances)
                openings.Add(new ParkingZoning.Opening
                {
                    P = e.World,
                    Width = e.Kind == EntranceKind.Footpath ? ParkingSettings.FootpathWidth : EntranceKinds.OneWay(e.Kind) ? ParkingSettings.OneWayWidth : s.Aisle,
                });
            var zoning = ParkingZoning.Plan(world, roads, s, openings, patches, gen.MinArea);
            plan.Issues.AddRange(zoning.Issues);
            cancel.ThrowIfCancellationRequested();

            var rq = new LayoutRequest { LotWorld = zoning.Lot, Settings = s, LeftHandTraffic = site.LeftHandTraffic };
            var reserved = input.Reserved;
            if (reserved != null && (reserved.Discs.Count > 0 || reserved.Corridors.Count > 0 || reserved.Polygons.Count > 0))
                rq.Blocked = (p, r) => reserved.Blocks(new float2((float)p.x, (float)p.y), (float)r);
            rq.Entrances.AddRange(entrances);
            rq.Roads = roads;
            if (site.ZoneGrids != null) rq.ZoneGrids.AddRange(site.ZoneGrids);
            // (P2-1) existing buildings inside or at the outline: the layout works round their lots
            foreach (var b in Placemaker.Core.Build.SiteSafety.Protected(site))
            {
                var poly = new double2[b.Length];
                for (int i = 0; i < b.Length; i++) poly[i] = new double2(b[i].x, b[i].y);
                rq.Buildings.Add(poly);
            }
            // (P2-1) hand edits: the picked edge (the last one placed wins) and the stall touches
            int edgeDropped = 0;
            if (input.Touches != null)
                foreach (var t in input.Touches)
                {
                    if (t.Kind == ParkingIds.TouchEdge)
                    {
                        int e = PickedEdge(world, new double2(t.Position.x, t.Position.z));
                        if (e < 0) { edgeDropped++; continue; }
                        rq.ChosenEdge = new[] { world[e], world[(e + 1) % world.Count] };
                    }
                    else if (t.Kind.StartsWith("stall.", StringComparison.Ordinal)) rq.Touches.Add(t);
                    else if (t.Kind == ParkingIds.LineAisle || t.Kind == ParkingIds.LineWalk)
                    {
                        var pts = Placemaker.Core.Tool.LineSketch.Decode(t.Arg);
                        if (pts.Length < 2) { edgeDropped++; continue; }
                        var w = new double2[pts.Length];
                        for (int i = 0; i < pts.Length; i++) w[i] = new double2(pts[i].x, pts[i].y);
                        rq.Sketches.Add((t.Kind == ParkingIds.LineAisle, w));
                    }
                }

            // ── layout ──
            cancel.ThrowIfCancellationRequested();
            // (P2-5) a round lot has its own layout (rings round a planted island); the rest works on it unchanged
            bool round = s.Look.Shape == LotShape.Round;
            List<AsphaltPiece> pieces = null; List<(List<double2> Ring, int Zone)> zones = null;
            var layout = round ? RoundLayout.Build(rq, cancel) : LayoutEngine.Build(rq, cancel, out pieces, out zones);
            var f = layout.Frame;
            if (layout.Lot.Count < 3)
            {
                Issues(plan, layout);
                return plan;
            }
            cancel.ThrowIfCancellationRequested();
            if (!round) layout.Surfaces = SurfaceCells.Build(layout.Lot, zones, pieces, s.Greenery == Greenery.None, cancel, layout.ZoneChain, layout.ZoneChainZones);
            cancel.ThrowIfCancellationRequested();

            var greens = new List<List<double2>>();
            foreach (var sp in layout.Surfaces) if (!sp.Asphalt) greens.Add(sp.Ring);
            // (perf) by the convex cells each ring was merged from, looked up by grid cell (was: every ring's box, then the
            // whole ring, for each of the planting probes every 0.3 m)
            var materials = new SurfaceIndex(layout.Surfaces);
            bool IsGreen(double2 p) => materials.IsGreen(p);
            bool Reserved(double2 local, double r) => rq.Blocked(f.ToWorld(local), r);

            // ── lanterns ──
            if (s.Lanterns)
            {
                Lanterns.Plan(layout, s.LanternSpacing, IsGreen);
                layout.Lanterns.RemoveAll(l =>
                {
                    foreach (var c in layout.Chargers) if (math.distance(c.Position, l.Position) < 1.5) return true;
                    return Reserved(l.Position, 0.8);
                });
            }
            cancel.ThrowIfCancellationRequested();

            // ── planting ──
            var species = new List<Species>();
            if (s.Greenery != Greenery.None && s.PlantingDensity > 0)
            {
                void Add(string id, bool tree)
                {
                    // rf-parking: spacing is the crown (trees) or the shrub's own size: plant bounds are the game's shrunk
                    // collision core, which put a 10 m crown every 4 m (same crown estimate as DressingWorld.CrownOf).
                    double spacing = tree ? 9 : 2.5, height = tree ? 10 : 1;
                    if (!string.IsNullOrEmpty(id) && site.Assets != null && site.Assets.TryGetValue(id, out var info))
                    {
                        var ext = info.BoundsMax - info.BoundsMin;
                        double sz = Math.Max(ext.x, ext.z);
                        if (ext.y > 0.1) height = ext.y;
                        if (tree) spacing = 2 * Math.Max(3.5, Math.Min(7, Math.Max(sz * 0.5, height * 0.28)));
                        else if (sz > 0.1) spacing = Math.Max(2, Math.Min(4, Math.Max(sz, height)));
                    }
                    species.Add(new Species { Tree = tree, Spacing = spacing, Height = height });
                }
                if (s.Trees.Length == 0) Add(ParkingAssets.DefaultTree, true); else foreach (var t in s.Trees) Add(t, true);
                if (s.Bushes.Length == 0) foreach (var b in ParkingAssets.DefaultBushes) Add(b, false); else foreach (var b in s.Bushes) Add(b, false);
                int density = (int)Math.Round(s.PlantingDensity);
                // stalls by 8 m cell (a stall is at most ~7 m across, so its own cell and the neighbours hold every hit)
                var bayGrid = new Dictionary<long, List<Bay>>();
                long Cell(double x, double y) => ((long)Math.Floor(x / 8) << 32) ^ (uint)(int)Math.Floor(y / 8);
                foreach (var b in layout.LiveBays)
                {
                    long k = Cell(b.Center.x, b.Center.y);
                    if (!bayGrid.TryGetValue(k, out var l)) bayGrid[k] = l = new List<Bay>();
                    l.Add(b);
                }
                // (perf) chargers by the same 8 m cell (every planting candidate asked every charger)
                var chargerGrid = new Dictionary<long, List<double2>>();
                foreach (var c in layout.Chargers)
                {
                    long k = Cell(c.Position.x, c.Position.y);
                    if (!chargerGrid.TryGetValue(k, out var l)) chargerGrid[k] = l = new List<double2>();
                    l.Add(c.Position);
                }
                bool PlantBlocked(double2 p)
                {
                    // never in a stall, even where a stall's corner reaches into the planting (cut corners of odd outlines)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            long k = Cell(p.x + dx * 8, p.y + dy * 8);
                            if (chargerGrid.Count > 0 && chargerGrid.TryGetValue(k, out var cs))
                                foreach (var c in cs) if (math.distance(c, p) < 1.5) return true;
                            if (bayGrid.TryGetValue(k, out var near))
                                foreach (var b in near) if (PGeo.Contains(b.Quad, p)) return true;
                        }
                    return Reserved(p, 1.0);
                }
                // rows first: the strips behind the stalls get evenly spaced trees and shrubs on their centre line
                var edges = new List<RowEdge>();
                foreach (var b in layout.LiveBays)
                {
                    if (b.Quad == null || b.Quad.Length < 4) continue;
                    var back = PGeo.Perp(b.RowDir);
                    if (PGeo.Dot(back, b.Axis) < 0) back = -back;
                    edges.Add(new RowEdge { Corner = b.Quad[2], RowDir = b.RowDir, Back = back });
                    edges.Add(new RowEdge { Corner = b.Quad[3], RowDir = b.RowDir, Back = back });
                    // the row-end caps beside a stall's open side: a line of shrubs down the cap (the probe from a side
                    // with a neighbouring stall hits its asphalt and is dropped)
                    foreach (var (s0, s1) in new[] { (b.Quad[0], b.Quad[3]), (b.Quad[1], b.Quad[2]) })
                    {
                        var mid = (s0 + s1) * 0.5;
                        var outward = PGeo.Norm(mid - b.Center);
                        var strip = PGeo.Perp(outward);
                        edges.Add(new RowEdge { Corner = mid, RowDir = strip, Back = outward, Cap = true });
                    }
                }
                // free strips no stall faces (a planted strip between two lanes): probed across from one long side
                foreach (var ring in greens)
                {
                    if (ring == null || ring.Count < 3) continue;
                    var axis = new double2(1, 0); double longest = 0;
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var d = ring[(i + 1) % ring.Count] - ring[i];
                        double l2 = PGeo.Dot(d, d);
                        if (l2 > longest) { longest = l2; axis = PGeo.Norm(d); }
                    }
                    var across = PGeo.Perp(axis);
                    double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                    foreach (var p in ring)
                    {
                        double x = PGeo.Dot(p, axis), y = PGeo.Dot(p, across);
                        x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    }
                    if (y1 - y0 > 4.5 || x1 - x0 < 12) continue;
                    for (double x = x0 + 0.75; x < x1; x += 1.5)
                        edges.Add(new RowEdge { Corner = axis * x + across * (y0 - 0.04), RowDir = axis, Back = across });
                }
                var rows = Vegetation.PlanRows(edges, species, density, input.Seed, layout.Lanterns, IsGreen, PlantBlocked);
                layout.Plants = Vegetation.Plan(greens, species, density, input.Seed, layout.Lanterns, PlantBlocked, rows);
                // (P2-5) angled rows leave small wedge islands at their ends: the loose fill put stray shrubs in them (rf-parking open
                // item). Only the planting lines plant shrubs there; a wedge gets a tree when it has room for one.
                if (s.StallAngleDeg != 90) layout.Plants.RemoveAll(p => !p.Tree && !p.Row);
                if (round) RoundLayout.RingTrees(layout, species, input.Seed);
            }
            cancel.ThrowIfCancellationRequested();

            // ── grading (s25): level tiers with ramps between them; everything below stands on the graded ground ──
            bool terraced = s.Look.Shape == LotShape.Terraced;
            if ((s.Grading || terraced) && !round)
            {
                var grading = LotGrading.Grade(layout, ground, cancel, zoning.Quads.Count == 0 ? world : null, roads, terraced);
                plan.Facts["gradeTiers"] = grading.Tiers;
                if (grading.Height != null)
                {
                    plan.Terraform.AddRange(grading.Pieces);
                    H = grading.Height;
                    var graded = grading.Height;
                    plan.HeightAfter = p => graded(new double2(p.x, p.y));
                    plan.Facts["gradeMaxCut"] = grading.MaxCut;
                    plan.Facts["gradeMaxFill"] = grading.MaxFill;
                    plan.Facts["gradeRowGrade"] = grading.RowGrade;
                    plan.Facts["gradeMaxStep"] = grading.MaxStep;
                    plan.Facts["gradeTies"] = grading.Ties;
                    plan.Facts["gradeMaxTie"] = grading.MaxTie;
                    if (grading.Walls.Count > 0) plan.Facts["terraceWalls"] = RetainingWalls(plan, layout, grading.Walls, H);
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainShaper.PermanentKey });
                    if (Math.Max(grading.MaxCut, grading.MaxFill) > 2.5f)
                        plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = K + "Issue.SteepGrade", Args = new object[] { Math.Round(Math.Max(grading.MaxCut, grading.MaxFill), 1) } });
                }
            }

            // (P2-5) the camp's corner and the works take their stalls; the islands' planting changes its kinds
            var (camp, works) = Equipment.Reserve(layout, s.Look, input.Seed);
            Islands.Before(layout, s.Look, s.Trees.Length == 0 ? 1 : s.Trees.Length);
            Emit(plan, layout, s, site, H, input, cancel, roads, zoning.MaxDepth);
            Islands.After(plan, layout, s.Look, H, input.Seed);
            if (s.Look.Drainage == Drainage.Barranca && s.Look.Paved) Islands.Ravine(plan, layout, H, input.Seed);
            Equipment.Place(plan, layout, s, H, IsGreen, input.Seed, camp, works);
            // (P2-5) kerbs, wheel stops and the designed ground (paint, wear, water)
            var kerbRuns = Kerbs.Find(layout, IsGreen);
            if (layout.Works != null) kerbRuns.RemoveAll(r => { var m = layout.Frame.ToLocal((r.A + r.B) * 0.5); return m.x >= layout.Works.MinX && m.x <= layout.Works.MaxX; });
            int kerbPieces = Kerbs.Emit(plan, kerbRuns, s.Look.Kerbs, H);
            int stops = s.Look.WheelStops ? Kerbs.WheelStops(plan, layout, H) : 0;
            plan.Facts["kerbPieces"] = kerbPieces;
            if (stops > 0) plan.Facts["wheelStops"] = stops;
            cancel.ThrowIfCancellationRequested();
            ParkingGround.Lay(plan, layout, s, kerbRuns, H, IsGreen, input.Seed, input.Settings);
            EmitBusStops(plan, busStops, world, roads, site, H);
            EmitZoning(plan, layout, zoning, s, H);
            EmitTouchTargets(plan, layout, world, rq.ChosenEdge, H);
            Touches.ReportDropped(plan, layout.TouchesDropped + edgeDropped);
            Issues(plan, layout);
            inspect?.Invoke(layout);
            return plan;
        }

        /// <summary>
        /// Bus stops on the street in front of the lot (PLT Tools/ParkingLotBusStops.cs put them on its
        /// internal zoning roads, which Placemaker does not build). The object stands on the lot-side
        /// sidewalk; yaw = travel direction of the lane next to the kerb (PLT BusStopSnap.TravelDirection).
        /// </summary>
        private static void EmitBusStops(AreaPlan plan, List<double2> busStops, List<double2> world, List<RoadLine> roads, SiteContext site, Func<double2, float> H)
        {
            int n = 0;
            foreach (var p in busStops)
            {
                n++;
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < world.Count; i++)
                {
                    double d = PGeo.DistPointSeg(p, world[i], world[(i + 1) % world.Count]);
                    if (d < bd) { bd = d; best = i; }
                }
                var inward = PGeo.Perp(PGeo.Norm(world[(best + 1) % world.Count] - world[best]));
                if (!Roads.ForEntrance(roads, p, inward, out var hit))
                {
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.BusStopNoRoad", Args = new object[] { n }, At = new float3((float)p.x, H(p), (float)p.y) });
                    continue;
                }
                var road = roads[hit.Road];
                var toLot = PGeo.Norm(p - hit.Point);
                if (PGeo.Len(p - hit.Point) < 1e-6) toLot = inward;
                var pos = hit.Point + toLot * Math.Max(0, road.Width / 2 - 1.5);
                var dir = hit.Direction;
                if (!road.OneWay)
                {
                    bool lotLeft = PGeo.Cross(dir, toLot) > 0;
                    // right-hand traffic drives on the right: the kerb lane on the left runs backwards
                    if (lotLeft != site.LeftHandTraffic) dir = -dir;
                }
                plan.Objects.Add(new PlannedObject
                {
                    Position = new float3((float)pos.x, H(pos), (float)pos.y), Yaw = Yaw(dir),
                    Role = ObjectRole.Marker, Asset = new AssetRef(ParkingAssets.BusStop),
                });
            }
        }

        /// <summary>
        /// Zoned quads (PlannedZoning). Last line of defence for "zoning never overlaps the lot": any
        /// quad touching a stall, a lane, a footpath or the lot owner area is dropped.
        /// </summary>
        private static void EmitZoning(AreaPlan plan, Layout layout, ZoningResult zoning, ParkingSettings s, Func<double2, float> H)
        {
            if (zoning.Quads.Count == 0) return;
            var f = layout.Frame;
            var blockers = new List<double2[]>();
            foreach (var b in layout.LiveBays)
            {
                var q = new double2[4];
                for (int i = 0; i < 4; i++) q[i] = f.ToWorld(b.Quad[i]);
                blockers.Add(q);
            }
            foreach (var l in layout.LiveLines)
                blockers.Add(PGeo.Corridor(f.ToWorld(l.A), f.ToWorld(l.B), l.Width / 2));
            // entrance pieces outside the lot (to the kerb, alleys, footpaths to the sidewalk)
            foreach (var net in plan.Nets)
                blockers.Add(PGeo.Corridor(new double2(net.A.x, net.A.z), new double2(net.D.x, net.D.z), net.Role == NetRole.Pathway ? 1.0 : 3.5));
            var lotWorld = new List<double2>();
            foreach (var p in layout.Lot) lotWorld.Add(f.ToWorld(p));
            int cells = 0, droppedQuads = 0;
            foreach (var zq in zoning.Quads)
            {
                // shrink 5 cm so a quad sharing an edge with the lot does not count as overlapping
                var c = (zq.A + zq.B + zq.C + zq.D) * 0.25;
                var r = zq.Ring;
                for (int i = 0; i < 4; i++) r[i] = r[i] + PGeo.Norm(c - r[i]) * 0.05;
                bool bad = false;
                foreach (var bq in blockers) if (PGeo.ConvexOverlap(r, bq, 1e-4)) { bad = true; break; }
                if (!bad) foreach (var p in r) if (PGeo.Contains(lotWorld, p)) { bad = true; break; }
                if (bad) { droppedQuads++; continue; }
                plan.Zoning.Add(new PlannedZoning
                {
                    A = P3(zq.A, H), B = P3(zq.B, H), C = P3(zq.C, H), D = P3(zq.D, H),
                    ZoneId = s.ZoneType ?? "",
                });
                cells += (int)Math.Floor(PGeo.Len(zq.B - zq.A) / ParkingSettings.ZoneCell + 1e-6) * (int)Math.Floor(PGeo.Len(zq.D - zq.A) / ParkingSettings.ZoneCell + 1e-6);
            }
            if (droppedQuads > 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = K + "Issue.ZoningTrimmed", Args = new object[] { droppedQuads } });
            plan.Stats.Add(new PlanStat(K + "Stat.ZoneCells", cells, "count"));
            plan.Facts["zoning.cells"] = cells;
        }

        private static float KerbH(List<(double2 at, float h)> kerbs, double2 p, Func<double2, float> H)
        {
            foreach (var k in kerbs) if (PGeo.Len(k.at - p) < 0.01) return k.h;
            return H(p);
        }

        /// <summary>
        /// (s58) A connector that ends at the kerb of an existing street (the game joins the dead end by LocalConnect) leaves the street at the
        /// street's own height: the strip between the lot edge and the kerb is graded as a sloped quad from the lot's height to the street's
        /// (the raw ground there made a 0.4-0.7 m step next to a street on a fill or in a cut), and the connector's kerb end is that height.
        /// </summary>
        private static void KerbApron(AreaPlan plan, double2 from, double2 kerb, double half, Func<double2, float> H, Func<double2, float> ground, double2 axis, double streetH, List<(double2 at, float h)> kerbs)
        {
            if (double.IsNaN(streetH) || double.IsInfinity(streetH)) return;
            var d = kerb - from; double len = PGeo.Len(d);
            if (len < 0.3) return;
            d = d / len;
            var n = new double2(-d.y, d.x);
            float hs = (float)streetH, h0 = H(from);
            kerbs.Add((kerb, hs));
            if (Math.Abs(hs - h0) < 0.25f && Math.Abs(hs - ground(axis)) < 0.25f) return;   // already (nearly) flush: nothing to grade
            float3 V(double2 p, float h) => new float3((float)p.x, h, (float)p.y);
            void Piece(float3[] ring, bool apron)
            {
                double signed = 0;
                for (int v = 0, u = ring.Length - 1; v < ring.Length; u = v++) signed += (double)ring[u].x * ring[v].z - (double)ring[v].x * ring[u].z;
                if (signed < 0) Array.Reverse(ring);
                var c = Poly.Centroid(Poly.To2D(ring));
                float th = global::Placemaker.Core.Build.TerraformSurface.At(ring, c);
                plan.Terraform.Add(new PlannedTerraform { Ring = ring, Kind = th >= H(new double2(c.x, c.y)) ? TerraformKind.Raise : TerraformKind.Lower, TargetHeight = th, Permanent = true, Sloped = true, Apron = apron });
            }
            Piece(new[] { V(from - n * half, h0), V(kerb - n * half, hs), V(kerb + n * half, hs), V(from + n * half, h0) }, true);
            // Level under the kerb and the first metres of the street, so the raster's bank falls under the road, not in the strip.
            Piece(new[] { V(kerb - n * half, hs), V(kerb + d * 3.0 - n * half, hs), V(kerb + d * 3.0 + n * half, hs), V(kerb + n * half, hs) }, false);
            bool has = false;
            foreach (var i in plan.Issues) if (i.Key == Layers.Common.TerrainShaper.PermanentKey) { has = true; break; }
            if (!has) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainShaper.PermanentKey });
        }

        /// <summary>(P2-1) The outline edge a "grid.edge" touch was made on: within 3 m of the edge and not past its ends; -1 = none.</summary>
        internal static int PickedEdge(List<double2> world, double2 p)
        {
            int best = -1; double bestD = 3;
            for (int i = 0; i < world.Count; i++)
            {
                var a = world[i]; var b = world[(i + 1) % world.Count];
                if (PGeo.Len(b - a) < 2) continue;
                var q = PGeo.ClosestOnSeg(p, a, b, out double u);
                if (u < 0.02 || u > 0.98) continue;
                double d = PGeo.Len(q - p);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// (P2-1) What the hand-edit tool can touch: every stall (also those made into islands, to take them back) with its state,
        /// and every outline edge long enough to line the rows up with.
        /// </summary>
        private static void EmitTouchTargets(AreaPlan plan, Layout layout, List<double2> world, double2[] chosen, Func<double2, float> H)
        {
            var f = layout.Frame;
            foreach (var b in layout.Bays)
            {
                if (b.Quad == null || b.Quad.Length < 4 || (b.Removed && !b.Island)) continue;
                var outline = new float2[4];
                for (int k = 0; k < 4; k++) { var w = f.ToWorld(b.Quad[k]); outline[k] = new float2((float)w.x, (float)w.y); }
                var c = f.ToWorld(b.Center);
                string state = b.Island ? "island" : b.Role == BayRole.Blank ? "blank" : b.Role == BayRole.Disabled ? "accessible"
                    : b.Role == BayRole.Electric ? "ev" : "normal";
                plan.TouchTargets.Add(new TouchTarget
                {
                    Group = "stall", Center = new float3((float)c.x, H(c), (float)c.y), Outline = outline, State = state,
                    Touched = b.TouchedBy ?? "", TouchAt = b.TouchAt,
                });
            }
            for (int i = 0; i < world.Count; i++)
            {
                var a = world[i]; var b = world[(i + 1) % world.Count];
                double len = PGeo.Len(b - a);
                if (len < 2) continue;
                var d = (b - a) / len; var n = PGeo.Perp(d);   // the outline is CCW: n points inward
                var mid = (a + b) * 0.5;
                bool picked = chosen != null && PGeo.Len(chosen[0] - a) < 1e-6 && PGeo.Len(chosen[1] - b) < 1e-6;
                plan.TouchTargets.Add(new TouchTarget
                {
                    Group = "grid", Center = new float3((float)mid.x, H(mid), (float)mid.y), State = picked ? "picked" : "edge",
                    Outline = new[] { F2(a), F2(b), F2(b + n * 1.5), F2(a + n * 1.5) },
                    Touched = picked ? ParkingIds.TouchEdge : "", TouchAt = new float3((float)mid.x, H(mid), (float)mid.y),
                });
            }
        }

        /// <summary>
        /// (P2-5) Terraced: a dark masonry retaining wall along every tier step (the game's garden walls in courses of 0.9 m), its foot
        /// on the lower tier and its top flush with the upper one. Returns the pieces made.
        /// </summary>
        private static int RetainingWalls(AreaPlan plan, Layout layout, List<(double Y, double X0, double X1, double Low, double High)> walls, Func<double2, float> H)
        {
            var f = layout.Frame;
            int n = 0;
            foreach (var w in walls)
            {
                // a parapet 0.25 m above the upper tier, so the wall reads as a wall and not as a buried ledge
                // Low/High: the tiers before and after the wall line (along +y); the wall stands on whichever is lower
                double lowH = Math.Min(w.Low, w.High), step = Math.Abs(w.High - w.Low), rise = step + 0.25, side = w.Low < w.High ? -1 : 1;
                int courses = Math.Max(1, (int)Math.Ceiling(rise / Kerbs.WallH));
                float sink = (float)(courses * Kerbs.WallH - rise);
                var dir = f.DirToWorld(new double2(1, 0));
                float yaw = (float)Math.Atan2(dir.x, dir.y);
                // stand a metre in front of the step on the lower tier's ground: the terrain raster smears the step over a cell or
                // two, and a wall at the step itself was buried to its top (game runner 1031-133315)
                double y = w.Y + side * 1.0;
                for (double x = w.X0 + 1.5; x + 1.5 <= w.X1 + 1e-6; x += 3)
                {
                    var p = f.ToWorld(new double2(x, y));
                    float g = H(p);
                    for (int c = 0; c < courses; c++)
                    {
                        plan.Objects.Add(new PlannedObject
                        {
                            Position = new float3((float)p.x, g, (float)p.y), Yaw = yaw, Role = ObjectRole.Furniture,
                            Asset = new AssetRef("EU_Wall08", "EU_Wall01"), Category = AssetCategories.RuinsWall, Variation = (uint)n,
                            Elevation = (float)(lowH - g) + c * Kerbs.WallH - sink,
                        });
                        n++;
                    }
                }
            }
            return n;
        }

        private static float2 F2(double2 p) => new float2((float)p.x, (float)p.y);

        private static float3 P3(double2 p, Func<double2, float> H) => new float3((float)p.x, H(p), (float)p.y);

        private static void Issues(AreaPlan plan, Layout layout)
        {
            foreach (var i in layout.Issues)
            {
                var pi = new PlanIssue { Severity = i.Severity, Key = i.Key, Args = i.Args ?? Array.Empty<object>() };
                if (i.At.HasValue)
                {
                    var w = layout.Frame.ToWorld(i.At.Value);
                    pi.At = new float3((float)w.x, 0, (float)w.y);
                }
                plan.Issues.Add(pi);
            }
        }

        /// <summary>
        /// (s47) Cut-back (m) along a road of half width hi (ray di away from the node) where it meets one of half width hj (ray dj):
        /// the game cuts each edge back to where it crosses the neighbour's — (hj + hi cos φ) / sin φ, φ the angle between the rays
        /// (a T is hj). A continuation (≥ 165°) needs none. Game.Net.ValidationHelpers.ValidateEdge: an edge shorter than its two
        /// cut-backs folds (InvalidShape).
        /// </summary>
        internal static double Cutback(double2 di, double hi, double2 dj, double hj)
        {
            double cos = Math.Max(-1, Math.Min(1, PGeo.Dot(di, dj)));
            if (cos < -0.9659) return 0;
            double sin = Math.Sqrt(Math.Max(1e-6, 1 - cos * cos));
            return Math.Max(0, (hj + hi * cos) / sin);
        }

        /// <summary>
        /// (s47) An alley entrance: a stub from the road (centre at <paramref name="road"/>) to the lot edge <paramref name="e"/>, then the
        /// connector to the junction <paramref name="j"/>. The stub needs its T cut-back (the road's half width) plus the cut-back of the
        /// turn into the connector; a lot edge on the road's kerb leaves it exactly the road's half width. The stub is carried on into the
        /// lot (<paramref name="inner"/>, up to 8 m) until both fit; false where a stall is in the way or the connector would be left
        /// too short.
        /// </summary>
        private static bool AlleyStub(double2 e, double2 j, double2 road, double roadHalf, double stubHalf, double connHalf, Layout layout, Frame f, out double2 inner)
        {
            inner = e;
            var toRoad = PGeo.Norm(road - e);
            for (double x = 0; x <= 8.0 + 1e-9; x += 0.25)
            {
                var p = e - toRoad * x;
                var toJ = PGeo.Norm(j - p);
                double turn = Cutback(toRoad, stubHalf, toJ, connHalf), margin = turn > 0.01 ? 0.3 : 0;   // a straight stub (a continuation) keeps its length
                double stubNeed = roadHalf + turn + margin;
                double connNeed = Cutback(toJ, connHalf, toRoad, stubHalf) + connHalf + margin;
                if (PGeo.Len(road - p) < stubNeed - 0.01 || PGeo.Len(j - p) < connNeed - 0.01) continue;
                if (x > 0)
                {
                    var a = PGeo.Corridor(p, e, stubHalf); var b = PGeo.Corridor(p, j, connHalf);
                    bool clash = false;
                    foreach (var bay in layout.LiveBays)
                    {
                        var q = new double2[4];
                        for (int i = 0; i < 4; i++) q[i] = f.ToWorld(bay.Quad[i]);
                        if (PGeo.ConvexOverlap(a, q, 1e-4) || PGeo.ConvexOverlap(b, q, 1e-4)) { clash = true; break; }
                    }
                    if (clash) return false;
                }
                inner = p;
                return true;
            }
            return false;
        }

        private static float Yaw(double2 worldDir) => (float)Math.Atan2(worldDir.x, worldDir.y);

        private static void Emit(AreaPlan plan, Layout layout, ParkingSettings s, SiteContext site, Func<double2, float> H, AreaInput input, CancellationToken cancel,
            List<RoadLine> roads, double zoningDepth)
        {
            var f = layout.Frame;
            Func<float2, float> h2 = p => H(new double2(p.x, p.y));
            float3 P3(double2 local) { var w = f.ToWorld(local); return new float3((float)w.x, H(w), (float)w.y); }
            float2[] W2(List<double2> ring)
            {
                var r = new float2[ring.Count];
                for (int i = 0; i < r.Length; i++) { var w = f.ToWorld(ring[i]); r[i] = new float2((float)w.x, (float)w.y); }
                return r;
            }

            // ── surfaces & regions ──
            var asphaltAsset = ParkingAssets.Pick(s.AsphaltAsset, ParkingAssets.Asphalt, ParkingAssets.AsphaltEai, ParkingAssets.AsphaltVanilla);
            var greenAsset = ParkingAssets.Pick(s.GreenAsset, ParkingAssets.Grass);
            // (P2-5) gravel and swale islands are soil, not lawn; the lot's paving its own material
            var soil = Islands.SurfaceFor(s.Look.Islands);
            if (soil != null) greenAsset = new AssetRef(soil, ParkingAssets.Grass);
            switch (s.Look.Paving)
            {
                case Paving.Concrete: asphaltAsset = new AssetRef("Concrete Surface 01", asphaltAsset.Id); break;
                case Paving.Pavers: asphaltAsset = new AssetRef("Tiles Surface 01", asphaltAsset.Id); break;
                case Paving.Gravel: case Paving.Dirt: asphaltAsset = new AssetRef(Islands.Soil, asphaltAsset.Id); break;
            }
            double covered = 0;
            int lotRegion = 0, greenRegion = 0;
            foreach (var sp in layout.Surfaces)
            {
                cancel.ThrowIfCancellationRequested();
                var rings = Acceptable(sp, W2, h2, cancel);
                foreach (var ring in rings)
                {
                    plan.Surfaces.Add(new PlannedSurface
                    {
                        Ring = ring, Role = sp.Asphalt ? SurfaceRole.Asphalt : SurfaceRole.Grass,
                        Asset = sp.Asphalt ? asphaltAsset : greenAsset,
                    });
                    covered += Pricing.PolygonArea(ring);
                    plan.Regions.Add(new PlannedRegion
                    {
                        Ring = ring,
                        Tags = new[] { sp.Asphalt ? RegionTags.Lot : RegionTags.Green },
                        Key = sp.Asphalt ? "lot:" + lotRegion++ : "green:" + greenRegion++,
                    });
                }
            }
            double uncovered = Math.Max(0, layout.LotArea - covered);
            if (layout.LotArea > 0 && uncovered / layout.LotArea > 0.005)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.SurfaceGaps", Args = new object[] { Math.Round(uncovered) } });

            // ── lot owner area ──
            var lotRings = SurfaceAcceptance.MakeAcceptable(W2(layout.Lot), h2);
            if (lotRings.Count > 0)
            {
                var best = lotRings[0];
                foreach (var r in lotRings) if (Pricing.PolygonArea(r) > Pricing.PolygonArea(best)) best = r;
                plan.Areas.Add(new PlannedArea { Ring = best, Role = AreaRole.Lot, Asset = new AssetRef("") });
            }
            else plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.LotOwnerRejected" });

            // ── nets ──
            var segs = new List<Seg>();
            // an entrance on a zoned edge sits behind the band: look that much further for its street
            double search = Roads.SearchDistance + zoningDepth;
            foreach (var l in layout.LiveLines)
            {
                switch (l.Kind)
                {
                    case LineKind.Ring:
                    case LineKind.Aisle:
                    case LineKind.Cross:
                        segs.Add(new Seg { A = f.ToWorld(l.A), B = f.ToWorld(l.B), Role = NetRole.DriveAisle, Asset = ParkingAssets.Drive(l.Width) });
                        break;
                    case LineKind.Footpath:
                        segs.Add(new Seg { A = f.ToWorld(l.A), B = f.ToWorld(l.B), Role = NetRole.Pathway, Asset = new AssetRef(ParkingAssets.PedestrianPath) });
                        break;
                    // (P2-1) lines the player drew: split where they cross the other lines (a junction, not an overlap)
                    case LineKind.Sketch:
                        segs.Add(new Seg { A = f.ToWorld(l.A), B = f.ToWorld(l.B), Role = NetRole.DriveAisle, Asset = ParkingAssets.Drive(l.Width), Sketch = true });
                        break;
                    case LineKind.Walk:
                        segs.Add(new Seg { A = f.ToWorld(l.A), B = f.ToWorld(l.B), Role = NetRole.Pathway, Asset = new AssetRef(ParkingAssets.PedestrianPath), Sketch = true });
                        break;
                }
            }
            var kerbs = new List<(double2 at, float h)>();   // s58: dead-end connector ends take the street's own height
            for (int ei = 0; ei < layout.Entrances.Count; ei++)
            {
                var e = layout.Entrances[ei];
                var E = f.ToWorld(e.Point); var J = f.ToWorld(e.Junction);
                var inward = f.DirToWorld(e.Inward);
                if (e.Kind == EntranceKind.Footpath)
                {
                    // PLT Fussweg: the inner piece is the layout's footpath line (emitted above); the
                    // outer piece runs to the street and is attached to it (see parking.md).
                    if (!e.Connected || e.FootLine < 0 || layout.Lines[e.FootLine].Removed) continue;
                    if (Roads.ForEntrance(roads, E, inward, out var fh, search))
                    {
                        if (PGeo.Len(fh.Point - E) > 0.5)
                            segs.Add(new Seg
                            {
                                A = E, B = fh.Point, Role = NetRole.Pathway, Asset = new AssetRef(ParkingAssets.PedestrianPath),
                                EndAttach = roads[fh.Road].Id, EndT = (float)fh.T,
                            });
                    }
                    else
                        plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.EntranceNoRoad", Args = new object[] { ei + 1 }, At = new float3((float)E.x, H(E), (float)E.y) });
                    continue;
                }
                if (!e.Connected || e.ConnectorLine < 0 || layout.Lines[e.ConnectorLine].Removed) continue;
                bool exit = EntranceKinds.Outbound(e.Kind);
                bool oneWay = EntranceKinds.OneWay(e.Kind);
                var asset = !oneWay ? ParkingAssets.Drive(e.Width) : ParkingAssets.OneWay(e.Width);
                double2 outer = E;
                bool hasRoad = Roads.ForEntrance(roads, E, inward, out var hit, search);
                if (hasRoad && EntranceKinds.IsAlley(e.Kind))
                {
                    var road = roads[hit.Road];
                    var alley = new Seg { Role = NetRole.Alley, Asset = oneWay ? new AssetRef(ParkingAssets.AlleyOneWay, ParkingAssets.Alley) : new AssetRef(ParkingAssets.Alley) };
                    // (s47) The stub must be long enough for its two nodes (AlleyStub): a lot edge on a lane's kerb leaves the stub as
                    // long as the lane's half width, and the turn into the lot's connector then folds both (InvalidShape). Where it
                    // cannot be made long enough (a stall in the way) there is no stub: the connector is a dead end at the kerb.
                    if (AlleyStub(E, J, hit.Point, road.Width / 2, oneWay ? 2.5 : 4.0, e.Width / 2, layout, f, out var inner))
                    {
                        if (exit) { alley.A = inner; alley.B = hit.Point; alley.EndAttach = road.Id; alley.EndT = (float)hit.T; }
                        else { alley.A = hit.Point; alley.B = inner; alley.StartAttach = road.Id; alley.StartT = (float)hit.T; }
                        if (PGeo.Len(hit.Point - inner) > 0.5) segs.Add(alley);
                        outer = inner;
                    }
                }
                else if (hasRoad)
                {
                    // LocalConnect needs a dead end at the kerb: run the connector to the road edge (PLT NetDefinitions).
                    double gap = PGeo.Len(hit.Point - E) - roads[hit.Road].Width / 2;
                    if (gap > 0.3)
                    {
                        outer = E + PGeo.Norm(hit.Point - E) * gap;
                        KerbApron(plan, E, outer, e.Width * 0.5 + 2.0, H, q => { try { return (site?.HeightAt ?? (_ => 0f))(new float2((float)q.x, (float)q.y)); } catch (Exception) { return 0f; } }, hit.Point, Roads.HeightAt(roads, hit), kerbs);
                    }
                    else if (gap > -0.5)
                    {
                        double hk = Roads.HeightAt(roads, hit);
                        if (!double.IsNaN(hk) && !double.IsInfinity(hk)) kerbs.Add((outer, (float)hk));   // the lot edge is on the kerb: its end is the street's height
                    }
                }
                else
                {
                    var at = new float3((float)E.x, H(E), (float)E.y);
                    plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = K + "Issue.EntranceNoRoad", Args = new object[] { ei + 1 }, At = at });
                }
                segs.Add(exit
                    ? new Seg { A = J, B = outer, Role = NetRole.DriveConnector, Asset = asset }
                    : new Seg { A = outer, B = J, Role = NetRole.DriveConnector, Asset = asset });

                if (oneWay)
                {
                    var mid = (E + J) * 0.5;
                    var dir = exit ? -inward : inward;
                    plan.Objects.Add(new PlannedObject
                    {
                        Position = new float3((float)mid.x, H(mid), (float)mid.y), Yaw = Yaw(dir), Role = ObjectRole.Arrow,
                        Asset = new AssetRef(ParkingAssets.Arrows[0], ParkingAssets.Arrows[1], ParkingAssets.Arrows[2]),
                    });
                }
            }
            foreach (var sg in SplitAtJunctions(segs))
            {
                var a = new float3((float)sg.A.x, KerbH(kerbs, sg.A, H), (float)sg.A.y);
                var d = new float3((float)sg.B.x, KerbH(kerbs, sg.B, H), (float)sg.B.y);
                plan.Nets.Add(new PlannedNet
                {
                    A = a, B = math.lerp(a, d, 1f / 3f), C = math.lerp(a, d, 2f / 3f), D = d,
                    Role = sg.Role, Asset = sg.Asset,
                    StartAttach = sg.StartAttach, StartAttachT = sg.StartT, EndAttach = sg.EndAttach, EndAttachT = sg.EndT,
                });
            }

            // ── objects ──
            int bays = 0, ev = 0, acc = 0;
            foreach (var b in layout.LiveBays)
            {
                if (b.Role == BayRole.Blank) continue;   // (P2-1) an empty paved space: no marking, no parking lane
                AssetRef asset;
                switch (b.Role)
                {
                    case BayRole.Disabled: asset = new AssetRef(ParkingAssets.DisabledDecal, ParkingAssets.Decal); acc++; break;
                    case BayRole.Electric: asset = new AssetRef(ParkingAssets.ElectricDecal, ParkingAssets.Decal); ev++; break;
                    default: asset = new AssetRef(ParkingAssets.Decal); break;
                }
                // (P2-1) markings off: every stall the invisible bay (the lane stays; the painted one when it is missing)
                if (!s.Markings) asset = new AssetRef(ParkingAssets.InvisibleBay, asset.Id);
                // (P2-5) a country's own lines are painted by ParkingGround: plain stalls keep only their parking lane
                else if (ParkingGround.OwnLines(s) && b.Role == BayRole.Normal) asset = new AssetRef(ParkingAssets.InvisibleBay, asset.Id);
                bays++;
                plan.Objects.Add(new PlannedObject
                {
                    Position = P3(b.Center), Yaw = Yaw(f.DirToWorld(b.Axis)), Role = ObjectRole.ParkingBay, Asset = asset,
                    Scale = b.Role == BayRole.Disabled ? 1f : ParkingMarkings.Scale(false, b.Width, b.Length), Variation = (uint)bays,
                });
            }
            var chargerAsset = ParkingAssets.Pick(s.ChargerAsset, ParkingAssets.Charger);
            // a charger points at its EV stalls: without the markings there is nothing for it to belong to (as PLT)
            if (s.Markings)
                foreach (var c in layout.Chargers)
                    plan.Objects.Add(new PlannedObject { Position = P3(c.Position), Yaw = Yaw(f.DirToWorld(c.Facing)), Role = ObjectRole.Charger, Asset = chargerAsset });
            foreach (var l in layout.Lanterns)
            {
                var asset = !string.IsNullOrEmpty(s.LanternAsset) ? new AssetRef(s.LanternAsset, ParkingAssets.LampSingle)
                    : l.Double ? new AssetRef(ParkingAssets.LampDouble, ParkingAssets.LampSingle) : new AssetRef(ParkingAssets.LampSingle);
                // single heads face along Facing; double heads light both sides, so they stand across it (PLT LaternenKatalog.Vorwaerts)
                var face = l.Double ? PGeo.Perp(l.Facing) : l.Facing;
                plan.Objects.Add(new PlannedObject { Position = P3(l.Position), Yaw = Yaw(f.DirToWorld(face)), Role = ObjectRole.Lamp, Asset = asset });
            }
            int trees = 0;
            var speciesAssets = SpeciesAssets(s);
            foreach (var p in layout.Plants)
            {
                // PLT default: no aging, ages "young/medium" (mask 6) picked per seed
                plan.Objects.Add(new PlannedObject
                {
                    Position = P3(p.Position), Yaw = (float)(Vegetation.Unit(Vegetation.Hash(p.Seed + 5)) * 2 * Math.PI),
                    Role = p.Tree ? ObjectRole.Tree : ObjectRole.Bush,
                    Asset = p.Species < speciesAssets.Count ? speciesAssets[p.Species] : new AssetRef(""),
                    // rf-parking: the trees of a planting line share one growth stage (adult), so the line reads as planted
                    Age = !p.Tree ? -1 : p.Row ? 2 : 1 + (int)(p.Seed % 2), Variation = p.Seed,
                });
                if (p.Tree) trees++;
            }

            // ── stats & facts ──
            plan.Stats.Add(new PlanStat(K + "Stat.Stalls", bays, "count"));
            plan.Stats.Add(new PlanStat(K + "Stat.EvStalls", ev, "count"));
            plan.Stats.Add(new PlanStat(K + "Stat.AccessibleStalls", acc, "count"));
            plan.Stats.Add(new PlanStat(K + "Stat.Area", Math.Round(layout.LotArea), "m2"));
            plan.Stats.Add(new PlanStat(K + "Stat.AisleLength", Math.Round(layout.AisleLength), "m"));
            plan.Stats.Add(new PlanStat(K + "Stat.Trees", trees, "count"));
            plan.Facts["capacity"] = bays;
            plan.Facts["evCapacity"] = ev;
            plan.Facts["disabledCapacity"] = acc;
            // Same key as Game/Economy/ParkingEconomySystem.FeeFact. Note: the build receipt sums facts
            // over layers, so a recipe with two parking steps would add their fees (docs/proposals/parking.md).
            plan.Facts["parking.fee"] = s.Fee;
            if (layout.SketchCut > 0 || layout.SketchRows > 0) { plan.Facts["sketch.cut"] = layout.SketchCut; plan.Facts["sketch.rows"] = layout.SketchRows; }
            layout.UncoveredArea = uncovered;
        }

        /// <summary>
        /// Rings CS2 keeps for one merged surface. When the game model trims the big ring (it splits
        /// and drops pieces it cannot rescue), the same cells are regrouped into smaller rings and the
        /// variant covering more area wins; cells are the last resort. A ring the build itself would refuse (the game's
        /// area shape rules, <see cref="SurfaceRules"/>: it left the whole ring out, bare ground in the lot) counts as
        /// lost, so its cells are regrouped too.
        /// </summary>
        private static List<float3[]> Acceptable(SurfacePoly sp, Func<List<double2>, float2[]> w2, Func<float2, float> h2, CancellationToken cancel)
        {
            double want = PGeo.Area(sp.Ring);
            var best = Kept(SurfaceAcceptance.MakeAcceptable(w2(sp.Ring), h2), h2, out double got);
            // (perf) A ring the game model only trimmed a little (spikes, short edges: under half a square metre) is kept as
            // it is: regrouping its cells and then every cell on its own covered more only a few times in a hundred, and then by
            // under 0.1 m², at the cost of re-planning every cell. Rings it cut up or dropped are still rescued.
            if (want - got <= RescueLoss || sp.Cells == null || sp.Cells.Count == 0) return best;
            foreach (int cap in new[] { 48, 16 })
            {
                var alt = new List<float3[]>();
                double a = 0;
                foreach (var ring in SurfaceCells.Regroup(sp.Cells, cap, cancel))
                {
                    alt.AddRange(Kept(SurfaceAcceptance.MakeAcceptable(w2(ring), h2), h2, out double ka));
                    a += ka;
                }
                if (a > got + 1e-3) { best = alt; got = a; }
                if (want - got <= 0.05) return best;
            }
            var cells = new List<float3[]>();
            double c = 0;
            foreach (var cell in sp.Cells)
            {
                cells.AddRange(Kept(SurfaceAcceptance.MakeAcceptable(w2(cell), h2), h2, out double kc));
                c += kc;
            }
            if (c > got + 1e-3) best = cells;
            return best;
        }

        /// <summary>
        /// The rings the build keeps (SurfaceRules.GameKeeps: as they are or repaired by the build); the rest it would skip.
        /// <paramref name="area"/>: their area.
        /// </summary>
        private static List<float3[]> Kept(List<float3[]> rings, Func<float2, float> h2, out double area)
        {
            var kept = new List<float3[]>(rings.Count);
            area = 0;
            foreach (var r in rings)
            {
                if (!SurfaceRules.GameKeeps(r)) continue;
                kept.Add(r);
                area += Pricing.PolygonArea(r);
            }
            return kept;
        }

        /// <summary>Area (m²) a surface ring may lose to the game model's clean-up before its cells are regrouped to win it back.</summary>
        internal const double RescueLoss = 0.5;

        private static List<AssetRef> SpeciesAssets(ParkingSettings s)
        {
            // must mirror the species order built in Build()
            var r = new List<AssetRef>();
            if (s.Trees.Length == 0) r.Add(new AssetRef(ParkingAssets.DefaultTree, ParkingAssets.Trees)); else foreach (var t in s.Trees) r.Add(new AssetRef(t));
            if (s.Bushes.Length == 0) foreach (var b in ParkingAssets.DefaultBushes) r.Add(new AssetRef(b, ParkingAssets.Bushes)); else foreach (var b in s.Bushes) r.Add(new AssetRef(b));
            return r;
        }

        /// <summary>
        /// Welds endpoints closer than 0.75 m (no sub-metre courses — PLT dropped courses under 1 m)
        /// and splits every segment at endpoints of others lying on it, so junctions share one node.
        /// </summary>
        private static List<Seg> SplitAtJunctions(List<Seg> segs)
        {
            var reps = new List<double2>();
            double2 Weld(double2 p)
            {
                foreach (var r in reps) if (PGeo.Len(r - p) < 0.75) return r;
                reps.Add(p);
                return p;
            }
            // attached road ends must not move: weld only lot-side ends
            foreach (var sg in segs)
            {
                if (sg.StartAttach < 0) sg.A = Weld(sg.A);
                if (sg.EndAttach < 0) sg.B = Weld(sg.B);
            }
            // (P2-1) a drawn line crossing another line gets a junction there (both are split): nets must not overlap
            for (int i = 0; i < segs.Count; i++)
                for (int j = i + 1; j < segs.Count; j++)
                {
                    var p = segs[i]; var q = segs[j];
                    if (!p.Sketch && !q.Sketch) continue;
                    if (!PGeo.LineLine(p.A, p.B, q.A, q.B, out double t, out double u) || t < 0 || t > 1 || u < 0 || u > 1) continue;
                    Weld(p.A + (p.B - p.A) * t);
                }
            var outList = new List<Seg>();
            foreach (var sg in segs)
            {
                if (PGeo.Len(sg.B - sg.A) < 0.5) continue;
                var ts = new List<double>();
                var ab = sg.B - sg.A; double l2 = PGeo.Dot(ab, ab);
                foreach (var p in reps)
                {
                    double t = PGeo.Dot(p - sg.A, ab) / l2;
                    if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                    if (PGeo.Len(sg.A + ab * t - p) > 2e-3) continue;
                    ts.Add(t);
                }
                if (ts.Count == 0) { outList.Add(sg); continue; }
                ts.Sort();
                double prev = 0; var prevP = sg.A;
                for (int i = 0; i <= ts.Count; i++)
                {
                    double t = i < ts.Count ? ts[i] : 1;
                    var p = i < ts.Count ? sg.A + ab * t : sg.B;
                    if (i < ts.Count) foreach (var r in reps) if (PGeo.Len(r - p) < 2e-3) { p = r; break; }
                    if (PGeo.Len(p - prevP) < 1e-6) continue;
                    outList.Add(new Seg
                    {
                        A = prevP, B = p, Role = sg.Role, Asset = sg.Asset,
                        StartAttach = prev == 0 ? sg.StartAttach : -1, StartT = sg.StartT,
                        EndAttach = t == 1 ? sg.EndAttach : -1, EndT = sg.EndT,
                    });
                    prev = t; prevP = p;
                }
            }
            return outList;
        }
    }

    /// <summary>
    /// (perf) Green or asphalt at a point, from the convex cells the surfaces were merged from (each ring is exactly the
    /// union of its cells), by grid cell: a handful of small convex cells per probe instead of every ring.
    /// Green = inside a green cell and no asphalt cell (as the rings were asked before).
    /// </summary>
    internal sealed class SurfaceIndex
    {
        private readonly List<List<double2>> _cells = new List<List<double2>>();
        private readonly List<bool> _asphalt = new List<bool>();
        private readonly List<double4> _box = new List<double4>();
        private readonly double2 _min;
        private readonly double _inv;
        private readonly int _nx, _ny;
        private readonly int[] _start, _items;

        public SurfaceIndex(List<SurfacePoly> surfaces)
        {
            var mn = new double2(double.MaxValue, double.MaxValue); var mx = new double2(double.MinValue, double.MinValue);
            double area = 0;
            foreach (var sp in surfaces)
            {
                var list = sp.Cells != null && sp.Cells.Count > 0 ? sp.Cells : new List<List<double2>> { sp.Ring };
                foreach (var c in list)
                {
                    if (c == null || c.Count < 3) continue;
                    PGeo.Bounds(c, out var a, out var b);
                    _cells.Add(c); _asphalt.Add(sp.Asphalt); _box.Add(new double4(a.x, a.y, b.x, b.y));
                    mn = math.min(mn, a); mx = math.max(mx, b);
                    area += (b.x - a.x) * (b.y - a.y);
                }
            }
            if (_cells.Count == 0) { _nx = _ny = 1; _inv = 1; _start = new int[2]; _items = Array.Empty<int>(); return; }
            double w = Math.Max(1e-6, mx.x - mn.x), h = Math.Max(1e-6, mx.y - mn.y);
            double cell = Math.Max(2, Math.Min(32, Math.Sqrt(area / _cells.Count)));
            cell = Math.Max(cell, Math.Max(w, h) / 256);
            _min = mn; _inv = 1 / cell;
            _nx = (int)Math.Floor(w * _inv) + 1; _ny = (int)Math.Floor(h * _inv) + 1;
            var count = new int[_nx * _ny + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
                    _start = (int[])count.Clone();
                    _items = new int[count[count.Length - 1]];
                }
                for (int i = 0; i < _cells.Count; i++)
                {
                    var b = _box[i];
                    int x0 = Cx(b.x), x1 = Cx(b.z), y0 = Cy(b.y), y1 = Cy(b.w);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int c = x + y * _nx;
                            if (pass == 0) count[c + 1]++; else _items[count[c]++] = i;
                        }
                }
            }
        }

        private int Cx(double x) { int i = (int)Math.Floor((x - _min.x) * _inv); return i < 0 ? 0 : i >= _nx ? _nx - 1 : i; }
        private int Cy(double y) { int i = (int)Math.Floor((y - _min.y) * _inv); return i < 0 ? 0 : i >= _ny ? _ny - 1 : i; }

        public bool IsGreen(double2 p)
        {
            if (_items.Length == 0) return false;
            int c = Cx(p.x) + Cy(p.y) * _nx;
            bool green = false;
            for (int k = _start[c], end = _start[c + 1]; k < end; k++)
            {
                int i = _items[k];
                var b = _box[i];
                if (p.x < b.x || p.x > b.z || p.y < b.y || p.y > b.w) continue;
                if (!PGeo.Contains(_cells[i], p)) continue;
                if (_asphalt[i]) return false;
                green = true;
            }
            return green;
        }
    }
}

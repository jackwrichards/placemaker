// Placemaker roads layer — the planning pipeline.
//
//   generate (Grid Road Generator patterns, Ti4goc) → split at existing roads → avoid obstacles
//   → perimeter road → drop pieces lying on existing roads → connect ends to existing roads
//   → prune stubs → heights → nets/objects → blocks and frontage regions.
//
// Pure and deterministic: no statics, no shared caches; safe to run concurrently.

using System;
using System.Collections.Generic;
using System.Threading;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Roads
{
    internal static class RoadsPlanner
    {
        public const string StatLength = "Placemaker.Roads.Stat.Length";
        public const string StatBlocks = "Placemaker.Roads.Stat.Blocks";
        public const string StatJunctions = "Placemaker.Roads.Stat.Junctions";
        public const string StatConnections = "Placemaker.Roads.Stat.Connections";

        public const string IssueNothing = "Placemaker.Roads.Issue.NothingFits";
        public const string IssueFlat = "Placemaker.Roads.Issue.TerrainFlat";
        public const string IssueObstacles = "Placemaker.Roads.Issue.ObstaclesAvoided";
        public const string IssueFailed = "Placemaker.Roads.Issue.GenerationFailed";
        public const string IssueBadArea = "Placemaker.Roads.Issue.InvalidArea";

        public const string FactLength = "roads.length";
        public const string FactBlocks = "roads.blocks";
        public const string FactJunctions = "roads.junctions";
        public const string FactConnections = "roads.connections";
        public const string FactRemoved = "roads.removedByObstacles";
        /// <summary>(Smarter roads) The grid's frame: 0 = the outline, 1 = the terrain, 2 = a spine road through the area.</summary>
        public const string FactLayoutFrame = "roads.layoutFrame";
        /// <summary>(Smarter roads) Streets given up so that no block is too small or too thin to build on.</summary>
        public const string FactSliversMerged = "roads.sliversMerged";
        /// <summary>A block smaller than this (m²), or thinner than <see cref="SliverWidth"/> on average, is a sliver.</summary>
        public const float SliverArea = 1200f, SliverWidth = 12f;

        public static bool IsSliver(float2[] ring)
        {
            float area = Poly.Area(ring), per = Poly.Perimeter(ring);
            return per > 1f && (area < SliverArea || 2f * area / per < SliverWidth);
        }

        /// <summary>
        /// (Smarter roads) Merges slivers into their neighbours: for a block too small or thin to build on, the shortest new
        /// street along it that can go (not the perimeter, a main road or a ring; the car network stays in one piece) goes;
        /// the blocks are worked out again. Up to eight rounds. Returns the streets given up.
        /// </summary>
        private static int RemoveSlivers(RoadNetwork net, float2[] boundary, List<ExistingRoad> allRoads, Func<NetRole, float> hw, CancellationToken cancel)
        {
            int removed = 0;
            var unfixable = new List<float2>();
            for (int round = 0; round < 8; round++)
            {
                cancel.ThrowIfCancellationRequested();
                var lines = new List<RoadBlocks.Line>();
                foreach (var e in net.Edges)
                {
                    if (!e.Alive || e.Walk || !e.IsCar) continue;
                    var pts = new List<float3>();
                    e.Curve.Flatten(pts, null, 4f, 6f);
                    var l = new List<float2>(pts.Count);
                    foreach (var p in pts) l.Add(p.xz);
                    lines.Add(new RoadBlocks.Line { Points = l, HalfWidth = hw(e.Role), Kind = RoadBlocks.KindGenerated, Role = e.Role });
                }
                foreach (var r in allRoads) AddClipped(lines, r, boundary);
                var blocks = RoadBlocks.Compute(boundary, lines, new List<(float2, float)>(), null, cancel);
                bool changed = false;
                foreach (var b in blocks)
                {
                    if (b.RoundaboutIsland || !IsSliver(b.Ring)) continue;
                    bool known = false;
                    foreach (var u in unfixable) if (math.distance(u, b.Centroid) < 1f) known = true;
                    if (known) continue;
                    var cands = new List<(float len, RoadEdge e)>();
                    foreach (var e in net.Edges)
                    {
                        if (!RoadGraph.Car(e) || e.Role == NetRole.PerimeterRoad || e.Role == NetRole.Avenue || e.Role == NetRole.Roundabout) continue;
                        float reach = hw(e.Role) + 4f;
                        bool along = false;
                        for (int i = 1; i <= 7 && !along; i++)
                            if (Poly.DistanceToBoundary(b.Ring, e.Curve.Position(i / 8f).xz) < reach && Poly.Contains(b.Ring, e.Curve.Position(i / 8f).xz) == false) along = true;
                        if (along) cands.Add((RoadGraph.Length(e), e));
                    }
                    cands.Sort((x, y) => x.len.CompareTo(y.len));
                    bool fixedOne = false;
                    foreach (var (_, e) in cands)
                    {
                        RoadGraph.Components(net, RoadGraph.Car, out int before);
                        e.Removed = true;
                        RoadGraph.Components(net, RoadGraph.Car, out int after);
                        if (after > before) { e.Removed = false; continue; }
                        removed++;
                        fixedOne = true;
                        break;
                    }
                    if (!fixedOne) unfixable.Add(b.Centroid);
                    else { changed = true; break; }
                }
                if (!changed) break;
                net.Weld();
            }
            return removed;
        }

        /// <summary>(Smarter roads) Grid character "lively": cross-street stretches left out for the brick pattern.</summary>
        public const string FactBrick = "roads.brick";
        /// <summary>(Smarter roads) Grid character: a desire-line diagonal laid; junctions bent to the hill; streets turned into steps; hillside parks.</summary>
        public const string FactDiagonal = "roads.diagonal", FactBent = "roads.bent", FactStairs = "roads.stairs", FactHillParks = "roads.hillParks";
        /// <summary>(Smarter roads) Desire paths painted; junctions with worn corners.</summary>
        public const string FactDesirePaths = "roads.desirePaths", FactWornCorners = "roads.wornCorners";
        /// <summary>(Smarter roads) Designed joins to the city; those continuing a street inside; those onto an existing junction.</summary>
        public const string FactCityLinks = "roads.cityLinks", FactLinksContinued = "roads.linksContinued", FactLinksOnJunction = "roads.linksOnJunction";
        /// <summary>(Smarter roads) Existing streets the grid's streets line up with.</summary>
        public const string FactStubsAligned = "roads.stubsAligned";

        private const float MinFragmentLength = 30f;

        // s34
        public const string StatWalkways = "Placemaker.Roads.Stat.Walkways";
        public const string StatSteepest = "Placemaker.Roads.Stat.Steepest";
        public const string IssueIslands = "Placemaker.Roads.Issue.IslandsDropped";
        public const string IssueNotConnected = "Placemaker.Roads.Issue.NotConnected";
        public const string IssueSteep = "Placemaker.Roads.Issue.Steep";
        public const string FactWalkways = "roads.walkways";
        public const string FactLinks = "roads.links";
        public const string FactIslandsDropped = "roads.islandsDropped";
        public const string FactAccessCuts = "roads.accessCuts";
        public const string FactLanes = "roads.lanes";
        public const string FactSteepest = "roads.steepestGrade";
        private const int MaxSteepIssues = 6;
        // s61: issues that explain themselves
        public const string IssueDroppedObstacle = "Placemaker.Roads.Issue.DroppedObstacle";
        public const string IssuePerimeterBlocked = "Placemaker.Roads.Issue.PerimeterBlocked";
        public const string IssuePerimeterOpen = "Placemaker.Roads.Issue.PerimeterOpen";
        public const string IssueJunctionCut = "Placemaker.Roads.Issue.JunctionCut";
        public const string FactBypassed = "roads.bypassed";
        public const string FactPerimeterClosed = "roads.perimeterClosed";
        public const string FactPerimeterOpen = "roads.perimeterOpen";
        private const int MaxBlockedIssues = 8;
        private const int MaxCutIssues = 4;

        private static string FormatValue(float v, string unit) => Math.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + unit;
        private static string FormatValue(bool on) => on ? "On" : "Off";

        private static bool NearObstacle(RoadObstacles o, float2 p, float radius)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * math.PI / 4f;
                if (o.BlockedAt(p + new float2(math.cos(a), math.sin(a)) * radius * 0.5f) || o.BlockedAt(p + new float2(math.cos(a), math.sin(a)) * radius)) return true;
            }
            return o.BlockedAt(p);
        }

        /// <summary>(s61) The sharp corners of the outline: a junction that crowds one merges onto it (the corner stays where the outline has it).</summary>
        private static void AddCorners(RoadNetwork net, float2[] boundary)
        {
            net.Corners.Clear();
            int n = boundary.Length;
            for (int i = 0; i < n; i++)
            {
                float2 d0 = math.normalizesafe(boundary[i] - boundary[(i + n - 1) % n]), d1 = math.normalizesafe(boundary[(i + 1) % n] - boundary[i]);
                if (math.degrees(math.acos(math.clamp(math.dot(d0, d1), -1f, 1f))) >= FreeAreaPerimeter.SharpCornerDegrees) net.Corners.Add(boundary[i]);
            }
        }

        private const float RingClipMargin = 1.5f;

        /// <summary>
        /// (Smarter roads) A whole layout the planner may try: its settings (a terrain fit the player left on Automatic) and,
        /// for the grid, the frame and phase RoadLayoutSearch picked for it (HasGrid false: decided inside).
        /// </summary>
        internal sealed class PlanVariant
        {
            public string Name = "base";
            public SettingsBag Settings;
            public bool HasGrid;
            public GridParameters Grid;
            public string Frame = "outline";
            public int StubsAligned;
            public RoadRank SpineRank;
            /// <summary>(Smarter roads) Lay the desire-line diagonal through the grid (Grid character).</summary>
            public bool Diagonal;

            public PlanVariant With(string name, bool diagonal) => new PlanVariant
            {
                Name = name, Settings = Settings, HasGrid = HasGrid, Grid = Grid, Frame = Frame, StubsAligned = StubsAligned, SpineRank = SpineRank, Diagonal = diagonal,
            };
        }

        public const string FactRating = "roads.rating";
        public const string FactVariants = "roads.variants";
        /// <summary>Which candidate layout won: 0 = the outline grid, 1 = the grid on the spine road, 2 = along the contours, 3 = turned against the slope.</summary>
        public const string FactVariant = "roads.variant";

        /// <summary>
        /// (Smarter roads) Plans the area. When there is more than one sensible layout (a grid on the outline or on a spine
        /// road through the area; on sloped land left on Automatic, a layout along the contours too) each is planned in full
        /// and rated (RoadsRating), and the best rated wins. Otherwise one plan, as before.
        /// </summary>
        public static AreaPlan Plan(AreaInput input, IReadOnlyList<ControlSchema> controls, CancellationToken cancel)
        {
            var plan = PlanBest(input, controls, cancel);
            // (p1-ground) The contour pattern needs one main slope: on flat land, or rough land that falls every way (egg-crate
            // hills), it found no line and built nothing ("NothingToBuild" in the game runner). Winding streets that settle into
            // the ground are what a hillside design means there, so it falls back to them rather than to an empty plan.
            var settings = input?.Settings;
            if (plan.Nets.Count == 0 && settings != null && RoadsSchema.ParsePattern(settings.S(RoadsIds.Pattern, "grid")) == RoadPattern.Contour)
            {
                var s = settings.Clone();
                s.Values[RoadsIds.Pattern] = "organic";
                AreaPlan fallback;
                input.Settings = s;
                try { fallback = PlanBest(input, controls, cancel); }
                finally { input.Settings = settings; }
                if (fallback.Nets.Count > 0)
                {
                    fallback.Facts[FactContourFallback] = 1;
                    return fallback;
                }
            }
            return plan;
        }

        /// <summary>Plan fact: the contour pattern found no slope and the plan fell back to winding streets.</summary>
        public const string FactContourFallback = "roads.contourFallback";

        private static AreaPlan PlanBest(AreaInput input, IReadOnlyList<ControlSchema> controls, CancellationToken cancel)
        {
            if (input?.Shape?.Ring == null || CleanRing(input.Shape.Ring).Length < 3) return PlanCore(input, controls, cancel, null);
            var variants = Variants(input, controls, cancel);
            if (variants.Count <= 1) return PlanCore(input, controls, cancel, variants.Count == 1 ? variants[0] : null);
            AreaPlan best = null;
            float bestScore = float.MinValue;
            int bestIndex = 0;
            for (int i = 0; i < variants.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var p = PlanCore(input, controls, cancel, variants[i]);
                float score = Rate(p, input, variants[i].Settings, controls).Total;
                // an avenue or a big road through the area leads the layout (the user: use it as the spine)
                if (variants[i].Frame == "spine" && variants[i].SpineRank >= RoadRank.Medium) score += 2f;
                // a later candidate must be clearly better (the first is the plain layout)
                if (best == null || score > bestScore + 1f) { best = p; bestScore = score; bestIndex = i; }
            }
            best.Facts[FactRating] = bestScore;
            best.Facts[FactVariants] = variants.Count;
            var w = variants[bestIndex];
            best.Facts[FactVariant] = w.Name == "follow" ? 2 : w.Name == "tilted" ? 3 : w.Frame == "spine" ? 1 : 0;
            return best;
        }

        /// <summary>The rating of a roads plan for its area and settings (the planner's own judge; the road lab uses it too).</summary>
        public static RoadsRating Rate(AreaPlan plan, AreaInput input, SettingsBag settings, IReadOnlyList<ControlSchema> controls)
        {
            settings = settings ?? input.Settings ?? new SettingsBag();
            var site = input.Site ?? new SiteContext();
            var pattern = RoadsSchema.ParsePattern(settings.S(RoadsIds.Pattern, "grid"));
            bool perimeter = pattern == RoadPattern.Terraces ? settings.S(RoadsIds.PerimeterRoad, "auto") == "on" : PerimeterWanted(settings.S(RoadsIds.PerimeterRoad, "auto"), input.Shape.Source);
            var o = new RoadsRating.Options
            {
                MaxGrade = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f,
                JunctionSpacing = RoadsSchema.Num(settings, controls, RoadsIds.JunctionSpacing),
                Perimeter = perimeter,
                Height = plan.HeightAfter ?? SafeHeight(site.HeightAt),
            };
            return RoadsRating.Measure(plan, CleanRing(input.Shape.Ring), site, o);
        }

        /// <summary>The candidate layouts worth planning in full (see <see cref="Plan"/>).</summary>
        internal static List<PlanVariant> Variants(AreaInput input, IReadOnlyList<ControlSchema> controls, CancellationToken cancel)
        {
            var list = new List<PlanVariant>();
            var site = input.Site ?? new SiteContext();
            var settings = input.Settings ?? new SettingsBag();
            Func<float2, float> heightAt = SafeHeight(site.HeightAt);
            GridParameters gp = RoadsSchema.ToGridParameters(settings, controls, out RoadPattern pattern);
            gp.HeightAt = heightAt;
            gp.Cancel = cancel;
            bool follow = settings.B(RoadsIds.FollowTerrain, true);
            bool frameFree = math.abs(gp.AngleOffsetDegrees) < 0.01f && !gp.AlignToTerrain;
            gp.EdgeInset = EdgeInset(settings, input.Shape.Source, pattern);
            float2[] ring2 = CleanRing(input.Shape.Ring);
            var genRing = GenRing(input, settings, controls, ring2, heightAt, out _);
            if (pattern == RoadPattern.Grid)
            {
                Func<NetRole, float> hw = HalfWidths(settings, site);
                var allRoads = RoadConnections.Collect(site);
                RoadConnections.Partition(allRoads, settings.B(RoadsIds.ConnectExisting, true), settings.S(RoadsIds.ConnectTo, ConnectTo.Streets), out var existing, out _);
                float maxGrade = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f;
                foreach (var f in RoadLayoutSearch.Frames(genRing, gp, existing, allRoads, follow ? heightAt : null, maxGrade, frameFree, hw, cancel))
                    list.Add(new PlanVariant { Name = f.Frame, Settings = settings, HasGrid = true, Grid = f.Params, Frame = f.Frame, StubsAligned = f.StubsAligned, SpineRank = f.SpineRank });
            }
            else list.Add(new PlanVariant { Settings = settings });
            // (Smarter roads) Grid character: a desire line between two city streets the grid makes people zig-zag between
            // asks for a diagonal. Lively lays it in every grid layout; auto also tries the first layout with it (rated).
            if (pattern == RoadPattern.Grid && list.Count > 0 && list[0].HasGrid)
            {
                string character = settings.S(RoadsIds.GridCharacter, RoadCharacter.Auto);
                if (character != RoadCharacter.Strict)
                {
                    var allD = RoadConnections.Collect(site);
                    RoadConnections.Partition(allD, settings.B(RoadsIds.ConnectExisting, true), settings.S(RoadsIds.ConnectTo, ConnectTo.Streets), out var exD, out _);
                    var g0 = list[0].Grid;
                    g0.HeightAt = heightAt;
                    float gain = character == RoadCharacter.Lively ? RoadCharacter.LivelyGain : RoadCharacter.AutoGain;
                    float mg = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f;
                    var d = RoadCharacter.DesireLine(genRing, g0, exD, follow ? heightAt : null, mg, gain);
                    if (d != null)
                    {
                        if (character == RoadCharacter.Lively) foreach (var v in list) v.Diagonal = true;
                        else list.Add(list[0].With("diagonal", true));
                    }
                    else if (RoadCharacter.FitGrid(genRing, g0, exD, follow ? heightAt : null, mg, gain, out var fitted))
                    {
                        // the blocks as set leave no room for it: a grid fitted to the diagonal (bigger blocks lined up on it);
                        // lively puts it first (it wins unless a plain layout is clearly better), auto lets it compete
                        var fv = list[0].With("diagonal", true);
                        fv.Grid = fitted;
                        if (character == RoadCharacter.Lively) list.Insert(0, fv); else list.Add(fv);
                    }
                }
            }
            // On sloped land the terrain fit left on Automatic may follow the contours instead (the rating decides).
            bool sloped = GridGenerator.TryContourDirection(new List<float2>(ring2), heightAt, out float2 level, out float aniso, out float meanSlope) && meanSlope >= 0.04f;
            // (Smarter roads) On an even hillside the grid may turn just enough that neither street direction climbs past the
            // design grade: the nearest such angle to the outline's (the rating decides whether it is worth the turn).
            if (sloped && follow && frameFree && pattern == RoadPattern.Grid && aniso > 0.5f && settings.S(RoadsIds.TerrainFit, "auto") == "auto")
            {
                float maxGrade = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f;
                float ratio = 0.95f * maxGrade / meanSlope;
                var u0 = GridGenerator.DefaultFrame(genRing, gp).u;
                float2 fall = new float2(-level.y, level.x);
                // angle (0..90°) between the outline's u axis and the fall line; feasible when cos ψ ≤ ratio and sin ψ ≤ ratio
                float psi0 = math.degrees(math.acos(math.saturate(math.abs(math.dot(u0, fall)))));
                if (ratio < 1f && ratio > 0.7072f)
                {
                    float lo = math.degrees(math.acos(ratio)), hi = math.degrees(math.asin(ratio));
                    if (psi0 < lo || psi0 > hi)
                    {
                        float target = psi0 < lo ? lo + 1f : hi - 1f;
                        // turn u from ψ0 to the target, the shorter way (sign: which side of the fall line u lies)
                        float side = math.sign(u0.x * fall.y - u0.y * fall.x) * math.sign(math.dot(u0, fall));
                        float turn = (target - psi0) * (side == 0f ? 1f : -side);
                        var g2 = gp;
                        g2.AngleOffsetDegrees = gp.AngleOffsetDegrees + turn;
                        var hw2 = HalfWidths(settings, site);
                        var all2 = RoadConnections.Collect(site);
                        RoadConnections.Partition(all2, settings.B(RoadsIds.ConnectExisting, true), settings.S(RoadsIds.ConnectTo, ConnectTo.Streets), out var ex2, out _);
                        foreach (var f in RoadLayoutSearch.Frames(genRing, g2, ex2, all2, heightAt, maxGrade, false, hw2, cancel))
                        {
                            list.Add(new PlanVariant { Name = "tilted", Settings = settings, HasGrid = true, Grid = f.Params, Frame = "terrain", StubsAligned = f.StubsAligned });
                            break;
                        }
                    }
                }
            }
            bool fits = pattern == RoadPattern.Grid || pattern == RoadPattern.Superblock || pattern == RoadPattern.Organic || pattern == RoadPattern.Tree;
            if (sloped && follow && fits && frameFree && settings.S(RoadsIds.TerrainFit, "auto") == "auto")
            {
                var s2 = settings.Clone();
                s2.Values[RoadsIds.TerrainFit] = "follow";
                list.Add(new PlanVariant { Name = "follow", Settings = s2, Frame = "terrain" });
            }
            return list;
        }

        /// <summary>
        /// (Smarter roads) No border on a drawn / painted area: the grid's outer streets run one lot deep inside the edge (the lots
        /// reach the edge, nothing runs out to it). An outline of existing roads (block / perimeter picks) is a border already.
        /// </summary>
        internal const float NoBorderInset = 52f;

        private static float EdgeInset(SettingsBag settings, AreaSource source, RoadPattern pattern)
        {
            if (pattern != RoadPattern.Grid || (source != AreaSource.Drawn && source != AreaSource.Painted)) return 0f;
            return PerimeterWanted(settings.S(RoadsIds.PerimeterRoad, "auto"), source) ? 0f : NoBorderInset;
        }

        /// <summary>The generators' outline: the selection, its corners rounded when a perimeter road follows a drawn/painted one.</summary>
        private static List<float3> GenRing(AreaInput input, SettingsBag settings, IReadOnlyList<ControlSchema> controls, float2[] ring2,
            Func<float2, float> heightAt, out float meanY)
        {
            meanY = 0f;
            foreach (var p in ring2) meanY += heightAt(p);
            meanY /= math.max(1, ring2.Length);
            var pattern = RoadsSchema.ParsePattern(settings.S(RoadsIds.Pattern, "grid"));
            var source = input.Shape.Source;
            bool perimeter = pattern == RoadPattern.Terraces ? settings.S(RoadsIds.PerimeterRoad, "auto") == "on" : PerimeterWanted(settings.S(RoadsIds.PerimeterRoad, "auto"), source);
            var genRing = new List<float3>(ring2.Length);
            foreach (var p in ring2) genRing.Add(new float3(p.x, meanY, p.y));
            float corner = PerimeterCorner(settings, controls, input.Site ?? new SiteContext());
            if (perimeter && corner > 0.5f && (source == AreaSource.Drawn || source == AreaSource.Painted))
            {
                var smooth = FreeAreaPerimeter.Smooth(genRing, corner);
                var cleaned = CleanRing(ToArray(smooth, meanY));
                if (cleaned.Length >= 3 && Poly.IsSimple(cleaned))
                {
                    genRing.Clear();
                    foreach (var p in cleaned) genRing.Add(new float3(p.x, meanY, p.y));
                }
            }
            return genRing;
        }

        /// <summary>The perimeter's corner radius: the setting, but never tighter than the road turns cleanly (half width + 6 m).</summary>
        private static float PerimeterCorner(SettingsBag settings, IReadOnlyList<ControlSchema> controls, SiteContext site)
        {
            float corner = RoadsSchema.Num(settings, controls, RoadsIds.PerimeterCornerRadius);
            if (corner > 0.5f) corner = math.max(corner, HalfWidths(settings, site)(NetRole.PerimeterRoad) + 6f);
            return corner;
        }

        internal static AreaPlan PlanCore(AreaInput input, IReadOnlyList<ControlSchema> controls, CancellationToken cancel, PlanVariant variant)
        {
            var plan = new AreaPlan();
            var site = input.Site ?? new SiteContext();
            var settings = variant?.Settings ?? input.Settings ?? new SettingsBag();
            Func<float2, float> heightAt = SafeHeight(site.HeightAt);

            float2[] ring2 = CleanRing(input.Shape?.Ring);
            if (ring2.Length < 3)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = IssueBadArea });
                return plan;
            }
            GridParameters gp = RoadsSchema.ToGridParameters(settings, controls, out RoadPattern pattern);
            gp.HeightAt = heightAt;
            gp.Cancel = cancel;
            AreaSource source = input.Shape.Source;
            gp.EdgeInset = EdgeInset(settings, source, pattern);
            // (s52) Hillside terraces lay their own streets along the contours: a perimeter road would climb straight up the
            // hill, so "auto" means none there.
            bool terraces = pattern == RoadPattern.Terraces;
            bool perimeter = terraces ? settings.S(RoadsIds.PerimeterRoad, "auto") == "on" : PerimeterWanted(settings.S(RoadsIds.PerimeterRoad, "auto"), source);
            bool connect = settings.B(RoadsIds.ConnectExisting, true);
            string connectTo = settings.S(RoadsIds.ConnectTo, ConnectTo.Streets);
            bool avoid = settings.B(RoadsIds.AvoidObstacles, true);
            bool follow = settings.B(RoadsIds.FollowTerrain, true) || pattern == RoadPattern.Contour;
            bool loopFamily = RoadsSchema.IsLoopFamily(pattern);
            Func<NetRole, float> hw = HalfWidths(settings, site);
            // Smarter roads: the existing roads split into those the plan may join (Connect to: every road but highways and
            // their ramps by default) and barriers it never joins nor crosses.
            var allRoads = RoadConnections.Collect(site);
            RoadConnections.Partition(allRoads, connect, connectTo, out var existing, out var barriers);
            float maxGrade = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f;

            // Drawn/painted outlines get rounded corners when a perimeter road follows them (GRG free area).
            var genRing = GenRing(input, settings, controls, ring2, heightAt, out float meanY);
            float corner = PerimeterCorner(settings, controls, site);
            float2[] boundary = new float2[genRing.Count];
            for (int i = 0; i < boundary.Length; i++) boundary[i] = genRing[i].xz;

            // ── 1. Generate ─────────────────────────────────────────────
            if (pattern == RoadPattern.Contour && GridGenerator.IsTerrainFlat(genRing, heightAt))
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueFlat });
                return plan;
            }
            // Smarter roads: the grid's frame and phase fit the network that is there (a spine, the streets it continues) and
            // the land; the player's angle / terrain alignment keep the frame, only the phase moves.
            if (pattern == RoadPattern.Grid)
            {
                string frame;
                int aligned;
                if (variant != null && variant.HasGrid)
                {
                    var g = variant.Grid;
                    g.HeightAt = heightAt;
                    g.Cancel = cancel;
                    gp = g;
                    frame = variant.Frame;
                    aligned = variant.StubsAligned;
                }
                else
                {
                    bool frameFree = math.abs(gp.AngleOffsetDegrees) < 0.01f && !gp.AlignToTerrain;
                    var layout = RoadLayoutSearch.ForGrid(genRing, gp, existing, allRoads, follow ? heightAt : null, maxGrade, frameFree, hw, cancel);
                    gp = layout.Params;
                    frame = layout.Frame;
                    aligned = layout.StubsAligned;
                }
                plan.Facts[FactLayoutFrame] = frame == "spine" ? 2 : frame == "terrain" ? 1 : 0;
                plan.Facts[FactStubsAligned] = aligned;
            }
            List<RoadSegmentDef> segments;
            RoundaboutInfo roundabout = default;
            // (s52) Hillside terraces: contour streets at a terrace's height step, links between them, bank walks (later).
            Terraces.TerraceLayout terraceLayout = null;
            Terraces.TerraceOptions terraceOpt = null;
            try
            {
                if (terraces)
                {
                    segments = new List<RoadSegmentDef>();
                    terraceOpt = Terraces.TerraceOptions.Read(settings, controls, HalfWidths(settings, site));
                    terraceOpt.Seed = input.Seed == 0 ? 1u : input.Seed;
                    Poly.Bounds(ring2, out float2 tmn, out float2 tmx);
                    var tObs = avoid ? new RoadObstacles(site, tmn, tmx) : null;
                    terraceLayout = Terraces.TerraceLayout.Build(boundary, heightAt, terraceOpt, tObs, cancel);
                    Terraces.TerraceRoutes.Plan(terraceLayout, terraceOpt, tObs, cancel);
                }
                else segments = loopFamily
                    ? GridGenerator.GenerateLoopGrid(genRing, gp)
                    : GridGenerator.GenerateGrid(genRing, gp, out _, out roundabout);
                // (Smarter roads) Grid character "lively": the brick pattern (staggered blocks, T junctions)
                if (pattern == RoadPattern.Grid && settings.S(RoadsIds.GridCharacter, RoadCharacter.Auto) == RoadCharacter.Lively)
                    plan.Facts[FactBrick] = RoadCharacter.Brick(segments, genRing, gp);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = IssueFailed, Args = new object[] { e.GetType().Name + ": " + e.Message } });
                return plan;
            }
            cancel.ThrowIfCancellationRequested();

            var net = new RoadNetwork();
            foreach (var s in segments)
            {
                if (!MathEx.Finite(s.Start) || !MathEx.Finite(s.End)) continue;
                if (math.distance(s.Start.xz, s.End.xz) < 0.5f) continue;
                var curve = s.IsArc ? RoadCurve.Fit(s.Start, s.StartTangent, s.EndTangent, s.End) : RoadCurve.Straight(s.Start, s.End);
                net.Edges.Add(new RoadEdge(curve, RoleOf(s, pattern, loopFamily), s.IsCulDeSacEnd));
            }
            if (terraceLayout != null) Terraces.TerraceNetwork.Add(net, terraceLayout, terraceOpt);
            AddCorners(net, boundary);
            net.Weld();

            // (s34) Follow contours: the long streets of a grid / superblocks are laid onto contour lines.
            bool followContours = settings.S(RoadsIds.TerrainFit, "auto") == "follow";
            if (followContours && follow && (pattern == RoadPattern.Grid || pattern == RoadPattern.Superblock)
                && GridGenerator.TryContourDirection(new List<float2>(boundary), heightAt, out float2 contourAxis, out float slopeAnisotropy, out float meanSlope)
                && slopeAnisotropy > 0.3f && meanSlope >= RoadContourWarp.MinSlope)
            {
                float rot = math.radians(gp.AngleOffsetDegrees);
                float2 axis = new float2(contourAxis.x * math.cos(rot) - contourAxis.y * math.sin(rot), contourAxis.x * math.sin(rot) + contourAxis.y * math.cos(rot));
                float pitch = pattern == RoadPattern.Grid ? gp.SpacingMeters : gp.SuperblockZoneMeters / 3f;
                RoadContourWarp.Apply(net, heightAt, axis, pitch, boundary);
            }
            // (Smarter roads) Grid character: the grid gives way to the hill (its junctions bend the streets under the design
            // grade) and to a desire line between two city streets (a diagonal). Strict keeps the plain grid.
            string character = settings.S(RoadsIds.GridCharacter, RoadCharacter.Auto);
            bool characterful = pattern == RoadPattern.Grid && character != RoadCharacter.Strict;
            // terraced land levels the blocks itself: the grid gives way to the hill only on natural land
            bool givesWay = characterful && follow && settings.S(RoadsIds.LandShape, "natural") != "terrace";
            if (givesWay)
                plan.Facts[FactBent] = RoadTerrainFit.Bend(net, heightAt, maxGrade, 0.2f * gp.SpacingMeters, boundary);
            RoadCharacter.Diagonal diagonal = null;
            if (characterful && variant != null && variant.Diagonal)
            {
                diagonal = RoadCharacter.DesireLine(genRing, gp, existing, follow ? heightAt : null, maxGrade,
                    character == RoadCharacter.Lively ? RoadCharacter.LivelyGain : RoadCharacter.AutoGain);
                if (diagonal != null) RoadCharacter.Insert(net, diagonal, meanY);
            }
            plan.Facts[FactDiagonal] = diagonal != null ? 1 : 0;

            var attachments = connect ? input.Shape.Attachments : null;

            // ── 2. Existing roads crossing the new streets ──────────────
            if (barriers.Count > 0 && RoadConnections.CutAtBarriers(net, barriers, hw) > 0) net.Weld();
            if (existing.Count > 0)
            {
                RoadConnections.SplitAtExisting(net, existing);
                net.Weld();
                // a street laid on an existing road for part of its way ends on it
                if (RoadConnections.TrimOverlaps(net, existing, hw) > 0) net.Weld();
            }
            // streets hugging an existing road go (the road fronts that land already)
            if (allRoads.Count > 0 && RoadConnections.RemoveAlongside(net, allRoads, hw) > 0) net.Weld();
            cancel.ThrowIfCancellationRequested();

            // ── 3. Obstacles (interior) ─────────────────────────────────
            int removedByObstacles = 0;
            var detours = new List<RoadDetour.Result>();
            RoadObstacles obstacles = null;
            if (avoid)
            {
                Poly.Bounds(ring2, out float2 mn, out float2 mx);
                obstacles = new RoadObstacles(site, mn, mx);
                var det0 = RoadDetour.Run(net, obstacles, null, e => hw(e.Role) + 1f, boundary);
                removedByObstacles += det0.Removed;
                detours.Add(det0);
                // (s34) No stub pruning here: on tree-like layouts (organic, superblocks) it cascaded through whole
                // networks (the rural playtest site lost every road). Dead ends are handled once the network is final
                // (RoadConnectivity.CleanDeadEnds: short stubs go, long ones become cul-de-sacs).
                if (removedByObstacles > 0) AnchorOutlineNodes(net, boundary, source);
            }
            cancel.ThrowIfCancellationRequested();

            // ── 4. Perimeter road ────────────────────────────────────────
            bool anyInterior = false;
            foreach (var e in net.Edges) anyInterior |= e.Alive;
            if (perimeter && anyInterior)
            {
                var interior = new List<RoadSegmentDef>();
                foreach (var e in net.Edges) if (e.Alive) interior.Add(new RoadSegmentDef(e.Curve.A, e.Curve.D, false));
                var crossings = RoadConnections.RingCrossings(genRing, existing, attachments);
                foreach (var s in FreeAreaPerimeter.PerimeterRoad(genRing, interior, crossings))
                {
                    if (!MathEx.Finite(s.Start) || !MathEx.Finite(s.End) || math.distance(s.Start.xz, s.End.xz) < 0.5f) continue;
                    var curve = s.IsArc ? RoadCurve.Fit(s.Start, s.StartTangent, s.EndTangent, s.End) : RoadCurve.Straight(s.Start, s.End);
                    net.Edges.Add(new RoadEdge(curve, NetRole.PerimeterRoad));
                }
                net.Weld();
                if (barriers.Count > 0 && RoadConnections.CutAtBarriers(net, barriers, hw, e => e.Role == NetRole.PerimeterRoad) > 0) net.Weld();
                if (obstacles != null)
                {
                    var det1 = RoadDetour.Run(net, obstacles, e => e.Role == NetRole.PerimeterRoad, e => hw(e.Role) + 1f, boundary);
                    removedByObstacles += det1.Removed;
                    detours.Add(det1);
                }
            }
            cancel.ThrowIfCancellationRequested();

            // ── 5. Connect to existing roads, drop duplicates of them ───
            if (allRoads.Count > 0) RoadConnections.RemoveOverlapping(net, allRoads, hw);
            int attachedCount = 0;
            if (connect && (existing.Count > 0 || HasNodeAttachments(attachments)))
            {
                RoadConnections.Attach(net, existing, attachments);
                net.Weld();
                net.CollapseShortEdges(GridGenerator.MinSegmentLength);
                net.Weld();
            }
            AnchorOutlineNodes(net, boundary, source);
            net.DropFragments(MinFragmentLength);
            cancel.ThrowIfCancellationRequested();

            // (Smarter roads) Streets still far too steep for cars become steps (footpaths), the car network staying whole.
            if (givesWay) plan.Facts[FactStairs] = RoadTerrainFit.StairStreets(net, heightAt, maxGrade);

            // ── 5b. (s34) Network design: access, connectivity, dead ends, lanes, junctions, walkways, grades ──
            var ctx = new RoadDesign.Context
            {
                Boundary = boundary, Existing = existing, Barriers = barriers, Obstacles = obstacles, Height = follow ? heightAt : null, HalfWidth = hw,
                Perimeter = perimeter, PerimeterCorner = perimeter ? corner : -1f, JunctionSpacing = RoadsSchema.Num(settings, controls, RoadsIds.JunctionSpacing),
                MaxGrade = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f,
                Walkways = RoadsSchema.Num(settings, controls, RoadsIds.Walkways) / 100f,
                WalkwayStyle = settings.S(RoadsIds.WalkwayStyle, "paved"),
                BackLanes = RoadsSchema.Num(settings, controls, RoadsIds.BackLanes) / 100f,
                LaneStyle = settings.S(RoadsIds.LaneStyle, "alley"),
                TipStyle = settings.S(RoadsIds.TipStyle, "same"),
                Seed = input.Seed == 0 ? 1u : input.Seed,
                FollowContours = followContours && follow,
            };
            if (!connect) ctx.Existing = new List<ExistingRoad>();
            if (terraces)
            {
                // (s52) Every terrace street keeps its links (no access management: the links ARE the main roads' side
                // streets), and the walkways are the bank walks the layout planned, not the generic cut-throughs.
                ctx.JunctionSpacing = 0f;
                ctx.Walkways = 0f;
                ctx.FollowContours = false;
            }
            var design = RoadDesign.Run(net, ctx, cancel);
            cancel.ThrowIfCancellationRequested();
            // (Smarter roads) No microscopic blocks: a block too small or too thin to build on loses one of its new streets.
            plan.Facts[FactSliversMerged] = RemoveSlivers(net, boundary, allRoads, hw, cancel);
            if (plan.Facts[FactSliversMerged] > 0)
            {
                RoadConnectivity.CleanDeadEnds(net, new RoadConnectivity.Options { Boundary = boundary, Existing = existing, Barriers = barriers, HalfWidth = hw, Perimeter = perimeter }, new RoadConnectivity.Result());
                RoadConnectivity.PruneLoosePaths(net);
                net.DropFragments(150f);
                RoadDesign.Tidy(net, ctx);
            }

            // ── 5c. (s52) Hillside terraces: bank walks, then ONE terrain field from the final network ──
            Terraces.TerraceGround terraceGround = null;
            Func<float2, float> ground = heightAt;
            int bankWalks = 0;
            bool landTerrace = settings.S(RoadsIds.LandShape, "natural") == "terrace";
            // Terraced blocks (every other pattern): each zoning street's frontage level across, banks between the levels.
            Terraces.TerraceOptions blockOpt = null;
            Terraces.BlockField.Survey blockSurvey = null;
            // (blocks another step fills — a park on every block — grade their own ground: no terraces under them)
            bool childBlocks = input.ChildTargets != null && Array.Exists(input.ChildTargets, t => t != null && t.StartsWith("block", StringComparison.Ordinal));
            if (terraceLayout == null && landTerrace && follow && !childBlocks)
            {
                blockOpt = Terraces.TerraceOptions.Read(settings, controls, hw);
                blockOpt.Seed = input.Seed == 0 ? 1u : input.Seed;
                blockSurvey = Terraces.BlockField.Measure(boundary, heightAt, blockOpt.FlatSlope);
                if (blockSurvey.Sloped)
                {
                    plan.Facts["blocks.walksDropped"] = Terraces.BlockField.DropClimbingWalks(net, blockSurvey);
                    var lctx = new Common.LayerContext { Input = input, Plan = plan, Settings = settings, Cancel = cancel, Seed = blockOpt.Seed };
                    terraceGround = Terraces.BlockField.Build(lctx, boundary, blockSurvey, net, blockOpt, hw, obstacles, allRoads, cancel);
                    ground = terraceGround.HeightAfter ?? heightAt;
                    if (terraceGround.Cells > 0) plan.HeightAfter = ground;
                }
                cancel.ThrowIfCancellationRequested();
            }
            if (terraceLayout != null && landTerrace)
            {
                bankWalks = Terraces.TerraceNetwork.AddWalks(net, terraceLayout, hw, terraceOpt.BankSlope);
                var lctx = new Common.LayerContext { Input = input, Plan = plan, Settings = settings, Cancel = cancel, Seed = terraceOpt.Seed };
                terraceGround = Terraces.TerraceField.Build(lctx, terraceLayout, net, terraceOpt, hw, obstacles, allRoads, cancel);
                ground = terraceGround.HeightAfter ?? heightAt;
                if (terraceGround.Cells > 0) plan.HeightAfter = ground;
                cancel.ThrowIfCancellationRequested();
            }

            // ── 5d. (Smarter roads) The ground the streets wear in: desire paths, corner wear, verges ──
            var wear = RoadPaint.Apply(plan, net, new RoadPaint.Options
            {
                Level = settings.S(RoadsIds.GroundWear, RoadPaint.Subtle), Boundary = boundary, Existing = allRoads, Obstacles = obstacles,
                Height = ground, HalfWidth = hw, Seed = input.Seed == 0 ? 1u : input.Seed,
            });
            plan.Facts[FactDesirePaths] = wear.Desire;
            plan.Facts[FactWornCorners] = wear.Corners;
            cancel.ThrowIfCancellationRequested();

            // ── 6. Output nets & objects ─────────────────────────────────
            int[] degree = net.Degrees(true);
            int[] carDegree = RoadGraph.Degrees(net, RoadGraph.Car);   // a street stopping where steps go on still gets its turning circle
            string street = settings.S(RoadsIds.StreetAsset, "");
            var junctions = 0;
            for (int n = 0; n < degree.Length; n++)
            {
                if (degree[n] >= 3) junctions++;
                if (degree[n] > 0 && net.Nodes[n].Attach >= 0) attachedCount++;
            }
            float totalLength = 0f, walkLength = 0f;
            var caps = new List<(float2 p, float r)>();
            string capStyle = settings.S(RoadsIds.CapStyle, "asphalt");
            string capSize = settings.S(RoadsIds.CapSize, "auto");
            foreach (var e in net.Edges)
            {
                if (!e.Alive) continue;
                var a = net.Nodes[e.N0];
                var d = net.Nodes[e.N1];
                float3 pa = Lift(a, existing, attachments, ground, follow, meanY);
                float3 pd = Lift(d, existing, attachments, ground, follow, meanY);
                var curve = e.Curve.WithEnds(pa, pd);
                curve.B.y = math.lerp(pa.y, pd.y, 1f / 3f);
                curve.C.y = math.lerp(pa.y, pd.y, 2f / 3f);
                // (s52) On terraced ground the inner control heights put the curve on the built ground at a third and two
                // thirds (a link's landing and ramp, a street over a fading terrace end).
                if (terraceGround != null && terraceGround.Cells > 0 && follow) FitHeights(ref curve, ground);
                long sa = a.Attach, da = d.Attach;
                float st = a.AttachT, dt = d.AttachT;
                bool cap = e.CapAtEnd && e.IsCar && carDegree[e.N1] == 1 && d.Attach < 0;
                if (e.Role == NetRole.Roundabout && site.LeftHandTraffic)
                {
                    // GRG emits roundabout rings counter-clockwise (right-hand traffic).
                    curve = curve.Reversed();
                    (sa, da) = (da, sa); (st, dt) = (dt, st);
                }
                var net0 = new PlannedNet
                {
                    A = curve.A, B = curve.B, C = curve.C, D = curve.D,
                    Role = e.Role,
                    Asset = AssetOf(e, settings, street),
                    Control = RoadRoles.ControlOf(e.Role),
                    StartAttach = sa, EndAttach = da,
                    StartAttachT = sa >= 0 ? st : 0f, EndAttachT = da >= 0 ? dt : 0f,
                    Upgrades = e.AssetOverride.Length > 0 || e.Walk ? Array.Empty<string>() : RoadsSchema.Upgrades(settings, e.Role),
                    FollowTerrain = follow,
                    Category = string.IsNullOrEmpty(e.Category) ? null : e.Category,
                };
                plan.Nets.Add(net0);
                float len = curve.LengthXZ(12);
                if (e.Walk) walkLength += len; else totalLength += len;
                if (e.Walk && e.Trail) TrailPaint(plan, curve, ground, input.Seed);
                if (cap && capStyle != "none")
                {
                    float width = e.AssetOverride == RoadRoles.Gravel ? 8f : 2f * hw(e.Role);
                    float2 tangent = e.Role == NetRole.Roundabout && site.LeftHandTraffic ? -curve.TangentXZ(0f) : curve.TangentXZ(1f);
                    float3 tip = e.Role == NetRole.Roundabout && site.LeftHandTraffic ? curve.A : curve.D;
                    plan.Objects.Add(new PlannedObject
                    {
                        Position = tip,
                        Yaw = math.atan2(tangent.x, tangent.y),
                        Role = ObjectRole.CulDeSacCap,
                        Asset = CapAsset(capStyle, capSize, width),
                        AttachedNet = plan.Nets.Count - 1,
                    });
                    caps.Add((tip.xz, CapRadius(width)));
                }
            }
            // Retaining walls (terraced blocks): a wide quay along the top of each deep cut, at the upper level; the game
            // raises its low side into the concrete quay wall (RESEARCH-NOTES-ADDENDUM 78).
            if (terraceGround != null) Terraces.BlockField.AddQuays(plan, terraceGround.Walls);
            if (roundabout.HasRoundabout && settings.B(RoadsIds.RoundaboutIsland, true) && MathEx.Finite(roundabout.Center)
                && (obstacles == null || !obstacles.BlockedAt(roundabout.Center.xz)) && IsJunction(net, degree, roundabout.Center.xz))
            {
                float2 c = roundabout.Center.xz;
                plan.Objects.Add(new PlannedObject
                {
                    Position = new float3(c.x, follow ? heightAt(c) : meanY, c.y),
                    Role = ObjectRole.RoundaboutIsland,
                    Asset = IslandAsset(roundabout.Radius),
                    Scale = 1f,
                });
            }
            cancel.ThrowIfCancellationRequested();

            if (plan.Nets.Count == 0)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueNothing });
            }
            // (s61) Every marker on the map explains itself: what happened, which setting caused it (named + its value, the UI
            // jumps to it), red = something was dropped / blocked, orange = a warning. Resolve first (bypass, merge, join); this
            // only reports what could not be resolved.
            float3 At3(float2 p) => new float3(p.x, heightAt(p), p.y);
            string spacingValue = FormatValue(RoadsSchema.Num(settings, controls, RoadsIds.JunctionSpacing), "m");
            string gradeValue = FormatValue(RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade), "%");
            if (removedByObstacles > 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueObstacles, Args = new object[] { removedByObstacles }, Cause = RoadsIds.AvoidObstacles, CauseValue = FormatValue(avoid) });
            int bypassed = 0;
            foreach (var d in detours) bypassed += d.Bypassed + d.Trimmed;
            if (bypassed > 0)
                plan.Facts[FactBypassed] = bypassed;
            int blockedShown = 0;
            foreach (var d in detours)
                foreach (var (at, role) in d.RemovedAt)
                {
                    if (blockedShown++ >= MaxBlockedIssues) break;
                    plan.Issues.Add(new PlanIssue
                    {
                        Severity = IssueSeverity.Warning, Key = role == NetRole.PerimeterRoad ? IssuePerimeterBlocked : IssueDroppedObstacle,
                        At = At3(at), Cause = RoadsIds.AvoidObstacles, CauseValue = FormatValue(avoid), Dropped = true,
                    });
                }
            foreach (var p in design.PerimeterClose.OpenAt)
                plan.Issues.Add(new PlanIssue
                {
                    Severity = IssueSeverity.Warning, Key = IssuePerimeterOpen, At = At3(p), Dropped = true,
                    Cause = obstacles != null && !obstacles.IsEmpty ? RoadsIds.AvoidObstacles : RoadsIds.PerimeterRoad,
                    CauseValue = obstacles != null && !obstacles.IsEmpty ? FormatValue(avoid) : settings.S(RoadsIds.PerimeterRoad, "auto"),
                });
            plan.Facts[FactPerimeterClosed] = design.PerimeterClose.Closed;
            plan.Facts[FactPerimeterOpen] = design.PerimeterClose.OpenAt.Count;
            // (s34) What the network design did that the player should know.
            var conn = design.Connectivity;
            if (conn.Dropped > 0)
            {
                float3? at = null;
                if (conn.DroppedAt.Count > 0) at = At3(conn.DroppedAt[0]);
                // The cause: an obstacle near the piece, else the junction spacing on the main roads, else the steepness limit.
                string cause = RoadsIds.JunctionSpacing, causeValue = spacingValue;
                if (conn.DroppedAt.Count > 0 && obstacles != null && NearObstacle(obstacles, conn.DroppedAt[0], 60f)) { cause = RoadsIds.AvoidObstacles; causeValue = FormatValue(avoid); }
                else if (ctx.JunctionSpacing <= 0f) { cause = RoadsIds.MaxGrade; causeValue = gradeValue; }
                plan.Issues.Add(new PlanIssue
                {
                    Severity = IssueSeverity.Warning, Key = IssueIslands, Args = new object[] { conn.Dropped, Math.Round(conn.DroppedLength) },
                    At = at, Cause = cause, CauseValue = causeValue, Dropped = true,
                });
            }
            if (conn.CityUnreached && plan.Nets.Count > 0)
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueNotConnected, Cause = RoadsIds.ConnectExisting, CauseValue = FormatValue(connect) });
            int cutsShown = 0;
            foreach (var at in design.AccessCutAt)
            {
                if (cutsShown++ >= MaxCutIssues) break;
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueJunctionCut, At = at, Cause = RoadsIds.JunctionSpacing, CauseValue = spacingValue });
            }
            if ((terraceLayout != null || (terraceGround != null && terraceGround.Cells > 0)) && follow)
            {
                // (s52) Grades on the ground the build leaves (the terraces and the link ramps), not the hillside before.
                design.Steep.Clear();
                design.SteepestGrade = 0f;
                float maxG = RoadsSchema.Num(settings, controls, RoadsIds.MaxGrade) / 100f;
                foreach (var n in plan.Nets)
                {
                    if (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide) continue;
                    var c = new RoadCurve(n.A, n.B, n.C, n.D);
                    float g = RoadGraph.MaxGrade(c, ground, 16f, 4f);
                    design.SteepestGrade = math.max(design.SteepestGrade, g);
                    if (g <= maxG + 0.005f) continue;
                    float3 at = c.Position(0.5f);
                    at.y = ground(at.xz);
                    design.Steep.Add((g, at));
                }
                design.Steep.Sort((x, y) => y.grade.CompareTo(x.grade));
            }
            if (follow)
            {
                int shown = 0;
                foreach (var (g, at) in design.Steep)
                {
                    if (shown++ >= MaxSteepIssues) break;
                    plan.Issues.Add(new PlanIssue
                    {
                        Severity = IssueSeverity.Warning, Key = IssueSteep, Args = new object[] { Math.Round(g * 100f) }, At = at,
                        Cause = RoadsIds.MaxGrade, CauseValue = gradeValue,
                    });
                }
            }

            // ── 6b. (s34) Pre-zone every new road ───────────────────────
            string zoneId = RoadPreZone.Offered ? RoadPreZone.ZoneId(settings.S(RoadsIds.PreZone, RoadPreZone.None)) : "";
            if (zoneId.Length > 0 && plan.Nets.Count > 0)
            {
                // (s52) Terraces zone only the level rows of each block (the terrace's cells, never the bank).
                RoadPreZone.DepthRule rule = null;
                if (blockOpt != null && terraceGround != null && terraceGround.Cells > 0)
                {
                    // Terraced blocks: a street may climb (its own grade), but its lots lie level across it: the cells from
                    // the road whose ground is level across the block (≤ the cross-fall + 2.5 %, or the flat threshold) and
                    // no steeper along it than the steepest street.
                    float level = math.max(blockOpt.CrossFall + 0.025f, blockOpt.FlatSlope);
                    float along = math.max(level, Terraces.BlockField.ClimbGrade + 0.02f);
                    rule = new RoadPreZone.DepthRule { MaxDepth = blockOpt.Cells, LevelAcross = RoadPreZone.LevelAcross(ground, level, along) };
                    plan.Facts[RoadPreZone.FactDepth] = blockOpt.Cells;
                    plan.Facts[RoadPreZone.FactLevel] = level;
                    plan.Facts[RoadPreZone.FactAlong] = along;
                }
                else if (terraceLayout != null)
                {
                    float level = math.max(terraceOpt.CrossFall + 0.025f, terraceOpt.FlatSlope);
                    rule = new RoadPreZone.DepthRule { MaxDepth = terraceOpt.Cells, Level = RoadPreZone.LevelCell(ground, level) };
                    plan.Facts[RoadPreZone.FactDepth] = terraceOpt.Cells;
                    plan.Facts[RoadPreZone.FactLevel] = level;
                }
                plan.Zoning.AddRange(RoadPreZone.Quads(plan.Nets, site, zoneId, ground, rule));
                plan.Facts[RoadPreZone.Fact] = 1;
            }

            // ── 7. Regions ───────────────────────────────────────────────
            var blocks = new List<RoadBlocks.Block>();
            if (plan.Nets.Count > 0)
            {
                var lines = new List<RoadBlocks.Line>();
                var frontIndex = new SegmentIndex(16f);
                var frontWidths = new List<float>();
                var pts = new List<float3>();
                foreach (var e in net.Edges)
                {
                    // Walkways cross blocks without dividing them (they are footpaths, not streets).
                    if (!e.Alive || e.Walk) continue;
                    pts.Clear();
                    e.Curve.Flatten(pts, null, 4f, 6f);
                    var l = new List<float2>(pts.Count);
                    foreach (var p in pts) l.Add(p.xz);
                    float w = hw(e.Role);
                    lines.Add(new RoadBlocks.Line { Points = l, HalfWidth = w, Kind = RoadBlocks.KindGenerated, Role = e.Role });
                    frontWidths.Add(w);
                    for (int i = 0; i + 1 < l.Count; i++) frontIndex.Add(l[i], l[i + 1], frontWidths.Count - 1);
                }
                foreach (var r in allRoads) AddClipped(lines, r, boundary);
                var along = input.Shape.Attachments;
                blocks = RoadBlocks.Compute(boundary, lines, caps, along, cancel);
                RoadBlocks.Order(blocks, RoadBlocks.PrincipalAxis(ring2));
                float depth = RoadsSchema.Num(settings, controls, RoadsIds.EdgeDepth);
                int edgeKey = 0;
                // (Smarter roads) Grid character "lively": the block in the middle is the district's square
                bool lively = plan.Facts.TryGetValue(FactBrick, out double bricks) && bricks > 0;
                float2 squareAt = lively ? RoadCharacter.SquareAt(boundary) : default;
                float cellArea = gp.SpacingMeters * gp.SpacingMeters;
                int hillParks = 0;
                for (int b = 0; b < blocks.Count; b++)
                {
                    cancel.ThrowIfCancellationRequested();
                    string[] tags;
                    // (smarter roads) a leftover too small or thin to build on is no zone: nothing is made of it
                    if (!blocks[b].RoundaboutIsland && IsSliver(blocks[b].Ring)) continue;
                    if (blocks[b].RoundaboutIsland) tags = new[] { RegionTags.Block, RegionTags.Core };
                    else if (lively && Poly.Contains(blocks[b].Ring, squareAt)) tags = new[] { RegionTags.Block, RegionTags.Green, RegionTags.Core };
                    else if (diagonal != null && RoadCharacter.IsTriangle(blocks[b].Ring, cellArea)) tags = new[] { RegionTags.Block, RegionTags.Green };
                    else if (givesWay && RoadTerrainFit.MeanSlope(blocks[b].Ring, ground) > RoadTerrainFit.HillParkSlope)
                    {
                        tags = new[] { RegionTags.Block, RegionTags.Green };   // too steep for lots: a hillside park
                        hillParks++;
                    }
                    else tags = new[] { RegionTags.Block };
                    plan.Regions.Add(new PlannedRegion { Ring = Poly.To3D(blocks[b].Ring, ground), Tags = tags, Key = "block:" + b });
                }
                plan.Facts[FactHillParks] = hillParks;
                for (int b = 0; b < blocks.Count; b++)
                {
                    cancel.ThrowIfCancellationRequested();
                    foreach (var strip in RoadBlocks.Frontage(blocks[b].Ring, frontIndex, frontWidths, depth))
                        plan.Regions.Add(new PlannedRegion { Ring = Poly.To3D(strip, ground), Tags = new[] { RegionTags.Edge }, Key = "edge:" + edgeKey++ });
                }
            }

            plan.Stats.Add(new PlanStat(StatLength, Math.Round(totalLength), "m"));
            plan.Stats.Add(new PlanStat(StatBlocks, blocks.Count, "count"));
            plan.Stats.Add(new PlanStat(StatJunctions, junctions, "count"));
            if (attachedCount > 0) plan.Stats.Add(new PlanStat(StatConnections, attachedCount, "count"));
            if (walkLength > 0f) plan.Stats.Add(new PlanStat(StatWalkways, Math.Round(walkLength), "m"));
            if (follow && design.SteepestGrade > 0.005f) plan.Stats.Add(new PlanStat(StatSteepest, Math.Round(design.SteepestGrade * 100f), "%"));
            plan.Facts[FactWalkways] = walkLength;
            plan.Facts[FactCityLinks] = design.CityLinks.Links;
            plan.Facts[FactLinksContinued] = design.CityLinks.Continued;
            plan.Facts[FactLinksOnJunction] = design.CityLinks.OnJunction;
            plan.Facts[FactLinks] = conn.Links + conn.CityLinks;
            plan.Facts[FactIslandsDropped] = conn.Dropped;
            plan.Facts[FactAccessCuts] = design.AccessCuts;
            plan.Facts[FactLanes] = design.Lanes;
            plan.Facts[FactSteepest] = design.SteepestGrade;
            plan.Facts[FactLength] = totalLength;
            plan.Facts[FactBlocks] = blocks.Count;
            plan.Facts[FactJunctions] = junctions;
            plan.Facts[FactConnections] = attachedCount;
            plan.Facts[FactRemoved] = removedByObstacles;
            if (terraceLayout != null) TerraceReport(plan, terraceLayout, terraceOpt, terraceGround, net, hw, obstacles, boundary, bankWalks);
            else if (blockOpt != null) BlockReport(plan, blockSurvey, blockOpt, terraceGround, net, hw, obstacles, boundary);
            return plan;
        }

        // s52: hillside terraces
        public const string StatTerraces = "Placemaker.Roads.Stat.Terraces";
        public const string StatTerracedArea = "Placemaker.Roads.Stat.TerracedArea";
        public const string StatCut = "Placemaker.Roads.Stat.Cut";
        public const string StatFill = "Placemaker.Roads.Stat.Fill";
        public const string StatDeepest = "Placemaker.Roads.Stat.DeepestCut";
        public const string StatHighest = "Placemaker.Roads.Stat.HighestFill";
        public const string IssueTerraceSteep = "Placemaker.Roads.Issue.TerraceSteep";
        public const string IssueTerraceEarthwork = "Placemaker.Roads.Issue.TerraceEarthwork";
        public const string IssueTerraceFlat = "Placemaker.Roads.Issue.TerraceFlat";

        /// <summary>Terraced blocks: the earthwork readout, the bank paint and trees (as the terraces').</summary>
        private static void BlockReport(AreaPlan plan, Terraces.BlockField.Survey survey, Terraces.TerraceOptions o, Terraces.TerraceGround g,
            RoadNetwork net, Func<NetRole, float> hw, RoadObstacles obstacles, float2[] boundary)
        {
            plan.Facts["blocks.sloped"] = survey != null && survey.Sloped ? 1 : 0;
            plan.Facts["blocks.slopedShare"] = survey?.SlopedShare ?? 0f;
            plan.Facts["blocks.relief"] = survey?.Relief ?? 0f;
            if (g == null || g.Grid == null || g.Cells == 0) return;
            var earth = g.Earth;
            plan.Facts["blocks.cut"] = earth.CutVolume;
            plan.Facts["blocks.fill"] = earth.FillVolume;
            plan.Facts["blocks.maxCut"] = earth.MaxCut;
            plan.Facts["blocks.maxFill"] = earth.MaxFill;
            plan.Facts["blocks.area"] = g.TerracedArea;
            plan.Facts["blocks.cells"] = g.Cells;
            plan.Facts["blocks.lostTerrace"] = g.LostTerrace;
            plan.Facts["blocks.clashes"] = g.Clashes.Count;
            float wallLen = 0f, wallMax = 0f;
            foreach (var w in g.Walls) { wallLen += w.Length; foreach (var r in w.Rise) wallMax = math.max(wallMax, r); }
            plan.Facts["blocks.walls"] = g.Walls.Count;
            plan.Facts["blocks.wallLength"] = wallLen;
            plan.Facts["blocks.wallHighest"] = wallMax;
            plan.Stats.Add(new PlanStat(StatTerracedArea, Math.Round(g.TerracedArea), "m2"));
            plan.Stats.Add(new PlanStat(StatCut, Math.Round(earth.CutVolume / 10.0) * 10.0, "m³"));
            plan.Stats.Add(new PlanStat(StatFill, Math.Round(earth.FillVolume / 10.0) * 10.0, "m³"));
            plan.Stats.Add(new PlanStat(StatDeepest, Math.Round(earth.MaxCut, 1), "m"));
            plan.Stats.Add(new PlanStat(StatHighest, Math.Round(earth.MaxFill, 1), "m"));
            if (g.Limited) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueTerraceEarthwork, Args = new object[] { Math.Round(o.Earthwork) } });
            int paints = o.Paint ? Terraces.TerraceDressing.Paint(plan, g, boundary, o) : 0;
            int trees = o.Trees ? Terraces.TerraceDressing.Trees(plan, g, boundary, o, net, hw, obstacles) : 0;
            plan.Facts["blocks.paints"] = paints;
            plan.Facts["blocks.trees"] = trees;
            if (paints > 0)
            {
                bool warned = false;
                foreach (var i in plan.Issues) warned |= i.Key == Layers.Common.TerrainPaint.PermanentKey;
                if (!warned) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainPaint.PermanentKey });
            }
        }

        /// <summary>(s52) Terraces: the earthwork readout, what the plan had to give up, the bank paint and trees.</summary>
        private static void TerraceReport(AreaPlan plan, Terraces.TerraceLayout lay, Terraces.TerraceOptions o, Terraces.TerraceGround g,
            RoadNetwork net, Func<NetRole, float> hw, RoadObstacles obstacles, float2[] boundary, int walks)
        {
            var levels = new HashSet<int>();
            foreach (var e in net.Edges) if (e.Alive && e.TerraceStreet >= 0) levels.Add(lay.Streets[e.TerraceStreet].Level);
            int hairpins = 0; float minRadius = float.PositiveInfinity;
            foreach (var r in lay.Links) { hairpins += r.Hairpins; minRadius = math.min(minRadius, r.MinRadius); }
            plan.Facts["terraces.sloped"] = lay.Sloped ? 1 : 0;
            plan.Facts["terraces.levels"] = levels.Count;
            plan.Facts["terraces.streets"] = lay.Streets.Count;
            plan.Facts["terraces.links"] = lay.Links.Count;
            plan.Facts["terraces.hairpins"] = hairpins;
            plan.Facts["terraces.walks"] = walks;
            plan.Facts["terraces.step"] = lay.LevelStep;
            plan.Facts["terraces.spacing"] = lay.Spacing;
            plan.Facts["terraces.refGrade"] = lay.RefGrade;
            if (!float.IsInfinity(minRadius)) plan.Facts["terraces.minLinkRadius"] = minRadius;
            if (levels.Count > 0) plan.Stats.Add(new PlanStat(StatTerraces, levels.Count, "count"));
            if (!lay.Sloped)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueTerraceFlat });
                return;
            }
            if (lay.TooSteep) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = IssueTerraceSteep });
            if (g == null || g.Grid == null) return;
            var earth = g.Earth;
            plan.Facts["terraces.cut"] = earth.CutVolume;
            plan.Facts["terraces.fill"] = earth.FillVolume;
            plan.Facts["terraces.maxCut"] = earth.MaxCut;
            plan.Facts["terraces.maxFill"] = earth.MaxFill;
            plan.Facts["terraces.area"] = g.TerracedArea;
            plan.Facts["terraces.steepestBank"] = g.SteepestBank;
            plan.Facts["terraces.cells"] = g.Cells;
            plan.Facts["terraces.streetGap"] = g.StreetGap;
            plan.Facts["terraces.roadGap"] = g.RoadGap;
            plan.Facts["terraces.roadGapKind"] = g.RoadGapKind;
            plan.Facts["terraces.lostCorridor"] = g.LostCorridor;
            plan.Facts["terraces.lostTerrace"] = g.LostTerrace;
            plan.Facts["terraces.clashes"] = g.Clashes.Count;
            for (int i = 0; i < g.Clashes.Count; i++)
            {
                plan.Facts["terraces.clash" + i + "x"] = g.Clashes[i].x; plan.Facts["terraces.clash" + i + "z"] = g.Clashes[i].y;
                plan.Facts["terraces.clash" + i + "kind"] = g.Clashes[i].z; plan.Facts["terraces.clash" + i + "step"] = g.Clashes[i].w;
            }
            plan.Facts["terraces.streetGapX"] = g.StreetGapAt.x;
            plan.Facts["terraces.streetGapZ"] = g.StreetGapAt.y;
            plan.Facts["terraces.roadGapX"] = g.RoadGapAt.x;
            plan.Facts["terraces.roadGapZ"] = g.RoadGapAt.y;
            if (g.Cells > 0)
            {
                plan.Stats.Add(new PlanStat(StatTerracedArea, Math.Round(g.TerracedArea), "m2"));
                plan.Stats.Add(new PlanStat(StatCut, Math.Round(earth.CutVolume / 10.0) * 10.0, "m³"));
                plan.Stats.Add(new PlanStat(StatFill, Math.Round(earth.FillVolume / 10.0) * 10.0, "m³"));
                plan.Stats.Add(new PlanStat(StatDeepest, Math.Round(earth.MaxCut, 1), "m"));
                plan.Stats.Add(new PlanStat(StatHighest, Math.Round(earth.MaxFill, 1), "m"));
            }
            if (g.Limited) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Info, Key = IssueTerraceEarthwork, Args = new object[] { Math.Round(o.Earthwork) } });
            int paints = o.Paint ? Terraces.TerraceDressing.Paint(plan, g, boundary, o) : 0;
            int trees = o.Trees ? Terraces.TerraceDressing.Trees(plan, g, boundary, o, net, hw, obstacles) : 0;
            plan.Facts["terraces.paints"] = paints;
            plan.Facts["terraces.trees"] = trees;
            if (paints > 0)
            {
                bool warned = false;
                foreach (var i in plan.Issues) warned |= i.Key == Layers.Common.TerrainPaint.PermanentKey;
                if (!warned) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainPaint.PermanentKey });
            }
        }

        /// <summary>(s52) Inner control heights so the curve meets the ground at t = 1/3 and 2/3 (ends fixed).</summary>
        private static void FitHeights(ref RoadCurve c, Func<float2, float> ground)
        {
            float a = c.A.y, d = c.D.y;
            float g1 = ground(c.Position(1f / 3f).xz), g2 = ground(c.Position(2f / 3f).xz);
            if (!math.isfinite(g1) || !math.isfinite(g2)) return;
            c.B.y = 3f * g1 - 1.5f * g2 - (5f / 6f) * a + d / 3f;
            c.C.y = 3f * g2 - 1.5f * g1 - (5f / 6f) * d + a / 3f;
        }

        public static bool PerimeterWanted(string mode, AreaSource source)
        {
            if (mode == "on") return true;
            if (mode == "off") return false;
            // Block and perimeter picks are already bounded by roads.
            return source == AreaSource.Drawn || source == AreaSource.Painted;
        }

        private static NetRole RoleOf(RoadSegmentDef s, RoadPattern pattern, bool loopFamily)
        {
            if (s.IsRoundabout) return NetRole.Roundabout;
            if (s.IsPedestrian) return NetRole.Pathway;
            // Contour: one road type only (GRG: uphill links are flagged as avenues internally).
            if (s.IsAvenue && pattern != RoadPattern.Contour) return NetRole.Avenue;
            if (!loopFamily && ((s.IsCulDeSacEnd && !s.KeepNetwork) || s.IsLocal)) return NetRole.CulDeSac;
            return NetRole.Street;
        }

        internal static Func<NetRole, float> HalfWidths(SettingsBag settings, SiteContext site)
        {
            string street = settings.S(RoadsIds.StreetAsset, "");
            return role =>
            {
                // The width of what will really be placed: the pick, else (automatic) the street pick for
                // street-like roles, else the first automatic prefab this playset has (Four-Lane avenues are 24 m).
                string id = RoleAssetId(role, settings);
                if (Known(site, id, out float w)) return w;
                if (string.IsNullOrEmpty(id))
                {
                    if ((role == NetRole.Street || role == NetRole.PerimeterRoad || role == NetRole.CulDeSac) && Known(site, street, out w)) return w;
                    foreach (var pick in RoadRoles.AutoPicks(role))
                        if (Known(site, pick, out w)) return w;
                }
                return Composer.DefaultHalfWidth(role);
            };
        }

        private static bool Known(SiteContext site, string id, out float halfWidth)
        {
            halfWidth = 0f;
            if (string.IsNullOrEmpty(id) || site.Assets == null || !site.Assets.TryGetValue(id, out var info)) return false;
            if (!(info.NetWidth > 0.5f) || !math.isfinite(info.NetWidth)) return false;
            halfWidth = info.NetWidth * 0.5f;
            return true;
        }

        private static string RoleAssetId(NetRole role, SettingsBag s)
        {
            switch (role)
            {
                case NetRole.Alley: return s.S(RoadsIds.LaneAsset, "");
                case NetRole.Avenue: return s.S(RoadsIds.AvenueAsset, "");
                case NetRole.Roundabout: return s.S(RoadsIds.RoundaboutAsset, "");
                case NetRole.CulDeSac: return s.S(RoadsIds.LocalAsset, "");
                case NetRole.Pathway: return s.S(RoadsIds.PathAsset, "");
                case NetRole.PerimeterRoad:
                    string p = s.S(RoadsIds.PerimeterAsset, "");
                    return p.Length > 0 ? p : s.S(RoadsIds.StreetAsset, "");
                default: return s.S(RoadsIds.StreetAsset, "");
            }
        }

        /// <summary>
        /// The road of a role. An explicit pick falls back to the street pick, then the role's automatic
        /// prefabs. Automatic (empty id) carries the role's preferred vanilla prefabs as fallbacks
        /// (RoadRoles.AutoPicks: Two-Lane streets, Four-Lane avenues, Two-Lane One-Way rings) — AutoAssets
        /// takes the first one this playset has, else the category pool.
        /// </summary>
        public static AssetRef AssetFor(NetRole role, SettingsBag s, string street)
        {
            string id = RoleAssetId(role, s);
            if (role == NetRole.Pathway) return new AssetRef(id);
            var picks = RoadRoles.AutoPicks(role);
            var fb = new List<string>(picks.Length + 1);
            bool streetLike = role == NetRole.PerimeterRoad || role == NetRole.Street || role == NetRole.CulDeSac;
            if (string.IsNullOrEmpty(id))
            {
                // Automatic local / perimeter roads follow an explicit street pick (a gravel village stays gravel).
                if (streetLike && !string.IsNullOrEmpty(street)) return new AssetRef(street, picks);
                return new AssetRef("", picks);
            }
            if (!string.IsNullOrEmpty(street) && street != id) fb.Add(street);
            foreach (var p in picks) if (p != id && !fb.Contains(p)) fb.Add(p);
            return new AssetRef(id, fb.ToArray());
        }

        /// <summary>(s34) The asset of a planned piece: its explicit override (gravel tips / lanes), a walkway's path, else the role's.</summary>
        private static AssetRef AssetOf(RoadEdge e, SettingsBag s, string street)
        {
            if (e.Walk)
            {
                string path = s.S(RoadsIds.PathAsset, "");
                return e.Trail && path.Length == 0 ? Layers.Park.ParkPathNets.Trail("") : new AssetRef(path);
            }
            var normal = AssetFor(e.Role, s, street);
            if (string.IsNullOrEmpty(e.AssetOverride)) return normal;
            var fb = new List<string>();
            if (!string.IsNullOrEmpty(normal.Id) && normal.Id != e.AssetOverride) fb.Add(normal.Id);
            foreach (var f in normal.Fallbacks ?? Array.Empty<string>()) if (!string.IsNullOrEmpty(f) && !fb.Contains(f) && f != e.AssetOverride) fb.Add(f);
            return new AssetRef(e.AssetOverride, fb.ToArray());
        }

        /// <summary>
        /// (s34) Worn ground along a dirt walkway: a feathered dirt paint corridor 5 m wide with a ragged edge.
        /// Extension point for s33's terrain paint toolkit (painted trails): replace this with its trail brush when merged.
        /// </summary>
        private static void TrailPaint(AreaPlan plan, RoadCurve curve, Func<float2, float> heightAt, uint seed)
        {
            var pts = new List<float3>();
            curve.Flatten(pts, null, 3f, 8f);
            if (pts.Count < 2) return;
            const float half = 2.5f;
            var left = new List<float2>();
            var right = new List<float2>();
            for (int i = 0; i < pts.Count; i++)
            {
                float2 a = pts[math.max(0, i - 1)].xz, b = pts[math.min(pts.Count - 1, i + 1)].xz;
                float2 d = math.normalizesafe(b - a);
                float2 n = new float2(-d.y, d.x);
                left.Add(pts[i].xz + n * half);
                right.Add(pts[i].xz - n * half);
            }
            right.Reverse();
            left.AddRange(right);
            var ring = Poly.Clean(left.ToArray(), 0.3f);
            if (ring.Length < 3 || !Poly.IsSimple(ring)) return;
            if (Poly.SignedArea(ring) < 0f) Array.Reverse(ring);
            plan.Paint.Add(new PlannedPaint
            {
                Ring = Poly.To3D(ring, heightAt), Layer = TerrainMaterials.Dirt, Strength = 0.8f, Feather = 1.6f, Noise = 0.9f, NoiseScale = 5f,
                Seed = seed ^ math.hash(new float2(curve.A.x, curve.A.z)),
            });
            // Terrain paint stays when the roads are removed (the shared warning every painting type gives).
            bool warned = false;
            foreach (var i in plan.Issues) warned |= i.Key == Layers.Common.TerrainPaint.PermanentKey;
            if (!warned) plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Warning, Key = Layers.Common.TerrainPaint.PermanentKey });
        }

        /// <summary>CS2 ships turning circles named CulDeSac{Small|Medium|Large|XL}{01 asphalt|02 grass|03 trees}.</summary>
        public static AssetRef CapAsset(string style, string size, float roadWidth)
        {
            string code = style == "grass" ? "02" : style == "trees" ? "03" : "01";
            string[] sizes = { "Small", "Medium", "Large", "XL" };
            int s = size == "small" ? 0 : size == "medium" ? 1 : size == "large" ? 2 : size == "xl" ? 3
                : roadWidth < 7f ? 0 : roadWidth < 14f ? 1 : roadWidth < 22f ? 2 : 3;
            if (code == "01" && s == 3) s = 2; // no asphalt XL variant
            var fallbacks = new List<string>();
            for (int k = s - 1; k >= 0; k--) fallbacks.Add("CulDeSac" + sizes[k] + code);
            return new AssetRef("CulDeSac" + sizes[s] + code, fallbacks.ToArray());
        }

        private static float CapRadius(float roadWidth) => math.max(roadWidth * 0.8f, roadWidth * 0.5f + 4f);

        /// <summary>CS2 roundabout islands {Small|Medium|Large|XL}Roundabout01 by radius (GRG thresholds).</summary>
        public static AssetRef IslandAsset(float radius)
        {
            string size = radius < 10f ? "Small" : radius < 15f ? "Medium" : radius < 20f ? "Large" : "XL";
            return new AssetRef(size + "Roundabout01", "MediumRoundabout01", "SmallRoundabout01");
        }

        private static bool IsJunction(RoadNetwork net, int[] degree, float2 p)
        {
            for (int n = 0; n < net.Nodes.Count; n++)
                if (degree[n] >= 3 && math.distance(net.Nodes[n].Position.xz, p) < 2f) return true;
            return false;
        }

        private static bool HasNodeAttachments(List<BoundaryAttachment> attachments)
        {
            if (attachments == null) return false;
            foreach (var a in attachments) if (a.Kind == AttachmentKind.RoadNode) return true;
            return false;
        }

        /// <summary>Nodes on an outline that is itself made of existing roads count as connected.</summary>
        private static void AnchorOutlineNodes(RoadNetwork net, float2[] boundary, AreaSource source)
        {
            bool outlineIsRoad = source == AreaSource.Block || source == AreaSource.Perimeter;
            foreach (var n in net.Nodes)
                if (outlineIsRoad && Poly.DistanceToBoundary(boundary, n.Position.xz) < 1f) n.Anchored = true;
        }

        private static float3 Lift(RoadNode n, List<ExistingRoad> existing, List<BoundaryAttachment> attachments,
            Func<float2, float> heightAt, bool follow, float meanY)
        {
            float3 p = n.Position;
            if (n.Attach >= 0)
            {
                foreach (var r in existing)
                    if (r.Id == n.Attach) return n.AttachT <= 0f ? r.Curve.A : n.AttachT >= 1f ? r.Curve.D : r.Curve.Position(n.AttachT);
                if (attachments != null)
                    foreach (var a in attachments)
                        if (a.ExternalId == n.Attach && a.Kind == AttachmentKind.RoadNode) return a.Position;
            }
            p.y = follow ? heightAt(p.xz) : meanY;
            return p;
        }

        /// <summary>
        /// Existing road clipped to the area. Pieces stop just beyond the outline (so they cross a road
        /// lying on it and get noded there); roads running along the outline within a small margin stay.
        /// </summary>
        private static void AddClipped(List<RoadBlocks.Line> lines, ExistingRoad r, float2[] boundary)
        {
            var pts = new List<float3>();
            r.Curve.Flatten(pts, null, 4f, 6f);
            bool Keep(float2 p) => Poly.Contains(boundary, p) || Poly.DistanceToBoundary(boundary, p) <= RingClipMargin;
            List<float2> current = null;
            void Flush()
            {
                if (current != null && current.Count >= 2)
                    lines.Add(new RoadBlocks.Line { Points = current, HalfWidth = r.HalfWidth, Kind = RoadBlocks.KindExisting, Role = NetRole.Street });
                current = null;
            }
            for (int i = 0; i < pts.Count; i++)
            {
                float2 p = pts[i].xz;
                bool keep = Keep(p);
                if (i > 0)
                {
                    float2 q = pts[i - 1].xz;
                    bool keptBefore = current != null;
                    if (keep != keptBefore && BoundaryCrossing(boundary, q, p, out float t))
                    {
                        // Extend 0.5 m past the outline crossing.
                        float2 dir = math.normalizesafe(p - q);
                        float2 x = math.lerp(q, p, t);
                        if (keep)
                        {
                            current = new List<float2> { x - dir * (RingClipMargin + 0.5f) };
                        }
                        else
                        {
                            current.Add(x + dir * (RingClipMargin + 0.5f));
                            Flush();
                        }
                    }
                }
                if (keep)
                {
                    if (current == null) current = new List<float2>();
                    current.Add(p);
                }
                else Flush();
            }
            Flush();
        }

        private static bool BoundaryCrossing(float2[] ring, float2 a, float2 b, out float t)
        {
            t = 0f;
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
                if (Seg.Intersect(a, b, ring[i], ring[(i + 1) % ring.Length], out float ta, out _) && ta < best) { best = ta; t = ta; }
            return best <= 1f;
        }

        private static Func<float2, float> SafeHeight(Func<float2, float> h)
        {
            if (h == null) return _ => 0f;
            return p =>
            {
                float v = h(p);
                return math.isfinite(v) ? v : 0f;
            };
        }

        private static float2[] ToArray(List<float3> pts, float y)
        {
            var r = new float2[pts.Count];
            for (int i = 0; i < r.Length; i++) r[i] = pts[i].xz;
            return r;
        }

        private static float2[] CleanRing(float3[] ring)
        {
            if (ring == null) return Array.Empty<float2>();
            var pts = new List<float2>(ring.Length);
            foreach (var p in ring) if (MathEx.Finite(p)) pts.Add(p.xz);
            return CleanRing(pts.ToArray());
        }

        private static float2[] CleanRing(float2[] ring)
        {
            if (ring == null || ring.Length < 3) return Array.Empty<float2>();
            var c = Poly.Clean(ring, 0.05f);
            return c.Length >= 3 ? c : Array.Empty<float2>();
        }
    }
}

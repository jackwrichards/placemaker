// Footpaths meeting a city street (the user, 22 Oct 2026: "every pedestrian entrance makes a crosswalk; let the game do
// the connector").
//
// A path end attached to a road splits the road there (CoursePos.m_Entity = edge, RESEARCH-NOTES 18), and the game puts a
// crosswalk across the road at the new node: a road node with a pathway on it gets General.Crosswalk
// (CompositionSelectSystem.GetNodeFlags, decompiled 1.6.2f1). Splitting also cuts the player's road in two, which is where
// modded roads misbehaved. So only entrances the player marked "with crosswalk" keep the attach. Every other path end
// stops at the street, unattached: every PathwayPrefab has LocalConnect (RequireDeadend, search 4 m past the road's edge
// plus the path's half width; NetInitializeSystem, GenerateEdgesSystem.FindNodeConnections), so the game joins the dead end
// to the road's sidewalk as a middle connection (ConnectedNode), with no node on the road, no crosswalk and no cut.
// The end node keeps the path's half width (+ KerbGap) off the road's edge: a visible path whose round end touches the
// road's outline is cut into the road by the game itself (CourseSplitSystem.CheckEdgeSegment: a circle of the course's
// half width at its end against the road's segment quads), which is the crosswalk again; the round end then reaches the
// sidewalk. Invisible paths never cut roads (MarkerPathway is not in a road's intersect layers): parking connectors end
// right at the kerb (PlanBuilder, s58). The build checks the clearance again with the real prefab widths (BuildService.Emit).
// Invisible footpaths (a car park's walk to the street, painted trails) keep their old join: they draw no crosswalk, and an
// invisible pedestrian path left as a dead end beside the street crashed the game twice in the game runner (native, on the
// car park's preview and on its Apply frame; runs send-entrance-crosswalks-1022-093425 and -094649).
//
// Runs once over the composed plan, after the path joins, so every type's entrances (parks, leisure types, plazas,
// markets, malls, parking footpaths) are treated alike without each layer knowing.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Common
{
    public static class StreetJoins
    {
        /// <summary>How far the round end of a kerb path stays off the road's edge (its full width, sidewalks included), m.</summary>
        public const float KerbGap = 0.25f;
        /// <summary>A crosswalk marker belongs to a path end within this distance (m), or to a path running past it.</summary>
        public const float CrosswalkReach = 10f;
        /// <summary>A marker this close to a path's centre line (m) is that path's entrance.</summary>
        public const float OnPath = 3f;
        /// <summary>Longest straight run added to reach the kerb (m); farther away the end stays where it is.</summary>
        public const float MaxExtend = 12f;

        /// <summary>Applies the rule to every footpath of the plan. Returns the number of ends moved to the kerb.</summary>
        public static int Apply(ComposedPlan plan, SiteContext site)
        {
            if (plan?.Layers == null || site?.NearbyNets == null) return 0;
            var roads = new Dictionary<long, NearbyNet>();
            var paths = new HashSet<long>();
            foreach (var n in site.NearbyNets)
            {
                if (n.Category == NetCategory.Road && n.ExternalId >= 0 && n.Width > 0f) roads[n.ExternalId] = n;
                else if (n.Category == NetCategory.Pathway) paths.Add(n.ExternalId);
            }
            if (roads.Count == 0) return 0;
            int kerb = 0, crossing = 0;
            foreach (var layer in plan.Layers)
            {
                if (layer?.Nets == null) continue;
                foreach (var net in layer.Nets)
                {
                    if (!PathJoins.IsPath(net) || !net.FollowTerrain || IsInvisible(net)) continue;   // decks keep their planned ends
                    for (int end = 0; end < 2; end++)
                    {
                        bool atStart = end == 0;
                        long id = atStart ? net.StartAttach : net.EndAttach;
                        if (id < 0 || paths.Contains(id)) continue;   // a path joining a city path: no street involved
                        var tip = (atStart ? net.A : net.D).xz;
                        if (!roads.TryGetValue(id, out var road))
                        {
                            // Attached to a net the site does not list as a road (a node, an older id): the street it lies by.
                            if (!NearestRoad(roads.Values, tip, out road))
                            {
                                plan.JoinNotes.Add($"an end at ({tip.x:F0}, {tip.y:F0}) attached to net {id}, no captured street within reach: left as it was");
                                continue;
                            }
                            plan.JoinNotes.Add($"an end at ({tip.x:F0}, {tip.y:F0}) attached to net {id} (not a captured street): taken as the street {road.ExternalId} beside it");
                        }
                        if (WantsCrosswalk(net, atStart, plan.Crosswalks)) { crossing++; continue; }
                        ToKerb(net, atStart, road, site.HeightAt, HalfWidth(net, site));
                        kerb++;
                    }
                }
            }
            plan.KerbJoins += kerb;
            plan.CrosswalkJoins += crossing;
            return kerb;
        }

        /// <summary>An invisible pedestrian path (no crosswalk, joins the street as it always did).</summary>
        public static bool IsInvisible(PlannedNet n) =>
            n.Category == AssetCategories.InvisiblePath || (n.Asset.Id ?? "").IndexOf("Invisible", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The path's half width: its asset's when the site knows it, else the role's default.</summary>
        public static float HalfWidth(PlannedNet n, SiteContext site)
        {
            if (!string.IsNullOrEmpty(n.Asset.Id) && site?.Assets != null && site.Assets.TryGetValue(n.Asset.Id, out var info) && info.NetWidth > 0.1f)
                return info.NetWidth * 0.5f;
            return Composer.DefaultHalfWidth(n.Role);
        }

        /// <summary>The road whose edge is nearest to p, within <see cref="Entrances.FrontageReach"/> + 2 m of it.</summary>
        private static bool NearestRoad(IEnumerable<NearbyNet> roads, float2 p, out NearbyNet road)
        {
            road = default;
            float best = Entrances.FrontageReach + 2f;
            bool found = false;
            foreach (var r in roads)
            {
                float d = RoadDistance(r, p) - r.Width * 0.5f;
                if (d < best) { best = d; road = r; found = true; }
            }
            return found;
        }

        /// <summary>A crosswalk entrance near this end: within reach of it, or on the path's half next to it.</summary>
        public static bool WantsCrosswalk(PlannedNet n, bool atStart, List<float2> crosswalks)
        {
            if (crosswalks == null || crosswalks.Count == 0) return false;
            var tip = (atStart ? n.A : n.D).xz;
            foreach (var c in crosswalks)
            {
                if (math.distance(c, tip) <= CrosswalkReach) return true;
                float t = PathJoins.Closest(n, c, out float d);
                if (d <= OnPath && (atStart ? t <= 0.5f : t >= 0.5f)) return true;
            }
            return false;
        }

        /// <summary>
        /// Moves the end to the path's half width + <see cref="KerbGap"/> past the road's edge and drops the attach: back along
        /// the path when the end lies closer (on the road, its sidewalk or touching it), straight on along the end's direction
        /// when it stops short.
        /// </summary>
        public static void ToKerb(PlannedNet n, bool atStart, NearbyNet road, Func<float2, float> heightAt = null, float halfWidth = 0f)
        {
            float target = road.Width * 0.5f + math.max(0f, halfWidth) + KerbGap;
            float Gap(float2 p) => RoadDistance(road, p) - target;
            var tip = atStart ? n.A : n.D;
            float g0 = Gap(tip.xz);
            if (g0 < 0f) Trim(n, atStart, Gap);   // too close (on the road, its sidewalk, touching it): cut back clear of it
            else if (g0 > 0.05f)
            {
                // Short of the kerb: carry the end straight on until it reaches it (never more than MaxExtend).
                var inner = atStart ? n.B : n.C;
                if (math.distancesq(inner.xz, tip.xz) < 0.01f) inner = atStart ? n.D : n.A;
                var dir = math.normalizesafe(tip.xz - inner.xz);
                float hit = -1f, prev = g0;
                for (float s = 0.25f; s <= MaxExtend; s += 0.25f)
                {
                    float g = Gap(tip.xz + dir * s);
                    if (g <= 0f) { hit = s; break; }
                    if (g > prev + 0.01f) break;   // running away from the road
                    prev = g;
                }
                if (hit > 0f)
                {
                    float lo = hit - 0.25f, hi = hit;
                    for (int it = 0; it < 16; it++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Gap(tip.xz + dir * mid) <= 0f) hi = mid; else lo = mid;
                    }
                    var p = tip.xz + dir * lo;
                    float y = heightAt != null ? heightAt(p) : tip.y;
                    if (!math.isfinite(y)) y = tip.y;
                    var delta = new float3(p.x, y, p.y) - tip;
                    if (atStart) { n.A += delta; n.B += delta * 0.5f; }
                    else { n.D += delta; n.C += delta * 0.5f; }
                }
            }
            if (atStart) { n.StartAttach = -1; n.StartAttachT = 0f; }
            else { n.EndAttach = -1; n.EndAttachT = 0f; }
        }

        /// <summary>
        /// The build's last check with the real prefab widths: a free end of a visible path whose round end (its half width)
        /// would touch one of <paramref name="roads"/> is cut back along the path until it clears it by <see cref="KerbGap"/>.
        /// Ends attached to a net are left alone (a crosswalk entrance). Returns the number of ends moved.
        /// </summary>
        public static int ClearOfRoads(PlannedNet n, IEnumerable<NearbyNet> roads, float halfWidth)
        {
            if (n == null || roads == null) return 0;
            int moved = 0;
            for (int end = 0; end < 2; end++)
            {
                bool atStart = end == 0;
                if ((atStart ? n.StartAttach : n.EndAttach) >= 0) continue;
                var tip = (atStart ? n.A : n.D).xz;
                NearbyNet hit = default; float worst = 0f; bool any = false;
                foreach (var r in roads)
                {
                    if (r.Category != NetCategory.Road || r.Width <= 0f) continue;
                    float over = r.Width * 0.5f + halfWidth + KerbGap - RoadDistance(r, tip);
                    if (over > 0.05f && over > worst) { worst = over; hit = r; any = true; }
                }
                if (!any) continue;
                // the path must have room to step back: a path lying along the road is left to the game
                float len = 0f; float3 prev = n.A;
                for (int k = 1; k <= 16; k++) { var p = Composer.Bezier(n.A, n.B, n.C, n.D, k / 16f); len += math.distance(prev.xz, p.xz); prev = p; }
                if (len < worst + 1f) continue;
                long other = atStart ? n.EndAttach : n.StartAttach;
                var probe = new PlannedNet { A = n.A, B = n.B, C = n.C, D = n.D, Role = n.Role, StartAttach = atStart ? 0 : -1, EndAttach = atStart ? -1 : 0 };
                if (!atStart) { probe.StartAttach = -1; probe.EndAttach = 0; }
                Trim(probe, atStart, p => RoadDistance(hit, p) - (hit.Width * 0.5f + halfWidth + KerbGap));
                n.A = probe.A; n.B = probe.B; n.C = probe.C; n.D = probe.D;
                if (atStart) n.EndAttach = other; else n.StartAttach = other;
                moved++;
            }
            return moved;
        }

        /// <summary>Cuts the end back to the first point (from the end inward) where gap(p) ≥ 0. False when none.</summary>
        private static bool Trim(PlannedNet n, bool atStart, Func<float2, float> gap)
        {
            const int samples = 64;
            float outside = -1f;
            for (int k = 1; k <= samples; k++)
            {
                float u = k / (float)samples;
                float t = atStart ? u : 1f - u;
                if (gap(Composer.Bezier(n.A, n.B, n.C, n.D, t).xz) >= 0f) { outside = t; break; }
            }
            if (outside < 0f) return false;
            float inside = atStart ? outside - 1f / samples : outside + 1f / samples;
            for (int it = 0; it < 20; it++)
            {
                float mid = (inside + outside) * 0.5f;
                if (gap(Composer.Bezier(n.A, n.B, n.C, n.D, mid).xz) >= 0f) outside = mid; else inside = mid;
            }
            if (atStart)
            {
                var rest = PathJoins.Split(n, outside);
                n.A = rest.A; n.B = rest.B; n.C = rest.C; n.D = rest.D;
                n.EndAttach = rest.EndAttach; n.EndAttachT = rest.EndAttachT;
            }
            else PathJoins.Split(n, outside);   // n keeps [0, t] and its start attach
            return true;
        }

        /// <summary>XZ distance from p to the road's centre line.</summary>
        public static float RoadDistance(NearbyNet road, float2 p)
        {
            const int samples = 24;
            float best = float.MaxValue, bestT = 0f;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float d = math.distancesq(Composer.Bezier(road.A, road.B, road.C, road.D, t).xz, p);
                if (d < best) { best = d; bestT = t; }
            }
            float lo = math.max(0f, bestT - 1f / samples), hi = math.min(1f, bestT + 1f / samples);
            for (int it = 0; it < 20; it++)
            {
                float m1 = lo + (hi - lo) / 3f, m2 = hi - (hi - lo) / 3f;
                float d1 = math.distancesq(Composer.Bezier(road.A, road.B, road.C, road.D, m1).xz, p);
                float d2 = math.distancesq(Composer.Bezier(road.A, road.B, road.C, road.D, m2).xz, p);
                if (d1 < d2) hi = m2; else lo = m1;
            }
            return math.distance(Composer.Bezier(road.A, road.B, road.C, road.D, (lo + hi) * 0.5f).xz, p);
        }
    }
}

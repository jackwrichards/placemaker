// Placemaker — a net edge's ground surface as polygons, from the game's own geometry (GPL-3.0).
//
// Each edge's two halves (EdgeGeometry) and its two ends at the junctions (Start/EndNodeGeometry, cut like
// Game.Zones.CellBlockJobs cuts zone cells with them): junction corners, roundabouts and dead ends' turning circles come out
// exact. A strip along the edge's curve misses all of these (it ends square at the node's centre), so anything built from
// it grew into turning circles and roundabouts. Used by the area tools' walls (AreaWalls) and the gardens' barriers
// (NeighbourPolishSystem.BarriersIn; an elevated edge's deck too: the ground under an overpass). Read-only; main thread.

using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Placemaker.Core.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace Placemaker.Game.Selection
{
    public static class RoadSurfaces
    {
        /// <summary>
        /// The edge's ground pieces (counter-clockwise rings) added to <paramref name="into"/>; Round marks a piece of a round
        /// end (a turning circle or a roundabout). Ends that are bridges or tunnels are left out (an elevated edge's deck:
        /// DeckPieces). Returns how many were added (0 when the edge has no geometry yet).
        /// </summary>
        public static int Pieces(EntityManager em, Entity edge, List<(float2[] Ring, bool Round)> into)
        {
            if (!em.HasComponent<EdgeGeometry>(edge)) return 0;
            var g = em.GetComponentData<EdgeGeometry>(edge);
            int n = Strip(g.m_Start.m_Left, g.m_Start.m_Right, false, into) + Strip(g.m_End.m_Left, g.m_End.m_Right, false, into);
            var comp = em.HasComponent<Composition>(edge) ? em.GetComponentData<Composition>(edge) : default;
            if (em.HasComponent<StartNodeGeometry>(edge) && KeepNode(em, comp.m_StartNode, false))
                n += NodePiece(em.GetComponentData<StartNodeGeometry>(edge).m_Geometry, into);
            if (em.HasComponent<EndNodeGeometry>(edge) && KeepNode(em, comp.m_EndNode, false))
                n += NodePiece(em.GetComponentData<EndNodeGeometry>(edge).m_Geometry, into);
            return n;
        }

        /// <summary>
        /// An elevated edge's deck as seen from above, in slices about <paramref name="slice"/> m long (a ramp climbs: its low
        /// end is a crack, its high end has room under it), each with how high its road surface is above the ground at its
        /// lowest corner (<paramref name="ground"/>: the ground height at a point); its elevated junction ends whole (tunnel
        /// ends left out). Returns how many were added (0 when the edge has no geometry yet).
        /// </summary>
        public static int DeckPieces(EntityManager em, Entity edge, Func<float3, float> ground, List<(float2[] Ring, float Height)> into, float slice = 8f)
        {
            if (!em.HasComponent<EdgeGeometry>(edge)) return 0;
            var g = em.GetComponentData<EdgeGeometry>(edge);
            int n = Slices(g.m_Start, ground, slice, into) + Slices(g.m_End, ground, slice, into);
            var comp = em.HasComponent<Composition>(edge) ? em.GetComponentData<Composition>(edge) : default;
            var ends = new List<(float2[] Ring, bool Round)>();
            for (int k = 0; k < 2; k++)
            {
                bool start = k == 0;
                if (!(start ? em.HasComponent<StartNodeGeometry>(edge) : em.HasComponent<EndNodeGeometry>(edge)) || !KeepNode(em, start ? comp.m_StartNode : comp.m_EndNode, true)) continue;
                var ng = start ? em.GetComponentData<StartNodeGeometry>(edge).m_Geometry : em.GetComponentData<EndNodeGeometry>(edge).m_Geometry;
                ends.Clear();
                NodePiece(ng, ends);
                float h = float.MaxValue;
                foreach (var c in new[] { ng.m_Left.m_Left, ng.m_Left.m_Right, ng.m_Right.m_Left, ng.m_Right.m_Right, ng.m_Middle })
                    h = math.min(h, math.min(c.a.y - ground(c.a), c.d.y - ground(c.d)));
                foreach (var (ring, _) in ends) into.Add((ring, h));
                n += ends.Count;
            }
            return n;
        }

        /// <summary>One half of an edge (its two side curves) in slices along it, each with its lowest height above the ground.</summary>
        private static int Slices(Segment s, Func<float3, float> ground, float slice, List<(float2[] Ring, float Height)> into)
        {
            float len = math.max(MathUtils.Length(s.m_Left.xz), MathUtils.Length(s.m_Right.xz));
            int count = math.clamp((int)math.ceil(len / math.max(1f, slice)), 1, 64), added = 0;
            var pieces = new List<(float2[] Ring, bool Round)>(1);
            for (int i = 0; i < count; i++)
            {
                var t = new float2((float)i / count, (float)(i + 1) / count);
                var left = MathUtils.Cut(s.m_Left, t);
                var right = MathUtils.Cut(s.m_Right, t);
                pieces.Clear();
                if (Strip(left, right, false, pieces) == 0) continue;
                float h = math.min(math.min(left.a.y - ground(left.a), left.d.y - ground(left.d)), math.min(right.a.y - ground(right.a), right.d.y - ground(right.d)));
                into.Add((pieces[0].Ring, h));
                added++;
            }
            return added;
        }

        /// <summary>A junction end that is not a tunnel nor, but for a deck, a bridge (its own composition says so).</summary>
        private static bool KeepNode(EntityManager em, Entity composition, bool deck)
        {
            if (composition == Entity.Null || !em.HasComponent<NetCompositionData>(composition)) return true;
            var flags = em.GetComponentData<NetCompositionData>(composition).m_Flags.m_General;
            var skip = deck ? CompositionFlags.General.Tunnel : CompositionFlags.General.Elevated | CompositionFlags.General.Tunnel;
            return (flags & skip) == 0;
        }

        /// <summary>One junction end, cut like CellBlockJobs: round ends (turning circles, roundabouts) in three strips.</summary>
        private static int NodePiece(EdgeNodeGeometry n, List<(float2[] Ring, bool Round)> into)
        {
            if (n.m_MiddleRadius > 0f)
            {
                var mid = MathUtils.Lerp(n.m_Right.m_Left, n.m_Right.m_Right, 0.5f);
                mid.d = n.m_Middle.d;
                return Strip(n.m_Left.m_Left, n.m_Left.m_Right, true, into) + Strip(n.m_Right.m_Left, mid, true, into) + Strip(mid, n.m_Right.m_Right, true, into);
            }
            return Strip(n.m_Left.m_Left, n.m_Middle, false, into) + Strip(n.m_Middle, n.m_Right.m_Right, false, into);
        }

        /// <summary>The surface between two side curves (left forward, right back) as one polygon.</summary>
        private static int Strip(Bezier4x3 left, Bezier4x3 right, bool round, List<(float2[] Ring, bool Round)> into)
        {
            var pts = new List<float2>(20);
            Sample(left, true, pts);
            Sample(right, false, pts);
            var ring = Poly.Clean(pts, 0.02f);
            if (ring.Length < 3 || Poly.Area(ring) < 0.05f) return 0;
            foreach (var p in ring) if (!Poly.IsFinite(p)) return 0;
            into.Add((Poly.EnsureCcw(ring), round));
            return 1;
        }

        private static void Sample(Bezier4x3 c, bool forward, List<float2> into)
        {
            float chord = math.distance(c.a.xz, c.d.xz);
            float hull = math.distance(c.a.xz, c.b.xz) + math.distance(c.b.xz, c.c.xz) + math.distance(c.c.xz, c.d.xz);
            int n = hull - chord < 0.03f ? 1 : math.clamp((int)math.ceil(hull / 3f), 4, 12);
            for (int i = 0; i <= n; i++)
            {
                float t = forward ? (float)i / n : 1f - (float)i / n;
                into.Add(MathUtils.Position(c, t).xz);
            }
        }
    }
}

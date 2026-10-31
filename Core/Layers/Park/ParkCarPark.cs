// A park's own car park (makeover, 2026-10-29; the user: "parks with parking lots ... optional everything"). A small lot on
// the longest street frontage, beside the main entrance when there is one, planned by the Parking layer (TransportKit.PlanLot,
// as the sports park does); the park keeps the rest and gets a walk-in gate at the lot's inner edge.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Transport;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public static class ParkCarPark
    {
        public const string LotTooSteepKey = "Placemaker.Park.Issue.LotTooSteep";

        /// <summary>Cuts the lot out of ctx.LayerRegion and plans it; returns the walk-in gate (null: no lot).</summary>
        public static bool Plan(LayerContext ctx, out Gate walk)
        {
            walk = default;
            var full = ctx.LayerRegion;
            if (full == null || full.Area < 7000f) { ctx.Issue(IssueSeverity.Info, TransportKit.NoLotKey); return false; }
            var roads = StreetFrontage.Roads(ctx.Input);
            var edges = StreetFrontage.Edges(full.Ring, roads, 30f);
            if (edges.Count == 0) { ctx.Issue(IssueSeverity.Info, TransportKit.NoLotKey); return false; }
            // the main entrance's edge first, else the longest frontage
            var mains = Entrances.ResolveTyped(ctx.Input, full, 0, EntranceKinds.Park).FindAll(g => g.Kind == EntranceKinds.Main);
            bool hasMain = mains.Count > 0; var main = hasMain ? mains[0] : default;
            // P2-4 (the hill matrix: a wild park on mountains cut its lot 11.5 m into the hill): every frontage and a few spots
            // along each are tried; the lot goes where its ground is flattest (the main entrance's edge and long frontages
            // break ties), and on land where even the flattest spot climbs more than 4 m there is no lot, with a note.
            float depth = 30f;
            float2[] cut = null; float2 at = default, inward = default; float bestScore = float.MaxValue, bestRelief = float.MaxValue;
            for (int i = 0; i < edges.Count; i++)
            {
                var (ea, eb) = edges[i];
                float elen = math.distance(ea, eb);
                var tangent = math.normalizesafe(eb - ea);
                float width = math.clamp(full.Area * 0.06f / depth, 34f, math.min(80f, elen - 16f));
                if (width < 30f) continue;
                bool mainEdge = hasMain && SegmentIndex.DistSq(main.Position, ea, eb) < 16f;
                var spots = new List<float> { elen * 0.25f, elen * 0.5f, elen * 0.75f };
                if (mainEdge)
                {
                    float g = math.dot(main.Position - ea, tangent), off = width * 0.5f + 14f;
                    spots.Add(g + off <= elen - width * 0.5f - 4f ? g + off : g - off);
                }
                foreach (float t0 in spots)
                {
                    float t = math.clamp(t0, width * 0.5f + 4f, elen - width * 0.5f - 4f);
                    var p = ea + tangent * t;
                    full.ClosestOnBoundary(p, out int seg, out _);
                    var inw = full.InwardNormal(seg);
                    var c = LayerRegion.Rect(p + inw * (depth * 0.5f - 2f), tangent, width * 0.5f, depth * 0.5f + 2f);
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int u = 0; u <= 4; u++)
                        for (int v = 0; v <= 4; v++)
                        {
                            var q = p + tangent * ((u / 4f - 0.5f) * width) + inw * (v / 4f * depth);
                            float h = ctx.H(q); lo = math.min(lo, h); hi = math.max(hi, h);
                        }
                    float relief = hi - lo;
                    float score = relief - (mainEdge ? 0.8f : 0f) - elen * 0.002f;
                    if (score < bestScore) { bestScore = score; bestRelief = relief; cut = c; at = p; inward = inw; }
                }
            }
            if (cut == null) { ctx.Issue(IssueSeverity.Info, TransportKit.NoLotKey); return false; }
            ctx.Plan.Facts["lotRelief"] = math.round(bestRelief * 10f) / 10f;
            if (bestRelief > 4f) { ctx.Issue(IssueSeverity.Info, LotTooSteepKey); return false; }
            // a country car park: trees along its edges, kept small
            var lot = TransportKit.PlanLot(ctx, cut, at, TransportKit.Bag(("lotGreen", "edges")), 0x9A4Cu);
            if (lot == null) return false;
            var rest = TransportKit.Subtract(full.Ring, cut, 1200f);
            if (rest == null) return false;
            ctx.LayerRegion = LayerRegion.From(rest);
            var walkIn = ctx.LayerRegion.ClosestOnBoundary(at + inward * (depth + 2f), out int s2, out float t2);
            ctx.Plan.Facts["lotStalls"] = lot.Stalls;
            ctx.Stat("Placemaker.Stat.Parking", lot.Stalls, Keys.UnitCount);
            walk = new Gate { Position = walkIn, Segment = s2, T = t2, Kind = EntranceKinds.Pedestrian, AttachId = -1 };
            return true;
        }
    }
}

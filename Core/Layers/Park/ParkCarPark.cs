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
            int pick = 0; float best = -1f;
            for (int i = 0; i < edges.Count; i++)
            {
                float len = math.distance(edges[i].a, edges[i].b);
                if (hasMain && SegmentIndex.DistSq(main.Position, edges[i].a, edges[i].b) < 16f) len *= 1.6f;
                if (len > best) { best = len; pick = i; }
            }
            var (ea, eb) = edges[pick];
            float elen = math.distance(ea, eb);
            var tangent = math.normalizesafe(eb - ea);
            float depth = 30f;
            float width = math.clamp(full.Area * 0.06f / depth, 34f, math.min(80f, elen - 16f));
            if (width < 30f) { ctx.Issue(IssueSeverity.Info, TransportKit.NoLotKey); return false; }
            float t = elen * 0.5f;
            if (hasMain && SegmentIndex.DistSq(main.Position, ea, eb) < 16f)
            {
                float g = math.dot(main.Position - ea, tangent), off = width * 0.5f + 14f;
                t = g + off <= elen - width * 0.5f - 4f ? g + off : g - off;
            }
            t = math.clamp(t, width * 0.5f + 4f, elen - width * 0.5f - 4f);
            var at = ea + tangent * t;
            full.ClosestOnBoundary(at, out int seg, out _);
            var inward = full.InwardNormal(seg);
            var cut = LayerRegion.Rect(at + inward * (depth * 0.5f - 2f), tangent, width * 0.5f, depth * 0.5f + 2f);
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

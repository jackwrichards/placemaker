// Placemaker — the urban ruins' ground (docs/runbooks/rundown.md). GPL-3.0.
//
// Old concrete everywhere it was poured: the skaters' pads stained and cracked with weeds at their edges; the fallen
// carriageway's asphalt with weedy cracks, faded lane lines and broken concrete where it came down; rubble and dust at the
// pillars' feet; the building's floor with moss and weeds in its joints and stagnant water in the basement; the yard rough
// soil and weeds trodden bare along the ways in.

using System.Collections.Generic;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Ruins;
using Placemaker.Core.Layers.Types.Rundown;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.UrbanRuins
{
    internal static class UrbanRuinsGround
    {
        internal static void Dress(LayerContext ctx, string sub, float decay, float over, List<OrientedRect> pads,
            List<(OrientedRect r, DecayKit.Slab s)> fallen, BuildingRuins.Result building)
        {
            // the big variation is paint (free, permanent): dry grass over the yard, dirt where spans fell, round the building,
            // under the jumps and in blobs where nobody mows
            var region = ctx.LayerRegion;
            var whole = DecayKit.Inset(region, 1f);
            if (whole != null) { TerrainPaint.Patch(ctx, whole, 1, 0.85f, 6f); TerrainPaint.Patch(ctx, whole, 3, 0.4f, 8f); }
            var seats = new List<(float2 c, float r)>();
            foreach (var (r, _) in fallen) seats.Add((r.Center, math.max(r.HalfLength, r.HalfWidth) * 1.3f));
            if (building != null) seats.Add((building.Footprint.Center, math.max(building.Footprint.HalfLength, building.Footprint.HalfWidth) * 1.25f));
            int extra = math.clamp((int)(region.Area / 10000f * (sub == "dirtJumps" ? 12f : 8f)), 3, 40);
            for (int i = 0; i < extra; i++)
            {
                uint h = Hashing.Mix(ctx.Seed, 0xDB10u + (uint)i);
                seats.Add((math.lerp(region.Min, region.Max, new float2(Hashing.Unit(h), Hashing.Unit(Hashing.Mix(h, 1u)))), math.lerp(6f, 14f, Hashing.Unit(Hashing.Mix(h, 2u)))));
            }
            for (int i = 0; i < seats.Count; i++)
            {
                var piece = LeisureKit.Intersect(region, LeisureKit.Blob(seats[i].c, seats[i].r, Hashing.Mix(ctx.Seed, 0xDB20u + (uint)i), 12, 0.35f));
                if (piece != null && piece.Area > 20f) TerrainPaint.Patch(ctx, piece.Ring, 3, 0.6f + 0.3f * Hashing.Unit(Hashing.Mix(ctx.Seed, 0xDB30u + (uint)i)), 4f);
            }

            var gr = new RundownGround(ctx, 0x0B5Du);
            if (!gr.Ready) return;
            float weeds = math.saturate(over * 1.1f);

            foreach (var pad in pads)
                gr.Slab(new List<float2[]> { pad.Ring }, null, pad.Ring, false, weeds * 0.6f, decay);
            foreach (var (r, s) in fallen)
            {
                gr.Slab(s.Shards, s.Cracks, r.Ring, true, weeds, 1f);
                // what is left of the lane lines
                foreach (var w in new[] { 0f, -r.HalfWidth * 0.5f, r.HalfWidth * 0.5f })
                    for (float u = -r.HalfLength + 2f; u < r.HalfLength - 2f; u += 6f)
                    {
                        uint h = Hashing.Mix(ctx.Seed, (int)(u * 10f + r.Center.x), (int)(w * 10f + r.Center.y));
                        if (Hashing.Unit(h) < 0.25f + 0.4f * decay) continue;
                        gr.Exact("fadedLine", r.Local(u, w), r.Axis, w == 0f ? 3f : 2.4f, 0.45f + 0.4f * Hashing.Unit(Hashing.Mix(h, 1u)), "paint:lane");
                    }
                gr.Rubble(r.Center, math.min(r.HalfLength, r.HalfWidth), 0.9f);
            }
            if (building != null)
            {
                gr.Slab(building.Floor.Shards, building.Floor.Cracks, building.Footprint.Ring, false, weeds, 1f);
                if (building.Pit != null)
                {
                    var pit = Poly.EnsureCcw((float2[])building.Pit.Clone());
                    gr.Pool(Brushes.Inset(pit, 0.8f) ?? pit, true);
                    Brushes.EdgeStrip(gr.B, pit, "soilSpill", new StripOptions { Side = 1, Closed = true, Gaps = 0.3f, Opacity = new float2(0.5f, 0.85f), Cause = "pit:rim" });
                }
                foreach (var p in building.Rubble) gr.Rubble(p, 3.5f, 1f);
            }

            // the yard
            var look = new RundownGround.GroundLook
            {
                Bare = sub == "dirtJumps" ? 0.4f : 0.22f, Weeds = 0.35f + 0.4f * over, Dry = 0.3f, Grit = 0.12f, Stones = 0.06f, Mottle = 0.16f,
                Moss = 0.03f, Litter = sub == "diySpot" || sub == "parkour" ? 0.08f : 0.04f, EdgeWeeds = 0.55f,
            };
            float Worn(float2 p)
            {
                float w = 0f;
                foreach (var pad in pads) w = math.max(w, 1f - math.saturate(Edge(pad, p) / 5f));
                return w * 0.8f;
            }
            gr.Ground(gr.Soil(), look, Worn);
            foreach (var o in ctx.Plan.Objects)
            {
                if (o == null) continue;
                string cat = o.Category ?? "";
                var p = o.Position.xz;
                if (cat == AssetCategories.RuinsPillar && building == null) gr.Rubble(p, 2.2f, 0.5f);
                else if (cat == AssetCategories.RuinsJunk) gr.Junk(p, 1.2f);
                else if (o.Role == ObjectRole.Bench) Brushes.At(gr.B, p, new float2(1f, 0f), "litterMix", 3.5f, new float2(0.6f, 0.9f), 0, ColourLevel.Any, "bench:litter");
            }
            foreach (var n in ctx.Plan.Nets)
                if (n != null && (n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide))
                    Brushes.EdgeStrip(gr.B, new[] { n.A.xz, n.D.xz }, "pathEdgeWear", new StripOptions { Side = 1, Offset = 0.8f, Gaps = 0.3f, Opacity = new float2(0.5f, 0.8f), Cause = "path:edge" });
            foreach (var run in AbandonedLotGround.FenceRuns(ctx)) gr.FenceLine(run, false, weeds);
            gr.Report(ctx);
        }

        private static float Edge(OrientedRect r, float2 p)
        {
            var d = p - r.Center;
            float u = math.abs(math.dot(d, r.Axis)) - r.HalfLength, v = math.abs(math.dot(d, r.Side)) - r.HalfWidth;
            return math.length(math.max(new float2(u, v), 0f));
        }
    }
}

// Placemaker — the construction site's ground (docs/runbooks/rundown.md). GPL-3.0.
//
// A site in work is churned by wheels and buckets: muddy twin ruts along the haul road and round the works, tyre tread carried
// out through the gate onto the street, soil spilled at the foot of every heap and along the dig's rim, standing water in the
// bottom of the dig and in the ruts, gravel and litter round the cabins, oil where the machines stand, broken concrete and
// brick dust over a demolition. Each subtype reads differently because its causes differ, not because of an amount.

using System.Collections.Generic;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Rundown;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.ConstructionSite
{
    internal static class ConstructionGround
    {
        internal static void Dress(LayerContext ctx, string sub, float2 gate, float2 inward, bool hasGate, List<List<float2>> tracks,
            float2[] works, IReadOnlyList<float2[]> mud, float2 rubbleFocus, float rubbleR)
        {
            if (!Placemaker.Core.Decals.DressingControls.GroundDecalsOn(ctx.Settings)) return;   // the player switched ground decals off
            var gr = new RundownGround(ctx, 0xC0A5u, 280f);
            if (!gr.Ready) return;
            var region = ctx.LayerRegion;

            // the whole yard: trodden soil and grit over the site's cover, darker and wetter where the wheels go
            var look = new RundownGround.GroundLook
            {
                Bare = 0.06f, Weeds = sub == "earthworks" ? 0.04f : 0.08f, Dry = 0f, Grit = sub == "compound" ? 0.35f : 0.25f,
                Stones = 0.12f, Mottle = 0.18f, Litter = 0.03f, EdgeWeeds = 0.95f, EdgeReach = 4f, Soil = sub == "demolition" ? "dust" : "soil",
            };
            var soil = gr.Soil();
            gr.Ground(soil, look);
            // the mud zones (works, haul road, gate apron): soil spills and churned ground
            foreach (var m in mud)
            {
                if (m == null || m.Length < 3) continue;
                var a = BrushArea.Of(Poly.EnsureCcw((float2[])m.Clone()));
                Brushes.Scatter(gr.B, a, "soilSpill", 0.35f, new ScatterOptions { Weight = p => 0.4f + 0.6f * ValueNoise.Clumps(p, 9f, Hashing.Mix(ctx.Seed, 0x5011u)), Opacity = new float2(0.55f, 0.9f), Clump = 3f, Spill = 1.5f, Cause = "mud:spill" });
                Brushes.Scatter(gr.B, a, "mudPatch", 0.12f, new ScatterOptions { Weight = p => ValueNoise.Clumps(p, 12f, Hashing.Mix(ctx.Seed, 0x5012u)), Opacity = new float2(0.4f, 0.7f), Clump = 2f, Cause = "mud:wet" });
            }
            // the haul roads: muddy twin ruts, and the tread carried out through the gate onto the street
            foreach (var t in tracks)
                if (t != null && t.Count >= 2) gr.Ruts(t, 2.1f, true);
            if (hasGate) gr.MudOut(gate, inward, 18f);

            // the works
            if (works != null && works.Length >= 3)
            {
                var ring = Poly.EnsureCcw((float2[])works.Clone());
                // the rim: soil kicked over the edge by the buckets
                Brushes.EdgeStrip(gr.B, ring, "soilSpill", new StripOptions { Side = -1, Offset = 1.5f, Closed = true, Gaps = 0.3f, Opacity = new float2(0.55f, 0.9f), Cause = "works:rim" });
                if (sub == "foundation" || sub == "earthworks")
                {
                    // water in the bottom of the dig: in its lowest corner (the far side from the ramp) and in the ruts
                    var c = Poly.Centroid(ring);
                    float low = float.MaxValue; float2 lowAt = c;
                    foreach (var q in ring) { var p = math.lerp(c, q, 0.7f); float h = ctx.H(p); if (h < low) { low = h; lowAt = p; } }
                    gr.Puddle(lowAt, 9f, false);
                    gr.Puddle(math.lerp(lowAt, c, 0.45f), 6f, false);
                    var inside = BrushArea.Of(ring);
                    Brushes.Scatter(gr.B, inside, "tyreTrack", 0.1f, new ScatterOptions { Opacity = new float2(0.6f, 0.9f), Scale = new float2(1f, 1.6f), Clump = 3f, Loners = 0.3f, Cause = "works:tracks" });
                    Brushes.Scatter(gr.B, inside, "mudPatch", 0.18f, new ScatterOptions { Opacity = new float2(0.45f, 0.75f), Clump = 2f, Cause = "works:mud" });
                }
            }
            if (sub == "demolition") gr.RubbleField(gr.Site, rubbleFocus, rubbleR, 0.9f);

            // from what the site placed
            foreach (var o in ctx.Plan.Objects)
            {
                if (o == null) continue;
                string cat = o.Category ?? "", id = o.Asset.Id ?? "";
                var p = o.Position.xz;
                if (cat == AssetCategories.ConstructionPile || cat == AssetCategories.ConstructionRubble)
                {
                    bool sand = id.IndexOf("Sand", System.StringComparison.Ordinal) >= 0;
                    bool rubble = cat == AssetCategories.ConstructionRubble || id.IndexOf("Rubble", System.StringComparison.Ordinal) >= 0;
                    uint h = Hashing.Mix(ctx.Seed, (int)(p.x * 3f), (int)(p.y * 3f));
                    var dir = new float2(math.cos(h), math.sin(h));
                    if (rubble) gr.Rubble(p, 3.5f, 0.8f);
                    else Brushes.At(gr.B, p, dir, sand ? "sandSpill" : "soilSpill", 7f, new float2(0.6f, 0.9f), 2, ColourLevel.Any, "heap:spill");
                }
                else if (cat == AssetCategories.ConstructionMachine || cat == AssetCategories.ConstructionGenerator)
                    Brushes.At(gr.B, p, new float2(1f, 0f), "oilStainOld", 3.5f, new float2(0.45f, 0.7f), 1, ColourLevel.Any, "machine:oil");
                else if (cat == AssetCategories.ConstructionCabin || cat == AssetCategories.ConstructionToilet)
                    Brushes.At(gr.B, p, new float2(0.2f, 1f), "pebbles", 6f, new float2(0.55f, 0.85f), 1, ColourLevel.Any, "compound:gravel");
                else if (cat == AssetCategories.ConstructionMaterial)
                    Brushes.At(gr.B, p, new float2(1f, 0.2f), "pebbles", 4f, new float2(0.45f, 0.75f), 0, ColourLevel.Any, "materials:grit");
            }
            // a few puddles in the ruts of the yard
            var rng = new BrushRng(Hashing.Mix(ctx.Seed, 0xC9DDu));
            int n = (int)math.ceil(3f * math.min(2.5f, region.Area / 12000f));
            foreach (var t in tracks)
                for (int i = 0; i < n && t != null && t.Count >= 2; i++)
                {
                    int seg = rng.I(t.Count - 1);
                    var p = math.lerp(t[seg], t[seg + 1], rng.F());
                    gr.Puddle(p, rng.R(3.5f, 6f), false);
                }
            gr.Report(ctx);
        }
    }
}

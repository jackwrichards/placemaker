// Placemaker — the abandoned lot's ground, one look per kind of lot (docs/runbooks/rundown.md). GPL-3.0.
//
// The user, 2026-10-29: "neglect ... changes the entire vibe of everything ... its own entire version". So each lot type
// gets its own ground, not a grime amount: the overgrown lot is weeds over weeds with moss on its slab, the rubble field is
// brick dust and broken concrete, the demolished block is levelled dust with the machines' ruts, the squatter camp is trodden
// bare round the fire with litter, the fenced wasteland is a graded gravel pad with puddles, the old car park is cracked
// asphalt with faded bays and weeds in every crack, the burnt-out lot is char and ash, the empty lot is dry grass with a
// shortcut worn across it. Causes are read from what the lot placed (piles, junk, fire, shelters, fences, slabs).

using System.Collections.Generic;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Rundown;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Ruins
{
    internal static class AbandonedLotGround
    {
        internal sealed class Story
        {
            public string Sub;
            public float Rubble, Over, Junk;
            public float2 Focus;
            public float RubbleR;
            public bool HasCamp;
            public float2 Camp;
            public float CampR;
            public readonly List<(OrientedRect r, DecayKit.Slab s, bool asphalt)> Slabs = new List<(OrientedRect, DecayKit.Slab, bool)>();
            public readonly List<float2[]> Shortcuts = new List<float2[]>();
            public readonly List<float2[]> Ruts = new List<float2[]>();
            public readonly List<float2[]> Pads = new List<float2[]>();
            public float2 BurnSeat;
            public float BurnR;
            public bool HasBays;
            public readonly List<(float2 c, float r)> Mounds = new List<(float2, float)>();
            public readonly List<float2> Spills = new List<float2>();
            public float2[] TipLine;
            public OrientedRect Bays;
        }

        internal static RundownGround.GroundLook LookOf(string sub, float over)
        {
            var g = new RundownGround.GroundLook();
            switch (sub)
            {
                // (the first game pictures: the lots still read as lawn; a rundown lot is weeds and soil, the grass the exception)
                case "overgrown":
                    g.Bare = 0.08f; g.Weeds = 0.6f; g.Dry = 0.2f; g.Grit = 0.04f; g.Mottle = 0.2f; g.Moss = 0.08f; g.Litter = 0.03f; g.EdgeWeeds = 0.25f; break;
                case "rubbleField":
                    g.Soil = "dust"; g.Bare = 0.6f; g.Weeds = 0.2f; g.Dry = 0.18f; g.Grit = 0.32f; g.Stones = 0.2f; g.Mottle = 0.14f; g.Litter = 0.03f; g.EdgeWeeds = 0.75f; break;
                case "demolished":
                    g.Soil = "dust"; g.Bare = 0.65f; g.Weeds = 0.07f; g.Dry = 0.1f; g.Grit = 0.3f; g.Stones = 0.08f; g.Mottle = 0.12f; g.EdgeWeeds = 0.9f; g.EdgeReach = 6f; break;
                case "squatterCamp":
                    g.Bare = 0.45f; g.Weeds = 0.45f; g.Dry = 0.32f; g.Grit = 0.14f; g.Mottle = 0.16f; g.Litter = 0.16f; g.EdgeWeeds = 0.55f; break;
                case "fencedWasteland":
                    g.Soil = "gravel"; g.Bare = 0.3f; g.Weeds = 0.32f; g.Dry = 0.26f; g.Grit = 0.24f; g.Stones = 0.07f; g.Mottle = 0.12f; g.EdgeWeeds = 0.9f; g.EdgeReach = 9f; break;
                case "brokenCarPark":
                    g.Bare = 0.22f; g.Weeds = 0.65f; g.Dry = 0.35f; g.Grit = 0.16f; g.Mottle = 0.14f; g.Litter = 0.08f; g.EdgeWeeds = 0.5f; break;
                case "burntOut":
                    g.Bare = 0.45f; g.Weeds = 0.24f; g.Dry = 0.45f; g.Grit = 0.16f; g.Mottle = 0.14f; g.Litter = 0.05f; g.EdgeWeeds = 0.7f; break;
                case "emptyLot":
                    g.Bare = 0.2f; g.Weeds = 0.4f; g.Dry = 0.55f; g.Grit = 0.08f; g.Mottle = 0.2f; g.Litter = 0.06f; g.EdgeWeeds = 0.6f; g.EdgeReach = 10f; break;
            }
            // the player's Overgrowth moves the balance between soil and weeds (the style's look stays its own)
            float k = math.lerp(0.55f, 1.45f, math.saturate(over));
            g.Weeds *= k; g.Bare *= math.lerp(1.3f, 0.75f, math.saturate(over));
            return g;
        }

        internal static void Dress(LayerContext ctx, Story st)
        {
            if (!Placemaker.Core.Decals.DressingControls.GroundDecalsOn(ctx.Settings)) return;   // the player switched ground decals off
            if (ctx.B("groundPaint")) Paint(ctx, st, math.clamp(ctx.I("paintLayer"), 1, 4));
            var gr = new RundownGround(ctx, 0x6A0Du);
            if (!gr.Ready) return;
            string sub = st.Sub;
            float weeds = math.saturate(st.Over * (sub == "overgrown" ? 1.15f : sub == "demolished" || sub == "burntOut" ? 0.45f : 0.85f));

            // the hard ground first (its passes keep the soil passes off it)
            foreach (var (r, s, asphalt) in st.Slabs)
                gr.Slab(s.Shards, s.Cracks, r.Ring, asphalt, weeds, sub == "demolished" ? 0.5f : 1f);
            foreach (var pad in st.Pads) gr.Hard.Add(pad);

            // worn ground: the shortcuts, the camp
            float Worn(float2 p)
            {
                float w = 0f;
                foreach (var line in st.Shortcuts) w = math.max(w, 1f - math.saturate(Dist(line, p) / 4f));
                if (st.HasCamp) w = math.max(w, 1f - math.saturate((math.distance(p, st.Camp) - st.CampR * 0.6f) / (st.CampR + 4f)));
                return w;
            }
            var soil = gr.Soil();
            gr.Ground(soil, LookOf(sub, st.Over), Worn);

            // the story's own causes
            if (sub == "rubbleField")
            {
                foreach (var (c, r) in st.Mounds) { gr.Rubble(c, r * 0.8f, 1f); gr.RubbleField(BrushArea.Of(LayerRegion.Circle(c, r * 2.2f, 14)), c, r * 2.2f, 0.9f); }
                foreach (var p in st.Spills) gr.Rubble(p, 2f, 0.6f);
                if (st.TipLine != null) gr.Ruts(st.TipLine, 2.1f, true);
            }
            else if (sub == "demolished" || sub == "burntOut")
                gr.RubbleField(soil, st.Focus, st.RubbleR, st.Rubble * (sub == "demolished" ? 0.6f : 1f));
            if (sub == "burntOut") gr.BurntArea(gr.Site, st.BurnSeat, st.BurnR);
            if (st.HasBays) FadedBays(gr, st.Bays, ctx.Seed);
            foreach (var line in st.Shortcuts) gr.Shortcut(line);
            foreach (var line in st.Ruts) gr.Ruts(line, 1.8f, sub == "demolished");
            if (st.HasCamp)
                Brushes.At(gr.B, st.Camp, new float2(1f, 0.3f), "barePatch", st.CampR * 2.2f, new float2(0.55f, 0.8f), 2, ColourLevel.Any, "camp:trodden");

            // from what the lot placed: rubble piles, junk, fires, shelters, fences
            foreach (var o in ctx.Plan.Objects)
            {
                if (o == null) continue;
                string cat = o.Category ?? "", id = o.Asset.Id ?? "";
                var p = o.Position.xz;
                if (cat == AssetCategories.RuinsRubble) { if (sub == "burntOut") { gr.Fire(p, 4f * o.Scale); gr.Fire(p + new float2(1.5f, -1f), 3f); } else if (o.Scale >= 0.9f) gr.Rubble(p, 4.2f * o.Scale, st.Rubble + 0.4f); }
                else if (cat == AssetCategories.RuinsJunk)
                {
                    gr.Junk(p, 1.4f);
                    // P2-4: now and then the rubbish was set alight: a scorch beside the heap
                    if (sub != "burntOut" && Hashing.Unit(Hashing.Mix(o.Variation, 0xF1E5u)) < 0.12f) gr.Fire(p + new float2(1.6f, 0.8f), 2.4f);
                }
                else if (cat == AssetCategories.CampingFire || id.StartsWith("BarrelFire", System.StringComparison.Ordinal)) gr.Fire(p, 1.8f);
                else if (cat == AssetCategories.RuinsShelter) Brushes.At(gr.B, p, new float2(0.3f, 1f), "litterMix", 4.5f, new float2(0.7f, 1f), 1, ColourLevel.Any, "camp:litter");
            }
            foreach (var run in FenceRuns(ctx)) gr.FenceLine(run, false, weeds);

            // water: puddles in the soft and sunken places (the gravel pad and the car park hold the most)
            int puddles = sub == "fencedWasteland" || sub == "brokenCarPark" ? 7 : sub == "demolished" || sub == "rubbleField" ? 5 : sub == "overgrown" ? 2 : 3;
            puddles = (int)math.ceil(puddles * math.min(2.5f, ctx.LayerRegion.Area / 12000f));
            var rng = new BrushRng(Hashing.Mix(ctx.Seed, 0x9DDu));
            var region = ctx.LayerRegion;
            for (int i = 0, tries = 0; i < puddles && tries < puddles * 12; tries++)
            {
                var p = math.lerp(region.Min, region.Max, new float2(rng.F(), rng.F()));
                if (!region.Contains(p) || region.BoundaryDistance(p, 8f) < 5f) continue;
                bool onSlab = false;
                foreach (var h in gr.Hard) if (Geometry.Poly.Contains(h, p)) { onSlab = true; break; }
                if ((sub == "brokenCarPark") != onSlab && rng.F() < 0.75f) continue;
                gr.Puddle(p, rng.R(4f, 9f), sub == "overgrown" || sub == "burntOut");
                i++;
            }
            gr.Report(ctx);
        }

        /// <summary>The car park's bays: rows either side of each aisle, the lines worn and broken.</summary>
        private static void FadedBays(RundownGround gr, OrientedRect r, uint seed)
        {
            const float period = 16f, bayW = 2.6f;
            for (float c = -math.floor((r.HalfWidth - 8f) / period) * period; c <= r.HalfWidth - 8f; c += period)
                for (float u = -r.HalfLength + 2f; u <= r.HalfLength - 2f; u += bayW)
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        uint h = Hashing.Mix(seed, (int)(u * 10f), (int)((c + sgn * 4f) * 10f));
                        if (Hashing.Unit(h) < 0.3f) continue;   // a line worn away completely
                        // one faded piece per bay line (a strip run per line cost five instances with its caps)
                        var a = r.Local(u, c + sgn * 3.1f); var b = r.Local(u, c + sgn * 8f);
                        gr.Exact("fadedLine", (a + b) * 0.5f, b - a, 4.9f, 0.28f + 0.3f * Hashing.Unit(Hashing.Mix(h, 1u)), "paint:bay");
                    }
        }

        /// <summary>The fence's panels joined into runs (one strip per run, not one per 8 m panel with caps at both ends).</summary>
        internal static List<float2[]> FenceRuns(LayerContext ctx)
        {
            var runs = new List<List<float2>>();
            foreach (var n in ctx.Plan.Nets)
            {
                if (n == null || n.Role != NetRole.Fence) continue;
                float2 a = n.A.xz, b = n.D.xz;
                var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                if (last != null && math.distancesq(last[last.Count - 1], a) < 0.25f) last.Add(b);
                else runs.Add(new List<float2> { a, b });
            }
            var o = new List<float2[]>();
            foreach (var r in runs) o.Add(r.ToArray());
            return o;
        }

        /// <summary>
        /// The big, soft variation is terrain paint (free, permanent): the lot gone to dry grass, dirt where it is worn,
        /// dumped on or levelled. Decals add the edges and the detail on top. Paint is the city's (it stays after removal; the
        /// lot's "Dirt ground" toggle says so).
        /// </summary>
        internal static void Paint(LayerContext ctx, Story st, int dirtLayer)
        {
            var region = ctx.LayerRegion;
            string sub = st.Sub;
            // dry grass over the whole lot (layer 1), lighter where it is green and wild
            var whole = DecayKit.Inset(region, 1f);
            if (whole != null)
            {
                TerrainPaint.Patch(ctx, whole, 1, 0.85f, 6f);
                // and a thin film of earth through the grass everywhere (the first pictures: a rundown lot still read as a lawn)
                // (the coordinator, round 7: "lawn everywhere") the whole lot is bare, trampled earth; green only survives at the fence and corners
                // (round 9: a 6-9 m inset left a mown-looking green frame) earth to within a couple of metres of the fence, a ragged edge
                var core = DecayKit.Inset(region, 2f);
                TerrainPaint.Patch(ctx, core ?? whole, dirtLayer, sub == "overgrown" ? 0.35f : sub == "emptyLot" ? 0.5f : sub == "brokenCarPark" ? 0.7f : 1f, 4f);
            }
            float share = sub switch
            {
                "demolished" => 0.85f, "rubbleField" => 0.7f, "burntOut" => 0.65f, "fencedWasteland" => 0.6f, "squatterCamp" => 0.4f,
                "brokenCarPark" => 0.3f, "emptyLot" => 0.4f, _ => 0.15f,
            };
            // dirt in blobs of blobs, never one even film; heaviest round the focus
            float ha = region.Area / 10000f;
            int blobs = math.clamp((int)(ha * 14f * share) + 2, 2, 60);
            for (int i = 0; i < blobs; i++)
            {
                uint h = Hashing.Mix(ctx.Seed, 0xB10Bu + (uint)i * 13u);
                float2 c;
                if (Hashing.Unit(h) < 0.5f) c = st.Focus + (new float2(Hashing.Unit(Hashing.Mix(h, 1u)), Hashing.Unit(Hashing.Mix(h, 2u))) - 0.5f) * math.max(20f, st.RubbleR * 2f);
                else c = math.lerp(region.Min, region.Max, new float2(Hashing.Unit(Hashing.Mix(h, 3u)), Hashing.Unit(Hashing.Mix(h, 4u))));
                float r = math.lerp(6f, 18f, Hashing.Unit(Hashing.Mix(h, 5u))) * math.sqrt(math.max(0.3f, share)) * 1.4f;
                var piece = LeisureKit.Intersect(region, LeisureKit.Blob(c, r, Hashing.Mix(h, 6u), 12, 0.35f));
                if (piece != null && piece.Area > 20f) TerrainPaint.Patch(ctx, piece.Ring, dirtLayer, 0.6f + 0.35f * Hashing.Unit(Hashing.Mix(h, 7u)), 4f);
            }
            // the dark leaf-litter floor of the scrub (layer 2) under every thicket: cells of 9 m holding two or more bushes or a tree
            var cells = new Dictionary<int2, (float2 sum, int n)>();
            foreach (var o in ctx.Plan.Objects)
            {
                if (o == null || (o.Role != ObjectRole.Bush && o.Role != ObjectRole.Tree)) continue;
                var p = o.Position.xz;
                var key = new int2((int)math.floor(p.x / 9f), (int)math.floor(p.y / 9f));
                cells.TryGetValue(key, out var c);
                cells[key] = (c.sum + p, c.n + (o.Role == ObjectRole.Tree ? 2 : 1));
            }
            int painted = 0;
            foreach (var kv in cells)
            {
                if (kv.Value.n < 2 || painted > 320) continue;
                var c = kv.Value.sum / math.max(1, kv.Value.n);
                var piece = LeisureKit.Intersect(region, LeisureKit.Blob(c, 4f + math.min(4f, kv.Value.n), Hashing.Mix(ctx.Seed, (uint)(kv.Key.x * 73856093) ^ (uint)(kv.Key.y * 19349663)), 10, 0.35f));
                if (piece != null && piece.Area > 10f) { TerrainPaint.Patch(ctx, piece.Ring, 2, sub == "overgrown" ? 0.85f : 0.6f, 3f); painted++; }
            }
            if (st.HasCamp)
            {
                var camp = LeisureKit.Intersect(region, LeisureKit.Blob(st.Camp, st.CampR + 4f, Hashing.Mix(ctx.Seed, 0xCA4Fu), 14, 0.25f));
                if (camp != null) TerrainPaint.Patch(ctx, camp.Ring, dirtLayer, 0.9f, 3f);
            }
        }

        private static float Dist(float2[] line, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i]; var b = line[i + 1];
                var ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.max(1e-4f, math.dot(ab, ab)));
                best = math.min(best, math.distance(p, a + ab * t));
            }
            return best;
        }
    }
}

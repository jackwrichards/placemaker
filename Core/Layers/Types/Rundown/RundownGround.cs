// Placemaker — the ground of the city's forgotten places (abandoned lots, ruins, construction sites, leftover strips),
// laid with the shared decal library while the layer plans (docs/runbooks/rundown.md). GPL-3.0.
//
// The sports park designs its ground after composition (Weathering.cs) by reading the plan back. A rundown type knows its
// own story while it plans (where the building came down, which slab cracked where, where the squatters sleep, which way
// the lorries drive), so it lays its decals right there, from its own causes, through this kit. Every pass is a cause a
// visitor would recognise:
//   ground      the lot's base: bare soil, weed tufts and dry grass in a mosaic (never one even film), grit
//   slab        an old slab: its own tone, weedy cracks along the real crack lines, moss in the joints, grime at the rim
//   rubble      broken concrete and brick spread round each pile, brick dust further out
//   fire        a burnt patch with ash round a fire barrel, a burnt-out car or a whole burnt lot
//   junk        litter and old oil under dumped junk
//   track       a worn shortcut, tyre ruts in soil, mud carried out to the road
//   water       puddles in the low places, stagnant water in pits and on sunken slabs
//   fence       weeds caught along a fence line, grime under it
// Neglect is not an amount here: each type (and style) calls the passes that tell its story, with its own kinds.
// The budget follows the site's size (Weathering.PerHectare), so a huge site is thinned evenly, never cut short.

using System;
using System.Collections.Generic;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Rundown
{
    public sealed class RundownGround
    {
        public readonly BrushContext B;
        public readonly float2[] Ring;
        public readonly BrushArea Site;
        private readonly uint _seed;
        private readonly int _start;
        private readonly List<DecalInstance> _out;
        public readonly bool Ready;
        /// <summary>Hard ground the soil passes leave out (slabs, pads, surfaces): their own passes dress them.</summary>
        public readonly List<float2[]> Hard = new List<float2[]>();

        public RundownGround(LayerContext ctx, uint salt, float perHectare = 300f)
        {
            Ring = Poly.EnsureCcw((float2[])ctx.LayerRegion.Ring.Clone());
            _seed = Hashing.Mix(ctx.Seed, salt);
            _out = ctx.Plan.DecalInstances;
            _start = _out.Count;
            var lib = DecalLibrary.EnsureCurrent();
            Ready = lib != null && lib.Count > 0;
            float ha = ctx.LayerRegion.Area / 10000f;
            int cap = (int)math.clamp(ha * perHectare, 120f, Weathering.MaxInstances);
            float density = math.min(1f, cap / math.max(1f, ha * perHectare * 0.95f));
            // big sites thin evenly: about perHectare a hectare up to the hard cap
            density = math.min(density, Weathering.MaxInstances / math.max(1f, ha * perHectare));
            B = new BrushContext(lib, _seed, _out) { Budget = _start + math.min(Weathering.MaxInstances, (int)(cap * 2.6f) + 400), Density = math.max(0.05f, density) };
            Site = BrushArea.Of(Ring);
        }

        public int Placed => _out.Count - _start;

        private uint H(int a, int b = 0) => Hashing.Mix(_seed, a, b);

        /// <summary>The soil of the lot: the site minus the hard ground and minus <paramref name="holes"/>.</summary>
        public BrushArea Soil(IEnumerable<float2[]> holes = null)
        {
            var all = new List<float2[]>(Hard);
            if (holes != null) all.AddRange(holes);
            return BrushArea.Of(Ring, all);
        }

        // ───────────── the lot's base ─────────────

        /// <summary>How the open ground of a place reads. Each style picks its own (never a scale of one look).</summary>
        public sealed class GroundLook
        {
            /// <summary>Bare soil patches (0..1 coverage), weed tufts, dry grass, grit and stones, mottle.</summary>
            /// <summary>Rank grass: the uncut, seeded meadow of a lot nobody mows (the dense carpet that stops it reading as a lawn).</summary>
            public float Rank = 0f;
            public float Bare = 0.25f, Weeds = 0.25f, Dry = 0.15f, Grit = 0.08f, Stones = 0f, Mottle = 0.12f, Moss = 0f, Litter = 0f;
            /// <summary>Weeds thicker toward the edges (fences catch seeds, nobody walks there): 0 even .. 1 edges only.</summary>
            public float EdgeWeeds = 0.5f;
            /// <summary>Metres the edge belt reaches in.</summary>
            public float EdgeReach = 14f;
            /// <summary>Soil colour: "soil" (brown), "dust" (brick and concrete dust), "gravel" (stones carpet).</summary>
            public string Soil = "soil";
        }

        public void Ground(BrushArea soil, GroundLook g, Func<float2, float> worn = null)
        {
            if (!Ready || soil == null || soil.IsEmpty) return;
            float Edge(float2 p) => 1f - math.saturate(Poly.DistanceToBoundary(Ring, p) / math.max(2f, g.EdgeReach));
            float Mosaic(float2 p, int salt, float scale) => ValueNoise.Clumps(p, scale, H(salt));
            float Worn(float2 p) => worn == null ? 0f : math.saturate(worn(p));
            // bare soil where it is worn or open (small and see-through: the paint carries the big variation; the first game
            // pictures showed big pale patches as smudges, the user's "macro smudginess") (away from the weedy edges)
            if (g.Bare > 0f)
            {
                string bare = g.Soil == "dust" ? "brickDust" : "barePatch";
                Brushes.Scatter(B, soil, bare, g.Bare, new ScatterOptions
                {
                    Weight = p => math.saturate(0.25f + 0.75f * Mosaic(p, 11, 26f) - 0.5f * g.EdgeWeeds * Edge(p) + 0.8f * Worn(p)),
                    Opacity = new float2(0.3f, 0.55f), Scale = new float2(0.45f, 0.8f), Clump = 4f, Spill = 1f, Loners = 0.35f, Cause = "ground:bare",
                });
            }
            if (g.Rank > 0f)
                Brushes.Scatter(B, soil, "rankGrass", g.Rank, new ScatterOptions { Weight = p => math.saturate(0.45f + 0.55f * Mosaic(p, 21, 22f)) * (1f - 0.95f * Worn(p)), Opacity = new float2(0.7f, 1f), Clump = 3f, Spread = 0.8f, Loners = 0.2f, Spill = 1f, Cause = "ground:rank" });
            if (g.Soil == "gravel")
                Brushes.Scatter(B, soil, "gravelCarpet", 0.35f, new ScatterOptions { Weight = p => 0.4f + 0.6f * Mosaic(p, 12, 30f), Opacity = new float2(0.35f, 0.6f), Clump = 2f, Cause = "ground:gravel" });
            if (g.Mottle > 0f)
                Brushes.Scatter(B, soil, "groundMottle", g.Mottle, new ScatterOptions { Weight = p => 0.35f + 0.65f * Mosaic(p, 13, 30f), Opacity = new float2(0.25f, 0.45f), Clump = 2.5f, Level = ColourLevel.Quiet, Cause = "ground:mottle" });
            if (g.Dry > 0f)
                Brushes.Scatter(B, soil, "dryGrass", g.Dry * 0.45f, new ScatterOptions { Weight = p => 0.3f + 0.7f * Mosaic(p, 14, 34f) * (1f - Worn(p)), Opacity = new float2(0.22f, 0.4f), Scale = new float2(0.5f, 0.85f), Clump = 3f, Spill = 2f, Cause = "ground:dry" });
            // weed tufts: thick on the edges, in clumps in the open, never where feet and wheels go
            if (g.Weeds > 0f)
                Brushes.Scatter(B, soil, "weedTufts", g.Weeds, new ScatterOptions
                {
                    Weight = p => math.saturate((1f - g.EdgeWeeds) * (0.3f + 0.7f * Mosaic(p, 15, 18f)) + g.EdgeWeeds * Edge(p) * (0.6f + 0.4f * Mosaic(p, 16, 9f))) * (1f - 0.9f * Worn(p)),
                    Opacity = new float2(0.65f, 1f), Clump = 4f, Spread = 0.6f, Loners = 0.3f, Spill = 1f, Cause = "ground:weeds",
                });
            if (g.Grit > 0f)
                Brushes.Scatter(B, soil, "pebbles", g.Grit, new ScatterOptions { Weight = p => 0.3f + 0.4f * Mosaic(p, 17, 14f) + 0.6f * Worn(p), Opacity = new float2(0.5f, 0.85f), Clump = 2.5f, Loners = 0.4f, Cause = "ground:grit" });
            if (g.Stones > 0f)
                Brushes.Scatter(B, soil, "rocks", g.Stones, new ScatterOptions { Weight = p => 0.2f + 0.8f * Mosaic(p, 18, 20f), Opacity = new float2(0.6f, 0.95f), Clump = 3f, Loners = 0.35f, Cause = "ground:stones" });
            if (g.Moss > 0f)
                Brushes.Scatter(B, soil, "mossPatch", g.Moss, new ScatterOptions { Weight = p => Mosaic(p, 19, 16f), Opacity = new float2(0.45f, 0.75f), Clump = 3f, Cause = "ground:moss" });
            if (g.Litter > 0f)
                Brushes.Scatter(B, soil, "litterMix", g.Litter, new ScatterOptions { Weight = p => 0.2f + 0.8f * Edge(p) * Mosaic(p, 20, 12f), Opacity = new float2(0.7f, 1f), Clump = 2.5f, Loners = 0.4f, Cause = "ground:litter" });
        }

        // ───────────── old slabs ─────────────

        /// <summary>An old slab (asphalt or concrete): faint tone, weedy cracks along its real cracks, moss and weeds at the
        /// joints, grime at its rim, a few hairline cracks inside the shards. <paramref name="weeds"/> 0..1: how far nature got.</summary>
        public void Slab(IReadOnlyList<float2[]> shards, IReadOnlyList<(float2 a, float2 b)> cracks, float2[] outline, bool asphalt, float weeds, float age = 1f)
        {
            if (!Ready) return;
            foreach (var s in shards)
            {
                if (s == null || s.Length < 3) continue;
                Hard.Add(s);
                var a = BrushArea.Of(s);
                Brushes.Tone(B, a, asphalt ? "asphaltTonePatch" : "pavingTonePatch", new float2(0.3f, 0.5f), 0.3f, ColourLevel.Quiet, "slab:tone");
                float area = Poly.Area(s);
                if (area > 30f)
                    Brushes.Scatter(B, a, asphalt ? "crackAsphalt" : "crackConcrete", 0.12f * age, new ScatterOptions { Opacity = new float2(0.6f, 0.95f), Clump = 1.5f, Loners = 0.6f, Cause = "slab:crack" });
                if (weeds > 0.2f)
                    Brushes.Scatter(B, a, "mossPatch", 0.1f * weeds, new ScatterOptions { Weight = p => 1f - math.saturate(Poly.DistanceToBoundary(s, p) / 2.5f), Opacity = new float2(0.4f, 0.7f), Clump = 2f, Spill = 0.5f, Cause = "slab:moss" });
                if (age > 0.6f && area > 40f)
                    Brushes.Scatter(B, a, "oilStainOld", 0.03f, new ScatterOptions { Opacity = new float2(0.35f, 0.6f), Clump = 1.5f, Loners = 0.7f, Cause = "slab:stain" });
            }
            // the cracks between the shards: weeds push up along them (or bare cracks on a young slab)
            if (cracks != null)
                foreach (var (a, b) in cracks)
                {
                    if (math.distance(a, b) < 2f) continue;
                    Brushes.CracksAlong(B, new[] { a, b }, weeds > 0.15f ? "crackWeeds" : (asphalt ? "crackAsphalt" : "crackConcrete"),
                        new StripOptions { Gaps = math.lerp(0.45f, 0.1f, weeds), Run = new float2(8f, 30f), Width = new float2(0.9f, 1.15f), Opacity = new float2(0.7f, 1f), Cause = "slab:cracks" });
                }
            if (outline != null && outline.Length >= 3)
                Brushes.EdgeStrip(B, Poly.EnsureCcw((float2[])outline.Clone()), "kerbGrimeStrip", new StripOptions { Side = 1, Offset = -0.3f, Closed = true, Gaps = 0.4f, Opacity = new float2(0.5f, 0.8f), Cause = "slab:rim" });
        }

        // ───────────── rubble, fire, junk ─────────────

        /// <summary>Broken concrete and brick round a pile (radius r), brick dust further out.</summary>
        public void Rubble(float2 p, float r, float amount = 1f)
        {
            if (!Ready || amount <= 0f) return;
            var u = Hashing.Unit(H((int)(p.x * 3f), (int)(p.y * 3f)));
            var dir = new float2(math.cos(u * 6.283f), math.sin(u * 6.283f));
            Brushes.At(B, p, dir, "rubbleSpread", math.clamp(r * 1.8f, 5f, 14f), new float2(0.7f, 1f) * math.min(1f, 0.6f + 0.4f * amount), 2, ColourLevel.Any, "rubble");
            if (amount > 0.4f) Brushes.At(B, p + dir * r * 0.6f, -dir, "brickDust", math.clamp(r * 2.6f, 7f, 18f), new float2(0.35f, 0.6f), 1, ColourLevel.Any, "rubble:dust");
        }

        /// <summary>Rubble spread over a whole field (the falloff from a collapse point).</summary>
        public void RubbleField(BrushArea area, float2 focus, float radius, float amount)
        {
            if (!Ready || amount <= 0f || area == null || area.IsEmpty) return;
            float W(float2 p) => math.pow(math.saturate(1f - math.distance(p, focus) / math.max(4f, radius)), 1.3f) * (0.5f + 0.5f * ValueNoise.Clumps(p, 10f, H(31)));
            Brushes.Scatter(B, area, "rubbleSpread", 0.45f * amount, new ScatterOptions { Weight = W, Opacity = new float2(0.65f, 1f), Clump = 3f, Loners = 0.3f, Cause = "rubble:field" });
            Brushes.Scatter(B, area, "brickDust", 0.3f * amount, new ScatterOptions { Weight = p => math.saturate(W(p) * 1.6f), Opacity = new float2(0.3f, 0.55f), Clump = 2f, Spill = 2f, Cause = "rubble:dust" });
        }

        public void Fire(float2 p, float r)
        {
            if (!Ready) return;
            Brushes.At(B, p, new float2(1f, 0f), "burntPatch", math.max(3f, r * 2f), new float2(0.75f, 0.95f), 1, ColourLevel.Any, "fire");
        }

        /// <summary>A whole burnt area (a burnt-out lot): patches of char and ash over it, heaviest at <paramref name="seat"/>.</summary>
        public void BurntArea(BrushArea area, float2 seat, float radius)
        {
            if (!Ready || area == null || area.IsEmpty) return;
            float W(float2 p) => math.saturate(1.15f - math.distance(p, seat) / math.max(6f, radius)) * (0.45f + 0.55f * ValueNoise.Clumps(p, 9f, H(41)));
            Brushes.Scatter(B, area, "burntPatch", 1.4f, new ScatterOptions { Weight = W, Opacity = new float2(0.8f, 1f), Clump = 3.5f, Loners = 0.25f, Cause = "fire:area" });
        }

        public void Junk(float2 p, float r)
        {
            if (!Ready) return;
            uint h = H((int)(p.x * 5f), (int)(p.y * 5f));
            Brushes.At(B, p, new float2(math.cos(h), math.sin(h)), "litterMix", math.max(3f, r * 2.2f), new float2(0.75f, 1f), 1, ColourLevel.Any, "junk");
            if (Hashing.Unit(h) < 0.45f) Brushes.At(B, p, new float2(1f, 0f), "oilStainOld", math.max(2f, r * 1.2f), new float2(0.4f, 0.65f), 0, ColourLevel.Any, "junk:oil");
        }

        // ───────────── feet and wheels ─────────────

        /// <summary>A worn shortcut across the lot (feet): a worn strip, grit and bare ground at its ends.</summary>
        public void Shortcut(IReadOnlyList<float2> line)
        {
            if (!Ready || line == null || line.Count < 2) return;
            Brushes.EdgeStrip(B, line, "wornPath", new StripOptions { Side = 1, Gaps = 0.12f, Run = new float2(14f, 40f), Opacity = new float2(0.7f, 1f), Width = new float2(1f, 1.3f), Cause = "track:feet" });
            Brushes.At(B, line[0], line[1] - line[0], "barePatch", 6f, new float2(0.45f, 0.7f), 1, ColourLevel.Any, "track:end");
            Brushes.At(B, line[line.Count - 1], line[line.Count - 2] - line[line.Count - 1], "barePatch", 6f, new float2(0.45f, 0.7f), 1, ColourLevel.Any, "track:end");
        }

        /// <summary>Twin tyre ruts in soil along a line (cars that park on the lot, a dumper's run).</summary>
        public void Ruts(IReadOnlyList<float2> line, float gauge = 1.7f, bool muddy = false)
        {
            if (!Ready || line == null || line.Count < 2) return;
            foreach (int s in new[] { -1, 1 })
            {
                var off = Offset(line, s * gauge * 0.5f);
                Brushes.EdgeStrip(B, off, "wheelRut", new StripOptions { Side = 1, Gaps = 0.2f, Run = new float2(10f, 30f), Opacity = new float2(0.65f, 0.95f), Cause = "track:ruts" });
                if (muddy) Brushes.EdgeStrip(B, off, "tyreTrack", new StripOptions { Side = 1, Gaps = 0.35f, Run = new float2(8f, 24f), Opacity = new float2(0.55f, 0.85f), Cause = "track:tread" });
            }
        }

        /// <summary>Mud carried out on tyres: tread tracks from the gate fading over the first metres outside the works.</summary>
        public void MudOut(float2 gate, float2 inward, float length = 18f)
        {
            if (!Ready) return;
            var side = new float2(-inward.y, inward.x);
            foreach (int s in new[] { -1, 1 })
            {
                var a = gate + side * s * 0.9f + inward * length; var b = gate + side * s * 0.9f - inward * 8f;   // out over the pavement onto the street
                Brushes.EdgeStrip(B, new[] { a, b }, "tyreTrack", new StripOptions { Side = 1, Gaps = 0f, Opacity = new float2(0.85f, 1f), Width = new float2(1.3f, 1.5f), Cause = "track:mudOut" });
                for (int k = 0; k < 4; k++) Brushes.At(B, math.lerp(a, b, 0.45f + k * 0.16f), b - a, "soilSpill", 3.5f, new float2(0.55f, 0.85f), 0, ColourLevel.Any, "track:mudOut");
            }
            Brushes.At(B, gate + inward * 4f, inward, "soilSpill", 8f, new float2(0.6f, 0.9f), 2, ColourLevel.Any, "track:gate");
        }

        // ───────────── water ─────────────

        public void Puddle(float2 p, float size, bool stagnant)
        {
            if (!Ready) return;
            uint h = H((int)(p.x * 7f), (int)(p.y * 7f));
            Brushes.At(B, p, new float2(math.cos(h), math.sin(h)), stagnant ? "stagnantWater" : "puddlePiece", size, new float2(0.7f, 0.95f), stagnant ? 1 : 2, ColourLevel.Any, stagnant ? "water:stagnant" : "water:puddle");
        }

        /// <summary>Water standing in a hollow (a pit's floor, a sunken slab): pieces filling its shape.</summary>
        public void Pool(float2[] hollow, bool stagnant)
        {
            if (!Ready || hollow == null || hollow.Length < 3) return;
            Brushes.Fill(B, hollow, stagnant ? "stagnantWater" : "puddlePiece", new float2(0.75f, 1f), ColourLevel.Any, stagnant ? "water:pool" : "water:puddles");
        }

        // ───────────── seams ─────────────

        /// <summary>Weeds caught along a fence (both sides) and grime under it.</summary>
        public void FenceLine(IReadOnlyList<float2> line, bool closed, float weeds)
        {
            if (!Ready || line == null || line.Count < 2) return;
            Brushes.EdgeStrip(B, line, "fenceLine", new StripOptions { Side = 1, Closed = closed, Gaps = 0.25f, Opacity = new float2(0.55f, 0.85f), Cause = "fence:grime" });
            if (weeds > 0f)
                Brushes.EdgeStrip(B, line, "leafDriftStripSummer", new StripOptions { Side = 1, Offset = -0.6f, Closed = closed, Gaps = 0.7f, Opacity = new float2(0.4f, 0.7f) * math.min(1f, weeds), Cause = "fence:litter" });
        }

        /// <summary>A faded painted line (old car-park bays, a lane on a broken road).</summary>
        public void Line(float2 a, float2 b, float opacity = 0.8f)
        {
            if (!Ready) return;
            Brushes.EdgeStrip(B, new[] { a, b }, "fadedLine", new StripOptions { Side = 1, Gaps = 0.15f, Run = new float2(4f, 14f), Opacity = new float2(0.6f, 1f) * opacity, Width = new float2(1f, 1f), Cause = "paint:line" });
        }

        /// <summary>One piece laid exactly along <paramref name="dir"/>, <paramref name="length"/> long (painted lines, where the
        /// brushes' small random turn would show).</summary>
        public bool Exact(string kind, float2 p, float2 dir, float length, float opacity, string cause)
        {
            if (!Ready || B.Density < 0.999f && Hashing.Unit(H((int)(p.x * 9f), (int)(p.y * 9f))) > B.Density) return false;
            var k = B.Lib.Get(kind);
            if (k == null) return false;
            var pool = B.Variants(k, ColourLevel.Any);
            var r = B.Rng(kind);
            int v = B.Pick(k, pool, p, length, ref r);
            dir = math.normalizesafe(dir, new float2(1f, 0f));
            float s = length / math.max(0.3f, DecalVariants.FootprintOf(k, v).x);
            return B.Add(k, v, p, math.atan2(-dir.y, dir.x), new float2(s, 1f), opacity, cause);
        }

        private static float2[] Offset(IReadOnlyList<float2> line, float d)
        {
            var o = new float2[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                var t = math.normalizesafe((i + 1 < line.Count ? line[i + 1] : line[i]) - (i > 0 ? line[i - 1] : line[i]));
                o[i] = line[i] + new float2(-t.y, t.x) * d;
            }
            return o;
        }

        /// <summary>Facts for the log and tests: instances by cause.</summary>
        public void Report(LayerContext ctx)
        {
            ctx.Plan.Facts["decalInstances"] = Placed;
            ctx.Plan.Facts["decalDensityPct"] = Math.Round(B.Density * 100f);
        }
    }
}

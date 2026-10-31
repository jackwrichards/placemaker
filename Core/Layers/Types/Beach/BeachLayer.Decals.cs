// Beach (p2-beaches, 2026-10-31): the shore's ground design, laid with the shared decal library while the layer plans (it knows
// its own waterline, its landings, its umbrellas and its landforms, like RundownGround). Every pass is a cause a visitor would
// recognise:
//   water      the band the waves keep wet and dark, the wrack line of the last high water, shells and pebbles sorted along it,
//              kelp heaps and driftwood thrown up by storms (wild shores), salt pans and creek mud in a marsh
//   wind       ripples over the dry sand, sand blown onto the promenade's beach side, sand thrown out of the dunes' blowouts
//   rain       sand washed down the bank behind the beach in fans
//   feet       footprint trails from each landing to the water, trampled sand round the landings
//   families   towels laid out by the umbrellas, buckets and spades and sand castles by the water, dug pits
//   rock       slabs and joints on the rock platforms and headlands, tide pools, wet weedy rock at the water, scree in ravines
//   style      shingle carpets and storm-ridge crests, pine duff on a lake beach, ash at the fire rings
// The budget follows the site's size (Weathering.MaxInstances, thinned evenly on huge sites).

using System.Collections.Generic;
using Placemaker.Core.Decals;
using Placemaker.Core.Decals.Dynamic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public sealed partial class BeachLayer
    {
        private const float DecalsPerHectare = 650f;

        private static BrushArea AreaOf(List<float2[]> rings)
        {
            var a = new BrushArea();
            foreach (var r in rings) if (r != null && r.Length >= 3) a.Outer.Add(r);
            return a;
        }

        /// <summary>Strip side for a line along the shore whose heavy side (+z) should face the water.</summary>
        private static int WaterSide(Shoreline shore, float2[] line)
        {
            if (line.Length < 2) return 1;
            var d = math.normalizesafe(line[1] - line[0]);
            var left = new float2(-d.y, d.x);
            shore.Nearest(line[0], out var inw, out _);
            return math.dot(left, inw) > 0f ? -1 : 1;
        }

        private static void LayDecals(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var f = b.Forms; var g = b.G;
            if (!DressingControls.GroundDecalsOn(ctx.Settings)) return;   // the player switched ground decals off
            var lib = DecalLibrary.EnsureCurrent();
            if (lib == null || lib.Count == 0 || shore.Mode == ShoreMode.Assumed || !shore.HasWaterline) return;
            var ring = Poly.EnsureCcw((float2[])shore.Land.Ring.Clone());
            float ha = shore.Land.Area / 10000f;
            int cap = (int)math.clamp(ha * DecalsPerHectare, 150f, Weathering.MaxInstances);
            float density = math.min(1f, Weathering.MaxInstances / math.max(1f, ha * DecalsPerHectare));
            var outList = ctx.Plan.DecalInstances;
            int start = outList.Count;
            var B = new BrushContext(lib, Hashing.Mix(ctx.Seed, 0xBEAC4u), outList) { Budget = start + math.min(Weathering.MaxInstances, (int)(cap * 2.4f) + 300), Density = math.max(0.05f, density) };
            string st = b.Style;
            bool sand = st != "shingle" && st != "marsh";
            bool wild = st == "wild" || st == "cove";
            bool tidy = st == "resort";
            float far = shore.MaxDepth + Shoreline.MaxGap + 20f;
            uint seed = Hashing.Mix(ctx.Seed, 0xBEADu);
            bool Rock(float2 p) { shore.Nearest(p, out _, out float s); float dw = shore.FromWater(p); return f.OnHeadland(s, dw, out _) || f.OnPlatform(s, dw) || g.OnCliff(p); }
            List<float2[]> Along(float dw, float spacing = 2f) => shore.OffsetFromWater(dw, 1f, 6f, spacing, Rock);
            BrushArea Band(float dw0, float dw1)
            {
                var a = new BrushArea();
                foreach (var run in b.BeachRuns)
                    foreach (var r in shore.StripFromWater(dw0, dw1, run.x, run.y)) a.Outer.Add(r);
                a.Keep = p => shore.Land.Contains(p) || shore.FromWater(p) > -1f;
                return a;
            }

            // ── water: wet band, wrack line, shells and pebbles, storm wrack ──
            if (st != "marsh")
                foreach (var line in Along(b.Wet * 0.45f))
                    Brushes.EdgeStrip(B, line, "wetSandBand", new StripOptions { Side = WaterSide(shore, line), Gaps = 0f, Run = new float2(24f, 60f), Opacity = new float2(0.55f, 0.85f), Width = new float2(0.9f, 1.25f), Cause = "water:wet" });
            float wrackAt = b.Wet + 1.2f + f.Berm * 2.5f;
            foreach (var line in Along(wrackAt))
                Brushes.EdgeStrip(B, line, "wrackLine", new StripOptions { Side = WaterSide(shore, line), Gaps = tidy ? 0.7f : wild ? 0.18f : 0.38f, Run = new float2(10f, 34f), Opacity = new float2(0.7f, 1f), Cause = "water:wrack" });
            var strand = Band(b.Wet - 1f, wrackAt + 4f);
            float Strand(float2 p) => math.saturate(1f - math.abs(shore.FromWater(p) - wrackAt) / 4.5f);
            Brushes.Scatter(B, strand, st == "shingle" ? "strandPebbles" : "shellScatter", tidy ? 0.06f : wild ? 0.22f : 0.13f, new ScatterOptions { Weight = Strand, Opacity = new float2(0.8f, 1f), Clump = 4f, Loners = 0.35f, Cause = "water:shells" });
            if (st != "shingle") Brushes.Scatter(B, strand, "strandPebbles", st == "granite" || st == "harbour" ? 0.18f : 0.06f, new ScatterOptions { Weight = Strand, Opacity = new float2(0.7f, 0.95f), Clump = 3f, Cause = "water:pebbles" });
            if (wild || st == "shingle" || st == "harbour" || st == "granite")
            {
                Brushes.Scatter(B, Band(b.Wet, wrackAt + 9f), "kelpHeap", wild ? 0.05f : 0.03f, new ScatterOptions { Weight = Strand, Opacity = new float2(0.85f, 1f), Clump = 2f, Loners = 0.5f, Cause = "water:kelp" });
                Brushes.Scatter(B, Band(wrackAt, b.D), "driftwood", wild ? 0.06f : 0.025f, new ScatterOptions { Weight = p => 0.4f + 0.6f * Strand(p), Opacity = new float2(0.9f, 1f), Clump = 1.5f, Loners = 0.6f, Cause = "water:driftwood" });
            }

            // ── wind: ripples, blown sand on the promenade, sand out of the blowouts ──
            if (sand)
            {
                Brushes.Scatter(B, Band(wrackAt + 3f, b.D), "sandRipples", tidy ? 0.12f : 0.3f, new ScatterOptions { Opacity = new float2(0.3f, 0.55f), Clump = 3f, Loners = 0.4f, Cause = "wind:ripples" });
                if (f.DuneH > 0f)
                    Brushes.Scatter(B, Band(f.DuneStart, f.DuneEnd), "sandRipples", 0.35f, new ScatterOptions { Opacity = new float2(0.35f, 0.6f), Clump = 3f, Cause = "wind:dunes" });
                foreach (var line in b.Lines)
                    Brushes.EdgeStrip(B, line, "blownSand", new StripOptions { Side = WaterSide(shore, line), Offset = -b.Hw * 0.3f, Gaps = tidy ? 0.6f : 0.35f, Run = new float2(8f, 26f), Opacity = new float2(0.55f, 0.9f), Cause = "wind:promenade" });
                foreach (var (bs, bw) in f.Blowouts)
                {
                    var p = At(shore, bs, f.DuneEnd - 2f);
                    shore.Nearest(p, out var inw, out _);
                    Brushes.At(B, p, inw, "sandFan", 12f, new float2(0.7f, 0.95f), 2, cause: "wind:blowout");
                }
            }

            // ── rain: sand fans down the bank behind the beach ──
            if (sand)
                foreach (var line in Along(b.D + 6f, 3f))
                {
                    float len = Shoreline.LengthOf(line);
                    for (float s = 6f; s < len; s += 13f)
                    {
                        var p = Shoreline.PointOn(line, s, out _);
                        if (g.Sculpt.GradeAt(p, 3f) < 0.12f || b.Occ.Corridors.Overlaps(p, 2f)) continue;
                        shore.Nearest(p, out var inw, out _);
                        Brushes.At(B, p - inw * 3f, -inw, "sandFan", 8f, new float2(0.6f, 0.9f), 1, cause: "rain:fan");
                    }
                }

            // ── feet: trails from the landings to the water, trampled sand round them ──
            int trail = 0;
            foreach (var (top, bottom) in b.Access)
            {
                shore.Nearest(bottom, out var inw, out float s);
                var land = bottom - inw * (LandingDepth + 0.5f);
                Brushes.At(B, land, -inw, "trampledSand", 6f, new float2(0.6f, 0.9f), 1, cause: "feet:landing");
                int n = 2 + (int)(Hashing.Unit(Hashing.Mix(seed, (uint)trail)) * 2f);
                for (int k = 0; k < n; k++)
                {
                    float spread = (k - (n - 1) * 0.5f) * 0.35f + (Hashing.Unit(Hashing.Mix(seed, trail, k)) - 0.5f) * 0.2f;
                    var w = shore.WaterAt(math.clamp(s + spread * 30f, 0f, shore.Length), out _);
                    var to = w + inw * (b.Wet * 0.3f);
                    Brushes.EdgeStrip(B, new[] { land, math.lerp(land, to, 0.5f) + new float2(inw.y, -inw.x) * spread * 2f, to }, "sandFootprints",
                        new StripOptions { Side = 1, Gaps = 0.15f, Run = new float2(8f, 30f), Opacity = new float2(0.7f, 0.95f), CornerDeg = 60f, Cause = "feet:trail" });
                }
                trail++;
            }

            // ── families: towels, toys and castles, pits ──
            if (sand && !wild && st != "ravine")
            {
                int i = 0;
                foreach (var p in b.UmbrellaSpots)
                {
                    uint v = Hashing.Mix(seed, (uint)(0x7000 + i++));
                    shore.Nearest(p, out var inw, out _);
                    var tan = new float2(inw.y, -inw.x);
                    if (Hashing.Unit(v) < 0.5f)
                        Brushes.At(B, p - inw * (2.4f + Hashing.Unit(Hashing.Mix(v, 1u)) * 1.5f) + tan * (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 2f, tan, "beachTowels", 3.6f, new float2(1f, 1f), 0, cause: "family:towels");
                    if (Hashing.Unit(Hashing.Mix(v, 3u)) < 0.22f)
                    {
                        float dw = b.Wet + 1.5f + Hashing.Unit(Hashing.Mix(v, 4u)) * 3f;
                        shore.Nearest(p, out _, out float s);
                        var q = At(shore, s + (Hashing.Unit(Hashing.Mix(v, 5u)) - 0.5f) * 6f, dw);
                        if (!b.Occ.Corridors.Overlaps(q, 1f)) Brushes.At(B, q, tan, "beachToys", 3.4f, new float2(0.9f, 1f), 0, cause: "family:toys");
                        if (Hashing.Unit(Hashing.Mix(v, 6u)) < 0.5f) Brushes.At(B, q + tan * 2.8f, tan, "sandPit", 3f, new float2(0.8f, 1f), 0, cause: "family:pit");
                    }
                }
                // a beach without umbrellas still has people: towels scattered near the water on a family-style shore
                if (b.UmbrellaSpots.Count < 4 && (st == "mulch" || st == "granite" || st == "sandy" || st == "harbour"))
                {
                    int n = (int)math.clamp(shore.Length / 22f, 2f, 18f);
                    foreach (float s in shore.Stations(n, 6f, 0.35f))
                    {
                        uint v = Hashing.Mix(seed, (uint)(s * 13f));
                        var q = At(shore, s, b.Wet + 2f + Hashing.Unit(v) * math.max(2f, b.D * 0.4f));
                        if (Rock(q) || b.Occ.Corridors.Overlaps(q, 1.5f) || b.Occ.Discs.Overlaps(q, 1.5f)) continue;
                        shore.Nearest(q, out var inw, out _);
                        Brushes.At(B, q, new float2(inw.y, -inw.x), "beachTowels", 3.6f, new float2(1f, 1f), 0, cause: "family:towels");
                        if (Hashing.Unit(Hashing.Mix(v, 1u)) < 0.3f) Brushes.At(B, q - inw * 3f, new float2(inw.y, -inw.x), "beachToys", 3f, new float2(0.9f, 1f), 0, cause: "family:toys");
                    }
                }
            }

            // ── rock: platforms, headlands, knolls, ravines, wet rock at the water ──
            foreach (var pl in f.Platforms)
            {
                var a = AreaOf(shore.StripFromWater(f.PlatformFrom + 1f, f.PlatformTo, pl.x, pl.y));
                Brushes.Scatter(B, a, "rockPlatform", 0.75f, new ScatterOptions { Opacity = new float2(0.8f, 1f), Clump = 3f, Loners = 0.3f, Cause = "rock:platform" });
                Brushes.Scatter(B, a, "tidePool", 0.16f, new ScatterOptions { Weight = p => math.saturate(1f - shore.FromWater(p) / 5f), Opacity = new float2(0.8f, 1f), Clump = 2.5f, Loners = 0.4f, Cause = "rock:pool" });
                Brushes.Scatter(B, AreaOf(shore.StripFromWater(f.PlatformFrom + 1f, 1.5f, pl.x, pl.y)), "wetRock", 0.55f, new ScatterOptions { Opacity = new float2(0.7f, 1f), Clump = 3f, Cause = "rock:wet" });
            }
            foreach (var (hs, hlen, hh, hw) in f.Headlands)
            {
                var r = Ribbon(shore, -hlen, 16f, dw => hs + 4f * math.sin(dw / 9f + hs) * (1f - math.saturate((dw + hlen) / (hlen + 18f))),
                    dw => hw * (0.55f + 0.45f * math.saturate((dw + hlen) / (hlen + 18f))) * 0.9f);
                var a = BrushArea.Of(r);
                Brushes.Scatter(B, a, "rockOutcrop", 0.55f, new ScatterOptions { Opacity = new float2(0.8f, 1f), Clump = 3f, Cause = "rock:headland" });
                Brushes.Scatter(B, a, "rockPlatform", 0.35f, new ScatterOptions { Opacity = new float2(0.7f, 1f), Clump = 2f, Cause = "rock:headland" });
                Brushes.Scatter(B, a, "tidePool", 0.06f, new ScatterOptions { Weight = p => shore.FromWater(p) < 3f ? 1f : 0.1f, Opacity = new float2(0.8f, 1f), Clump = 2f, Cause = "rock:pool" });
            }
            foreach (var (ic, ir, ih, _) in f.Islands)
            {
                var isle = BrushArea.Of(LeisureKit.Blob(ic, ir * 1.05f, Hashing.Mix(seed, (uint)(ic.x * 3f)), 14, 0.2f));
                Brushes.Scatter(B, isle, "rockOutcrop", 0.5f, new ScatterOptions { Opacity = new float2(0.8f, 1f), Clump = 2f, Cause = "rock:island" });
                Brushes.Scatter(B, isle, "tidePool", 0.05f, new ScatterOptions { Weight = p => math.saturate(math.distance(p, ic) / ir - 0.6f), Opacity = new float2(0.8f, 1f), Clump = 1.5f, Cause = "rock:pool" });
                Brushes.EdgeStrip(B, LeisureKit.Blob(ic, ir * 1.1f, Hashing.Mix(seed, (uint)(ic.y * 3f)), 18, 0.15f), "wrackLine", new StripOptions { Closed = true, Gaps = 0.4f, Opacity = new float2(0.7f, 1f), Cause = "water:island" });
            }
            foreach (var (kc, kr, kh) in f.Knolls)
                Brushes.Scatter(B, BrushArea.Of(LeisureKit.Blob(kc, kr, Hashing.Mix(seed, (uint)(kc.x * 5f)), 12, 0.2f)), "rockOutcrop", 0.6f, new ScatterOptions { Opacity = new float2(0.85f, 1f), Clump = 2f, Cause = "rock:knoll" });
            foreach (var (rs, rw, rlen) in f.Ravines)
            {
                var floor = BrushArea.Of(Ribbon(shore, 0f, math.min(rlen, far), dw => rs + 3f * math.sin(dw / 13f + rs), dw => 2.5f + 0.04f * dw));
                var walls = BrushArea.Of(Ribbon(shore, 0f, math.min(rlen, far), dw => rs + 3f * math.sin(dw / 13f + rs), dw => rw * (0.75f + 0.35f * math.saturate(dw / 40f))));
                Brushes.Scatter(B, floor, "stonesCarpet", 0.6f, new ScatterOptions { Opacity = new float2(0.75f, 1f), Clump = 3f, Cause = "rock:ravine" });
                Brushes.Scatter(B, walls, "screeApron", 0.5f, new ScatterOptions { Opacity = new float2(0.7f, 1f), Clump = 3f, Cause = "rock:scree" });
                Brushes.Scatter(B, walls, "rockOutcrop", 0.3f, new ScatterOptions { Opacity = new float2(0.8f, 1f), Clump = 2f, Cause = "rock:wall" });
                var mouth = At(shore, rs, 1f);
                shore.Nearest(mouth, out var inw, out _);
                Brushes.At(B, mouth, -inw, "gravelFan", 10f, new float2(0.75f, 1f), 2, cause: "rock:mouth");
            }
            foreach (var (p, r) in b.RockSpots)
            {
                float dw = shore.FromWater(p);
                if (dw > 2.5f || dw < -4f) continue;
                Brushes.At(B, p, new float2(1f, 0f), "wetRock", r * 2.6f, new float2(0.7f, 0.95f), 0, cause: "rock:wet");
            }

            // ── style grounds ──
            if (st == "shingle")
            {
                // soft overlapping patches, not the tiling carpet (a carpet laid alone shows its square edge: round 1)
                // p2-beaches: banded, not one even carpet (the game runner's shingle read as a flat strip): thick on the storm-ridge
                // crests, thin in the sandy troughs between them, a wrack line at the foot of each ridge's sea face
                float Crest(float2 p)
                {
                    float dw = shore.FromWater(p), w = 0f;
                    foreach (float rd in f.Ridges) w = math.max(w, math.exp(-(dw - rd) * (dw - rd) / 9f));
                    return 0.15f + 0.85f * w;
                }
                Brushes.Scatter(B, Band(-1f, b.D + 1f), "shinglePatch", 2f, new ScatterOptions { Weight = Crest, Opacity = new float2(0.85f, 1f), Clump = 2.5f, Loners = 0.5f, Spill = 1.5f, Cause = "shingle:ground" });
                foreach (float rd in f.Ridges)
                {
                    foreach (var line in Along(rd))
                        Brushes.EdgeStrip(B, line, "shingleRidge", new StripOptions { Side = -WaterSide(shore, line), Gaps = 0.08f, Run = new float2(20f, 50f), Opacity = new float2(0.8f, 1f), Cause = "shingle:ridge" });
                    foreach (var line in Along(rd - 3.2f))
                        Brushes.EdgeStrip(B, line, "wrackLine", new StripOptions { Side = WaterSide(shore, line), Gaps = 0.3f, Run = new float2(10f, 30f), Opacity = new float2(0.6f, 0.9f), Cause = "shingle:wrack" });
                }
                Brushes.Scatter(B, Band(b.D - 2f, b.D + 8f), "shinglePatch", 0.3f, new ScatterOptions { Opacity = new float2(0.7f, 1f), Clump = 2f, Cause = "shingle:thrown" });
            }
            if (st == "granite")
                Brushes.Scatter(B, Band(b.Wet, b.D), "shinglePatch", 0.14f, new ScatterOptions { Opacity = new float2(0.5f, 0.8f), Clump = 2f, Loners = 0.4f, Cause = "granite:grit" });
            if (st == "mulch")
            {
                var duff = Band(b.Wet + 1f, b.D + 14f);
                Brushes.Scatter(B, duff, "pineDuffSand", 0.55f, new ScatterOptions { Weight = p => math.saturate((shore.FromWater(p) - b.Wet) / math.max(4f, b.D * 0.8f)), Opacity = new float2(0.7f, 1f), Clump = 3f, Cause = "pines:duff" });
                Brushes.Scatter(B, Band(b.D * 0.5f, b.D + 20f), "pineNeedlesCarpet", 0.3f, new ScatterOptions { Weight = p => math.saturate((shore.FromWater(p) - b.D * 0.5f) / 10f), Opacity = new float2(0.5f, 0.85f), Clump = 3f, Cause = "pines:needles" });
            }
            if (f.Channels.Count > 0)
            {
                foreach (var (pc, pr) in f.Pans) Brushes.At(B, pc, new float2(1f, 0f), "saltPan", pr * 2.1f, new float2(0.8f, 1f), 1, cause: "marsh:pan");
                foreach (var ch in f.Channels)
                {
                    Brushes.EdgeStrip(B, ch, "shoreMud", new StripOptions { Side = 1, Offset = -1.6f, Gaps = 0.2f, Run = new float2(8f, 26f), Opacity = new float2(0.7f, 1f), CornerDeg = 70f, Cause = "marsh:bank" });
                    Brushes.EdgeStrip(B, ch, "shoreMud", new StripOptions { Side = -1, Offset = -1.6f, Gaps = 0.2f, Run = new float2(8f, 26f), Opacity = new float2(0.7f, 1f), CornerDeg = 70f, Cause = "marsh:bank" });
                }
                var flat = Band(b.Wet * 0.5f, f.MarshTo);
                Brushes.Scatter(B, flat, "marshMud", 0.28f, new ScatterOptions { Weight = p => math.saturate(1f - f.ChannelDistance(p, out _) / 12f), Opacity = new float2(0.6f, 0.95f), Clump = 3f, Cause = "marsh:mud" });
                Brushes.Scatter(B, flat, "sedgeLitter", 0.32f, new ScatterOptions { Opacity = new float2(0.6f, 0.9f), Clump = 3f, Cause = "marsh:sedge" });
                // long marsh grass in drifts over the flat (from above the reed bushes alone read as scattered shrubs)
                Brushes.Scatter(B, flat, "longGrass", 0.3f, new ScatterOptions { Opacity = new float2(0.6f, 0.95f), Clump = 5f, Loners = 0.2f, Cause = "marsh:grass" });
            }
            foreach (var c in b.FirePlaces) Brushes.At(B, c, new float2(1f, 0f), "ashScorch", 3.2f, new float2(0.85f, 1f), 1, cause: "fire:ash");

            // the land behind: each biome area's own decal passes
            var sc = new Weathering.Scene(new List<AreaPlan> { ctx.Plan }, ring, ctx.Site, g.H, Hashing.Mix(ctx.Seed, 0xB10Fu), 1f, lib, outList, B.Density, B.Budget);
            if (BiomeGround.Any(sc)) BiomeGround.Run(sc);

            // nothing past the drawn outline: the bluff and ravine bands reach beyond it (seen past the road in the game)
            int culled = 0;
            for (int i = outList.Count - 1; i >= start; i--)
                if (!shore.Outline.Contains(outList[i].P)) { outList.RemoveAt(i); culled++; }
            ctx.Plan.Facts["beachDecalsCulled"] = culled;

            int placed = outList.Count - start;
            ctx.Plan.Facts["beachDecals"] = placed;
            ctx.Plan.Facts["beachDecalDensity"] = density;
        }
    }
}

// Park moods (makeover, 2026-10-29). The user: "Regular parks are going to need a massive makeover ... rock sections,
// barrancas, all that"; "consistency across multiple seeds, but inconsistency across a single park" (in vibes: "it should
// just be dynamically whatever it should be").
//
// A park is not one texture. After the paths and lawns are known, the park picks a few PLACES in its lawns and gives
// each a mood that fits the ground it is on:
//   gully     a barranca: a dry, winding channel cut 1-2.5 m into the lawn along the fall of the land, its banks set with
//             half-buried boulders, cobbles on its floor, a fallen log across it, alders and scrub on its rim
//   outcrop   a rocky knoll: the ground raised into a low dome, broken by boulders that come up through the turf (big ones
//             tilted, half buried), scree on the downhill side, a pine or a birch, flowering scrub between the stones
//   hollow    a wet hollow: a soft dip where rain stands, ringed by alders, poplars and birches, rushes of low green scrub
//   meadow    an unmown corner: long dry grass, wild flowering shrubs, one or two old solitary trees
//   glade     a copse: a ring of mixed trees round a small clearing, an old tree in it, logs lying where they fell
//   stones    (rural parks) a ring of standing stones on a rise of rough grass
// Places are chosen from what the land offers (the lowest ground holds the hollow, the slope the gully, the high ground
// the outcrop), from the style's palette, and never twice the same mood while another fits. Every seed gets a park with
// several distinct places; no two places of one park look alike.
//
// Everything is vanilla: Boulder01-08, Rock01-30, Log01-18, StandingStone01-08, the game's trees (ages, the odd dead
// one) and wild bushes. The ground is one Sculpt over the park (free only inside the places), emitted as sloped
// terraform (permanent: the plan warns). The places are given to the ground design as regions tagged "mood:<id>"
// (Core/Decals/Dynamic/ParkGround.cs lays their decals: scree, moss, runoff, damp, long grass, forest floor).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Park
{
    public enum Mood { Gully = 0, Outcrop = 1, Hollow = 2, Meadow = 3, Glade = 4, Stones = 5 }

    public sealed class MoodPlace
    {
        public Mood Mood;
        public float2 C;
        public float R;
        /// <summary>The place's outline (an organic blob, or the channel's banks for a gully).</summary>
        public float2[] Ring;
        /// <summary>Gully: the channel's centreline (downhill from [0]); its floor half-width, depth and top half-width.</summary>
        public float2[] Line;
        public float Floor, Depth, Half;
        /// <summary>Outcrop / hollow / stones: height of the rise (negative: a dip).</summary>
        public float Rise;
        public uint Seed;
    }

    public static class ParkMoods
    {
        public const string RegionPrefix = "mood:";
        public const string FloorTag = "mood:gullyFloor";
        public static readonly string[] Ids = { "gully", "outcrop", "hollow", "meadow", "glade", "stones" };

        public static readonly string[] Boulders = { "Boulder01", "Boulder02", "Boulder03", "Boulder04", "Boulder05", "Boulder06", "Boulder07", "Boulder08" };
        public static readonly string[] Stones = { "Rock01", "Rock03", "Rock05", "Rock07", "Rock09", "Rock11", "Rock13", "Rock15", "Rock17", "Rock19", "Rock21", "Rock23", "Rock25", "Rock27", "Rock29",
            "Rock02", "Rock04", "Rock06", "Rock08", "Rock10", "Rock12", "Rock14", "Rock16", "Rock18", "Rock20", "Rock22", "Rock24", "Rock26", "Rock28", "Rock30" };
        public static readonly string[] Logs = { "Log02", "Log05", "Log01", "Log04", "Log08", "Log11", "Log07", "Log14" };
        public static readonly string[] StandingStones = { "StandingStone01", "StandingStone02", "StandingStone03", "StandingStone04", "StandingStone05", "StandingStone06", "StandingStone07", "StandingStone08" };
        private static readonly string[] WetTrees = { "EU_AlderTree01", "EU_PoplarTree01", "BirchTree01", "EU_AlderTree01" };
        private static readonly string[] RimTrees = { "EU_AlderTree01", "BirchTree01", "OakTree01" };
        private static readonly string[] KnollTrees = { "PineTree01", "BirchTree01", "PineTree01" };
        private static readonly string[] CopseTrees = { "OakTree01", "BirchTree01", "NA_HickoryTree01", "EU_ChestnutTree01", "SpruceTree01", "OakTree01" };
        private static readonly string[] MeadowTrees = { "OakTree01", "BirchTree01", "EU_ChestnutTree01" };
        private static readonly string[] Scrub = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildElderly01", "FlowerBushWildElderly01" };
        private static readonly string[] Rushes = { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWild01" };
        private static readonly string[] Wildflowers = { "FlowerBushWild01", "FlowerBushWild02", "FlowerBushWildChild02", "FlowerBushWildElderly01" };

        /// <summary>The moods a style can have and how much it likes each (gully, outcrop, hollow, meadow, glade, stones).</summary>
        public static float[] Palette(string style)
        {
            switch (style)
            {
                case "garden": return new[] { 0.5f, 1.4f, 0.8f, 1.2f, 0.8f, 0f };
                case "urban": return new[] { 0f, 1.5f, 0.2f, 0.6f, 0.9f, 0f };
                case "picnic": return new[] { 0.4f, 0.9f, 0.6f, 1.3f, 1.4f, 0f };
                case "wild": return new[] { 1.4f, 1.0f, 1.0f, 1.4f, 1.0f, 0.7f };
                case "neglected": return new[] { 0.8f, 0.5f, 1.2f, 1.2f, 1.2f, 0f };
                case "formal": case "modern": case "victorian": return new[] { 0f, 0.5f, 0f, 0.3f, 0.4f, 0f };
                case "botanical": return new[] { 0.5f, 1.8f, 1.1f, 1.4f, 0.6f, 0f };
                case "playground": return new[] { 0.9f, 1.6f, 0.4f, 0.6f, 1.4f, 0f };
                default: return new[] { 1.2f, 1.1f, 1.0f, 1.0f, 1.0f, 0.15f };
            }
        }

        private static float U(uint seed, int a, int b = 0) => Hashing.Unit(Hashing.Mix(seed, a, b));
        private static float Rg(uint seed, float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(seed, a, b));

        // ───────────────────────── choosing the places ─────────────────────────

        /// <summary>
        /// Picks the places and their moods, shapes the ground (one Sculpt, emitted now so what is placed later stands on it)
        /// and takes their ground in the park's occupancy (the planting and furniture keep out). Call before furniture.
        /// </summary>
        public static List<MoodPlace> Plan(LayerContext ctx, ParkSite s, List<float2[]> lawns, string style, bool terrain, float amount = 1f)
        {
            var places = new List<MoodPlace>();
            var region = s.LayerRegion;
            if (amount <= 0f || region.Area < 1800f) return places;
            var pal = Palette(style);
            float lawnArea = 0f;
            foreach (var l in lawns) lawnArea += math.abs(Poly.Area(l));
            if (lawnArea < 1200f) return places;
            uint seed = Hashing.Mix(ctx.Seed, 0x300Du);
            int wanted = (int)math.clamp(math.round(lawnArea / 5000f * amount), 1f, 6f);
            float side = math.sqrt(lawnArea);
            // ground statistics for the land's own reading (low, high, fall)
            float mean = 0f; int ns = 0; float lo = float.MaxValue, hi = float.MinValue;
            var mn = region.Min; var mx = region.Max;
            for (int j = 0; j <= 8; j++)
                for (int i = 0; i <= 8; i++)
                {
                    var p = math.lerp(mn, mx, new float2(i / 8f, j / 8f));
                    if (!region.Contains(p)) continue;
                    float h = ctx.H(p); mean += h; ns++; lo = math.min(lo, h); hi = math.max(hi, h);
                }
            mean = ns > 0 ? mean / ns : 0f;
            float relief = math.max(0.5f, hi - lo);
            var used = new int[Ids.Length];
            var gates = new List<float2>();
            foreach (var g in s.Gates) gates.Add(g.Position);
            var taken = new List<float2>(gates);
            for (int k = 0; k < wanted * 3 && places.Count < wanted; k++)
            {
                ctx.Check();
                uint ks = Hashing.Mix(seed, (uint)k);
                // the first place is the park's hero (the biggest); then smaller ones
                float r = math.clamp(side * (places.Count == 0 ? Rg(ks, 0.22f, 0.28f, 1) : Rg(ks, 0.12f, 0.18f, 1)), 8f, places.Count == 0 ? 38f : 24f);
                if (!Spots.Find(s.Occ, r, Spots.Preference.Open, Hashing.Mix(ks, 2u), out var c, taken, r + 9f, 1.5f, 1200))
                {
                    r *= 0.75f;
                    if (!Spots.Find(s.Occ, r, Spots.Preference.Open, Hashing.Mix(ks, 3u), out c, taken, r + 7f, 1.5f, 1200))
                    {
                        // a crowded park (a picnic lawn, a small block) still finds room for a small place at its edge
                        r = 6.5f;
                        if (!Spots.Find(s.Occ, r, Spots.Preference.NearBoundary, Hashing.Mix(ks, 4u), out c, taken, r + 6f, 1f, 1200)) continue;
                    }
                }
                if (s.InLake(c, r + 2f) || s.InClearing(c, r * 0.8f)) continue;
                // what the land says here
                float h0 = ctx.H(c);
                var grad = Grad(ctx, c, r * 0.6f);
                float slope = math.length(grad);
                float low = math.saturate((mean - h0) / relief * 2f + 0.5f);   // 1 = low ground
                var w = new float[Ids.Length];
                float total = 0f;
                for (int pass = 0; pass < 2 && total <= 0f; pass++)
                for (int m = 0; m < w.Length; m++)
                {
                    float x = pal[m];
                    if (x <= 0f) { w[m] = 0f; continue; }
                    switch ((Mood)m)
                    {
                        case Mood.Gully: x *= (r < 10.5f ? 0.15f : 1f) * (1f + math.saturate(slope / 0.04f) * 2.5f); break;
                        case Mood.Hollow: x *= 0.6f + low * 1.8f; break;
                        case Mood.Outcrop: x *= 0.7f + (1f - low) * 1.2f + math.saturate(slope / 0.06f); break;
                        case Mood.Stones: x *= r < 9f ? 0f : 0.6f + (1f - low); break;
                    }
                    // never twice the same mood while others fit; a third time almost never
                    int fresh = 0;
                    for (int q = 0; q < pal.Length; q++) if (pal[q] > 0f && used[q] == 0) fresh++;
                    if (used[m] > 0 && fresh > 0 && pass == 0) x = 0f;   // a repeat only when every mood of the palette is used
                    else if (used[m] > 1) x *= 0.1f;
                    w[m] = x; total += x;
                }
                if (total <= 0f) break;
                float pick = U(ks, 7) * total;
                int mood = 0;
                for (int m = 0; m < w.Length; m++) { pick -= w[m]; if (pick <= 0f) { mood = m; break; } }
                var place = new MoodPlace { Mood = (Mood)mood, C = c, R = r, Seed = ks };
                Shape(ctx, s, place, grad);
                places.Add(place);
                used[mood]++;
                taken.Add(c);
                // the place's ground is its own (kind 205: planting and furniture keep out by its radius)
                s.Occ.Take(c, r * (place.Mood == Mood.Glade || place.Mood == Mood.Meadow ? 0.92f : 1f), 205);
            }
            if (places.Count == 0) return places;
            if (terrain) Sculpt(ctx, s, places);
            for (int m = 0; m < Ids.Length; m++) { ctx.Plan.Facts.Remove("mood." + Ids[m]); if (used[m] > 0) ctx.Plan.Facts["mood." + Ids[m]] = used[m]; }
            ctx.Plan.Facts["moodPlaces"] = places.Count;
            return places;
        }

        private static float2 Grad(LayerContext ctx, float2 c, float d)
        {
            float hx = ctx.H(c + new float2(d, 0)) - ctx.H(c - new float2(d, 0));
            float hz = ctx.H(c + new float2(0, d)) - ctx.H(c - new float2(0, d));
            return new float2(hx, hz) / (2f * d);
        }

        /// <summary>The place's outline and, for a gully, its channel.</summary>
        private static void Shape(LayerContext ctx, ParkSite s, MoodPlace p, float2 grad)
        {
            uint v = p.Seed;
            switch (p.Mood)
            {
                case Mood.Gully:
                {
                    // downhill when the land falls, else across the place's long way
                    var axis = math.lengthsq(grad) > 1e-5f ? -math.normalize(grad) : new float2(math.cos(U(v, 20) * 6.283f), math.sin(U(v, 20) * 6.283f));
                    var side = new float2(-axis.y, axis.x);
                    float L = p.R * 2f * 0.92f;
                    p.Depth = math.clamp(p.R * 0.11f, 1.1f, 2.4f);
                    p.Floor = math.clamp(p.R * 0.09f, 1.2f, 2.2f);
                    p.Half = p.Floor + p.Depth / 0.3f;   // the banks average 30 % (steepest ~45 %: never a sheet)
                    // the channel must keep its banks inside the place
                    float amp = math.max(0f, math.min(p.R * 0.2f, p.R - p.Half - 0.5f));
                    int n = 13;
                    var line = new float2[n];
                    float ph = U(v, 21) * 6.283f, f = Rg(v, 0.9f, 1.6f, 22);
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)(n - 1);
                        float u = (t - 0.5f) * L;
                        float wob = math.sin(t * math.PI * f * 2f + ph) * amp * math.sin(t * math.PI);   // pinned at the ends
                        line[i] = p.C + axis * u + side * wob;
                    }
                    p.Line = line;
                    p.Ring = Channel(line, p.Half + 1.5f);
                    break;
                }
                case Mood.Outcrop:
                    p.Rise = Rg(v, 1.1f, 2.4f, 30) * math.saturate(p.R / 14f + 0.3f);
                    p.Ring = LeisureKit.Blob(p.C, p.R * 0.92f, v, 18, 0.28f);
                    break;
                case Mood.Hollow:
                    p.Rise = -Rg(v, 0.6f, 1.15f, 31) * math.saturate(p.R / 14f + 0.4f);
                    p.Ring = LeisureKit.Blob(p.C, p.R * 0.9f, v, 18, 0.25f);
                    break;
                case Mood.Stones:
                    p.Rise = Rg(v, 0.5f, 1.1f, 32);
                    p.Ring = LeisureKit.Blob(p.C, p.R * 0.9f, v, 16, 0.2f);
                    break;
                default:
                    p.Ring = LeisureKit.Blob(p.C, p.R * 0.95f, v, 18, 0.3f);
                    break;
            }
        }

        /// <summary>The two banks of a centreline as one ring (left side forward, right side back).</summary>
        public static float2[] Channel(float2[] line, float half)
        {
            int n = line.Length;
            var ring = new float2[n * 2];
            for (int i = 0; i < n; i++)
            {
                var d = math.normalizesafe(line[math.min(n - 1, i + 1)] - line[math.max(0, i - 1)]);
                var nrm = new float2(-d.y, d.x);
                float taper = math.lerp(0.35f, 1f, math.sin(i / (float)(n - 1) * math.PI));
                ring[i] = line[i] + nrm * half * taper;
                ring[2 * n - 1 - i] = line[i] - nrm * half * taper;
            }
            return Poly.EnsureCcw(ring);
        }

        /// <summary>Distance from p to a polyline and the share t (0..1) along it of the nearest point.</summary>
        public static float LineDist(float2[] line, float2 p, out float t)
        {
            float best = float.MaxValue, acc = 0f, at = 0f, total = 0f;
            for (int i = 0; i + 1 < line.Length; i++) total += math.distance(line[i], line[i + 1]);
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i]; var b = line[i + 1];
                var ab = b - a; float l2 = math.max(1e-6f, math.lengthsq(ab));
                float u = math.saturate(math.dot(p - a, ab) / l2);
                float d = math.distance(p, a + ab * u);
                float seg = math.sqrt(l2);
                if (d < best) { best = d; at = acc + seg * u; }
                acc += seg;
            }
            t = total > 0f ? at / total : 0f;
            return best;
        }

        // ───────────────────────── the ground ─────────────────────────

        private static void Sculpt(LayerContext ctx, ParkSite s, List<MoodPlace> places)
        {
            var region = s.LayerRegion;
            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            var shaped = places.FindAll(p => p.Mood == Mood.Gully || p.Mood == Mood.Outcrop || p.Mood == Mood.Hollow || p.Mood == Mood.Stones);
            if (shaped.Count == 0) return;
            foreach (var p in shaped) { mn = math.min(mn, p.C - p.R - 4f); mx = math.max(mx, p.C + p.R + 4f); }
            // free only inside a place and clear of every path (the paths keep their ground)
            var sc = new Common.Sculpt(ctx.H, mn, mx, 3f, q =>
            {
                if (!region.ContainsDisc(q, 2f) || s.Occ.Corridors.Clearance(q, 3f) < 2.5f) return false;
                foreach (var p in shaped) if (math.distancesq(q, p.C) < (p.R + 1f) * (p.R + 1f)) return true;
                return false;
            });
            sc.Shape((q, b, t) =>
            {
                float off = 0f;
                foreach (var p in shaped)
                {
                    float d = math.distance(q, p.C);
                    if (d > p.R + 1f) continue;
                    // every place fades to nothing at its rim
                    float rim = 1f - Common.Sculpt.Smooth01((d - p.R * 0.55f) / (p.R * 0.45f));
                    float noise = (ValueNoise.Sample(q / 5f, p.Seed) - 0.5f) * 0.35f;
                    switch (p.Mood)
                    {
                        case Mood.Gully:
                        {
                            float dl = LineDist(p.Line, q, out float tt);
                            float ends = Common.Sculpt.Smooth01(tt / 0.22f) * Common.Sculpt.Smooth01((1f - tt) / 0.22f);
                            float prof = 1f - Common.Sculpt.Smooth01((dl - p.Floor) / math.max(0.5f, p.Half - p.Floor));
                            // the floor falls gently downhill along the channel; the banks are a little ragged
                            float depth = p.Depth * (0.85f + 0.3f * tt) * ends;
                            off += -depth * prof + noise * prof * (1f - prof) * 2.2f;
                            break;
                        }
                        case Mood.Outcrop:
                        {
                            float k = 1f - Common.Sculpt.Smooth01(d / (p.R * 0.95f));
                            float lump = 0.75f + 0.5f * ValueNoise.Sample(q / 7f, p.Seed + 3u);
                            off += p.Rise * k * lump + noise * k * 1.4f;
                            break;
                        }
                        case Mood.Hollow:
                        {
                            float k = 1f - Common.Sculpt.Smooth01(d / (p.R * 0.9f));
                            off += p.Rise * k * (0.85f + 0.3f * ValueNoise.Sample(q / 6f, p.Seed + 5u));
                            break;
                        }
                        case Mood.Stones:
                            off += p.Rise * (1f - Common.Sculpt.Smooth01(d / (p.R * 0.95f))) + noise * 0.3f * rim;
                            break;
                    }
                }
                return b + off;
            });
            sc.LimitGrade(0.42f, 10);
            var res = sc.Emit(ctx, null);
            if (res.Cells > 0)
            {
                ctx.Plan.Facts["moodTerrainCells"] = res.Cells;
                ctx.Plan.Facts["moodTerrainLower"] = math.round(-res.MinOffset * 10f) / 10f;
                ctx.Plan.Facts["moodTerrainRaise"] = math.round(res.MaxOffset * 10f) / 10f;
            }
        }

        // ───────────────────────── what stands in each place ─────────────────────────

        /// <summary>Places the rocks, logs and planting of every place, paints its ground and gives it to the ground design.</summary>
        public static void Emit(LayerContext ctx, ParkSite s, List<MoodPlace> places, bool neglected)
        {
            if (places == null || places.Count == 0) return;
            var kit = new Placemaker.Core.Paint.PaintKit(ctx.Plan.Paint, ctx.H, Hashing.Mix(ctx.Seed, 0x300Eu)) { Outline = ctx.LayerRegion.Ring };
            int rocks = 0, plants = 0;
            foreach (var p in places)
            {
                ctx.Check();
                var e = new Emitter(ctx, s, p, neglected);
                switch (p.Mood)
                {
                    case Mood.Gully: e.Gully(); break;
                    case Mood.Outcrop: e.Outcrop(); break;
                    case Mood.Hollow: e.Hollow(); break;
                    case Mood.Meadow: e.Meadow(); break;
                    case Mood.Glade: e.Glade(); break;
                    case Mood.Stones: e.StoneRing(); break;
                }
                e.Paint(kit);
                rocks += e.Rocks; plants += e.Plants;
                ctx.AddRegion(p.Ring, RegionPrefix + Ids[(int)p.Mood] + ":" + places.IndexOf(p), RegionPrefix + Ids[(int)p.Mood]);
                if (p.Mood == Mood.Gully) ctx.AddRegion(Channel(p.Line, p.Floor + 0.6f), FloorTag + ":" + places.IndexOf(p), FloorTag);
            }
            TerrainPaint.Warn(ctx);
            ctx.Plan.Facts["moodRocks"] = rocks;
            ctx.Plan.Facts["moodPlants"] = plants;
        }

        private sealed class Emitter
        {
            private readonly LayerContext _ctx;
            private readonly ParkSite _s;
            private readonly MoodPlace _p;
            private readonly bool _neglected;
            private readonly uint _v;
            private int _n;
            public int Rocks, Plants;
            private readonly List<(float2 p, float r)> _mine = new List<(float2, float)>();

            public Emitter(LayerContext ctx, ParkSite s, MoodPlace p, bool neglected) { _ctx = ctx; _s = s; _p = p; _neglected = neglected; _v = Hashing.Mix(p.Seed, 0xE1u); }

            private float U(int a) => Hashing.Unit(Hashing.Mix(_v, a, _n));
            private float R(float lo, float hi, int a) => math.lerp(lo, hi, U(a));
            private uint Var() => Hashing.Mix(_v, 0x77, _n);
            private bool Free(float2 q, float r)
            {
                if (!_s.LayerRegion.ContainsDisc(q, r + 1f)) return false;
                if (_s.Occ.Corridors.Overlaps(q, r, 0.8f)) return false;
                foreach (var m in _mine) if (math.distancesq(m.p, q) < (m.r + r) * (m.r + r)) return false;
                return true;
            }

            /// <summary>A boulder coming up through the ground: scaled, turned, tilted and sunk by a share of its height.</summary>
            private bool Boulder(float2 q, float scale, float sink, float tilt)
            {
                _n++;
                float r = 2.4f * scale;
                if (!Free(q, r * 0.6f)) return false;
                var o = _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Furniture, new AssetRef(Pick(Boulders, 2), Boulders), Var(), -1, AssetCategories.Rock);
                o.Scale = scale;
                o.Elevation = -sink * 1.6f * scale;   // the game's negative elevation: buried, colliding with nothing
                o.Pitch = (U(3) - 0.5f) * tilt; o.Roll = (U(4) - 0.5f) * tilt;
                _mine.Add((q, r * 0.6f));
                Rocks++;
                return true;
            }

            private void Stone(float2 q, float scale)
            {
                _n++;
                if (!Free(q, 0.5f * scale)) return;
                var o = _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Furniture, new AssetRef(Pick(Stones, 2), Stones), Var(), -1, AssetCategories.Rock);
                o.Scale = scale;
                o.Elevation = -0.12f * scale;
                o.Pitch = (U(3) - 0.5f) * 0.5f; o.Roll = (U(4) - 0.5f) * 0.5f;
                _mine.Add((q, 0.45f * scale));
                Rocks++;
            }

            private void Log(float2 q, float yaw, float sink = 0.08f)
            {
                _n++;
                if (!Free(q, 1.2f)) return;
                var o = _ctx.Object(q, yaw, ObjectRole.Furniture, new AssetRef(Pick(Logs, 2), Logs), Var(), -1, AssetCategories.Rock);   // "rock": the site harmony pass lets it follow the ground (no pad)
                o.Elevation = -sink;
                o.Scale = R(0.75f, 1.05f, 3);
                o.Roll = (U(4) - 0.5f) * 0.25f;
                // the log's length (10 m x scale) takes ground along it
                var d = new float2(math.sin(yaw), math.cos(yaw));
                for (int i = -2; i <= 2; i++) _mine.Add((q + d * i * 2.2f * o.Scale, 0.6f));
                Rocks++;
            }

            private bool Tree(float2 q, string[] species, int age, float spacing = 5.5f)
            {
                _n++;
                if (!_s.LayerRegion.ContainsDisc(q, 3.5f) || _s.Occ.Corridors.Overlaps(q, 2.5f, 0.35f) || _s.GateDistanceSq(q) < 100f) return false;
                foreach (var m in _mine) if (math.distancesq(m.p, q) < math.max(spacing, m.r + 2f) * math.max(spacing, m.r + 2f)) return false;
                if (_s.Occ.Discs.Overlaps(q, 2.5f, 0f) && !InOwn(q)) return false;
                _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Tree, new AssetRef(Pick(species, 2), species), Var(), age);
                _mine.Add((q, 2.6f));
                _s.Occ.Take(q, 2.5f, (int)ParkItemKind.Tree);
                Plants++;
                return true;
            }

            private bool Bush(float2 q, string[] species, float spacing = 2.4f)
            {
                _n++;
                if (!_s.LayerRegion.ContainsDisc(q, 1.6f) || _s.Occ.Corridors.Overlaps(q, 1.2f, 0.35f)) return false;
                foreach (var m in _mine) if (math.distancesq(m.p, q) < math.max(spacing, m.r + 0.9f) * math.max(spacing, m.r + 0.9f)) return false;
                if (_s.Occ.Discs.Overlaps(q, 1.2f, 0f) && !InOwn(q)) return false;
                _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Bush, new AssetRef(Pick(species, 2), species), Var());
                _mine.Add((q, 1.1f));
                Plants++;
                return true;
            }

            /// <summary>Inside this place's own disc (its occupancy is its own to plant).</summary>
            private bool InOwn(float2 q) => math.distance(q, _p.C) < _p.R * 0.98f;

            private string Pick(string[] a, int salt) => a[(int)(Hashing.Mix(_v, salt, _n) % (uint)a.Length)];
            private float2 Dir(float a) => new float2(math.cos(a), math.sin(a));
            private int OldOrDead(float deadChance) => U(9) < deadChance ? PlannedAge.Dead : U(10) < 0.6f ? PlannedAge.Old : PlannedAge.Adult;

            // ── gully ──
            public void Gully()
            {
                var line = _p.Line;
                float len = 0f; for (int i = 0; i + 1 < line.Length; i++) len += math.distance(line[i], line[i + 1]);
                // boulders along both banks, in clumps with gaps: the big ones on the outside of the bends
                float bank = (_p.Floor + _p.Half) * 0.5f;
                for (float a = R(1f, 3f, 11); a < len - 1f; a += R(2.6f, 6.5f, 12))
                {
                    var at = Along(line, a, out var tan);
                    var nrm = new float2(-tan.y, tan.x);
                    float t = a / len;
                    float ends = math.saturate(math.min(t, 1f - t) / 0.18f);
                    if (ends < 0.25f || U(13) < 0.22f) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (U(14 + side) < 0.35f) continue;
                        float sc = R(0.32f, 0.75f, 16) * (U(17) < 0.18f ? 1.6f : 1f) * math.lerp(0.7f, 1f, ends);
                        var q = at + nrm * side * bank * R(0.7f, 1.2f, 18) + tan * R(-1f, 1f, 19);
                        if (Boulder(q, sc, R(0.35f, 0.55f, 20), 0.6f) && U(21) < 0.55f)
                            Stone(q + nrm * side * R(1f, 2.2f, 22) + tan * R(-1.5f, 1.5f, 23), R(1.2f, 2.4f, 24));
                    }
                    // cobbles on the floor
                    int cob = (int)R(1f, 4f, 25);
                    for (int c = 0; c < cob; c++) Stone(at + nrm * R(-_p.Floor, _p.Floor, 26 + c) + tan * R(-2f, 2f, 30 + c), R(0.7f, 1.7f, 34 + c));
                }
                // a fallen log across the channel (and a dead tree that dropped it)
                if (U(40) < 0.7f)
                {
                    var at = Along(line, len * R(0.35f, 0.65f, 41), out var tan);
                    Log(at, math.atan2(tan.x, tan.y) + math.PI * 0.5f + R(-0.35f, 0.35f, 42), 0.05f);
                }
                // the rim: scrub in clumps, alders and birches at the ends and the bends
                RimPlanting(line, _p.Half + 1.2f, RimTrees, Scrub, 0.18f);
            }

            private void RimPlanting(float2[] line, float off, string[] trees, string[] bushes, float dead)
            {
                float len = 0f; for (int i = 0; i + 1 < line.Length; i++) len += math.distance(line[i], line[i + 1]);
                int t = 0;
                for (float a = R(0f, 4f, 50); a < len; a += R(3f, 7f, 51))
                {
                    var at = Along(line, a, out var tan);
                    var nrm = new float2(-tan.y, tan.x);
                    float side = U(52) < 0.5f ? -1f : 1f;
                    var q = at + nrm * side * (off + R(0f, 2.5f, 53));
                    if (U(54) < 0.25f && t < 4) { if (Tree(q + nrm * side * 1.5f, trees, t == 0 ? OldOrDead(dead) : PlannedAge.Adult)) t++; }
                    else
                    {
                        int k = (int)R(1f, 4f, 55);
                        for (int b = 0; b < k; b++) Bush(q + new float2(R(-2f, 2f, 56 + b), R(-2f, 2f, 60 + b)), bushes);
                    }
                }
            }

            // ── outcrop ──
            public void Outcrop()
            {
                // the crown: a clump of big stones off-centre, the biggest tilted against its neighbours
                var crown = _p.C + Dir(R(0f, 6.283f, 100)) * _p.R * R(0.05f, 0.25f, 101);
                int big = (int)R(3f, 6f, 102);
                Boulder(crown, R(0.95f, 1.35f, 103) * math.saturate(_p.R / 14f + 0.35f), 0.42f, 0.5f);
                for (int i = 0; i < big; i++)
                {
                    float a = R(0f, 6.283f, 104 + i);
                    Boulder(crown + Dir(a) * R(2.2f, 4.5f, 110 + i) * math.saturate(_p.R / 14f + 0.4f), R(0.45f, 0.95f, 116 + i), R(0.35f, 0.55f, 122 + i), 0.7f);
                }
                // stones breaking the turf on the slopes, and scree spilling downhill (toward the place's lowest side)
                var down = math.normalizesafe(_p.C - crown, Dir(R(0f, 6.283f, 128)));
                int out_ = (int)(_p.R * R(0.5f, 0.8f, 129));
                for (int i = 0; i < out_; i++)
                {
                    float a = R(0f, 6.283f, 130 + i), d = _p.R * math.sqrt(R(0.15f, 0.8f, 160 + i));
                    var q = _p.C + Dir(a) * d;
                    if (U(190 + i) < 0.35f) Boulder(q, R(0.25f, 0.5f, 200 + i), R(0.4f, 0.6f, 230 + i), 0.8f);
                    else Stone(q, R(0.9f, 2.2f, 260 + i));
                }
                for (int i = 0; i < 10; i++) Stone(_p.C + down * _p.R * R(0.45f, 0.95f, 300 + i) + new float2(-down.y, down.x) * R(-4f, 4f, 310 + i), R(0.6f, 1.4f, 320 + i));
                // a pine or a birch on the knoll, flowering scrub tucked between the stones
                int nt = U(330) < 0.6f ? 1 : 2;
                for (int i = 0; i < nt * 4 && nt > 0; i++) if (Tree(_p.C + Dir(R(0f, 6.283f, 331 + i)) * _p.R * R(0.35f, 0.75f, 340 + i), KnollTrees, U(350 + i) < 0.3f ? PlannedAge.Old : PlannedAge.Adult, 6f)) nt--;
                for (int i = 0; i < (int)(_p.R * 0.6f); i++) Bush(_p.C + Dir(R(0f, 6.283f, 360 + i)) * _p.R * R(0.25f, 0.85f, 400 + i), U(440 + i) < 0.5f ? Wildflowers : Scrub, 1.8f);
            }

            // ── hollow ──
            public void Hollow()
            {
                // wet trees round the rim, in a loose crescent (the dip stays open to the sky)
                float a0 = R(0f, 6.283f, 500), span = R(3.2f, 4.6f, 501);
                int nt = (int)math.clamp(_p.R / 3.5f, 3f, 7f);
                for (int i = 0; i < nt * 3 && nt > 0; i++)
                {
                    float a = a0 + R(0f, span, 502 + i);
                    if (Tree(_p.C + Dir(a) * _p.R * R(0.78f, 1.0f, 530 + i), WetTrees, i == 0 && _neglected ? PlannedAge.Dead : U(560 + i) < 0.25f ? PlannedAge.Old : PlannedAge.Adult, 5f)) nt--;
                }
                // rushes: low green scrub in drifts round the wet bottom
                for (int i = 0; i < (int)(_p.R * 1.4f); i++)
                {
                    float a = R(0f, 6.283f, 600 + i);
                    Bush(_p.C + Dir(a) * _p.R * R(0.35f, 0.7f, 650 + i), Rushes, 1.7f);
                }
                // a half-sunk log and a few stones at the water's edge
                if (U(700) < 0.6f) Log(_p.C + Dir(R(0f, 6.283f, 701)) * _p.R * R(0.2f, 0.45f, 702), R(0f, 6.283f, 703), 0.18f);
                for (int i = 0; i < 4; i++) Stone(_p.C + Dir(R(0f, 6.283f, 710 + i)) * _p.R * R(0.4f, 0.65f, 720 + i), R(0.8f, 1.6f, 730 + i));
            }

            // ── meadow ──
            public void Meadow()
            {
                int nt = (int)math.clamp(_p.R / 9f, 1f, 3f);
                for (int i = 0; i < nt * 4 && nt > 0; i++)
                    if (Tree(_p.C + Dir(R(0f, 6.283f, 800 + i)) * _p.R * R(0.2f, 0.7f, 820 + i), MeadowTrees, i == 0 ? PlannedAge.Old : PlannedAge.Adult, 12f)) nt--;
                // wild flowering shrubs in drifts (clumps of 3-6), not dots
                int drifts = (int)math.clamp(_p.R / 4f, 2f, 7f);
                for (int d = 0; d < drifts; d++)
                {
                    var c = _p.C + Dir(R(0f, 6.283f, 850 + d)) * _p.R * math.sqrt(R(0.05f, 0.8f, 870 + d));
                    int k = (int)R(3f, 7f, 890 + d);
                    for (int b = 0; b < k; b++) Bush(c + Dir(R(0f, 6.283f, 900 + b * 7 + d)) * R(0.8f, 3.5f, 960 + b * 7 + d), Wildflowers, 1.6f);
                }
            }

            // ── glade ──
            public void Glade()
            {
                // a ring of mixed trees round a clearing, thicker on one side
                float thick = R(0f, 6.283f, 1000);
                int nt = (int)math.clamp(_p.R * 0.9f, 6f, 18f);
                for (int i = 0; i < nt * 3 && nt > 0; i++)
                {
                    float a = R(0f, 6.283f, 1001 + i);
                    float side = 0.5f + 0.5f * math.cos(a - thick);
                    if (U(1060 + i) > 0.35f + 0.65f * side) continue;
                    float d = _p.R * R(0.55f, 0.98f, 1120 + i);
                    if (Tree(_p.C + Dir(a) * d, CopseTrees, U(1180 + i) < (_neglected ? 0.15f : 0.06f) ? PlannedAge.Dead : U(1240 + i) < 0.3f ? PlannedAge.Old : U(1300 + i) < 0.2f ? PlannedAge.Teen : PlannedAge.Adult, 5f)) nt--;
                }
                // the old tree in the clearing
                Tree(_p.C + Dir(R(0f, 6.283f, 1360)) * _p.R * R(0f, 0.2f, 1361), new[] { "OakTree01", "EU_ChestnutTree01" }, PlannedAge.Old, 4f);
                // logs where they fell, scrub under the trees
                int logs = (int)R(1f, 3.9f, 1370);
                for (int i = 0; i < logs; i++) Log(_p.C + Dir(R(0f, 6.283f, 1371 + i)) * _p.R * R(0.35f, 0.7f, 1380 + i), R(0f, 6.283f, 1390 + i));
                for (int i = 0; i < (int)(_p.R * 0.9f); i++)
                {
                    float a = thick + R(-1.6f, 1.6f, 1400 + i);
                    Bush(_p.C + Dir(a) * _p.R * R(0.6f, 1.0f, 1450 + i), Scrub, 2.2f);
                }
                if (U(1500) < 0.5f) Stone(_p.C + Dir(R(0f, 6.283f, 1501)) * _p.R * 0.5f, 2f);
            }

            // ── standing stones ──
            public void StoneRing()
            {
                int n = (int)R(5f, 9.9f, 1600);
                float rr = _p.R * R(0.38f, 0.55f, 1601);
                float a0 = R(0f, 6.283f, 1602);
                for (int i = 0; i < n; i++)
                {
                    _n++;
                    if (U(1603) < 0.12f) continue;   // a fallen gap
                    float a = a0 + i * 6.283f / n + R(-0.12f, 0.12f, 1604);
                    var q = _p.C + Dir(a) * rr;
                    if (!Free(q, 0.8f)) continue;
                    var o = _ctx.Object(q, a + math.PI * 0.5f + R(-0.3f, 0.3f, 1605), ObjectRole.Furniture, new AssetRef(Pick(StandingStones, 2), StandingStones), Var(), -1, AssetCategories.Rock);
                    o.Scale = R(0.85f, 1.25f, 1606);
                    o.Elevation = -0.15f;
                    o.Pitch = (U(1607) - 0.5f) * 0.18f; o.Roll = (U(1608) - 0.5f) * 0.18f;
                    _mine.Add((q, 0.9f));
                    Rocks++;
                }
                for (int i = 0; i < 8; i++) Stone(_p.C + Dir(R(0f, 6.283f, 1620 + i)) * _p.R * R(0.2f, 0.8f, 1630 + i), R(0.8f, 1.8f, 1640 + i));
                for (int i = 0; i < 5; i++) Bush(_p.C + Dir(R(0f, 6.283f, 1660 + i)) * _p.R * R(0.7f, 0.95f, 1670 + i), Scrub, 2.5f);
            }

            // ── paint ──
            public void Paint(Placemaker.Core.Paint.PaintKit kit)
            {
                uint v = Hashing.Mix(_v, 0xB1u);
                switch (_p.Mood)
                {
                    case Mood.Gully:
                        TerrainPaint.Patch(_ctx, Channel(_p.Line, _p.Half * 0.95f), TerrainMaterials.Dry, 0.55f, 3f);
                        TerrainPaint.Patch(_ctx, Channel(_p.Line, _p.Floor + 1.2f), TerrainMaterials.Dirt, 0.8f, 1.5f);
                        break;
                    case Mood.Outcrop:
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.95f, v, 16, 0.3f), TerrainMaterials.Dry, 0.7f, 3f);
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.5f, v + 1u, 14, 0.35f), TerrainMaterials.Dirt, 0.6f, 2.5f);
                        break;
                    case Mood.Hollow:
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.85f, v, 16, 0.25f), TerrainMaterials.Lush, 0.75f, 3f);
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.35f, v + 1u, 14, 0.3f), TerrainMaterials.Dirt, 0.55f, 2f);
                        break;
                    case Mood.Meadow:
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.95f, v, 18, 0.3f), TerrainMaterials.Dry, 0.85f, 4f);
                        break;
                    case Mood.Glade:
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.95f, v, 18, 0.25f), TerrainMaterials.Dirt, 0.45f, 4f);
                        break;
                    case Mood.Stones:
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.9f, v, 16, 0.25f), TerrainMaterials.Dry, 0.75f, 3f);
                        break;
                }
            }

            private static float2 Along(float2[] line, float a, out float2 tan)
            {
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    float l = math.distance(line[i], line[i + 1]);
                    if (a <= l || i + 2 == line.Length)
                    {
                        tan = math.normalizesafe(line[i + 1] - line[i]);
                        return math.lerp(line[i], line[i + 1], math.saturate(a / math.max(1e-4f, l)));
                    }
                    a -= l;
                }
                tan = new float2(1, 0);
                return line[0];
            }
        }
    }
}

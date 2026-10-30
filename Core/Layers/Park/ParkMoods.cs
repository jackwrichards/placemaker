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
    public enum Mood { Gully = 0, Outcrop = 1, Hollow = 2, Meadow = 3, Glade = 4, Stones = 5, Allotment = 6 }

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
        /// <summary>The ground the place may use: a lawn between the paths inset 3 m (or a blob in the open lawn). R is its inscribed radius, C its deepest point.</summary>
        public float2[] Lawn;
    }

    public static class ParkMoods
    {
        public const string RegionPrefix = "mood:";
        public const string FloorTag = "mood:gullyFloor";
        public static readonly string[] Ids = { "gully", "outcrop", "hollow", "meadow", "glade", "stones", "allotment" };

        public static readonly string[] Boulders = { "Boulder01", "Boulder02", "Boulder03", "Boulder04", "Boulder05", "Boulder06", "Boulder07", "Boulder08" };
        public static readonly string[] Stones = { "Rock01", "Rock03", "Rock05", "Rock07", "Rock09", "Rock11", "Rock13", "Rock15", "Rock17", "Rock19", "Rock21", "Rock23", "Rock25", "Rock27", "Rock29",
            "Rock02", "Rock04", "Rock06", "Rock08", "Rock10", "Rock12", "Rock14", "Rock16", "Rock18", "Rock20", "Rock22", "Rock24", "Rock26", "Rock28", "Rock30" };
        public static readonly string[] Logs = { "Log02", "Log05", "Log01", "Log04", "Log08", "Log11", "Log07", "Log14" };
        public static readonly string[] StandingStones = { "StandingStone01", "StandingStone02", "StandingStone03", "StandingStone04", "StandingStone05", "StandingStone06", "StandingStone07", "StandingStone08" };
        private static readonly string[] WetTrees = { "EU_PoplarTree01", "EU_AlderTree01", "EU_PoplarTree01", "BirchTree01" };   // columnar poplars mark the hollow from afar
        private static readonly string[] RimTrees = { "EU_AlderTree01", "BirchTree01", "OakTree01" };
        private static readonly string[] KnollTrees = { "PineTree01", "BirchTree01", "PineTree01" };
        // dark spires against pale meadow: the copse reads from 45° as a dark mass with a few broadleaves in it
        private static readonly string[] CopseTrees = { "SpruceTree01", "PineTree01", "SpruceTree01", "OakTree01", "PineTree01", "BirchTree01", "SpruceTree01" };
        private static readonly string[] MeadowTrees = { "OakTree01", "BirchTree01", "EU_ChestnutTree01" };
        private static readonly string[] Scrub = { "GreenBushWild01", "GreenBushWildAdult01", "GreenBushWild02", "GreenBushWildElderly01", "FlowerBushWild02" };
        private static readonly string[] Rushes = { "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWild01" };
        private static readonly string[] Wildflowers = { "FlowerBushWild02", "FlowerBushWild02", "FlowerBushWildChild02", "FlowerBushWild02" };

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
                // the strict geometric idioms keep their geometry: no moods
                case "formal": case "modern": case "victorian": return new[] { 0f, 0f, 0f, 0f, 0f, 0f };
                case "botanical": return new[] { 0.5f, 1.8f, 1.1f, 1.4f, 0.6f, 0f };
                case "playground": return new[] { 0.9f, 1.6f, 0.4f, 0.6f, 1.4f, 0f };
                case "memorial": return new[] { 0f, 0.2f, 0.3f, 1.4f, 1.0f, 1.3f };
                case "none": return new[] { 0f, 0f, 0f, 0f, 0f, 0f };   // places off: only the community garden, when asked
                default: return new[] { 1.2f, 1.1f, 1.0f, 1.0f, 1.0f, 0.15f };
            }
        }

        private static float U(uint seed, int a, int b = 0) => Hashing.Unit(Hashing.Mix(seed, a, b));
        private static float Sum(float[] a) { float s = 0f; foreach (var x in a) s += x; return s; }
        private static float Rg(uint seed, float lo, float hi, int a, int b = 0) => math.lerp(lo, hi, U(seed, a, b));

        // ───────────────────────── choosing the places ─────────────────────────

        /// <summary>
        /// Picks the places and their moods, shapes the ground (one Sculpt, emitted now so what is placed later stands on it)
        /// and takes their ground in the park's occupancy (the planting and furniture keep out). Call before furniture.
        ///
        /// A place is a whole lawn between the paths (inset 3 m from them), so every lawn of a park can be a room with its own
        /// vibe; the lawn that holds the playground or the protected clearing stays the open lawn, and a big open lawn can still
        /// give one disc-shaped place at its far side. (First try, discs of 8-38 m anywhere: in the game they rarely fitted
        /// between the paths and read as nothing, run show-5-builds-1030-000121-ff10.)
        /// </summary>
        public static List<MoodPlace> Plan(LayerContext ctx, ParkSite s, List<float2[]> lawns, string style, bool terrain, float amount = 1f, bool allotment = false)
        {
            var places = new List<MoodPlace>();
            var region = s.LayerRegion;
            if (amount <= 0f || region.Area < 1500f || lawns == null) return places;
            // with the Terrain feature on, its landform is the park's character and the places step aside (only the community garden,
            // when asked, which shapes nothing): the two together moved the picnic onto a 0.84 m step on a hill (SiteHarmonyTests)
            if (ctx.Input != null && ctx.Input.HasChild("terrain")) { terrain = false; if (!allotment) return places; style = "none"; }
            var pal = Palette(style);
            uint seed = Hashing.Mix(ctx.Seed, 0x300Du);
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

            // candidates: the lawns, inset from the paths, biggest first; the open lawn (playground, clearing) offers a disc only
            var cands = new List<(float2[] ring, float2 c, float ins, float area, bool core)>();
            foreach (var l in lawns)
            {
                if (l == null || l.Length < 3) continue;
                float a = math.abs(Poly.Area(l));
                if (a < 380f) continue;
                var inset = Placemaker.Core.Decals.Dynamic.Brushes.Inset(Poly.EnsureCcw((float2[])l.Clone()), 3f);
                if (inset == null || inset.Length < 3 || math.abs(Poly.Area(inset)) < 200f) continue;
                inset = Poly.EnsureCcw(inset);
                var lr = LayerRegion.From(inset);
                if (lr == null) continue;
                var c = lr.DeepestPoint(out float ins, null, 14);
                if (ins < 4f) continue;
                bool core = s.ClearingRadius > 0f && Poly.Contains(l, s.ClearingCenter);
                // a lawn that already holds something of the type (a playground clearing, a play zone, a hub: any taken disc of 3 m or
                // more) keeps it: the lawn is the open one and offers only a disc place clear of it
                foreach (var d in s.Occ.Discs.Items) if ((d.Kind == 200 || d.R >= 3f && d.Kind != 205) && Poly.Contains(l, d.P)) { core = true; break; }
                if (s.Lake.Length >= 3 && Poly.Contains(l, Poly.Centroid(s.Lake))) core = true;
                foreach (var poly in s.Occ.Polygons) if (poly != null && poly.Length >= 3 && Poly.Contains(l, Poly.Centroid(poly))) { core = true; break; }
                cands.Add((inset, c, ins, math.abs(Poly.Area(inset)), core));
            }
            cands.Sort((x, y) => y.area.CompareTo(x.area));
            // a park without lawn rooms (one big lawn, few paths) offers discs in its open lawn instead
            int lawnRooms = 0;
            foreach (var cd in cands) if (!cd.core) lawnRooms++;
            int maxPlaces = (int)math.clamp(math.round((lawnRooms + 2.5f) * amount), 1f, math.min(14f, 6f + region.Area / 40000f));   // a big park has more places
            if (Sum(pal) <= 0f) maxPlaces = allotment ? 1 : 0;
            var taken = new List<float2>();
            foreach (var g in s.Gates) taken.Add(g.Position);
            // the open lawn keeps its heart, but a big one gives one or two places at its sides (a park with few paths has
            // few lawn rooms: one entrance in the game made 1-2 places, run show-24-builds-1030-003548-0555)
            var order = new List<(float2[] ring, float2 c, float ins, float area, bool core)>();
            foreach (var cd in cands)
            {
                if (!cd.core) { order.Add(cd); continue; }
                int tries = cd.area >= 6000f ? 2 : cd.area >= 1800f ? 1 : 0;
                for (int t = 0; t < tries; t++) order.Add(cd);
            }
            int k = 0;
            foreach (var cd in order)
            {
                if (places.Count >= maxPlaces) break;
                ctx.Check();
                uint ks = Hashing.Mix(seed, (uint)k++);
                float2[] ring = cd.ring; float2 c = cd.c; float r = cd.ins;
                if (cd.core)
                {
                    float rr = math.clamp(math.sqrt(cd.area) * 0.17f, 8f, 18f);
                    var occ = new Occupancy(LayerRegion.From(ring), ctx.Site, s.Occ.Discs, s.Occ.Corridors);
                    bool found = false;
                    for (int t = 0; t < 3 && !found; t++)
                    {
                        float rt = rr * (1f - t * 0.2f);
                        found = Spots.Find(occ, rt, t == 1 ? Spots.Preference.Open : Spots.Preference.NearBoundary, Hashing.Mix(ks, 2u + (uint)t), out c, taken, rt + 8f, 0.5f, 1200)
                            && !s.InClearing(c, rt * 0.8f) && !s.InLake(c, rt + 2f);
                        if (found) rr = rt;
                    }
                    if (!found) continue;
                    r = rr; ring = Within(LeisureKit.Blob(c, rr * 0.95f, ks, 18, 0.25f), cd.ring, c);
                    s.Occ.Take(c, rr, 205);   // a later disc in the same lawn keeps clear of it
                }
                // what the land says here
                float h0 = ctx.H(c);
                var grad = Grad(ctx, c, math.max(4f, r * 0.6f));
                float slope = math.length(grad);
                float low = math.saturate((mean - h0) / relief * 2f + 0.5f);   // 1 = low ground
                float len = LongExtent(ring, out _);
                var w = new float[Ids.Length];
                float total = 0f;
                for (int pass = 0; pass < 2 && total <= 0f; pass++)
                {
                    total = 0f;
                    int fresh = 0;
                    for (int q = 0; q < pal.Length; q++) if (pal[q] > 0f && used[q] == 0) fresh++;
                    for (int m = 0; m < w.Length; m++)
                    {
                        float x = m < pal.Length ? pal[m] : 0f;
                        if (x <= 0f) { w[m] = 0f; continue; }
                        switch ((Mood)m)
                        {
                            // a gully wants a long room (it runs the length of it) and loves a slope
                            case Mood.Gully: x *= (r < 6.5f || len < 24f ? 0.05f : math.saturate(len / 45f) + 0.3f) * (1f + math.saturate(slope / 0.04f) * 2.5f); break;
                            case Mood.Hollow: x *= 0.6f + low * 1.8f; break;
                            case Mood.Outcrop: x *= 0.7f + (1f - low) * 1.2f + math.saturate(slope / 0.06f); break;
                            case Mood.Stones: x *= r < 8f ? 0f : 0.6f + (1f - low); break;
                            case Mood.Glade: x *= r < 6f ? 0.4f : 1f; break;
                        }
                        // the user asked for rock first ("rock sections, barrancas"): the first place leans to rock where the style has it
                        if (places.Count == 0 && ((Mood)m == Mood.Gully || (Mood)m == Mood.Outcrop)) x *= 2.5f;
                        // never twice the same mood while another of the palette is unused
                        if (used[m] > 0 && fresh > 0 && pass == 0) x = 0f;
                        else if (used[m] > 1) x *= 0.1f;
                        w[m] = x; total += x;
                    }
                }
                // the community garden option: the first lawn room big enough becomes the allotments
                bool allot = allotment && used[(int)Mood.Allotment] == 0 && !cd.core && cd.area >= 380f;
                if (total <= 0f && !allot) { if (pal.Length > 0 && Sum(pal) > 0f) break; continue; }
                float pick = U(ks, 7) * total;
                int mood = 0;
                for (int m = 0; m < w.Length; m++) { pick -= w[m]; if (pick <= 0f) { mood = m; break; } }
                if (allot) mood = (int)Mood.Allotment;
                // a knoll, a hollow or a stone ring has a size of its own: in a big lawn it takes a piece round the lawn's heart and
                // the rest of the lawn stays the park's (whole big lawns spread 50 stones thin, run show-24-builds-1030-001417-7e24)
                float cap = Cap((Mood)mood);
                if (r > cap + 2f)
                {
                    ring = Within(LeisureKit.Blob(c, cap + 4f, ks, 18, 0.22f), ring, c);
                    r = cap;
                }
                var place = new MoodPlace { Mood = (Mood)mood, C = c, R = r, Seed = ks, Lawn = ring };
                Shape(ctx, s, place, grad);
                places.Add(place);
                used[mood]++;
                taken.Add(c);
                // the place's ground is its own: the park's planting, groves and furniture keep out (it plants its own)
                s.Occ.TakePolygon(ring);
            }
            if (places.Count == 0) return places;
            if (terrain) Sculpt(ctx, s, places);
            float area = 0f;
            foreach (var p in places) area += math.abs(Poly.Area(p.Lawn));
            for (int m = 0; m < Ids.Length; m++) { ctx.Plan.Facts.Remove("mood." + Ids[m]); if (used[m] > 0) ctx.Plan.Facts["mood." + Ids[m]] = used[m]; }
            ctx.Plan.Facts["moodPlaces"] = places.Count;
            ctx.Plan.Facts["moodArea"] = math.round(area);
            return places;
        }

        /// <summary>The largest inscribed radius a mood uses (m); a meadow, a copse and a gully take a whole lawn.</summary>
        private static float Cap(Mood m)
        {
            switch (m)
            {
                case Mood.Outcrop: return 15f;
                case Mood.Hollow: return 14f;
                case Mood.Stones: return 10f;
                case Mood.Glade: return 22f;
                default: return 999f;
            }
        }

        /// <summary>The blob's points pulled toward <paramref name="c"/> until they lie inside the lawn (a blob clipped to its lawn, near enough).</summary>
        private static float2[] Within(float2[] blob, float2[] lawn, float2 c)
        {
            var o = new float2[blob.Length];
            for (int i = 0; i < blob.Length; i++)
            {
                var q = blob[i];
                for (int k = 0; k < 12 && !Poly.Contains(lawn, q); k++) q = math.lerp(q, c, 0.2f);
                o[i] = q;
            }
            return Poly.EnsureCcw(o);
        }

        /// <summary>The ground the places took (m²): the park plants that much less of its own.</summary>
        public static float Area(List<MoodPlace> places)
        {
            float a = 0f;
            if (places != null) foreach (var p in places) a += math.abs(Poly.Area(p.Lawn));
            return a;
        }

        /// <summary>Half the ring's extent along its long axis (principal axis of its vertices); the axis.</summary>
        private static float LongExtent(float2[] ring, out float2 axis)
        {
            var c = Poly.Centroid(ring);
            float sxx = 0f, syy = 0f, sxy = 0f;
            foreach (var q in ring) { var d = q - c; sxx += d.x * d.x; syy += d.y * d.y; sxy += d.x * d.y; }
            float ang = 0.5f * math.atan2(2f * sxy, sxx - syy);
            axis = new float2(math.cos(ang), math.sin(ang));
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var q in ring) { float u = math.dot(q - c, axis); lo = math.min(lo, u); hi = math.max(hi, u); }
            return hi - lo;
        }

        private static float2 Grad(LayerContext ctx, float2 c, float d)
        {
            float hx = ctx.H(c + new float2(d, 0)) - ctx.H(c - new float2(d, 0));
            float hz = ctx.H(c + new float2(0, d)) - ctx.H(c - new float2(0, d));
            return new float2(hx, hz) / (2f * d);
        }

        /// <summary>Distance from q to the place's lawn edge (negative outside).</summary>
        internal static float Edge(MoodPlace p, float2 q)
        {
            float d = Poly.DistanceToBoundary(p.Lawn, q);
            return Poly.Contains(p.Lawn, q) ? d : -d;
        }

        /// <summary>The place's outline and, for a gully, its channel along the room's length (or down the fall of the land).</summary>
        private static void Shape(LayerContext ctx, ParkSite s, MoodPlace p, float2 grad)
        {
            uint v = p.Seed;
            float depthK = math.saturate(p.R / 12f + 0.35f);
            switch (p.Mood)
            {
                case Mood.Gully:
                {
                    float len = LongExtent(p.Lawn, out var axis);
                    // down the fall when the land falls clearly and the room is long enough that way
                    if (math.length(grad) > 0.025f)
                    {
                        var down = -math.normalize(grad);
                        if (math.abs(math.dot(down, axis)) > 0.5f) axis = down * math.sign(math.dot(down, axis));
                        else if (math.dot(axis, down) < 0f) axis = -axis;
                    }
                    var side = new float2(-axis.y, axis.x);
                    p.Floor = math.clamp(p.R * 0.14f, 1.1f, 2.2f);
                    p.Half = math.min(p.Floor + 2.4f / 0.3f, p.R - 0.6f);
                    p.Depth = math.clamp((p.Half - p.Floor) * 0.3f, 0.7f, 2.4f);   // the banks average 30 % (steepest ~45 %: never a sheet)
                    // the centreline: through the room's deepest point along its length, wandering, trimmed so its banks stay inside
                    var c = Poly.Centroid(p.Lawn);
                    var mid = p.C;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var q in p.Lawn) { float u = math.dot(q - mid, axis); lo = math.min(lo, u); hi = math.max(hi, u); }
                    int n = 17;
                    var pts = new List<float2>();
                    float ph = U(v, 21) * 6.283f, f = Rg(v, 0.7f, 1.4f, 22);
                    float amp = math.min(p.R * 0.35f, 6f);
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)(n - 1);
                        float u = math.lerp(lo, hi, t);
                        float wob = math.sin(t * math.PI * f * 2f + ph) * amp;
                        var q = mid + axis * u + side * wob;
                        // pull the line toward the middle of the room where it runs thin
                        for (int it = 0; it < 4 && Edge(p, q) < p.Half * 0.75f; it++)
                        {
                            float best = Edge(p, q); var bq = q;
                            foreach (float sgn in new[] { -1f, 1f })
                            {
                                var q2 = q + side * sgn * 1.5f;
                                float e2 = Edge(p, q2);
                                if (e2 > best) { best = e2; bq = q2; }
                            }
                            if (bq.Equals(q)) break;
                            q = bq;
                        }
                        if (Edge(p, q) >= math.max(1.5f, p.Half * 0.55f)) pts.Add(q);
                    }
                    if (pts.Count < 4)
                    {
                        // too thin for a channel: a rocky knoll instead
                        p.Mood = Mood.Outcrop;
                        goto case Mood.Outcrop;
                    }
                    p.Line = pts.ToArray();
                    p.Ring = p.Lawn;
                    break;
                }
                case Mood.Outcrop:
                    p.Rise = Rg(v, 1.3f, 2.6f, 30) * depthK;
                    p.Ring = p.Lawn;
                    break;
                case Mood.Hollow:
                    p.Rise = -Rg(v, 0.7f, 1.3f, 31) * depthK;
                    p.Ring = p.Lawn;
                    break;
                case Mood.Stones:
                    p.Rise = Rg(v, 0.5f, 1.1f, 32);
                    p.Ring = p.Lawn;
                    break;
                default:
                    p.Ring = p.Lawn;
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
            foreach (var p in shaped) foreach (var q in p.Lawn) { mn = math.min(mn, q - 4f); mx = math.max(mx, q + 4f); }
            // free only inside a place's lawn and clear of every path (the paths keep their ground)
            var sc = new Common.Sculpt(ctx.H, mn, mx, 3f, q =>
            {
                if (!region.ContainsDisc(q, 5f) || s.Occ.Corridors.Clearance(q, 3f) < 2.5f || ctx.Input?.Reserved != null && ctx.Input.Reserved.Blocks(q, 2f)) return false;
                foreach (var p in shaped) if (Poly.Contains(p.Lawn, q)) return true;
                return false;
            });
            sc.Shape((q, b, t) =>
            {
                float off = 0f;
                foreach (var p in shaped)
                {
                    if (!Poly.Contains(p.Lawn, q)) continue;
                    float e = Poly.DistanceToBoundary(p.Lawn, q);
                    // every place fades to nothing at its lawn's edge, following the lawn's own shape
                    float body = Common.Sculpt.Smooth01(e / math.max(3f, math.min(p.R, 16f) * 0.9f));
                    float noise = (ValueNoise.Sample(q / 5f, p.Seed) - 0.5f) * 0.35f;
                    switch (p.Mood)
                    {
                        case Mood.Gully:
                        {
                            float dl = LineDist(p.Line, q, out float tt);
                            float ends = Common.Sculpt.Smooth01(tt / 0.18f) * Common.Sculpt.Smooth01((1f - tt) / 0.18f);
                            float prof = 1f - Common.Sculpt.Smooth01((dl - p.Floor) / math.max(0.5f, p.Half - p.Floor));
                            float edge = Common.Sculpt.Smooth01(e / math.max(2.5f, p.Depth / 0.3f));
                            // the floor falls gently downhill along the channel; the banks are a little ragged
                            float depth = p.Depth * (0.85f + 0.3f * tt) * ends * edge;
                            off += -depth * prof + noise * prof * (1f - prof) * 2.2f;
                            break;
                        }
                        case Mood.Outcrop:
                        {
                            float lump = 0.7f + 0.6f * ValueNoise.Sample(q / 7f, p.Seed + 3u);
                            float toward = 1f - 0.35f * Common.Sculpt.Smooth01(math.distance(q, p.C) / math.max(6f, p.R * 2.2f));
                            off += p.Rise * body * lump * toward + noise * body * 1.4f;
                            break;
                        }
                        case Mood.Hollow:
                            off += p.Rise * body * (0.85f + 0.3f * ValueNoise.Sample(q / 6f, p.Seed + 5u));
                            break;
                        case Mood.Stones:
                            off += p.Rise * body + noise * 0.3f * body;
                            break;
                    }
                }
                // the paths keep their level band and shoulders: the change dies out from 4 m to 9 m off every path (a bank against a
                // levelled path band read as a curtain to the harmony checks on a hill)
                if (off != 0f) off *= Common.Sculpt.Smooth01((s.Occ.Corridors.Clearance(q, 10f) - 4f) / 5f);
                return b + off;
            });
            sc.LimitGrade(0.28f, 14);                   // the change itself no steeper than 28 % (no step against the ground round it)
            sc.LimitGrade(0.34f, 14, absolute: true);   // banks at most 34 % over the ground as it is (on a hill too): under 0.65 m a heightmap texel (site harmony calls 0.8 m a curtain)
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
                    case Mood.Allotment: e.Allotments(); break;
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

            private readonly string[] _trees, _bushes;

            public Emitter(LayerContext ctx, ParkSite s, MoodPlace p, bool neglected)
            {
                _ctx = ctx; _s = s; _p = p; _neglected = neglected; _v = Hashing.Mix(p.Seed, 0xE1u);
                // the player's species choice (when the host has one) is used for every tree and bush of the places too
                _trees = ctx.L("treeSpecies") ?? Array.Empty<string>();
                _bushes = ctx.L("bushSpecies") ?? Array.Empty<string>();
                float td = ctx.F("treeDensity");
                _density = td > 0f && ctx.ControlOf?.Invoke("treeDensity") != null ? math.saturate(td / 26f) : 1f;
                if (ctx.ControlOf?.Invoke("treeDensity") != null && td <= 0f) _density = 0f;
            }
            private readonly float _density;

            private float U(int a) => Hashing.Unit(Hashing.Mix(_v, a, _n));
            private float R(float lo, float hi, int a) => math.lerp(lo, hi, U(a));
            private uint Var() => Hashing.Mix(_v, 0x77, _n);
            /// <summary>Space another step reserved (a parking lot, a centrepiece of a mixed recipe).</summary>
            private bool Reserved(float2 q, float r) => _ctx.Input?.Reserved != null && _ctx.Input.Reserved.Blocks(q, r);
            private bool Free(float2 q, float r)
            {
                if (!_s.LayerRegion.ContainsDisc(q, r + 1f) || !Mine(q, -0.5f) || Reserved(q, r)) return false;
                if (_s.Occ.Corridors.Overlaps(q, r, 0.8f)) return false;
                foreach (var m in _mine) if (math.distancesq(m.p, q) < (m.r + r) * (m.r + r)) return false;
                return true;
            }

            /// <summary>A boulder coming up through the ground: scaled, turned, tilted and sunk by a share of its height.</summary>
            private bool Boulder(float2 q, float scale, float sink, float tilt, bool pack = false)
            {
                _n++;
                float r = 2.4f * scale;
                // packed boulders (an outcrop's crown) lean into each other: only paths, the outline and reserved space stop them
                if (pack ? !_s.LayerRegion.ContainsDisc(q, r * 0.6f + 1f) || _s.Occ.Corridors.Overlaps(q, r * 0.6f, 0.8f) || Reserved(q, r * 0.6f) || !Mine(q, -0.5f) : !Free(q, r * 0.6f)) return false;
                var o = _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Furniture, new AssetRef(Pick(Boulders, 2), Boulders), Var(), -1, AssetCategories.Rock);
                o.Scale = scale;
                o.Elevation = -sink * 1.6f * scale;   // the game's negative elevation: buried, colliding with nothing
                o.Pitch = (U(3) - 0.5f) * tilt; o.Roll = (U(4) - 0.5f) * tilt;
                _mine.Add((q, r * 0.6f));
                Rocks++;
                // a boulder never sits alone on the turf: broken pieces lie at its foot, more on its downhill side (a boulder
                // dropped on the lawn read as a prop, run show-24-builds-1030-001417-7e24)
                int bits = 2 + (int)(U(5) * 3f);
                for (int i = 0; i < bits; i++)
                {
                    float a = U(6 + i) * 6.283f;
                    float d = r * (0.55f + 0.45f * U(12 + i));
                    var p = q + new float2(math.cos(a), math.sin(a)) * d;
                    if (_s.Occ.Corridors.Overlaps(p, 0.6f, 0.5f) || !Mine(p, -1f) || Reserved(p, 0.6f)) continue;
                    var o2 = _ctx.Object(p, U(20 + i) * 6.283f, ObjectRole.Furniture, new AssetRef(Pick(Stones, 30 + i), Stones), Var(), -1, AssetCategories.Rock);
                    o2.Scale = 0.7f + 1.3f * U(40 + i) * math.saturate(scale + 0.3f);
                    o2.Elevation = -0.15f * o2.Scale;
                    o2.Pitch = (U(50 + i) - 0.5f) * 0.6f; o2.Roll = (U(60 + i) - 0.5f) * 0.6f;
                    Rocks++;
                }
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
                if (_trees.Length == 0 && !AssetAvailability.Has(AssetCategories.Tree)) return false;   // a playset without trees
                // the park's tree density speaks for the places too (26 a hectare = as designed)
                if (U(11) > _density) return false;
                if (!_s.LayerRegion.ContainsDisc(q, 3.5f) || !Mine(q, 0.5f) || _s.Occ.Corridors.Overlaps(q, 2.5f, 0.35f) || _s.GateDistanceSq(q) < 100f) return false;
                foreach (var m in _mine) if (math.distancesq(m.p, q) < math.max(spacing, m.r + 2f) * math.max(spacing, m.r + 2f)) return false;
                if (_s.Occ.Discs.Overlaps(q, 2.5f, 0f) && !InOwn(q) || Reserved(q, 2.5f)) return false;
                if (_trees.Length > 0) species = _trees;   // the player's own species win
                _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Tree, new AssetRef(Pick(species, 2), species), Var(), age);
                _mine.Add((q, 2.6f));
                _s.Occ.Take(q, 2.5f, (int)ParkItemKind.Tree);
                Plants++;
                return true;
            }

            private bool Bush(float2 q, string[] species, float spacing = 2.4f)
            {
                _n++;
                if (!_s.LayerRegion.ContainsDisc(q, 1.6f) || !Mine(q, 0.2f) || _s.Occ.Corridors.Overlaps(q, 1.2f, 0.35f)) return false;
                foreach (var m in _mine) if (math.distancesq(m.p, q) < math.max(spacing, m.r + 0.9f) * math.max(spacing, m.r + 0.9f)) return false;
                if (_s.Occ.Discs.Overlaps(q, 1.2f, 0f) && !InOwn(q) || Reserved(q, 1.2f)) return false;
                if (_bushes.Length > 0) species = _bushes;
                else if (!AssetAvailability.Has(AssetCategories.Bush)) return false;
                _ctx.Object(q, R(0f, 6.283f, 1), ObjectRole.Bush, new AssetRef(Pick(species, 2), species), Var());
                _mine.Add((q, 1.1f));
                Plants++;
                return true;
            }

            /// <summary>Inside this place's own ground (its occupancy is its own to plant).</summary>
            private bool InOwn(float2 q) => Poly.Contains(_p.Lawn, q);
            /// <summary>Inside the place's lawn, at least <paramref name="margin"/> from its edge (negative: that far outside is fine).</summary>
            private bool Mine(float2 q, float margin) => ParkMoods.Edge(_p, q) >= margin;
            /// <summary>A point of the place's lawn between eMin and eMax from its edge (the lawn's own shape, not a disc); NaN-free fallback: the deepest point.</summary>
            private float2 Pt(int salt, float eMin = 0.5f, float eMax = 999f)
            {
                Poly.Bounds(_p.Lawn, out var lo, out var hi);
                for (int t = 0; t < 16; t++)
                {
                    var q = math.lerp(lo, hi, new float2(Hashing.Unit(Hashing.Mix(_v, salt * 31 + t, 1)), Hashing.Unit(Hashing.Mix(_v, salt * 31 + t, 2))));
                    float e = ParkMoods.Edge(_p, q);
                    if (e >= eMin && e <= eMax) return q;
                }
                return _p.C;
            }

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
                        bool placed = Boulder(q, sc, R(0.35f, 0.55f, 20), 0.6f);
                        // banks of a barranca are broken rock: a second stone leans on the first more often than not
                        if (placed && U(27) < 0.6f) Boulder(q + tan * R(1.2f, 2.6f, 28) * (U(29) < 0.5f ? -1f : 1f) + nrm * side * R(-0.6f, 0.8f, 31), sc * R(0.5f, 0.85f, 32), R(0.35f, 0.55f, 33), 0.9f, true);
                        if (placed && U(21) < 0.55f)
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
                float k = math.saturate(_p.R / 12f + 0.35f);
                // the crown: a clump of big stones off-centre, the biggest tilted against its neighbours
                var crown = _p.C + Dir(R(0f, 6.283f, 100)) * _p.R * R(0.05f, 0.3f, 101);
                int big = (int)R(5f, 9f, 102);
                Boulder(crown, R(1.4f, 1.9f, 103) * k, 0.4f, 0.5f, true);
                for (int i = 0; i < big; i++)
                {
                    float a = R(0f, 6.283f, 104 + i);
                    Boulder(crown + Dir(a) * R(1.6f, 4.2f, 110 + i) * k, R(0.6f, 1.35f, 116 + i) * k, R(0.3f, 0.55f, 122 + i), 0.9f, true);   // a massed crown, not lumps on a lawn
                }
                // a second, smaller group elsewhere on a long knoll, and stones breaking the turf over the whole of it
                if (LongEnough(26f))
                {
                    var g2 = Pt(127, _p.R * 0.4f);
                    if (math.distance(g2, crown) > 9f)
                        for (int i = 0; i < 4; i++) Boulder(g2 + Dir(R(0f, 6.283f, 140 + i)) * R(0f, 3.5f, 145 + i), R(0.45f, 0.95f, 150 + i) * k, R(0.35f, 0.55f, 155 + i), 0.7f);
                }
                var down = math.normalizesafe(_p.C - crown, Dir(R(0f, 6.283f, 128)));
                int out_ = (int)(math.sqrt(math.abs(Poly.Area(_p.Lawn))) * R(0.5f, 0.8f, 129));
                for (int i = 0; i < out_; i++)
                {
                    var q = Pt(160 + i, 1f);
                    if (U(190 + i) < 0.3f) Boulder(q, R(0.25f, 0.55f, 200 + i), R(0.4f, 0.6f, 230 + i), 0.8f);
                    else Stone(q, R(0.9f, 2.4f, 260 + i));
                }
                // scree spilling downhill from the crown
                for (int i = 0; i < 12; i++) Stone(crown + down * R(3f, _p.R * 1.1f, 300 + i) + new float2(-down.y, down.x) * R(-4f, 4f, 310 + i), R(0.6f, 1.4f, 320 + i));
                // a pine or a birch on the knoll, flowering scrub tucked between the stones
                int nt = U(330) < 0.6f ? 1 : 2;
                for (int i = 0; i < nt * 6 && nt > 0; i++) if (Tree(Pt(331 + i, 2.5f), KnollTrees, U(350 + i) < 0.3f ? PlannedAge.Old : PlannedAge.Adult, 6f)) nt--;
                for (int i = 0; i < (int)(_p.R * 0.9f); i++) Bush(Pt(360 + i, 0.8f), U(440 + i) < 0.4f ? Wildflowers : Scrub, 1.8f);
            }

            private bool LongEnough(float len) => ParkMoodsLong(_p.Lawn) >= len;
            private static float ParkMoodsLong(float2[] ring) { Poly.Bounds(ring, out var lo, out var hi); return math.cmax(hi - lo); }

            // ── hollow ──
            public void Hollow()
            {
                // wet trees round the rim, in a loose crescent on one side (the dip stays open to the sky)
                float a0 = R(0f, 6.283f, 500);
                int nt = (int)math.clamp(_p.R / 2.6f, 3f, 8f);
                for (int i = 0; i < nt * 5 && nt > 0; i++)
                {
                    var q = Pt(502 + i, 0.8f, 4.5f);
                    var d = math.normalizesafe(q - _p.C);
                    if (math.dot(d, Dir(a0)) < -0.2f) continue;
                    if (Tree(q, WetTrees, i == 0 && _neglected ? PlannedAge.Dead : U(560 + i) < 0.25f ? PlannedAge.Old : PlannedAge.Adult, 5f)) nt--;
                }
                // rushes: low green scrub in drifts round the wet bottom
                int drifts = (int)math.clamp(_p.R / 3f, 2f, 6f);
                for (int dr = 0; dr < drifts; dr++)
                {
                    var c = Pt(600 + dr, _p.R * 0.25f, _p.R * 0.7f);
                    int k = (int)R(4f, 9f, 620 + dr);
                    for (int b = 0; b < k; b++) Bush(c + Dir(R(0f, 6.283f, 640 + b * 7 + dr)) * R(0.6f, 3.2f, 700 + b * 7 + dr), Rushes, 1.5f);
                }
                // a half-sunk log and a few stones at the water's edge
                if (U(780) < 0.6f) Log(Pt(781, _p.R * 0.4f), R(0f, 6.283f, 783), 0.18f);
                for (int i = 0; i < 5; i++) Stone(Pt(790 + i, _p.R * 0.3f, _p.R * 0.7f), R(0.8f, 1.6f, 795 + i));
            }

            // ── meadow ──
            public void Meadow()
            {
                int nt = (int)math.clamp(_p.R / 8f, 1f, 3f);
                for (int i = 0; i < nt * 5 && nt > 0; i++)
                    if (Tree(Pt(800 + i, 3f), MeadowTrees, i == 0 ? PlannedAge.Old : PlannedAge.Adult, 12f)) nt--;
                // wild flowering shrubs in drifts (clumps of 3-7), not dots, mostly toward the meadow's edge
                int drifts = (int)math.clamp(math.sqrt(math.abs(Poly.Area(_p.Lawn))) / 6f, 2f, 8f);
                for (int d = 0; d < drifts; d++)
                {
                    var c = Pt(850 + d, 1f, math.max(3f, _p.R * 0.7f));
                    int k = (int)R(3f, 8f, 890 + d);
                    for (int b = 0; b < k; b++) Bush(c + Dir(R(0f, 6.283f, 900 + b * 7 + d)) * R(0.8f, 3.5f, 960 + b * 7 + d), U(1990 + b) < 0.7f ? Wildflowers : Scrub, 1.6f);
                }
            }

            // ── glade ──
            public void Glade()
            {
                // a ring of mixed trees along the lawn's edge round a clearing, thicker on one side
                float thick = R(0f, 6.283f, 1000);
                int nt = (int)math.clamp(math.sqrt(math.abs(Poly.Area(_p.Lawn))) * 0.75f, 6f, 26f);
                for (int i = 0; i < nt * 4 && nt > 0; i++)
                {
                    var q = Pt(1001 + i, 1f, math.max(5f, _p.R * 0.55f));
                    float side = 0.5f + 0.5f * math.dot(math.normalizesafe(q - _p.C), Dir(thick));
                    if (U(1060 + i) > 0.3f + 0.7f * side) continue;
                    if (Tree(q, CopseTrees, U(1180 + i) < (_neglected ? 0.15f : 0.06f) ? PlannedAge.Dead : U(1240 + i) < 0.3f ? PlannedAge.Old : U(1300 + i) < 0.2f ? PlannedAge.Teen : PlannedAge.Adult, 5f)) nt--;
                }
                // the old tree in the clearing
                Tree(_p.C + Dir(R(0f, 6.283f, 1360)) * _p.R * R(0f, 0.2f, 1361), new[] { "OakTree01", "EU_ChestnutTree01" }, PlannedAge.Old, 4f);
                // logs where they fell, scrub under the trees
                int logs = (int)R(1f, 3.9f, 1370);
                for (int i = 0; i < logs; i++) Log(Pt(1371 + i, 3f), R(0f, 6.283f, 1390 + i));
                for (int i = 0; i < (int)(_p.R * 1.2f); i++)
                {
                    var q = Pt(1400 + i, 0.5f, 5f);
                    if (math.dot(math.normalizesafe(q - _p.C), Dir(thick)) < -0.3f) continue;
                    Bush(q, Scrub, 2.2f);
                }
                if (U(1500) < 0.5f) Stone(Pt(1501, 2f), 2f);
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

            // ── allotments (the community garden folded into the park) ──
            private static readonly string[] PlotKinds = { GroundCover.Rows, GroundCover.Soil, GroundCover.Rows, GroundCover.Green, GroundCover.Plowed, GroundCover.Rows };
            private static readonly string[] Sheds = { "ToolShed02", "ToolShed01", "ToolShed03" };
            private static readonly string[] Tanks = { "WaterTank01", "WaterTank02" };
            private static readonly string[] Glass = { "Greenhouse01", "Greenhouse02", "Greenhouse03" };
            private static readonly string[] Beds = { "GardenBed01CornerHigh", "GardenBed02CornerHigh", "GardenBed03CornerHigh", "GardenBed01CornerLow" };

            /// <summary>
            /// A lawn room laid out as allotments: double rows of plots (worked soil, crop rows, greens) along the room's length with
            /// grass walks between, a shed at the back of some plots, water tanks and a greenhouse along the middle walk, raised beds
            /// at its head. Tidy, small and human: the community garden as a corner of the park, not a farm.
            /// </summary>
            public void Allotments()
            {
                float len = ParkMoodsLong(_p.Lawn);
                var c0 = Poly.Centroid(_p.Lawn);
                // the room's long axis
                float sxx = 0f, syy = 0f, sxy = 0f;
                foreach (var q in _p.Lawn) { var d = q - c0; sxx += d.x * d.x; syy += d.y * d.y; sxy += d.x * d.y; }
                float ang = 0.5f * math.atan2(2f * sxy, sxx - syy);
                var ax = new float2(math.cos(ang), math.sin(ang)); var sd = new float2(-ax.y, ax.x);
                float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
                foreach (var q in _p.Lawn) { float u = math.dot(q - c0, ax), v = math.dot(q - c0, sd); u0 = math.min(u0, u); u1 = math.max(u1, u); v0 = math.min(v0, v); v1 = math.max(v1, v); }
                const float pw = 4.2f, pd = 7f, walk = 1.4f, spine = 2.6f;
                int plots = 0, k = 0;
                // double rows: plot | walk-side ... each pair of rows shares a spine walk
                for (float v = v0 + 1f; v + 2f * pd + spine <= v1 - 1f; v += 2f * pd + spine + walk)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        float vc = v + (side == 0 ? pd * 0.5f : pd * 1.5f + spine);
                        for (float u = u0 + 1f; u + pw <= u1 - 1f; u += pw + walk)
                        {
                            k++;
                            var cc = c0 + ax * (u + pw * 0.5f) + sd * vc;
                            var ring = new[] { cc - ax * pw * 0.5f - sd * pd * 0.5f, cc + ax * pw * 0.5f - sd * pd * 0.5f, cc + ax * pw * 0.5f + sd * pd * 0.5f, cc - ax * pw * 0.5f + sd * pd * 0.5f };
                            bool ok = true;
                            foreach (var q in ring) if (ParkMoods.Edge(_p, q) < 0.8f || _s.Occ.Corridors.Overlaps(q, 0.8f, 0.3f) || Reserved(q, 0.6f)) { ok = false; break; }
                            if (!ok) continue;
                            _n++;
                            if (U(2000 + k) < 0.08f) continue;   // an empty plot, gone to grass
                            GroundCover.Field(_ctx, ring, PlotKinds[(int)(Hashing.Mix(_v, (uint)k) % (uint)PlotKinds.Length)]);
                            _mine.Add((cc, math.min(pw, pd) * 0.5f));
                            plots++;
                            // a shed at the back of one plot in three (the back = away from the spine walk)
                            if (U(2100 + k) < 0.34f)
                            {
                                var back = cc + sd * (side == 0 ? -1f : 1f) * (pd * 0.5f - 0.9f) + ax * (pw * 0.5f - 0.8f) * (U(2200 + k) < 0.5f ? -1f : 1f);
                                var o = _ctx.Object(back, math.atan2(sd.x, sd.y) + (side == 0 ? 0f : math.PI), ObjectRole.Furniture, new AssetRef(Pick(Sheds, 3), Sheds), Var(), -1, AssetCategories.GardenShed);
                                Rocks++;
                            }
                        }
                    }
                    // along the spine walk: a water tank and, once, a greenhouse
                    var mid = c0 + sd * (v + pd + spine * 0.5f);
                    for (int t = 0; t < 2; t++)
                    {
                        var q = mid + ax * math.lerp(u0 + 3f, u1 - 3f, U(2300 + t + (int)v));
                        if (ParkMoods.Edge(_p, q) < 1.5f || _s.Occ.Corridors.Overlaps(q, 1.2f, 0.3f)) continue;
                        bool glass = t == 1 && U(2400 + (int)v) < 0.6f;
                        _ctx.Object(q, math.atan2(ax.x, ax.y), ObjectRole.Furniture, glass ? new AssetRef(Pick(Glass, 4), Glass) : new AssetRef(Pick(Tanks, 5), Tanks), Var(), -1, glass ? AssetCategories.Glasshouse : AssetCategories.GardenShed);
                        Rocks++;
                    }
                }
                // raised beds at the room's head, by the nearest path
                var head = c0 + ax * (u0 + 2f);
                for (int i = 0; i < 4; i++)
                {
                    var q = head + sd * ((i - 1.5f) * 2.6f);
                    if (ParkMoods.Edge(_p, q) < 1f || _s.Occ.Corridors.Overlaps(q, 1.2f, 0.3f)) continue;
                    _ctx.Object(q, math.atan2(ax.x, ax.y), ObjectRole.Furniture, new AssetRef(Pick(Beds, 6 + i), Beds), Var(), -1, AssetCategories.GardenCrop);
                    Rocks++;
                }
                _ctx.Plan.Facts["allotmentPlots"] = plots;
            }

            // ── paint ──
            public void Paint(Placemaker.Core.Paint.PaintKit kit)
            {
                uint v = Hashing.Mix(_v, 0xB1u);
                switch (_p.Mood)
                {
                    case Mood.Gully:
                        TerrainPaint.Patch(_ctx, Channel(_p.Line, _p.Half * 0.95f), TerrainMaterials.Dry, 0.8f, 3f);
                        TerrainPaint.Patch(_ctx, Channel(_p.Line, _p.Floor + 1.2f), TerrainMaterials.Dirt, 1f, 1.5f);
                        break;
                    case Mood.Outcrop:
                        TerrainPaint.Patch(_ctx, _p.Lawn, TerrainMaterials.Dry, 0.9f, 3f);
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.65f, v + 1u, 14, 0.35f), TerrainMaterials.Dirt, 0.9f, 2.5f);
                        break;
                    case Mood.Hollow:
                        TerrainPaint.Patch(_ctx, _p.Lawn, TerrainMaterials.Lush, 1f, 3f);
                        TerrainPaint.Patch(_ctx, LeisureKit.Blob(_p.C, _p.R * 0.35f, v + 1u, 14, 0.3f), TerrainMaterials.Dirt, 0.7f, 2f);
                        break;
                    case Mood.Meadow:
                        TerrainPaint.Patch(_ctx, _p.Lawn, TerrainMaterials.Dry, 1f, 4f);
                        break;
                    case Mood.Glade:
                        TerrainPaint.Patch(_ctx, _p.Lawn, TerrainMaterials.Dirt, 0.65f, 4f);
                        break;
                    case Mood.Stones:
                        TerrainPaint.Patch(_ctx, _p.Lawn, TerrainMaterials.Dry, 0.75f, 3f);
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

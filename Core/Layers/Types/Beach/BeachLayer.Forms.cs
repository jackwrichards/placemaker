// Beach (p2-beaches, 2026-10-31): the landform settings, the new styles, the shore advice, and what stands on the forms
// (boulders piled on headlands and choking ravines, granite boulders in the shallows, reeds along marsh creeks, a boardwalk
// out over the marsh or the sand, footbridges where the promenade meets a ravine).
//
// Real places the styles are judged against (docs/runbooks/beach.md):
//   shingle   Brighton and Chesil Beach: steep pebble face, storm ridges, beach cusps, a row of huts, boats hauled up
//   mulch     a pine-fringed lake beach (Bavarian lakes, the Muskokas): a short sand strip, needles and duff, kayaks, a jetty
//   granite   Lake Tahoe's east shore (Sand Harbor): rounded granite boulders in clear shallows, coarse sand pockets, Jeffrey pines
//   ravine    a barranca on the Canary coast: a high rocky bank cut by boulder-choked gullies down to a pocket beach
//   marsh     a saltmarsh shore (Norfolk, Wadden Sea): creeks, salt pans, reed beds, a boardwalk out to the water
//   harbour   a Cornish cove: two rock headlands jutting out, rock platforms with pools, a small beach and boats between
//   wild      an undeveloped strand: low dunes with blowouts, wrack and driftwood, a fire ring, nobody's umbrellas
//   cove      (Wild dunes) a dune system: a foredune with blowouts, hummocky back dunes, sand fences, a boardwalk over them

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Common.Biomes;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    public sealed partial class BeachLayer
    {
        public const string NoWaterKey = "Placemaker.Beach.Issue.NoWater";
        public const string ShortShoreKey = "Placemaker.Beach.Issue.ShortShore";
        public const string IslandsOutsideKey = "Placemaker.Beach.Issue.IslandsOutside";
        public const string NarrowLandKey = "Placemaker.Beach.Issue.NarrowLand";
        public const string SteepShoreKey = "Placemaker.Beach.Issue.SteepShore";
        public const string CliffAdviceKey = "Placemaker.Beach.Issue.CliffAdvice";
        private static readonly string[] Boulders = NatureKit.Boulders;

        /// <summary>(p2-beaches) The land behind the beach as a Biome mix: the style's own backdrop (lawn) and dunes, shingle, heath, pine
        /// wood, rock, marsh, meadow.</summary>
        public static readonly BiomeOffer Biomes = new BiomeOffer(("lawn", 100), ("dunes", 0), ("shingle", 0), ("heath", 0), ("meadow", 0), ("forest", 0), ("rocky", 0), ("wetland", 0)) { Scale = 60 };

        /// <summary>(appended) The landforms: every one a look the styles preset, each changes the ground on any style.</summary>
        private static void DefineFormControls(ControlSet c)
        {
            c.Slider("berm", 0, 0, 2, 0.1, "m", ControlTier.More, "shore").Tip();
            c.Slider("storms", 0, 0, 3, 1, "count", ControlTier.More, "shore").Tip();
            c.Slider("headlands", 0, 0, 50, 2, "m", ControlTier.More, "shore").Tip();
            c.Slider("platforms", 0, 0, 100, 5, "%", ControlTier.More, "rocks").Tip();
            c.Slider("bluff", 0, 0, 12, 0.5, "m", ControlTier.More, "shore").Tip();
            c.Slider("ravines", 0, 0, 3, 1, "count", ControlTier.More, "shore").Tip();
            c.Slider("knolls", 0, 0, 12, 1, "count", ControlTier.More, "rocks").Tip();
            c.Slider("boulders", 0, 0, 300, 10, "perHa", ControlTier.More, "rocks").Tip();
            c.Slider("channels", 0, 0, 8, 1, "count", ControlTier.More, "shore").Tip();
            c.Slider("blowouts", 0, 0, 6, 1, "count", ControlTier.More, "backdrop").When("backdrop=dunes").Tip();
            c.Slider("hummocks", 0, 0, 1.5, 0.1, "m", ControlTier.More, "backdrop").When("backdrop=dunes").Tip();
            c.Toggle("boardwalk", false, ControlTier.More, "amenities").Tip();
            c.Slider("islands", 0, 0, 2, 1, "count", ControlTier.More, "shore").Tip();
            BiomeMix.Define(c, Biomes);
        }

        private void DefineFormPresets()
        {
            // Shingle beach: a steep pebble face with three storm ridges and cusps, a row of beach huts behind a wide promenade,
            // boats hauled up, no umbrellas (Brighton, Chesil Beach).
            Preset("shingle", ("style", "shingle"), ("paintLayer", (double)TerrainMaterials.Dirt), ("beachDepth", 60.0), ("slope", 11.0), ("wetBand", 4.0), ("storms", 3.0),
                ("sandBlend", 6.0), ("umbrellas", 0.0), ("huts", "row"), ("boats", true), ("backdrop", "lawn"), ("treeDensity", 10.0), ("lifeguards", 0.0), ("kiosks", 2.0),
                ("showers", false), ("pathWidth", "wide"), ("benchSpacing", 18.0), ("accessSpacing", 70.0), ("landings", false), ("bay", 0.0), ("rockClusters", 0.0),
                ("biomeLawn", 0.0), ("biomeHeath", 45.0), ("biomeMeadow", 30.0), ("biomeRocky", 25.0));   // p2-beaches: coastal heath, not a park
            // Pine lake beach: a short strip of sand under pines, needles and duff drifting onto it, a jetty, kayaks, a fire ring.
            Preset("mulch", ("style", "mulch"), ("beachDepth", 22.0), ("wetBand", 3.0), ("slope", 4.0), ("sandBlend", 10.0), ("umbrellas", 0.0), ("backdrop", "woods"),
                ("treeDensity", 60.0), ("grassDensity", 40.0), ("jetties", 1.0), ("jettyLength", 22.0), ("boats", true), ("fires", true), ("lifeguards", 0.0), ("kiosks", 0.0),
                ("huts", "none"), ("lamps", false), ("benchSpacing", 30.0), ("bay", 12.0), ("berm", 0.25), ("knolls", 2.0), ("boulders", 20.0), ("landings", false),
                ("biomeLawn", 0.0), ("biomeForest", 70.0), ("biomeHeath", 30.0));
            // Granite shore: rounded granite domes and boulders on the beach and out in clear shallows, coarse sand between, pines.
            Preset("granite", ("style", "granite"), ("beachDepth", 34.0), ("wetBand", 4.0), ("slope", 6.0), ("knolls", 7.0), ("boulders", 55.0), ("umbrellas", 12.0),
                ("backdrop", "woods"), ("treeDensity", 25.0), ("lifeguards", 0.0), ("kiosks", 0.0), ("huts", "none"), ("jetties", 1.0), ("jettyLength", 30.0),
                ("boats", true), ("sandBlend", 12.0), ("bay", 0.0), ("lamps", false), ("landings", false), ("underwater", 30.0), ("islands", 1.0),
                ("biomeLawn", 0.0), ("biomeRocky", 50.0), ("biomeForest", 30.0), ("biomeHeath", 20.0));
            // Rocky ravine: a high rocky bank behind a pocket beach, cut by two boulder-choked barrancas down to the water.
            Preset("ravine", ("style", "ravine"), ("beachDepth", 20.0), ("wetBand", 3.0), ("slope", 7.0), ("bluff", 10.0), ("ravines", 2.0), ("boulders", 60.0), ("knolls", 0.0),
                ("umbrellas", 0.0), ("backdrop", "none"), ("treeDensity", 30.0), ("lifeguards", 0.0), ("kiosks", 0.0), ("huts", "none"), ("showers", false), ("lamps", false),
                ("benchSpacing", 40.0), ("bay", 0.0), ("landings", false), ("sandBlend", 8.0),
                ("biomeLawn", 0.0), ("biomeRocky", 35.0), ("biomeHeath", 40.0), ("biomeForest", 25.0));   // pines and heath on the bluff top, not bare
            // Marsh shore: a saltmarsh flat with creeks and salt pans, reed beds, a boardwalk out to the open water, a strip of sand.
            Preset("marsh", ("style", "marsh"), ("beachDepth", 64.0), ("wetBand", 2.0), ("slope", 2.0), ("channels", 4.0), ("boardwalk", true), ("umbrellas", 0.0),
                ("backdrop", "none"), ("treeDensity", 18.0), ("lifeguards", 0.0), ("kiosks", 0.0), ("huts", "none"), ("showers", false), ("landings", false),
                ("accessSpacing", 200.0), ("benchSpacing", 35.0), ("lamps", false), ("sandBlend", 4.0), ("bay", 0.0), ("islands", 1.0), ("paintLayer", (double)TerrainMaterials.Lush),
                ("biomeLawn", 0.0), ("biomeMeadow", 70.0), ("biomeForest", 30.0));   // the shore is the marsh; a wetland biome behind it cost 300 ms more
            // Harbour cove: two rock headlands jutting out into the water with boulders piled on them, rock platforms with pools,
            // a small beach and boats between, a jetty.
            Preset("harbour", ("style", "harbour"), ("headlands", 34.0), ("platforms", 40.0), ("bay", 0.0), ("beachDepth", 30.0), ("umbrellas", 35.0), ("boats", true),
                ("jetties", 1.0), ("jettyLength", 30.0), ("backdrop", "none"), ("treeDensity", 15.0), ("lifeguards", 1.0), ("kiosks", 1.0), ("huts", "few"), ("boulders", 20.0),
                ("berm", 0.4), ("tidePools", 0.0), ("rockClusters", 0.0), ("islands", 1.0),
                ("biomeLawn", 0.0), ("biomeRocky", 20.0), ("biomeHeath", 35.0), ("biomeMeadow", 25.0), ("biomeForest", 20.0));   // p2-beaches: maquis, no palm lawn
            // Wild beach: low dunes with blowouts, wrack and driftwood, a fire ring, no facilities.
            Preset("wild", ("style", "wild"), ("beachDepth", 46.0), ("umbrellas", 0.0), ("backdrop", "dunes"), ("duneHeight", 6.0), ("duneDepth", 48.0), ("blowouts", 2.0),
                ("hummocks", 0.6), ("berm", 0.8), ("fires", true), ("lifeguards", 0.0), ("kiosks", 0.0), ("huts", "none"), ("showers", false), ("lamps", false),
                ("benchSpacing", 0.0), ("accessSpacing", 120.0), ("landings", false), ("boats", false), ("treeDensity", 15.0), ("rockClusters", 2.0), ("bay", 20.0),
                ("grassDensity", 75.0), ("biomeLawn", 0.0), ("biomeDunes", 50.0), ("biomeHeath", 25.0), ("biomeMeadow", 25.0));
        }

        // ───────────── advice ─────────────

        /// <summary>
        /// Warnings with the best practice for the selection: no water, too short a shore, too little land, a steep bank, a cliff.
        /// </summary>
        private static void ShoreAdvice(LayerContext ctx, Shoreline shore, float landDepth)
        {
            if (shore.Mode == ShoreMode.Assumed || !shore.HasWaterline)
            {
                ctx.Issue(IssueSeverity.Warning, NoWaterKey);
                return;
            }
            if (shore.Length < 40f) ctx.Issue(IssueSeverity.Warning, ShortShoreKey, (int)math.round(shore.Length));
            if (landDepth < 25f) ctx.Issue(IssueSeverity.Warning, NarrowLandKey, (int)math.round(landDepth));
            if (shore.CliffShare > 0.3f) ctx.Issue(IssueSeverity.Warning, CliffAdviceKey);
            else
            {
                // the bank's typical grade over the first 15 m inland
                var grades = new List<float>();
                for (float s = 4f; s < shore.Length - 4f; s += 8f)
                {
                    var w = shore.WaterAt(s, out var inw);
                    grades.Add((ctx.H(w + inw * 15f) - ctx.H(w)) / 15f);
                }
                grades.Sort();
                float g = grades.Count > 0 ? grades[grades.Count / 2] : 0f;
                ctx.Plan.Facts["bankGrade"] = g;
                if (g > 0.22f) ctx.Issue(IssueSeverity.Info, SteepShoreKey, (int)math.round(g * 100f));
            }
        }

        // ───────────── what stands on the forms ─────────────

        private static float2 At(Shoreline shore, float s, float dw)
        {
            var w = shore.WaterAt(math.clamp(s, 0f, shore.Length), out var inw);
            return w + inw * dw;
        }

        private static bool Boulder(Work b, float2 p, float scale, uint v, bool inWater, float sink = 0.3f)
        {
            var ctx = b.Ctx;
            if (b.G.OnCliff(p) || !inWater && b.Shore.FromWater(p) < 0.3f) return false;
            if (!b.Shore.Land.Contains(p) && !inWater) return false;
            // never past the drawn outline (the water beyond it belongs to the player, not this build)
            if (!b.Shore.Outline.ContainsDisc(p, 1.1f * scale)) return false;
            float r = 1.1f * scale;
            bool onLand = b.Shore.Land.Contains(p);
            // off the land (out in the water) the occupancy has no ground to give: only keep clear of other rocks and decks
            if (onLand ? !FreeIgnoringGround(b.Occ, p, r * 0.7f, 0.2f, 0.05f) : b.Occ.Discs.Overlaps(p, r * 0.7f, 0.05f) || b.Occ.Corridors.Overlaps(p, r * 0.7f))
                return false;
            if (ctx.Input.Reserved != null && ctx.Input.Reserved.Blocks(p, r)) return false;
            var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 5u)) * 6.283f, ObjectRole.Furniture, new AssetRef(Boulders[(int)(v % (uint)Boulders.Length)]), v, -1, AssetCategories.Rock);
            o.Scale = scale;
            // boulders are already partly buried; sink 0.35-0.6 of their 1.6 m on top so they bed in instead of sitting on the ground like popcorn
            o.Elevation = -math.min(0.6f, 0.2f + sink) * 1.6f * scale;
            b.Occ.Take(p, r * 0.8f, 2);
            b.RockSpots.Add((p, r));
            b.Rocks++;
            return true;
        }

        /// <summary>Boulders where the forms say: headlands piled with them, ravines choked, knolls crowned, a field in the shallows.</summary>
        private static void FormRocks(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var f = b.Forms;
            if (!AssetAvailability.Has(AssetCategories.Rock)) return;
            uint seed = Hashing.Mix(ctx.Seed, 0xB01Du);
            uint n = 0;
            // headlands: a mass of big boulders down both flanks and round the tip, smaller ones on top
            foreach (var (hs, hlen, hh, hw) in f.Headlands)
                for (float dw = -hlen - 3f; dw < 14f; dw += 2.2f)
                {
                    float u = math.saturate((dw + hlen) / (hlen + 18f));
                    float bend = 4f * math.sin(dw / 9f + hs) * (1f - u);
                    float w = hw * (0.55f + 0.45f * u);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        uint v = Hashing.Mix(seed, n++);
                        float k = 0.75f + 0.45f * Hashing.Unit(v);
                        Boulder(b, At(shore, hs + bend + side * w * k, dw), 1.1f + 1.4f * Hashing.Unit(Hashing.Mix(v, 1u)) * (1.2f - u * 0.4f), v, true, 0.25f);
                        if (Hashing.Unit(Hashing.Mix(v, 2u)) < 0.45f)
                            Boulder(b, At(shore, hs + bend + side * w * (k + 0.35f), dw - 1f), 0.8f + 0.9f * Hashing.Unit(Hashing.Mix(v, 3u)), Hashing.Mix(v, 4u), true, 0.4f);
                    }
                    uint tv = Hashing.Mix(seed, n++);
                    if (Hashing.Unit(tv) < 0.35f) Boulder(b, At(shore, hs + bend + (Hashing.Unit(Hashing.Mix(tv, 1u)) - 0.5f) * w, dw), 0.7f + 0.8f * Hashing.Unit(Hashing.Mix(tv, 2u)), tv, true, 0.35f);
                    // the tip: a skerry of boulders running out past the end
                    if (dw < -hlen + 2f)
                        for (int i = 0; i < 3; i++)
                        {
                            uint xv = Hashing.Mix(seed, n++);
                            Boulder(b, At(shore, hs + bend + (Hashing.Unit(xv) - 0.5f) * w * 1.4f, dw - 2f - 4f * Hashing.Unit(Hashing.Mix(xv, 1u))), 1f + 1.2f * Hashing.Unit(Hashing.Mix(xv, 2u)), xv, true, 0.3f);
                        }
                }
            // the pocket beach: boulders fallen at the feet of its walls (the gully path's line kept clear)
            if (b.Pocket != null)
            {
                var pk = b.Pocket;
                float depth = pk.Sand + 3f;
                for (int i = 0; i <= 40; i++)
                {
                    float a = math.PI * i / 40f;
                    uint v = Hashing.Mix(seed, n++);
                    float inset = 2.2f + 2f * Hashing.Unit(v);
                    var p = pk.At((pk.HalfW - inset) * math.cos(a), (depth - inset) * math.sin(a));
                    if (math.abs(math.dot(p - pk.O, pk.Tan)) < pk.Gully) continue;   // the gully mouth
                    Boulder(b, p, 0.8f + 1.4f * Hashing.Unit(Hashing.Mix(v, 1u)), v, true, 0.35f);
                }
                // up the gully a few fallen rocks in clumps (one every 3 m each side read as a kerb lining the path)
                for (float d = pk.Sand + 3f; d < pk.End; d += 4f + 4f * Hashing.Unit(Hashing.Mix(seed, n++)))
                {
                    uint v = Hashing.Mix(seed, n++);
                    if (Hashing.Unit(Hashing.Mix(v, 7u)) < 0.45f) continue;
                    float side = Hashing.Unit(Hashing.Mix(v, 8u)) < 0.5f ? -1f : 1f;
                    int clump = 1 + (int)(Hashing.Unit(Hashing.Mix(v, 9u)) * 3f);
                    for (int k = 0; k < clump; k++)
                    {
                        uint kv = Hashing.Mix(v, 20u + (uint)k);
                        Boulder(b, pk.At(side * (pk.Gully - 1.2f - 2.2f * Hashing.Unit(kv)), d + (Hashing.Unit(Hashing.Mix(kv, 1u)) - 0.5f) * 3f),
                            0.5f + 1.2f * Hashing.Unit(Hashing.Mix(kv, 2u)), kv, false, 0.35f);
                    }
                }
            }
            // ravines: boulders choke the floor, more at the wall feet
            foreach (var (rs, rw, rlen) in f.Ravines)
                for (float dw = 1f; dw < math.min(rlen, shore.MaxDepth + Shoreline.MaxGap); dw += 3.4f)
                {
                    float wiggle = 3f * math.sin(dw / 13f + rs);
                    float half = 2.2f + 0.04f * dw;
                    for (int i = 0; i < 2; i++)
                    {
                        uint v = Hashing.Mix(seed, n++);
                        float c = (Hashing.Unit(v) * 2f - 1f) * (half + 2.5f);
                        float sc = 0.6f + 1.5f * Hashing.Unit(Hashing.Mix(v, 1u)) * math.saturate(math.abs(c) / half + 0.3f);
                        Boulder(b, At(shore, rs + wiggle + c, dw + (Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * 2f), sc, v, false, 0.35f);
                    }
                }
            // granite knolls: crowned and ringed with boulders
            for (int ki = 0; ki < f.Knolls.Count; ki++)
            {
                var (kc, kr, kh) = f.Knolls[ki];
                int m = 4 + (int)(kr * 0.6f);
                for (int i = 0; i < m; i++)
                {
                    uint v = Hashing.Mix(seed, n++);
                    float a = Hashing.Unit(v) * 6.283f, rr = kr * math.sqrt(Hashing.Unit(Hashing.Mix(v, 1u))) * 1.05f;
                    Boulder(b, kc + new float2(math.cos(a), math.sin(a)) * rr, 1f + 1.8f * Hashing.Unit(Hashing.Mix(v, 2u)) * (1f - rr / (kr * 1.6f)), v, true, 0.3f);
                }
            }
            // platforms: a few boulders on their seaward lip
            foreach (var pl in f.Platforms)
                for (float s = pl.x + 3f; s < pl.y - 3f; s += 7f)
                {
                    uint v = Hashing.Mix(seed, n++);
                    if (Hashing.Unit(v) < 0.5f) Boulder(b, At(shore, s, f.PlatformFrom + 1.5f + 3f * Hashing.Unit(Hashing.Mix(v, 1u))), 0.8f + 1f * Hashing.Unit(Hashing.Mix(v, 2u)), v, true, 0.35f);
                }
            // a boulder field: clumped over the beach band and out in the shallows
            float perHa = ctx.F("boulders");
            if (perHa > 0f)
            {
                float area = shore.Length * (b.D + 12f);
                int wanted = (int)(perHa * area / 10000f);
                uint fs = Hashing.Mix(seed, 0xF1E1u);
                int placed = 0;
                for (int t = 0; t < wanted * 8 && placed < wanted; t++)
                {
                    uint v = Hashing.Mix(fs, (uint)t);
                    float s = Hashing.Unit(v) * shore.Length, dw = -12f + Hashing.Unit(Hashing.Mix(v, 1u)) * (b.D + 12f);
                    var p = At(shore, s, dw);
                    float clump = ValueNoise.Clumps(p, 18f, fs);
                    if (Hashing.Unit(Hashing.Mix(v, 2u)) > clump * clump * 1.6f) continue;
                    if (f.InRavine(s, dw)) continue;
                    if (b.Occ.Corridors.Overlaps(p, 2f)) continue;
                    float sc = 0.6f + 1.9f * math.pow(Hashing.Unit(Hashing.Mix(v, 3u)), 1.6f);
                    if (Boulder(b, p, sc, v, dw < 0f, dw < 0f ? 0.3f : 0.5f)) placed++;
                }
                ctx.Plan.Facts["boulderField"] = placed;
            }
            ctx.Plan.Facts["formRocks"] = b.Rocks;
        }

        /// <summary>Reeds massed along the marsh creeks and round the salt pans, tussocks over the flat.</summary>
        private static void MarshPlanting(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var f = b.Forms;
            if (f.Channels.Count == 0) return;
            string[] reeds = { "GreenBushWildTeen01", "GreenBushWildChild01", "GreenBushWildTeen01", "GreenBushWild01" };
            string[] tussocks = { "GreenBushWildChild01", "GreenBushWildChild01", "GreenBushWildTeen01" };
            uint seed = Hashing.Mix(ctx.Seed, 0x4EEDu);
            int placed = 0, cap = (int)math.clamp(shore.Length * f.MarshTo * 0.12f, 200f, 2400f);
            var keep = b.Occ.GroundOk;
            b.Occ.GroundOk = null;
            try
            {
                for (int t = 0; t < cap * 6 && placed < cap; t++)
                {
                    uint v = Hashing.Mix(seed, (uint)t);
                    float s = Hashing.Unit(v) * shore.Length, dw = b.Wet * 0.5f + Hashing.Unit(Hashing.Mix(v, 1u)) * (f.MarshTo + 4f);
                    var p = At(shore, s, dw);
                    if (!shore.Land.Contains(p)) continue;
                    float dc = f.ChannelDistance(p, out float hw);
                    bool bank = dc > hw * 0.9f && dc < hw + 4f;
                    bool pan = false;
                    foreach (var (pc, pr) in f.Pans) { float d = math.distance(p, pc); if (d < pr * 0.95f) { pan = false; goto skip; } if (d < pr + 3f) pan = true; }
                    // reed beds in clumps along the creeks, not hedge rows
                    float clump = ValueNoise.Clumps(p, 7f, seed ^ 0x5EDu);
                    float chance = bank ? 1.1f * clump : pan ? 0.8f * clump : 0.16f * ValueNoise.Clumps(p, 9f, seed);
                    if (Hashing.Unit(Hashing.Mix(v, 2u)) > chance) continue;
                    if (dc < hw * 0.9f) continue;
                    if (!b.Occ.Free(p, 0.5f, 0.2f, 0.05f) || ctx.Input.Reserved != null && ctx.Input.Reserved.Blocks(p, 0.8f)) continue;
                    var ids = bank || pan ? reeds : tussocks;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Bush, new AssetRef(ids[(int)(Hashing.Mix(v, 4u) % (uint)ids.Length)]), v);
                    o.Scale = bank ? 0.7f + 0.45f * Hashing.Unit(Hashing.Mix(v, 5u)) : 0.45f + 0.3f * Hashing.Unit(Hashing.Mix(v, 5u));
                    b.Occ.Take(p, 0.45f, 3);
                    placed++;
                skip:;
                }
            }
            finally { b.Occ.GroundOk = keep; }
            ctx.Plan.Facts["marshReeds"] = placed;
        }

        /// <summary>
        /// A boardwalk from the promenade out over the beach or the marsh to the water, in two or three legs, ending in a T at
        /// the water. It starts on a path end (a short ground path off the promenade) so the deck and the path share one node.
        /// </summary>
        private static void Boardwalk(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G;
            if (!ctx.B("boardwalk") || !b.HasPath || shore.Mode == ShoreMode.Assumed || !shore.HasWaterline) return;
            var asset = new AssetRef("", Waterfront.WaterfrontArt.Boardwalk);
            float deckY = (g.HasLevel ? g.Level : g.Ref(shore.Length * 0.5f)) + 0.9f;
            // the promenade point nearest the middle of the shore
            float sMid = shore.Length * (0.4f + 0.2f * Hashing.Unit(Hashing.Mix(ctx.Seed, 0xB0A1u)));
            float best = float.MaxValue; float2 top = default;
            foreach (var line in b.Lines)
                for (int i = 0; i < line.Length; i++)
                {
                    shore.Nearest(line[i], out _, out float a);
                    if (math.abs(a - sMid) < best) { best = math.abs(a - sMid); top = line[i]; }
                }
            if (best > 30f) return;
            shore.Nearest(top, out var inw, out float s0);
            if (b.Forms.InRavine(s0, shore.FromWater(top), 4f)) return;
            // the ground stub off the promenade: the deck begins at its end
            var start = top - inw * (b.Hw + 3.5f);
            if (!b.Occ.Free(start, 1f, 0.2f, 0.1f)) return;
            var pts = new List<float2> { start };
            float total = shore.FromWater(start) + 4f;
            uint v = Hashing.Mix(ctx.Seed, 0xB0A2u);
            int legs = total > 30f ? 3 : 2;
            var cur = start;
            for (int i = 1; i <= legs; i++)
            {
                float target = total * (1f - (float)i / legs) - (i == legs ? 6f : 0f);
                float side = (i & 1) == 1 ? 1f : -1f;
                float ang = side * (0.25f + 0.25f * Hashing.Unit(Hashing.Mix(v, (uint)i)));
                var tan = new float2(inw.y, -inw.x);
                var dir = math.normalizesafe(-inw * math.cos(ang) + tan * math.sin(ang));
                float drop = shore.FromWater(cur) - target;
                if (drop <= 2f) break;
                var next = cur + dir * (drop / math.max(0.5f, math.cos(ang)));
                pts.Add(next);
                cur = next;
            }
            if (pts.Count < 2 || shore.FromWater(pts[pts.Count - 1]) > -3f) return;
            LeisureKit.Path(ctx, b.Occ, top, start, NetRole.Pathway, AssetMix.One(ctx.S("pathAsset")));
            // the deck stays 0.6 m over the sand or the marsh and 0.9 m over the water
            float Y(float2 q) => math.max(deckY, g.H(q) + 0.6f);
            // the first piece ramps from the stub's ground up to the deck (the build fits its end onto the path end)
            float y0 = g.H(start);
            var ramp = start + math.normalizesafe(pts[1] - start) * math.min(6f, math.distance(start, pts[1]) * 0.4f);
            Deck(b, start, y0, ramp, Y(ramp), asset);
            Deck(b, ramp, Y(ramp), pts[1], Y(pts[1]), asset);
            for (int i = 2; i < pts.Count; i++) Deck(b, pts[i - 1], Y(pts[i - 1]), pts[i], Y(pts[i]), asset);
            // the T at the water
            var end = pts[pts.Count - 1];
            var d = math.normalizesafe(end - pts[pts.Count - 2]);
            var sideV = new float2(-d.y, d.x);
            Deck(b, end, deckY, end + sideV * 6f, deckY, asset);
            Deck(b, end, deckY, end - sideV * 6f, deckY, asset);
            b.Decks.Add((start, end));
            ctx.Plan.Facts["boardwalk"] = Shoreline.LengthOf(pts.ToArray());
        }

        private static void Deck(Work b, float2 a, float ya, float2 e, float ye, AssetRef asset)
        {
            if (math.distance(a, e) < 1f) return;
            float3 A = new float3(a.x, ya, a.y), D = new float3(e.x, ye, e.y);
            b.Ctx.Plan.Nets.Add(new PlannedNet
            {
                A = A, B = math.lerp(A, D, 1f / 3f), C = math.lerp(A, D, 2f / 3f), D = D,
                Role = NetRole.Pathway, Asset = asset, FollowTerrain = false, Category = AssetCategories.NetPier,
            });
            float hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            b.Occ.TakeCorridor(a, e, hw + 1f, 100);
        }

        /// <summary>
        /// A footbridge from the beach out to each island: a ground path off the promenade to its foot on the sand, the deck
        /// (ramp, level span 1.3 m over the water, ramp) to a path end on the island, a path up to a bench at the top. Both deck ends
        /// are path ends, so each joint is one node (docs/GROUND-RUNDOWN.md, bridges).
        /// </summary>
        private static void IslandBridges(Work b)
        {
            var ctx = b.Ctx; var shore = b.Shore; var g = b.G; var f = b.Forms;
            if (f.Islands.Count == 0 || shore.Mode == ShoreMode.Assumed || !shore.HasWaterline) return;
            var asset = new AssetRef("", Waterfront.WaterfrontArt.Boardwalk);
            var pathAsset = AssetMix.One(ctx.S("pathAsset"));
            float level = g.HasLevel ? g.Level : g.Ref(shore.Length * 0.5f);
            int n = 0;
            foreach (var (ic, ir, ih, s) in f.Islands)
            {
                uint v = Hashing.Mix(ctx.Seed, 0x15B0u + (uint)n);
                var w = shore.WaterAt(s, out var inw);
                var land = w + inw * math.max(3f, b.Wet * 0.6f);
                var dir = math.normalizesafe(ic - land);
                var foot = ic - dir * (ir * 0.55f);
                float best = 70f; float2 from = default; bool has = false;
                foreach (var line in b.Lines) foreach (var q in line) { float d = math.distance(q, land); if (d < best) { best = d; from = q; has = true; } }
                if (!has || b.Occ.Corridors.Overlaps(land, 1.5f)) continue;
                LeisureKit.Path(ctx, b.Occ, from, land, NetRole.Pathway, pathAsset);
                LeisureKit.Path(ctx, b.Occ, foot, ic, NetRole.Pathway, pathAsset);
                float y0 = g.H(land), y1 = level + ih * 0.9f, mid = math.max(level + 1.3f, math.max(y0, y1));
                var a = math.lerp(land, foot, 0.15f); var e = math.lerp(land, foot, 0.85f);
                Deck(b, land, y0, a, mid, asset);
                Deck(b, a, mid, e, mid, asset);
                Deck(b, e, mid, foot, y1, asset);
                b.Decks.Add((land, foot));
                // the island: a bench at the top looking back, pines in its lee, boulders round its rim
                var bench = ctx.Object(ic + dir * 1.6f, LeisureKit.Yaw(ic, ic + dir), ObjectRole.Bench, AssetMix.Pick(BeachArt.Benches, 0u), v);
                for (int k = 0; k < 4; k++)
                {
                    uint tv = Hashing.Mix(v, 10u + (uint)k);
                    float ang = math.atan2(dir.y, dir.x) + (Hashing.Unit(tv) - 0.5f) * 2.4f;
                    var tp = ic + new float2(math.cos(ang), math.sin(ang)) * ir * (0.35f + 0.25f * Hashing.Unit(Hashing.Mix(tv, 1u)));
                    ctx.Object(tp, Hashing.Unit(Hashing.Mix(tv, 2u)) * 6.283f, ObjectRole.Tree, new AssetRef("", "PineTree01"), tv);
                }
                for (int k = 0; k < 14; k++)
                {
                    uint rv = Hashing.Mix(v, 40u + (uint)k);
                    float ang = Hashing.Unit(rv) * 6.283f;
                    var rp = ic + new float2(math.cos(ang), math.sin(ang)) * ir * (0.85f + 0.3f * Hashing.Unit(Hashing.Mix(rv, 1u)));
                    if (math.distance(rp, foot) < 4f) continue;
                    Boulder(b, rp, 0.9f + 1.3f * Hashing.Unit(Hashing.Mix(rv, 2u)), rv, true, 0.35f);
                }
                n++;
            }
            ctx.Plan.Facts["islandBridges"] = n;
        }

        /// <summary>Footbridges where the promenade stops at a ravine's rim: a deck from one walk end straight to the next.</summary>
        private static void RavineBridges(Work b, List<(float2 a, float ha, float2 e, float he)> ends)
        {
            var f = b.Forms;
            if (f.Ravines.Count == 0 || ends.Count < 2) return;
            var asset = new AssetRef("", Waterfront.WaterfrontArt.Boardwalk);
            int bridges = 0;
            for (int i = 0; i < ends.Count; i++)
                for (int j = 0; j < ends.Count; j++)
                {
                    if (i == j) continue;
                    var p = ends[i].e; var q = ends[j].a;
                    float d = math.distance(p, q);
                    if (d < 4f || d > 45f) continue;
                    // only across a ravine: the middle of the span lies in one
                    var mid = (p + q) * 0.5f;
                    b.Shore.Nearest(mid, out _, out float s);
                    if (!f.InRavine(s, b.Shore.FromWater(mid))) continue;
                    float y = math.max(ends[i].he, ends[j].ha);
                    Deck(b, p, ends[i].he, math.lerp(p, q, 0.2f), y, asset);
                    Deck(b, math.lerp(p, q, 0.2f), y, math.lerp(p, q, 0.8f), y, asset);
                    Deck(b, math.lerp(p, q, 0.8f), y, q, ends[j].ha, asset);
                    b.Decks.Add((p, q));
                    bridges++;
                }
            b.Ctx.Plan.Facts["ravineBridges"] = bridges;
        }
    }
}

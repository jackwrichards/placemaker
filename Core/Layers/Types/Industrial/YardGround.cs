// Industrial category, the ground (the user, 2026-10-19: "not just surface drawn ... surface painted elegantly, excellently.
// Dirt pile, dirt puddle, mud puddle, garbage, clean, dirty ... mound of dirt that's painted, certain sections of concrete,
// certain sections of dead trees"). A yard's ground tells where the work happens, so every mark has a reason:
//
//   * the terrain itself is painted (no flat surface polygon over the yard): worked earth or gravel over the yard, fading to
//     dry grass along the fence;
//   * mud where the wheels are: dark strips along both sides of every aisle, a mud patch at every aisle crossing and at the
//     gate apron;
//   * oil-soaked dark ground round the heaps, the mountain, the bays and the machines; the ground under a row of wrecks is
//     worn to dirt; concrete only where something heavy stands (stacks, bays, the plant, the office forecourt);
//   * spoil mounds: real terraformed mounds of dirt in the leftover corners, painted, with a dead tree on top, dead bushes
//     round it and a fallen log at the foot;
//   * decals placed on purpose (asset packs only, the clean game has none): puddles at the aisle crossings and along the
//     mud, an oil stain under every machine and parked lorry, litter round the skips, cracks on the concrete.
//
// Terrain paint is one 3.5 m texel per cell of the city's splatmap, so every mark here is at least 4 m across.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public static class YardGround
    {
        // Vanilla terrain materials (TerrainMaterials): 1 dry grass, 2 forest floor (the darkest: mud, oil), 3 dirt, 4 sand.
        public const int DryGrass = 1, Mud = 2, Dirt = 3, Gravel = 4;

        /// <summary>
        /// The painted yard (seen in the game, 2026-10-19: the forest-floor layer, the darkest, reads as GREEN on a vanilla map,
        /// so it never paints a yard): worked earth or grey gravel over the whole yard, dry grass along the fence, gravel worn
        /// to earth along the aisles and at the crossings. The dark, oily ground is a real surface (OilPatches).
        /// </summary>
        public static void Paint(IndustrialSite s, string ground, uint salt)
        {
            var ctx = s.Ctx;
            float a = s.Spec.Aisle;
            // Earth right up to the fence (no grass inside a yard: the user, 2026-10-19).
            TerrainPaint.Field(ctx, s.Region.Ring, Dirt, s.Organic ? 0.85f : 1f, 1f, 1f, null, 0f, 0f, 0f, Hashing.Mix(salt, 1u));
            int baseLayer = ground == "gravel" ? Gravel : Dirt;
            TerrainPaint.Field(ctx, s.Inner.Ring, baseLayer, s.Organic ? 0.85f : 1f, 4f, 2f, null, 0f, 0f, 0f, Hashing.Mix(salt, 2u));
            int k = 0;
            if (ground == "gravel")
            {
                // Wheels wear gravel down to the earth: a strip along each side of the aisles, a patch at each crossing.
                foreach (var (p, q) in s.Aisles)
                {
                    float len = math.distance(p, q);
                    if (len < 4f) continue;
                    var dir = (q - p) / len;
                    var n = new float2(-dir.y, dir.x);
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var c = (p + q) * 0.5f + n * side * (a * 0.25f);
                        TerrainPaint.Field(ctx, Clip(s, LayerRegion.Rect(c, dir, len * 0.5f, 2f)), Dirt, 1f, 1.6f, 1.6f, null, 0f, 0f, 0f, Hashing.Mix(salt, 10u + (uint)k++));
                    }
                }
                foreach (var c in Crossings(s))
                    TerrainPaint.Field(ctx, Clip(s, LeisureKit.Blob(c, a * 0.8f + 1.5f, Hashing.Mix(salt, 0x60u + (uint)k++), 14, 0.3f)), Dirt, 1f, 2.5f, 2f, null, 0f, 0f, 0f, Hashing.Mix(salt, 0x61u + (uint)k));
            }
            else
            {
                // Earth yards: the stock stands on drier, greyer hardstanding (a sandy patch per block of goods or skips).
                foreach (var b in s.Blocks)
                    if (b.Use == "goods" || b.Use == "skips" || b.Use == "stockpiles")
                        TerrainPaint.Field(ctx, Clip(s, s.Rect(b.U0 + 0.5f, b.U1 - 0.5f, b.V0 + 0.5f, b.V1 - 0.5f).Ring), Gravel, 1f, 2f, 2f, null, 0f, 0f, 0f, Hashing.Mix(salt, 0x0B0u + (uint)b.Index));
            }
        }

        private static readonly AssetRef Oil = new AssetRef("Oil Surface 01");

        /// <summary>
        /// The dark ground, where it belongs: oil-soaked patches round every heap, machine and the plant, at the aisle crossings
        /// and the gate of an earth or gravel yard. Real surfaces (irregular blobs), at most a couple of dozen.
        /// </summary>
        public static int OilPatches(IndustrialSite s, string ground, uint salt)
        {
            var ctx = s.Ctx;
            int n = 0;
            float a = s.Spec.Aisle;
            var spots = new List<(float2 p, float r)>();
            foreach (var (p, r) in s.Anchors) spots.Add((p, math.min(r * 0.7f + 2f, 9f)));
            if (ground != "concrete")
            {
                foreach (var c in Crossings(s)) spots.Add((c, a * 0.45f + 0.8f));
                if (s.HasGate) spots.Add((s.W(0f, 3.5f), a * 0.45f + 0.5f));
            }
            for (int i = 0; i < spots.Count && n < 24; i++)
            {
                uint v = Hashing.Mix(salt, (uint)i);
                var (p, r) = spots[i];
                // Off-centre and lobed: a spill, not a disc.
                var c = p + new float2(Hashing.Unit(v) - 0.5f, Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * r * 0.4f;
                if (YardKit_Patch(ctx, s, LeisureKit.Blob(c, r, Hashing.Mix(v, 2u), 12, 0.45f))) n++;
            }
            return n;
        }

        private static bool YardKit_Patch(LayerContext ctx, IndustrialSite s, float2[] ring)
        {
            var clipped = Clip(s, ring);
            if (clipped == null || clipped.Count < 3) return false;
            return ctx.Surface(clipped, SurfaceRole.Ground, Oil, AssetCategories.SurfaceDirt);
        }

        // ───────────── hardstanding ─────────────

        /// <summary>The yard's hard surfaces, all vanilla (a clean game has these): which one a block of each use stands on.</summary>
        public static class Hard
        {
            public static readonly AssetRef Concrete = new AssetRef("Concrete Surface 02", "Concrete Surface 01");
            public static readonly AssetRef ConcreteLight = new AssetRef("Concrete Surface 01", "Concrete Surface 02");
            public static readonly AssetRef Pavement = new AssetRef("Pavement Surface 02", "Pavement Surface 01");
            public static readonly AssetRef Dark = new AssetRef("Pavement Surface 01", "Pavement Surface 02");
            public static readonly AssetRef Oil = new AssetRef("Oil Surface 01");
            public static readonly AssetRef Ore = new AssetRef("Ore Surface 01", "Sand Surface 02");
            public static readonly AssetRef Gravel = new AssetRef("Sand Surface 02", "Sand Surface 01");
            public static readonly AssetRef Rubbish = new AssetRef("Landfill Surface 01", "Oil Surface 01");
        }

        /// <summary>
        /// The user (2026-10-19): "I'm not loving the grass. Maybe the whole thing should be paved ... different types of
        /// pavements, plus rockiness, plus dirt painted." Every block stands on a slab of its own kind (containers on concrete,
        /// wrecks on rusty ore ground, heaps and the mountain on oily ground, skips and goods on worn pavement, the tip's bays on
        /// rubbish), each slab's outline a little ragged so the yard is a patchwork, not a drawing; the seams between slabs and
        /// lanes are painted earth. Slabs never overlap each other or the lanes (two surfaces on one spot flicker).
        /// </summary>
        public static int Slabs(IndustrialSite s, string ground, uint salt)
        {
            int n = 0;
            foreach (var b in s.Blocks)
            {
                if (b.Use.Length == 0 || b.Use == "stockpiles") continue;
                AssetRef asset; SurfaceRole role;
                switch (b.Use)
                {
                    // Container slabs: worn grey pavement (the makeover, 2026-10-30: all-concrete blocks
                    // read as bright vanilla sheets from 45 degrees).
                    case "stacks": case "box20": case "box40": case "units": case "containers": asset = Hard.Pavement; role = SurfaceRole.Paving; break;
                    case "wrecks": case "carports": asset = (b.Index % 3) == 0 ? Hard.Gravel : Hard.Ore; role = SurfaceRole.Ground; break;
                    case "mountain": case "heaps": case "crusher": case "crushed": asset = (b.Index % 3) == 2 ? Hard.Ore : Hard.Rubbish; role = SurfaceRole.Ground; break;
                    case "bays": case "parts": asset = ground == "concrete" ? Hard.Concrete : Hard.Rubbish; role = ground == "concrete" ? SurfaceRole.Paving : SurfaceRole.Ground; break;
                    case "plant": asset = Hard.Concrete; role = SurfaceRole.Paving; break;
                    default: asset = (b.Index & 1) == 0 ? Hard.Pavement : Hard.Gravel; role = (b.Index & 1) == 0 ? SurfaceRole.Paving : SurfaceRole.Ground; break;
                }
                var ring = Ragged(s, b, Hashing.Mix(salt, (uint)b.Index));
                if (s.Ctx.Surface(ring, role, asset, role == SurfaceRole.Paving ? AssetCategories.SurfacePaving : AssetCategories.SurfaceNatural)) n++;
            }
            return n;
        }

        /// <summary>A block's rectangle with ragged long edges: every 4–7 m the edge steps in or out by up to half a metre.</summary>
        private static float2[] Ragged(IndustrialSite s, YardBlock b, uint salt)
        {
            var pts = new List<float2>();
            float u0 = b.U0 - 0.3f, u1 = b.U1 + 0.3f, v0 = b.V0 - 0.3f, v1 = b.V1 + 0.3f;
            int k = 0;
            // Front edge left to right, then the back edge right to left; the short ends stay straight.
            for (float u = u0; u < u1; u += 4f + Hashing.Unit(Hashing.Mix(salt, (uint)k)) * 3f, k++)
                pts.Add(s.W(u, v0 + (u == u0 ? 0f : (Hashing.Unit(Hashing.Mix(salt, (uint)k + 500u)) - 0.3f) * 0.6f)));
            pts.Add(s.W(u1, v0));
            pts.Add(s.W(u1, v1));
            for (float u = u1 - 4f; u > u0; u -= 4f + Hashing.Unit(Hashing.Mix(salt, (uint)k)) * 3f, k++)
                pts.Add(s.W(u, v1 - (Hashing.Unit(Hashing.Mix(salt, (uint)k + 900u)) - 0.3f) * 0.6f));
            pts.Add(s.W(u0, v1));
            return pts.ToArray();
        }

        // ───────────── rocks ─────────────

        private static readonly YardItem Rock = new YardItem(AssetCategories.Rock, 1f, 0.9f, "Rock01", "Rock02", "Rock05", "Rock07", "Rock09", "Rock12", "Rock16", "Rock21", "Rock25", "Rock29");
        private static readonly YardItem BigRock = new YardItem(AssetCategories.Rock, 5f, 4.5f, "Boulder02", "Boulder04", "Boulder06", "Boulder08");

        /// <summary>
        /// Rocky margins: stones in twos and threes along the edge band and in the left-over ground, now and then a boulder,
        /// as if the yard was scraped out of rough ground and the stones pushed to its edges.
        /// </summary>
        public static int Rocks(IndustrialSite s, uint salt)
        {
            int n = 0;
            var ring = s.Inner.Ring;
            float per = s.Inner.Perimeter;
            int i = 0;
            for (float t = 2f; t < per; t += 5f + Hashing.Unit(Hashing.Mix(salt, (uint)i)) * 9f, i++)
            {
                uint v = Hashing.Mix(salt, 0x2000u + (uint)i);
                var e = s.Inner.PointAt(t, out _, out var inward, out _);
                var p = e - inward * (s.Spec.Edge * (0.25f + 0.5f * Hashing.Unit(v)));
                if (!s.Region.ContainsDisc(p, 1f)) continue;
                bool gate = false;
                foreach (var g in s.Gates) if (math.distance(g.Position, p) < s.Spec.Aisle + 4f) { gate = true; break; }
                if (gate) continue;
                if (Hashing.Unit(Hashing.Mix(v, 1u)) < 0.12f && s.Spec.Edge >= 4f)
                {
                    if (s.Put(BigRock, BigRock.Pick(v), p - inward * 0.5f, Turn(s.U, Hashing.Unit(v) * 6.283f), v, 0.1f)) n++;
                }
                else n += YardFill.Clump(s, p, 1.2f, Rock, 2 + (int)(Hashing.Unit(Hashing.Mix(v, 2u)) * 3f), v);
            }
            return n;
        }

        private static float2 Turn(float2 d, float a) => new float2(d.x * math.cos(a) - d.y * math.sin(a), d.x * math.sin(a) + d.y * math.cos(a));

        private static IReadOnlyList<float2> Clip(IndustrialSite s, float2[] ring)
        {
            try
            {
                var pieces = Poly.Intersection(ring, s.Region.Ring);
                Region best = null;
                foreach (var r in pieces) if (r?.Outer != null && (best == null || r.Area > best.Area)) best = r;
                return best?.Outer ?? ring;
            }
            catch (System.Exception) { return ring; }
        }

        /// <summary>Where two aisles meet (centre points).</summary>
        public static List<float2> Crossings(IndustrialSite s)
        {
            var list = new List<float2>();
            for (int i = 0; i < s.Aisles.Count; i++)
                for (int j = i + 1; j < s.Aisles.Count; j++)
                {
                    var (a0, a1) = s.Aisles[i]; var (b0, b1) = s.Aisles[j];
                    // Pieces of one bending track meet end to end: that is no crossing.
                    if (math.distancesq(a0, b0) < 0.01f || math.distancesq(a0, b1) < 0.01f || math.distancesq(a1, b0) < 0.01f || math.distancesq(a1, b1) < 0.01f) continue;
                    if (Poly.SegmentIntersection(a0, a1, b0, b1, out var x, out _, out _)) list.Add(x);
                    else
                    {
                        // T-junction: one aisle ends on the other.
                        foreach (var e in new[] { b0, b1 })
                            if (math.sqrt(SegmentIndex.DistSq(e, a0, a1)) < s.Spec.Aisle * 0.6f) { list.Add(SegmentIndex.ClosestPoint(e, a0, a1)); break; }
                    }
                }
            return list;
        }

        // ───────────── spoil mounds ─────────────

        private static readonly YardItem DeadBush = new YardItem(AssetCategories.Bush, 1.8f, 1.8f, "FlowerBushWildDead02");
        private static readonly YardItem FallenLog = new YardItem(AssetCategories.Rock, 0.7f, 10.4f, "Log02", "Log05", "Log08");
        private static readonly AssetRef DeadTree = new AssetRef("");

        /// <summary>
        /// Spoil mounds in the leftover corners (where no block fits and nothing stands): a terraformed mound of dirt, painted
        /// dirt with dry grass round its foot, a dead tree on top, dead bushes on its flanks and a fallen log at the foot.
        /// Returns the mounds for the sculptor.
        /// </summary>
        public static List<Mound> Spoil(IndustrialSite s, int max, uint salt)
        {
            var mounds = new List<Mound>();
            if (max <= 0) return mounds;
            var ctx = s.Ctx;
            float front = s.Spec.Front, a = s.Spec.Aisle;
            // Left-over ground only: never in a block, never near an aisle, never in the front yard.
            var spots = new List<(float2 p, float r)>();
            foreach (var p in Scatter.Candidates(s.Inner, 3f, Hashing.Mix(salt, 1u), 1500, ctx.Cancel))
            {
                var f = s.F(p);
                if (f.y < front + a && math.abs(f.x) < s.Spec.FrontZone + 3f) continue;
                float r = math.min(10f, s.Inner.BoundaryDistance(p, 12f) + s.Spec.Edge * 0.5f);
                foreach (var bl in s.Blocks)
                {
                    float du = math.max(0f, math.max(bl.U0 - f.x, f.x - bl.U1)), dv = math.max(0f, math.max(bl.V0 - f.y, f.y - bl.V1));
                    r = math.min(r, math.sqrt(du * du + dv * dv) - 1.5f);
                    if (r < 4.5f) break;
                }
                if (r < 4.5f) continue;
                foreach (var (q0, q1) in s.Aisles) r = math.min(r, math.sqrt(SegmentIndex.DistSq(p, q0, q1)) - a * 0.5f - 2f);
                if (r < 4.5f) continue;
                r = math.min(r, s.Occ.Clearance(p, 12f) - 1f);
                if (r >= 4.5f) spots.Add((p, r));
            }
            spots.Sort((x, y) => y.r.CompareTo(x.r));
            var picked = new List<float2>();
            int n = 0;
            foreach (var (p, r0) in spots)
            {
                if (n >= max) break;
                bool near = false;
                foreach (var q in picked) if (math.distance(p, q) < r0 + 18f) { near = true; break; }
                if (near) continue;
                float r = r0;
                picked.Add(p);
                uint v = Hashing.Mix(salt, 0x5B01u + (uint)n);
                float h = math.clamp(r * 0.3f, 1.2f, 3f);
                mounds.Add(new Mound { Center = p, Radius = r, Height = h, Flat = 0.25f });
                s.Occ.Take(p, r * 0.7f, YardKit.KindStuff);
                // Paint: dirt on the mound, dry grass round its foot.
                TerrainPaint.Field(ctx, LeisureKit.Blob(p, r + 2f, Hashing.Mix(v, 1u), 16, 0.25f), DryGrass, 0.9f, 2.5f, 2f, null, 0f, 0f, 0f, Hashing.Mix(v, 2u));
                TerrainPaint.Field(ctx, LeisureKit.Blob(p, r * 0.85f, Hashing.Mix(v, 3u), 16, 0.3f), Dirt, 1f, 2.5f, 2f, null, 0f, 0f, 0f, Hashing.Mix(v, 4u));
                // The dead tree on top (tree age 4 = dead), dead bushes on the flanks, a fallen log at the foot.
                ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 6u)) * 6.283f, ObjectRole.Tree, DeadTree, v, PlannedAge.Dead);
                int bushes = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 7u)) * 3f);
                for (int i = 0; i < bushes; i++)
                {
                    float ang = (i + Hashing.Unit(Hashing.Mix(v, 8u + (uint)i)) * 0.5f) / bushes * 6.283f;
                    var bp = p + new float2(math.cos(ang), math.sin(ang)) * r * 0.6f;
                    ctx.Object(bp, ang, ObjectRole.Bush, DeadBush.Pick(v), Hashing.Mix(v, 20u + (uint)i), -1, DeadBush.Category);
                }
                // The log along the mound's foot, only where it lies wholly on free ground.
                for (int t = 0; t < 4; t++)
                {
                    float ang = Hashing.Unit(Hashing.Mix(v, 40u + (uint)t)) * 6.283f;
                    var dir = new float2(math.cos(ang), math.sin(ang));
                    var lp = p + dir * (r * 0.9f);
                    if (s.Put(FallenLog, FallenLog.Pick(v), lp, new float2(-dir.y, dir.x), Hashing.Mix(v, 30u), 0.3f)) break;
                }
                n++;
            }
            return mounds;
        }

        /// <summary>A dead tree and dead bushes in each corner of the edge band that has room (weathered corners).</summary>
        public static int DeadCorners(IndustrialSite s, uint salt)
        {
            if (s.Spec.Edge < 3f) return 0;
            var ring = s.Region.Ring;
            int made = 0;
            for (int i = 0; i < ring.Length; i++)
            {
                var prev = ring[(i + ring.Length - 1) % ring.Length]; var c = ring[i]; var next = ring[(i + 1) % ring.Length];
                var e0 = math.normalizesafe(c - prev); var e1 = math.normalizesafe(next - c);
                if (Poly.SignedArea(ring) * (e0.x * e1.y - e0.y * e1.x) <= 0f) continue;   // reflex corner
                if (math.dot(e0, e1) > 0.5f) continue;                                      // not a real corner
                var bis = math.normalizesafe(-e0 + e1);
                var p = c + bis * (s.Spec.Edge * 0.9f + 1.2f);
                if (!s.Region.ContainsDisc(p, 1.5f) || !s.Occ.Free(p, 1.6f, 0.5f, 0.2f)) continue;
                uint v = Hashing.Mix(salt, (uint)i);
                s.Ctx.Object(p, Hashing.Unit(v) * 6.283f, ObjectRole.Tree, DeadTree, v, PlannedAge.Dead);
                s.Occ.Take(p, 1.6f, YardKit.KindStuff);
                foreach (var d in new[] { e1, -e0 })
                {
                    var bp = p + d * 2.6f;
                    if (s.Region.ContainsDisc(bp, 1f) && s.Occ.Free(bp, 0.9f, 0.3f, 0.1f))
                        s.Ctx.Object(bp, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Bush, DeadBush.Pick(v), Hashing.Mix(v, 3u), -1, DeadBush.Category);
                }
                made++;
            }
            return made;
        }

        // ───────────── decals ─────────────

        /// <summary>
        /// Wear decals with a reason (asset packs only): puddles at the aisle crossings and every 18 m along the mud, an oil
        /// stain under each machine and lorry, litter round the skips, cracks on the concrete pads.
        /// </summary>
        public static int Decals(IndustrialSite s, uint salt)
        {
            var ctx = s.Ctx;
            int n = 0;
            float a = s.Spec.Aisle;
            void Put(float2 p, string cat, uint v)
            {
                if (!s.Region.ContainsDisc(p, 1f) || !s.ReservedOnly.Free(p, 0.6f, 0f, 0f)) return;
                ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Decal, AssetMix.One(""), v, -1, cat);
                n++;
            }
            int k = 0;
            foreach (var c in Crossings(s))
                for (int i = 0; i < 2; i++)
                {
                    uint v = Hashing.Mix(salt, 0x7001u + (uint)k++);
                    var off = new float2(Hashing.Unit(v) - 0.5f, Hashing.Unit(Hashing.Mix(v, 2u)) - 0.5f) * a * 0.8f;
                    Put(c + off, AssetCategories.DecalPuddles, v);
                }
            foreach (var (p, q) in s.Aisles)
            {
                float len = math.distance(p, q);
                if (len < 20f) continue;
                var dir = (q - p) / len; var nrm = new float2(-dir.y, dir.x);
                int j = 0;
                for (float t = 12f; t < len - 8f; t += 26f, j++)
                {
                    uint v = Hashing.Mix(salt, 0x7100u + (uint)k++);
                    // On the aisle's edge, alternating sides: where the water stands at the kerb of a yard road.
                    Put(p + dir * t + nrm * ((j & 1) == 0 ? 1f : -1f) * (a * 0.5f - 0.6f), AssetCategories.DecalPuddles, v);
                }
            }
            foreach (var o in new List<PlannedObject>(ctx.Plan.Objects))
            {
                if (o.Role != ObjectRole.Furniture) continue;
                uint v = Hashing.Mix(salt, 0x7200u + (uint)k++);
                if (o.Category == AssetCategories.ScrapMachine || o.Category == AssetCategories.LogisticsTruck || (o.Category == AssetCategories.LandfillSkip && (k & 3) == 0))
                    Put(o.Position.xz, o.Category == AssetCategories.LandfillSkip ? AssetCategories.DecalLitter : AssetCategories.DecalDirt, v);
            }
            foreach (var b in s.Blocks)
                if (b.Use == "stacks" || b.Use == "bays" || b.Use == "plant")
                    for (int i = 0; i < 3; i++)
                    {
                        uint v = Hashing.Mix(salt, 0x7300u + (uint)(b.Index * 7 + i));
                        Put(s.W(math.lerp(b.U0, b.U1, Hashing.Unit(v)), b.AisleFront ? b.V0 + 0.8f : b.V1 - 0.8f), AssetCategories.DecalCracks, v);
                    }
            return n;
        }
    }
}

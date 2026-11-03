// Industrial category: the organic scrapyard (2026-10-19, the user after the block-grid version: "perfectly laid out scrap
// piles in a grid. It's horrible ... not lived in"). A scrapyard is not a car park. Seen from above it is one continuous field
// of scrap that the tracks were pushed through:
//
//   * tracks, not aisles: from the gate a worn track wanders to the back of the yard; on a bigger yard a loop leaves it and
//     comes back, and short spurs run off towards the fence. They bend because they went round whatever was there;
//   * the field: everything between the tracks is scrap, packed edge to edge. What kind changes slowly over the yard (value
//     noise zones): piled wrecks here, metal and rubble there, a corner of trash, drums and pallets. Small clearings where a
//     loader works. The ground swells where the heaps are biggest;
//   * along the tracks the wrecks stand nose to the track, the second row behind piled higher; against the fence a windrow of
//     cars piled two and three high;
//   * the front yard (gate, booth, office, staff cars) stays as designed: that part of a yard is tidy.
//
// Everything still respects the rules the game sets: two props whose boxes meet lose one, so the field packs props touching,
// never inside each other; stacked cars rest on the roofs below (YardFill.Mess.Height).

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public static class OrganicYard
    {
        /// <summary>A zone of the field: what is tipped there (with shares) and how dense.</summary>
        public struct Zone
        {
            public YardFill.Part[] Mix;
            public float Density;
            public float Stack;
            public Zone(YardFill.Part[] mix, float density, float stack) { Mix = mix; Density = density; Stack = stack; }
        }

        public const float TrackWidth = 6.5f;

        // ───────────── tracks ─────────────

        /// <summary>
        /// The tracks: the main one from the gate to the back, wandering; on a yard of more than ~0.6 ha a loop round the middle
        /// that leaves the main track and comes back to it; a spur or two to the fence. Stored as the site's aisles (centre
        /// segments) so the masts, the mud and the decals find them.
        /// </summary>
        public static void Tracks(IndustrialSite s, uint salt)
        {
            float hw = TrackWidth * 0.5f;
            float front = s.Spec.Front;
            float uSpan = s.UMax - s.UMin, vTop = s.VMax - hw - 3f;
            var main = new List<float2> { s.W(0f, -1.5f), s.W(0f, front) };
            float u = 0f;
            int k = 0;
            for (float v = front + 18f; v < vTop; v += 16f + Hashing.Unit(Hashing.Mix(salt, (uint)k)) * 10f, k++)
            {
                u += (Hashing.Unit(Hashing.Mix(salt, 100u + (uint)k)) - 0.5f) * uSpan * 0.22f;
                u = math.clamp(u, s.UMin + hw + 8f, s.UMax - hw - 8f);
                var p = s.W(u, v);
                if (!s.Inner.ContainsDisc(p, hw + 2f)) break;
                main.Add(p);
            }
            if (main.Count < 3 && s.Inner.ContainsDisc(s.W(0f, (front + vTop) * 0.5f), hw + 1f)) main.Add(s.W(0f, (front + vTop) * 0.5f));
            var lines = new List<List<float2>> { Smooth(main, 2) };
            // The loop round the middle.
            if (s.Inner.Area > 6000f && uSpan > 70f)
            {
                // Off the middle and lumpy: it went round whatever was there, it is not a racetrack.
                var c = s.W((s.UMin + s.UMax) * 0.5f + (Hashing.Unit(Hashing.Mix(salt, 290u)) - 0.5f) * uSpan * 0.15f, (front + s.VMax) * 0.5f + 4f);
                float ru = uSpan * 0.3f, rv = (s.VMax - front) * 0.28f;
                var loop = new List<float2>();
                for (int i = 0; i < 9; i++)
                {
                    float a = (i + (Hashing.Unit(Hashing.Mix(salt, 310u + (uint)i)) - 0.5f) * 0.5f) / 9f * 6.283f;
                    float j = 0.7f + 0.55f * Hashing.Unit(Hashing.Mix(salt, 300u + (uint)i));
                    var p = c + s.U * math.cos(a) * ru * j + s.V * math.sin(a) * rv * j;
                    if (s.Inner.ContainsDisc(p, hw + 2f)) loop.Add(p);
                }
                if (loop.Count >= 7) { loop.Add(loop[0]); lines.Add(Smooth(loop, 2)); }
            }
            // Spurs to the fence (one a side on a wide yard).
            foreach (int side in new[] { -1, 1 })
            {
                if (uSpan < 90f) break;
                float v = front + (s.VMax - front) * (0.3f + 0.35f * Hashing.Unit(Hashing.Mix(salt, 400u + (uint)(side + 1))));
                var a = Nearest(lines[0], v, s);
                var b = s.W(side > 0 ? s.UMax - hw - 4f : s.UMin + hw + 4f, v + (Hashing.Unit(Hashing.Mix(salt, 410u)) - 0.5f) * 12f);
                if (s.Inner.ContainsDisc(b, hw + 1f) && !s.Inner.CrossesBoundary(a, b, true))
                    lines.Add(Smooth(new List<float2> { a, math.lerp(a, b, 0.5f) + s.V * (Hashing.Unit(Hashing.Mix(salt, 420u + (uint)(side + 1))) - 0.5f) * 10f, b }, 2));
            }
            s.Aisles.Clear();
            foreach (var line in lines)
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    if (math.distance(line[i], line[i + 1]) < 0.5f) continue;
                    s.Aisles.Add((line[i], line[i + 1]));
                    s.Occ.TakeCorridor(line[i], line[i + 1], hw, YardKit.KindHaul);
                }
            s.Tracks.AddRange(lines);
            s.SpineEnd = front;
        }

        private static float2 Nearest(List<float2> line, float v, IndustrialSite s)
        {
            float2 best = line[0]; float bd = float.MaxValue;
            foreach (var p in line) { float d = math.abs(s.F(p).y - v); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        /// <summary>Chaikin corner cutting: a track bends, it does not kink.</summary>
        private static List<float2> Smooth(List<float2> pts, int passes)
        {
            bool closed = pts.Count > 2 && math.distance(pts[0], pts[pts.Count - 1]) < 0.01f;
            for (int p = 0; p < passes; p++)
            {
                var r = new List<float2>();
                if (!closed) r.Add(pts[0]);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    r.Add(math.lerp(pts[i], pts[i + 1], 0.25f));
                    r.Add(math.lerp(pts[i], pts[i + 1], 0.75f));
                }
                if (!closed) r.Add(pts[pts.Count - 1]); else r.Add(r[0]);
                pts = r;
            }
            return pts;
        }

        // ───────────── rows along the tracks ─────────────

        /// <summary>
        /// Wrecks along the tracks: nose to the track on both sides, a second row behind piled higher, gaps where nobody
        /// parked. They follow the track's bends, so the rows curve.
        /// </summary>
        public static int TrackRows(IndustrialSite s, YardItem car, float fill, uint salt)
        {
            int made = 0;
            float hw = TrackWidth * 0.5f;
            int k = 0;
            foreach (var line in s.Tracks)
            {
                float carry = 0f;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var a = line[i]; var b = line[i + 1];
                    float len = math.distance(a, b);
                    if (len < 0.1f) continue;
                    var dir = (b - a) / len;
                    var n = new float2(-dir.y, dir.x);
                    float t = carry;
                    for (; t < len; t += car.X + 0.7f + Hashing.Unit(Hashing.Mix(salt, (uint)k)) * 0.8f, k++)
                    {
                        var p = a + dir * t;
                        if (s.F(p).y < s.Spec.Front + 2f) continue;
                        foreach (float side in new[] { -1f, 1f })
                            for (int row = 0; row < 2; row++)
                            {
                                uint v = Hashing.Mix(salt, (uint)(k * 4 + (side > 0 ? 2 : 0) + row));
                                if (Hashing.Unit(v) > fill * (row == 0 ? 0.85f : 0.75f)) continue;
                                var c = p + n * side * (hw + 0.6f + car.Z * 0.5f + row * (car.Z + 0.5f));
                                var m = YardFill.Mess.Wrecks(row == 0 ? 0.2f : 0.6f);
                                m.Yaw = 0.25f;
                                made += YardFill.PlaceOne(s, car, car.Pick(v), c, n * -side, v, m);
                            }
                    }
                    carry = t - len;
                }
            }
            s.Wrecks += made;
            return made;
        }

        // ───────────── the field ─────────────

        /// <summary>
        /// The scrap field: every free spot inside the yard (outside the front yard and the tracks) gets something from the zone
        /// it lies in, the zones drifting over the yard with value noise; big things first, then the small stuff in the gaps;
        /// clearings where the density noise is low. Swells of terrain under the densest heaps.
        /// </summary>
        public static int Field(IndustrialSite s, Zone[] zones, float fill, uint salt)
        {
            var ctx = s.Ctx;
            int made = 0;
            uint zn = Hashing.Mix(salt, 0x2011u);
            var cands = YardKit.Shuffled(Scatter.Candidates(s.Inner, 1.7f, Hashing.Mix(salt, 1u), 90000, ctx.Cancel), Hashing.Mix(salt, 2u));
            float frontZone = s.Spec.FrontZone + 2f, front = s.Spec.Front;
            // Piles (the game showed evenly spread scrap read as confetti): the scrap is heaped in piles of 5–10 m with trodden
            // ground between them, crossed by the odd lone wreck or drum. A pile may lap up to its track.
            var piles = new List<(float2 c, float r)>();
            foreach (var p in YardKit.Shuffled(Scatter.Candidates(s.Inner, 12f + 6f * (1f - math.saturate(fill)), Hashing.Mix(salt, 7u), 600, ctx.Cancel), Hashing.Mix(salt, 8u)))
            {
                var f = s.F(p);
                if (f.y < front + 4f && math.abs(f.x) < frontZone + 2f) continue;
                float r = 5f + 5f * Hashing.Unit(Hashing.Mix(salt, 900u + (uint)piles.Count));
                foreach (var (q0, q1) in s.Aisles) r = math.min(r, math.sqrt(SegmentIndex.DistSq(p, q0, q1)) - TrackWidth * 0.5f + 1.5f);
                r = math.min(r, s.Inner.BoundaryDistance(p, 12f) + 2f);
                if (r >= 3.5f) piles.Add((p, r));
            }
            // Piles by 12 m bucket (a candidate only looks at the piles round it).
            var buckets = new Dictionary<long, List<int>>();
            long Key(int bx, int by) => ((long)bx << 32) ^ (uint)by;
            for (int pi = 0; pi < piles.Count; pi++)
            {
                var (pc, pr) = piles[pi];
                for (int bx = (int)math.floor((pc.x - pr) / 12f); bx <= (int)math.floor((pc.x + pr) / 12f); bx++)
                    for (int by = (int)math.floor((pc.y - pr) / 12f); by <= (int)math.floor((pc.y + pr) / 12f); by++)
                    {
                        if (!buckets.TryGetValue(Key(bx, by), out var l)) buckets[Key(bx, by)] = l = new List<int>();
                        l.Add(pi);
                    }
            }
            var swells = new List<(float2 p, float score)>();
            for (int pass = 0; pass < 2; pass++)
            {
                int i = 0;
                foreach (var p in cands)
                {
                    i++;
                    var f = s.F(p);
                    if (f.y < front + 2f && math.abs(f.x) < frontZone) continue;
                    float t = 9f;
                    if (buckets.TryGetValue(Key((int)math.floor(p.x / 12f), (int)math.floor(p.y / 12f)), out var near))
                        foreach (var pi in near) t = math.min(t, math.distance(p, piles[pi].c) / piles[pi].r);
                    var zone = zones[math.min(zones.Length - 1, (int)(ValueNoise.Clumps(p, 30f, zn) * zones.Length))];
                    // In a pile: packed, denser in its middle; between piles: one thing in a dozen spots.
                    float dens = t < 1f ? zone.Density * (1f - 0.25f * t) : 0.07f;
                    if (Hashing.Unit(Hashing.Mix(salt, (uint)i * 7u + (uint)pass)) > dens) continue;
                    uint v = Hashing.Mix(salt, (uint)i * 31u + (uint)pass);
                    var item = PickFrom(zone.Mix, Hashing.Unit(Hashing.Mix(v, 1u)), pass == 0);
                    if (item.Options == null) continue;
                    var dir = new float2(math.sin(Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f), math.cos(Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f));
                    // Small things turned any way need their whole circle free (their corners would cut into a neighbour).
                    if (!s.Occ.Free(p, pass == 0 ? math.min(item.X, item.Z) * 0.5f : item.Radius * 0.92f, 0.3f, 0.05f)) continue;
                    int n;
                    if (item.Category == AssetCategories.ScrapWreck)
                    {
                        var m = YardFill.Mess.Wrecks(t < 1f ? math.min(0.9f, zone.Stack + 0.3f * (1f - t)) : 0.05f);
                        m.Pos = 0f; m.Yaw = 0f; m.Flip = 0.14f; m.Side = 0.07f;
                        n = YardFill.PlaceOne(s, item, item.Pick(v), p, dir, v, m);
                        if (n > 0) s.Wrecks += n;
                    }
                    else if (s.Put(item, item.Pick(v), p, dir, v, 0.03f))
                    {
                        n = 1;
                        if (pass == 1) s.Occ.Take(p, item.Radius, YardKit.KindStuff);   // its whole turning circle
                    }
                    else n = 0;
                    made += n;
                }
            }
            foreach (var (pc, pr) in piles) if (pr >= 6f) swells.Add((pc, pr));
            // Swells under the densest heaps: a few, well apart.
            swells.Sort((x, y) => y.score.CompareTo(x.score));
            var picked = new List<float2>();
            foreach (var (p, _) in swells)
            {
                if (picked.Count >= math.max(3, (int)(s.Inner.Area / 2500f))) break;
                bool near = false;
                foreach (var q in picked) if (math.distance(p, q) < 18f) { near = true; break; }
                if (near) continue;
                float r = math.min(12f, s.Inner.BoundaryDistance(p, 16f) - 1f);
                foreach (var (q0, q1) in s.Aisles) r = math.min(r, math.sqrt(SegmentIndex.DistSq(p, q0, q1)) - TrackWidth * 0.5f - 1f);
                if (r < 5f) continue;
                picked.Add(p);
                s.Mounds.Add(new Mound { Center = p, Radius = r, Height = math.min(2.6f, r * 0.25f), Flat = 0.25f });
                s.Anchors.Add((p, r));
                s.Heaps++;
            }
            return made;
        }

        /// <summary>A part by share; the first pass takes only the big things (≥ 4 m), the second only the small ones.</summary>
        private static YardItem PickFrom(YardFill.Part[] mix, float r, bool big)
        {
            float total = 0f;
            foreach (var p in mix) if ((math.max(p.Item.X, p.Item.Z) >= 4f) == big) total += p.Share;
            if (total <= 0f) return default;
            r *= total;
            foreach (var p in mix)
            {
                if ((math.max(p.Item.X, p.Item.Z) >= 4f) != big) continue;
                if (r < p.Share) return p.Item;
                r -= p.Share;
            }
            return default;
        }

        // ───────────── the work pad ─────────────

        private static readonly YardItem Conveyor = IndustrialAssets.Conveyor;

        /// <summary>
        /// The working heart of the yard, kept clear before the field fills in: the most open spot off the tracks, a loader in
        /// the middle of it, open skips in an arc, a generator and (for the crusher) the conveyor out of the crusher. Returns
        /// false when the yard has no room for one.
        /// </summary>
        public static bool WorkPad(IndustrialSite s, bool crusher, uint salt)
        {
            var c = s.Inner.DeepestPoint(out float clear, (q, cap) => s.Occ.Clearance(q, cap), 18);
            var f = s.F(c);
            if (f.y < s.Spec.Front + 6f) return false;
            float r = math.min(11f, clear - 0.8f);
            if (r < 6f) return false;
            uint v = salt;
            var face = math.normalizesafe(s.Inner.Centroid - c, s.V);
            if (math.lengthsq(s.Inner.Centroid - c) < 1f) face = s.V;
            var loader = IndustrialAssets.Loader;
            if (s.Put(loader, loader.Pick(v), c, YardFill.Turn(face, (Hashing.Unit(v) - 0.5f) * 1.2f), Hashing.Mix(v, 1u), 0.05f)) s.Machines++;
            var skip = IndustrialAssets.OpenSkip;
            int n = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 2u)) * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = (i - (n - 1) * 0.5f) * 0.55f + 3.14f;
                var dir = YardFill.Turn(face, a);
                var p = c + dir * (r - skip.X * 0.5f - 0.5f);
                s.Put(skip, skip.Pick(Hashing.Mix(v, 3u + (uint)i)), p, YardFill.Turn(dir, 1.57f + (Hashing.Unit(Hashing.Mix(v, 9u + (uint)i)) - 0.5f) * 0.3f), Hashing.Mix(v, 20u + (uint)i), 0.05f);
            }
            var gen = IndustrialAssets.Generator;
            s.Put(gen, gen.Pick(v), c + YardFill.Turn(face, 1.2f) * (r - 3f), YardFill.Turn(face, 0.3f), Hashing.Mix(v, 30u), 0.05f);
            if (crusher)
            {
                // The crusher: the plant block with its conveyor tipping into a skip.
                var plant = IndustrialAssets.PlantSmall;
                var pc = c + YardFill.Turn(face, -1.3f) * (r - plant.Z * 0.5f - 0.5f);
                if (s.Put(plant, plant.Pick(v), pc, face, Hashing.Mix(v, 40u), 0.05f))
                    s.Put(Conveyor, Conveyor.Pick(v), pc + YardFill.Turn(face, -1.3f + 1.57f) * (plant.X * 0.5f + Conveyor.Z * 0.5f), YardFill.Turn(face, -1.3f + 1.57f), Hashing.Mix(v, 41u), 0.05f);
            }
            s.Occ.Take(c, r, YardKit.KindStuff);
            s.Anchors.Add((c, r));
            s.PadAt = c; s.PadR = r;
            return true;
        }

        // ───────────── ground ─────────────

        /// <summary>
        /// The tracks are dirt roads (the user, 2026-10-19: "maybe the road should be a dirt road so it doesn't look as perfect"):
        /// no surface strip, the earth itself painted the grey-beige of compacted gravel, ragged at the edges, over the yard's
        /// browner earth (which is painted a little weaker so the road shows).
        /// </summary>
        public static void TrackSurfaces(IndustrialSite s)
        {
            float hw = TrackWidth * 0.5f;
            int k = 0;
            foreach (var (a, b) in s.Aisles)
            {
                float len = math.distance(a, b);
                if (len < 0.3f) continue;
                var dir = (b - a) / len;
                var ring = LayerRegion.Rect((a + b) * 0.5f, dir, len * 0.5f + hw * 0.6f, hw);
                TerrainPaint.Field(s.Ctx, ring, YardGround.Gravel, 1f, 1.2f, 1.4f, null, 0f, 0f, 0f, Hashing.Mix(s.Ctx.Seed, 0x7ACu + (uint)k++));
            }
        }

        // ───────────── spill ─────────────

        private static readonly YardItem[] SpillItems = { IndustrialAssets.Drums, IndustrialAssets.Drums, IndustrialAssets.Crates, IndustrialAssets.Gas, IndustrialAssets.Tyres, IndustrialAssets.Pallets, IndustrialAssets.Trash };

        /// <summary>
        /// Junk spilling onto the roads (the user: "sometimes the cars and the whatever junk should spill onto the road"): every
        /// so often along a track a drum or two, a crate, a tyre load or a pallet lies on its edge, and now and then a wreck
        /// stands half on it, left where the forklift dropped it. Never more than the outer 1.8 m of the road, so it stays
        /// drivable, never on another prop.
        /// </summary>
        public static int Spill(IndustrialSite s, float fill, uint salt)
        {
            var ctx = s.Ctx;
            float hw = TrackWidth * 0.5f;
            // What is already there (cars and props), as discs: the track itself is not in this index.
            var placed = new DiscIndex(6f);
            foreach (var o in ctx.Plan.Objects)
                if (o.Role != ObjectRole.Decal) placed.Add(o.Position.xz, o.Category == AssetCategories.ScrapWreck ? 2.6f : 1.2f, 0);
            int n = 0, k = 0;
            foreach (var line in s.Tracks)
            {
                float next = 8f + Hashing.Unit(Hashing.Mix(salt, (uint)line.Count)) * 10f, at = 0f;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var a = line[i]; var b = line[i + 1];
                    float len = math.distance(a, b);
                    if (len < 0.1f) continue;
                    var dir = (b - a) / len;
                    var nrm = new float2(-dir.y, dir.x);
                    for (; next <= at + len; next += 9f + Hashing.Unit(Hashing.Mix(salt, (uint)k * 3u + 1u)) * 16f, k++)
                    {
                        uint v = Hashing.Mix(salt, (uint)k);
                        if (Hashing.Unit(v) > 0.45f + 0.35f * math.saturate(fill)) continue;
                        var p0 = a + dir * (next - at);
                        if (s.F(p0).y < s.Spec.Front + 4f) continue;
                        float side = (v & 1u) == 0 ? 1f : -1f;
                        bool car = Hashing.Unit(Hashing.Mix(v, 1u)) < 0.18f;
                        var item = car ? IndustrialAssets.Car : SpillItems[(int)(Hashing.Mix(v, 2u) % (uint)SpillItems.Length)];
                        // How far onto the road: a car stands half on it (its near side 1.8 m in), small things 0.3-1.5 m in.
                        float into = car ? 1.8f : 0.3f + 1.2f * Hashing.Unit(Hashing.Mix(v, 3u));
                        float rr = car ? item.X * 0.5f : item.Radius;
                        var p = p0 + nrm * side * (hw - into + rr);
                        float yaw = car ? LeisureKit.Yaw(p, p + dir) + (Hashing.Unit(Hashing.Mix(v, 4u)) - 0.5f) * 0.8f : Hashing.Unit(Hashing.Mix(v, 4u)) * 6.283f;
                        float cr = car ? 2.7f : item.Radius;
                        if (!s.Region.ContainsDisc(p, cr) || placed.Overlaps(p, cr, 0.1f) || !s.ReservedOnly.Free(p, cr, 0f, 0.2f)) continue;
                        var o = ctx.Object(p, yaw, ObjectRole.Furniture, item.Pick(v), v, -1, item.Category);
                        if (car && Hashing.Unit(Hashing.Mix(v, 5u)) < 0.25f) { o.Roll = math.PI * 0.5f * side; o.Elevation = item.X * 0.5f; }
                        placed.Add(p, cr, 0);
                        n++;
                        // A second drum or crate beside the first.
                        if (!car && Hashing.Unit(Hashing.Mix(v, 6u)) < 0.5f)
                        {
                            var q = p + dir * (item.Radius * 2.1f);
                            if (s.Region.ContainsDisc(q, cr) && !placed.Overlaps(q, cr, 0.1f) && s.ReservedOnly.Free(q, cr, 0f, 0.2f))
                            {
                                ctx.Object(q, Hashing.Unit(Hashing.Mix(v, 7u)) * 6.283f, ObjectRole.Furniture, item.Pick(Hashing.Mix(v, 8u)), Hashing.Mix(v, 9u), -1, item.Category);
                                placed.Add(q, cr, 0);
                                n++;
                            }
                        }
                    }
                    at += len;
                }
            }
            s.Items += n;
            return n;
        }

        /// <summary>Rubbish and rusty ground under the field in big ragged blobs (it was a tip before it was a yard).</summary>
        public static int FieldGround(IndustrialSite s, uint salt)
        {
            int n = 0;
            var c = s.Inner.Centroid;
            float rad = math.sqrt(s.Inner.Area) * 0.22f;
            var spots = new List<float2>();
            foreach (var p in Scatter.Candidates(s.Inner, rad * 0.9f, Hashing.Mix(salt, 5u), 60, s.Ctx.Cancel)) spots.Add(p);
            for (int i = 0; i < spots.Count && n < 14; i++)
            {
                var p = spots[i];
                var f = s.F(p);
                if (f.y < s.Spec.Front + 4f && math.abs(f.x) < s.Spec.FrontZone + 6f) continue;
                uint v = Hashing.Mix(salt, (uint)i);
                var asset = (v % 3u) == 0 ? YardGround.Hard.Ore : YardGround.Hard.Rubbish;
                // Blobs never overlap (two ground surfaces on one spot flicker): at most 0.45 of their spacing.
                float br = rad * 0.9f * (0.36f + 0.08f * Hashing.Unit(v));
                // Off the dirt roads (a surface would cover the road's paint).
                foreach (var (q0, q1) in s.Aisles) br = math.min(br, math.sqrt(SegmentIndex.DistSq(p, q0, q1)) - TrackWidth * 0.5f - 0.5f);
                if (br < 4f) continue;
                var blob = LeisureKit.Blob(p, br, Hashing.Mix(v, 1u), 16, 0.3f);
                try
                {
                    var pieces = Poly.Intersection(blob, s.Inner.Ring);
                    foreach (var piece in pieces)
                        if (piece.Outer != null && piece.Outer.Length >= 3 && Poly.Area(piece.Outer) > 30f && s.Ctx.Surface(piece.Outer, SurfaceRole.Ground, asset, AssetCategories.SurfaceNatural)) n++;
                }
                catch (System.Exception) { }
            }
            return n;
        }
    }
}

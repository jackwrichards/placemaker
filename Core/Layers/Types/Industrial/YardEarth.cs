// Industrial category, the earth of a yard (the user, 2026-10-19: "3D objects that make like a dirt pile on the ground ... Let
// your mind take you to amazing lands", the game's own pieces only, never an add-on pack). What the game's own ground pieces
// make of a yard; every piece was looked at in the game runner first (suite/scenarios/lab-ground.json, the lab "lineup"):
//
//   * a bund of boulders along the fence (edge "boulders"): the eight 5 m boulders shoulder to shoulder, any way round, the
//     gaps stopped with stones, open at the gates; the way real yards keep lorries off the fence;
//   * dirt banks: a long terraformed bank along a stretch of the back or side fence, the mining truck's dirt heap
//     (OreMiningTruck01_OrePile03, a 7.6 x 9 m mound, the only vanilla dirt pile that draws on its own) in a chain along
//     its crest, stones at its foot, a dead bush; painted earth;
//   * scree: stones (the 1 m Rock01-30) spilled round every heap and the work pad, and in twos and threes along the dirt roads;
//   * crushed concrete: the grey concrete rubble heaps in an arc by the work pad, a boulder at their end;
//   * a burn barrel (BarrelFire01/02) by the office with pallets round it to sit on.
//
// Not used, and why (seen in the game): the vanilla Pile{Sand,Stone,Coal,Ore,Garbage,Crop}{Small..Massive}01 storage heaps
// draw nothing on their own (their mesh follows a stored resource); every "...Random01" prefab draws as a blue placeholder;
// the standing stones and stone heads are monuments, not yard stone.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public static class YardEarth
    {
        public static readonly YardItem Boulder = new YardItem(AssetCategories.Rock, 5.4f, 5.0f,
            "Boulder01", "Boulder02", "Boulder03", "Boulder04", "Boulder05", "Boulder06", "Boulder07", "Boulder08");
        public static readonly YardItem Stone = new YardItem(AssetCategories.Rock, 1.0f, 0.9f,
            "Rock01", "Rock03", "Rock05", "Rock06", "Rock09", "Rock11", "Rock14", "Rock17", "Rock20", "Rock24", "Rock26", "Rock28");
        public static readonly YardItem DirtHeap = new YardItem(AssetCategories.ConstructionPile, 7.6f, 9.0f, "OreMiningTruck01_OrePile03");
        public static readonly YardItem FireBarrel = new YardItem(AssetCategories.ScrapBarrel, 0.8f, 0.8f, "BarrelFire01", "BarrelFire02");
        private static readonly YardItem DeadBush = new YardItem(AssetCategories.Bush, 1.8f, 1.8f, "FlowerBushWildDead02");

        // ───────────── the boulder bund ─────────────

        /// <summary>
        /// Boulders shoulder to shoulder along every side of the fence (the street side too: that is where a lorry would
        /// drive through), each turned any way, a little in and out of line; where one does not fit, two or three stones.
        /// Open at the gates and the corners.
        /// </summary>
        public static int Bund(IndustrialSite s, uint salt)
        {
            var region = s.Region;
            int n = region.Ring.Length, made = 0;
            // Roughly along the fence (a boulder turned square to it would poke its corner through), as deep as it reaches.
            const float wobble = 0.35f;
            float reach = Boulder.X * 0.5f * math.cos(wobble) + Boulder.Z * 0.5f * math.sin(wobble);
            float off = reach + 0.3f;
            for (int i = 0; i < n; i++)
            {
                var a = region.Ring[i]; var b = region.Ring[(i + 1) % n];
                float len = math.distance(a, b);
                if (len < 6f) continue;
                var t = (b - a) / len;
                var inward = region.InwardNormal(i);
                uint ev = Hashing.Mix(salt, (uint)i);
                int k = 0;
                for (float d = 3.4f + Hashing.Unit(ev) * 1.5f; d < len - 3.4f; d += 5.0f + Hashing.Unit(Hashing.Mix(ev, (uint)k)) * 1.3f, k++)
                {
                    uint v = Hashing.Mix(ev, 0x100u + (uint)k);
                    var p = a + t * d + inward * (off + Hashing.Unit(Hashing.Mix(v, 1u)) * 0.6f);
                    if (NearGate(s, p, 3.5f)) continue;
                    var along = YardFill.Turn(t, (Hashing.Unit(Hashing.Mix(v, 2u)) * 2f - 1f) * wobble) * ((v & 4u) == 0 ? 1f : -1f);
                    // Set 0.3-1 m into the ground (seen in the game: a sunk boulder stays put and reads as bedded in).
                    if (s.Put(Boulder, Boulder.Pick(v), p, along, v, 0.02f)) { Sink(s, 0.3f + Hashing.Unit(Hashing.Mix(v, 7u)) * 0.7f); made++; }
                    else made += YardFill.Clump(s, p, 1.3f, Stone, 2 + (int)(Hashing.Unit(Hashing.Mix(v, 3u)) * 2f), Hashing.Mix(v, 4u));
                    // Stones in the gap to the next boulder now and then.
                    if (Hashing.Unit(Hashing.Mix(v, 5u)) < 0.35f)
                        made += YardFill.Clump(s, p + t * 2.9f + inward * 0.8f, 0.9f, Stone, 2, Hashing.Mix(v, 6u));
                }
            }
            return made;
        }

        /// <summary>Sets the prop just placed this deep into the ground (PlannedObject.Elevation &lt; 0).</summary>
        private static void Sink(IndustrialSite s, float m)
        {
            var list = s.Ctx.Plan.Objects;
            if (list.Count > 0) list[list.Count - 1].Elevation = -m;
        }

        /// <summary>Inside one of a block yard's blocks (their pads and slabs are concrete: no stones on them).</summary>
        private static bool InBlock(IndustrialSite s, float2 p)
        {
            if (s.Blocks.Count == 0) return false;
            var f = s.F(p);
            foreach (var b in s.Blocks)
                if (f.x > b.U0 - 0.5f && f.x < b.U1 + 0.5f && f.y > b.V0 - 0.5f && f.y < b.V1 + 0.5f) return true;
            return false;
        }

        private static bool NearGate(IndustrialSite s, float2 p, float extra)
        {
            foreach (var g in s.Gates) if (math.distance(g.Position, p) < s.Spec.Aisle * 0.5f + extra + 2.7f) return true;
            return false;
        }

        // ───────────── dirt banks ─────────────

        /// <summary>
        /// Up to <paramref name="max"/> long banks of earth along the back and side fence where the ground is free: a
        /// terraformed bank (an ellipse along the fence), dirt heaps in a chain along its crest, stones and a dead bush at its
        /// foot, earth painted over it. Claims its ground, so the yard's fill keeps off it. <paramref name="setBack"/>: this much
        /// further in (behind a boulder bund).
        /// </summary>
        public static int Banks(IndustrialSite s, int max, uint salt, float setBack = 0f)
        {
            if (max <= 0) return 0;
            var ctx = s.Ctx;
            var inner = s.Inner;
            float per = inner.Perimeter;
            const float depth = 10f;
            var found = new List<(float2 a, float2 b, float2 inward)>();
            // Walk the inner outline: a run of free ground 10 m deep, at least 22 m long, never on the street side.
            float runStart = -1f;
            float2 runA = default, runIn = default;
            for (float t = 0f; t <= per && found.Count < max * 3; t += 3f)
            {
                var e = inner.PointAt(t, out _, out var inw, out _);
                bool ok = math.dot(inw, s.V) < 0.6f && s.F(e).y > s.Spec.Front + 6f;
                if (ok)
                {
                    var mid = e + inw * (depth * 0.5f + 0.4f + setBack);
                    ok = s.Occ.Free(mid, depth * 0.5f, 0f, 0.5f) && inner.ContainsDisc(mid, depth * 0.5f - 0.5f) && !NearGate(s, e, 8f);
                }
                if (ok && runStart < 0f) { runStart = t; runA = e; runIn = inw; }
                bool turn = runStart >= 0f && math.dot(inw, runIn) < 0.94f;
                if ((!ok || turn || t + 3f > per) && runStart >= 0f)
                {
                    float len = t - 3f - runStart;
                    if (len >= 22f)
                    {
                        var eb = inner.PointAt(t - 3f, out _, out _, out _);
                        found.Add((runA, eb, runIn));
                    }
                    runStart = -1f;
                    if (ok && turn) { runStart = t; runA = e; runIn = inw; }
                }
            }
            found.Sort((x, y) => math.distancesq(y.a, y.b).CompareTo(math.distancesq(x.a, x.b)));
            int made = 0, banks = 0;
            foreach (var (ra, rb, inw) in found)
            {
                if (banks >= max) break;
                float len = math.min(math.distance(ra, rb), 48f);
                var t = math.normalizesafe(rb - ra);
                var c = (ra + rb) * 0.5f + inw * (depth * 0.5f + 0.4f + setBack);
                if (!s.Occ.Free(c, depth * 0.5f, 0f, 0.5f)) continue;
                uint v = Hashing.Mix(salt, (uint)banks);
                float h = 2.2f + Hashing.Unit(v) * 1.2f;
                var a = c - t * (len * 0.5f - depth * 0.5f);
                var b = c + t * (len * 0.5f - depth * 0.5f);
                s.Mounds.Add(new Mound { Center = c, Radius = depth * 0.5f, Height = h, Flat = 0.2f, Axis = t, Ratio = math.max(1f, len / depth) });
                // Earth over it, ragged.
                TerrainPaint.Field(ctx, LeisureKit.Blob(c, depth * 0.62f, Hashing.Mix(v, 1u), 18, 0.25f), YardGround.Dirt, 1f, 2f, 1.5f, null, 0f, 0f, 0f, Hashing.Mix(v, 2u));
                foreach (var end in new[] { a, b })
                    TerrainPaint.Field(ctx, LeisureKit.Blob(end, depth * 0.6f, Hashing.Mix(v, 3u), 16, 0.3f), YardGround.Dirt, 1f, 2f, 1.5f, null, 0f, 0f, 0f, Hashing.Mix(v, 4u));
                // Along the crest, one every 6-8 m: spoil worked into the bank (mixed and concrete rubble sunk to their tops), now and
                // then a boulder, rarely the dirt heap (seen in the game: the heap is clay-orange, a row of them reads as rugs).
                int j = 0;
                for (float d = 3.5f; d < len - 3.5f; d += 6f + Hashing.Unit(Hashing.Mix(v, 10u + (uint)j)) * 2f, j++)
                {
                    uint w = Hashing.Mix(v, 0x40u + (uint)j);
                    var p = ra + t * (len * 0.5f - math.min(len, 48f) * 0.5f + d) + inw * (depth * 0.5f + 0.4f + setBack + (Hashing.Unit(w) - 0.5f) * 1.6f);
                    var dir = YardFill.Turn(t, 1.57f + (Hashing.Unit(Hashing.Mix(w, 1u)) - 0.5f) * 0.9f);
                    float pick = Hashing.Unit(Hashing.Mix(w, 3u));
                    var item = pick < 0.12f ? DirtHeap : pick < 0.32f ? Boulder : pick < 0.66f ? IndustrialAssets.MixedRubble : IndustrialAssets.Rubble;
                    float sink = item.Category == AssetCategories.Rock ? 0.5f + Hashing.Unit(Hashing.Mix(w, 2u)) * 0.6f
                        : item.Z > 8.5f && item.X > 7f && item.Category != AssetCategories.ConstructionRubble ? 0.6f + Hashing.Unit(Hashing.Mix(w, 2u)) * 0.3f
                        : 0.35f + Hashing.Unit(Hashing.Mix(w, 2u)) * 0.4f;
                    if (s.Put(item, item.Pick(w), p, dir, w, 0.02f)) { Sink(s, sink); made++; }
                }
                // Stones at its foot on the yard side; a dead bush or two.
                for (int q = 0; q < 3 + (int)(len / 14f); q++)
                {
                    uint w = Hashing.Mix(v, 0x80u + (uint)q);
                    var p = c + t * ((Hashing.Unit(w) - 0.5f) * (len - 4f)) + inw * (depth * 0.5f + 0.6f);
                    made += YardFill.Clump(s, p, 1.4f, Stone, 2 + (int)(Hashing.Unit(Hashing.Mix(w, 1u)) * 4f), Hashing.Mix(w, 2u));
                }
                for (int q = 0; q < 2; q++)
                {
                    uint w = Hashing.Mix(v, 0xC0u + (uint)q);
                    var p = c + t * ((Hashing.Unit(w) - 0.5f) * (len - 6f)) - inw * (depth * 0.35f);
                    if (s.Region.ContainsDisc(p, 1f) && s.Occ.Free(p, 0.9f, 0f, 0.1f))
                    {
                        ctx.Object(p, Hashing.Unit(Hashing.Mix(w, 1u)) * 6.283f, ObjectRole.Bush, DeadBush.Pick(w), Hashing.Mix(w, 2u), -1, DeadBush.Category);
                        s.Occ.Take(p, 0.9f, YardKit.KindStuff);
                    }
                }
                s.Occ.TakeCorridor(a, b, depth * 0.5f, YardKit.KindStuff);
                s.Anchors.Add((c, depth * 0.5f));
                banks++;
            }
            s.Banks += banks;
            return made;
        }

        // ───────────── scree ─────────────

        /// <summary>
        /// Stones spilled where the work is: round every heap, pile and pad (a few clumps on its rim), and along the dirt roads
        /// in twos and threes at the edge now and then.
        /// </summary>
        public static int Scree(IndustrialSite s, float amount, uint salt)
        {
            int made = 0, k = 0;
            amount = math.saturate(amount);
            foreach (var (p, r) in s.Anchors.ToArray())
            {
                int clumps = (int)math.round((1f + r / 4f) * amount);
                for (int i = 0; i < clumps; i++, k++)
                {
                    uint v = Hashing.Mix(salt, (uint)k);
                    float ang = Hashing.Unit(v) * 6.283f;
                    var q = p + new float2(math.cos(ang), math.sin(ang)) * (r + 0.6f + Hashing.Unit(Hashing.Mix(v, 1u)) * 1.8f);
                    if (!s.Inner.ContainsDisc(q, 1.5f)) continue;
                    made += YardFill.Clump(s, q, 1.2f + Hashing.Unit(Hashing.Mix(v, 2u)) * 0.8f, Stone, 3 + (int)(Hashing.Unit(Hashing.Mix(v, 3u)) * 5f), Hashing.Mix(v, 4u));
                }
            }
            s.Ctx.Plan.Facts["scree.anchors"] = s.Anchors.Count;
            s.Ctx.Plan.Facts["scree.round"] = made;
            // At the foot of things: wherever the ground is free but something stands within a few metres (between the
            // piles, at the wrecks' wheels, by the bays), stones in drifting patches (value noise), more with "amount".
            int gaps = 0;
            // (A 3 m sweep, 4 m on a big yard: the sweep is the slow part of the stones, 0.5 s on 20 ha at 2.4 m.)
            float step = s.Inner.Area > 60000f ? 4f : 3f;
            foreach (var p in Scatter.Candidates(s.Inner, step, Hashing.Mix(salt, 9u), 30000, s.Ctx.Cancel))
            {
                if (ValueNoise.Clumps(p, 16f, Hashing.Mix(salt, 10u)) > 0.18f + 0.42f * amount) continue;
                if (s.F(p).y < s.Spec.Front + 3f) continue;
                if (InBlock(s, p)) continue;
                float c = s.Occ.Clearance(p, 3.5f);
                if (c < 0.8f || c > 3.2f) continue;
                uint v = Hashing.Mix(salt, 0x5000u + (uint)gaps++);
                made += YardFill.Clump(s, p, math.min(c - 0.25f, 1.5f), Stone, 2 + (int)(Hashing.Unit(v) * 4f), Hashing.Mix(v, 1u));
            }
            s.Ctx.Plan.Facts["scree.gaps"] = made - s.Ctx.Plan.Facts["scree.round"];
            float hw = s.Spec.Aisle * 0.5f;
            foreach (var line in s.Tracks)
            {
                float next = 6f + Hashing.Unit(Hashing.Mix(salt, 0x7000u + (uint)line.Count)) * 8f, at = 0f;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var a = line[i]; var b = line[i + 1];
                    float len = math.distance(a, b);
                    if (len < 0.1f) continue;
                    var dir = (b - a) / len;
                    var nrm = new float2(-dir.y, dir.x);
                    for (; next <= at + len; next += 12f + Hashing.Unit(Hashing.Mix(salt, 0x7100u + (uint)k)) * 14f, k++)
                    {
                        uint v = Hashing.Mix(salt, 0x7200u + (uint)k);
                        if (Hashing.Unit(v) > 0.6f * amount + 0.1f) continue;
                        float side = (v & 1u) == 0 ? 1f : -1f;
                        var q = a + dir * (next - at) + nrm * side * (hw + 0.6f + Hashing.Unit(Hashing.Mix(v, 1u)) * 0.8f);
                        made += YardFill.Clump(s, q, 0.9f, Stone, 2 + (int)(Hashing.Unit(Hashing.Mix(v, 2u)) * 2f), Hashing.Mix(v, 3u));
                    }
                    at += len;
                }
            }
            s.Ctx.Plan.Facts["scree.roads"] = made - s.Ctx.Plan.Facts["scree.round"] - s.Ctx.Plan.Facts["scree.gaps"];
            s.Stones += made;
            return made;
        }

        // ───────────── rubble in the earth ─────────────

        private static readonly YardItem[] StrewnMix =
        {
            IndustrialAssets.Rubble, IndustrialAssets.Rubble, IndustrialAssets.Rubble, IndustrialAssets.MixedRubble, IndustrialAssets.MixedRubble, IndustrialAssets.WoodRubble,
        };

        /// <summary>
        /// Rubble worked into the ground: concrete, mixed and plank rubble heaps set 0.5-0.95 m into the earth, so only their
        /// tops show, bits of concrete and board strewn in the dirt, in drifting patches across the yard (more in the scrap
        /// field and round the pad, never on the roads, never into a cabin or a container). A sunk piece takes no room in the
        /// game (PlannedObject.Elevation &lt; 0), so it may lie under the wrecks and the heaps. <paramref name="amount"/> 0..1.
        /// </summary>
        public static int Strewn(IndustrialSite s, float amount, uint salt)
        {
            amount = math.saturate(amount);
            if (amount <= 0f) return 0;
            var ctx = s.Ctx;
            // What a sunk heap must not reach into: rooms (offices, booths, toilets) and containers, plants, bays.
            var solid = new DiscIndex(8f);
            foreach (var o in ctx.Plan.Objects)
            {
                var c = o.Category ?? "";
                if (c == AssetCategories.ScrapCabin || c == AssetCategories.ScrapContainer || c == AssetCategories.ConstructionToilet
                    || c == AssetCategories.ScrapMachine || c == AssetCategories.QuarryPlant || c == AssetCategories.LogisticsShed || c == AssetCategories.GardenShed)
                    solid.Add(o.Position.xz, 4f, 0);
            }
            var laid = new DiscIndex(6f);
            float hw = s.Spec.Aisle * 0.5f;
            int made = 0, k = 0;
            foreach (var p in Scatter.Candidates(s.Inner, 5.5f, Hashing.Mix(salt, 1u), 30000, ctx.Cancel))
            {
                if (s.F(p).y < s.Spec.Front + 4f) continue;
                if (ValueNoise.Clumps(p, 26f, Hashing.Mix(salt, 2u)) > 0.2f + 0.6f * amount) continue;
                uint v = Hashing.Mix(salt, 0x100u + (uint)k++);
                var item = StrewnMix[(int)(Hashing.Mix(v, 1u) % (uint)StrewnMix.Length)];
                float r = item.Radius * 0.7f;
                if (!s.Inner.ContainsDisc(p, r) || solid.Overlaps(p, r, 0.5f) || laid.Overlaps(p, r * 0.8f, 0f)) continue;
                bool road = false;
                foreach (var (a, b) in s.Aisles) if (SegmentIndex.DistSq(p, a, b) < (hw + r * 0.6f) * (hw + r * 0.6f)) { road = true; break; }
                if (road || !s.ReservedOnly.Free(p, r, 0f, 0f)) continue;
                var o2 = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, item.Pick(v), Hashing.Mix(v, 3u), -1, item.Category);
                o2.Elevation = -(0.5f + Hashing.Unit(Hashing.Mix(v, 4u)) * 0.45f);
                laid.Add(p, r, 0);
                made++;
            }
            s.Strewn += made;
            return made;
        }

        // ───────────── crushed concrete ─────────────

        /// <summary>
        /// Crushed concrete by the work pad: two to four grey rubble heaps in an arc on the pad's far side, a boulder at the end
        /// of the arc (the crusher's feed and its product).
        /// </summary>
        public static int Crushed(IndustrialSite s, uint salt)
        {
            if (s.PadR <= 0f) return 0;
            var c = s.PadAt;
            var face = math.normalizesafe(s.Inner.Centroid - c, s.V);
            int n = 2 + (int)(Hashing.Unit(salt) * 3f), made = 0;
            float r = s.PadR + IndustrialAssets.Rubble.Radius + 0.6f;
            for (int i = 0; i <= n; i++)
            {
                uint v = Hashing.Mix(salt, (uint)i + 1u);
                float ang = 3.14f + (i - n * 0.5f) * 0.62f + (Hashing.Unit(v) - 0.5f) * 0.2f;
                var dir = YardFill.Turn(face, ang);
                var item = i == n ? Boulder : IndustrialAssets.Rubble;
                var p = c + dir * (i == n ? r - 1.5f : r);
                if (s.Put(item, item.Pick(v), p, YardFill.Turn(dir, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f), Hashing.Mix(v, 2u), 0.05f))
                {
                    Sink(s, item.Category == AssetCategories.Rock ? 0.6f : 0.1f + Hashing.Unit(Hashing.Mix(v, 3u)) * 0.25f);
                    made++;
                    if (item.Category != AssetCategories.Rock) s.Anchors.Add((p, IndustrialAssets.Rubble.Radius));
                }
            }
            return made;
        }

        // ───────────── the burn barrel ─────────────

        /// <summary>
        /// A burn barrel where the men stand: on free ground near the office (the front yard, off the gate road), two or three
        /// pallets round it to sit on.
        /// </summary>
        public static int BurnBarrel(IndustrialSite s, uint salt)
        {
            float fz = s.Spec.FrontZone, front = s.Spec.Front;
            for (int i = 0; i < 40; i++)
            {
                uint v = Hashing.Mix(salt, (uint)i);
                float side = s.OfficeSide;
                var p = s.W(side * (fz + 4f + Hashing.Unit(v) * 9f), 3f + Hashing.Unit(Hashing.Mix(v, 1u)) * (front - 2f));
                // Room for the ring of seats with a margin (an office container's claim is its box, its mesh reaches a little past).
                if (!s.Inner.ContainsDisc(p, 3.5f) || !s.Occ.Free(p, 3.6f, 0f, 0.4f)) continue;
                var bb = FireBarrel;
                if (!s.Put(bb, bb.Pick(v), p, s.V, v, 0.05f)) continue;
                int made = 1;
                int seats = 2 + (int)(Hashing.Unit(Hashing.Mix(v, 2u)) * 2f);
                for (int j = 0; j < seats; j++)
                {
                    float ang = Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f + j * 6.283f / seats + (Hashing.Unit(Hashing.Mix(v, 4u + (uint)j)) - 0.5f) * 0.6f;
                    var dir = new float2(math.cos(ang), math.sin(ang));
                    var item = IndustrialAssets.Pallets;
                    if (s.Put(item, item.Pick(Hashing.Mix(v, 8u + (uint)j)), p + dir * 1.7f, YardFill.Turn(dir, 1.57f), Hashing.Mix(v, 12u + (uint)j), 0.3f)) made++;
                }
                return made;
            }
            return 0;
        }
    }
}

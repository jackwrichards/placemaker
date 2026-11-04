// Industrial category: how a block, the front yard and the edge are filled. Every filler lays its props on an exact grid
// (equal pitch, centred in the block, one kind per row or bay) and fills a block from the spine outward, so a part-full yard
// reads as a yard that is filling up, not as holes. Variety comes from choosing per row / per bay, never per prop.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public static class YardFill
    {
        /// <summary>How many of n slots to use for a fill share (at least one).</summary>
        private static int Take(int n, float fill) => n <= 0 ? 0 : math.clamp((int)math.round(n * math.saturate(fill)), 1, n);

        /// <summary>Slot positions along U, centred in the block, ordered from the spine outward.</summary>
        private static List<float> Slots(YardBlock b, float pitch, float size, float endMargin)
        {
            var list = new List<float>();
            float usable = b.Length - 2f * endMargin;
            int n = (int)math.floor((usable - size) / pitch) + 1;
            if (usable < size || n <= 0) return list;
            float start = b.UMid - (n - 1) * pitch * 0.5f;
            for (int i = 0; i < n; i++) list.Add(start + i * pitch);
            list.Sort((x, y) => math.abs(x).CompareTo(math.abs(y)));
            return list;
        }

        // ───────────── rows ─────────────

        /// <summary>
        /// How untidy a row is (the user, 2026-10-19: "it looks like someone laid out all the elements like a mathematician").
        /// The row stays a row; each prop is a little off it, turned a little, some are missing in clumps (never single random
        /// holes), some carry more of the same on top, a few lie on their roof or their side.
        /// </summary>
        public struct Mess
        {
            /// <summary>Sideways and along-the-row wander (m).</summary>
            public float Pos;
            /// <summary>Turn (rad, ±).</summary>
            public float Yaw;
            /// <summary>Share of the row's slots left empty, in clumps.</summary>
            public float Missing;
            /// <summary>Chance a slot carries 1..StackMax more on top (with their own small turn and shift).</summary>
            public float Stack;
            public int StackMax;
            /// <summary>Chance a ground prop lies on its roof / on its side (cars).</summary>
            public float Flip, Side;
            /// <summary>The prop's height (m): what a stacked one stands on, how high a flipped one's wheels are.</summary>
            public float Height;

            public static Mess Tidy => new Mess { Pos = 0.08f, Yaw = 0.015f, Missing = 0.03f };
            public static Mess Containers => new Mess { Pos = 0.15f, Yaw = 0.03f, Missing = 0.12f, Height = TierHeight };
            public static Mess Goods => new Mess { Pos = 0.2f, Yaw = 0.2f, Missing = 0.15f };
            // Cars: the game hides one of two props whose boxes meet, so a wreck may wander and turn only as far as its row
            // leaves room (pitch = width + gap + 2 × Pos); a car on another clears it by a hand (Height).
            // Height: the game's cars stand 1.4–2.0 m (VehiclePropSystem log, 2026-10-19), so a car on a car rests 1.3 m up: always
            // on (pressed a little into) the roof below, never floating over it.
            public static Mess Wrecks(float stack) => new Mess { Pos = 0.25f, Yaw = 0.12f, Missing = 0.1f, Stack = stack, StackMax = 2, Flip = 0.07f, Side = 0.04f, Height = 1.3f };
        }

        /// <summary>Container height plus a hair, so a box stands on the one below without their boxes meeting.</summary>
        public const float TierHeight = 2.62f;

        public static float2 Turn(float2 d, float a) => new float2(d.x * math.cos(a) - d.y * math.sin(a), d.x * math.sin(a) + d.y * math.cos(a));

        /// <summary>
        /// The colour seed of the i-th box of a row: runs of three to six boxes of one shipping line, two lines a row, now and
        /// then an odd box (the game picks a box's colour from its seed). -1 for anything but containers.
        /// </summary>
        private static int RowColour(YardItem item, uint rowPick, int i, uint v)
        {
            if (item.Category != AssetCategories.ScrapContainer) return -1;
            int run = 3 + (int)(rowPick % 4u);
            if (Hashing.Unit(Hashing.Mix(v, 0xC0Du)) < 0.1f) return (int)(Hashing.Mix(v, 0xC0Eu) & 0x7FFFu);
            return (int)(Hashing.Mix(rowPick, (i / run & 1) == 0 ? 0xC01u : 0xC02u) & 0x7FFFu);
        }

        /// <summary>Containers: one box kind per run of the row (the colour run, RowColour); anything else its usual pick.</summary>
        private static AssetRef RunAsset(YardItem item, uint pick, uint rowPick, int i)
        {
            if (item.Category != AssetCategories.ScrapContainer) return item.Pick(pick);
            int run = 3 + (int)(rowPick % 4u);
            return item.Pick(Hashing.Mix(rowPick, (uint)(i / run)));
        }

        /// <summary>One prop with a mess applied (wander, pile, tip), for fillers outside this class.</summary>
        public static int PlaceOne(IndustrialSite s, YardItem item, AssetRef asset, float2 c, float2 face, uint v, Mess m) => Place(s, item, asset, c, face, v, m, 1);

        /// <summary>Is slot p left empty (clumps of value noise, so gaps come in runs like a yard that is emptying)?</summary>
        private static bool Gap(float2 p, float share, uint seed) => share > 0f && ValueNoise.Clumps(p, 9f, seed) < share;

        /// <summary>
        /// One prop of a row with the mess applied: placed (checked) on the ground, maybe flipped, maybe with more on top.
        /// Returns how many props it made.
        /// </summary>
        private static int Place(IndustrialSite s, YardItem item, AssetRef asset, float2 c, float2 face, uint v, Mess m, int tiers, int colour = -1)
        {
            var dir = m.Yaw > 0f ? Turn(face, (Hashing.Unit(Hashing.Mix(v, 9u)) - 0.5f) * 2f * m.Yaw) : face;
            if (m.Pos > 0f) c += new float2(Hashing.Unit(Hashing.Mix(v, 10u)) - 0.5f, Hashing.Unit(Hashing.Mix(v, 11u)) - 0.5f) * 2f * m.Pos;
            if (!s.Put(item, asset, c, dir, v, 0.02f)) return 0;
            int n = 1;
            var objs = s.Ctx.Plan.Objects;
            var ground = objs[objs.Count - 1];
            ground.ColorSeed = colour;
            float roll = Hashing.Unit(Hashing.Mix(v, 12u));
            bool flipped = false;
            if (m.Flip > 0f && roll < m.Flip) { ground.Roll = math.PI; ground.Elevation = m.Height; flipped = true; }
            else if (m.Side > 0f && roll < m.Flip + m.Side) { ground.Roll = (v & 1u) == 0 ? math.PI * 0.5f : -math.PI * 0.5f; ground.Elevation = item.X * 0.5f; flipped = true; }
            // More on top: a column of boxes (tiers), or a pile of one or two more (Stack).
            int extra = tiers > 1 ? tiers - 1 : 0;
            if (extra == 0 && !flipped && m.Stack > 0f && Hashing.Unit(Hashing.Mix(v, 13u)) < m.Stack)
                extra = 1 + (int)(Hashing.Unit(Hashing.Mix(v, 14u)) * math.max(1, m.StackMax));
            float h = m.Height > 0f ? m.Height : TierHeight;
            for (int t = 1; t <= extra && !flipped; t++)
            {
                uint tv = Hashing.Mix(v, 0x7E00u + (uint)t);
                var d = Turn(dir, (Hashing.Unit(tv) - 0.5f) * (tiers > 1 ? 0.03f : 0.5f));
                if (tiers > 1 && (tv & 3u) == 0) d = -d;   // the odd box turned end for end
                var off = tiers > 1 ? float2.zero : new float2(Hashing.Unit(Hashing.Mix(tv, 1u)) - 0.5f, Hashing.Unit(Hashing.Mix(tv, 2u)) - 0.5f) * 0.3f;
                if (!s.ReservedOnly.Free(c + off, math.max(item.X, item.Z) * 0.5f, 0f, 0.3f)) break;   // never over a path another layer keeps
                var o = s.Ctx.Object(c + off - d * item.CZ, math.atan2(d.x, d.y), ObjectRole.Furniture, colour >= 0 && (tv & 3u) != 0 ? asset : item.Pick(tv), tv, -1, item.Category);
                o.Elevation = t * h;
                if (colour >= 0) o.ColorSeed = (tv & 3u) == 0 ? colour + 7 : colour;
                // The top car of a pile sometimes lies on its roof.
                if (tiers <= 1 && t == extra && m.Flip > 0f && Hashing.Unit(Hashing.Mix(tv, 3u)) < 0.3f) { o.Roll = math.PI; o.Elevation = (t + 1) * h; }
                s.Items++;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Props side by side across the block (long side across, nose to the aisle): two rows back to back when the block has
        /// aisles on both sides and room, else one row facing its aisle. One kind per row unless <paramref name="perSlot"/>.
        /// <paramref name="tiers"/> stacks each column (containers), the outer columns and a few others one lower.
        /// </summary>
        public static int RowsAcross(IndustrialSite s, YardBlock b, YardItem item, float gap, float fill, uint salt,
            Mess mess = default, bool perSlot = false, float endMargin = 0.5f, int tiers = 1)
        {
            int placed = 0;
            var rows = new List<(float v, float2 face)>();
            bool two = b.Depth >= 2f * item.Z + 0.4f;
            float inset = 0.1f + mess.Pos;
            if (two && b.AisleFront && b.AisleBack)
            {
                rows.Add((b.V0 + item.Z * 0.5f + inset, -s.V));
                rows.Add((b.V1 - item.Z * 0.5f - inset, s.V));
            }
            else if (two)
            {
                // One aisle: both rows face it (the second row stands behind the first).
                var face = b.AisleFront ? -s.V : s.V;
                rows.Add((b.V0 + item.Z * 0.5f + inset, face));
                rows.Add((b.V1 - item.Z * 0.5f - inset, face));
            }
            else rows.Add((b.AisleFront ? b.V0 + item.Z * 0.5f + inset : b.V1 - item.Z * 0.5f - inset, b.AisleFront ? -s.V : s.V));
            var slots = Slots(b, item.X + gap + mess.Pos, item.X, endMargin + mess.Pos);
            int use = Take(slots.Count, fill);
            for (int r = 0; r < rows.Count; r++)
            {
                uint rowPick = Hashing.Mix(salt, (uint)(b.Index * 7 + r));
                for (int i = 0; i < use; i++)
                {
                    uint v = Hashing.Mix(rowPick, (uint)i);
                    var c = s.W(slots[i], rows[r].v);
                    if (Gap(c, mess.Missing, salt)) continue;
                    placed += Place(s, item, RunAsset(item, perSlot ? v : rowPick, rowPick, i), c, rows[r].face, v, mess, Tiers(tiers, use, i, v), RowColour(item, rowPick, i, v));
                }
            }
            return placed;
        }

        /// <summary>Tiers of the i-th of n columns (filled from the spine): the outer two step down and one in three of the rest
        /// is a box lower, so the skyline is uneven the way a working yard's is.</summary>
        private static int Tiers(int tiers, int n, int i, uint v)
        {
            if (tiers <= 1) return 1;
            int fromEnd = n - 1 - i;
            int h = fromEnd == 0 ? tiers - 2 : fromEnd == 1 ? tiers - 1 : tiers;
            if (fromEnd > 1 && Hashing.Unit(Hashing.Mix(v, 0x71E5u)) < 0.33f) h--;
            return math.max(1, h);
        }

        /// <summary>Props end to end along the block in lines across its depth (long side along the aisle).</summary>
        public static int RowsAlong(IndustrialSite s, YardBlock b, YardItem item, float gapAlong, float gapAcross, float fill, uint salt,
            Mess mess = default, int maxLines = 99, bool perSlot = false, int tiers = 1)
        {
            int placed = 0;
            int lines = math.min(maxLines, (int)math.floor((b.Depth - 0.4f + gapAcross) / (item.X + gapAcross)));
            if (lines <= 0) return 0;
            float pitchV = item.X + gapAcross;
            // Lines start at the aisle side.
            bool fromFront = b.AisleFront || !b.AisleBack;
            var slots = Slots(b, item.Z + gapAlong + mess.Pos, item.Z, 0.4f + mess.Pos);
            int use = Take(slots.Count, fill);
            for (int l = 0; l < lines; l++)
            {
                float v = fromFront ? b.V0 + 0.2f + item.X * 0.5f + l * pitchV : b.V1 - 0.2f - item.X * 0.5f - l * pitchV;
                uint linePick = Hashing.Mix(salt, (uint)(b.Index * 13 + l));
                var dir = (l & 1) == 0 ? s.U : -s.U;
                for (int i = 0; i < use; i++)
                {
                    uint v2 = Hashing.Mix(linePick, (uint)i);
                    var c = s.W(slots[i], v);
                    if (Gap(c, mess.Missing, salt)) continue;
                    placed += Place(s, item, RunAsset(item, perSlot ? v2 : linePick, linePick, i), c, dir, v2, mess, Tiers(tiers, use, i, v2), RowColour(item, linePick, i, v2));
                }
            }
            return placed;
        }

        /// <summary>A short pile of the same thing tipped together (tyres, pallets, drums): loose, overlapping a little.</summary>
        public static int Clump(IndustrialSite s, float2 c, float r, YardItem item, int count, uint salt)
        {
            int n = 0;
            const float golden = 2.39996323f;
            for (int i = 0; i < count * 2 && n < count; i++)
            {
                float t = math.sqrt((i + 0.5f) / (count * 1.2f));
                uint v = Hashing.Mix(salt, (uint)i);
                var p = c + new float2(math.cos(i * golden), math.sin(i * golden)) * r * t;
                float a = Hashing.Unit(v) * 6.283f;
                // Touching, never overlapping: the game hides one of two props whose boxes meet.
                if (s.Occ.Free(p, item.Radius * 0.92f, 0f, 0.02f) && s.Put(item, item.Pick(v), p, new float2(math.sin(a), math.cos(a)), v, 0.02f)) n++;
            }
            return n;
        }

        // ───────────── stockpiles ─────────────

        private static readonly YardItem Boulder = new YardItem(AssetCategories.Rock, 5f, 4.5f, "Boulder01", "Boulder03", "Boulder05", "Boulder07", "Boulder02", "Boulder06");
        private static readonly YardItem Stone = new YardItem(AssetCategories.Rock, 1f, 0.9f, "Rock01", "Rock03", "Rock06", "Rock09", "Rock11", "Rock14", "Rock20", "Rock26");

        /// <summary>
        /// Stockpiles of loose material along the block: real terrain cones painted sand, dirt or dark spoil (the game has no
        /// standalone sand or gravel heap that renders), a rock pile of boulders with stones at their feet, each cone a
        /// different size and not quite in line, a loader parked at one of them.
        /// </summary>
        public static int Stockpiles(IndustrialSite s, YardBlock b, uint salt, bool rocks = true)
        {
            var ctx = s.Ctx;
            float maxR = math.min(b.Depth * 0.5f - 0.6f, 8f);
            if (maxR < 3.5f || b.Length < 10f) return 0;
            int made = 0;
            float u = b.U0 + 1f;
            int i = 0;
            while (u < b.U1 - 3f)
            {
                uint v = Hashing.Mix(salt, (uint)(b.Index * 31 + i));
                float r = math.lerp(maxR * 0.6f, maxR, Hashing.Unit(v));
                if (u + 2f * r > b.U1 - 0.5f) r = (b.U1 - 0.5f - u) * 0.5f;
                if (r < 3f) break;
                float vOff = (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * math.max(0f, b.Depth - 2f * r - 1f);
                var c = s.W(u + r, b.VMid + vOff);
                int kind = rocks && i % 4 == 3 ? 3 : i % 3;   // 0 sand, 1 dirt, 2 dark spoil, 3 rocks
                if (kind == 3)
                {
                    // A rock pile: two or three boulders leaning together, stones round their feet.
                    int bn = 2 + (int)(Hashing.Unit(Hashing.Mix(v, 2u)) * 2f);
                    for (int k = 0; k < bn; k++)
                    {
                        float a = (k + Hashing.Unit(Hashing.Mix(v, 3u + (uint)k))) / bn * 6.283f;
                        var p = c + new float2(math.cos(a), math.sin(a)) * r * 0.35f;
                        if (s.Put(Boulder, Boulder.Pick(Hashing.Mix(v, 5u + (uint)k)), p, new float2(math.sin(a * 3f), math.cos(a * 3f)), Hashing.Mix(v, 6u + (uint)k), -0.6f)) made++;
                    }
                    made += Clump(s, c, r, Stone, 10, Hashing.Mix(v, 9u));
                    TerrainPaint.Field(ctx, LeisureKit.Blob(c, r + 1f, Hashing.Mix(v, 10u), 14, 0.3f), YardGround.Dirt, 1f, 2f, 1.5f, null, 0f, 0f, 0f, Hashing.Mix(v, 11u));
                }
                else
                {
                    // A cone of the material itself: terrain raised and painted, a soft heap from any angle.
                    float h = math.clamp(r * 0.5f, 1.6f, 3.6f);
                    s.Mounds.Add(new Mound { Center = c, Radius = r, Height = h, Flat = 0.08f });
                    s.Occ.Take(c, r * 0.8f, Scrapyard.YardKit.KindStuff);
                    int layer = kind == 0 ? YardGround.Gravel : kind == 1 ? YardGround.Dirt : YardGround.Mud;
                    TerrainPaint.Field(ctx, LeisureKit.Blob(c, r + 0.8f, Hashing.Mix(v, 12u), 14, 0.25f), layer, 1f, 1.6f, 1.2f, null, 0f, 0f, 0f, Hashing.Mix(v, 13u));
                    // A few stones rolled off the foot of the dirt and spoil heaps.
                    if (kind != 0) made += Clump(s, c + Turn(s.U, Hashing.Unit(v) * 6.283f) * (r * 0.9f), 1.5f, Stone, 3, Hashing.Mix(v, 14u));
                }
                s.Anchors.Add((c, r));
                s.Heaps++;
                made++;
                u += 2f * r + 1.5f + Hashing.Unit(Hashing.Mix(v, 15u)) * 2.5f;
                i++;
            }
            // The loader working the first heap, nose to it, a little askew.
            if (made > 0)
            {
                var loader = IndustrialAssets.Loader;
                float lv = b.AisleFront ? b.V0 + loader.X * 0.5f + 0.3f : b.V1 - loader.X * 0.5f - 0.3f;
                var lp = s.W(b.U0 + maxR * 0.8f, lv);
                if (s.Put(loader, loader.Pick(salt), lp, Turn(b.AisleFront ? s.V : -s.V, (Hashing.Unit(salt) - 0.5f) * 0.6f), Hashing.Mix(salt, 77u), 0.05f)) s.Machines++;
            }
            return made;
        }

        // ───────────── bays ─────────────

        /// <summary>
        /// Material bays along the back of the block: concrete-block walls (back and sides, open to the aisle), each bay holding
        /// one material. <paramref name="fillBay"/>(bay index, centre, face toward the aisle, inner width, inner depth) fills it.
        /// </summary>
        public static int Bays(IndustrialSite s, YardBlock b, float bayWidth, float fill, uint salt,
            System.Action<int, float2, float2, float, float> fillBay)
        {
            // The bays open to the block's aisle (the front one when it has both).
            bool openFront = b.AisleFront;
            float depth = math.min(b.Depth - 0.4f, 14f);
            const float wall = 0.65f;
            int n = (int)math.floor((b.Length - 1f - wall) / (bayWidth + wall));
            if (n <= 0 || depth < 5f) return 0;
            int use = Take(n, fill);
            float total = n * (bayWidth + wall) + wall;
            float u0 = b.UMid - total * 0.5f;
            // Nothing behind the block (the back fence): the bays lean on it, with a paved apron in front of their mouths.
            bool againstBack = openFront ? !b.AisleBack : !b.AisleFront;
            float vBack = openFront ? (againstBack ? b.V1 - 0.1f : b.V0 + depth) : (againstBack ? b.V0 + 0.1f : b.V1 - depth);
            float sign = openFront ? -1f : 1f;   // from the back wall toward the opening, in V
            var face = openFront ? -s.V : s.V;
            // Fill from the spine side: pick the bays nearest it.
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((x, y) => math.abs(u0 + wall + (x + 0.5f) * (bayWidth + wall)).CompareTo(math.abs(u0 + wall + (y + 0.5f) * (bayWidth + wall))));
            var used = new HashSet<int>(order.GetRange(0, use));
            int lo = n, hi = -1;
            foreach (var i in used) { lo = math.min(lo, i); hi = math.max(hi, i); }
            // Back wall over the used bays; side walls between them, 3/4 deep.
            float uA = u0 + lo * (bayWidth + wall), uB = u0 + (hi + 1) * (bayWidth + wall) + wall;
            float vw = vBack + (openFront ? -wall * 0.5f : wall * 0.5f);
            s.BlockWall(s.W(uA, vw), s.W(uB, vw), Hashing.Mix(salt, 0xBAC0u));
            for (int i = lo; i <= hi + 1; i++)
            {
                float uw = u0 + i * (bayWidth + wall) + wall * 0.5f;
                float vEnd = vBack + sign * depth * 0.75f;
                s.BlockWall(s.W(uw, vw + sign * wall * 0.5f), s.W(uw, vEnd), Hashing.Mix(salt, 0xBAC1u + (uint)i));
            }
            int made = 0;
            foreach (var i in used)
            {
                float uc = u0 + wall + (i + 0.5f) * (bayWidth + wall) - wall * 0.5f;
                float innerDepth = depth - wall;
                var c = s.W(uc, vBack + sign * (wall + innerDepth * 0.5f));
                fillBay?.Invoke(i, c, face, bayWidth - 0.4f, innerDepth - 0.4f);
                s.Anchors.Add((c, bayWidth * 0.5f));
                made++;
            }
            s.Bays += made;
            return made;
        }

        /// <summary>A bay (or any rectangle) filled with a grid of small goods of one kind, from the back wall forward.</summary>
        public static int Grid(IndustrialSite s, float2 c, float2 face, float width, float depth, YardItem item, float gap, float fill, uint salt,
            bool perSlot = false)
        {
            var side = new float2(face.y, -face.x);
            int nx = (int)math.floor((width + gap) / (item.X + gap));
            int nz = (int)math.floor((depth + gap) / (item.Z + gap));
            if (nx <= 0 || nz <= 0) return 0;
            int rows = math.max(1, (int)math.ceil(nz * math.saturate(fill)));
            int placed = 0;
            uint pick = Hashing.Mix(salt, 0x6A1Du);
            for (int r = 0; r < rows; r++)
            {
                // r = 0 is against the back wall.
                float z = -depth * 0.5f + item.Z * 0.5f + r * (item.Z + gap);
                for (int k = 0; k < nx; k++)
                {
                    float x = (k - (nx - 1) * 0.5f) * (item.X + gap);
                    uint v = Hashing.Mix(salt, (uint)(r * 97 + k));
                    var p = c - face * z + side * x;
                    if (Gap(p, Mess.Goods.Missing, salt)) continue;
                    placed += Place(s, item, item.Pick(perSlot ? v : pick), p, face, v, Mess.Goods, 1);
                }
            }
            return placed;
        }

        /// <summary>One big heap centred in a bay or block, its long side across the bay when it fits better.</summary>
        public static bool Heap(IndustrialSite s, float2 c, float2 face, float width, float depth, YardItem item, uint salt)
        {
            float2 dir = face;
            if (item.Z > depth && item.X <= depth && item.Z <= width) dir = new float2(face.y, -face.x);
            else if (item.Z > depth || item.X > width)
            {
                // Too big for the bay: the next smaller metal heap.
                if (item.Category != IndustrialAssets.MetalHeap.Category) return false;
                item = IndustrialAssets.MetalHeap;
                if (item.Z > depth || item.X > width) return false;
            }
            if (!s.Put(item, item.Pick(salt), c, dir, salt, -0.2f)) return false;
            s.Heaps++;
            return true;
        }

        /// <summary>
        /// A block heaped full (the junk yard's stock, seen too sparse in the game): loads laid edge to edge over the whole block,
        /// each turned its own quarter, the materials in clumps (rusty metal next to rusty metal, rubble with rubble), on a
        /// gentle terrain swell so the block rises in the middle; the fill share leaves the outer end bare.
        /// </summary>
        public static int Carpet(IndustrialSite s, YardBlock b, YardItem[] kinds, float fill, uint salt)
        {
            float cell = 0f;
            foreach (var k in kinds) cell = math.max(cell, math.max(k.X, k.Z));
            cell += 0.2f;
            int nu = (int)math.floor((b.Length - 0.6f) / cell), nv = (int)math.floor((b.Depth - 0.4f) / (cell * 0.87f));
            if (nu < 1 || nv < 1) return 0;
            float u0 = b.UMid - (nu - 1) * cell * 0.5f, v0 = b.VMid - (nv - 1) * cell * 0.87f * 0.5f;
            int use = Take(nu, fill);
            // Columns nearest the spine first.
            var cols = new List<int>();
            for (int i = 0; i < nu; i++) cols.Add(i);
            cols.Sort((x, y) => math.abs(u0 + x * cell).CompareTo(math.abs(u0 + y * cell)));
            int made = 0;
            uint noise = Hashing.Mix(salt, 0xCA9Eu);
            for (int ci = 0; ci < use; ci++)
            {
                int i = cols[ci];
                for (int k = 0; k < nv; k++)
                {
                    float u = u0 + i * cell + ((k & 1) == 0 ? 0f : cell * 0.25f);
                    if (u + cell * 0.5f > b.U1 + 0.1f) u -= cell * 0.5f;
                    var p = s.W(u, v0 + k * cell * 0.87f);
                    uint v = Hashing.Mix(salt, (uint)(b.Index * 977 + i * 31 + k));
                    // Clumped materials: the same kind over a few cells.
                    var item = kinds[(int)(ValueNoise.Clumps(p, 14f, noise) * kinds.Length * 0.999f)];
                    var dir = Turn(s.U, (int)(Hashing.Unit(v) * 4f) * math.PI * 0.5f + (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.06f);
                    if (s.Put(item, item.Pick(v), p, dir, v, 0.02f)) made++;
                }
            }
            if (made == 0) return 0;
            s.Heaps += made;
            var c = s.W(b.UMid, b.VMid);
            s.Anchors.Add((c, math.min(b.Length, b.Depth) * 0.4f));
            float mr = math.min(b.Length, b.Depth) * 0.5f - 0.6f;
            if (mr > 4f) s.Mounds.Add(new Mound { Center = c, Radius = mr, Height = math.min(2.2f, mr * 0.25f), Flat = 0.35f, Axis = s.U, Ratio = math.max(1f, b.Length / math.max(1f, b.Depth)) });
            return made;
        }

        /// <summary>One ingredient of a jumble: the prop and its share.</summary>
        public struct Part
        {
            public YardItem Item;
            public float Share;
            public Part(YardItem item, float share) { Item = item; Share = share; }
        }

        /// <summary>Scrap metal: wrecks are the yard's metal (the game has no scrap prop), tipped among rubble, drums and crates.</summary>
        public static readonly Part[] ScrapMix =
        {
            new Part(IndustrialAssets.Car, 0.34f), new Part(IndustrialAssets.MixedRubble, 0.14f), new Part(IndustrialAssets.Trash, 0.14f),
            new Part(IndustrialAssets.Drums, 0.12f), new Part(IndustrialAssets.Crates, 0.08f), new Part(IndustrialAssets.WoodRubble, 0.08f),
            new Part(IndustrialAssets.Tyres, 0.06f), new Part(IndustrialAssets.Reels, 0.04f),
        };
        public static readonly Part[] CarsMix =
        {
            new Part(IndustrialAssets.Car, 0.8f), new Part(IndustrialAssets.Van, 0.06f), new Part(IndustrialAssets.Drums, 0.05f),
            new Part(IndustrialAssets.Tyres, 0.04f), new Part(IndustrialAssets.Crates, 0.03f), new Part(IndustrialAssets.Gas, 0.02f),
        };
        public static readonly Part[] PartsMix =
        {
            new Part(IndustrialAssets.Car, 0.45f), new Part(IndustrialAssets.Crates, 0.14f), new Part(IndustrialAssets.Drums, 0.12f),
            new Part(IndustrialAssets.Pallets, 0.1f), new Part(IndustrialAssets.Gas, 0.06f), new Part(IndustrialAssets.Reels, 0.05f),
            new Part(IndustrialAssets.Tyres, 0.08f),
        };
        public static readonly Part[] MetalMix =
        {
            new Part(IndustrialAssets.MetalHeap, 0.4f), new Part(IndustrialAssets.MixedRubble, 0.2f), new Part(IndustrialAssets.TruckLoad, 0.15f),
            new Part(IndustrialAssets.Car, 0.15f), new Part(IndustrialAssets.Drums, 0.05f), new Part(IndustrialAssets.Reels, 0.05f),
        };
        public static readonly Part[] TrashMix =
        {
            new Part(IndustrialAssets.Trash, 0.15f), new Part(IndustrialAssets.Drums, 0.2f), new Part(IndustrialAssets.Crates, 0.12f),
            new Part(IndustrialAssets.Pallets, 0.12f), new Part(IndustrialAssets.Boxes, 0.1f), new Part(IndustrialAssets.WoodRubble, 0.1f),
            new Part(IndustrialAssets.Car, 0.06f),
        };
        public static readonly Part[] RubbleMix =
        {
            new Part(IndustrialAssets.Rubble, 0.3f), new Part(IndustrialAssets.MixedRubble, 0.3f), new Part(IndustrialAssets.WoodRubble, 0.2f),
            new Part(IndustrialAssets.Trash, 0.1f), new Part(IndustrialAssets.Pallets, 0.1f),
        };
        public static readonly Part[] CrushedMix =
        {
            new Part(IndustrialAssets.Car, 0.7f), new Part(IndustrialAssets.MixedRubble, 0.15f), new Part(IndustrialAssets.Drums, 0.15f),
        };

        /// <summary>
        /// A block packed with scrap the way a working yard tips it (the game showed rows of tidy mounds read as a toy): every
        /// few metres something, picked from the mix with its share, turned any way, touching its neighbours but never inside
        /// them; wrecks among it pile up two or three high and some lie on their roofs. A swell of terrain lifts the middle.
        /// The fill share leaves the outer end of the block bare.
        /// </summary>
        public static int Jumble(IndustrialSite s, YardBlock b, Part[] mix, float fill, uint salt, float swell = 1.6f)
        {
            int made = 0;
            float total = 0f;
            foreach (var p in mix) total += p.Share;
            float uFar = b.UMid >= 0f ? b.U0 + (b.Length - 0.6f) * math.saturate(fill) + 0.3f : b.U1 - (b.Length - 0.6f) * math.saturate(fill) - 0.3f;
            var wreck = Mess.Wrecks(0.45f);
            wreck.Flip = 0.18f; wreck.Side = 0.08f; wreck.Pos = 0f; wreck.Yaw = 0f;
            int i = 0;
            for (float v = b.V0 + 1.2f; v < b.V1 - 0.8f; v += 2.4f)
                for (float u = b.U0 + 1.2f + ((int)(v * 10f) % 2) * 1.1f; u < b.U1 - 0.8f; u += 2.6f, i++)
                {
                    if (b.UMid >= 0f ? u > uFar : u < uFar) continue;
                    uint h = Hashing.Mix(salt, (uint)(b.Index * 7919 + i));
                    var c = s.W(u + (Hashing.Unit(h) - 0.5f) * 1.6f, v + (Hashing.Unit(Hashing.Mix(h, 1u)) - 0.5f) * 1.6f);
                    float pick = Hashing.Unit(Hashing.Mix(h, 2u)) * total;
                    var item = mix[mix.Length - 1].Item;
                    foreach (var p in mix) { if (pick < p.Share) { item = p.Item; break; } pick -= p.Share; }
                    var dir = Turn(s.U, Hashing.Unit(Hashing.Mix(h, 3u)) * 6.283f);
                    if (!s.Occ.Free(c, math.min(item.X, item.Z) * 0.5f, 0f, 0.05f)) continue;
                    made += item.Category == AssetCategories.ScrapWreck ? Place(s, item, item.Pick(h), c, dir, h, wreck, 1) : s.Put(item, item.Pick(h), c, dir, h, 0.03f) ? 1 : 0;
                }
            if (made == 0) return 0;
            s.Heaps++;
            var mid = s.W(b.UMid, b.VMid);
            s.Anchors.Add((mid, math.min(b.Length, b.Depth) * 0.4f));
            float mr = math.min(b.Length, b.Depth) * 0.5f - 0.6f;
            if (swell > 0f && mr > 4f) s.Mounds.Add(new Mound { Center = mid, Radius = mr, Height = math.min(swell, mr * 0.3f), Flat = 0.3f, Axis = s.U, Ratio = math.max(1f, b.Length / math.max(1f, b.Depth)) });
            return made;
        }

        // ───────────── heaps, carports, plant ─────────────

        /// <summary>Big heaps of one material side by side along the block, evenly spaced.</summary>
        public static int HeapRow(IndustrialSite s, YardBlock b, YardItem item, float gap, float fill, uint salt)
        {
            // Long side across the block when it fits, else along it.
            bool across = item.Z <= b.Depth - 0.6f;
            float along = across ? item.X : item.Z, deep = across ? item.Z : item.X;
            if (deep > b.Depth - 0.4f) return 0;
            var slots = Slots(b, along + gap, along, 0.8f);
            int use = Take(slots.Count, fill);
            int made = 0;
            float v = b.AisleFront ? b.V1 - deep * 0.5f - 0.3f : b.V0 + deep * 0.5f + 0.3f;
            for (int i = 0; i < use; i++)
            {
                uint p = Hashing.Mix(salt, (uint)(b.Index * 11 + i));
                // Heaps are tipped, not placed: each a little off the line and turned its own way.
                var dir = Turn(across ? (b.AisleFront ? -s.V : s.V) : s.U, (Hashing.Unit(Hashing.Mix(p, 1u)) - 0.5f) * 1.2f);
                var hc = s.W(slots[i] + (Hashing.Unit(Hashing.Mix(p, 2u)) - 0.5f) * gap * 0.8f, v + (Hashing.Unit(Hashing.Mix(p, 3u)) - 0.5f) * math.max(0f, b.Depth - deep - 1f));
                if (s.Put(item, item.Pick(p), hc, dir, p, -0.4f)) { made++; s.Heaps++; s.Anchors.Add((hc, along * 0.5f)); }
            }
            return made;
        }

        /// <summary>Carports facing the aisle with a car under each (dismantling bays), drums between them.</summary>
        public static int Carports(IndustrialSite s, YardBlock b, float fill, uint salt, YardItem car)
        {
            var port = IndustrialAssets.Carport;
            if (b.Depth < port.Z + 0.4f) return 0;
            const float gap = 1.8f;
            var slots = Slots(b, port.X + gap, port.X, 0.6f);
            int use = Take(slots.Count, fill);
            var face = b.AisleFront ? -s.V : s.V;
            float v = b.AisleFront ? b.V0 + port.Z * 0.5f + 0.2f : b.V1 - port.Z * 0.5f - 0.2f;
            int made = 0;
            for (int i = 0; i < use; i++)
            {
                uint p = Hashing.Mix(salt, (uint)(b.Index * 17 + i));
                var c = s.W(slots[i], v);
                s.Pad(new OrientedRect { Center = c, Axis = s.U, HalfLength = port.X * 0.5f + 0.3f, HalfWidth = port.Z * 0.5f + 0.3f }, SurfaceRole.Paving, AssetCategories.SurfacePaving);
                // The carport is a roof: the car stands under it (placed first, the roof does not check the car).
                if (!s.Put(car, car.Pick(p), c, face, p, 0.02f)) continue;
                s.Put(port, port.Pick(salt), c, face, p, 0f, false);
                s.Wrecks++;
                made++;
                // Drums or a gas pallet in the gap toward the outer end.
                if (i + 1 < use || i == 0)
                {
                    float gu = slots[i] + math.sign(slots[i] == 0f ? 1f : slots[i]) * (port.X * 0.5f + gap * 0.5f);
                    var item = (i & 1) == 0 ? IndustrialAssets.Drums : IndustrialAssets.Gas;
                    s.Put(item, item.Pick(p), s.W(gu, v + (b.AisleFront ? 1.5f : -1.5f)), face, Hashing.Mix(p, 3u), 0.02f);
                }
            }
            return made;
        }

        /// <summary>
        /// A small process plant, symmetric about the block's middle: the plant block in the centre, a tank each side, the
        /// chimney behind, a conveyor out to the aisle, all on one concrete pad.
        /// </summary>
        public static bool Plant(IndustrialSite s, YardBlock b, uint salt, bool tall)
        {
            var core = b.Depth >= IndustrialAssets.PlantSquare.Z + 1f && b.Length >= IndustrialAssets.PlantSquare.X + 26f ? IndustrialAssets.PlantSquare
                : IndustrialAssets.PlantSmall;
            var tank = IndustrialAssets.TankSmall;
            if (b.Length < core.X + 4f || b.Depth < core.Z + 1f) return false;
            var face = b.AisleFront ? -s.V : s.V;
            float vc = b.AisleFront ? b.V0 + core.Z * 0.5f + 0.8f : b.V1 - core.Z * 0.5f - 0.8f;
            float pad = math.min(b.Length - 0.6f, core.X + 2f * (tank.X + 3f) + 2f);
            s.Pad(s.Rect(b.UMid - pad * 0.5f, b.UMid + pad * 0.5f, b.V0 + 0.2f, b.V1 - 0.2f), SurfaceRole.Paving, AssetCategories.SurfacePaving);
            var c = s.W(b.UMid, vc);
            if (!s.Put(core, core.Pick(salt), c, face, salt, 0.05f)) return false;
            s.Anchors.Add((c, core.X * 0.5f));
            // Tanks either side (when there is room) and the chimney between plant and back wall.
            foreach (float side in new[] { -1f, 1f })
            {
                float tu = b.UMid + side * (core.X * 0.5f + 2.5f + tank.X * 0.5f);
                if (math.abs(tu - b.UMid) + tank.X * 0.5f > b.Length * 0.5f - 0.3f) continue;
                float tv = b.AisleFront ? b.V0 + tank.Z * 0.5f + 0.8f : b.V1 - tank.Z * 0.5f - 0.8f;
                if (tank.Z > b.Depth - 1f) continue;
                s.Put(tank, tank.Pick(Hashing.Mix(salt, 1u)), s.W(tu, tv), face, Hashing.Mix(salt, (uint)(2 + side)), 0.1f);
            }
            var stack = tall ? IndustrialAssets.StackLarge : IndustrialAssets.StackMedium;
            float behind = (b.AisleFront ? 1f : -1f) * (core.Z * 0.5f + stack.Z * 0.5f + 1.2f);
            var sc = s.W(b.UMid + core.X * 0.3f, vc + behind);
            if (s.Region.ContainsDisc(sc, 2f) && math.abs(vc + behind - b.VMid) < b.Depth * 0.5f) s.Put(stack, stack.Pick(salt), sc, face, Hashing.Mix(salt, 7u), 0.05f);
            else s.Put(IndustrialAssets.StackSmall, IndustrialAssets.StackSmall.Pick(salt), s.W(b.UMid + core.X * 0.5f + 1.6f, vc), face, Hashing.Mix(salt, 8u), 0.05f);
            s.Machines++;
            return true;
        }

        /// <summary>
        /// The yard's centrepiece: the scrap mountain. The game's big pile props are empty without their building and two props
        /// whose boxes meet lose one of them (both seen in the game), so the mountain is metal truck loads laid edge to edge on
        /// a round patch, each turned its own quarter, on a real terrain mound that lifts the middle; dark spoil and rubble loads
        /// at its rim, a loader nosing into it and a ragged line of skips along the aisle.
        /// </summary>
        public static bool Mountain(IndustrialSite s, YardBlock b, uint salt, bool skips = true)
        {
            // The mountain is the scrap jumble on a big terrain swell (the loads tiled before read as smooth toys in the game).
            if (Jumble(s, b, ScrapMix, 1f, salt, 3.8f) == 0) return false;
            var loader = IndustrialAssets.Loader;
            float side = b.UMid >= 0f ? -1f : 1f;
            float uEnd = side < 0f ? b.U0 : b.U1;
            var lp = s.W(uEnd - side * (loader.Z * 0.5f + 0.4f), b.VMid);
            if (s.Put(loader, loader.Pick(salt), lp, Turn(s.U * side, (Hashing.Unit(Hashing.Mix(salt, 10u)) - 0.5f) * 0.5f), Hashing.Mix(salt, 2u), 0.05f)) s.Machines++;
            return true;
        }

        /// <summary>
        /// Small goods (reels, crates, drums, boxes, pallets) the way a yard keeps them: in loose piles along the block, each a
        /// clump of one kind, the piles a few metres apart and not in line. Fills from the spine outward.
        /// </summary>
        public static int Piles(IndustrialSite s, YardBlock b, YardItem item, float fill, uint salt)
        {
            int made = 0;
            float span = b.Length - 2f;
            if (span < 4f || b.Depth < 3f) return 0;
            var centres = new List<float2>();
            float u = b.U0 + 2f;
            int i = 0;
            while (u < b.U1 - 2f)
            {
                uint v = Hashing.Mix(salt, (uint)(b.Index * 41 + i));
                float r = math.min(b.Depth * 0.5f - 0.6f, 1.4f + Hashing.Unit(v) * 1.2f);
                float vOff = (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * math.max(0f, b.Depth - 2f * r - 1f);
                centres.Add(new float2(u + r, b.VMid + vOff));
                u += 2f * r + 1.5f + Hashing.Unit(Hashing.Mix(v, 2u)) * 3f;
                i++;
            }
            centres.Sort((x, y) => math.abs(x.x).CompareTo(math.abs(y.x)));
            int use = Take(centres.Count, fill);
            for (int k = 0; k < use; k++)
            {
                uint v = Hashing.Mix(salt, 0x9110u + (uint)(b.Index * 41 + k));
                float r = math.min(b.Depth * 0.5f - 0.6f, 1.4f + Hashing.Unit(v) * 1.2f);
                int count = 3 + (int)(Hashing.Unit(Hashing.Mix(v, 3u)) * 5f);
                made += Clump(s, s.W(centres[k].x, centres[k].y), r, item, count, v);
            }
            if (made > 0) s.Anchors.Add((s.W(b.UMid, b.VMid), math.min(b.Length, b.Depth) * 0.3f));
            return made;
        }

        // ───────────── front yard ─────────────

        /// <summary>What the front yard holds.</summary>
        public struct FrontSpec
        {
            public YardItem Office;       // the office kind (container office, mobile home, booth…)
            public int Offices;           // how many in the row
            public bool Booth;            // a gate booth by the barrier
            public bool Weighbridge;      // a weighbridge slab on the spine
            public bool Parking;          // staff/customer parking on the other side
            public bool Toilet, Flag;
            public YardItem ParkedCar;
        }

        /// <summary>
        /// The front yard: boom barrier and booth at the gate, the office row along the spine on the office side with a paved
        /// forecourt, toilet and bins at its end, the parking row on the other side, the weighbridge on the spine.
        /// </summary>
        public static void FrontYard(IndustrialSite s, FrontSpec f, uint salt)
        {
            var ctx = s.Ctx;
            float a = s.Spec.Aisle, front = s.Spec.Front;
            int os = s.OfficeSide, ps = -os;
            float2 toSpine(int side) => -s.U * side;
            // Spine apron: the gate mouth is paved a little wider than the spine.
            if (s.HasGate)
            {
                s.Pad(s.Rect(-a * 0.5f - 1.5f, a * 0.5f + 1.5f, -0.5f, 4f), SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
                // Boom barrier on the inbound lane's kerb, booth beside it.
                var boom = IndustrialAssets.Boom;
                s.Put(boom, boom.Pick(salt), s.W(os * (a * 0.5f + 0.5f), 5f), s.U * -os, Hashing.Mix(salt, 1u), 0.05f);
                if (f.Booth)
                {
                    var booth = IndustrialAssets.Booth;
                    // Long side along the spine, window to the lane.
                    var c = s.W(os * (a * 0.5f + 1.2f + booth.Z * 0.5f), 6.5f + booth.X * 0.5f);
                    if (s.Put(booth, booth.Pick(salt), c, toSpine(os), Hashing.Mix(salt, 2u), 0.1f)) s.Offices++;
                }
            }
            if (f.Weighbridge)
            {
                float v0 = math.min(front - 1f, 7f), v1 = math.min(front - 0.5f, v0 + 18f);
                if (v1 - v0 > 8f) s.Pad(s.Rect(-1.8f, 1.8f, v0, v1), SurfaceRole.Paving, AssetCategories.SurfacePaving);
            }
            // The office row: side by side along the spine behind a paved forecourt, each end-on to it (like portacabins).
            float rowStart = f.Booth ? 6.5f + IndustrialAssets.Booth.X + 1.5f : 4f;
            var office = f.Office;
            float back = a * 0.5f + 3.2f;   // forecourt between spine and offices
            float uRow = os * (back + office.Z * 0.5f);
            float pitch = office.X + 1.2f;
            float v = rowStart;
            if (f.Offices > 0 && office.Options != null)
            {
                int fit = math.min(f.Offices, (int)math.floor((front - 0.5f - rowStart + 1.2f) / pitch));
                if (fit > 0)
                    s.Pad(s.Rect(os > 0 ? a * 0.5f : -back - office.Z - 0.6f, os > 0 ? back + office.Z + 0.6f : -a * 0.5f, rowStart - 1f, rowStart + fit * pitch - 0.2f),
                        SurfaceRole.Paving, AssetCategories.SurfacePaving);
                for (int i = 0; i < fit; i++)
                {
                    var c = s.W(uRow, v + office.X * 0.5f);
                    // Long side across: local Z along U, the end toward the spine.
                    if (s.Put(office, office.Pick(Hashing.Mix(salt, 0x0FFu)), c, -s.U * os, Hashing.Mix(salt, 0x100u + (uint)i), 0.1f)) s.Offices++;
                    v += pitch;
                }
            }
            // Toilet, bins and a flag at the end of the row.
            if (f.Toilet && v + 1.5f <= front)
            {
                s.Put(IndustrialAssets.Toilet, IndustrialAssets.Toilet.Pick(salt), s.W(uRow, v + 0.8f), toSpine(os), Hashing.Mix(salt, 0x7011u), 0.1f);
                s.Put(IndustrialAssets.Toilet, IndustrialAssets.Toilet.Pick(salt), s.W(uRow + os * 1.6f, v + 0.8f), toSpine(os), Hashing.Mix(salt, 0x7012u), 0.1f);
            }
            s.Put(IndustrialAssets.Bin, IndustrialAssets.Bin.Pick(salt), s.W(os * (a * 0.5f + 1.2f), rowStart + 0.5f), toSpine(os), Hashing.Mix(salt, 0xB1Bu), 0.1f);
            if (f.Flag) s.Put(IndustrialAssets.Flag, IndustrialAssets.Flag.Pick(salt), s.W(os * (a * 0.5f + 1.2f), math.max(2.5f, rowStart - 2f)), s.V, Hashing.Mix(salt, 0xF1Au), 0.1f);

            // Parking: one row of stalls along the spine on the other side, cars nose in.
            if (f.Parking)
            {
                var car = f.ParkedCar;
                float stall = 2.9f, depth = 5.6f;
                float pv0 = 4.5f, pv1 = front - 0.8f;
                int n = (int)math.floor((pv1 - pv0) / stall);
                if (n >= 2)
                {
                    float u0 = ps * (a * 0.5f + 0.4f), u1 = ps * (a * 0.5f + 0.4f + depth);
                    s.Pad(s.Rect(math.min(u0, u1), math.max(u0, u1), pv0 - 0.3f, pv0 + n * stall + 0.3f), SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
                    for (int i = 0; i < n; i++)
                    {
                        uint p = Hashing.Mix(salt, 0xCA70u + (uint)i);
                        // Two in three stalls taken, in a fixed rhythm (never random holes).
                        if (i % 3 == 2) continue;
                        var c = s.W(ps * (a * 0.5f + 0.4f + depth * 0.5f), pv0 + (i + 0.5f) * stall);
                        if (s.Put(car, car.Pick(p), c, s.U * ps, p, 0.02f)) s.Parked++;
                    }
                    // Wheel stops: a block at the head of the row.
                    s.BlockWall(s.W(ps * (a * 0.5f + 0.4f + depth + 0.4f), pv0), s.W(ps * (a * 0.5f + 0.4f + depth + 0.4f), pv0 + n * stall), Hashing.Mix(salt, 0xCA7Bu));
                }
            }
        }

        // ───────────── edge ─────────────

        /// <summary>
        /// The tidy band inside the fence: "trees" (an even screen), "blocks" (a wall of concrete blocks), "containers" (a wall
        /// of 20 ft boxes along the back and sides, never along the street), "plain" (gravel only). Gaps at the gates.
        /// </summary>
        public static void Edge(IndustrialSite s, string mode, string[] treeIds, uint salt)
        {
            var region = s.Region;
            float e = s.Spec.Edge;
            if (mode == "plain" || e < 1.5f) return;
            if (mode == "trees")
            {
                YardKit.Screen(s.Ctx, s.Occ, s.Gates, math.min(e * 0.5f + 0.3f, 3.2f), 8f, treeIds, salt);
                return;
            }
            if (mode == "boulders") return;   // laid before the fill (YardEarth.Bund)
            // Wrecks: a wall of scrap cars piled two and three high along the back and the sides, nose to the fence.
            var item = mode == "containers" ? IndustrialAssets.Box20 : mode == "wrecks" ? IndustrialAssets.Car : IndustrialAssets.Block;
            int n = region.Ring.Length;
            for (int i = 0; i < n; i++)
            {
                var a = region.Ring[i]; var b = region.Ring[(i + 1) % n];
                float len = math.distance(a, b);
                if (len < 4f) continue;
                var t = (b - a) / len;
                var inward = region.InwardNormal(i);
                // Containers stand along the back and the sides only: never facing the street.
                bool big = mode == "containers" || mode == "wrecks";
                if (big && math.dot(inward, s.V) > 0.7f) continue;
                float off = mode == "containers" ? item.X * 0.5f + 0.6f : mode == "wrecks" ? item.Z * 0.5f + 0.7f : 0.9f;
                float along = mode == "containers" ? item.Z : item.X;
                float pitch = along + (mode == "containers" ? 0.35f : mode == "wrecks" ? 0.9f : 0.05f);
                // Keep clear of the corners (the next edge's row) and of the gates.
                // (The next edge's row stands this deep inside the fence.)
                float corner = mode == "containers" ? item.X + 1.4f : mode == "wrecks" ? item.Z + 1.6f : 1.8f;
                float m0 = corner, m1 = len - corner;
                int k = (int)math.floor((m1 - m0 - along) / pitch) + 1;
                if (k <= 0) continue;
                float start = (m0 + m1) * 0.5f - (k - 1) * pitch * 0.5f;
                uint pick = Hashing.Mix(salt, (uint)i);
                for (int j = 0; j < k; j++)
                {
                    var p = a + t * (start + j * pitch) + inward * off;
                    bool nearGate = false;
                    foreach (var g in s.Gates) if (math.distance(g.Position, p) < s.Spec.Aisle * 0.5f + 3f + along * 0.5f) { nearGate = true; break; }
                    if (nearGate) continue;
                    uint v = Hashing.Mix(pick, (uint)j);
                    // Containers lie along the fence (local Z along the edge); blocks too (their long side is local X); wrecks nose in.
                    var dir = mode == "containers" ? t : mode == "wrecks" ? -inward : inward;
                    if (mode == "containers") s.Containers += Place(s, item, item.Pick(v), p, dir, v, Mess.Containers, 2);   // a two-high container wall
                    else if (mode == "wrecks") s.Wrecks += Place(s, item, item.Pick(v), p, Turn(dir, (Hashing.Unit(v) - 0.5f) * 0.3f), v, Mess.Wrecks(0.85f), 1);
                    else s.Put(item, item.Pick(pick), p, dir, v, 0.01f);
                }
            }
        }

        /// <summary>Floodlight masts where the aisles meet the edge band, at the gate and at the end of the spine.</summary>
        public static int Lights(IndustrialSite s, uint salt)
        {
            var mast = IndustrialAssets.Flood;
            var spots = new List<float2>(s.AisleEnds);
            spots.Add(s.W(s.OfficeSide * (s.Spec.Aisle * 0.5f + 0.8f), 2.2f));
            spots.Add(s.W(-s.OfficeSide * (s.Spec.Aisle * 0.5f + 0.8f), s.SpineEnd + s.Spec.Aisle * 0.5f + 0.8f));
            int n = 0;
            for (int i = 0; i < spots.Count; i++)
                if (s.Region.ContainsDisc(spots[i], 0.8f) && s.Put(mast, mast.Pick(salt), spots[i], s.V, Hashing.Mix(salt, (uint)i), 0.05f)) n++;
            s.Lights += n;
            return n;
        }
    }
}

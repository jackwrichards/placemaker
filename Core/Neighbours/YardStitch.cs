// Placemaker neighbourhood polish — yard stitching (pure geometry, engine-free).
//
// The game's houses are fixed models on rectangular lots, each with its own fence/hedge lines and its own lawn. Side by
// side they never join up. Version 2 (after "fences and lawns each doing their own thing look wrong"): ONE plot per house
// first, and everything else follows from it.
//   1. Plot: the house's lot, widened on each side that has a neighbour to the middle line between the two lots (the
//      shared edge on a straight street, the bisector of a curve's wedge), deepened only to the middle line with a house
//      behind it (back to back). Never further back into open land. A plot is a rectangle cut by straight lines: it is
//      convex, never overlaps another plot and never enters another house's lot.
//   2. Fences sit on plot edges: one line per shared side (the nicer of the two houses' side lines, hedge > fence > wall;
//      the other is hidden), the back line on the plot's back edge. Lines ending on the lot edge where the plot reaches
//      further carry on to the plot edge (only as a natural continuation). No line leaves its plot; where two lines lie
//      on one another (back to back) the nicer one stays.
//   3. Lawn fills the plot, around every driveway and paving: lawn and fence share their edges.
//   4. Land a ring of houses leaves in its middle (inside a circle) is one shared green; ground paint blends the rest.
// Frames: a house's local +z points to its street (front), +x to its right; lots are (hx, hz) half sizes in metres.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static partial class YardStitch
    {
        /// <summary>The objects gardens and shop yards are given (StitchPlan.Bushes Kind indexes this; only ever appended to: a
        /// saved city's pieces are matched by name, but the order is the plan's).</summary>
        public static readonly string[] BushKinds =
        {
            "GreenBushWild01", "GreenBushWild02", "FlowerBushWild01", "FlowerBushWild02", "GreenBushWildAdult01",   // 0-4 bushes
            "Boulder01", "Boulder02", "Boulder03",                                                                    // 5-7 boulders
            "Rock01", "Rock05", "Rock08", "Rock12", "Rock21",                                                         // 8-12 rocks
            "RubblePileMixed01", "RubblePileMixed02", "RubblePileConcrete01", "RubblePileConcrete02",                // 13-16 rubble
            "Pallet01", "Pallet02", "CratePile01", "CratePile02", "BarrelPile01", "Barrel01",                         // 17-22 yard stock
            "Log01", "Log04", "Log07",                                                                                // 23-25 logs
            "GardenBench01", "GardenBench02", "Flowerbed01",                                                          // 26-28 garden things
            // 29-40 a shop's service corner and car park (YardStitch.ShopYard)
            "TrashContainerEmpty02", "TrashContainerEmpty03", "LargeOpenContainer01", "Pallet03", "CratePile03",
            "CardboardBoxPile01", "CardboardBoxPile03", "GasBottlePallet01", "ToolShed03", "ShippingContainer01", "BackupGenerator01",
            "ParkingLotDecal01",
            // 41-53 parked cars and vans (Placemaker's vehicle props, Game/Assets/VehiclePropSystem)
            "Placemaker Vehicle Car 1", "Placemaker Vehicle Car 2", "Placemaker Vehicle Car 3", "Placemaker Vehicle Car 4", "Placemaker Vehicle Car 5",
            "Placemaker Vehicle Car 6", "Placemaker Vehicle Car 7", "Placemaker Vehicle Car 8", "Placemaker Vehicle Car 9", "Placemaker Vehicle Car 10",
            "Placemaker Vehicle Van 1", "Placemaker Vehicle Van 2", "Placemaker Vehicle Van 3",
            // 54-56 trees along a shop yard's fence
            "EU_PoplarTree01", "BirchTree01", "NA_LindenTree01",
        };
        private const int FirstRock = 5, FirstStone = 8, FirstRubble = 13, FirstStock = 17, FirstLog = 23, FirstBench = 26, Flowerbed = 28;
        /// <summary>The kinds gardens are given (0-28); the rest are a shop yard's.</summary>
        public const int GardenKinds = 29;

        public static readonly string[] Themes = { "garden", "natural", "tidy", "yard" };

        /// <summary>The offline benchmark's allocation counter (GC.GetAllocatedBytesForCurrentThread): each stage's allocations in
        /// the plan's notes. Null in the game.</summary>
        public static Func<long> AllocatedBytes;

        /// <summary>How each garden came to be, for the offline replay (OutgrowBench --explain): every garden (by house index)
        /// after each step that shapes it, and why the planner joined or split two houses. Null in the game (nothing is copied).</summary>
        public static Action<string, float2[][]> Trace;
        public static Action<string> TraceWhy;

        public static string ThemeOf(StitchHouse h) =>
            !string.IsNullOrEmpty(h.Theme) && Array.IndexOf(Themes, h.Theme) >= 0 ? h.Theme : "garden";

        /// <summary>Both face one point in front of them, within <paramref name="reach"/> of their street edges (a turning circle,
        /// a tight bend): their front middles' forward rays meet ahead of both.</summary>
        private static bool Converge(StitchHouse a, StitchHouse b, float reach)
        {
            var pa = a.Position + a.Forward * a.HalfLot.y;
            var pb = b.Position + b.Forward * b.HalfLot.y;
            float den = a.Forward.x * b.Forward.y - a.Forward.y * b.Forward.x;
            if (math.abs(den) < 0.2f) return false;   // (nearly) parallel: a straight street
            var w = pb - pa;
            float t = (w.x * b.Forward.y - w.y * b.Forward.x) / den;
            float u = (w.x * a.Forward.y - w.y * a.Forward.x) / den;
            return t > 0f && u > 0f && t < reach && u < reach;
        }

        /// <summary>A knob for one house: what its lot was polished with (StitchHouse.Options), else the settings.</summary>
        private static float K(StitchHouse h, StitchSettings s, string name, float fallback) =>
            h?.Options != null && h.Options.TryGetValue(name, out var v) ? v : s.Knob(name, fallback);

        /// <summary>What a house's land past its lot becomes: its outgrow type's "fill" (PolishLooks.FillGarden, FillCarPark,
        /// FillPaved), whatever the building is (the type alone decides; an old "by the building" is a garden).</summary>
        private static int FillOf(StitchHouse h, StitchSettings s)
        {
            int f = (int)math.round(K(h, s, "fill", PolishLooks.FillGarden));
            return f >= PolishLooks.FillGarden && f <= PolishLooks.FillPaved ? f : PolishLooks.FillGarden;
        }

        /// <summary>A car park yard's style: its outgrow type's "parkStyle", whatever the building is (an old "by the building":
        /// everyday).</summary>
        private static int ParkStyleOf(StitchHouse h, StitchSettings s)
        {
            int p = (int)math.round(K(h, s, "parkStyle", PolishLooks.ParkEveryday));
            return p >= PolishLooks.ParkClean && p <= PolishLooks.ParkIndustrial ? p : PolishLooks.ParkEveryday;
        }

        /// <summary>Its land past the lot is hard ground (a car park yard or paving), not a garden.</summary>
        private static bool HardLand(StitchHouse h, StitchSettings s) => FillOf(h, s) != PolishLooks.FillGarden;

        /// <summary>The paving its land is laid in: the surface most of its ground already is, else (a home) plain paving.</summary>
        private static string PavingOf(StitchHouse h) => string.IsNullOrEmpty(h.MainSurface) ? "Pavement Surface 01" : h.MainSurface;

        /// <summary>How far its land keeps from water (m, from the waterline).</summary>
        private static float WaterGap(StitchHouse h, StitchSettings s) => math.max(0f, K(h, s, "waterGap", 30f));

        /// <summary>
        /// A painted garden (no drawn lawn) or the house's own lawns: a mix along every street. The setting "Painted gardens"
        /// (share p) is a Garden house's chance; a Natural or Yard house is painted more often (1 - (1 - p)², 33% → 55%), a
        /// Tidy one seldom (p², 33% → 11%); 0 never, 100 always.
        /// </summary>
        private static bool PaintedGarden(StitchHouse h, StitchSettings s)
        {
            float p = math.saturate(K(h, s, "paintedGardens", 33f) / 100f);
            switch (ThemeOf(h))
            {
                case "natural": case "yard": p = 1f - (1f - p) * (1f - p); break;
                case "tidy": p *= p; break;
            }
            return Hash((ulong)h.Id ^ 0x9A1D7UL) % 100 < (uint)math.round(p * 100f);
        }

        public static int Score(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            return n.Contains("hedge") ? 3 : n.Contains("fence") ? 2 : n.Contains("wall") ? 1 : 0;
        }

        /// <summary>A straight line through P along D; a house keeps the side its own lot is on.</summary>
        private sealed class Cut
        {
            public float2 P, D;
            /// <summary>When set, the side kept (else the house's own front middle).</summary>
            public float2? Keep;
            /// <summary>The line two joined houses share their land on (side by side, back to back, facing): the emergent split
            /// (knob emergent) shares the land by nearness instead.</summary>
            public bool Pair;
            /// <summary>Of those, the middle line two side neighbours share: the emergent split keeps it (their side fences are
            /// laid on it; nearness gives the same line, only less exactly).</summary>
            public bool Side;
        }

        public static StitchPlan Plan(IReadOnlyList<StitchHouse> houses, StitchSettings s = null)
        {
            s = s ?? new StitchSettings();
            // the same houses always in the same order, however the caller gathered them (the game takes a pass's houses from
            // a set): a house re-planned in a neighbour's pass gets exactly what its own pass gave it. Before, who closed a
            // shared garden edge (and where the closing pieces stood) followed the order, so every pass took a row's hedge
            // pieces away and made them again on the other side
            houses = houses.OrderBy(h => h.Id).ToArray();
            var plan = new StitchPlan();
            Feature.Clear();
            SetBarriers(s, houses);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var stages = new List<string>();
            var allocs = new List<string>();
            long allocAt = AllocatedBytes?.Invoke() ?? 0;
            void Stage(string name)
            {
                stages.Add($"{name} {clock.Elapsed.TotalMilliseconds:F0}");
                clock.Restart();
                if (AllocatedBytes == null) return;
                long now = AllocatedBytes();
                allocs.Add($"{name} {(now - allocAt) / 1048576.0:F2}");
                allocAt = now;
            }
            int n = houses.Count;

            // ── 1. neighbours: side pairs (i's right side next to j's left side) and back to back ──
            var right = Enumerable.Repeat(-1, n).ToArray();
            var left = Enumerable.Repeat(-1, n).ToArray();
            var rightGap = Enumerable.Repeat(float.MaxValue, n).ToArray();
            // the widest street gap any of these houses' looks allows (each pair checks its own below)
            float maxFgAll = n > 0 ? houses.Max(h => K(h, s, "maxFrontGap", s.MaxFrontGap)) : s.MaxFrontGap;
            for (int i = 0; i < n; i++)
            {
                var a = houses[i];
                // why the nearest candidate was no neighbour, written out only when the house is left without one (a string
                // for every candidate turned down was garbage for nothing): 1 street edges apart, 2 gaps, 3 something between
                int whyKind = 0;
                long whyId = 0;
                float whyA = 0f, whyB = 0f;
                for (int j = 0; j < n; j++)
                {
                    var b = houses[j];
                    if (i == j) continue;
                    // around a turning circle or a tight bend both face one point a little in front of them: neighbours even
                    // when they face far apart (the corners of a cul-de-sac fill in, each up to the middle line)
                    bool bend = Converge(a, b, s.Knob("bendReach", 60f));
                    if (math.dot(a.Forward, b.Forward) < (bend ? s.Knob("bendDot", -0.6f) : 0.5f)) continue;
                    if (math.dot(b.Position - a.Position, a.Right) <= 0f) continue;
                    // side by side, not one behind the other: their street edges line up (measured along the two houses'
                    // mean direction: on a tight circle neighbours face up to ~50° apart)
                    var frontA = a.Position + a.Forward * a.HalfLot.y;
                    var frontB = b.Position + b.Forward * b.HalfLot.y;
                    float offset = math.abs(math.dot(frontB - frontA, math.normalizesafe(a.Forward + b.Forward, a.Forward)));
                    float fg = math.distance(a.FrontRight, b.FrontLeft);
                    if (fg > maxFgAll) continue;
                    float deep = Deep(a, b);
                    float bg = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                    if (offset > s.MaxFrontOffset) { if (whyKind == 0) { whyKind = 1; whyId = b.Id; whyA = offset; } continue; }
                    float maxFg = math.min(K(a, s, "maxFrontGap", s.MaxFrontGap), K(b, s, "maxFrontGap", s.MaxFrontGap));
                    float maxBg = bend ? s.Knob("bendBackGap", 130f) : math.min(K(a, s, "maxBackGap", s.MaxBackGap), K(b, s, "maxBackGap", s.MaxBackGap));
                    if (fg > maxFg || bg > maxBg) { whyKind = 2; whyId = b.Id; whyA = fg; whyB = bg; continue; }
                    // the land between them (from just behind the street to the shallower one's back) is open: no road, path,
                    // track or other building runs through it
                    float shallow = math.min(a.HalfLot.y, b.HalfLot.y);
                    if (Blocked(new[] { a.FrontRight - a.Forward * 2f, b.FrontLeft - b.Forward * 2f, BackAt(b, -1, shallow), BackAt(a, +1, shallow) }))
                    { whyKind = 3; whyId = b.Id; continue; }
                    if (fg >= rightGap[i]) continue;
                    // one left neighbour per house: the nearer claim wins
                    int other = left[j];
                    if (other >= 0 && other != i && rightGap[other] <= fg) continue;
                    if (other >= 0 && other != i) { right[other] = -1; rightGap[other] = float.MaxValue; }
                    if (right[i] >= 0) left[right[i]] = -1;
                    right[i] = j;
                    left[j] = i;
                    rightGap[i] = fg;
                }
                if (right[i] < 0 && whyKind != 0)
                {
                    string why = whyKind == 1 ? $"{whyId}: street edges {whyA:F1} m apart" : whyKind == 2 ? $"{whyId}: gap {whyA:F1} m at the street, {whyB:F1} m at the back" : $"{whyId}: a road, path or building between";
                    plan.Notes.Add($"{a.Id} has no right neighbour: nearest {why}");
                }
            }
            Stage("sides");
            // a row (houses joined side by side) shares one back line: a shallow lot's garden reaches back to the row's
            // deepest lot (at most maxDeepen further), so back fences line up and a ring's middle is an even shape
            var rowDeepen = new float[n];
            var ringCut = new Cut[n];   // set: the house is on the inside of a closed ring
            var ringOf = Enumerable.Repeat(-1, n).ToArray();
            int rings = 0;
            var seen = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (seen[i] || (left[i] < 0 && right[i] < 0)) continue;
                var row = new List<int>();
                var stack = new Stack<int>();
                stack.Push(i);
                seen[i] = true;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    row.Add(k);
                    foreach (int m in new[] { left[k], right[k] })
                        if (m >= 0 && !seen[m]) { seen[m] = true; stack.Push(m); }
                }
                float depth = row.Max(k => houses[k].HalfLot.y);
                // a closed ring (every house has both neighbours): its gardens all reach one line, however far, so what it
                // encloses is an even shape and not a star
                bool ring = row.Count >= 4 && row.All(k => left[k] >= 0 && right[k] >= 0);
                if (ring)
                {
                    var fronts = row.Select(k => houses[k].Position + houses[k].Forward * houses[k].HalfLot.y).ToList();
                    var centre = fronts.Aggregate(float2.zero, (acc, p) => acc + p) / fronts.Count;
                    bool inside = row.Count(k => math.dot(houses[k].Forward, houses[k].Position - centre) > 0f) >= row.Count * 3 / 4;
                    if (inside)
                    {
                        // the inside of a circle: the slices stop at a circle of half the street's radius (a shared green in
                        // the middle rather than long thin points), nearer only where a house reaches further in
                        float back = s.Knob("ringBack", 0.5f) * fronts.Average(p => math.distance(p, centre));
                        foreach (int k in row)
                            if (houses[k].Footprint != null)
                                foreach (var p in houses[k].Footprint) back = math.min(back, math.dot(p - centre, houses[k].Forward) - s.Knob("ringClear", 3f));
                        back = math.max(back, 4f);
                        foreach (int k in row)
                        {
                            ringOf[k] = rings;
                            ringCut[k] = new Cut { P = centre + houses[k].Forward * back, D = houses[k].Right };
                            rowDeepen[k] = 2f * houses[k].HalfLot.y + 200f;   // as far as it takes: the ring line cuts it
                        }
                        rings++;
                        continue;
                    }
                }
                // (never paved land: a paved slab behind a short shop reads as an empty car park)
                foreach (int k in row) rowDeepen[k] = HardLand(houses[k], s) ? 0f : ring ? 2f * (depth - houses[k].HalfLot.y) : math.min(2f * (depth - houses[k].HalfLot.y), K(houses[k], s, "maxDeepen", 60f));
            }

            Stage("rows");
            float maxRear = n > 0 ? houses.Max(h => K(h, s, "maxRearGap", 60f)) : 24f;
            var rear = Enumerable.Repeat(-1, n).ToArray();
            for (int i = 0; i < n; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j || math.dot(houses[i].Forward, houses[j].Forward) > -0.5f || ringCut[i] != null || ringCut[j] != null) continue;
                    if (!RearGap(houses[i], houses[j], out float gap, out float overlap) || gap > math.min(K(houses[i], s, "maxRearGap", 60f), K(houses[j], s, "maxRearGap", 60f)) || overlap < s.Knob("minRearOverlap", 6f)) continue;
                    if (gap > 0.5f && Blocked(Hull(new[] { houses[i].BackLeft, houses[i].BackRight, houses[j].BackLeft, houses[j].BackRight }))) continue;
                    if (gap < best) { best = gap; rear[i] = j; }
                    TraceWhy?.Invoke($"rear-candidate {houses[i].Id} {houses[j].Id} gap {gap:F1} overlap {overlap:F1} angle {math.degrees(math.acos(math.clamp(-math.dot(houses[i].Forward, houses[j].Forward), -1f, 1f))):F0}");
                }
            }
            if (TraceWhy != null)
                for (int i = 0; i < n; i++) if (rear[i] >= 0) TraceWhy($"rear {houses[i].Id} {houses[rear[i]].Id}");
            Stage("rear");
            // the player's per-lot flag: an unpolished lot never changes; next to a polished one it is that one's edge; two
            // unpolished neighbours are left alone (no pair at all)
            bool Pol(int k) => houses[k].Polished;
            bool Live(int a, int b) => Pol(a) || Pol(b);
            int Fixed(int a, int b) => !Pol(a) ? a : !Pol(b) ? b : -1;
            var rearPairs = new HashSet<(int, int)>();
            for (int i = 0; i < n; i++) if (rear[i] >= 0 && Live(i, rear[i])) rearPairs.Add((math.min(i, rear[i]), math.max(i, rear[i])));
            var touched = new bool[n];
            var paired = new HashSet<(int, int)>();
            for (int i = 0; i < n; i++)
                if (right[i] >= 0 && Live(i, right[i])) { touched[i] = touched[right[i]] = true; paired.Add((math.min(i, right[i]), math.max(i, right[i]))); }
            foreach (var (i, j) in rearPairs) { touched[i] = touched[j] = true; paired.Add((i, j)); }

            // a side or back still free that faces another house's free side or back (the corners of a block, where houses
            // on two streets meet): both reach the middle line between them
            var free = new bool[n, 3];   // edges: 0 left, 1 right, 2 back
            for (int i = 0; i < n; i++) { free[i, 0] = left[i] < 0; free[i, 1] = right[i] < 0; free[i, 2] = ringCut[i] == null; }
            foreach (var (i, j) in rearPairs) free[i, 2] = free[j, 2] = false;
            var candidates = new List<(int I, int EI, int J, int EJ, float Gap, Cut Cut)>();
            float maxFacing = n > 0 ? houses.Max(h => K(h, s, "maxFacingGap", 50f)) : 20f;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (!Live(i, j) || paired.Contains((i, j)) || math.distance(houses[i].Position, houses[j].Position) > math.length(houses[i].HalfLot) + math.length(houses[j].HalfLot) + maxFacing) continue;
                    for (int ei = 0; ei < 3; ei++)
                        for (int ej = 0; ej < 3; ej++)
                        {
                            if (!free[i, ei] || !free[j, ej] || !Faces(houses[i], ei, houses[j], ej, math.min(K(houses[i], s, "maxFacingGap", 50f), K(houses[j], s, "maxFacingGap", 50f)), s.Knob("minFacingOverlap", 4f), out float g, out var cut)) continue;
                            if (g > 0.5f)
                            {
                                var (fa0, fa1, _) = Edge(houses[i], ei);
                                var (fb0, fb1, _) = Edge(houses[j], ej);
                                if (Blocked(Hull(new[] { fa0, fa1, fb0, fb1 }))) continue;
                            }
                            // an unpolished lot facing a polished one: the border is its own edge (the polished one takes the gap)
                            int fx = Fixed(i, j);
                            if (fx >= 0)
                            {
                                var (f0, f1, _) = Edge(houses[fx], fx == i ? ei : ej);
                                cut = new Cut { P = f0, D = math.normalizesafe(f1 - f0, cut.D) };
                            }
                            candidates.Add((i, ei, j, ej, g, cut));
                        }
                }
            var facing = new List<(int I, int EI, int J, int EJ, float Gap, Cut Cut)>();
            foreach (var c in candidates.OrderBy(c => c.Gap))
            {
                // an edge may face several houses along its length (a long side behind two corner lots): one pair each
                if (paired.Contains((c.I, c.J))) continue;
                paired.Add((c.I, c.J));
                touched[c.I] = touched[c.J] = true;
                facing.Add(c);
                TraceWhy?.Invoke($"facing {houses[c.I].Id} {"lrb"[c.EI]} {houses[c.J].Id} {"lrb"[c.EJ]} gap {c.Gap:F1}");
            }
            if (TraceWhy != null)
                for (int i = 0; i < n; i++) if (right[i] >= 0) TraceWhy($"side {houses[i].Id} {houses[right[i]].Id}");
            // an unpolished lot keeps its lot as its plot and nothing of it is edited
            for (int k = 0; k < n; k++) if (!Pol(k)) touched[k] = false;

            // a shallow lot between two much deeper ones (on a straight bit of street): it keeps its own depth, and the land
            // behind it goes to the deep neighbours, split down its middle (they meet behind it, around it)
            var wraps = new List<(int L, int M, int R, float Ext)>();
            // the emergent split (YardStitch.Emergent): the land shared by nearness, each shared edge straightened, instead of the
            // lines below and the steps that patch them up after (wraps, walkways meeting, sharing the rest, slivers, caps)
            bool emergent = s.Knob("emergent", 1f) > 0.5f;
            if (!emergent && houses.Any(h => K(h, s, "wrap", 1f) > 0.5f))
                for (int m = 0; m < n; m++)
                {
                    int l = left[m], r = right[m];
                    if (l < 0 || r < 0 || K(houses[m], s, "wrap", 1f) < 0.5f || ringCut[m] != null || HardLand(houses[m], s)) continue;
                    float dm = 2f * houses[m].HalfLot.y, deep = 2f * math.min(houses[l].HalfLot.y, houses[r].HalfLot.y);
                    if (dm > deep * s.Knob("wrapRatio", 0.7f) || deep - dm < 8f) continue;
                    if (math.dot(houses[l].Forward, houses[m].Forward) < 0.94f || math.dot(houses[r].Forward, houses[m].Forward) < 0.94f) continue;
                    wraps.Add((l, m, r, deep - dm));
                    rowDeepen[m] = 0f;
                }

            Stage("pairs");
            // ── 2. plots ──
            var cuts = new List<Cut>[n];
            for (int i = 0; i < n; i++) cuts[i] = new List<Cut>();
            var borders = new Dictionary<(int, int), Cut>();
            var steps = new List<(int I, int J, bool AFront, float Deep, uint Hash)>();
            var alleys = new List<(int I, int J)>();
            var edgeBorders = new Dictionary<(int, int), int>();   // side pairs with an unpolished lot: which one
            bool squareSplits = s.Knob("squareSplits", 1f) > 0.5f;
            // side pairs on their middle line (not a walkway or a step): land left between them later is shared on it (ShareBetween)
            var sideCuts = new Dictionary<(int, int), Cut>();
            for (int i = 0; i < n; i++)
            {
                if (right[i] < 0 || !Live(i, right[i])) continue;
                var a = houses[i];
                var b = houses[right[i]];
                float deep = Deep(a, b);
                var f = (a.FrontRight + b.FrontLeft) * 0.5f;
                var k = (BackAt(a, +1, deep) + BackAt(b, -1, deep)) * 0.5f;
                int fx = Fixed(i, right[i]);
                if (fx >= 0)
                {
                    // yes meets no: the polished one takes the whole gap up to the unpolished lot's own edge (its plot only)
                    var nh = houses[fx];
                    int yi = fx == i ? right[i] : i;
                    var ecut = fx == i ? new Cut { P = nh.FrontRight, D = -nh.Forward } : new Cut { P = nh.FrontLeft, D = -nh.Forward };
                    borders[(i, right[i])] = ecut;
                    edgeBorders[(i, right[i])] = fx;
                    cuts[yi].Add(ecut);
                    float fg0 = math.distance(a.FrontRight, b.FrontLeft), bg0 = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                    if (fg0 > 1f && fg0 > bg0 + 0.5f)
                    {
                        var along = math.normalize(b.FrontLeft - a.FrontRight);
                        var inward = math.normalizesafe(-(a.Forward + b.Forward), -a.Forward);
                        cuts[yi].Add(new Cut { P = a.FrontRight + inward * s.Knob("streetSetback", 0.6f), D = along, Keep = k });
                    }
                    continue;
                }
                // how the land between them is shared, as people do: a wedge opening to the back (the outside of a curve) is
                // often split into a front part for one and a back part for the other (the fences stay on the houses' own
                // lines, a row of bushes makes the step); a narrow even gap is often left as a walkway between the houses;
                // otherwise the middle line
                {
                    float fg = math.distance(a.FrontRight, b.FrontLeft), bg = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                    uint ph = Hash(((ulong)a.Id * 0x9E3779B97F4A7C15UL) ^ (ulong)b.Id);
                    bool step = s.Knob("steps", 1f) > 0.5f && ringCut[i] == null && ringCut[right[i]] == null && bg > fg + 6f && ph % 100 < (uint)s.Knob("stepChance", 0f)
                        && SideLines(a, +1, s).Any() && SideLines(b, -1, s).Any();
                    bool alley = !step && math.min(K(a, s, "alleys", 1f), K(b, s, "alleys", 1f)) > 0.5f && fg >= 3f && fg <= 9f && math.abs(bg - fg) < 2f && (ph >> 8) % 100 < (uint)math.min(K(a, s, "alleyChance", 45f), K(b, s, "alleyChance", 45f));
                    if (step || alley)
                    {
                        // each keeps to the other's lot line (a step: both reach it, the parts are taken away below), or to
                        // its own (an alley)
                        var ca = step ? new Cut { P = b.FrontLeft, D = b.Forward, Keep = a.Position } : new Cut { P = a.FrontRight, D = a.Forward, Keep = a.Position };
                        var cb = step ? new Cut { P = a.FrontRight, D = a.Forward, Keep = b.Position } : new Cut { P = b.FrontLeft, D = b.Forward, Keep = b.Position };
                        ca.Pair = cb.Pair = !step;   // (the emergent split keeps the walkway itself free instead)
                        cuts[i].Add(ca);
                        cuts[right[i]].Add(cb);
                        if (step) steps.Add((i, right[i], (ph >> 4 & 1) == 0, deep, ph));
                        else alleys.Add((i, right[i]));
                        continue;
                    }
                }
                var cut = new Cut { P = f, D = math.normalizesafe(k - f, -a.Forward) };
                // two lots on one grid at right angles (round a block's corner): split along one of their own side lines instead
                // of the 45° middle line of their wedge (knob squareCorners 1). Off: the 45° line shares the corner fairly, both
                // backyards get half (the user, 23 Oct: "that's ideal ... they both perfectly share that corner of the block";
                // with the side line one of them took the whole corner); their long thin ends are capped (CapNarrow)
                if (s.Knob("squareCorners", 0f) > 0.5f && math.abs(math.dot(a.Forward, b.Forward)) < 0.5f && OnOneGrid(a, b, out _, out _))
                {
                    // the land between them goes to the house whose back faces it (its garden just grows straight back, up to
                    // the other's side line); the other keeps to its own side line
                    var alongB = new Cut { P = b.FrontLeft, D = b.Forward };
                    var alongA = new Cut { P = a.FrontRight, D = a.Forward };
                    var gap = (f + k) * 0.5f;
                    float behindA = math.dot(a.Position - gap, a.Forward), behindB = math.dot(b.Position - gap, b.Forward);
                    var first = behindB >= behindA ? alongA : alongB;
                    var second = behindB >= behindA ? alongB : alongA;
                    if (Clear(first, Lot(first == alongA ? b : a))) cut = first;
                    else if (Clear(second, Lot(second == alongA ? b : a))) cut = second;
                }
                cut.Pair = true;
                cut.Side = true;
                borders[(i, right[i])] = cut;
                sideCuts[(math.min(i, right[i]), math.max(i, right[i]))] = cut;
                cuts[i].Add(cut);
                cuts[right[i]].Add(cut);
                float frontGap = math.distance(a.FrontRight, b.FrontLeft), backGap = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep));
                if (frontGap > 1f && frontGap > backGap + 0.5f)
                {
                    var along = math.normalize(b.FrontLeft - a.FrontRight);
                    var inward = math.normalizesafe(-(a.Forward + b.Forward), -a.Forward);
                    var street = new Cut { P = a.FrontRight + inward * s.Knob("streetSetback", 0.6f), D = along, Keep = k };
                    cuts[i].Add(street);
                    cuts[right[i]].Add(street);
                }
            }
            var deepen = new bool[n];
            var extra = new float[n, 3];
            // the line each pair that is not side by side shares its land on (facing, back to back): land left between them
            // later is shared on the same line (ShareBetween)
            var pairCuts = new Dictionary<(int, int), Cut>();
            foreach (var c in facing)
            {
                pairCuts[(c.I, c.J)] = c.Cut;
                c.Cut.Pair = Fixed(c.I, c.J) < 0;
                if (Pol(c.I)) cuts[c.I].Add(c.Cut);
                if (Pol(c.J)) cuts[c.J].Add(c.Cut);
                extra[c.I, c.EI] = math.max(extra[c.I, c.EI], c.Gap + 2f);
                extra[c.J, c.EJ] = math.max(extra[c.J, c.EJ], c.Gap + 2f);
                plan.Pairs.Add(new StitchPair { Left = houses[c.I].Id, Right = houses[c.J].Id, Rear = true, FrontGap = c.Gap, BackGap = c.Gap, Edge = Fixed(c.I, c.J) >= 0 });
            }
            foreach (var (i, j) in rearPairs)
            {
                var a = houses[i];
                var b = houses[j];
                var backA = a.Position - a.Forward * a.HalfLot.y;
                var backB = b.Position - b.Forward * b.HalfLot.y;
                int fx = Fixed(i, j);
                // back to back with an unpolished lot: the polished garden reaches that lot's back edge
                var cut = fx < 0 ? new Cut { P = (backA + backB) * 0.5f, D = math.normalizesafe(a.Right - b.Right, a.Right) }
                    : new Cut { P = fx == i ? backA : backB, D = houses[fx].Right };
                cut.Pair = fx < 0;
                pairCuts[(i, j)] = cut;
                if (Pol(i)) cuts[i].Add(cut);
                if (Pol(j)) cuts[j].Add(cut);
                deepen[i] = deepen[j] = true;
                RearGap(a, b, out float gap, out _);
                plan.Pairs.Add(new StitchPair { Left = a.Id, Right = b.Id, Rear = true, FrontGap = gap, BackGap = gap, Edge = fx >= 0 });
            }
            for (int i = 0; i < n; i++) if (ringCut[i] != null) cuts[i].Add(ringCut[i]);
            // a polished garden with a side or back nobody else's: it reaches out to a road, path, track or building this near
            // and hugs it (the line of its edge, just clear of it); its own lines on that side follow (fences, below)
            var hugs = new List<(int I, int E, Cut Cut)>();
            var backHug = new Cut[n];   // the street (road, path, track) a garden's back reaches and ends along
            for (int i = 0; i < n; i++)
                {
                    if (!Pol(i)) continue;
                    bool hugOn = K(houses[i], s, "hug", 1f) > 0.5f;
                    var h = houses[i];
                    for (int e = 0; e < 3; e++)
                    {
                        bool used = e == 0 ? left[i] >= 0 : e == 1 ? right[i] >= 0 : rear[i] >= 0 || ringCut[i] != null || deepen[i];
                        if (used || extra[i, e] > 0f) continue;
                        Cut hcut = null;
                        float reach = 0f;
                        if (!hugOn || !Hug(houses, i, e, s, out hcut, out reach))
                        {
                            // nothing near to meet: it still grows this far into open land (cut back later at anything there)
                            float open = K(h, s, "openReach", 0f);
                            if (open < 1f || HardLand(h, s) && e != 2) continue;
                            extra[i, e] = open;
                            touched[i] = true;
                            continue;
                        }
                        extra[i, e] = reach + 2f;
                        cuts[i].Add(hcut);
                        hugs.Add((i, e, hcut));
                        if (e == 2) backHug[i] = hcut;
                        touched[i] = true;
                    }
                }
            if (hugs.Count > 0) plan.Notes.Add($"{hugs.Count} garden sides reach out to a road, path or building");
            float2 FrontMid(StitchHouse h) => h.Position + h.Forward * (h.HalfLot.y - 0.5f);
            var kept = new float2[n][];
            for (int i = 0; i < n; i++)
            {
                if (!Pol(i)) { kept[i] = Lot(houses[i]); continue; }   // an unpolished lot is kept whole
                var poly = Lot(houses[i]).ToList();
                foreach (var c in cuts[i]) poly = ClipHalfPlane(poly, c.P, c.D, c.Keep ?? FrontMid(houses[i]));
                kept[i] = poly.Count >= 3 && Poly.Area(poly) > 1f ? poly.ToArray() : Lot(houses[i]);
            }
            float sideReach = n > 0 ? houses.Max(h => K(h, s, "sideReach", 100f)) : 40f;
            var plots = new float2[n][];
            // first every garden as far as it reaches (its neighbours' middle lines cut it already)
            var raw = new List<float2>[n];
            // each garden's reach for the emergent split: as far as it may grow, cut only by what is no neighbour's line
            var reachR = emergent ? new List<float2>[n] : null;
            // and as far as it may grow into land another garden reaches too (land between houses is shared, wherever it is)
            var reachW = emergent ? new List<float2>[n] : null;
            // the same garden not ended by the street behind it: what the spike test below measures (a street at an angle to the
            // lot's back narrows the garden toward its end, which is no spike)
            var unended = new List<float2>[n];
            bool streetEnd = s.Knob("streetEnd", 1f) > 0.5f;
            for (int i = 0; i < n; i++)
            {
                var h = houses[i];
                if (!touched[i]) continue;
                float reachH = K(h, s, "sideReach", 100f);
                float xl = -h.HalfLot.x - (left[i] >= 0 ? reachH : extra[i, 0]);
                float xr = h.HalfLot.x + (right[i] >= 0 ? reachH : extra[i, 1]);
                float zb = -h.HalfLot.y - math.max(math.max(deepen[i] ? K(h, s, "maxRearGap", 60f) : 0f, extra[i, 2]), rowDeepen[i]);
                // in front of the lot as far as its street: where the street curves away from the lot's front line, the
                // triangles between the lot's front corners and the kerb were nobody's (the user: "it kind of has to play to its
                // back"). Only for a lot whose street lies right in front of it: the street, cut back below, ends it. Only paved
                // land (a car park, a yard): a garden's side fences carried in front of the lot met the neighbours' fences there
                // with gaps (replay of a test town's passes: 18 gaps, 32 with gardens reaching forward)
                float zf = h.HalfLot.y + (HardLand(h, s) && FacesStreet(h) ? K(h, s, "frontReach", 15f) : 0f);
                var rect = new List<float2> { h.ToWorld(new float2(xl, zb)), h.ToWorld(new float2(xr, zb)), h.ToWorld(new float2(xr, zf)), h.ToWorld(new float2(xl, zf)) };
                var poly = rect;
                foreach (var c in cuts[i]) poly = ClipHalfPlane(poly, c.P, c.D, c.Keep ?? FrontMid(h));
                raw[i] = poly;
                if (emergent)
                {
                    // (a side with a neighbour: the land between is shared by nearness, emergentReach is plenty; back as far as
                    // another lot behind it, met or not: whoever is nearer takes the land between)
                    float gw = K(h, s, "emergentReach", 40f);
                    float xlE = left[i] >= 0 ? -h.HalfLot.x - gw : xl, xrE = right[i] >= 0 ? h.HalfLot.x + gw : xr, zbE = zb;
                    float gapE = K(h, s, "maxRearGap", 60f);
                    if (!deepen[i] && LotBehind(houses, i, gapE, left[i], right[i])) zbE = math.min(zb, -h.HalfLot.y - gapE);
                    var r = new List<float2> { h.ToWorld(new float2(xlE, zbE)), h.ToWorld(new float2(xrE, zbE)), h.ToWorld(new float2(xrE, zf)), h.ToWorld(new float2(xlE, zf)) };
                    foreach (var c in cuts[i]) if (!c.Pair || c.Side) r = ClipHalfPlane(r, c.P, c.D, c.Keep ?? FrontMid(h));
                    reachR[i] = r;
                    TraceWhy?.Invoke($"reach {h.Id} back {-zbE - h.HalfLot.y:F0} (rear {deepen[i]}, behind {!deepen[i] && zbE < zb}, extra {extra[i, 2]:F0}, row {rowDeepen[i]:F0}, hug {backHug[i] != null}) sides {-xlE - h.HalfLot.x:F0}/{xrE - h.HalfLot.x:F0}");
                    float xlw = math.min(xlE, -h.HalfLot.x - gw), xrw = math.max(xrE, h.HalfLot.x + gw), zbw = zbE;   // (sideways only: two side neighbours both reaching back would take the open land behind a row)
                    var wide = new List<float2> { h.ToWorld(new float2(xlw, zbw)), h.ToWorld(new float2(xrw, zbw)), h.ToWorld(new float2(xrw, zf)), h.ToWorld(new float2(xlw, zf)) };
                    foreach (var c in cuts[i]) if (!c.Pair || c.Side) wide = ClipHalfPlane(wide, c.P, c.D, c.Keep ?? FrontMid(h));
                    reachW[i] = wide;
                }
                if (!streetEnd || backHug[i] == null) continue;
                // (a free side hugging the same street: a row's end lot meets it with its side and its back)
                var o = rect;
                foreach (var c in cuts[i]) if (!AlongStreet(c, backHug[i])) o = ClipHalfPlane(o, c.P, c.D, c.Keep ?? FrontMid(h));
                unended[i] = o;
            }
            static bool AlongStreet(Cut c, Cut street) =>
                c == street || math.abs(c.D.x * street.D.y - c.D.y * street.D.x) < 0.09f && math.abs(street.D.x * (c.P.y - street.P.y) - street.D.y * (c.P.x - street.P.x)) < 2f;
            var rawLo = new float2[n];
            var rawHi = new float2[n];
            for (int i = 0; i < n; i++)
            {
                IReadOnlyList<float2> r = raw[i] ?? (IReadOnlyList<float2>)kept[i];
                rawLo[i] = MinOf(r);
                rawHi[i] = MaxOf(r);
            }
            for (int i = 0; i < n; i++)
            {
                var h = houses[i];
                var lot = Lot(h);
                if (!touched[i]) { plots[i] = lot; continue; }
                var poly = raw[i];
                // never into another house's lot, nor into land another garden reaches too (a house it is not joined with):
                // the land between them is split down the middle, each keeps its side
                for (int j = 0; j < n; j++)
                {
                    if (j == i || paired.Contains((math.min(i, j), math.max(i, j)))) continue;
                    if (rawHi[j].x < rawLo[i].x || rawLo[j].x > rawHi[i].x || rawHi[j].y < rawLo[i].y || rawLo[j].y > rawHi[i].y) continue;
                    var o = houses[j];
                    var lotO = kept[j];
                    if (!Overlaps(poly, lotO) && (raw[j] == null || !Overlaps(poly, raw[j]))) continue;
                    var (pa, pb) = Closest(kept[i], lotO);
                    var mid = (pa + pb) * 0.5f;
                    var nrm = math.distance(pa, pb) > 0.05f ? math.normalize(pb - pa) : math.normalizesafe(o.Position - h.Position);
                    if (math.distance(pa, pb) <= 0.05f) mid = (h.Position + o.Position) * 0.5f;
                    // two lots on one grid meeting at a corner: split along the grid's axis nearest the way between them, not
                    // square to it (a slanted middle line made the 45° tapers of gardens growing into an open square from
                    // two sides; the user, 23 Oct: "they taper off and then they flatten out"; knob squareSplits 0: as before)
                    if (squareSplits && OnOneGrid(h, o, out var gu, out var gv))
                        nrm = math.abs(math.dot(nrm, gu)) >= math.abs(math.dot(nrm, gv)) ? gu * math.sign(math.dot(nrm, gu)) : gv * math.sign(math.dot(nrm, gv));
                    poly = ClipHalfPlane(poly, mid, new float2(-nrm.y, nrm.x), h.Position);
                    if (unended[i] != null) unended[i] = ClipHalfPlane(unended[i], mid, new float2(-nrm.y, nrm.x), h.Position);
                    TraceWhy?.Invoke($"split {h.Id} {o.Id} at {mid.x:F1},{mid.y:F1} line {math.degrees(math.atan2(-nrm.y, nrm.x)):F0}° (closest {pa.x:F1},{pa.y:F1} - {pb.x:F1},{pb.y:F1})");
                }
                plots[i] = poly.Count >= 3 && Poly.Area(poly) > 1f ? poly.ToArray() : lot;
            }
            if (emergent) Emergent(houses, plots, touched, reachR, reachW, alleys, s, plan);
            if (Trace != null)
            {
                Trace("grown", raw.Select(r => r?.ToArray()).ToArray());
                Trace("split", plots.Select(p => p?.ToArray()).ToArray());
            }
            // a garden never runs out into a spike (on the inside of a curve its sides close in behind it): it ends where it has
            // become narrower than half its lot (at least minGardenWidth); the land past that is nobody's (FillGaps never sees it).
            // Only its sides count: a garden that ends along a street behind it at an angle narrows toward that end too, and was
            // cut square there, a grass triangle left between every back fence and the sidewalk (the user, 22 Oct: "WHERE DO
            // THESE COME FROM?"). Its end is the street: measured as if the street were not there, as far back as it reaches
            int narrowed = 0;
            for (int i = 0; i < n; i++)
            {
                if (!touched[i] || plots[i] == null || raw[i] == null || emergent) continue;
                var h = houses[i];
                float minW = math.max(K(h, s, "minGardenWidth", 8f), h.HalfLot.x);
                float? z = unended[i] != null && unended[i].Count >= 3 ? NarrowsAt(unended[i], h, minW, plots[i].Min(p => math.dot(p - h.Position, h.Forward))) : NarrowsAt(plots[i], h, minW);
                if (z == null) continue;
                var p = h.ToWorld(new float2(0f, z.Value));
                var cutPlot = ClipHalfPlane(plots[i].ToList(), p, h.Right, h.Position);
                if (cutPlot.Count < 3 || Poly.Area(cutPlot) < 1f) continue;
                plots[i] = cutPlot.ToArray();
                raw[i] = ClipHalfPlane(raw[i], p, h.Right, h.Position);
                narrowed++;
            }
            if (narrowed > 0) plan.Notes.Add($"{narrowed} gardens end where they get narrow (no spikes)");
            // nor up a cliff: each side grown past the lot ends (a straight line across it) where the land gets steeper than
            // maxSlope or further than maxRise above or below the house
            int sloped = 0;
            if (s.Ground != null)
                for (int i = 0; i < n; i++)
                {
                    if (!touched[i] || plots[i] == null) continue;
                    // (a side joined to its neighbour is not cut: the land between them stays one, steep or not; the user, 23 Oct: "very
                    // inclined to have continuity ... even if it slightly means breaking their own rule set on ... steepness")
                    bool joinedOn = s.Knob("slopeJoined", 0f) > 0.5f;
                    var cut = SlopeCut(houses[i], plots[i], s, joinedOn || left[i] < 0, joinedOn || right[i] < 0);
                    if (cut == null) continue;
                    plots[i] = cut;
                    if (raw[i] != null) raw[i] = ClipToPlot(raw[i], houses[i], cut);
                    sloped++;
                }
            if (sloped > 0) plan.Notes.Add($"{sloped} gardens end where the land gets too steep or too high");
            void T(string name) { if (Trace != null) Trace(name, plots.Select(p => p?.ToArray()).ToArray()); }
            T("narrow+slope");
            var stepSplits = new List<(int I, int J, bool AFront, float2 Pa, float2 Pb, float2 S, float2 Inward, uint Hash)>();
            foreach (var (si, sj, aFront, deep, ph) in steps)
            {
                var a = houses[si];
                var b = houses[sj];
                var wedge = new List<float2> { a.FrontRight, b.FrontLeft, BackAt(b, -1, deep), BackAt(a, +1, deep) };
                var m = math.normalizesafe(a.Forward + b.Forward, a.Forward);
                var front = (a.FrontRight + b.FrontLeft) * 0.5f;
                float frac = 0.42f + (ph >> 12 & 15) / 15f * 0.2f;
                var split = front - m * (2f * deep * frac);
                var dir = new float2(-m.y, m.x);
                var frontPart = ClipHalfPlane(wedge, split, dir, front);
                var backPart = ClipHalfPlane(wedge, split, dir, front - m * (4f * deep));
                int ft = aFront ? si : sj, bt = aFront ? sj : si;
                plots[ft] = Subtract(plots[ft], backPart);
                plots[bt] = Subtract(plots[bt], frontPart);
                // where the step meets each house's lot line
                var pa = LineHit(split, dir, a.FrontRight, a.Forward);
                var pb = LineHit(split, dir, b.FrontLeft, b.Forward);
                stepSplits.Add((si, sj, aFront, pa, pb, split, -m, ph));
            }

            T("steps");
            Stage("plots");
            foreach (var (l, m, r, ext) in wraps)
            {
                var h = houses[m];
                float hx = h.HalfLot.x, hz = h.HalfLot.y;
                // each half reaches a little past the lot's side, so no sliver stays between the halves and the neighbours
                var leftHalf = new[] { h.ToWorld(new float2(-hx - 6f, -hz)), h.ToWorld(new float2(0f, -hz)), h.ToWorld(new float2(0f, -hz - ext)), h.ToWorld(new float2(-hx - 6f, -hz - ext)) };
                var rightHalf = new[] { h.ToWorld(new float2(0f, -hz)), h.ToWorld(new float2(hx + 6f, -hz)), h.ToWorld(new float2(hx + 6f, -hz - ext)), h.ToWorld(new float2(0f, -hz - ext)) };
                plots[l] = Grow(plots, l, leftHalf);
                plots[r] = Grow(plots, r, rightHalf);
            }
            // a walkway runs from the street to the shallower lot's back; behind it the two gardens meet on the middle line, as
            // side neighbours do (the user, 22 Oct: their hedges ran on side by side down to the back street, a grass strip
            // between, "who would want two fences running parallel to each other?")
            var walkwayEnds = new List<(int I, int J, float Hz)>();
            foreach (var (ai, bi) in s.Knob("walkwayMeet", 0f) > 0.5f && !emergent ? alleys : new List<(int, int)>())
            {
                var a = houses[ai];
                var b = houses[bi];
                float hz = math.min(a.HalfLot.y, b.HalfLot.y);
                var ea = BackAt(a, +1, hz);
                var eb = BackAt(b, -1, hz);
                if (!SideBack(plots[ai], a, +1, out var ba) || !SideBack(plots[bi], b, -1, out var bb)) continue;
                if (math.dot(ea - ba, a.Forward) < 1f || math.dot(eb - bb, b.Forward) < 1f) continue;   // neither reaches past it
                var m0 = (ea + eb) * 0.5f;
                var m1 = (ba + bb) * 0.5f;
                plots[ai] = Grow(plots, ai, new[] { ea, m0, m1, ba });
                plots[bi] = Grow(plots, bi, new[] { m0, eb, bb, m1 });
                walkwayEnds.Add((ai, bi, hz));
            }

            // gardens never reach onto a road, path or track, nor another building's lot: cut back just clear of them (the
            // house's own lot is never cut), the part that holds the house kept
            if (s_barriers.Count > 0)
            {
                int clipped = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!touched[i] || plots[i] == null) continue;
                    // a garden another pass wrote is its garden (FixedGardens): what this plan would have made of it is never used
                    if (IsFixed(houses[i])) continue;
                    var clear = ClearOfBarriers(plots[i], Lot(houses[i]), houses[i].Position, s.Knob("barrierMargin", 0.8f), WaterGap(houses[i], s), DeckMargin(s));
                    if (clear == null) continue;
                    plots[i] = clear;
                    clipped++;
                }
                if (clipped > 0) plan.Notes.Add($"{clipped} gardens cut back clear of roads, paths, buildings and water");
            }
            T("clear");
            Stage("clear");
            // (fillGapsLate: the gaps are filled once the gardens round a block's corner have shared their land, below; filled
            // here, the land between them went in spokes to every garden around it)
            bool gapsLate = s.Knob("fillGapsLate", 1f) > 0.5f;
            if (!gapsLate && s.Knob("fillGaps", 0f) > 0.5f) FillGaps(houses, plots, touched, raw, kept, s, plan);
            Stage("gaps");
            FixedGardens(houses, plots, touched, plan);
            T("gaps+fixedGardens");
            Stage("fixedGardens");
            if (s.Knob("shareBetween", 0f) > 0.5f && !emergent)
            {
                foreach (var kv in sideCuts) pairCuts[kv.Key] = kv.Value;
                ShareBetween(houses, plots, touched, left, right, ringCut, pairCuts, alleys, s, plan);
            }
            T("share");
            Stage("share");
            if (s.Knob("separate", 1f) > 0.5f) Separate(houses, plots, touched, plan);
            T("separate");
            if (s.Knob("trimSlivers", 1f) > 0.5f && !emergent) TrimSlivers(houses, plots, touched, s, plan);
            T("trimSlivers");
            if (gapsLate && s.Knob("fillGaps", 0f) > 0.5f && !emergent) FillGaps(houses, plots, touched, raw, kept, s, plan);
            T("fillGaps");
            if (s.Knob("capNarrow", 1f) > 0.5f && !emergent) CapNarrow(houses, plots, touched, s, plan);
            T("capNarrow");
            if (s.Knob("straightBacks", 1f) > 0.5f && !emergent)
            {
                // (a garden that shares its back or front with another lot keeps that line)
                var sharesEnd = new bool[n];
                foreach (var key in pairCuts.Keys) if (!sideCuts.ContainsKey((math.min(key.Item1, key.Item2), math.max(key.Item1, key.Item2)))) { sharesEnd[key.Item1] = true; sharesEnd[key.Item2] = true; }
                for (int i = 0; i < n; i++) if (deepen[i] || ringCut[i] != null) sharesEnd[i] = true;
                StraightBacks(houses, plots, touched, left, right, sideCuts, sharesEnd, s, plan);
            }
            T("straightBacks");
            Stage("fixed");

            // ── 3. fences on plot edges ──
            var moved = new Dictionary<long, (float2 A, float2 B)>();
            var placed = new HashSet<long>();   // lines laid on a shared edge: they belong to two plots
            var stepBeds = new List<(int House, float2 A, float2 B, float2 Inward)>();
            foreach (var (si, sj, aFront, pa, pb, split, inward, ph) in stepSplits)
            {
                var a = houses[si];
                var b = houses[sj];
                var ar = SideLines(a, +1, s).OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                var bl = SideLines(b, -1, s).OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                foreach (var l in SideLines(a, +1, s).Concat(SideLines(b, -1, s))) if (l.Id != ar.Id && l.Id != bl.Id) plan.HideLines.Add(l.Id);
                // the front taker's neighbour keeps its line on its own lot from the street to the step; the front taker's
                // line runs on its own lot from the step to the back
                var (aSeg, bSeg) = aFront ? ((pa, a.BackRight), (b.FrontLeft, pb)) : ((a.FrontRight, pa), (pb, b.BackLeft));
                moved[ar.Id] = Along(ar, aSeg.Item1, aSeg.Item2);
                moved[bl.Id] = Along(bl, bSeg.Item1, bSeg.Item2);
                placed.Add(ar.Id);
                placed.Add(bl.Id);
                // the step itself: a row of bushes in the back taker's garden
                stepBeds.Add((aFront ? sj : si, pa, pb, inward));
                plan.Pairs.Add(new StitchPair { Left = a.Id, Right = b.Id, Front = (a.FrontRight + b.FrontLeft) * 0.5f, Back = split, FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(a.BackRight, b.BackLeft) });
                plan.Notes.Add($"{a.Id}/{b.Id}: the gap split, {(aFront ? a.Id : b.Id)} takes the front");
            }
            foreach (var (ai, bi) in alleys)
            {
                var a = houses[ai];
                var b = houses[bi];
                // a walkway between the two, from the street to the shallower house's back; their own fences stay
                float hz = math.min(a.HalfLot.y, b.HalfLot.y);
                var ring = Ccw(new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, hz), BackAt(a, +1, hz) });
                var inset = Poly.Offset(ring, 0.35f).OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                var path = inset == null ? null : Prepared(inset);
                if (path != null && path.Length >= 3) plan.Paths.Add((a.Id, path, s.Knob("alleyTiles", 0f) > 0.5f ? "Tiles Surface 01" : "Pavement Surface 01"));
                plan.Pairs.Add(new StitchPair { Left = a.Id, Right = b.Id, Rear = true, FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(a.BackRight, b.BackLeft) });
                plan.Notes.Add($"{a.Id}/{b.Id}: a walkway between them");
            }
            foreach (var kv in borders)
            {
                var (i, j) = kv.Key;
                var a = houses[i];
                var b = houses[j];
                float deep = Deep(a, b);
                var pair = new StitchPair
                {
                    Left = a.Id, Right = b.Id,
                    FrontGap = math.distance(a.FrontRight, b.FrontLeft), BackGap = math.distance(BackAt(a, +1, deep), BackAt(b, -1, deep)),
                    Gap = new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, deep), BackAt(a, +1, deep) },
                };
                plan.Pairs.Add(pair);
                if (edgeBorders.TryGetValue(kv.Key, out int fx))
                {
                    // yes meets no: the polished garden reaches the unpolished lot's edge; that lot's own fence on that side
                    // is the border (the polished one's duplicate side line goes), else the polished one's line goes onto the edge
                    int yi = fx == i ? j : i;
                    var nh = houses[fx];
                    var yh = houses[yi];
                    pair.Edge = true;
                    if (!EdgeOn(plots[yi], kv.Value, out float e0, out float e1)) continue;
                    pair.Front = kv.Value.P + kv.Value.D * e0;
                    pair.Back = kv.Value.P + kv.Value.D * e1;
                    var theirs = SideLines(nh, fx == i ? +1 : -1, s).ToList();
                    var mine = SideLines(yh, yi == i ? +1 : -1, s).ToList();
                    if (theirs.Count > 0)
                    {
                        foreach (var l in mine) plan.HideLines.Add(l.Id);
                        pair.KeptLine = theirs.OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First().Id;
                        plan.Notes.Add($"{yh.Id}: its garden reaches unpolished {nh.Id}'s lot edge and leans on its {theirs[0].Name}");
                        continue;
                    }
                    if (mine.Count == 0) { plan.Notes.Add($"{yh.Id}: its garden reaches unpolished {nh.Id}'s lot edge (no fence on either side)"); continue; }
                    var line = mine.OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                    pair.KeptLine = line.Id;
                    foreach (var l in mine) if (l.Id != line.Id) plan.HideLines.Add(l.Id);
                    if (pair.FrontGap >= s.TouchingGap || pair.BackGap >= s.TouchingGap)
                    {
                        moved[line.Id] = math.distance(line.A, pair.Front) + math.distance(line.B, pair.Back) <= math.distance(line.B, pair.Front) + math.distance(line.A, pair.Back) ? (pair.Front, pair.Back) : (pair.Back, pair.Front);
                        placed.Add(line.Id);
                    }
                    plan.Notes.Add($"{yh.Id}: its garden reaches unpolished {nh.Id}'s lot edge, its own {line.Name} on that edge");
                    continue;
                }
                bool ea = EdgeOn(plots[i], kv.Value, out float ta0, out float ta1);
                bool eb = EdgeOn(plots[j], kv.Value, out float tb0, out float tb1);
                if (!ea && !eb) continue;
                float t0 = math.min(ea ? ta0 : tb0, eb ? tb0 : ta0), t1 = math.max(ea ? ta1 : tb1, eb ? tb1 : ta1);
                pair.Front = kv.Value.P + kv.Value.D * t0;
                pair.Back = kv.Value.P + kv.Value.D * t1;
                var all = SideLines(a, +1, s).Select(l => (a, l)).Concat(SideLines(b, -1, s).Select(l => (b, l))).ToList();
                if (all.Count == 0) continue;
                var keep = all.OrderByDescending(x => Score(x.l.Name)).ThenByDescending(x => math.distance(x.l.A, x.l.B)).First();
                pair.KeptLine = keep.l.Id;
                foreach (var x in all) if (x.l.Id != keep.l.Id) plan.HideLines.Add(x.l.Id);
                if (pair.FrontGap < s.TouchingGap && pair.BackGap < s.TouchingGap) continue;   // lots touch: only the duplicate goes
                // keep the line's direction: its end nearer the street goes to the street end
                float da = math.distance(keep.l.A, pair.Front) + math.distance(keep.l.B, pair.Back);
                float db = math.distance(keep.l.B, pair.Front) + math.distance(keep.l.A, pair.Back);
                moved[keep.l.Id] = da <= db ? (pair.Front, pair.Back) : (pair.Back, pair.Front);
                placed.Add(keep.l.Id);
            }
            // around a shallow lot: the lines shared with it stop at its back corners, and a line (one the stitching freed) runs
            // down the middle behind it, between the two deep neighbours
            foreach (var (l, m, r, ext) in wraps)
            {
                var h = houses[m];
                foreach (var (a, b, corner) in new[] { (l, m, h.BackLeft), (m, r, h.BackRight) })
                {
                    var pair = plan.Pairs.FirstOrDefault(p => !p.Rear && p.Left == houses[a].Id && p.Right == houses[b].Id);
                    if (pair == null || pair.KeptLine < 0 || plan.HideLines.Contains(pair.KeptLine)) continue;
                    var line = houses[a].Lines.Concat(houses[b].Lines).FirstOrDefault(x => x.Id == pair.KeptLine);
                    if (line == null) continue;
                    var cur = moved.TryGetValue(line.Id, out var mv) ? mv : (line.A, line.B);
                    var dl = cur.B - cur.A;
                    float t = math.saturate(math.dot(corner - cur.A, dl) / math.max(1e-6f, math.dot(dl, dl)));
                    var at = cur.A + dl * t;
                    bool aIsStreet = math.dot(cur.A - h.Position, h.Forward) > math.dot(cur.B - h.Position, h.Forward);
                    moved[line.Id] = aIsStreet ? (cur.A, at) : (at, cur.B);
                    placed.Add(line.Id);
                    pair.Back = at;
                }
                var from = h.ToWorld(new float2(0f, -h.HalfLot.y));
                var to = h.ToWorld(new float2(0f, -h.HalfLot.y - ext));
                var spare = new[] { m, l, r }.SelectMany(k => houses[k].Lines.Where(x => plan.HideLines.Contains(x.Id))).OrderByDescending(x => Score(x.Name)).FirstOrDefault();
                if (spare != null)
                {
                    plan.HideLines.Remove(spare.Id);
                    moved[spare.Id] = (from, to);
                    placed.Add(spare.Id);
                }
                plan.Notes.Add($"{houses[l].Id} and {houses[r].Id} meet behind {h.Id}");
            }
            // the walkway's own fences stop where it ends (the gardens close the rest: across its end, down the middle)
            var stay = new HashSet<long>();
            foreach (var (ai, bi, hz) in walkwayEnds)
            {
                plan.Notes.Add($"{houses[ai].Id}/{houses[bi].Id}: their gardens meet behind the walkway");
                foreach (var (k, side) in new[] { (ai, +1), (bi, -1) })
                {
                    var h = houses[k];
                    float zEnd = h.HalfLot.y - 2f * hz;
                    foreach (var l in SideLines(h, side, s).ToList())
                    {
                        if (plan.HideLines.Contains(l.Id)) continue;
                        var (p, q) = moved.TryGetValue(l.Id, out var mv) ? mv : (l.A, l.B);
                        var lp = h.ToLocal(p);
                        var lq = h.ToLocal(q);
                        if (math.max(lp.y, lq.y) < zEnd + 0.5f) { plan.HideLines.Add(l.Id); continue; }
                        stay.Add(l.Id);
                        if (math.min(lp.y, lq.y) >= zEnd - 0.05f) continue;
                        moved[l.Id] = (lp.y < zEnd ? h.ToWorld(new float2(lp.x, zEnd)) : p, lq.y < zEnd ? h.ToWorld(new float2(lq.x, zEnd)) : q);
                    }
                }
            }
            // a side facing another house's side or back: its line onto the plot's edge on the middle line (overlaps go below)
            foreach (var c in facing)
                foreach (var (k, e) in new[] { (c.I, c.EI), (c.J, c.EJ) })
                {
                    if (e == 2 || !Pol(k)) continue;
                    var h = houses[k];
                    var lines = SideLines(h, e == 0 ? -1 : +1, s).Where(l => !plan.HideLines.Contains(l.Id)).ToList();
                    if (lines.Count == 0 || !EdgeOn(plots[k], c.Cut, out float t0, out float t1)) continue;
                    var line = lines.OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                    var p0 = c.Cut.P + c.Cut.D * t0;
                    var p1 = c.Cut.P + c.Cut.D * t1;
                    moved[line.Id] = math.distance(line.A, p0) + math.distance(line.B, p1) <= math.distance(line.A, p1) + math.distance(line.B, p0) ? (p0, p1) : (p1, p0);
                    foreach (var l in lines) if (l.Id != line.Id) plan.HideLines.Add(l.Id);
                }
            // a side reaching out to a road: its line onto the plot's edge along the road
            foreach (var (k, e, hc) in hugs)
            {
                if (e == 2) continue;
                var h = houses[k];
                var lines = SideLines(h, e == 0 ? -1 : +1, s).Where(l => !plan.HideLines.Contains(l.Id) && !moved.ContainsKey(l.Id)).ToList();
                if (lines.Count == 0 || !EdgeOn(plots[k], hc, out float t0, out float t1)) continue;
                var line = lines.OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).First();
                var p0 = hc.P + hc.D * t0;
                var p1 = hc.P + hc.D * t1;
                moved[line.Id] = math.distance(line.A, p0) + math.distance(line.B, p1) <= math.distance(line.A, p1) + math.distance(line.B, p0) ? (p0, p1) : (p1, p0);
                foreach (var l in lines) if (l.Id != line.Id) plan.HideLines.Add(l.Id);
            }
            // (the emergent split: each house's own lines onto the edges of its garden as it is, instead of the back line and the
            // no-line-leaves-its-plot steps below, which follow the lines the gardens used to be cut on)
            if (emergent) SnapLines(houses, plots, touched, plan, moved, placed, stay, s);
            // each house's back line on its plot's back edge, when that edge is not the lot's own
            for (int i = 0; i < n; i++)
            {
                if (!touched[i] || emergent) continue;
                var h = houses[i];
                var backs = EdgeLines(h, back: true, s).Where(l => !plan.HideLines.Contains(l.Id)).ToList();
                if (backs.Count == 0) continue;
                if (!BackEdge(h, plots[i], out var e0, out var e1)) { foreach (var l in backs) plan.HideLines.Add(l.Id); continue; }
                bool e0Left = h.ToLocal(e0).x < h.ToLocal(e1).x;
                var el = e0Left ? e0 : e1;
                var er = e0Left ? e1 : e0;
                if (math.distance(el, h.BackLeft) < 0.3f && math.distance(er, h.BackRight) < 0.3f) continue;
                var line = backs.OrderByDescending(l => math.distance(l.A, l.B)).First();
                moved[line.Id] = h.ToLocal(line.A).x < h.ToLocal(line.B).x ? (el, er) : (er, el);
                foreach (var l in backs) if (l.Id != line.Id) plan.HideLines.Add(l.Id);
            }
            // a line ending on the lot's edge where the plot reaches further carries on to the plot's edge
            for (int i = 0; i < n; i++)
            {
                if (!touched[i]) continue;
                var h = houses[i];
                foreach (var l in h.Lines)
                {
                    if (moved.ContainsKey(l.Id) || plan.HideLines.Contains(l.Id) || stay.Contains(l.Id)) continue;
                    var next = (l.A, l.B);
                    foreach (bool atA in new[] { true, false })
                    {
                        var end = atA ? next.Item1 : next.Item2;
                        var from = atA ? next.Item2 : next.Item1;
                        // the line reaches the lot's edge here, and carried on it would run further inside the plot
                        float back = s.EdgeTolerance / math.max(0.5f, math.distance(from, end));
                        if (!OnLotEdge(h, end, s.EdgeTolerance) || !RayToPlotEdge(Lot(h), from, end, out var lotHit, 1f - back) || math.distance(lotHit, end) > s.EdgeTolerance) continue;
                        if (!RayToPlotEdge(plots[i], from, end, out var hit, 1f - back) || math.dot(hit - lotHit, end - from) < 0.5f * math.distance(from, end)) continue;
                        if (atA) next.Item1 = hit; else next.Item2 = hit;
                    }
                    if ((next.Item1.Equals(l.A) && next.Item2.Equals(l.B)) || !NaturalStretch((l.A, l.B), next, s)) continue;
                    moved[l.Id] = next;
                }
            }
            // no line leaves its own plot (a shared edge's line belongs to both)
            for (int i = 0; i < n; i++)
            {
                if (!touched[i] || emergent) continue;
                var grown = Poly.Offset(Ccw(plots[i]), -0.3f);
                if (grown.Count == 0) continue;
                var ring = grown.OrderByDescending(r => Poly.Area(r)).First();
                foreach (var l in houses[i].Lines)
                {
                    if (placed.Contains(l.Id) || plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    float len = math.distance(a, b);
                    if (len < 0.5f) continue;
                    var inside = Poly.ClipSegmentIntervals(ring, a, b);
                    float2 best = default;
                    foreach (var iv in inside) if (iv.y - iv.x > best.y - best.x) best = iv;
                    if (best.x <= 0.001f && best.y >= 0.999f) continue;
                    if ((best.y - best.x) * len < s.Knob("minLine", 2f)) { plan.HideLines.Add(l.Id); continue; }
                    moved[l.Id] = (math.lerp(a, b, best.x), math.lerp(a, b, best.y));
                }
            }
            Stage("lines");
            Dedupe(houses, touched, plan, moved, s);
            Stage("dedupe");

            // a garden open at the back (no line of anyone's along its back edge) closes with a line of its own that the
            // stitching freed (a duplicate side line it hid, its own or a side neighbour's): every garden ends in a fence or hedge
            // (the lines as they will stand, made once and kept up to date as freed lines come back: a list of every line made
            // for every garden was a tenth of a plan's garbage)
            List<(float2 A, float2 B)> drawnNow = null;
            for (int i = 0; i < n; i++)
                {
                    if (!touched[i] || emergent || K(houses[i], s, "closeBacks", 1f) < 0.5f || !BackEdge(houses[i], plots[i], out var e0, out var e1) || math.distance(e0, e1) < 3f) continue;
                    var h = houses[i];
                    drawnNow ??= DrawnLines(houses, plan, moved);
                    if (Covered(e0, e1, drawnNow) >= 0.6f * math.distance(e0, e1)) continue;
                    // its own first, else one its side neighbours freed (the duplicate on their shared side)
                    var (spareOf, spare) = new[] { i, left[i], right[i] }.Where(k => k >= 0 && Pol(k)).SelectMany(k => houses[k].Lines.Where(l => plan.HideLines.Contains(l.Id)).OrderByDescending(l => Score(l.Name)).ThenByDescending(l => math.distance(l.A, l.B)).Select(l => (k, l))).FirstOrDefault();
                    if (spare == null) continue;
                    plan.HideLines.Remove(spare.Id);
                    bool e0Left = h.ToLocal(e0).x < h.ToLocal(e1).x;
                    moved[spare.Id] = e0Left ? (e0, e1) : (e1, e0);
                    // (drawn from now on, as DrawnLines has it: a neighbour another pass planned is drawn as it stands)
                    var so = houses[spareOf];
                    if (!(so.Polished && !so.Edit && so.Standing != null)) drawnNow.Add(moved[spare.Id]);
                    plan.Notes.Add($"{h.Id}: its open back closed with a freed {spare.Name}");
                }

            // a moved line never runs through a house, a shed, a pool or a deck, nor across a driveway: it stops there
            Stage("fences");
            TrimAtObstacles(houses, plan, moved, s);
            Stage("trimObstacles");
            LinesOnEdges(houses, plots, touched, plan, moved, s);
            Stage("linesOnEdges");
            // no two lines on one another before the open edges are closed: a line taken away after that left a gap
            Dedupe(houses, touched, plan, moved, s);
            Stage("dedupe2");
            CloseGardens(houses, plots, touched, plan, moved, s);
            if (emergent) JoinLooseEnds(houses, plots, touched, plan, moved, s);
            Stage("trim");

            // planted beds with bushes along the new fences and hedges of extended gardens
            var beds = new List<Region>[n];
            for (int i = 0; i < n; i++) beds[i] = new List<Region>();
            var reserved = new List<Region>[n];   // features' ground (a sand pit): never under a lawn, kept through bed fitting
            for (int i = 0; i < n; i++) reserved[i] = new List<Region>();
            // planting, features, paths and rocks: each house as it was polished (its own options), else the settings
            if (houses.Any(h => h.Edit && K(h, s, "plant", 1f) > 0.5f))
            {
                var built = new Obstacle(houses);
                foreach (var (hi, a, b, inward) in stepBeds) if (houses[hi].Edit && K(houses[hi], s, "plant", 1f) > 0.5f) StepRow(houses[hi], plots[hi], a, b, inward, plan, beds[hi], built, s);
                Plant(houses, plots, touched, plan, moved, beds, built, s);
                Stage("beds");
                var fenceLines = DrawnLines(houses, plan, moved)
                    .Concat(plan.FencePieces.Select(p => { var d = new float2(math.sin(p.Yaw), math.cos(p.Yaw)) * 2.1f; return (p.Position - d, p.Position + d); })).ToList();
                Features(houses, plots, touched, plan, beds, reserved, built, s, fenceLines);
                Stage("features");
                Paths(houses, plots, touched, left, right, plan, beds, built, s);
                Stage("paths");
                Rocks(houses, plots, plan, moved, built, s);
            }

            Stage("plant");
            // ── 4. lawns fill their plots, around driveways and paving ──
            // a small pocket no plot takes (where four corners meet) joins the nearest plot's lawn
            var lawnLand = new List<Region>[n];
            // (only the gardens this plan edits: nothing reads a neighbour's)
            for (int i = 0; i < n; i++) if (houses[i].Edit && houses[i].Polished && plots[i] != null) lawnLand[i] = Poly.Normalize(Ccw(plots[i]));
            // a big garden whose look asks for it ("Lawn reach" under 400 m): mowed lawn near the house, the land further out
            // painted (its look's meadow ground). Not by default: a lawn reaches the fence (the user: "it should always line
            // up with the fence"). Never paved land: it always fills its whole ground.
            for (int i = 0; i < n; i++)
            {
                var h = houses[i];
                float lawnReach = K(h, s, "lawnReach", 1000f);
                if (!touched[i] || !h.Edit || !h.Polished || plots[i] == null || lawnReach >= 400f || HardLand(h, s)) continue;
                var near = Poly.Offset(Ccw(Lot(h)), -math.max(0f, lawnReach), OffsetJoin.Round, 4f, 0.5f);
                if (near.Count == 0) continue;
                var beyond = Poly.Boolean(lawnLand[i], near, BoolOp.Difference);
                if (beyond.Sum(r => r.Area) < 20f) continue;
                lawnLand[i] = Poly.Boolean(lawnLand[i], near, BoolOp.Intersection);
                int layer = (int)K(h, s, "meadowLayer", 1f);
                if (layer > 0 && K(h, s, "paint", 1f) > 0.5f)
                    foreach (var r in beyond.Where(r => r.Area >= 12f))
                        foreach (var piece in r.HasHoles ? Poly.CutHoles(r, 0f) : new List<float2[]> { r.Outer })
                            Paint(plan, Poly.Simplify(piece, 0.3f), layer, K(h, s, "meadowStrength", 0.6f), 3f, 1.5f, 6f, (uint)(h.Id * 40503L) ^ (uint)Poly.Area(piece));
                plan.Notes.Add($"{h.Id}: lawn within {lawnReach:F0} m of the house, {beyond.Sum(r => r.Area):F0} m² painted further out");
            }
            foreach (var region in Poly.UnionAll(Enumerable.Range(0, n).Select(i => (IReadOnlyList<float2>)Ccw(plots[i]))))
                foreach (var hole in region.Holes)
                {
                    if (Poly.Area(hole) > s.Knob("pocketMax", 150f)) continue;
                    var c = Poly.Centroid(hole);
                    int best = -1;
                    float bestD = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (!touched[i]) continue;
                        float d = DistanceToRing(plots[i], c);
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best < 0 || lawnLand[best] == null) continue;
                    lawnLand[best] = Poly.Boolean(lawnLand[best], new[] { new Region(Ccw(hole)) }, BoolOp.Union);
                }
            for (int i = 0; i < n; i++)
            {
                if (!houses[i].Edit || !houses[i].Polished || plots[i] == null) continue;
                var h = houses[i];
                var plot = Ccw(plots[i]);
                if (touched[i]) plan.Plots[h.Id] = plot;
                if (K(h, s, "lawns", 1f) < 0.5f) continue;   // the player keeps lawns as the houses come
                // a painted garden (by its theme), joined to its neighbours or not: the house's own grass surfaces are tucked
                // away under the house and the whole garden (minus the paving) is painted, lush (a yard: worn ground)
                if (!HardLand(h, s) && PaintedGarden(h, s) && h.Lawns.Any(l => l.Ring != null && l.Ring.Length >= 3) | touched[i])
                {
                    var keepOff = houses.SelectMany(o => o.Hard).Where(r => r != null && r.Length >= 3 && Overlaps(plot, r)).Select(r => new Region(Ccw(r))).Concat(beds[i]).Concat(reserved[i]).ToList();
                    var garden = keepOff.Count > 0 ? Poly.Boolean(lawnLand[i], keepOff, BoolOp.Difference) : lawnLand[i];
                    bool yard = ThemeOf(h) == "yard";
                    foreach (var r in garden.Where(r => r.Area >= 6f))
                        Paint(plan, r.Outer, (int)K(h, s, "gardenLayer", yard ? 3f : s.Knob("lushLayer", 2f)), yard ? s.Knob("yardPaint", 0.7f) : K(h, s, "gardenPaint", 0.85f), 1.6f, 1.4f, 5f, (uint)(h.Id * 2246822519L) ^ (uint)Poly.Area(r.Outer));
                    var under = UnderHouse(h);
                    if (under != null && s.Knob("hideOwnLawn", 1f) > 0.5f)
                        foreach (var l in h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3)) plan.Lawns[l.Id] = under;
                    plan.Notes.Add($"{h.Id}: a painted garden ({ThemeOf(h)})");
                    continue;
                }
                if (!touched[i]) continue;
                // once the plot reaches past the lot, the lawn fills all of it but driveways and paving (lawn and fence meet)
                var ext = Poly.Boolean(new[] { (IReadOnlyList<float2>)plot }, new[] { (IReadOnlyList<float2>)Ccw(Lot(h)) }, BoolOp.Difference, FillRule.NonZero);
                if (ext.Sum(r => r.Area) < 1f) continue;
                var hard0 = houses.SelectMany(o => o.Hard).Where(r => r != null && r.Length >= 3 && Overlaps(plot, r)).Select(r => new Region(Ccw(r))).Concat(beds[i]).Concat(reserved[i]).ToList();
                // paved land (a car park yard, or the paving most of a shop's or works' ground already is), not lawn: whatever the
                // building is (a house or a tower given a car park look gets one)
                if (HardLand(h, s))
                {
                    var spare = Poly.Boolean(lawnLand[i], new[] { new Region(Ccw(Lot(h))) }, BoolOp.Difference);
                    float spare0 = spare.Sum(r => r.Area);
                    var off = hard0.Concat(h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).Select(l => new Region(Ccw(l.Ring)))).ToList();
                    if (off.Count > 0) spare = Poly.Boolean(spare, off, BoolOp.Difference);
                    // a car park yard: green edges, a service corner, an overflow car park, green further out
                    if (FillOf(h, s) == PolishLooks.FillCarPark && spare.Sum(r => r.Area) >= K(h, s, "shopYardMin", 80f))
                    {
                        ShopYard(houses, h, plot, spare.Where(r => r.Area >= 1f).ToList(), plan, s);
                        continue;
                    }
                    int made = 0, failed = 0;
                    foreach (var r in spare.Where(r => r.Area >= 10f))
                    {
                        var ring = Prepared(r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer);
                        if (ring != null) { plan.Paths.Add((h.Id, ring, PavingOf(h))); made++; } else failed++;
                    }
                    plan.Notes.Add($"{h.Id}: its extra land in {PavingOf(h)} ({made} pieces{(failed > 0 ? $", {failed} the game would not take" : "")}; land {spare.Sum(r => r.Area):F0} m² in {spare.Count}, {spare0:F0} m² before paving, plot {Poly.Area(plot):F0}, ext {ext.Sum(r => r.Area):F0}, landAll {lawnLand[i].Sum(r => r.Area):F0})");
                    continue;
                }
                var lawns = h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).OrderByDescending(l => Poly.Area(l.Ring)).ToList();
                var hard = houses.SelectMany(o => o.Hard).Where(r => r != null && r.Length >= 3 && Overlaps(plot, r)).Select(r => new Region(Ccw(r))).Concat(beds[i]).Concat(reserved[i]).ToList();
                var pieces = hard.Count > 0 ? Poly.Boolean(lawnLand[i], hard, BoolOp.Difference) : lawnLand[i];
                pieces = pieces.Where(p => p.Area > 0.5f).ToList();
                if (s.Knob("debugLawns", 0f) > 0.5f) plan.Notes.Add($"{h.Id}: land {lawnLand[i].Sum(r => r.Area):F0} m², {pieces.Count} pieces ({string.Join(", ", pieces.Select(p => p.Area.ToString("F0")))}), {lawns.Count} lawns ({string.Join(", ", lawns.Select(l => Poly.Area(l.Ring).ToString("F0")))})");
                // each lawn (largest first) takes the pieces of the plot it lies in; the house's other lawns stay as they are
                foreach (var lawn in lawns)
                {
                    var mine = pieces.Where(p => Overlaps(p.Outer, lawn.Ring)).ToList();
                    if (mine.Count == 0) { plan.Notes.Add($"{h.Id}: lawn not fitted (no free land touches it)"); continue; }
                    pieces.RemoveAll(p => mine.Contains(p));
                    var others = lawns.Where(o => o != lawn).Select(o => new Region(Ccw(o.Ring))).ToList();
                    var merged = Poly.Boolean(new[] { new Region(Ccw(lawn.Ring)) }, mine, BoolOp.Union);
                    if (others.Count > 0 || beds[i].Count > 0) merged = Poly.Boolean(merged, others.Concat(beds[i]), BoolOp.Difference);
                    merged = Poly.Boolean(merged, lawnLand[i], BoolOp.Intersection);
                    if (merged.Count == 0) { plan.Notes.Add($"{h.Id}: lawn not fitted (nothing left of it in its plot)"); continue; }
                    var keep = Keeping(merged, lawn.Ring);
                    // what the other lawns cut off from it is land no lawn reaches yet
                    pieces.AddRange(merged.Where(r => r != keep));
                    var main = OpenHoles(keep, h);
                    var grownLawn = Prepared(main.Outer);
                    bool overBeds = grownLawn != null && beds[i].Count > 0 && Poly.Boolean(new[] { new Region(Ccw(grownLawn)) }, beds[i], BoolOp.Intersection).Sum(r => r.Area) > 0.5f;
                    if (overBeds)
                    {
                        // the lawn's cleaned outline reaches a little over some beds: the beds give way (trimmed round the
                        // lawn; one that loses most of itself goes, with its bushes)
                        FitBedsAround(h, grownLawn, plan, beds[i]);
                        overBeds = false;
                    }
                    if ((grownLawn == null || grownLawn.Length < 3 || overBeds) && beds[i].Count > 0)
                    {
                        // the beds leave a shape the game will not take: the garden goes without them
                        plan.Beds.RemoveAll(b => b.House == h.Id);
                        plan.Bushes.RemoveAll(b => b.House == h.Id);
                        beds[i].Clear();
                        var plain = Poly.Boolean(new[] { new Region(Ccw(lawn.Ring)) }, mine, BoolOp.Union);
                        if (others.Count > 0) plain = Poly.Boolean(plain, others, BoolOp.Difference);
                        plain = Poly.Boolean(plain, lawnLand[i], BoolOp.Intersection);
                        if (plain.Count > 0)
                        {
                            main = OpenHoles(Keeping(plain, lawn.Ring), h);
                            grownLawn = Prepared(main.Outer);
                        }
                        plan.Notes.Add($"{h.Id}: its beds left out (the lawn would not take them{(overBeds ? ": they would sit under it" : "")})");
                    }
                    if (grownLawn == null || grownLawn.Length < 3) { plan.Notes.Add($"{h.Id}: lawn not fitted (the game would not take its shape: {main.Outer.Length} points, {main.Area:F0} m²)"); continue; }
                    if (math.abs(Poly.Area(grownLawn) - Poly.Area(lawn.Ring)) < 0.5f) { plan.Notes.Add($"{h.Id}: lawn unchanged ({Poly.Area(grownLawn):F0} m²)"); continue; }
                    plan.LawnAddedM2 += Poly.Area(grownLawn) - Poly.Area(lawn.Ring);
                    plan.Lawns[lawn.Id] = grownLawn;
                }
                // land no lawn of the house reaches (a gravel-yard house, the far side of a driveway): a lawn of its own
                foreach (var piece in pieces.OrderByDescending(p => p.Area))
                {
                    if (piece.Area < s.Knob("minNewLawn", 20f)) continue;
                    var ring = Prepared(OpenHoles(piece, h).Outer);
                    if (ring == null || ring.Length < 3) continue;
                    plan.NewLawns.Add((h.Id, ring));
                    plan.LawnAddedM2 += Poly.Area(ring);
                }
            }

            // ground paint round every polished house, joined or not: the gardens of a pass as one shape, grown outward with
            // rounded corners into a near band and a wider, fainter one, minus the gardens themselves (lush, soft, uneven)
            var blenders = Enumerable.Range(0, n).Where(i => houses[i].Edit && houses[i].Polished && plots[i] != null && K(houses[i], s, "paint", 1f) > 0.5f && K(houses[i], s, "blendWidth", 20f) > 0.5f).ToList();
            if (blenders.Count > 0)
            {
                var gardens = blenders.Select(i => (IReadOnlyList<float2>)Ccw(plots[i])).ToList();
                var allPlots = Enumerable.Range(0, n).Where(i => plots[i] != null).Select(i => new Region(Ccw(plots[i]))).ToList();
                var joined = gardens.Count > 0 ? Poly.UnionAll(gardens) : new List<Region>();
                // the widest of the pass's looks (the bands are painted once round all of its gardens)
                float near = blenders.Max(i => K(houses[i], s, "blendWidth", 20f)), far = math.max(near, blenders.Max(i => K(houses[i], s, "blendFar", 40f)));
                float nearStrength = blenders.Max(i => K(houses[i], s, "blendStrength", 0.6f));
                int Most(string knob, float fallback) => blenders.GroupBy(i => (int)K(houses[i], s, knob, fallback)).OrderByDescending(g => g.Count()).First().Key;
                int nearLayer = Most("blendLayer", s.Knob("lushLayer", 2f)), farLayer = Most("blendFarLayer", s.Knob("lushLayer", 2f));
                // never painted up to the water: the bands stop half the widest water gap short of it (a beach stays sand)
                float2 glo = new float2(float.MaxValue), ghi = new float2(float.MinValue);
                foreach (var g in joined) { Poly.Bounds(g.Outer, out var a, out var b); glo = math.min(glo, a); ghi = math.max(ghi, b); }
                var wetOff = joined.Count > 0 ? WaterNear(glo - far, ghi + far, 0.5f * blenders.Max(i => WaterGap(houses[i], s))) : new List<Region>();
                var bandOff = wetOff.Count > 0 ? allPlots.Concat(wetOff).ToList() : allPlots;
                foreach (var (width, strength, feather, seed, bandLayer) in new[] { (far, nearStrength * s.Knob("blendFarStrength", 0.32f) / 0.6f, far * 0.45f, 0xFA2u, farLayer), (near, nearStrength, near * 0.55f, 0xB1EDu, nearLayer) })
                    foreach (var g in joined)
                        foreach (var grown in Poly.Offset(g.Outer, -width, OffsetJoin.Round, 4f, 0.6f))
                        {
                            var band = Poly.Boolean(new[] { grown }, bandOff, BoolOp.Difference);
                            foreach (var r in band.Where(r => r.Area >= 12f))
                                foreach (var piece in r.HasHoles ? Poly.CutHoles(r, 0f) : new List<float2[]> { r.Outer })
                                    if (Poly.Area(piece) >= 12f && bandLayer > 0)
                                        Paint(plan, Poly.Simplify(piece, 0.3f), bandLayer, strength, feather, width * 0.25f, 9f + width * 0.2f, seed ^ (uint)math.round(Poly.Centroid(piece).x * 7f) ^ (uint)math.round(Poly.Centroid(piece).y * 13f));
                        }
            }
            Stage("lawns");
            // ── 5. the middle a ring of houses leaves: one shared green ──
            Commons(houses, plots, ringOf, plan, s);

            // ground paint along the outside of back fences that face open land (a worn edge where the field meets the gardens)
            for (int i = 0; i < n; i++)
                {
                    if (!touched[i] || K(houses[i], s, "paint", 1f) < 0.5f || !BackEdge(houses[i], plots[i], out var e0, out var e1) || math.distance(e0, e1) < 4f) continue;
                    var mid = (e0 + e1) * 0.5f;
                    var outward = math.normalizesafe(mid - houses[i].Position);
                    var d = math.normalize(e1 - e0);
                    var nrm = new float2(-d.y, d.x);
                    if (math.dot(nrm, outward) < 0f) nrm = -nrm;
                    if (Enumerable.Range(0, n).Any(k => k != i && Poly.Contains(plots[k], mid + nrm * 1.5f))) continue;
                    int edgeLayer = (int)K(houses[i], s, "edgeLayer", 3f);
                    if (edgeLayer <= 0) continue;
                    float w = K(houses[i], s, "edgeWidth", 2.4f);
                    Paint(plan, new[] { e0, e1, e1 + nrm * w, e0 + nrm * w }, edgeLayer, K(houses[i], s, "edgeStrength", 0.45f), 1.2f, 0.9f, 2.5f, (uint)(houses[i].Id & 0x7fffffff));
                }

            GroundPatches(houses, plots, touched, plan, s);
            foreach (var kv in moved) if (!plan.HideLines.Contains(kv.Key)) plan.MoveLines[kv.Key] = kv.Value;
            KeepUnpolished(houses, plan);
            TreesOffDecks(plan);
            plan.Notes.Add($"{touched.Count(t => t)} plots: {borders.Count} shared sides, {rearPairs.Count} back to back, {plan.MoveLines.Count} lines moved, {plan.HideLines.Count} hidden, {plan.Lawns.Count} lawns fitted, {plan.NewLawns.Count} new lawns, {plan.Beds.Count} beds, {plan.Bushes.Count} bushes and rocks, {plan.Paths.Count} paths");
            if (plan.Paints.Count > 0 || s.Knob("paint", 1f) > 0.5f) PlanPaint(plan, s, houses.Where(h => !h.Polished).Select(h => new Region(Ccw(Lot(h)))).ToList());
            Stage("rest");
            plan.Notes.Add("time (ms): " + string.Join(", ", stages));
            if (allocs.Count > 0) plan.Notes.Add("alloc (MB): " + string.Join(", ", allocs));
            return plan;
        }

        /// <summary>
        /// The no lot never changes: whatever the steps above planned for an unpolished house (its lines, lawns, plot, beds,
        /// bushes, paths) is taken out again, so nothing of it can be edited whatever a rule did.
        /// </summary>
        private static void KeepUnpolished(IReadOnlyList<StitchHouse> houses, StitchPlan plan)
        {
            foreach (var h in houses)
            {
                if (h.Polished) continue;
                foreach (var l in h.Lines) { plan.HideLines.Remove(l.Id); plan.MoveLines.Remove(l.Id); }
                foreach (var l in h.Lawns) plan.Lawns.Remove(l.Id);
                plan.Plots.Remove(h.Id);
                plan.NewLawns.RemoveAll(x => x.House == h.Id);
                plan.Beds.RemoveAll(x => x.House == h.Id);
                plan.Bushes.RemoveAll(x => x.House == h.Id);
                plan.Paths.RemoveAll(x => x.House == h.Id);
            }
        }

        // ───────────── lines lying on one another ─────────────

        /// <summary>
        /// No two lines on top of or through one another: where a line runs alongside a nicer one (within about a metre, nearly
        /// parallel) or crosses it, that stretch goes from the lesser line (score, then length) and its longest remaining piece
        /// stays (a piece too short goes). A line merely ending on another (a corner, a T) is left as it is.
        /// </summary>
        private static void Dedupe(IReadOnlyList<StitchHouse> houses, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            var lines = new List<(int House, StitchLine L)>();
            // what never changes here: an unpolished lot's lines (as they are), and a polished garden this plan does not edit
            // whose lines and closing pieces another pass placed (as they stand). A line of an edited garden along or through
            // one gives way.
            bool Fixed(int k) => !houses[k].Polished || (!houses[k].Edit && houses[k].Standing != null);
            long standingId = -1;
            for (int i = 0; i < houses.Count; i++)
            {
                if (houses[i].Polished && !houses[i].Edit && houses[i].Standing != null)
                {
                    foreach (var (a, b) in houses[i].Standing) lines.Add((i, new StitchLine { Id = standingId--, Name = "", A = a, B = b }));
                    continue;
                }
                if (touched[i] || !houses[i].Polished) foreach (var l in houses[i].Lines) if (!plan.HideLines.Contains(l.Id)) lines.Add((i, l));
            }
            (float2 A, float2 B) At(StitchLine l) => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
            var order = lines.OrderByDescending(x => Fixed(x.House) ? 1 : 0).ThenByDescending(x => Score(x.L.Name)).ThenByDescending(x => { var p = At(x.L); return math.distance(p.A, p.B); }).ToList();
            float minLine = s.Knob("minLine", 2f);
            // one buffer for every pair (steps is at most 240)
            var near = new bool[241];
            // each line as it stands now and whether it is hidden, kept in step with moved and HideLines (only this loop changes
            // them, and only by hiding a line or cutting it shorter), and filed by grid cell where it stood when the loop began: a
            // line only ever shrinks inside where it was, so the cells hold every line a kept one's box can meet (each line was
            // checked against every other before: most of a plan's time on a big patch)
            int count = order.Count;
            var curA = new float2[count];
            var curB = new float2[count];
            var hidden = new bool[count];
            var fixedLine = new bool[count];
            var byId = new Dictionary<long, int>(count);
            Dictionary<long, List<int>> sameId = null;
            for (int m = 0; m < count; m++)
            {
                (curA[m], curB[m]) = At(order[m].L);
                hidden[m] = plan.HideLines.Contains(order[m].L.Id);
                fixedLine[m] = Fixed(order[m].House);
                if (byId.TryGetValue(order[m].L.Id, out int was))
                {
                    sameId ??= new Dictionary<long, List<int>>();
                    if (!sameId.TryGetValue(order[m].L.Id, out var list)) sameId[order[m].L.Id] = list = new List<int> { was };
                    list.Add(m);
                }
                else byId[order[m].L.Id] = m;
            }
            void Hide(int m)
            {
                plan.HideLines.Add(order[m].L.Id);
                if (sameId != null && sameId.TryGetValue(order[m].L.Id, out var all)) foreach (int x in all) hidden[x] = true;
                else hidden[m] = true;
            }
            void Move(int m, float2 a, float2 b)
            {
                moved[order[m].L.Id] = (a, b);
                if (sameId != null && sameId.TryGetValue(order[m].L.Id, out var all)) foreach (int x in all) { curA[x] = a; curB[x] = b; }
                else { curA[m] = a; curB[m] = b; }
            }
            const float DedupeCell = 16f;
            var cells = new Dictionary<long, List<int>>();
            for (int m = 0; m < count; m++)
            {
                if (fixedLine[m]) continue;   // never cut by another (below)
                var lo0 = math.min(curA[m], curB[m]) - 0.01f;
                var hi0 = math.max(curA[m], curB[m]) + 0.01f;
                int x0 = (int)math.floor(lo0.x / DedupeCell), x1 = (int)math.floor(hi0.x / DedupeCell), y0 = (int)math.floor(lo0.y / DedupeCell), y1 = (int)math.floor(hi0.y / DedupeCell);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long key = ((long)x << 32) ^ (uint)y;
                        if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                        l.Add(m);
                    }
            }
            var seenAt = new int[count];
            var candidates = new List<int>();
            for (int k = 0; k < order.Count; k++)
            {
                var keep = order[k];
                if (hidden[k]) continue;
                var ka = curA[k];
                var kb = curB[k];
                float klen = math.distance(ka, kb);
                if (klen < 1f) continue;
                var kd = (kb - ka) / klen;
                // a fence just inside an unpolished lot still reads as the same border as one on its edge
                float tol = houses[keep.House].Polished ? s.Knob("lineClearance", 1.2f) : math.max(s.Knob("lineClearance", 1.2f), s.EdgeTolerance);
                var lo = math.min(ka, kb) - tol - 1f;
                var hi = math.max(ka, kb) + tol + 1f;
                // the lines after it in the order whose cells meet its box, in that order
                candidates.Clear();
                {
                    int x0 = (int)math.floor(lo.x / DedupeCell), x1 = (int)math.floor(hi.x / DedupeCell), y0 = (int)math.floor(lo.y / DedupeCell), y1 = (int)math.floor(hi.y / DedupeCell);
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        {
                            if (!cells.TryGetValue(((long)x << 32) ^ (uint)y, out var l)) continue;
                            foreach (int m in l)
                                if (m > k && seenAt[m] != k + 1) { seenAt[m] = k + 1; candidates.Add(m); }
                        }
                    candidates.Sort();
                }
                foreach (int m in candidates)
                {
                    var other = order[m];
                    if (other.L.Id == keep.L.Id || hidden[m] || fixedLine[m]) continue;
                    var oa = curA[m];
                    var ob = curB[m];
                    if (math.max(oa.x, ob.x) < lo.x || math.min(oa.x, ob.x) > hi.x || math.max(oa.y, ob.y) < lo.y || math.min(oa.y, ob.y) > hi.y) continue;
                    float olen = math.distance(oa, ob);
                    if (olen < 0.5f) continue;
                    float sin = math.abs(kd.x * (ob - oa).y / olen - kd.y * (ob - oa).x / olen);
                    bool parallel = sin < math.sin(math.radians(25f));
                    // which stretches of the other line come too close to the kept one
                    int steps = math.clamp((int)(olen / 0.4f), 4, 240);
                    bool any = false;
                    for (int t = 0; t <= steps; t++)
                    {
                        var p = math.lerp(oa, ob, t / (float)steps);
                        // nearly parallel: only alongside the kept line (two fences carrying on end to end are fine)
                        float along = math.dot(p - ka, kd);
                        near[t] = DistanceToSegment(p, ka, kb) < tol && (!parallel || (along > 0.3f && along < klen - 0.3f));
                        any |= near[t];
                    }
                    if (!any) continue;
                    // a line ending on the kept one at an angle is a corner or a T: fine
                    if (!parallel)
                    {
                        int first = -1, last = -1;
                        for (int t = 0; t <= steps; t++) if (near[t]) { if (first < 0) first = t; last = t; }
                        float reach = tol / math.max(sin, 0.2f) + 0.5f;
                        bool onlyAtStart = first == 0, onlyAtEnd = last == steps;
                        for (int t = 0; t <= steps && (onlyAtStart || onlyAtEnd); t++)
                        {
                            if (!near[t]) continue;
                            if (!(t * olen / steps <= reach)) onlyAtStart = false;
                            if (!((steps - t) * olen / steps <= reach)) onlyAtEnd = false;
                        }
                        if (onlyAtStart || onlyAtEnd) continue;
                        // a corner of the kept line resting on the other one (the kept line ends there) is a T too
                        if (DistanceToSegment(ka, oa, ob) < tol || DistanceToSegment(kb, oa, ob) < tol)
                        {
                            float tA = math.dot(ka - oa, ob - oa) / (olen * olen), tB = math.dot(kb - oa, ob - oa) / (olen * olen);
                            bool aOn = DistanceToSegment(ka, oa, ob) < tol, bOn = DistanceToSegment(kb, oa, ob) < tol;
                            if ((aOn && !bOn) || (bOn && !aOn)) continue;
                        }
                    }
                    // the longest run of the other line clear of the kept one
                    int bestStart = -1, bestLen = 0, run = 0;
                    for (int t = 0; t <= steps; t++)
                    {
                        if (!near[t]) { run++; if (run > bestLen) { bestLen = run; bestStart = t - run + 1; } }
                        else run = 0;
                    }
                    float keepLen = bestLen <= 1 ? 0f : (bestLen - 1) * olen / steps;
                    if (keepLen < minLine) { Hide(m); continue; }
                    float u0 = bestStart / (float)steps, u1 = (bestStart + bestLen - 1) / (float)steps;
                    // pull back a little from the kept line where it was cut (not at the line's own ends)
                    float pad = math.min(0.3f / olen, (u1 - u0) * 0.2f);
                    if (u0 > 0f) u0 += pad;
                    if (u1 < 1f) u1 -= pad;
                    // a line that ran across the kept one ends on it (a T), not a metre and a half short of it: that left a gap
                    // where two gardens' fences met at an angle
                    if (!parallel && s.Knob("dedupeT", 1f) > 0.5f)
                    {
                        var od = ob - oa;
                        float den = od.x * (kb - ka).y - od.y * (kb - ka).x;
                        if (math.abs(den) > 1e-6f)
                        {
                            var w = ka - oa;
                            float tOther = (w.x * (kb - ka).y - w.y * (kb - ka).x) / den;
                            float tKept = (w.x * od.y - w.y * od.x) / den;
                            if (tKept > -0.3f / klen && tKept < 1f + 0.3f / klen)
                            {
                                if (u0 > 0f && tOther >= 0f && tOther < u0) u0 = tOther;
                                else if (u1 < 1f && tOther > u1 && tOther <= 1f) u1 = tOther;
                            }
                        }
                    }
                    Move(m, math.lerp(oa, ob, u0), math.lerp(oa, ob, u1));
                }
            }
        }

        // ───────────── the middle of a ring ─────────────

        /// <summary>
        /// The land a closed ring of houses encloses (only a ring seen whole: a pass that sees part of it plans no green),
        /// inside the back edges and outside every plot: one shared green, its outline cleaned into a shape the game takes.
        /// </summary>
        private static void Commons(IReadOnlyList<StitchHouse> houses, float2[][] plots, int[] ringOf, StitchPlan plan, StitchSettings s)
        {
            foreach (var members in Enumerable.Range(0, houses.Count).Where(i => ringOf[i] >= 0).GroupBy(i => ringOf[i]).Select(g => g.ToList()))
            {
                // a shared green only for a ring polished all round (an unpolished house's garden stays as it was)
                if (members.Count < 4 || members.Any(i => !houses[i].Polished)) continue;
                var ends = new List<float2>();
                foreach (int i in members) if (BackEdge(houses[i], plots[i], out var e0, out var e1)) { ends.Add(e0); ends.Add(e1); }
                if (ends.Count < 8) continue;
                var c = ends.Aggregate(float2.zero, (acc, p) => acc + p) / ends.Count;
                var ring = Poly.Normalize(ends.OrderBy(p => math.atan2(p.y - c.y, p.x - c.x)).ToArray());
                if (ring.Count == 0) continue;
                var taken = plots.Where(p => p != null).Select(p => new Region(Ccw(p))).ToList();
                var piece = Poly.Boolean(new[] { ring.OrderByDescending(r => r.Area).First() }, taken, BoolOp.Difference).OrderByDescending(r => r.Area).FirstOrDefault();
                if (piece == null || piece.Area < 30f) continue;
                // half a metre in from the fences, cleaned (no slivers, no needle points) so the game draws it
                var inset = Poly.Offset(piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer, 0.5f).OrderByDescending(r => Poly.Area(r)).FirstOrDefault();
                if (inset == null) continue;
                var green = Prepared(inset);
                if (green == null || green.Length < 3) continue;
                plan.Commons.Add(green);
                plan.Notes.Add($"a shared green in the middle ({Poly.Area(green):F0} m²)");
            }
        }

        // ───────────── planting ─────────────

        /// <summary>
        /// A planted bed (forest floor) along some of the fences and hedges round an extended garden, on the garden's side,
        /// clear of the corners, the house, its sheds and paving; bushes along it (two kinds per bed, a garden's look rather
        /// than a park's). Which fences get one is decided by the line and the house, so every pass plants the same.
        /// </summary>
        private static void Plant(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan,
            Dictionary<long, (float2 A, float2 B)> moved, List<Region>[] beds, Obstacle built, StitchSettings s)
        {
            // the lines as they will stand (a neighbour another pass planned: as it stands, known by where it is)
            var drawn = houses.Where(h => !(h.Polished && !h.Edit && h.Standing != null)).SelectMany(h => h.Lines).Where(l => !plan.HideLines.Contains(l.Id))
                .Select(l => (l.Id, P: moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B)))
                .Concat(houses.Where(h => h.Polished && !h.Edit && h.Standing != null).SelectMany(h => h.Standing)
                    .Select(p => ((long)Hash((ulong)(long)math.round(p.A.x * 4f) * 73856093UL ^ (ulong)(long)math.round(p.A.y * 4f) * 19349663UL ^ (ulong)(long)math.round(p.B.x * 4f) * 83492791UL), P: p)))
                .OrderBy(x => x.Item1).ToList();
            for (int i = 0; i < houses.Count; i++)
            {
                if (!touched[i] || !houses[i].Edit || HardLand(houses[i], s) || K(houses[i], s, "plant", 1f) < 0.5f) continue;
                string theme = ThemeOf(houses[i]);
                if (theme == "natural" || theme == "yard") continue;   // no tidy beds in a wild garden or a works yard
                var h = houses[i];
                int maxBushes = (int)K(h, s, "bushesPerGarden", 14f);
                var plot = Ccw(plots[i]);
                // only gardens the stitching made bigger: a lot left as it was keeps the game's own planting
                var ext = Poly.Boolean(new[] { (IReadOnlyList<float2>)plot }, new[] { (IReadOnlyList<float2>)Ccw(Lot(h)) }, BoolOp.Difference, FillRule.NonZero);
                if (ext.Sum(r => r.Area) < K(h, s, "plantMinExtra", 40f)) continue;
                var centre = Poly.Centroid(plot);
                int bushes = 0;
                // (a line with an end further than 0.8 m off the garden's bounds is further than that off its outline)
                Poly.Bounds(plot, out var plotLo, out var plotHi);
                plotLo -= 0.81f;
                plotHi += 0.81f;
                foreach (var (id, (a, b)) in drawn)
                {
                    if (a.x < plotLo.x || a.y < plotLo.y || a.x > plotHi.x || a.y > plotHi.y || b.x < plotLo.x || b.y < plotLo.y || b.x > plotHi.x || b.y > plotHi.y) continue;
                    float len = math.distance(a, b);
                    if (len < 6f || DistanceToRing(plot, a) > 0.8f || DistanceToRing(plot, b) > 0.8f || DistanceToRing(plot, (a + b) * 0.5f) > 0.8f) continue;
                    uint hash = Hash((ulong)id ^ ((ulong)h.Id * 0x9E3779B97F4A7C15UL));
                    if (hash % 100 >= (uint)(h.Options != null ? K(h, s, "bedChance", 65f) : theme == "tidy" ? s.Knob("tidyBedChance", 90f) : s.Knob("bedChance", 65f))) continue;
                    var d = (b - a) / len;
                    var nrm = new float2(-d.y, d.x);
                    if (math.dot(centre - (a + b) * 0.5f, nrm) < 0f) nrm = -nrm;
                    float width = 1.2f + (hash >> 8 & 7) * 0.08f;
                    var e0 = a + d * 1.4f;
                    var e1 = b - d * 1.4f;
                    // an organic edge: the bed swells and narrows along the fence and tapers into the lawn at its ends
                    float blen = math.distance(e0, e1), phase = (hash >> 24 & 255) / 255f * 6.28f;
                    float Width(float t) => width * (0.35f + 0.65f * math.sqrt(math.max(0f, math.sin(math.PI * t)))) * (1f + 0.22f * math.sin(t * blen / 2.6f + phase));
                    var edge = new List<float2> { e0 - nrm * 1f };
                    int segs = math.max(4, (int)(blen / 0.9f));
                    for (int k = 0; k <= segs; k++) { float t = k / (float)segs; edge.Add(math.lerp(e0, e1, t) + nrm * Width(t)); }
                    edge.Add(e1 - nrm * 1f);
                    var rect = Ccw(edge.ToArray());
                    var region = Poly.Boolean(new[] { new Region(rect) }, new[] { new Region(plot) }, BoolOp.Intersection);
                    // only on bare land: never over a lawn the house keeps as it is
                    var near = built.Near(rect).Concat(beds[i]).Concat(h.Lawns.Where(l => l.Ring != null && l.Ring.Length >= 3).Select(l => new Region(Ccw(l.Ring)))).ToList();
                    if (near.Count > 0) region = Poly.Boolean(region, near, BoolOp.Difference);
                    var piece = region.OrderByDescending(r => r.Area).FirstOrDefault();
                    if (piece == null || piece.Area < 4f) continue;
                    // a bed reaches the garden's edge (one cut off from it by a driveway would be an island the lawn cannot go round)
                    if (!piece.Outer.Any(q => DistanceToRing(plot, q) < 0.15f)) continue;
                    var ring = Prepared(piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer);
                    if (ring == null || ring.Length < 3) continue;
                    plan.Beds.Add((h.Id, ring));
                    beds[i].Add(new Region(Ccw(ring)));
                    // bushes along the bed's middle, two kinds alternating
                    int k0 = (int)(hash >> 12) % FirstRock, k1 = (k0 + 1 + (int)(hash >> 16) % (FirstRock - 1)) % FirstRock;   // plants only (rocks come after them)
                    float spacing = 2.3f + (hash >> 20 & 7) * 0.12f;
                    int count = 0;
                    for (float t = spacing * 0.5f; t < blen && bushes < maxBushes; t += spacing)
                    {
                        uint hb = Hash(hash + (uint)count * 7919u);
                        float tt = math.saturate((t + ((hb & 255) / 255f - 0.5f) * 0.5f) / blen);
                        var p = math.lerp(e0, e1, tt) + nrm * (Width(tt) * (0.5f + ((hb >> 8 & 255) / 255f - 0.5f) * 0.3f));
                        if (!Poly.Contains(ring, p)) continue;
                        // a tidy garden: flower beds between the bushes
                        int kind = theme == "tidy" && count % 3 == 1 ? Flowerbed : count % 2 == 0 ? k0 : k1;
                        plan.Bushes.Add((h.Id, p, (hb >> 16 & 1023) / 1023f * 2f * math.PI, kind));
                        bushes++;
                        count++;
                    }
                }
            }
        }

        /// <summary>Everything built on the houses' lots (houses, sheds, pools, decks, driveways, paving) with their bounds.</summary>
        private sealed class Obstacle
        {
            private readonly List<(float2 Min, float2 Max, Region R)> _all = new List<(float2, float2, Region)>();

            public Obstacle(IReadOnlyList<StitchHouse> houses)
            {
                foreach (var h in houses)
                    foreach (var r in (h.Footprint != null ? new[] { h.Footprint } : new float2[0][]).Concat(h.Props).Concat(h.Hard))
                    {
                        if (r == null || r.Length < 3) continue;
                        var ring = Ccw(r);
                        _all.Add((MinOf(ring), MaxOf(ring), new Region(ring)));
                    }
            }

            /// <summary>The obstacles whose bounds meet the bounds of these points.</summary>
            public List<Region> Near(IReadOnlyList<float2> pts)
            {
                var lo = MinOf(pts);
                var hi = MaxOf(pts);
                var list = new List<Region>();
                foreach (var (min, max, r) in _all)
                    if (min.x <= hi.x && max.x >= lo.x && min.y <= hi.y && max.y >= lo.y) list.Add(r);
                return list;
            }
        }

        /// <summary>
        /// Big new land gets something to look at, by what the building is. A home's garden: a rock garden, a sand pit, a wild
        /// corner (logs, big bushes) or a bench nook (a bench facing the house, a flower bed). Industry: a storage yard (rubble,
        /// pallets, crates, barrels) on worn ground. Placed at the roomiest spot of the new land, clear of everything built.
        /// </summary>
        /// <summary>Objects a feature placed (not a bed's planting): kept when beds are fitted round a lawn. Per plan, on its thread.</summary>
        [ThreadStatic] private static HashSet<(long, float2)> s_feature;
        private static HashSet<(long, float2)> Feature => s_feature ?? (s_feature = new HashSet<(long, float2)>());

        private static void Features(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, List<Region>[] beds, List<Region>[] reserved, Obstacle built, StitchSettings s, List<(float2 A, float2 B)> fenceLines = null)
        {
            // each garden's land past its lot, worked out once for all its features (it is the same for each)
            var landOf = new Dictionary<int, List<Region>>();
            var clearOf = new Dictionary<int, (List<Region> Land, int Reserved)>();
            var slots = new List<(int I, int Slot)>();
            for (int i = 0; i < houses.Count; i++)
            {
                if (plots[i] == null) continue;
                float newLand = Poly.Area(plots[i]) - 4f * houses[i].HalfLot.x * houses[i].HalfLot.y;
                int count = 1 + (int)math.clamp((newLand - K(houses[i], s, "featureMinLand", 100f)) / math.max(50f, K(houses[i], s, "featureSpacing", 600f)), 0f, 5f);
                for (int k = 0; k < count; k++) slots.Add((i, k));
            }
            foreach (var (i, slot) in slots)
            {
                var h = houses[i];
                if (!touched[i] || !h.Edit || plots[i] == null || K(h, s, "plant", 1f) < 0.5f || K(h, s, "features", 1f) < 0.5f) continue;
                string theme = ThemeOf(h);
                bool industry = theme == "yard";
                if (!industry && HardLand(h, s)) continue;   // paved land: only a works yard's storage
                uint hash = Hash((ulong)h.Id * 0x7F4A7C15UL ^ 0xFEA7UL ^ ((ulong)slot * 0x9E3779B1UL));
                if (hash % 100 >= (uint)(h.Options != null ? K(h, s, "featureChance", 55f) : theme == "natural" ? s.Knob("naturalFeatureChance", 75f) : s.Knob("featureChance", 55f))) continue;
                var plot = Ccw(plots[i]);
                if (!landOf.TryGetValue(i, out var land)) landOf[i] = land = Poly.Boolean(new[] { new Region(plot) }, new[] { new Region(Ccw(Lot(h))) }, BoolOp.Difference);
                if (land.Sum(r => r.Area) < K(h, s, "featureMinLand", 100f)) continue;
                // the land clear of what stands, beds and fences, worked out once per garden (it was again for each of up to six
                // features, every fence near a big garden a hole cut each time); each feature then only keeps off the ones before
                if (clearOf.TryGetValue(i, out var cleared))
                {
                    var since = reserved[i].Skip(cleared.Reserved).ToList();
                    foreach (var (fh, fat) in Feature) if (fh == h.Id) since.Add(new Region(Ccw(Blob(fat, 7f, hash, 10))));
                    land = since.Count > 0 ? Poly.Boolean(cleared.Land, since, BoolOp.Difference) : cleared.Land;
                    goto search;
                }
                var keepOff = built.Near(plot).Concat(beds[i]).Concat(reserved[i]).ToList();
                // fences and hedges (its own, its neighbours', the closing pieces): a feature keeps 2.5 m clear of them
                if (fenceLines != null)
                {
                    var plo = MinOf(plot) - 3f;
                    var phi = MaxOf(plot) + 3f;
                    foreach (var (fa, fb) in fenceLines)
                    {
                        if (math.max(fa.x, fb.x) < plo.x || math.min(fa.x, fb.x) > phi.x || math.max(fa.y, fb.y) < plo.y || math.min(fa.y, fb.y) > phi.y) continue;
                        var fd = math.normalizesafe(fb - fa);
                        if (math.lengthsq(fd) < 0.5f) continue;
                        var fnrm = new float2(-fd.y, fd.x) * 2.5f;
                        keepOff.Add(new Region(Ccw(new[] { fa - fd * 2.5f - fnrm, fb + fd * 2.5f - fnrm, fb + fd * 2.5f + fnrm, fa - fd * 2.5f + fnrm })));
                    }
                }
                if (keepOff.Count > 0) land = Poly.Boolean(land, keepOff, BoolOp.Difference);
                clearOf[i] = (land, reserved[i].Count);
                // the features this garden has already: the next one goes elsewhere
                {
                    var had = new List<Region>();
                    foreach (var (fh, fat) in Feature) if (fh == h.Id) had.Add(new Region(Ccw(Blob(fat, 7f, hash, 10))));
                    if (had.Count > 0) land = Poly.Boolean(land, had, BoolOp.Difference);
                }
            search:
                // the roomiest spot: the grid point farthest from the land's edge
                // a roomy spot, nearer the house the better (a garden reaching far keeps its features where people are); the grid
                // coarser in big land, so a huge garden is searched as quickly as a small one
                float2 best = default;
                float room = 0f, bestScore = float.MinValue;
                foreach (var r in land)
                {
                    var lo = MinOf(r.Outer);
                    var hi = MaxOf(r.Outer);
                    float cell = math.max(1f, math.sqrt(math.max(1f, (hi.x - lo.x) * (hi.y - lo.y))) / 50f);
                    // (the same spot as a look at every grid point would find, with less looking: a point that could not beat the
                    // best so far even with all the room there is, or as soon as it is too near an edge, is passed by, and a hole
                    // (a fence's keep-off strip) only looked at where the point is near it. Every fence near a big garden is a
                    // hole: the grid point by point against all of them was most of a plan's time on a big yard)
                    int nh = r.Holes.Count;
                    var holeLo = new float2[nh];
                    var holeHi = new float2[nh];
                    for (int k = 0; k < nh; k++) Poly.Bounds(r.Holes[k], out holeLo[k], out holeHi[k]);
                    for (float x = lo.x + cell * 0.5f; x < hi.x; x += cell)
                        for (float y = lo.y + cell * 0.5f; y < hi.y; y += cell)
                        {
                            var p = new float2(x, y);
                            float toHouse = math.distance(p, h.Position);
                            if (6f - toHouse * 0.02f < bestScore - 0.001f) continue;
                            if (!Poly.Contains(r.Outer, p)) continue;
                            bool inHole = false;
                            for (int k = 0; k < nh && !inHole; k++)
                                if (p.x >= holeLo[k].x - 0.01f && p.x <= holeHi[k].x + 0.01f && p.y >= holeLo[k].y - 0.01f && p.y <= holeHi[k].y + 0.01f && Poly.Contains(r.Holes[k], p)) inHole = true;
                            if (inHole) continue;
                            // below this the spot is lost: too near an edge, or not roomier than the best by enough for how far it is
                            float stop = math.max(2.2f, bestScore + toHouse * 0.02f - 0.001f);
                            float c = DistanceToRing(r.Outer, p, stop);
                            for (int k = 0; k < nh && c >= stop; k++)
                            {
                                // (a hole further off than the nearest edge so far cannot be nearer)
                                var off = math.max(math.max(holeLo[k] - p, p - holeHi[k]), 0f);
                                if (math.length(off) > c + 0.01f) continue;
                                c = math.min(c, DistanceToRing(r.Holes[k], p, stop));
                            }
                            if (c < stop) continue;
                            float score = math.min(c, 6f) - math.distance(p, h.Position) * 0.02f;
                            if (c >= 2.2f && score > bestScore) { bestScore = score; room = c; best = p; }
                        }
                }
                if (room < 2.2f) continue;
                float rad = math.min(room - 0.6f, industry ? 6f : 3.2f);
                float Turn(uint k) => (Hash(hash + k) & 1023) / 1023f * 2f * math.PI;
                float2 Around(uint k, float maxR) { float a = Turn(k), d = maxR * math.sqrt(((Hash(hash ^ (k * 2654435761u)) >> 4) & 1023) / 1023f); return best + new float2(math.cos(a), math.sin(a)) * d; }
                void Put(int kind, float2 at, uint k) { plan.Bushes.Add((h.Id, at, Turn(k + 97u), kind)); Feature.Add((h.Id, at)); }
                if (theme == "natural")
                {
                    if ((hash >> 12 & 1) == 0)
                    {
                        // a rocky outcrop: boulders and stones on bare dry ground, a big bush in its lee
                        int boulders = 2 + (int)(hash >> 14 & 1);
                        for (int k = 0; k < boulders; k++) Put(FirstRock + (int)(Hash(hash + (uint)k * 13u) % 3), Around((uint)k + 31u, rad * 0.6f), (uint)k + 31u);
                        for (int k = 0; k < 4; k++) Put(FirstStone + (int)(Hash(hash + (uint)k * 7u) % 5), Around((uint)k + 41u, rad), (uint)k + 41u);
                        Put(4, Around(51u, rad), 51u);
                        if (K(h, s, "rockGround", 1f) > 0f) Paint(plan, Blob(best, rad + 1.2f, hash, 12), (int)K(h, s, "rockGround", 1f), s.Knob("outcropDry", 0.75f), 1.8f, 1.2f, 4f, hash);
                        if (K(h, s, "rockLayer", 3f) > 0f) Paint(plan, Blob(best, rad * 0.7f, hash ^ 0x55u, 10), (int)K(h, s, "rockLayer", 3f), s.Knob("outcropDirt", 0.5f), 1.2f, 0.8f, 3f, hash ^ 0x55u);
                        plan.Notes.Add($"{h.Id}: a rocky outcrop");
                    }
                    else
                    {
                        // a log pile and big bushes on lush ground
                        LogPile(h.Id, best, Turn(61u), 3, hash, plan);
                        for (int k = 0; k < 4; k++) Put(4, Around((uint)k + 71u, rad), (uint)k + 71u);
                        Paint(plan, Blob(best, rad + 1.5f, hash, 10), (int)s.Knob("lushLayer", 2f), 0.85f, 2f, 1.5f, 4f, hash);
                        plan.Notes.Add($"{h.Id}: a wild corner");
                    }
                    continue;
                }
                if (theme == "tidy")
                {
                    // a flower garden: three flower beds in a row and a bench facing the house
                    var toHouseT = math.normalizesafe(h.Position - best);
                    var acrossT = new float2(-toHouseT.y, toHouseT.x);
                    float yawT = math.atan2(toHouseT.x, toHouseT.y);
                    for (int k = -1; k <= 1; k++) { var at = best + acrossT * (k * 2.2f); plan.Bushes.Add((h.Id, at, yawT, Flowerbed)); Feature.Add((h.Id, at)); }
                    var benchAt = best - toHouseT * 1.8f;
                    plan.Bushes.Add((h.Id, benchAt, yawT, FirstBench + (int)(hash >> 16) % 2));
                    Feature.Add((h.Id, benchAt));
                    plan.Notes.Add($"{h.Id}: a flower garden");
                    continue;
                }
                if (industry)
                {
                    // a storage yard: a few piles and stock, on worn ground
                    int count = 4 + (int)(hash >> 8 & 3);
                    for (int k = 0; k < count; k++)
                    {
                        int kind = (Hash(hash + (uint)k * 31u) % 3) switch { 0 => FirstRubble + (int)(Hash(hash + (uint)k) % 4), 1 => FirstStock + (int)(Hash(hash + (uint)k) % 4), _ => FirstStock + 4 + (int)(Hash(hash + (uint)k) % 2) };
                        Put(kind, Around((uint)k, rad), (uint)k);
                    }
                    if (K(h, s, "yardLayer", 3f) > 0f) Paint(plan, Blob(best, rad + 1.5f, hash, 12), (int)K(h, s, "yardLayer", 3f), s.Knob("yardDirt", 0.7f), 2f, 1.2f, 3.5f, hash);
                    plan.Notes.Add($"{h.Id}: a storage yard");
                    continue;
                }
                switch (hash >> 12 & 3)
                {
                    case 0:   // a rock garden: a boulder and stones on a dirt patch, a bush or two
                        Put(FirstRock + (int)(hash >> 16) % 3, best, 1u);
                        for (int k = 0; k < 3 + (int)(hash >> 18 & 1); k++) Put(FirstStone + (int)(Hash(hash + (uint)k) % 5), Around((uint)k + 5u, rad), (uint)k + 5u);
                        Put((int)(hash >> 20) % 2 == 0 ? 0 : 4, Around(17u, rad), 17u);
                        if (K(h, s, "rockLayer", 3f) > 0f) Paint(plan, Blob(best, rad + 0.5f, hash, 10), (int)K(h, s, "rockLayer", 3f), s.Knob("rockDirt", 0.55f), 1.5f, 0.8f, 3f, hash);
                        plan.Notes.Add($"{h.Id}: a rock garden");
                        break;
                    case 1:   // a sand pit, a bench beside it
                    {
                        var pit = Blob(best, math.min(rad, 2.2f), hash, 10);
                        var ring = Prepared(pit);
                        if (ring == null) break;
                        plan.Paths.Add((h.Id, ring, "Sand Surface 01"));
                        reserved[i].Add(new Region(Ccw(ring)));
                        var toHouse = math.normalizesafe(h.Position - best);
                        Put(FirstBench + (int)(hash >> 16) % 2, best + new float2(-toHouse.y, toHouse.x) * (math.min(rad, 2.2f) + 1.1f), 3u);
                        plan.Notes.Add($"{h.Id}: a sand pit");
                        break;
                    }
                    case 2:   // a wild corner: logs and big bushes on lush ground
                        LogPile(h.Id, Around(11u, rad * 0.5f), Turn(11u), 2, hash, plan);
                        for (int k = 0; k < 3; k++) Put(4, Around((uint)k + 21u, rad), (uint)k + 21u);
                        Paint(plan, Blob(best, rad + 1.5f, hash, 10), (int)s.Knob("lushLayer", 2f), 0.8f, 2f, 1.5f, 4f, hash);
                        plan.Notes.Add($"{h.Id}: a wild corner");
                        break;
                    default:   // a bench nook: a bench facing the house with a flower bed beside it
                    {
                        var toHouse = math.normalizesafe(h.Position - best);
                        float yaw = math.atan2(toHouse.x, toHouse.y);
                        plan.Bushes.Add((h.Id, best, yaw, FirstBench + (int)(hash >> 16) % 2));
                        plan.Bushes.Add((h.Id, best + new float2(-toHouse.y, toHouse.x) * 1.8f, yaw, Flowerbed));
                        Feature.Add((h.Id, best));
                        Feature.Add((h.Id, best + new float2(-toHouse.y, toHouse.x) * 1.8f));
                        Put(2 + (int)(hash >> 18) % 2, best - toHouse * 1.2f + new float2(toHouse.y, -toHouse.x) * 1.4f, 9u);
                        plan.Notes.Add($"{h.Id}: a bench nook");
                        break;
                    }
                }
            }
        }

        /// <summary>Logs lying side by side, all one way (a pile, not sticks dropped anyhow).</summary>
        private static void LogPile(long house, float2 at, float yaw, int count, uint hash, StitchPlan plan)
        {
            var across = new float2(math.cos(yaw), -math.sin(yaw));
            for (int k = 0; k < count; k++)
            {
                var p = at + across * ((k - (count - 1) * 0.5f) * 0.75f);
                plan.Bushes.Add((house, p, yaw + ((Hash(hash + (uint)k * 3u) & 15) / 15f - 0.5f) * 0.08f, FirstLog + (int)(Hash(hash + (uint)k) % 3)));
                Feature.Add((house, p));
            }
        }

        /// <summary>A soft, uneven round shape (a pond's or a patch's outline).</summary>
        private static float2[] Blob(float2 c, float r, uint seed, int points)
        {
            var ring = new float2[points];
            for (int k = 0; k < points; k++)
            {
                float a = k * 2f * math.PI / points;
                float wobble = 0.82f + 0.3f * ((Hash(seed + (uint)k * 977u) & 255) / 255f);
                ring[k] = c + new float2(math.cos(a), math.sin(a)) * r * wobble;
            }
            return Ccw(ring);
        }

        /// <summary>A step between two gardens: a planted bed with bushes close together, like a young hedge.</summary>
        private static void StepRow(StitchHouse h, float2[] plot, float2 a, float2 b, float2 inward, StitchPlan plan, List<Region> beds, Obstacle built, StitchSettings s)
        {
            if (math.distance(a, b) < 2f) return;
            var d = math.normalize(b - a);
            var nrm = new float2(-d.y, d.x);
            if (math.dot(nrm, inward) < 0f) nrm = -nrm;
            if (!Poly.Contains(plot, (a + b) * 0.5f + nrm * 0.6f)) nrm = -nrm;
            var rect = Ccw(new[] { a - nrm * 0.3f, b - nrm * 0.3f, b + nrm * 1.3f, a + nrm * 1.3f });
            var region = Poly.Boolean(new[] { new Region(rect) }, new[] { new Region(Ccw(plot)) }, BoolOp.Intersection);
            var near = built.Near(rect);
            if (near.Count > 0) region = Poly.Boolean(region, near, BoolOp.Difference);
            var piece = region.OrderByDescending(r => r.Area).FirstOrDefault();
            if (piece == null || piece.Area < 2f) return;
            var ring = Prepared(piece.Outer);
            if (ring == null || ring.Length < 3) return;
            plan.Beds.Add((h.Id, ring));
            beds.Add(new Region(Ccw(ring)));
            uint hash = Hash((ulong)h.Id ^ 0x5EEDUL);
            int kind = new[] { 0, 1, 4 }[hash % 3];
            int count = 0;
            for (float t = 0.8f; t < math.distance(a, b) - 0.5f; t += 1.5f)
            {
                var p = a + d * t + nrm * 0.55f;
                if (!Poly.Contains(ring, p)) continue;
                plan.Bushes.Add((h.Id, p, (Hash(hash + (uint)count) & 1023) / 1023f * 2f * math.PI, kind));
                count++;
            }
        }

        /// <summary>
        /// A garden path in some big new gardens: paving from the back of the house to the back fence, with a dog-leg
        /// half way (people walk round the beds), clear of everything built.
        /// </summary>
        private static void Paths(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, int[] left, int[] right, StitchPlan plan, List<Region>[] beds, Obstacle built, StitchSettings s)
        {
            // who gets a garden path (decided by the house, so every pass agrees)
            bool Wants(int k)
            {
                var hk = houses[k];
                if (!touched[k] || HardLand(hk, s) || hk.Footprint == null || hk.Footprint.Length < 3) return false;
                if (ThemeOf(hk) == "natural" || ThemeOf(hk) == "yard") return false;
                if (K(hk, s, "plant", 1f) < 0.5f || Hash((ulong)hk.Id * 0x2545F4914F6CDD1DUL) % 100 >= (uint)K(hk, s, "pathChance", 45f)) return false;
                return Poly.Area(plots[k]) - 4f * hk.HalfLot.x * hk.HalfLot.y >= K(hk, s, "pathMinExtra", 120f);
            }
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!h.Edit || !Wants(i)) continue;
                uint hash = Hash((ulong)h.Id * 0x2545F4914F6CDD1DUL);
                var plot = Ccw(plots[i]);
                if (!BackEdge(h, plots[i], out var e0, out var e1)) continue;
                // a neighbour with a path too: both paths meet where their shared fence reaches the back (the right-hand
                // neighbour first), rather than each going out back on its own
                int mate = right[i] >= 0 && Wants(right[i]) ? right[i] : left[i] >= 0 && Wants(left[i]) ? left[i] : -1;
                StitchPair shared = mate < 0 ? null : plan.Pairs.FirstOrDefault(p => !p.Rear && ((p.Left == h.Id && p.Right == houses[mate].Id) || (p.Right == h.Id && p.Left == houses[mate].Id)));
                if (shared != null && math.lengthsq(shared.Back - shared.Front) > 4f)
                {
                    float backOfHouse0 = h.Footprint.Min(p => h.ToLocal(p).y);
                    var start0 = h.ToWorld(new float2(0f, backOfHouse0 - 0.4f));
                    var meet = shared.Back + math.normalizesafe(shared.Front - shared.Back) * 0.9f;
                    var sideIn = math.normalizesafe(h.Position - meet);
                    sideIn = math.dot(sideIn, h.Right) > 0f ? h.Right : -h.Right;
                    var meetIn = meet + sideIn * 0.7f;
                    var turn = h.ToWorld(new float2(0f, h.ToLocal(meetIn).y));
                    if (math.distance(start0, turn) > 2f && math.distance(turn, meetIn) > 1f)
                    {
                        var legs = Poly.UnionAll(new[] { Strip(start0, turn, 1.0f), Strip(turn, meetIn, 1.0f) }.Select(p => (IReadOnlyList<float2>)p));
                        legs = Poly.Boolean(legs, new[] { new Region(plot) }, BoolOp.Intersection);
                        var nearLegs = built.Near(new[] { start0, turn, meetIn }).Concat(beds[i]).ToList();
                        if (nearLegs.Count > 0) legs = Poly.Boolean(legs, nearLegs, BoolOp.Difference);
                        var leg = legs.OrderByDescending(r => r.Area).FirstOrDefault();
                        var legRing = leg == null || leg.Area < 4f ? null : Prepared(leg.HasHoles ? Poly.CutHoles(leg, 0f).First() : leg.Outer);
                        if (legRing != null)
                        {
                            plan.Paths.Add((h.Id, legRing, "Tiles Surface 01"));
                            beds[i].Add(new Region(Ccw(legRing)));
                            continue;
                        }
                    }
                }
                // from the middle of the house's back wall
                float backOfHouse = h.Footprint.Min(p => h.ToLocal(p).y);
                var start = h.ToWorld(new float2(0f, backOfHouse - 0.4f));
                // to the back fence itself (a path that stops short would leave the lawn wrapped round it)
                var end = (e0 + e1) * 0.5f - math.normalizesafe(h.Position - (e0 + e1) * 0.5f) * 0.6f;
                if (math.distance(start, end) < 6f) continue;
                float jog = ((hash >> 8 & 1) == 0 ? 1f : -1f) * (1.5f + (hash >> 9 & 3) * 0.5f);
                var mid0 = math.lerp(start, end, 0.5f);
                var mid1 = mid0 + h.Right * jog;
                var pieces = new[] { Strip(start, mid0, 1.0f), Strip(mid0, mid1, 1.0f), Strip(mid1, end + h.Right * jog, 1.0f) };
                var path = Poly.UnionAll(pieces.Select(p => (IReadOnlyList<float2>)p));
                path = Poly.Boolean(path, new[] { new Region(plot) }, BoolOp.Intersection);
                var nearPath = built.Near(pieces.SelectMany(p => p).ToArray()).Concat(beds[i]).ToList();
                if (nearPath.Count > 0) path = Poly.Boolean(path, nearPath, BoolOp.Difference);
                var piece = path.OrderByDescending(r => r.Area).FirstOrDefault();
                if (piece == null || piece.Area < 5f) continue;
                var ring = Prepared(piece.HasHoles ? Poly.CutHoles(piece, 0f).First() : piece.Outer);
                if (ring == null || ring.Length < 3) continue;
                plan.Paths.Add((h.Id, ring, "Tiles Surface 01"));
                beds[i].Add(new Region(Ccw(ring)));
            }
        }

        /// <summary>A rock or two where some shared fences meet the street (in the garden, clear of driveways).</summary>
        private static void Rocks(IReadOnlyList<StitchHouse> houses, float2[][] plots, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, Obstacle built, StitchSettings s)
        {
            // a rock never sits by a hedge (a rock against a hedge looks placed, not found)
            var hedges = houses.SelectMany(h => h.Lines).Where(l => Score(l.Name) == 3 && !plan.HideLines.Contains(l.Id))
                .Select(l => moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B)).ToList();
            var byId = new Dictionary<long, int>();
            for (int i = 0; i < houses.Count; i++) byId[houses[i].Id] = i;
            foreach (var pair in plan.Pairs)
            {
                if (pair.Rear || math.lengthsq(pair.Back - pair.Front) < 1f) continue;
                uint hash = Hash(((ulong)pair.Left << 20) ^ (ulong)pair.Right ^ 0xB0D1UL);
                long owner = (hash >> 7 & 1) == 0 ? pair.Left : pair.Right;
                if (!byId.TryGetValue(owner, out int oi)) continue;
                string ownerTheme = ThemeOf(houses[oi]);
                float chance = K(houses[oi], s, "plant", 1f) < 0.5f || K(houses[oi], s, "rockChance", 12f) <= 0f ? 0f
                    : houses[oi].Options != null ? K(houses[oi], s, "rockChance", 12f)
                    : ownerTheme == "natural" ? s.Knob("naturalRockChance", 35f) : ownerTheme == "garden" ? K(houses[oi], s, "rockChance", 12f) : 0f;
                if (hash % 100 >= (uint)chance) continue;
                if (!houses[oi].Polished)
                {
                    // never on an unpolished lot: the polished side of the pair
                    owner = owner == pair.Left ? pair.Right : pair.Left;
                    if (!byId.TryGetValue(owner, out oi) || !houses[oi].Polished) continue;
                }
                var d = math.normalizesafe(pair.Back - pair.Front);
                var toPlot = math.normalizesafe(houses[oi].Position - pair.Front);
                var side = new float2(-d.y, d.x);
                if (math.dot(side, toPlot) < 0f) side = -side;
                var p = pair.Front + d * (1.6f + (hash >> 9 & 3) * 0.3f) + side * 1.2f;
                if (!Poly.Contains(plots[oi], p) || built.Near(new[] { p - 0.1f, p + 0.1f }).Any(r => Poly.Contains(r.Outer, p))
                    || plan.Beds.Any(bd => Poly.Contains(bd.Ring, p)) || plan.Paths.Any(pt => Poly.Contains(pt.Ring, p))) continue;
                if (hedges.Any(hl => DistanceToSegment(p, hl.A, hl.B) < 4f)) continue;
                plan.Bushes.Add((owner, p, (hash >> 12 & 1023) / 1023f * 2f * math.PI, FirstRock + (int)((hash >> 22) % 3)));
            }
        }

        /// <summary>A plot grown by a piece of land (whatever of it no other plot holds), as one outline.</summary>
        private static float2[] Grow(float2[][] plots, int owner, float2[] piece)
        {
            var others = Enumerable.Range(0, plots.Length).Where(k => k != owner && plots[k] != null).Select(k => new Region(Ccw(plots[k]))).ToList();
            var free = Poly.Boolean(new[] { new Region(Ccw(piece)) }, others, BoolOp.Difference);
            if (free.Count == 0) return plots[owner];
            var merged = Poly.Boolean(new[] { new Region(Ccw(plots[owner])) }, free, BoolOp.Union);
            var main = merged.OrderByDescending(r => r.Area).FirstOrDefault();
            if (main == null || main.Area < Poly.Area(plots[owner])) return plots[owner];
            return Poly.Simplify(main.Outer, 0.05f);
        }

        /// <summary>Segments filed by grid cell (each in every cell its bounds, grown by a margin, touch): a point's cell holds every
        /// segment within the margin of it, in the order they were added.</summary>
        private sealed class LineGrid
        {
            private float _cell, _margin;
            private readonly Dictionary<long, List<(float2 A, float2 B)>> _cells = new Dictionary<long, List<(float2, float2)>>();
            private static readonly List<(float2 A, float2 B)> None = new List<(float2, float2)>();
            /// <summary>Lines too big to file by cell: every point looks at them too.</summary>
            public readonly List<(float2 A, float2 B)> Big = new List<(float2, float2)>();

            public LineGrid(float cell, float margin) { _cell = cell; _margin = margin; }

            /// <summary>Empty again, its cells' lists kept (a grid per plan was a twentieth of its garbage).</summary>
            public LineGrid Reset(float cell, float margin)
            {
                _cell = cell;
                _margin = margin;
                Big.Clear();
                if (_cells.Count > 8192) _cells.Clear();
                else foreach (var l in _cells.Values) l.Clear();
                return this;
            }

            private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

            public void Add(float2 a, float2 b)
            {
                var lo = math.min(a, b) - _margin;
                var hi = math.max(a, b) + _margin;
                int x0 = (int)math.floor(lo.x / _cell), x1 = (int)math.floor(hi.x / _cell), y0 = (int)math.floor(lo.y / _cell), y1 = (int)math.floor(hi.y / _cell);
                // a very long slanted line: looked at from everywhere (cheaper than hundreds of cells)
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 4096) { Big.Add((a, b)); return; }
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long k = Key(x, y);
                        if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<(float2, float2)>();
                        l.Add((a, b));
                    }
            }

            public List<(float2 A, float2 B)> At(float2 p) => _cells.TryGetValue(Key((int)math.floor(p.x / _cell), (int)math.floor(p.y / _cell)), out var l) ? l : None;
        }

        // the line grids CloseGardens and JoinEnds file the lines in, kept per thread
        [ThreadStatic] private static LineGrid s_closeGrid, s_joinGrid;

        private static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.distance(p, a + ab * t);
        }

        private static float2[] Strip(float2 a, float2 b, float width)
        {
            var d = math.normalizesafe(b - a, new float2(1f, 0f));
            var n = new float2(-d.y, d.x) * (width * 0.5f);
            var ext = d * (width * 0.5f);
            return Ccw(new[] { a - ext - n, b + ext - n, b + ext + n, a - ext + n });
        }

        private static float2[] Subtract(float2[] plot, List<float2> part)
        {
            if (part == null || part.Count < 3 || Poly.Area(part) < 0.5f) return plot;
            var left = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plot) }, new[] { (IReadOnlyList<float2>)Ccw(part.ToArray()) }, BoolOp.Difference, FillRule.NonZero);
            var keep = left.OrderByDescending(r => r.Area).FirstOrDefault();
            return keep == null || keep.Area < 1f ? plot : keep.Outer;
        }

        /// <summary>Where the line p + t·d meets the line q + u·e.</summary>
        private static float2 LineHit(float2 p, float2 d, float2 q, float2 e)
        {
            float den = d.x * e.y - d.y * e.x;
            if (math.abs(den) < 1e-6f) return p;
            var w = q - p;
            float t = (w.x * e.y - w.y * e.x) / den;
            return p + d * t;
        }

        /// <summary>A line laid on a new segment, keeping its direction (its end nearer a goes to a).</summary>
        private static (float2 A, float2 B) Along(StitchLine l, float2 a, float2 b) =>
            math.distance(l.A, a) + math.distance(l.B, b) <= math.distance(l.A, b) + math.distance(l.B, a) ? (a, b) : (b, a);

/// <summary>
        /// A ring cleaned into a shape the game draws (PrepareSurface: node spacing, triangulation): tried as it is, then
        /// simplified more and more (a wavy bed edge or a cut-out leaves tiny edges the game will not take); the largest
        /// accepted piece, or null when nothing near the asked-for size is left.
        /// </summary>
        private static float2[] Prepared(IReadOnlyList<float2> ring)
        {
            if (ring == null || ring.Count < 3) return null;
            float want = Poly.Area(ring);
            foreach (float tol in new[] { 0.05f, 0.25f, 0.6f })
            {
                var r = Placemaker.Core.Build.AreaRings.PrepareSurface(Poly.Simplify(ring, tol), Placemaker.Core.Build.AreaRings.SurfaceMinNodeDistance)
                    .OrderByDescending(x => Poly.Area(x)).FirstOrDefault();
                if (r != null && r.Length >= 3 && Poly.Area(r) >= want * 0.6f) return r;
            }
            return null;
        }

        /// <summary>The house's beds trimmed round its lawn; a bed left with under half of itself goes, and bushes off a bed go too.</summary>
        private static void FitBedsAround(StitchHouse h, float2[] lawn, StitchPlan plan, List<Region> beds)
        {
            var lawnRegion = new[] { new Region(Ccw(lawn)) };
            beds.Clear();
            for (int k = plan.Beds.Count - 1; k >= 0; k--)
            {
                var (house, ring) = plan.Beds[k];
                if (house != h.Id) continue;
                var left = Poly.Boolean(new[] { new Region(Ccw(ring)) }, lawnRegion, BoolOp.Difference).OrderByDescending(r => r.Area).FirstOrDefault();
                var fitted = left != null && left.Area >= Poly.Area(ring) * 0.5f ? Prepared(left.HasHoles ? Poly.CutHoles(left, 0f).First() : left.Outer) : null;
                if (fitted == null) plan.Beds.RemoveAt(k);
                else { plan.Beds[k] = (house, fitted); beds.Add(new Region(Ccw(fitted))); }
            }
            plan.Bushes.RemoveAll(b => b.House == h.Id && b.Kind < FirstRock && !plan.Beds.Any(bd => bd.House == h.Id && Poly.Contains(bd.Ring, b.Position)) && !Feature.Contains((b.House, b.Position)));
        }

        /// <summary>A small triangle well inside the house's outline: where a garden's own grass is tucked away (drawn under the
        /// house, so not seen; given back as it was when the garden is no longer painted).</summary>
        private static float2[] UnderHouse(StitchHouse h)
        {
            if (h.Footprint == null || h.Footprint.Length < 3) return null;
            var c = Poly.Centroid(h.Footprint);
            if (!Poly.Contains(h.Footprint, c)) return null;
            var f = h.Forward;
            var r = h.Right;
            return Ccw(new[] { c + f * 0.6f, c - f * 0.3f + r * 0.52f, c - f * 0.3f - r * 0.52f });
        }

        private static uint Hash(ulong x)
        {
            x ^= x >> 33; x *= 0xff51afd7ed558ccdUL; x ^= x >> 33; x *= 0xc4ceb9fe1a85ec53UL; x ^= x >> 33;
            return (uint)x;
        }

        // ───────────── ground paint ─────────────

        /// <summary>
        /// Terrain materials (2 lush grass, 3 dirt): a soft lush fade from every fitted lawn out into the field, lush meadow on
        /// shared greens and on any wedge no lawn covers, worn dirt where a shared side meets the back fence. Painting keeps
        /// the max per texel, so painting again changes nothing.
        /// </summary>
        private static void PlanPaint(StitchPlan plan, StitchSettings s, List<Region> unpolished)
        {
            float fade = s.Knob("fadeWidth", 8f);
            foreach (var kv in plan.Lawns.Select(x => (Key: x.Key, Value: x.Value)).Concat(plan.NewLawns.Select(x => (Key: x.House, Value: x.Ring))))
            {
                var outer = Poly.Offset(Ccw(kv.Value), -fade);
                if (outer.Count == 0) continue;
                var ring = outer.OrderByDescending(r => Poly.Area(r)).First();
                if (unpolished.Count == 0) { Paint(plan, ring, (int)s.Knob("lushLayer", 2f), s.Knob("fadeStrength", 0.65f), fade, 2f, 9f, (uint)(kv.Key * 2654435761L)); continue; }
                // never onto an unpolished lot: the fade stops at its edge
                foreach (var r in Poly.Boolean(new[] { new Region(Ccw(ring)) }, unpolished, BoolOp.Difference).Where(r => r.Area >= 8f))
                    Paint(plan, r.HasHoles ? Poly.CutHoles(r, 0f).First() : r.Outer, (int)s.Knob("lushLayer", 2f), s.Knob("fadeStrength", 0.65f), math.min(fade, 3f), 2f, 9f, (uint)(kv.Key * 2654435761L) ^ (uint)r.Area);
            }
            foreach (var common in plan.Commons) Paint(plan, common, (int)s.Knob("lushLayer", 2f), s.Knob("meadowStrength", 0.9f), 3f, 2f, 8f, 0x51CEu);
            foreach (var pair in plan.Pairs)
            {
                if (pair.Edge) continue;   // its corners lie on an unpolished lot's edge: no worn patches on that lot
                // (the gap between two houses is their lawns now: painting it too left dark patches behind when a plan changed)
                if (s.Knob("gapPaint", 0f) > 0.5f && pair.Gap != null && Poly.Area(pair.Gap) >= 20f)
                {
                    var region = Poly.Normalize(Ccw(pair.Gap));
                    if (region.Count > 0) Paint(plan, region[0].Outer, (int)s.Knob("lushLayer", 2f), s.Knob("meadowStrength", 0.9f), 2.5f, 1.5f, 7f, (uint)((pair.Left * 17 + pair.Right * 3) & 0x7fffffff));
                }
                if (pair.Rear || math.lengthsq(pair.Back - pair.Front) < 1f) continue;
                float r = s.Knob("dirtBack", 3.2f);
                if (r <= 0.1f) continue;
                var blob = new float2[10];
                for (int i = 0; i < blob.Length; i++) { float ang = i * math.PI * 2f / blob.Length; blob[i] = pair.Back + new float2(math.cos(ang), math.sin(ang)) * r; }
                Paint(plan, blob, 3, s.Knob("dirtStrength", 0.6f), r * 0.8f, r * 0.35f, 3f, (uint)((pair.Left * 31 + pair.Right) & 0x7fffffff));
                // a smaller worn patch where the shared fence meets the street
                float rf = s.Knob("dirtFront", 1.8f);
                if (rf > 0.1f)
                {
                    var front = new float2[10];
                    for (int i = 0; i < front.Length; i++) { float ang = i * math.PI * 2f / front.Length; front[i] = pair.Front + new float2(math.cos(ang), math.sin(ang)) * rf; }
                    Paint(plan, front, 3, s.Knob("dirtStrength", 0.6f) * 0.8f, rf * 0.8f, rf * 0.35f, 3f, (uint)((pair.Left * 7 + pair.Right * 13) & 0x7fffffff));
                }
            }
        }

        private static void Paint(StitchPlan plan, float2[] ring, int layer, float strength, float feather, float noise, float noiseScale, uint seed) =>
            plan.Paints.Add(new Placemaker.Core.PlannedPaint
            {
                Ring = ring.Select(p => new float3(p.x, 0f, p.y)).ToArray(), Layer = layer, Strength = strength, Feather = feather, Noise = noise, NoiseScale = noiseScale, Seed = seed,
            });

        // ───────────── houses ─────────────

        /// <summary>Everything a fence must not cross: houses, sheds, pools, decks, driveways and paving (30 cm in from their edges, so a fence along one is fine).</summary>
        private static List<float2[]> Obstacles(IReadOnlyList<StitchHouse> houses)
        {
            var list = new List<float2[]>();
            foreach (var h in houses)
            {
                if (h.Footprint != null && h.Footprint.Length >= 3) list.Add(Ccw(h.Footprint));
                foreach (var r in h.Props.Concat(h.Hard))
                {
                    if (r == null || r.Length < 3) continue;
                    var inset = Inset(r);
                    if (inset != null) list.Add(inset);
                }
            }
            return list;
        }

        // a house's paving and sheds 30 cm in, by their numbers: the game hands every plan the same rings for a house until it
        // changes, so each is worked out once (shared by the plans on several workers at once). By the numbers, not the array: a
        // weak table by array spent more time finding and adding entries than the insets took (and a replay's arrays are new)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<RingKey, float2[]> s_insets = new System.Collections.Concurrent.ConcurrentDictionary<RingKey, float2[]>();
        private static int s_insetsAdded;

        /// <summary>A paving or shed ring 30 cm in (counter-clockwise), or null when it is too small for that.</summary>
        private static float2[] Inset(float2[] ring)
        {
            var key = new RingKey(ring, 0.3f);
            if (s_insets.TryGetValue(key, out var have)) return have;
            float2[] made = null;
            if (Poly.Area(ring) >= 1f)
            {
                var inset = Poly.Offset(Ccw(ring), 0.3f).OrderByDescending(x => Poly.Area(x)).FirstOrDefault();
                made = inset != null && inset.Length >= 3 ? Ccw(inset) : null;
            }
            // (a counter: ConcurrentDictionary.Count takes every lock of the table)
            if (System.Threading.Interlocked.Increment(ref s_insetsAdded) > 60000) { s_insets.Clear(); s_insetsAdded = 0; }
            s_insets[key] = made;
            return made;
        }

        private static void TrimAtObstacles(IReadOnlyList<StitchHouse> houses, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            // the houses' own (their footprints, sheds, paving) first, then the barriers in their order (looked up in the
            // barriers' own grid, BarriersIn: every road and building of the patch copied and filed again for every plan was
            // most of this step's garbage). The numbering is as one list: houses' 0.., barriers' after them
            var footprints = Obstacles(houses);
            int own = footprints.Count;
            var fpLo = new float2[own];
            var fpHi = new float2[own];
            for (int f = 0; f < own; f++) Poly.Bounds(footprints[f], out fpLo[f], out fpHi[f]);
            float2 Lo(int f) => f < own ? fpLo[f] : s_barriers[f - own].Item1;
            float2 Hi(int f) => f < own ? fpHi[f] : s_barriers[f - own].Item2;
            float2[] Ring(int f) => f < own ? footprints[f] : s_barriers[f - own].Item3;
            // filed by 16 m cell (a line only looks at the ones in its cells, in their order: every line against every house,
            // shed, road and building of the patch was a tenth of a plan in the game's runtime)
            const float TrimCell = 16f;
            var fpCells = new Dictionary<long, List<int>>();
            var fpBig = new List<int>();
            for (int f = 0; f < own; f++)
            {
                int cx0 = (int)math.floor(fpLo[f].x / TrimCell), cx1 = (int)math.floor(fpHi[f].x / TrimCell), cy0 = (int)math.floor(fpLo[f].y / TrimCell), cy1 = (int)math.floor(fpHi[f].y / TrimCell);
                if ((long)(cx1 - cx0 + 1) * (cy1 - cy0 + 1) > 4096) { fpBig.Add(f); continue; }   // (very big: looked at by every line)
                for (int x = cx0; x <= cx1; x++)
                    for (int y = cy0; y <= cy1; y++)
                    {
                        long key = ((long)x << 32) ^ (uint)y;
                        if (!fpCells.TryGetValue(key, out var l)) fpCells[key] = l = new List<int>();
                        l.Add(f);
                    }
            }
            var fpSeen = new int[own];
            int fpStamp = 0;
            var fpNear = new List<int>();
            var barriersNear = new List<int>();
            var inside = new List<float2>();
            int trimmed = 0, dropped = 0;
            foreach (var id in moved.Keys.ToList())
            {
                if (plan.HideLines.Contains(id)) continue;
                var m = moved[id];
                float len = math.distance(m.A, m.B);
                if (len < 0.5f) continue;
                inside.Clear();
                var mlo = math.min(m.A, m.B);
                var mhi = math.max(m.A, m.B);
                fpNear.Clear();
                fpStamp++;
                for (int x = (int)math.floor(mlo.x / TrimCell); x <= (int)math.floor(mhi.x / TrimCell); x++)
                    for (int y = (int)math.floor(mlo.y / TrimCell); y <= (int)math.floor(mhi.y / TrimCell); y++)
                        if (fpCells.TryGetValue(((long)x << 32) ^ (uint)y, out var l))
                            foreach (int f in l) if (fpSeen[f] != fpStamp) { fpSeen[f] = fpStamp; fpNear.Add(f); }
                fpNear.AddRange(fpBig);
                BarriersIn(mlo, mhi, barriersNear);
                foreach (int k in barriersNear) fpNear.Add(own + k);
                fpNear.Sort();
                foreach (int f in fpNear)
                {
                    var flo = Lo(f);
                    var fhi = Hi(f);
                    if (fhi.x < mlo.x || flo.x > mhi.x || fhi.y < mlo.y || flo.y > mhi.y) continue;
                    Poly.ClipSegmentIntervals(Ring(f), m.A, m.B, inside);
                }
                if (inside.Sum(iv => iv.y - iv.x) * len < 0.3f) continue;
                // the clear stretches between the houses' intervals; keep the longest
                inside.Sort((p, q) => p.x.CompareTo(q.x));
                float best0 = 0f, best1 = 0f, from = 0f;
                foreach (var iv in inside)
                {
                    if (iv.x - from > best1 - best0) { best0 = from; best1 = iv.x; }
                    from = math.max(from, iv.y);
                }
                if (1f - from > best1 - best0) { best0 = from; best1 = 1f; }
                if ((best1 - best0) * len < s.Knob("minLine", 2f)) { plan.HideLines.Add(id); dropped++; continue; }
                // pull back from the wall a little
                float pad = math.min(0.4f / len, (best1 - best0) * 0.25f);
                moved[id] = (math.lerp(m.A, m.B, best0 > 0f ? best0 + pad : 0f), math.lerp(m.A, m.B, best1 < 1f ? best1 - pad : 1f));
                trimmed++;
            }
            if (trimmed + dropped > 0) plan.Notes.Add($"{trimmed} moved lines stop at a house, shed, driveway, road or path, {dropped} too short left out");
        }

        /// <summary>
        /// A surface cannot have holes: each one (a patio or the house inside a lawn that now goes all the way round) is
        /// opened with a 0.4 m slit to the outside, in whichever of the house's four directions costs the least lawn.
        /// </summary>
        private static Region OpenHoles(Region main, StitchHouse h)
        {
            for (int guard = 0; guard < 6 && main.HasHoles; guard++)
            {
                var hole = main.Holes.OrderByDescending(r => Poly.Area(r)).First();
                var c = Poly.Centroid(hole);
                if (!Poly.Contains(hole, c)) c = hole[0];
                Region best = null;
                float bestLoss = float.MaxValue;
                foreach (var dir in new[] { h.Forward, -h.Forward, h.Right, -h.Right })
                {
                    var nrm = new float2(-dir.y, dir.x) * 0.2f;
                    var far = c + dir * 400f;
                    var strip = Ccw(new[] { c - nrm, far - nrm, far + nrm, c + nrm });
                    var parts = Poly.Boolean(new[] { main }, new[] { new Region(strip) }, BoolOp.Difference);
                    var keep = parts.OrderByDescending(r => r.Area).FirstOrDefault();
                    if (keep == null || keep.Holes.Count >= main.Holes.Count) continue;
                    float loss = main.Area - keep.Area;
                    if (h.Footprint != null && h.Footprint.Length >= 3)
                    {
                        var removed = Poly.Boolean(new[] { main }, new[] { keep }, BoolOp.Difference);
                        loss -= Poly.Boolean(removed, new[] { new Region(Ccw(h.Footprint)) }, BoolOp.Intersection).Sum(r => r.Area);
                    }
                    if (loss < bestLoss) { bestLoss = loss; best = keep; }
                }
                if (best == null) return new Region(Poly.CutHoles(main, 0f).First());
                main = best;
            }
            return main.HasHoles ? new Region(main.Outer) : main;
        }

        // ───────────── geometry helpers ─────────────

        /// <summary>How much of the segment a..b the given lines cover (lying along it within 0.6 m), in metres.</summary>
        private static float Covered(float2 a, float2 b, IEnumerable<(float2 A, float2 B)> lines)
        {
            float len = math.distance(a, b);
            var d = (b - a) / len;
            var nrm = new float2(-d.y, d.x);
            var spans = new List<float2>();
            foreach (var (p, q) in lines)
            {
                if (math.abs(math.dot(p - a, nrm)) > 0.6f || math.abs(math.dot(q - a, nrm)) > 0.6f) continue;
                float t0 = math.dot(p - a, d), t1 = math.dot(q - a, d);
                float lo = math.max(0f, math.min(t0, t1)), hi = math.min(len, math.max(t0, t1));
                if (hi > lo) spans.Add(new float2(lo, hi));
            }
            spans.Sort((x, y) => x.x.CompareTo(y.x));
            float total = 0f, end = 0f;
            foreach (var sp in spans)
            {
                if (sp.y <= end) continue;
                total += sp.y - math.max(sp.x, end);
                end = sp.y;
            }
            return total;
        }

        private static float DistanceToRing(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var e = ring[(i + 1) % ring.Length] - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * t));
            }
            return best;
        }

        /// <summary>The same, given up as soon as it is below <paramref name="stop"/> (then only known to be below it).</summary>
        private static float DistanceToRing(float2[] ring, float2 p, float stop)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length && best >= stop; i++)
            {
                var a = ring[i];
                var e = ring[(i + 1) % ring.Length] - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * t));
            }
            return best;
        }

        /// <summary>A lot edge (0 left, 1 right, 2 back) with its outward normal.</summary>
        private static (float2 A, float2 B, float2 N) Edge(StitchHouse h, int e) =>
            e == 0 ? (h.FrontLeft, h.BackLeft, -h.Right) : e == 1 ? (h.FrontRight, h.BackRight, h.Right) : (h.BackLeft, h.BackRight, -h.Forward);

        /// <summary>
        /// Edge ei of a faces edge ej of b: (nearly) opposite outward normals, b's edge in front of a's (at most
        /// <paramref name="maxGap"/>), overlapping along it by <paramref name="minOverlap"/>. The cut is their middle line.
        /// </summary>
        private static bool Faces(StitchHouse a, int ei, StitchHouse b, int ej, float maxGap, float minOverlap, out float gap, out Cut cut)
        {
            gap = 0f; cut = null;
            var (a0, a1, na) = Edge(a, ei);
            var (b0, b1, nb) = Edge(b, ej);
            if (math.dot(na, nb) > -0.9f) return false;   // within ~25° of facing
            gap = math.dot((b0 + b1) * 0.5f - (a0 + a1) * 0.5f, na);
            if (gap < -1f || gap > maxGap || math.dot((a0 + a1) * 0.5f - (b0 + b1) * 0.5f, nb) < -1f) return false;
            float len = math.distance(a0, a1);
            var d = (a1 - a0) / len;
            float u0 = math.dot(b0 - a0, d), u1 = math.dot(b1 - a0, d);
            float lo = math.max(0f, math.min(u0, u1)), hi = math.min(len, math.max(u0, u1));
            if (hi - lo < minOverlap) return false;
            gap = math.max(0f, gap);
            var nrm = math.normalizesafe(na - nb, na);
            cut = new Cut { P = a0 + d * ((lo + hi) * 0.5f) + na * (gap * 0.5f), D = new float2(-nrm.y, nrm.x) };
            return true;
        }

        private static float Deep(StitchHouse a, StitchHouse b) => math.max(a.HalfLot.y, b.HalfLot.y);

        private static float2[] Lot(StitchHouse h) => new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };

        /// <summary>A house's side edge (+1 right, -1 left) followed from its street edge to <paramref name="halfDepth"/> × 2 deep.</summary>
        private static float2 BackAt(StitchHouse h, int side, float halfDepth) => h.ToWorld(new float2(side * h.HalfLot.x, h.HalfLot.y - 2f * halfDepth));

        private static bool OnLotEdge(StitchHouse h, float2 p, float tol)
        {
            var l = h.ToLocal(p);
            bool inX = math.abs(l.x) <= h.HalfLot.x + tol, inZ = math.abs(l.y) <= h.HalfLot.y + tol;
            return (inX && math.abs(math.abs(l.y) - h.HalfLot.y) < tol) || (inZ && math.abs(math.abs(l.x) - h.HalfLot.x) < tol);
        }

        /// <summary>Two houses back to back: the gap between their back edges and how far those overlap side to side.</summary>
        private static bool RearGap(StitchHouse a, StitchHouse b, out float gap, out float overlap)
        {
            gap = overlap = 0f;
            var backA = a.Position - a.Forward * a.HalfLot.y;
            var backB = b.Position - b.Forward * b.HalfLot.y;
            float gapA = math.dot(backB - backA, -a.Forward), gapB = math.dot(backA - backB, -b.Forward);
            if (gapA < -2f || gapB < -2f) return false;
            gap = math.max(0f, (gapA + gapB) * 0.5f);
            var dir = math.normalizesafe(a.Right - b.Right, a.Right);
            var mid = (backA + backB) * 0.5f;
            float Along(float2 p) => math.dot(p - mid, dir);
            float a0 = math.min(Along(a.BackLeft), Along(a.BackRight)), a1 = math.max(Along(a.BackLeft), Along(a.BackRight));
            float b0 = math.min(Along(b.BackLeft), Along(b.BackRight)), b1 = math.max(Along(b.BackLeft), Along(b.BackRight));
            overlap = math.min(a1, b1) - math.max(a0, b0);
            return true;
        }

        /// <summary>Sutherland–Hodgman: the part of a convex polygon on the side of the line (p, d) where <paramref name="keep"/> lies.</summary>
        /// <summary>
        /// Going back from the back of the house's lot, the first depth (local z) where the garden is narrower than minW across
        /// (and stays so for 3 m: a fence corner is no spike); null when it never gets that narrow.
        /// </summary>
        private static float? NarrowsAt(IReadOnlyList<float2> plot, StitchHouse h, float minW, float until = float.MinValue, bool pieces = false)
        {
            // (pieces: the widest single piece across counts, not the whole span: two prongs side by side are narrow)
            var xs = pieces ? new List<float>() : null;
            // (until: no further back than this, the house's local z: where the garden really ends)
            float zMin = float.MaxValue;
            var local = new float2[plot.Count];
            for (int k = 0; k < plot.Count; k++)
            {
                var d = plot[k] - h.Position;
                local[k] = new float2(math.dot(d, h.Right), math.dot(d, h.Forward));
                zMin = math.min(zMin, local[k].y);
            }
            zMin = math.max(zMin, until);
            float z0 = -h.HalfLot.y;
            if (zMin > z0 - 4f) return null;
            float? start = null;
            for (float z = z0; z > zMin; z -= 1f)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                xs?.Clear();
                for (int k = 0; k < local.Length; k++)
                {
                    var a = local[k];
                    var b = local[(k + 1) % local.Length];
                    if ((a.y - z) * (b.y - z) > 0f || a.y == b.y) continue;
                    float x = math.lerp(a.x, b.x, (z - a.y) / (b.y - a.y));
                    lo = math.min(lo, x);
                    hi = math.max(hi, x);
                    xs?.Add(x);
                }
                float width = hi - lo;
                if (xs != null && xs.Count >= 2)
                {
                    xs.Sort();
                    width = 0f;
                    for (int k = 0; k + 1 < xs.Count; k += 2) width = math.max(width, xs[k + 1] - xs[k]);
                }
                bool narrow = width < minW;
                if (!narrow) { start = null; continue; }
                start ??= z;
                if (start.Value - z >= 3f) return start;
            }
            return start;
        }

        /// <summary>Behind depth z (the house's local z) the garden is one piece across that only gets narrower (by more than half a
        /// metre never wider) to its end.</summary>
        private static bool TapersToEnd(float2[] plot, StitchHouse h, float z)
        {
            var local = new float2[plot.Length];
            float zMin = float.MaxValue;
            for (int k = 0; k < plot.Length; k++)
            {
                var d = plot[k] - h.Position;
                local[k] = new float2(math.dot(d, h.Right), math.dot(d, h.Forward));
                zMin = math.min(zMin, local[k].y);
            }
            var xs = new List<float>();
            float last = float.MaxValue;
            for (float at = z; at > zMin + 0.25f; at -= 1f)
            {
                xs.Clear();
                for (int k = 0; k < local.Length; k++)
                {
                    var a = local[k];
                    var b = local[(k + 1) % local.Length];
                    if ((a.y - at) * (b.y - at) > 0f || a.y == b.y) continue;
                    xs.Add(math.lerp(a.x, b.x, (at - a.y) / (b.y - a.y)));
                }
                if (xs.Count > 2) return false;   // prongs
                if (xs.Count < 2) continue;
                float width = math.abs(xs[1] - xs[0]);
                if (width > last + 0.5f) return false;   // wider again
                last = math.min(last, width);
            }
            return true;
        }

        /// <summary>A garden that, behind its lot, splits into prongs or runs on narrower than minGardenWidth (half its lot at
        /// least) ends straight across there, after every cut at roads, lots and gardens and the land shared later: squeezed
        /// between two roads at an angle it ran on in a neck or two prongs (the user, 23 Oct: "cap that gap off and just be
        /// done there"; knob capNarrow 0: as before).</summary>
        private static void CapNarrow(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchSettings s, StitchPlan plan)
        {
            int capped = 0;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!touched[i] || !h.Edit || !h.Polished || IsFixed(h) || plots[i] == null || plots[i].Length < 3) continue;
                float minW = math.max(K(h, s, "minGardenWidth", 8f), h.HalfLot.x);
                float? z = NarrowsAt(plots[i], h, minW, float.MinValue, true);
                if (z == null) continue;
                // a garden that only tapers to its end (a street behind it at an angle, a neighbour's slanted line) ends there as it
                // is: capped, a grass triangle stayed between its fence and the kerb (the test "no grass triangle left"); only a neck
                // that widens again or prongs are capped
                if (TapersToEnd(plots[i], h, z.Value)) continue;
                // the part in front of the line across (the house's side)
                const float big = 2000f;
                var front = Ccw(new[] { h.ToWorld(new float2(-big, z.Value)), h.ToWorld(new float2(big, z.Value)), h.ToWorld(new float2(big, big)), h.ToWorld(new float2(-big, big)) });
                var parts = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plots[i]) }, new[] { (IReadOnlyList<float2>)front }, BoolOp.Intersection, FillRule.NonZero);
                var keep = parts.OrderByDescending(r => Poly.Contains(r.Outer, h.Position) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
                if (keep == null) continue;
                var outer = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(r => Poly.Area(r)).First() : keep.Outer;
                if (outer == null || outer.Length < 3 || Poly.Area(outer) < 1f) continue;
                plots[i] = Poly.Simplify(outer, 0.05f);
                capped++;
            }
            if (capped > 0) plan.Notes.Add($"{capped} gardens end straight across where they get narrow or split (capped)");
        }

        private static List<float2> ClipHalfPlane(List<float2> poly, float2 p, float2 d, float2 keep)
        {
            float Side(float2 x) => d.x * (x.y - p.y) - d.y * (x.x - p.x);
            float sign = math.sign(Side(keep));
            if (sign == 0f || poly.Count < 3) return poly;
            var result = new List<float2>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                float sa = Side(a) * sign, sb = Side(b) * sign;
                if (sa >= 0f) result.Add(a);
                if ((sa >= 0f) != (sb >= 0f)) result.Add(a + (b - a) * (sa / (sa - sb)));
            }
            return result;
        }

        /// <summary>The stretch of a plot's boundary lying on a cut line, as parameters along the line (false: none).</summary>
        private static bool EdgeOn(float2[] plot, Cut cut, out float t0, out float t1)
        {
            t0 = float.MaxValue; t1 = float.MinValue;
            var nrm = new float2(-cut.D.y, cut.D.x);
            foreach (var v in plot)
            {
                if (math.abs(math.dot(v - cut.P, nrm)) > 0.05f) continue;
                float t = math.dot(v - cut.P, cut.D);
                t0 = math.min(t0, t);
                t1 = math.max(t1, t);
            }
            return t1 - t0 > 0.5f;
        }

        /// <summary>How far back the plot runs along the house's side line (+1 right, -1 left): its corner there furthest back.</summary>
        private static bool SideBack(float2[] plot, StitchHouse h, int side, out float2 corner)
        {
            corner = default;
            float best = float.MaxValue;
            foreach (var p in plot)
            {
                var l = h.ToLocal(p);
                if (math.abs(l.x - side * h.HalfLot.x) > 0.3f || l.y >= best) continue;
                best = l.y;
                corner = p;
            }
            return best < float.MaxValue;
        }

        /// <summary>The plot's edge across the house's back: the edge running side to side, behind the lot's middle, furthest back
        /// (a ring line can end a plot before the lot's own back).</summary>
        private static bool BackEdge(StitchHouse h, float2[] plot, out float2 e0, out float2 e1)
        {
            e0 = e1 = default;
            float back = float.MaxValue;
            for (int i = 0; i < plot.Length; i++)
            {
                var a = plot[i];
                var b = plot[(i + 1) % plot.Length];
                var la = h.ToLocal(a);
                var lb = h.ToLocal(b);
                if (la.y > -0.5f || lb.y > -0.5f || math.distance(a, b) < 0.5f) continue;
                if (math.abs(la.x - lb.x) < 2f * math.abs(la.y - lb.y)) continue;   // across the back, not down a side
                float y = (la.y + lb.y) * 0.5f;
                if (y < back) { back = y; e0 = a; e1 = b; }
            }
            return back < float.MaxValue;
        }

        /// <summary>Where a line, carried on from <paramref name="from"/> through <paramref name="end"/>, leaves the plot.</summary>
        private static bool RayToPlotEdge(float2[] plot, float2 from, float2 end, out float2 hit, float minT = 0.999f)
        {
            hit = end;
            var d = end - from;
            if (math.lengthsq(d) < 1e-4f) return false;
            float best = float.MaxValue;
            for (int i = 0; i < plot.Length; i++)
            {
                var a = plot[i];
                var e = plot[(i + 1) % plot.Length] - a;
                float den = d.x * e.y - d.y * e.x;
                if (math.abs(den) < 1e-6f) continue;
                var w = a - from;
                float t = (w.x * e.y - w.y * e.x) / den, u = (w.x * d.y - w.y * d.x) / den;
                if (u < -1e-4f || u > 1f + 1e-4f || t < minT) continue;
                if (t < best) { best = t; hit = from + d * t; }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Moved lines: only the stretch that lies on a garden's edge stays (a line carried past where its garden now ends, or
        /// into a strip a garden took, would stand in the middle of a garden); none of it on an edge: it goes.
        /// </summary>
        private static void LinesOnEdges(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            int n = houses.Count;
            var plotLo = new float2[n];
            var plotHi = new float2[n];
            for (int j = 0; j < n; j++) if (plots[j] != null) Poly.Bounds(plots[j], out plotLo[j], out plotHi[j]);
            for (int i = 0; i < n; i++)
            {
                if (!touched[i] || plots[i] == null) continue;
                foreach (var l in houses[i].Lines)
                {
                    if (plan.HideLines.Contains(l.Id) || !moved.TryGetValue(l.Id, out var m)) continue;
                    float len = math.distance(m.A, m.B);
                    if (len < 0.5f) continue;
                    int steps = math.max(2, (int)(len / 0.5f));
                    int run0 = -1, best0 = 0, best1 = -1;
                    for (int k = 0; k <= steps; k++)
                    {
                        var p = math.lerp(m.A, m.B, (float)k / steps);
                        bool onEdge = false;
                        for (int j = 0; j < n && !onEdge; j++)
                            if (plots[j] != null && p.x >= plotLo[j].x - 1f && p.y >= plotLo[j].y - 1f && p.x <= plotHi[j].x + 1f && p.y <= plotHi[j].y + 1f && DistanceToRing(plots[j], p) < 1.0f) onEdge = true;
                        if (onEdge && run0 < 0) run0 = k;
                        if ((!onEdge || k == steps) && run0 >= 0)
                        {
                            int end = onEdge ? k : k - 1;
                            if (end - run0 > best1 - best0) { best0 = run0; best1 = end; }
                            run0 = -1;
                        }
                    }
                    float kept0 = (float)best0 / steps, kept1 = (float)best1 / steps;
                    if (best1 < 0 || (kept1 - kept0) * len < s.Knob("minLine", 2f)) { plan.HideLines.Add(l.Id); plan.Notes.Add($"{houses[i].Id}: a line inside its garden goes"); continue; }
                    if (kept0 > 0.001f || kept1 < 0.999f) moved[l.Id] = (math.lerp(m.A, m.B, kept0), math.lerp(m.A, m.B, kept1));
                }
            }
        }

        /// <summary>The fence and hedge lines as they will stand once this plan is written: the edited houses' where it puts
        /// them, a polished house it does not edit as it stands now (its lines and closing pieces: another pass placed them,
        /// this plan never moves them), an unpolished lot's where the game put them.</summary>
        private static List<(float2 A, float2 B)> DrawnLines(IReadOnlyList<StitchHouse> houses, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved)
        {
            var list = new List<(float2 A, float2 B)>();
            foreach (var h in houses)
            {
                if (h.Polished && !h.Edit && h.Standing != null) { list.AddRange(h.Standing); continue; }
                foreach (var l in h.Lines)
                    if (!plan.HideLines.Contains(l.Id)) list.Add(h.Polished && moved.TryGetValue(l.Id, out var m) ? m : (l.A, l.B));
            }
            return list;
        }

        /// <summary>
        /// Every open edge of a polished garden (not its street front) closed: pieces of the house's own fence or hedge kind (or
        /// the look's) along it, following its bends; an edge two gardens share gets them once. Open: no line runs along it as
        /// the lines will stand (DrawnLines: a neighbour another pass planned counts as it really stands).
        /// </summary>
        private static void CloseGardens(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            int n = houses.Count;
            var drawn = DrawnLines(houses, plan, moved);
            var walls = Obstacles(houses);
            var wallLo = new float2[walls.Count];
            var wallHi = new float2[walls.Count];
            for (int w = 0; w < walls.Count; w++) Poly.Bounds(walls[w], out wallLo[w], out wallHi[w]);
            // (the houses themselves: along a house's wall is no place for a fence either, its own or the next one's: row houses
            // touching share a wall on their lot line, and a hedge laid along it stuck out of the front onto the pavement)
            bool[] isHouse = new bool[walls.Count];
            for (int w = 0; w < walls.Count; w++) isHouse[w] = false;
            {
                int w = 0;
                foreach (var h in houses)
                {
                    if (h.Footprint != null && h.Footprint.Length >= 3) { isHouse[w] = true; wallLo[w] -= 0.35f; wallHi[w] += 0.35f; w++; }
                    foreach (var r in h.Props.Concat(h.Hard)) if (r != null && r.Length >= 3 && Inset(r) != null) w++;
                }
            }
            // the lines (drawn, and the pieces placed so far) by 4 m cell: a point only looks at the lines in its own cell
            var near = (s_closeGrid ??= new LineGrid(4f, 1f)).Reset(4f, 1f);
            foreach (var l in drawn) near.Add(l.A, l.B);
            bool Covered(float2 p, float2 dir)
            {
                // (its cell's lines, then the ones too big to file: one loop, no list joined for every point)
                var cellLines = near.At(p);
                int lineCount = cellLines.Count + near.Big.Count;
                for (int q = 0; q < lineCount; q++)
                {
                    var (a, b) = q < cellLines.Count ? cellLines[q] : near.Big[q - cellLines.Count];
                    var e = b - a;
                    float len2 = math.dot(e, e);
                    if (len2 < 0.01f) continue;
                    float t = math.dot(p - a, e) / len2;
                    if (t < -0.02f || t > 1.02f) continue;
                    if (math.distance(p, a + e * math.saturate(t)) > 0.9f) continue;
                    if (math.abs(math.dot(math.normalize(e), dir)) > 0.85f) return true;
                }
                return false;
            }
            // the walls by 8 m cell (each in every cell its bounds touch): a point only looks at its own cell's
            const float WallCell = 8f;
            var wallCells = new Dictionary<long, List<int>>();
            for (int w = 0; w < walls.Count; w++)
                for (int x = (int)math.floor(wallLo[w].x / WallCell); x <= (int)math.floor(wallHi[w].x / WallCell); x++)
                    for (int y = (int)math.floor(wallLo[w].y / WallCell); y <= (int)math.floor(wallHi[w].y / WallCell); y++)
                    {
                        long key = ((long)x << 32) ^ (uint)y;
                        if (!wallCells.TryGetValue(key, out var l)) wallCells[key] = l = new List<int>();
                        l.Add(w);
                    }
            bool InWall(float2 at)
            {
                if (!wallCells.TryGetValue(((long)(int)math.floor(at.x / WallCell) << 32) ^ (uint)(int)math.floor(at.y / WallCell), out var l)) return false;
                const float AlongWall = 0.35f;
                foreach (int w in l)
                {
                    if (at.x < wallLo[w].x || at.y < wallLo[w].y || at.x > wallHi[w].x || at.y > wallHi[w].y) continue;   // (a house's bounds grown by AlongWall)
                    if (Poly.Contains(walls[w], at) || isHouse[w] && DistanceToRing(walls[w], at) < AlongWall) return true;
                }
                return false;
            }
            int pieces = 0, gardens = 0;
            bool runs = s.Knob("closeRuns", 1f) > 0.5f;
            for (int i = 0; i < n; i++)
            {
                var h = houses[i];
                if (!touched[i] || !h.Edit || !h.Polished || plots[i] == null || K(h, s, "closeGardens", 1f) < 0.5f) continue;
                string piece = PieceFor(h, (int)K(h, s, "fenceKind", 0f));
                var ring = Ccw(plots[i]);
                int before = plan.FencePieces.Count;
                if (runs)
                {
                    CloseRuns(h, ring, piece, plan, near, InWall, OpenFrontFrom(h, s), s.Knob("emergent", 1f) > 0.5f);
                    if (plan.FencePieces.Count > before) { gardens++; pieces += plan.FencePieces.Count - before; }
                    continue;
                }
                for (int k = 0; k < ring.Length; k++)
                {
                    var a = ring[k];
                    var b = ring[(k + 1) % ring.Length];
                    float len = math.distance(a, b);
                    if (len < 1.5f) continue;
                    // the street front stays open (the lot's front line, carried along the street)
                    if (h.ToLocal(a).y >= h.HalfLot.y - 1.2f && h.ToLocal(b).y >= h.HalfLot.y - 1.2f) continue;
                    var dir = (b - a) / len;
                    // the stretches no line runs along
                    float step = 0.5f, run0 = -1f;
                    var open = new List<(float T0, float T1)>();
                    for (float d = 0f; d <= len + 1e-3f; d += step)
                    {
                        bool free = !Covered(a + dir * math.min(d, len), dir);
                        if (free && run0 < 0f) run0 = d;
                        if ((!free || d + step > len + 1e-3f) && run0 >= 0f) { open.Add((run0, math.min(d, len))); run0 = -1f; }
                    }
                    foreach (var (t0, t1) in open)
                    {
                        float l = t1 - t0;
                        if (l < 2f) continue;
                        var p0 = a + dir * (t0 + 0.2f);
                        var p1 = a + dir * (t1 - 0.2f);
                        float pl = math.distance(p0, p1);
                        // end to end, a hair over each other: no gaps (the house's own pieces never hide each other)
                        int count = math.max(1, (int)math.ceil(pl / 3.95f));
                        float yaw = math.atan2(dir.x, dir.y);
                        for (int c = 0; c < count && plan.FencePieces.Count - before < 300; c++)
                        {
                            var at = math.lerp(p0, p1, (c + 0.5f) / count);
                            // never through a house, shed, driveway, road or another building
                            if (InWall(at) || OnBarrier(at)) continue;
                            plan.FencePieces.Add((h.Id, at, yaw, piece));
                        }
                        near.Add(p0, p1);
                    }
                }
                if (plan.FencePieces.Count > before) { gardens++; pieces += plan.FencePieces.Count - before; }
            }
            if (pieces > 0) plan.Notes.Add($"{gardens} gardens closed with {pieces} fence and hedge pieces");
            if (runs && s.Knob("joinEnds", 1f) > 0.5f) JoinEnds(houses, plots, touched, plan, moved, s);
        }

        // ───────────── closing a garden: runs of pieces ─────────────

        /// <summary>
        /// Where a garden's front yard begins (the house's local z): in front of the house's front wall a garden stays open to
        /// its street, beside it too; no piece is laid there. Row houses, lots touching and no fences of their own, had a
        /// hedge run down every side line through the front yards to the pavement (the user, 23 Oct: "fences sticking out in
        /// front of them"). Paved land (a car park, a yard) is closed as before; a house with no footprint known, too.
        /// </summary>
        private static float OpenFrontFrom(StitchHouse h, StitchSettings s)
        {
            if (HardLand(h, s) || h.Footprint == null || h.Footprint.Length < 3 || s.Knob("openFrontYards", 1f) < 0.5f) return float.MaxValue;
            float front = float.MinValue;
            foreach (var p in h.Footprint) front = math.max(front, h.ToLocal(p).y);
            // (a row house fills its lot nearly to the street: the metre left in front of it is open too; a piece laid there
            // stuck out of its facade onto the pavement)
            return front + 0.25f;
        }

        /// <summary>The length a fence or hedge piece covers (vanilla pieces are 4 m, their short ones 2 m).</summary>
        private const float PieceLong = 4f, PieceShort = 2f;

        private enum EdgeState : byte { Open, Covered, Wall, Front }

        /// <summary>
        /// One garden closed (the user, 21 Oct: gaps in fence runs at roads and turning circles, corners that do not meet, steps
        /// between a fence and the next): its edge walked every 25 cm, the stretches no line runs along (not its street front,
        /// not through a house, shed, driveway, road or building) joined across the plot's corners into runs, and each run laid
        /// end to end with pieces. Before, every edge was closed alone: a piece centred in each edge's open stretch, 20 cm short
        /// of both ends (a notch at every corner), edges under 1.5 m and stretches under 2 m left out (a curve's short edges
        /// were gaps), and a piece whose middle touched a driveway or a road left out whole (a 4 m gap). A run's end against a
        /// line goes onto that line's end (no step where they meet).
        /// </summary>
        private static void CloseRuns(StitchHouse h, float2[] ring, string piece, StitchPlan plan, LineGrid near, Func<float2, bool> inWall, float openFrom = float.MaxValue, bool openSideStreet = false)
        {
            // (openSideStreet: a side yard along a street stays open like the front, beside the lot within its depth; a garden
            // past the house's back is fenced along a street. The emergent split gives a corner lot the strip beside it at the
            // street, which the lot's own edge kept open before)
            bool SideStreet(float2 p)
            {
                if (!openSideStreet) return false;
                var l = h.ToLocal(p);
                return l.y >= -h.HalfLot.y - 0.5f && math.abs(l.x) >= h.HalfLot.x - 0.5f && RoadNear(p, 2.5f);
            }
            // what a point along an edge going in direction dir is: open, along a line (and which), in a wall, the street front
            (EdgeState State, float2 A, float2 B) At(float2 p, float2 dir, bool front)
            {
                if (front) return (EdgeState.Front, default, default);
                if (inWall(p) || OnBarrier(p)) return (EdgeState.Wall, default, default);
                // (its cell's lines, then the ones too big to file: one loop, no list joined for every point)
                var cellLines = near.At(p);
                int lineCount = cellLines.Count + near.Big.Count;
                for (int q = 0; q < lineCount; q++)
                {
                    var (a, b) = q < cellLines.Count ? cellLines[q] : near.Big[q - cellLines.Count];
                    var e = b - a;
                    float len2 = math.dot(e, e);
                    if (len2 < 0.01f) continue;
                    float t = math.dot(p - a, e) / len2;
                    if (t < -0.02f || t > 1.02f) continue;
                    if (math.distance(p, a + e * math.saturate(t)) > 0.9f) continue;
                    if (math.abs(math.dot(math.normalize(e), dir)) > 0.85f) return (EdgeState.Covered, a, b);
                }
                return (EdgeState.Open, default, default);
            }
            var pts = new List<float2>();
            var states = new List<(EdgeState State, float2 A, float2 B)>();
            var corner = new List<bool>();
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % ring.Length];
                float len = math.distance(a, b);
                if (len < 0.01f) continue;
                var dir = (b - a) / len;
                // the street front stays open (the lot's front line, carried along the street)
                bool front = h.ToLocal(a).y >= h.HalfLot.y - 1.2f && h.ToLocal(b).y >= h.HalfLot.y - 1.2f;
                int steps = math.max(1, (int)math.ceil(len / 0.25f));
                for (int st = 0; st < steps; st++)
                {
                    var p = a + dir * (len * st / steps);
                    pts.Add(p);
                    states.Add(At(p, dir, front || h.ToLocal(p).y > openFrom || SideStreet(p)));
                    corner.Add(st == 0);
                }
            }
            int m = pts.Count;
            if (m < 2) return;
            // start where the edge is not open (a garden open all round: anywhere, the run closes on itself)
            int start = states.FindIndex(x => x.State != EdgeState.Open);
            bool loop = start < 0;
            if (loop) start = 0;
            var run = new List<float2>();
            var runCorner = new List<bool>();
            (EdgeState State, float2 A, float2 B) before = default;
            int cap = plan.FencePieces.Count + 400;
            for (int k = 0; k <= m; k++)
            {
                int idx = (start + k) % m;
                bool last = k == m;
                var st = states[idx];
                if (!last && st.State == EdgeState.Open)
                {
                    if (run.Count == 0 && k > 0)
                    {
                        // it starts on the point that stopped the edge before it (as it ends on the one after it)
                        int prev = (start + k - 1) % m;
                        before = states[prev];
                        run.Add(pts[prev]);
                        runCorner.Add(corner[prev]);
                    }
                    run.Add(pts[idx]);
                    runCorner.Add(corner[idx]);
                    continue;
                }
                if (run.Count > 0)
                {
                    if (loop) { run.Add(run[0]); runCorner.Add(true); }
                    else
                    {
                        // the run ends on the first point of what stops it (25 cm on); against a line, on that line's end
                        run.Add(pts[idx]);
                        runCorner.Add(corner[idx]);
                        SnapEnd(run, runCorner, true, before);
                        SnapEnd(run, runCorner, false, st);
                    }
                    if (plan.FencePieces.Count < cap) LayRun(run, runCorner, h.Id, piece, plan);
                    for (int r = 0; r + 1 < run.Count; r++) near.Add(run[r], run[r + 1]);
                    run.Clear();
                    runCorner.Clear();
                }
            }
        }

        /// <summary>A run's end next to a line (it stopped because a line runs along there): onto that line's nearer end when
        /// it is close, else straight across onto the line (a short piece across the step).</summary>
        private static void SnapEnd(List<float2> run, List<bool> runCorner, bool first, (EdgeState State, float2 A, float2 B) by)
        {
            if (by.State != EdgeState.Covered || run.Count < 2) return;
            var end = first ? run[0] : run[run.Count - 1];
            var inner = first ? run[1] : run[run.Count - 2];
            // the nearer end of the line it ran into, when that line ends here (not one of another garden's run ends near
            // by: two gardens snapping to each other's ends made a V of short pieces)
            var q = math.distance(by.A, end) <= math.distance(by.B, end) ? by.A : by.B;
            if (math.distance(q, end) < 1.6f && math.dot(q - inner, end - inner) > -0.01f)
            {
                if (math.distance(q, end) > 0.05f) { if (first) run[0] = q; else run[run.Count - 1] = q; }
                return;
            }
            // no end near: across onto the line it runs into
            var e = by.B - by.A;
            float t = math.saturate(math.dot(end - by.A, e) / math.max(1e-6f, math.dot(e, e)));
            var on = by.A + e * t;
            if (math.distance(on, end) < 0.3f) return;
            if (first) { run.Insert(0, on); runCorner.Insert(0, true); runCorner[1] = true; }
            else { run.Add(on); runCorner.Add(true); runCorner[runCorner.Count - 2] = true; }
        }

        /// <summary>A fence kind's 2 m piece (FenceResidentialPieceLow01: FenceResidentialPieceLowShort01); hedges have none. The
        /// game makes the 4 m one where it has no such piece.</summary>
        public static string ShortPiece(string piece)
        {
            if (string.IsNullOrEmpty(piece) || piece.IndexOf("Fence", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var m = System.Text.RegularExpressions.Regex.Match(piece, @"^(.*Piece(?:High|Low))(\d+)$");
            return m.Success ? m.Groups[1].Value + "Short" + m.Groups[2].Value : null;
        }

        /// <summary>Pieces end to end along a straight fence line, of the kind matching a line called lineName (a fence of a
        /// building that levelled up and kept its garden without a plan: the game's NeighbourPolishSystem.LevelUps), laid as
        /// a garden's runs are.</summary>
        public static List<(float2 Position, float Yaw, string Name)> PiecesAlong(float2 a, float2 b, string lineName)
        {
            var m = System.Text.RegularExpressions.Regex.Match(lineName ?? "", @"^(.*?)(High|Low)(\d+)$");
            string piece = m.Success ? m.Groups[1].Value + "Piece" + m.Groups[2].Value + m.Groups[3].Value : "HedgePieceLow01";
            var plan = new StitchPlan();
            LayRun(new List<float2> { a, b }, new List<bool> { true, true }, 0, piece, plan);
            return plan.FencePieces.Select(p => (p.Position, p.Yaw, p.Name)).ToList();
        }

        /// <summary>
        /// Pieces end to end along a run: it turns at a corner of more than 25° (a piece ends there), a gentle bend is followed
        /// chord by chord; the last piece of each straight part ends exactly at its end (a short piece where the kind has one),
        /// so neither a corner nor an end is left open.
        /// </summary>
        private static void LayRun(List<float2> run, List<bool> runCorner, long house, string piece, StitchPlan plan)
        {
            // the run without repeated points
            var pts = new List<float2>();
            var isCorner = new List<bool>();
            for (int k = 0; k < run.Count; k++)
            {
                if (pts.Count > 0 && math.distance(pts[pts.Count - 1], run[k]) < 0.01f) continue;
                pts.Add(run[k]);
                isCorner.Add(runCorner[k]);
            }
            if (pts.Count < 2) return;
            // notches of a few centimetres in the outline (where a straight road edge meets its turning circle) smoothed away:
            // they hid the bend there, and one piece cut across it into the fence beside
            Unnotch(pts, isCorner, 0.15f);
            // where it turns: split into straight parts (a corner closer than a metre to the last one is passed over)
            var parts = new List<List<float2>>();
            var cur = new List<float2> { pts[0] };
            float since = 0f;
            for (int k = 1; k < pts.Count; k++)
            {
                cur.Add(pts[k]);
                since += math.distance(pts[k - 1], pts[k]);
                if (k == pts.Count - 1 || !isCorner[k]) continue;
                var din = math.normalizesafe(pts[k] - pts[k - 1]);
                var dout = math.normalizesafe(pts[k + 1] - pts[k]);
                float rest = 0f;
                for (int r = k; r + 1 < pts.Count && rest < 1f; r++) rest += math.distance(pts[r], pts[r + 1]);
                if (math.dot(din, dout) > math.cos(math.radians(25f)) || since < 1f || rest < 1f) continue;
                parts.Add(cur);
                cur = new List<float2> { pts[k] };
                since = 0f;
            }
            if (cur.Count >= 2) parts.Add(cur);
            string shortName = ShortPiece(piece);
            void Add(float2 a, float2 b, string name)
            {
                var d = b - a;
                if (math.lengthsq(d) < 1e-4f) return;
                plan.FencePieces.Add((house, (a + b) * 0.5f, math.atan2(d.x, d.y), name));
            }
            foreach (var part in parts)
            {
                float total = 0f;
                for (int k = 0; k + 1 < part.Count; k++) total += math.distance(part[k], part[k + 1]);
                if (total < 0.3f) continue;
                var end = part[part.Count - 1];
                var at = part[0];
                int seg = 0, laid = 0;
                // chords of a whole piece along the part while one fits before its end
                while (math.distance(at, end) > PieceLong + 0.05f)
                {
                    float2? next = null;
                    for (int k = seg; k + 1 < part.Count && next == null; k++)
                    {
                        // the point on this segment a piece's length from where we are
                        var p = part[k];
                        var e = part[k + 1] - p;
                        float a2 = math.dot(e, e);
                        if (a2 < 1e-8f) continue;
                        var w = p - at;
                        float bq = 2f * math.dot(w, e), c = math.dot(w, w) - PieceLong * PieceLong;
                        float disc = bq * bq - 4f * a2 * c;
                        if (disc < 0f) continue;
                        float t = (-bq + math.sqrt(disc)) / (2f * a2);
                        if (t < 0f || t > 1f) continue;
                        next = p + e * t;
                        seg = k;
                    }
                    if (next == null) break;
                    Add(at, next.Value, piece);
                    at = next.Value;
                    laid++;
                }
                // what is left: up to the end exactly
                float left = math.distance(at, end);
                if (left < 0.15f && laid > 0) continue;
                var dir = math.normalizesafe(end - at, new float2(0f, 1f));
                if (shortName != null && left <= PieceShort + 0.05f)
                {
                    if (laid > 0 || left >= PieceShort - 0.3f) Add(end - dir * PieceShort, end, shortName);
                    else { var mid = (at + end) * 0.5f; Add(mid - dir * (PieceShort * 0.5f), mid + dir * (PieceShort * 0.5f), shortName); }
                }
                else if (shortName != null && left <= 2f * PieceShort)
                {
                    Add(at, at + dir * PieceShort, shortName);
                    Add(end - dir * PieceShort, end, shortName);
                }
                else if (laid > 0) Add(end - dir * PieceLong, end, piece);
                else { var mid = (at + end) * 0.5f; Add(mid - dir * (PieceLong * 0.5f), mid + dir * (PieceLong * 0.5f), piece); }
            }
        }

        /// <summary>A run's points without the ones that stray less than <paramref name="tol"/> from the line between their
        /// neighbours kept (Douglas-Peucker; its ends always stay); what stays is a corner.</summary>
        private static void Unnotch(List<float2> pts, List<bool> isCorner, float tol)
        {
            if (pts.Count < 3) return;
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int A, int B)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                if (b - a < 2) continue;
                int far = -1;
                float worst = tol;
                for (int k = a + 1; k < b; k++)
                {
                    float d = DistanceToSegment(pts[k], pts[a], pts[b]);
                    if (d > worst) { worst = d; far = k; }
                }
                if (far < 0) continue;
                keep[far] = true;
                stack.Push((a, far));
                stack.Push((far, b));
            }
            int w = 0;
            for (int k = 0; k < pts.Count; k++)
            {
                if (!keep[k]) continue;
                pts[w] = pts[k];
                isCorner[w] = true;
                w++;
            }
            pts.RemoveRange(w, pts.Count - w);
            isCorner.RemoveRange(w, isCorner.Count - w);
        }

        /// <summary>
        /// The last word on gaps: two loose ends of fences close together along a garden's edge (a corner the runs did not
        /// close, the end of a line and the start of a run, a step between two gardens' fences) are joined with pieces of the
        /// garden's own kind. Ends against a house, shed, driveway, road or building are no loose ends; two fences coming up to
        /// the street side by side are left open (the street front); a house's own lines as the game made them are never
        /// joined to each other (its gates).
        /// </summary>
        private static void JoinEnds(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, Dictionary<long, (float2 A, float2 B)> moved, StitchSettings s)
        {
            int n = houses.Count;
            var bits = new List<(float2 A, float2 B, float W, bool Ours, int House)>();
            float W(string name) => name != null && name.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0 ? 1.1f : 0.2f;
            for (int i = 0; i < n; i++)
            {
                var h = houses[i];
                if (h.Polished && !h.Edit && h.Standing != null) { foreach (var (a, b) in h.Standing) bits.Add((a, b, 0.4f, true, i)); continue; }
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) continue;
                    bool mv = h.Polished && moved.TryGetValue(l.Id, out var m);
                    var (a, b) = mv ? moved[l.Id] : (l.A, l.B);
                    if (math.distance(a, b) < 0.3f) continue;
                    bits.Add((a, b, W(l.Name), mv, i));
                }
            }
            var byHouse = houses.Select((h, i) => (h.Id, i)).ToDictionary(x => x.Id, x => x.i);
            foreach (var (house, at, yaw, name) in plan.FencePieces)
            {
                var d = new float2(math.sin(yaw), math.cos(yaw)) * ((name.IndexOf("Short", StringComparison.Ordinal) >= 0 ? PieceShort : PieceLong) * 0.5f);
                bits.Add((at - d, at + d, W(name), true, byHouse.TryGetValue(house, out var hi) ? hi : -1));
            }
            var grid = (s_joinGrid ??= new LineGrid(4f, 1.5f)).Reset(4f, 1.5f);
            foreach (var b in bits) grid.Add(b.A, b.B);
            var widthOf = new Dictionary<(float2, float2), float>();
            foreach (var b in bits) widthOf[(b.A, b.B)] = b.W;
            var walls = Obstacles(houses);
            var wallLo = new float2[walls.Count];
            var wallHi = new float2[walls.Count];
            for (int k = 0; k < walls.Count; k++) Poly.Bounds(walls[k], out wallLo[k], out wallHi[k]);
            // against a house, shed or paving (within d), on a road or another building (a garden stops 0.8 m short of
            // those, so a fence along a road is not against it)
            bool Against(float2 p, float d)
            {
                // (a wall whose bounds are further off than d is neither under the point nor within d of it)
                for (int k = 0; k < walls.Count; k++)
                {
                    if (p.x < wallLo[k].x - d - 0.01f || p.x > wallHi[k].x + d + 0.01f || p.y < wallLo[k].y - d - 0.01f || p.y > wallHi[k].y + d + 0.01f) continue;
                    if (Poly.Contains(walls[k], p) || DistanceToRing(walls[k], p) < d) return true;
                }
                return BarrierNear(p, 1f, 0.3f);
            }
            bool NearBarrier(float2 p, float d) => BarrierNear(p, d, d);
            var mine = Enumerable.Range(0, n).Where(i => touched[i] && houses[i].Edit && houses[i].Polished && plots[i] != null).ToList();
            var mineLo = new float2[mine.Count];
            var mineHi = new float2[mine.Count];
            for (int k = 0; k < mine.Count; k++) Poly.Bounds(plots[mine[k]], out mineLo[k], out mineHi[k]);
            int NearPlot(float2 p, float d)
            {
                int best = -1;
                float bd = d;
                for (int k = 0; k < mine.Count; k++)
                {
                    // (a garden whose bounds are further off than the nearest so far cannot be nearer)
                    var off = math.max(math.max(mineLo[k] - p, p - mineHi[k]), 0f);
                    if (math.length(off) > bd + 0.01f) continue;
                    int i = mine[k];
                    float x = DistanceToRing(plots[i], p);
                    if (x < bd) { bd = x; best = i; }
                }
                return best;
            }
            var loose = new List<(int Bit, float2 P)>();
            for (int bi = 0; bi < bits.Count; bi++)
            {
                var f = bits[bi];
                foreach (var p in new[] { f.A, f.B })
                {
                    if (NearPlot(p, 1.5f) < 0) continue;
                    bool met = false;
                    foreach (var (a, b) in grid.At(p))
                    {
                        if (a.Equals(f.A) && b.Equals(f.B)) continue;
                        if (DistanceToSegment(p, a, b) <= 0.35f + f.W * 0.5f + widthOf[(a, b)] * 0.5f) { met = true; break; }
                    }
                    if (!met && !Against(p, 0.6f)) loose.Add((bi, p));
                }
            }
            var used = new HashSet<int>();
            int joined = 0;
            for (int i = 0; i < loose.Count; i++)
            {
                if (used.Contains(i)) continue;
                int best = -1;
                float bd = 6f;
                for (int j = 0; j < loose.Count; j++)
                {
                    if (j == i || used.Contains(j) || loose[j].Bit == loose[i].Bit) continue;
                    float d = math.distance(loose[i].P, loose[j].P);
                    if (d < bd && d > 0.3f) { bd = d; best = j; }
                }
                if (best < 0) continue;
                var f = bits[loose[i].Bit];
                var g = bits[loose[best].Bit];
                var p = loose[i].P;
                var q = loose[best].P;
                // a house's own lines as the game made them: its own gaps (gates)
                if (!f.Ours && !g.Ours) continue;
                var gd = math.normalizesafe(q - p);
                var df = math.normalizesafe(f.B - f.A);
                var dg = math.normalizesafe(g.B - g.A);
                if (NearBarrier(p, 2.5f) && NearBarrier(q, 2.5f) && math.abs(math.dot(df, gd)) < 0.5f && math.abs(math.dot(dg, gd)) < 0.5f) continue;
                // never across a house, shed, driveway, road or building
                bool blocked = false;
                for (int t = 1; t < 8 && !blocked; t++) blocked = Against(math.lerp(p, q, t / 8f), 0.05f);
                if (blocked) continue;
                int owner = NearPlot((p + q) * 0.5f, 1.5f);
                if (owner < 0) continue;
                var h = houses[owner];
                LayRun(new List<float2> { p, q }, new List<bool> { true, true }, h.Id, PieceFor(h, (int)K(h, s, "fenceKind", 0f)), plan);
                used.Add(i);
                used.Add(best);
                joined++;
            }
            if (joined > 0) plan.Notes.Add($"{joined} loose fence ends joined");
        }

        /// <summary>
        /// Land some gardens reached but none got (the strips the cuts between gardens that are not neighbours leave, "alleys"):
        /// each piece goes to the garden it borders most, so neighbouring gardens meet. Never onto a lot, a road or a building.
        /// </summary>
        private static void FillGaps(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, List<float2>[] raw, float2[][] kept, StitchSettings s, StitchPlan plan)
        {
            int n = houses.Count;
            var want = Enumerable.Range(0, n).Where(i => touched[i] && houses[i].Edit && houses[i].Polished && raw[i] != null && raw[i].Count >= 3 && plots[i] != null).ToList();
            if (want.Count < 2) return;
            var reached = Poly.UnionAll(want.Select(i => (IReadOnlyList<float2>)Ccw(raw[i].ToArray())));
            // what the reached land is cut by: only shapes within its bounds can cut it (the rest kept out of the boolean, in the
            // same order, so it cuts the same; with every road piece around the patch it was most of a plan's time)
            float2 rlo = new float2(float.MaxValue), rhi = new float2(float.MinValue);
            foreach (var r in reached) foreach (var p in r.Outer) { rlo = math.min(rlo, p); rhi = math.max(rhi, p); }
            bool Meets(IReadOnlyList<float2> ring)
            {
                Poly.Bounds(ring, out var lo, out var hi);
                return !(hi.x < rlo.x || lo.x > rhi.x || hi.y < rlo.y || lo.y > rhi.y);
            }
            var taken = new List<IReadOnlyList<float2>>();
            for (int i = 0; i < n; i++)
            {
                // a garden another pass wrote takes its real land, not what this plan would have given it (that left strips
                // nobody took and nobody fenced between gardens planned at different times)
                var own = IsFixed(houses[i]) ? houses[i].Garden : plots[i];
                if (own != null) { var c = Ccw(own); if (Meets(c)) taken.Add(c); }
                if (kept[i] != null) { var c = Ccw(kept[i]); if (Meets(c)) taken.Add(c); }
                // (and the whole lot: filled after the gardens are kept off lots, a gap reached into one: 4 m² over a lot)
                { var c = Ccw(Lot(houses[i])); if (Meets(c)) taken.Add(c); }
            }
            float margin = s.Knob("barrierMargin", 0.8f), deck = DeckMargin(s);
            // water: kept the widest water gap of these gardens off
            float wet = math.max(margin, want.Max(i => WaterGap(houses[i], s)));
            // (the barriers near the reached land, in their order: the furthest any is grown, a metre more)
            float growMost = math.max(wet + 0.5f, math.max(margin, deck) * 4f + 0.5f) + 1f;
            if (s_barriers != null && rlo.x <= rhi.x)
                foreach (int k in BarriersIn(rlo - growMost, rhi + growMost))
                {
                    var (blo, bhi, ring) = s_barriers[k];
                    bool water = s_barrierKind[k] == WaterKind;
                    float m = s_barrierKind[k] == DeckKind ? deck : margin;
                    // grown by the margin (a sharp corner's miter reaches out up to four times it), water by its gap
                    float grow = water ? wet + 0.5f : m * 4f + 0.5f;
                    if (bhi.x + grow < rlo.x || blo.x - grow > rhi.x || bhi.y + grow < rlo.y || blo.y - grow > rhi.y) continue;
                    foreach (var g in water ? GrownWater(ring, wet) : Grown(ring, m)) { var c = Ccw(g); if (Meets(c)) taken.Add(c); }
                }
            var gaps = Poly.Boolean(RingsOf(reached), taken, BoolOp.Difference, FillRule.NonZero);
            int filled = 0, refused = 0;
            float area = 0f;
            bool tidy = s.Knob("fillGapsTidy", 1f) > 0.5f;
            bool split = s.Knob("fillGapsSplit", 1f) > 0.5f;
            int shared = 0;
            foreach (var gap in gaps.OrderByDescending(g => g.Area))
            {
                if (gap.Area < 2f) continue;
                if (split)
                {
                    int got = SplitGap(houses, plots, want, gap);
                    if (got > 1) { shared++; area += gap.Area; continue; }
                    if (got < 0) continue;
                }
                // the garden it borders most (its outline sampled every metre, near which garden's edge)
                var ring = gap.Outer;
                var score = new Dictionary<int, int>();
                for (int k = 0; k < ring.Length; k++)
                {
                    var a = ring[k];
                    var b = ring[(k + 1) % ring.Length];
                    int steps = math.max(1, (int)(math.distance(a, b)));
                    for (int t = 0; t < steps; t++)
                    {
                        var p = math.lerp(a, b, (t + 0.5f) / steps);
                        foreach (int i in want)
                            if (DistanceToRing(plots[i], p) < 0.6f) { score[i] = (score.TryGetValue(i, out var c) ? c : 0) + 1; break; }
                    }
                }
                if (score.Count == 0) continue;
                // the garden it borders most whose outline it does not make more crooked: it fills a notch, it is not a prong
                // into land between other gardens (the user: a strip down the middle of an open crossing looked bad; a wedge
                // left beside a garden looked worse)
                foreach (int best in score.OrderByDescending(kv => kv.Value).Select(kv => kv.Key))
                {
                    var merged = Poly.Boolean(new List<Region> { new Region(Ccw(plots[best])) }, new List<Region> { gap }, BoolOp.Union);
                    var keep = merged.OrderByDescending(r => Poly.Contains(r.Outer, houses[best].Position) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
                    if (keep == null) continue;
                    var outer = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(r => Poly.Area(r)).First() : keep.Outer;
                    if (outer == null || outer.Length < 3) continue;
                    var grown = Poly.Simplify(outer, 0.05f);
                    if (tidy && Corners(grown) > Corners(plots[best])) { refused++; continue; }
                    plots[best] = grown;
                    filled++;
                    area += gap.Area;
                    break;
                }
            }
            if (filled > 0) plan.Notes.Add($"{filled} strips between gardens given to the garden beside them");
            if (shared > 0) plan.Notes.Add($"{shared} strips between gardens shared out by the gardens around them");
            if (filled + shared > 0) plan.Notes.Add($"(gaps between gardens filled: {area:F0} m²)");
            if (refused > 0) plan.Notes.Add($"{refused} strips between gardens not given to a garden they would make more crooked");
        }

        /// <summary>
        /// The thin parts of a garden past its lot taken off: a strip narrower than sliverWidth (m) running into a crack between
        /// two other gardens (the user, 23 Oct, at an open crossing: "the top left green one found its way in that little
        /// crack"). Its land goes to the gardens on either side of it (FillGaps, next). Only gardens crooked enough to have one
        /// (more than 5 corners) are looked at; the lot is never touched.
        /// </summary>
        private static void TrimSlivers(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchSettings s, StitchPlan plan)
        {
            float half = s.Knob("sliverWidth", 3f) * 0.5f;
            if (half <= 0f) return;
            int trimmed = 0;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!touched[i] || plots[i] == null || !h.Edit || !h.Polished || IsFixed(h) || Corners(plots[i]) <= 5) continue;
                var ring = Ccw(plots[i]);
                // what is left of it after shrinking by half the width and growing back (square corners kept), and the lot
                var opened = new List<Region>();
                foreach (var r in Poly.Offset(ring, half, OffsetJoin.Miter)) opened.AddRange(Poly.Offset(r, -half, OffsetJoin.Miter));
                opened.Add(new Region(Ccw(Lot(h))));
                // the thin parts: the garden less that, real pieces only (the outline itself is kept exactly)
                var thin = Poly.Boolean(new List<Region> { new Region(ring) }, opened, BoolOp.Difference).Where(r => r.Area > 1.5f).ToList();
                if (thin.Count == 0) continue;
                var left = Poly.Boolean(new List<Region> { new Region(ring) }, thin, BoolOp.Difference);
                var keep = left.OrderByDescending(x => Poly.Contains(x.Outer, h.Position) ? 1 : 0).ThenByDescending(x => x.Area).FirstOrDefault();
                if (keep == null || keep.Outer.Length < 3) continue;
                var outer = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(x => Poly.Area(x)).First() : keep.Outer;
                if (outer == null || outer.Length < 3) continue;
                plots[i] = Poly.Simplify(outer, 0.05f);
                trimmed++;
            }
            if (trimmed > 0) plan.Notes.Add($"{trimmed} gardens' thin strips (under {half * 2f:F0} m) taken off");
        }

        /// <summary>
        /// The open backs of a row (open land, a shore) in one line: each garden's back runs straight between the points where
        /// its side lines end, the same points its neighbours' backs run to (the shallower of the two gardens' ends on the line
        /// they share). Before, each back ended on its own: a straight reach line at its lot's angle, a water margin, a slope,
        /// and the row's outline zigzagged with a spike at every lot (the user, 23 Oct: "spikes because they don't want to meet
        /// up on their true edges"). Only taken away (never onto land past the old back), at most maxBackTrim (m) deep, the lot
        /// never touched; a garden at a row's end, or sharing its back with another lot, keeps its back.
        /// </summary>
        private static void StraightBacks(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, int[] left, int[] right,
            Dictionary<(int, int), Cut> sideCuts, bool[] sharesEnd, StitchSettings s, StitchPlan plan)
        {
            // the furthest back a plot reaches along a side line (its corner there)
            static bool End(float2[] plot, Cut c, out float2 end)
            {
                end = default;
                float best = float.MinValue;
                foreach (var v in plot)
                {
                    var o = v - c.P;
                    if (math.abs(c.D.x * o.y - c.D.y * o.x) > 0.25f) continue;
                    float t = math.dot(o, c.D);
                    if (t > best) { best = t; end = v; }
                }
                return best > float.MinValue;
            }
            var junction = new Dictionary<(int, int), float2>();
            foreach (var kv in sideCuts)
            {
                var (i, j) = kv.Key;
                var c = kv.Value;
                if (plots[i] == null || plots[j] == null) continue;
                if (!End(plots[i], c, out var ei) || !End(plots[j], c, out var ej)) continue;
                junction[kv.Key] = math.dot(ei - c.P, c.D) <= math.dot(ej - c.P, c.D) ? ei : ej;
            }
            float maxTrim = s.Knob("maxBackTrim", 4f);
            int straightened = 0;
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!touched[i] || plots[i] == null || !h.Edit || !h.Polished || IsFixed(h) || sharesEnd[i] || left[i] < 0 || right[i] < 0) continue;
                if (!junction.TryGetValue((math.min(i, left[i]), math.max(i, left[i])), out var jl)) continue;
                if (!junction.TryGetValue((math.min(i, right[i]), math.max(i, right[i])), out var jr)) continue;
                // both behind the lot, apart
                if (Local(h, jl).y > -h.HalfLot.y + 0.5f || Local(h, jr).y > -h.HalfLot.y + 0.5f || math.distance(jl, jr) < 1f) continue;
                var d = math.normalize(jr - jl);
                float Side(float2 p) { var o = p - jl; return d.x * o.y - d.y * o.x; }
                float home = math.sign(Side(h.Position));
                if (home == 0f || Lot(h).Any(p => Side(p) * home < -0.05f)) continue;
                // only a little off the back
                if (plots[i].Any(p => Side(p) * home < -maxTrim)) continue;
                var cut = ClipHalfPlane(plots[i].ToList(), jl, d, h.Position);
                if (cut.Count < 3 || Poly.Area(plots[i]) - Poly.Area(cut) < 0.3f) continue;
                plots[i] = cut.ToArray();
                straightened++;
            }
            if (straightened > 0) plan.Notes.Add($"{straightened} gardens' backs straightened to their neighbours' (one line along the row)");
        }

        /// <summary>
        /// A gap between gardens shared by the gardens along its edge (the user, 23 Oct: "very inclined to have continuity",
        /// four gardens round an open crossing "should all have met up there"): its outline split where it passes from one
        /// garden to the next (halfway along a stretch no garden runs along), each garden taking its stretch and the land
        /// up to the line between those points (two gardens), or up to the gap's middle (three and more). Returns how many
        /// gardens it went to; 1: one garden only borders it (the caller decides), 0: none, -1: it could not be split.
        /// </summary>
        private static int SplitGap(IReadOnlyList<StitchHouse> houses, float2[][] plots, List<int> want, Region gap)
        {
            var ring = gap.Outer;
            Poly.Bounds(ring, out var glo, out var ghi);
            var near = want.Where(i =>
            {
                Poly.Bounds(plots[i], out var lo, out var hi);
                return !(hi.x < glo.x - 1f || lo.x > ghi.x + 1f || hi.y < glo.y - 1f || lo.y > ghi.y + 1f);
            }).ToList();
            if (near.Count == 0) return 0;
            // the outline every half metre (its corners kept), and the garden each point lies along
            var pts = new List<float2>();
            var who = new List<int>();
            for (int k = 0; k < ring.Length; k++)
            {
                var a = ring[k];
                var b = ring[(k + 1) % ring.Length];
                int steps = math.max(1, (int)(math.distance(a, b) / 0.5f));
                for (int t = 0; t < steps; t++)
                {
                    var p = math.lerp(a, b, t / (float)steps);
                    int w = -1;
                    float bd = 0.6f;
                    foreach (int i in near)
                    {
                        float d = DistanceToRing(plots[i], p);
                        if (d < bd) { bd = d; w = i; }
                    }
                    pts.Add(p);
                    who.Add(w);
                }
            }
            int m = pts.Count;
            if (m < 4) return -1;
            // a garden touching it at a point only (under a metre) is not along it
            for (int pass = 0; pass < 2; pass++)
                for (int k = 0; k < m; k++)
                {
                    if (who[k] < 0) continue;
                    int len = 1;
                    while (len < 3 && who[(k + len) % m] == who[k]) len++;
                    int back = 1;
                    while (back < 3 && who[(k - back + m) % m] == who[k]) back++;
                    if (len + back - 1 < 3) who[k] = -1;
                }
            var labels = who.Where(w => w >= 0).Distinct().ToList();
            if (labels.Count <= 1) return labels.Count;
            // the stretches along one garden, in order round the outline (starting where one begins)
            int start = -1;
            for (int k = 0; k < m; k++) if (who[k] >= 0 && who[(k - 1 + m) % m] != who[k]) { start = k; break; }
            if (start < 0) return -1;
            var runs = new List<(int Who, int From, int To)>();   // indexes from start, To inclusive
            for (int k = 0; k < m;)
            {
                int w = who[(start + k) % m];
                int e = k;
                while (e + 1 < m && who[(start + e + 1) % m] == w) e++;
                if (w >= 0) runs.Add((w, k, e));
                k = e + 1;
            }
            // where one stretch ends and the next begins: halfway along the land between them, else where they meet
            int r = runs.Count;
            var cut = new int[r];   // cut[j]: the point between run j-1 and run j
            for (int j = 0; j < r; j++)
            {
                var prev = runs[(j - 1 + r) % r];
                int from = prev.To + 1, to = runs[j].From;
                if (j == 0) from -= m;   // (the land before the first stretch is at the end of the outline)
                cut[j] = to > from ? (from + to) / 2 : to;
            }
            float2 P(int k) => pts[((start + k) % m + m) % m];
            float2 centre = float2.zero;
            if (r > 2)
            {
                for (int j = 0; j < r; j++) centre += P(cut[j]);
                centre /= r;
                if (!Poly.Contains(ring, centre)) return -1;
            }
            var pieces = new List<(int Who, float2[] Ring)>();
            for (int j = 0; j < r; j++)
            {
                int a = cut[j], b = j + 1 < r ? cut[j + 1] : cut[0] + m;
                var piece = new List<float2>();
                for (int k = a; k <= b; k++) piece.Add(P(k));
                if (r > 2) piece.Add(centre);
                if (piece.Count < 3) continue;
                pieces.Add((runs[j].Who, piece.ToArray()));
            }
            // each piece kept inside the gap (a line between the points could cross out of a crooked one), joined to its garden
            int given = 0;
            foreach (var (w, piece) in pieces)
            {
                var inside = Poly.Boolean(new List<Region> { new Region(Ccw(piece)) }, new List<Region> { gap }, BoolOp.Intersection);
                if (inside.Count == 0) continue;
                var merged = Poly.Boolean(new List<Region> { new Region(Ccw(plots[w])) }, inside, BoolOp.Union);
                var keep = merged.OrderByDescending(x => Poly.Contains(x.Outer, houses[w].Position) ? 1 : 0).ThenByDescending(x => x.Area).FirstOrDefault();
                if (keep == null) continue;
                var outer = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(x => Poly.Area(x)).First() : keep.Outer;
                if (outer == null || outer.Length < 3) continue;
                var grown = Poly.Simplify(outer, 0.05f);
                // a piece that makes its garden crooked (more than one new corner: a hook out into open land) is left
                if (Corners(grown) > Corners(plots[w]) + 1) continue;
                plots[w] = grown;
                given++;
            }
            return given;
        }

        /// <summary>
        /// Land between two outgrown lots that none of their gardens took, shared between them: each gets its side of the line
        /// they already share (back to back, facing), else of the middle line between the two lots (all of it when the other
        /// lot is not outgrown). Lots that stand at an angle (on two streets that meet at 45°, a block's corner, backs on a
        /// slant) fit none of the joining rules, and their gardens end short of each other (no spikes, paving never into open
        /// land sideways): with every reach at 500 m a wedge of grass stayed by the street and strips between the works, a
        /// hedge on each side (the user, 22 Oct: "a nice, clean connection, no jumbledness, no gaps"). Only the land around
        /// the two lots (their convex hull; a car park or paving's up to its street in front) that no lot, garden, walkway, road,
        /// path, track, building or water gap takes; only lots within each other's "Block corners" reach (maxFacingGap) with no
        /// other lot right between them; side by side neighbours on their middle line (the corner of a block where their
        /// street turns), never across a walkway or a step between them; never a ring's houses (its middle is a shared green).
        /// </summary>
        private static void ShareBetween(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, int[] left, int[] right, Cut[] ringCut,
            Dictionary<(int, int), Cut> pairCuts, List<(int I, int J)> alleys, StitchSettings s, StitchPlan plan)
        {
            int n = houses.Count;
            bool Mine(int i) => houses[i].Edit && houses[i].Polished && !IsFixed(houses[i]) && ringCut[i] == null && plots[i] != null && plots[i].Length >= 3;
            var lots = new float2[n][];
            var lo = new float2[n];
            var hi = new float2[n];
            for (int k = 0; k < n; k++) { lots[k] = Ccw(Lot(houses[k])); Poly.Bounds(lots[k], out lo[k], out hi[k]); }
            // the walkways between two houses (paved from the street to the shallower one's back, below): nobody's garden
            var walkways = alleys.Select(w =>
            {
                var a = houses[w.I];
                var b = houses[w.J];
                float hz = math.min(a.HalfLot.y, b.HalfLot.y);
                return Ccw(new[] { a.FrontRight, b.FrontLeft, BackAt(b, -1, hz), BackAt(a, +1, hz) });
            }).ToList();
            bool Apart(float2 alo, float2 ahi, float2 blo, float2 bhi, float d) => blo.x > ahi.x + d || bhi.x < alo.x - d || blo.y > ahi.y + d || bhi.y < alo.y - d;
            var pairs = new List<(float Gap, int I, int J)>();
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (!Mine(i) && !Mine(j) || ringCut[i] != null || ringCut[j] != null) continue;
                    if (!houses[i].Polished && !houses[j].Polished) continue;
                    // side by side with a walkway or a step between them: that gap stays as it is
                    if ((right[i] == j || left[i] == j) && houses[i].Polished && houses[j].Polished && !pairCuts.ContainsKey((i, j))) continue;
                    float reach = !houses[j].Polished ? K(houses[i], s, "maxFacingGap", 50f)
                        : !houses[i].Polished ? K(houses[j], s, "maxFacingGap", 50f)
                        : math.min(K(houses[i], s, "maxFacingGap", 50f), K(houses[j], s, "maxFacingGap", 50f));
                    if (reach < 1f || Apart(lo[i], hi[i], lo[j], hi[j], reach)) continue;
                    var (pa, pb) = Closest(lots[i], lots[j]);
                    float gap = math.distance(pa, pb);
                    if (gap <= reach) pairs.Add((gap, i, j));
                }
            if (pairs.Count == 0) return;
            float margin = s.Knob("barrierMargin", 0.8f), deck = DeckMargin(s);
            int given = 0;
            float area = 0f;
            // a car park or paving reaches its street in front (as its plot does: frontReach), so the corner two of them leave by
            // a street is between them too (the land past the street is cut off by it)
            float2[] Reaching(int k)
            {
                var h = houses[k];
                if (!h.Polished || !HardLand(h, s)) return lots[k];
                float f = h.HalfLot.y + K(h, s, "frontReach", 15f);
                return lots[k].Concat(new[] { h.ToWorld(new float2(-h.HalfLot.x, f)), h.ToWorld(new float2(h.HalfLot.x, f)) }).ToArray();
            }
            // each house's land as the pairs see it (a fixed garden as it stands, else its plot), counter-clockwise, with its
            // bounds: worked out once, again only for a garden that took land here (a list of every lot, garden and road grown
            // for every pair, then filtered, was half of this step)
            var ownCcw = new float2[n][];
            var ownLo = new float2[n];
            var ownHi = new float2[n];
            var ownKnown = new bool[n];
            float2[] Own(int k)
            {
                if (ownKnown[k]) return ownCcw[k];
                ownKnown[k] = true;
                var own = IsFixed(houses[k]) ? houses[k].Garden : plots[k];
                if (own == null || own.Length < 3) return ownCcw[k] = null;
                Poly.Bounds(own, out ownLo[k], out ownHi[k]);
                return ownCcw[k] = Ccw(own);
            }
            var taken = new List<IReadOnlyList<float2>>();
            var grownNear = new List<IReadOnlyList<float2>>();
            // two lots on one grid (parallel or at right angles, within 2°): the land between them is a box on that grid and the
            // line they share runs along it. Their hull and the line square to their shortest way (corner to corner) cut gardens
            // slanted, with notches where two such lines met (the user, 23 Oct: "It should be square ... minimal turns"; four
            // houses bulldozed in a grid suburb). Lots at another angle keep the hull and the slanted line (knob shareGrid 0: all
            // of them, as before)
            bool gridKnob = s.Knob("shareGrid", 1f) >= 0.5f;
            foreach (var (_, i, j) in pairs.OrderBy(p => p.Gap).ThenBy(p => p.I).ThenBy(p => p.J))
            {
                var gu = new float2(0f);
                var gv = new float2(0f);
                bool grid = gridKnob && OnOneGrid(houses[i], houses[j], out gu, out gv);
                var hull = grid ? GridBox(Reaching(i).Concat(Reaching(j)), gu, gv) : Ccw(Hull(Reaching(i).Concat(Reaching(j)).ToList()));
                if (hull.Length < 3) continue;
                Poly.Bounds(hull, out var hlo, out var hhi);
                // another lot right between them (across the shortest way from one to the other): not neighbours, it shares the
                // land with each of them
                // (its lot a little smaller: a lot beside one of them, touching it, is not between)
                var (sa, sb) = Closest(lots[i], lots[j]);
                bool between = false;
                for (int k = 0; k < n && !between; k++)
                    if (k != i && k != j && !Apart(hlo, hhi, lo[k], hi[k], 0f) && Poly.ClipSegmentIntervals(Inset(lots[k]) ?? lots[k], sa, sb).Any(iv => iv.y - iv.x > 0.01f)) between = true;
                if (between) continue;
                // what of it is taken: lots, gardens (a fixed one as it stands), roads, paths, tracks, buildings, decks, water;
                // only what lies in it (a slanted outline's bounds hold many more, and they made the boolean slow), kept as it is
                // found, in the same order as before (one list for every pair: the boolean takes a copy)
                taken.Clear();
                for (int k = 0; k < n; k++)
                {
                    if (!Apart(hlo, hhi, lo[k], hi[k], 0f) && Overlaps(hull, lots[k])) taken.Add(lots[k]);
                    var own = Own(k);
                    if (own != null && !Apart(hlo, hhi, ownLo[k], ownHi[k], 0f) && Overlaps(hull, own)) taken.Add(own);
                }
                float wet = math.max(margin, math.max(WaterGap(houses[i], s), WaterGap(houses[j], s)));
                grownNear.Clear();
                GrownBarriersNear(hlo, hhi, margin, deck, wet, grownNear);
                foreach (var g in grownNear) if (Overlaps(hull, g)) taken.Add(g);
                foreach (var w in walkways) if (Overlaps(hull, w)) taken.Add(w);
                if (!AnyBare(hull, hlo, hhi, taken, new[] { Mine(i) ? plots[i] : null, Mine(j) ? plots[j] : null })) continue;
                var free = Poly.Boolean(new[] { (IReadOnlyList<float2>)hull }, taken, BoolOp.Difference, FillRule.NonZero).Where(r => r.Area >= 2f).ToList();
                if (free.Count == 0) continue;
                // the line they share
                Cut line;
                if (!pairCuts.TryGetValue((i, j), out line))
                {
                    var (ca, cb) = Closest(lots[i], lots[j]);
                    bool touching = math.distance(ca, cb) <= 0.05f;
                    var nrm = touching ? math.normalizesafe(houses[j].Position - houses[i].Position, new float2(1f, 0f)) : math.normalize(cb - ca);
                    // (on one grid: along the grid's axis nearest it)
                    if (grid) nrm = math.abs(math.dot(nrm, gu)) >= math.abs(math.dot(nrm, gv)) ? gu * math.sign(math.dot(nrm, gu)) : gv * math.sign(math.dot(nrm, gv));
                    line = new Cut { P = touching ? (houses[i].Position + houses[j].Position) * 0.5f : (ca + cb) * 0.5f, D = new float2(-nrm.y, nrm.x) };
                }
                float big = math.length(hhi - hlo) * 2f + 10f;
                foreach (var (me, other) in new[] { (i, j), (j, i) })
                {
                    if (!Mine(me)) continue;
                    var part = free;
                    if (houses[other].Polished)
                    {
                        // its side of the line: where its house stands
                        var d = math.normalizesafe(line.D, new float2(1f, 0f));
                        var side = new float2(-d.y, d.x);
                        if (math.dot(houses[me].Position - line.P, side) < 0f) side = -side;
                        var half = Ccw(new[] { line.P - d * big, line.P + d * big, line.P + d * big + side * big, line.P - d * big + side * big });
                        part = Poly.Boolean(free, new List<Region> { new Region(half) }, BoolOp.Intersection).Where(r => r.Area >= 1f).ToList();
                    }
                    if (!HardLand(houses[me], s) && part.Count > 0)
                    {
                        // a garden's front stays at its lot's street edge (a driveway reaching it stays open to the street)
                        var h = houses[me];
                        var behind = Ccw(new[] { h.ToWorld(new float2(-big, h.HalfLot.y)), h.ToWorld(new float2(-big, -big)), h.ToWorld(new float2(big, -big)), h.ToWorld(new float2(big, h.HalfLot.y)) });
                        part = Poly.Boolean(part, new List<Region> { new Region(behind) }, BoolOp.Intersection).Where(r => r.Area >= 1f).ToList();
                    }
                    if (part.Count == 0) continue;
                    float was = Math.Abs(Poly.Area(plots[me]));
                    var merged = Poly.Boolean(new List<Region> { new Region(Ccw(plots[me])) }, part, BoolOp.Union);
                    var keep = merged.OrderByDescending(r => Poly.Contains(r.Outer, houses[me].Position) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
                    if (keep == null) continue;
                    var outer = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(r => Poly.Area(r)).First() : keep.Outer;
                    if (outer == null || outer.Length < 3) continue;
                    var next = Poly.Simplify(outer, 0.05f);
                    // nor up a cliff
                    if (s.Ground != null) next = SlopeCut(houses[me], next, s) ?? next;
                    // (a part that does not touch its garden, across a road: not its)
                    float gained = Math.Abs(Poly.Area(next)) - was;
                    if (gained < 1f) continue;
                    plots[me] = next;
                    ownKnown[me] = false;
                    touched[me] = true;
                    given++;
                    area += gained;
                }
            }
            if (given > 0) plan.Notes.Add($"{given} gardens take their side of the land between them and a lot at an angle ({area:F0} m²)");
        }

        /// <summary>The line runs clear of the ring (every corner on one side, or on it).</summary>
        private static bool Clear(Cut c, float2[] ring)
        {
            int pos = 0, neg = 0;
            foreach (var p in ring)
            {
                float side = c.D.x * (p.y - c.P.y) - c.D.y * (p.x - c.P.x);
                if (side > 0.3f) pos++; else if (side < -0.3f) neg++;
            }
            return pos == 0 || neg == 0;
        }

        /// <summary>The two lots stand on one grid (their streets parallel or at right angles, within 2°): its axes (u across
        /// the first lot, v along its forward).</summary>
        private static bool OnOneGrid(StitchHouse a, StitchHouse b, out float2 u, out float2 v)
        {
            v = math.normalizesafe(a.Forward, new float2(0f, 1f));
            u = new float2(v.y, -v.x);
            var f = math.normalizesafe(b.Forward, v);
            float c = math.abs(math.dot(f, v)), d = math.abs(math.dot(f, u));
            // (2°: a straight street and the ones square to it; lots side by side on a curve differ by a few degrees and keep the hull)
            const float cos2 = 0.99939f;
            return c >= cos2 || d >= cos2;
        }

        /// <summary>The box on the grid (u, v) around the points, counter-clockwise.</summary>
        private static float2[] GridBox(IEnumerable<float2> pts, float2 u, float2 v)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (var p in pts)
            {
                float a = math.dot(p, u), b = math.dot(p, v);
                u0 = math.min(u0, a); u1 = math.max(u1, a);
                v0 = math.min(v0, b); v1 = math.max(v1, b);
            }
            return Ccw(new[] { u * u0 + v * v0, u * u1 + v * v0, u * u1 + v * v1, u * u0 + v * v1 });
        }

        /// <summary>Roads, paths, tracks and other buildings near [lo, hi], grown by the margin (decks by theirs), water by its
        /// gap: what no garden takes (as FillGaps keeps them out).</summary>
        private static List<IReadOnlyList<float2>> GrownBarriersNear(float2 lo, float2 hi, float margin, float deck, float wet, List<IReadOnlyList<float2>> into = null)
        {
            var list = into ?? new List<IReadOnlyList<float2>>();
            if (s_barriers == null) return list;
            float reach = math.max(math.max(margin, deck) * 4f, wet) + 0.5f;
            foreach (int k in BarriersIn(lo - reach, hi + reach))
            {
                var (blo, bhi, ring) = s_barriers[k];
                bool water = s_barrierKind[k] == WaterKind;
                float m = s_barrierKind[k] == DeckKind ? deck : margin;
                float grow = water ? wet + 0.5f : m * 4f + 0.5f;
                if (bhi.x + grow < lo.x || blo.x - grow > hi.x || bhi.y + grow < lo.y || blo.y - grow > hi.y) continue;
                foreach (var g in water ? GrownWater(ring, wet) : Grown(ring, m)) list.Add(Ccw(g));
            }
            return list;
        }

        /// <summary>A quick look: some point of the outline (sampled a few hundred times at most) that nothing here takes, next
        /// to one of the gardens it could go to (land only across other land is never theirs).</summary>
        private static bool AnyBare(float2[] ring, float2 lo, float2 hi, List<IReadOnlyList<float2>> taken, float2[][] gardens)
        {
            float step = math.max(1.5f, math.sqrt(math.max(1f, (hi.x - lo.x) * (hi.y - lo.y))) / 16f);
            var boxes = taken.Select(t => { Poly.Bounds(t, out var a, out var b); return (a, b); }).ToList();
            for (float x = lo.x + step * 0.5f; x < hi.x; x += step)
                for (float y = lo.y + step * 0.5f; y < hi.y; y += step)
                {
                    var p = new float2(x, y);
                    if (!Poly.Contains(ring, p)) continue;
                    bool held = false;
                    for (int k = 0; k < taken.Count && !held; k++)
                        if (p.x >= boxes[k].a.x && p.x <= boxes[k].b.x && p.y >= boxes[k].a.y && p.y <= boxes[k].b.y && Poly.Contains(taken[k], p)) held = true;
                    if (held) continue;
                    foreach (var g in gardens) if (g != null && DistanceToRing(g, p) <= step * 1.5f) return true;
                }
            return false;
        }

        /// <summary>
        /// Gardens another pass wrote that this plan does not edit (StitchHouse.Garden) are fixed: an edited garden never takes
        /// their land (its own lot is never cut; a fixed garden over it is reported in GardenClashes, so that neighbour is
        /// planned again and gives it back), and their plots are those gardens, so fences meet them on their real edges.
        /// Houses that grow fast one after another never end up with gardens through each other.
        /// </summary>
        private static void FixedGardens(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan)
        {
            int n = houses.Count;
            var fixedIdx = Enumerable.Range(0, n).Where(j => !houses[j].Edit && houses[j].Polished && houses[j].Garden != null && houses[j].Garden.Length >= 3).ToList();
            if (fixedIdx.Count == 0) return;
            int cut = 0;
            for (int i = 0; i < n; i++)
            {
                if (!houses[i].Edit || !touched[i] || plots[i] == null) continue;
                var lot = Lot(houses[i]);
                foreach (int j in fixedIdx)
                {
                    var g = Ccw(houses[j].Garden);
                    if (OverlapArea(plots[i], g) < 1f) continue;
                    if (OverlapArea(lot, g) > 2f && !plan.GardenClashes.Contains(houses[j].Id)) plan.GardenClashes.Add(houses[j].Id);
                    var left = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plots[i]) }, new[] { (IReadOnlyList<float2>)g }, BoolOp.Difference, FillRule.NonZero);
                    var withLot = Poly.Boolean(left.ToList(), new List<Region> { new Region(Ccw(lot)) }, BoolOp.Union);
                    var keep = withLot.OrderByDescending(r => Poly.Contains(r.Outer, houses[i].Position) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
                    if (keep == null) continue;
                    var ring = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(r => Poly.Area(r)).First() : keep.Outer;
                    if (ring == null || ring.Length < 3 || Poly.Area(ring) < 1f) continue;
                    plots[i] = Poly.Simplify(ring, 0.05f);
                    cut++;
                }
            }
            foreach (int j in fixedIdx) plots[j] = Ccw(houses[j].Garden);
            if (cut > 0) plan.Notes.Add($"{cut} gardens kept off their neighbours' gardens");
            if (plan.GardenClashes.Count > 0) plan.Notes.Add($"{plan.GardenClashes.Count} neighbours' gardens lie over a lot here: they are planned again");
        }

        /// <summary>
        /// Last word on where the gardens this plan writes end: none lies over another house's lot or over another of them.
        /// The middle line between two side neighbours runs through a lot where the houses stand at an angle on a curve
        /// (Brecon, the user's city: one garden 92 m² into the next house's lot), and every later step (wraps, strips given to
        /// a neighbour, the house's own lot added back) may reach a little over a lot too. A garden over a lot put a fence
        /// through the neighbour's garden ("crossover errors"), and it never went away: each pass that saw it planned that
        /// garden again (GardenClashes), which made the same garden again, every third of a second for as long as the city
        /// ran, pieces made and taken away each time (fences and outlines flickering). Other lots are cut out (the house's own
        /// lot always stays); land two of these gardens share goes to the house it is nearer.
        /// </summary>
        private static void Separate(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan)
        {
            int n = houses.Count;
            bool Mine(int i) => touched[i] && houses[i].Edit && houses[i].Polished && plots[i] != null && plots[i].Length >= 3;
            var lots = new float2[n][];
            var lotLo = new float2[n];
            var lotHi = new float2[n];
            for (int j = 0; j < n; j++) { lots[j] = Ccw(Lot(houses[j])); Poly.Bounds(lots[j], out lotLo[j], out lotHi[j]); }
            float2[] Keep(List<Region> parts, int i)
            {
                var withLot = Poly.Boolean(parts, new List<Region> { new Region(lots[i]) }, BoolOp.Union);
                var keep = withLot.OrderByDescending(r => Poly.Contains(r.Outer, houses[i].Position) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
                if (keep == null) return null;
                var ring = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(r => Poly.Area(r)).First() : keep.Outer;
                return ring != null && ring.Length >= 3 && Poly.Area(ring) > 1f ? Poly.Simplify(ring, 0.05f) : null;
            }
            int offLots = 0, shared = 0;
            for (int i = 0; i < n; i++)
            {
                if (!Mine(i)) continue;
                Poly.Bounds(plots[i], out var plo, out var phi);
                var over = new List<IReadOnlyList<float2>>();
                for (int j = 0; j < n; j++)
                {
                    if (j == i || lotHi[j].x < plo.x || lotLo[j].x > phi.x || lotHi[j].y < plo.y || lotLo[j].y > phi.y) continue;
                    if (OverlapArea(plots[i], lots[j]) > 0.05f) over.Add(lots[j]);
                }
                if (over.Count == 0) continue;
                var left = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plots[i]) }, over, BoolOp.Difference, FillRule.NonZero);
                var ring = Keep(left, i);
                if (ring == null) continue;
                plots[i] = ring;
                offLots++;
            }
            for (int i = 0; i < n; i++)
            {
                if (!Mine(i)) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (!Mine(j)) continue;
                    Poly.Bounds(plots[i], out var ilo, out var ihi);
                    Poly.Bounds(plots[j], out var jlo, out var jhi);
                    if (jhi.x < ilo.x || jlo.x > ihi.x || jhi.y < ilo.y || jlo.y > ihi.y) continue;
                    var both = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plots[i]) }, new[] { (IReadOnlyList<float2>)Ccw(plots[j]) }, BoolOp.Intersection, FillRule.NonZero);
                    if (both.Sum(r => r.Area) < 0.5f) continue;
                    // each piece of the shared land to the house it is nearer (its lot's edge, 0 inside it)
                    var toI = new List<IReadOnlyList<float2>>();
                    var toJ = new List<IReadOnlyList<float2>>();
                    foreach (var r in both)
                    {
                        if (r.Area < 0.05f) continue;
                        var c = Poly.Centroid(r.Outer);
                        float di = Poly.Contains(lots[i], c) ? 0f : DistanceToRing(lots[i], c);
                        float dj = Poly.Contains(lots[j], c) ? 0f : DistanceToRing(lots[j], c);
                        (di <= dj ? toI : toJ).Add(Ccw(r.Outer));
                    }
                    if (toJ.Count > 0)
                    {
                        var ring = Keep(Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plots[i]) }, toJ, BoolOp.Difference, FillRule.NonZero), i);
                        if (ring != null) plots[i] = ring;
                    }
                    if (toI.Count > 0)
                    {
                        var ring = Keep(Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plots[j]) }, toI, BoolOp.Difference, FillRule.NonZero), j);
                        if (ring != null) plots[j] = ring;
                    }
                    shared++;
                }
            }
            if (offLots + shared > 0) plan.Notes.Add($"{offLots} gardens cut off other houses' lots, {shared} pairs of gardens over each other split");
        }

        /// <summary>A polished garden this plan does not edit and whose garden another pass wrote (StitchHouse.Garden): fixed.</summary>
        private static bool IsFixed(StitchHouse h) => !h.Edit && h.Polished && h.Garden != null && h.Garden.Length >= 3;

        // the smallest and largest x and y of some points (Aggregate with math.min made a delegate each time: 19 MB of a city's
        // 25 plans)
        private static float2 MinOf(IReadOnlyList<float2> pts)
        {
            var m = new float2(float.MaxValue);
            for (int i = 0; i < pts.Count; i++) m = math.min(m, pts[i]);
            return m;
        }

        private static float2 MaxOf(IReadOnlyList<float2> pts)
        {
            var m = new float2(float.MinValue);
            for (int i = 0; i < pts.Count; i++) m = math.max(m, pts[i]);
            return m;
        }

        private static float OverlapArea(IReadOnlyList<float2> a, IReadOnlyList<float2> b)
        {
            // (shapes whose bounds lie apart share nothing: no boolean for them, asked for every garden against every other)
            Poly.Bounds(a, out var alo, out var ahi);
            Poly.Bounds(b, out var blo, out var bhi);
            if (ahi.x < blo.x - 0.01f || alo.x > bhi.x + 0.01f || ahi.y < blo.y - 0.01f || alo.y > bhi.y + 0.01f) return 0f;
            return Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(a.ToArray()) }, new[] { (IReadOnlyList<float2>)Ccw(b.ToArray()) }, BoolOp.Intersection, FillRule.NonZero).Sum(r => r.Area);
        }

        private static IEnumerable<IReadOnlyList<float2>> RingsOf(IEnumerable<Region> regions)
        {
            foreach (var r in regions)
            {
                yield return r.Outer;
                foreach (var h in r.Holes) yield return h;
            }
        }

        /// <summary>The piece object of a house's fence kind ("FenceResidentialHigh01" → "FenceResidentialPieceHigh01"), or the
        /// look's: 1 low hedge, 2 tall hedge, 3 low wood fence, 4 tall wood fence.</summary>
        /// <summary>
        /// The plot cut back where it grows past its lot onto land steeper than the house's maxSlope (%) or further than its
        /// maxRise (m) above or below the house: on each side (left, right, back), stepping out from the lot edge, the first
        /// line across the plot where the ground fails ends it there (null: nothing to cut).
        /// </summary>
        private static float2[] SlopeCut(StitchHouse h, float2[] plot, StitchSettings s, bool cutLeft = true, bool cutRight = true)
        {
            float maxSlope = K(h, s, "maxSlope", 80f) / 100f, maxRise = K(h, s, "maxRise", 0f);
            if (maxSlope <= 0f && maxRise <= 0f) return null;
            var g = s.Ground;
            float home = g(h.Position);
            // the plot's extent in the house's frame
            float xMin = 0f, xMax = 0f, zMin = 0f;
            foreach (var p in plot)
            {
                var l = Local(h, p);
                xMin = math.min(xMin, l.x); xMax = math.max(xMax, l.x); zMin = math.min(zMin, l.y);
            }
            float hx = h.HalfLot.x, hz = h.HalfLot.y;
            const float Step = 2f, E = 1.5f;
            bool Fails(float2 local)
            {
                var p = h.ToWorld(local);
                if (maxRise > 0f && math.abs(g(p) - home) > maxRise) return true;
                if (maxSlope <= 0f) return false;
                float gx = (g(p + new float2(E, 0f)) - g(p - new float2(E, 0f))) / (2f * E);
                float gz = (g(p + new float2(0f, E)) - g(p - new float2(0f, E))) / (2f * E);
                return math.sqrt(gx * gx + gz * gz) > maxSlope;
            }
            var poly = plot.ToList();
            bool cutAny = false;
            // the back first, across the lot's width; then each side, along the lot's depth (the corners past both follow)
            {
                float edge = hz, far = -zMin;
                for (float t = edge + Step; t < far + 0.01f; t += Step)
                {
                    bool fail = false;
                    for (int k = 0; k <= 4 && !fail; k++) fail = Fails(new float2(math.lerp(-hx, hx, k / 4f), -t));
                    if (!fail) continue;
                    float at = math.max(edge, t - Step * 0.5f);
                    poly = ClipHalfPlane(poly, h.ToWorld(new float2(0f, -at)), h.Right, h.Position);
                    cutAny = true;
                    break;
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                if (side < 0 ? !cutLeft : !cutRight) continue;
                float edge = hx, far = side < 0 ? -xMin : xMax;
                for (float t = edge + Step; t < far + 0.01f; t += Step)
                {
                    bool fail = false;
                    for (int k = 0; k <= 4 && !fail; k++) fail = Fails(new float2(side * t, math.lerp(-hz, hz, k / 4f)));
                    if (!fail) continue;
                    float at = math.max(edge, t - Step * 0.5f);
                    poly = ClipHalfPlane(poly, h.ToWorld(new float2(side * at, 0f)), h.Forward, h.Position);
                    cutAny = true;
                    break;
                }
            }
            if (!cutAny || poly.Count < 3 || Poly.Area(poly) < 1f) return null;
            return poly.ToArray();
        }

        private static float2 Local(StitchHouse h, float2 p) => new float2(math.dot(p - h.Position, h.Right), math.dot(p - h.Position, h.Forward));

        /// <summary>A polygon clipped to a convex plot (each of the plot's edges a half-plane keeping the house's side).</summary>
        private static List<float2> ClipToPlot(List<float2> poly, StitchHouse h, float2[] plot)
        {
            var r = poly;
            for (int i = 0; i < plot.Length && r.Count >= 3; i++)
            {
                var a = plot[i]; var b = plot[(i + 1) % plot.Length];
                if (math.distancesq(a, b) < 1e-6f) continue;
                r = ClipHalfPlane(r, a, math.normalize(b - a), h.Position);
            }
            return r;
        }

        public static string PieceFor(StitchHouse h, int kind)
        {
            switch (kind)
            {
                case 1: return "HedgePieceLow01";
                case 2: return "HedgePieceHigh01";
                case 3: return "FenceResidentialPieceLow01";
                case 4: return "FenceResidentialPieceHigh01";
            }
            var main = h.Lines.GroupBy(l => l.Name).OrderByDescending(g => g.Sum(l => math.distance(l.A, l.B))).Select(g => g.Key).FirstOrDefault();
            if (!string.IsNullOrEmpty(main))
            {
                var m = System.Text.RegularExpressions.Regex.Match(main, @"^(.*?)(High|Low)(\d+)$");
                if (m.Success) return m.Groups[1].Value + "Piece" + m.Groups[2].Value + m.Groups[3].Value;
            }
            return ThemeOf(h) == "yard" ? "FenceIndustrialPieceLow01" : "HedgePieceLow01";
        }

        /// <summary>Worn patches in big gardens and a band along the roads and paths beside them, in each look's ground.</summary>
        private static void GroundPatches(IReadOnlyList<StitchHouse> houses, float2[][] plots, bool[] touched, StitchPlan plan, StitchSettings s)
        {
            for (int i = 0; i < houses.Count; i++)
            {
                var h = houses[i];
                if (!touched[i] || !h.Edit || !h.Polished || plots[i] == null || K(h, s, "paint", 1f) < 0.5f) continue;
                var plot = Ccw(plots[i]);
                // a few worn patches: more the bigger the garden
                int patchLayer = (int)K(h, s, "patchLayer", 3f);
                float chance = K(h, s, "patchChance", 0f);
                if (patchLayer > 0 && chance > 0f)
                {
                    float newLand = Poly.Area(plot) - 4f * h.HalfLot.x * h.HalfLot.y;
                    int tries = (int)math.clamp(newLand / 250f, 0f, 12f);
                    var lot = Lot(h);
                    for (int k = 0; k < tries; k++)
                    {
                        uint hk = Hash((ulong)h.Id * 0x51ED27UL + (ulong)k * 0x9E37UL);
                        if (hk % 100 >= (uint)chance) continue;
                        var lo = MinOf(plot);
                        var hi = MaxOf(plot);
                        var at = new float2(math.lerp(lo.x, hi.x, ((hk >> 8) & 1023) / 1023f), math.lerp(lo.y, hi.y, ((hk >> 18) & 1023) / 1023f));
                        if (!Poly.Contains(plot, at) || Poly.Contains(lot, at) || DistanceToRing(plot, at) < 2.5f) continue;
                        float r = 1.8f + ((hk >> 4) & 15) / 15f * 2.5f;
                        Paint(plan, Blob(at, r, hk, 10), patchLayer, 0.45f + ((hk >> 28) & 7) / 7f * 0.25f, 1.6f, 1.2f, 3f, hk);
                    }
                }
                // a band along the roads and paths beside the garden
                int roadLayer = (int)K(h, s, "roadLayer", 0f);
                float width = K(h, s, "roadWidth", 4f);
                if (roadLayer > 0 && width > 0.3f && s_barriers.Count > 0)
                {
                    var near = new List<float2[]>();
                    var plo = MinOf(plot) - width;
                    var phi = MaxOf(plot) + width;
                    foreach (int b in BarriersIn(plo, phi))
                    {
                        if (s_barrierKind != null && b < s_barrierKind.Count && s_barrierKind[b] != 0) continue;
                        near.Add(s_barriers[b].Ring);
                    }
                    if (near.Count == 0) continue;
                    var grownRoads = near.SelectMany(r => Poly.Offset(r, -width)).Select(r => (IReadOnlyList<float2>)Ccw(r)).ToList();
                    var band = Poly.Boolean(grownRoads, near.Select(r => (IReadOnlyList<float2>)r), BoolOp.Difference, FillRule.NonZero);
                    var inGarden = Poly.Boolean(band, new List<Region> { new Region(plot) }, BoolOp.Intersection);
                    foreach (var r in inGarden.Where(r => r.Area >= 4f))
                        foreach (var piece in r.HasHoles ? Poly.CutHoles(r, 0f) : new List<float2[]> { r.Outer })
                            Paint(plan, Poly.Simplify(piece, 0.3f), roadLayer, K(h, s, "roadStrength", 0.5f), 1.4f, 1f, 3f, (uint)(h.Id * 7919L) ^ (uint)Poly.Area(piece));
                }
            }
        }

        // the plan's barriers (StitchSettings.Barriers) with their bounds; per plan, on its thread
        [ThreadStatic] private static List<(float2 Min, float2 Max, float2[] Ring)> s_barriers;
        [ThreadStatic] private static List<int> s_barrierKind;
        [ThreadStatic] private static List<long> s_barrierGroup;
        // each barrier grown by the margin, worked out once per plan (by the ring itself)
        [ThreadStatic] private static Dictionary<float2[], (float Margin, List<float2[]> Rings)> s_grown;
        // water grown by a water gap, once per plan, ring and gap
        [ThreadStatic] private static Dictionary<(float2[] Ring, float Gap), List<float2[]>> s_grownWater;
        /// <summary>StitchSettings.BarrierKinds: water.</summary>
        private const int WaterKind = 2;
        /// <summary>StitchSettings.BarrierKinds: a deck overhead (an elevated road, path or track, a bridge) or a pillar under
        /// one. Where it is lower than the knob "deckClearance" (StitchSettings.BarrierHeights) the ground under it is a crack:
        /// never grown onto nor joined across, like a lot, and kept a deck gap further off than a road; no garden faces it as
        /// its street nor paints a road band along it. Higher, gardens grow under it as if it were not there (but no tree).</summary>
        public const int DeckKind = 3;

        /// <summary>How far a garden keeps off a deck overhead too low to grow under: the barrier margin and the deck gap.</summary>
        private static float DeckMargin(StitchSettings s) => s.Knob("barrierMargin", 0.8f) + math.max(0f, s.Knob("deckGap", 1.5f));

        // every deck overhead of the plan, high or low (a tree keeps off all of them); per plan, on its thread
        [ThreadStatic] private static List<(float2 Min, float2 Max, float2[] Ring)> s_decks;
        /// <summary>How far a tree keeps off a deck overhead (its crown never reaches under it, m).</summary>
        private const float TreeOffDeck = 3f;

        private static bool IsTree(int kind) => kind >= FirstStreetTree && kind < FirstStreetTree + StreetTreeCount;

        /// <summary>
        /// No tree under a deck overhead nor within a crown of one, however high it is ("a huge tree doesn't belong under a
        /// highway, even if it's a really tall highway"): the ground under a high deck is a garden's, its trees go.
        /// </summary>
        private static void TreesOffDecks(StitchPlan plan)
        {
            if (s_decks == null || s_decks.Count == 0) return;
            int removed = plan.Bushes.RemoveAll(b => IsTree(b.Kind) && s_decks.Any(d =>
                b.Position.x >= d.Min.x - TreeOffDeck && b.Position.x <= d.Max.x + TreeOffDeck && b.Position.y >= d.Min.y - TreeOffDeck && b.Position.y <= d.Max.y + TreeOffDeck
                && (Poly.Contains(d.Ring, b.Position) || DistanceToRing(d.Ring, b.Position) < TreeOffDeck)));
            if (removed > 0) plan.Notes.Add($"{removed} trees left out under decks overhead");
        }

        /// <summary>Water grown by <paramref name="gap"/> with round corners (a smooth line however the water was sampled).</summary>
        private static List<float2[]> GrownWater(float2[] ring, float gap)
        {
            s_grownWater ??= new Dictionary<(float2[], float), List<float2[]>>();
            if (s_grownWater.TryGetValue((ring, gap), out var g)) return g;
            g = Poly.Offset(ring, -gap, OffsetJoin.Round, 4f, 0.5f).Select(r => Poly.Simplify(r.Outer, 0.4f)).Where(r => r.Length >= 3).ToList();
            s_grownWater[(ring, gap)] = g;
            return g;
        }

        /// <summary>The water near [lo, hi] grown by <paramref name="gap"/> (0: as it is).</summary>
        private static List<Region> WaterNear(float2 lo, float2 hi, float gap)
        {
            var list = new List<Region>();
            if (s_barriers == null) return list;
            foreach (int k in BarriersIn(lo - gap, hi + gap))
            {
                if (s_barrierKind[k] != WaterKind) continue;
                if (gap < 0.01f) { list.Add(new Region(Ccw(s_barriers[k].Ring))); continue; }
                foreach (var g in GrownWater(s_barriers[k].Ring, gap)) list.Add(new Region(Ccw(g)));
            }
            return list;
        }

        private sealed class SameRing : IEqualityComparer<float2[]>
        {
            public static readonly SameRing Instance = new SameRing();
            public bool Equals(float2[] a, float2[] b) => ReferenceEquals(a, b);
            public int GetHashCode(float2[] r) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(r);
        }

        /// <summary>A barrier grown by <paramref name="margin"/> (Poly.Offset outward), once per plan, and once for a road piece
        /// the game hands every pass again (by its numbers: the same piece grows the same).</summary>
        private static List<float2[]> Grown(float2[] ring, float margin)
        {
            s_grown ??= new Dictionary<float2[], (float, List<float2[]>)>(SameRing.Instance);
            if (s_grown.TryGetValue(ring, out var g) && g.Margin == margin) return g.Rings;
            var key = new RingKey(ring, margin);
            if (!s_grownShared.TryGetValue(key, out var rings))
            {
                rings = Poly.Offset(ring, -margin);
                if (System.Threading.Interlocked.Increment(ref s_grownAdded) > 60000) { s_grownShared.Clear(); s_grownAdded = 0; }
                s_grownShared[key] = rings;
            }
            s_grown[ring] = (margin, rings);
            return rings;
        }

        // grown barriers by their numbers, shared by every plan (they run on several workers at once); never changed once in
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<RingKey, List<float2[]>> s_grownShared = new System.Collections.Concurrent.ConcurrentDictionary<RingKey, List<float2[]>>();
        private static int s_grownAdded;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct FloatBits { [System.Runtime.InteropServices.FieldOffset(0)] public float F; [System.Runtime.InteropServices.FieldOffset(0)] public int I; }
        private static int Bits(float f) => new FloatBits { F = f }.I;

        /// <summary>A ring's exact numbers (and the margin) as a dictionary key.</summary>
        private readonly struct RingKey : IEquatable<RingKey>
        {
            private readonly float2[] _ring;
            private readonly float _margin;
            private readonly int _hash;

            public RingKey(float2[] ring, float margin)
            {
                _ring = ring;
                _margin = margin;
                uint h = 2166136261u ^ (uint)ring.Length;
                foreach (var p in ring) { h = (h ^ (uint)Bits(p.x)) * 16777619u; h = (h ^ (uint)Bits(p.y)) * 16777619u; }
                _hash = (int)(h ^ (uint)Bits(margin));
            }

            public bool Equals(RingKey o)
            {
                if (_hash != o._hash || _margin != o._margin || _ring.Length != o._ring.Length) return false;
                for (int i = 0; i < _ring.Length; i++)
                    if (Bits(_ring[i].x) != Bits(o._ring[i].x) || Bits(_ring[i].y) != Bits(o._ring[i].y)) return false;
                return true;
            }

            public override bool Equals(object obj) => obj is RingKey o && Equals(o);
            public override int GetHashCode() => _hash;
        }

        /// <summary>Two rings' outlines come within <paramref name="d"/> of each other (or cross).</summary>
        private static bool Within(IReadOnlyList<float2> a, IReadOnlyList<float2> b, float d)
        {
            Poly.Bounds(a, out var alo, out var ahi);
            Poly.Bounds(b, out var blo, out var bhi);
            if (ahi.x + d < blo.x || alo.x - d > bhi.x || ahi.y + d < blo.y || alo.y - d > bhi.y) return false;
            float d2 = d * d;
            for (int i = 0; i < a.Count; i++)
            {
                var p = a[i];
                var q = a[(i + 1) % a.Count];
                // (an edge further than d from the other's bounds is further than d from all of it)
                if (math.max(p.x, q.x) + d + 0.001f < blo.x || math.min(p.x, q.x) - d - 0.001f > bhi.x || math.max(p.y, q.y) + d + 0.001f < blo.y || math.min(p.y, q.y) - d - 0.001f > bhi.y) continue;
                for (int j = 0; j < b.Count; j++)
                    if (SegmentDistanceSq(p, q, b[j], b[(j + 1) % b.Count]) <= d2) return true;
            }
            return false;
        }

        private static float SegmentDistanceSq(float2 p, float2 q, float2 r, float2 s)
        {
            if (SegmentsCross(p, q, r, s)) return 0f;
            float PointSeg(float2 x, float2 a, float2 b)
            {
                var ab = b - a;
                float t = math.saturate(math.dot(x - a, ab) / math.max(1e-12f, math.dot(ab, ab)));
                return math.distancesq(x, a + ab * t);
            }
            return math.min(math.min(PointSeg(p, r, s), PointSeg(q, r, s)), math.min(PointSeg(r, p, q), PointSeg(s, p, q)));
        }

        private static void SetBarriers(StitchSettings s, IReadOnlyList<StitchHouse> houses)
        {
            s_barriers = new List<(float2, float2, float2[])>();
            s_barrierKind = new List<int>();
            s_barrierGroup = new List<long>();
            s_decks = new List<(float2, float2, float2[])>();
            s_grown = new Dictionary<float2[], (float, List<float2[]>)>(SameRing.Instance);
            s_grownWater = new Dictionary<(float2[], float), List<float2[]>>();
            if (s.Barriers == null) return;
            // the room a deck must leave to be grown under: the strictest look of the plan's houses (one rule for a patch of
            // town: a garden never gets land under a deck its neighbour's look calls a crack)
            float room = s.Knob("deckClearance", PolishLooks.Defaults["deckClearance"]);
            foreach (var h in houses) room = math.max(room, K(h, s, "deckClearance", room));
            for (int k = 0; k < s.Barriers.Count; k++)
            {
                var r = s.Barriers[k];
                if (r == null || r.Length < 3 || Poly.Area(r) < 0.5f) continue;
                var ring = Ccw(r);
                var lo = MinOf(ring);
                var hi = MaxOf(ring);
                if (s.BarrierKinds != null && k < s.BarrierKinds.Count && s.BarrierKinds[k] == DeckKind)
                {
                    s_decks.Add((lo, hi, ring));
                    // high enough: the ground under it is open land
                    if (s.BarrierHeights != null && k < s.BarrierHeights.Count && s.BarrierHeights[k] >= room) continue;
                }
                s_barriers.Add((lo, hi, ring));
                s_barrierKind.Add(s.BarrierKinds != null && k < s.BarrierKinds.Count ? s.BarrierKinds[k] : 0);
                // a barrier with no group is one of its own (a negative number no group uses)
                s_barrierGroup.Add(s.BarrierGroups != null && k < s.BarrierGroups.Count ? s.BarrierGroups[k] : -1L - k);
            }
            // each group's pieces (a garden never hugs its own street: every piece of it)
            s_barrierGroupMembers = new Dictionary<long, List<int>>();
            for (int k = 0; k < s_barriers.Count; k++)
            {
                if (!s_barrierGroupMembers.TryGetValue(s_barrierGroup[k], out var members)) s_barrierGroupMembers[s_barrierGroup[k]] = members = new List<int>();
                members.Add(k);
            }
            // filed by grid cell, so a question about one spot never looks at every road piece of the patch
            s_barrierCells = new Dictionary<long, List<int>>();
            s_barrierSeen = new int[s_barriers.Count];
            s_barrierQuery = 0;
            for (int k = 0; k < s_barriers.Count; k++)
            {
                var (mn, mx, _) = s_barriers[k];
                int x0 = (int)math.floor(mn.x / BarrierCell), x1 = (int)math.floor(mx.x / BarrierCell), y0 = (int)math.floor(mn.y / BarrierCell), y1 = (int)math.floor(mx.y / BarrierCell);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long key = ((long)x << 32) ^ (uint)y;
                        if (!s_barrierCells.TryGetValue(key, out var l)) s_barrierCells[key] = l = new List<int>();
                        l.Add(k);
                    }
            }
        }

        private const float BarrierCell = 24f;
        [ThreadStatic] private static Dictionary<long, List<int>> s_barrierCells;
        [ThreadStatic] private static Dictionary<long, List<int>> s_barrierGroupMembers;
        [ThreadStatic] private static int[] s_barrierSeen;
        [ThreadStatic] private static int s_barrierQuery;

        /// <summary>The barriers whose bounds meet [lo, hi], in the order they were given (as a scan of all of them finds them).</summary>
        private static List<int> BarriersIn(float2 lo, float2 hi) => BarriersIn(lo, hi, new List<int>());

        /// <summary>BarriersIn into a list of the caller's (cleared first).</summary>
        private static List<int> BarriersIn(float2 lo, float2 hi, List<int> found)
        {
            found.Clear();
            if (s_barrierCells == null || s_barriers.Count == 0) return found;
            int stamp = ++s_barrierQuery;
            int x0 = (int)math.floor(lo.x / BarrierCell), x1 = (int)math.floor(hi.x / BarrierCell), y0 = (int)math.floor(lo.y / BarrierCell), y1 = (int)math.floor(hi.y / BarrierCell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!s_barrierCells.TryGetValue(((long)x << 32) ^ (uint)y, out var l)) continue;
                    foreach (int k in l)
                    {
                        if (s_barrierSeen[k] == stamp) continue;
                        s_barrierSeen[k] = stamp;
                        var (mn, mx, _) = s_barriers[k];
                        if (mn.x <= hi.x && mx.x >= lo.x && mn.y <= hi.y && mx.y >= lo.y) found.Add(k);
                    }
                }
            found.Sort();
            return found;
        }

        /// <summary>Some barrier's bounds meet the box (BarriersIn's test, nothing gathered or sorted).</summary>
        private static bool AnyBarrierIn(float2 lo, float2 hi)
        {
            if (s_barrierCells == null || s_barriers.Count == 0) return false;
            int x0 = (int)math.floor(lo.x / BarrierCell), x1 = (int)math.floor(hi.x / BarrierCell), y0 = (int)math.floor(lo.y / BarrierCell), y1 = (int)math.floor(hi.y / BarrierCell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!s_barrierCells.TryGetValue(((long)x << 32) ^ (uint)y, out var l)) continue;
                    foreach (int k in l)
                    {
                        var (mn, mx, _) = s_barriers[k];
                        if (mn.x <= hi.x && mx.x >= lo.x && mn.y <= hi.y && mx.y >= lo.y) return true;
                    }
                }
            return false;
        }

        /// <summary>A barrier holds the point.</summary>
        private static bool OnBarrier(float2 p)
        {
            // (a point: one cell, nothing to gather or sort; asked for every few centimetres of every garden's edge)
            if (s_barrierCells == null || s_barriers.Count == 0) return false;
            if (!s_barrierCells.TryGetValue(((long)(int)math.floor(p.x / BarrierCell) << 32) ^ (uint)(int)math.floor(p.y / BarrierCell), out var l)) return false;
            foreach (int k in l)
            {
                var (mn, mx, ring) = s_barriers[k];
                if (mn.x <= p.x && mx.x >= p.x && mn.y <= p.y && mx.y >= p.y && Poly.Contains(ring, p)) return true;
            }
            return false;
        }

        /// <summary>A barrier whose bounds meet the box <paramref name="box"/> around the point holds it or comes within
        /// <paramref name="d"/> of it (as a look through BarriersIn would find, without gathering them).</summary>
        private static bool BarrierNear(float2 p, float box, float d)
        {
            if (s_barrierCells == null || s_barriers.Count == 0) return false;
            var lo = p - box;
            var hi = p + box;
            int stamp = ++s_barrierQuery;
            int x0 = (int)math.floor(lo.x / BarrierCell), x1 = (int)math.floor(hi.x / BarrierCell), y0 = (int)math.floor(lo.y / BarrierCell), y1 = (int)math.floor(hi.y / BarrierCell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!s_barrierCells.TryGetValue(((long)x << 32) ^ (uint)y, out var l)) continue;
                    foreach (int k in l)
                    {
                        if (s_barrierSeen[k] == stamp) continue;
                        s_barrierSeen[k] = stamp;
                        var (mn, mx, ring) = s_barriers[k];
                        if (mn.x <= hi.x && mx.x >= lo.x && mn.y <= hi.y && mx.y >= lo.y && (Poly.Contains(ring, p) || DistanceToRing(ring, p) < d)) return true;
                    }
                }
            return false;
        }

        /// <summary>A road, path or track (not a lot, building or water) holds the point or comes within <paramref name="d"/> of it.</summary>
        private static bool RoadNear(float2 p, float d)
        {
            if (s_barrierCells == null || s_barriers.Count == 0) return false;
            var lo = p - d;
            var hi = p + d;
            int stamp = ++s_barrierQuery;
            int x0 = (int)math.floor(lo.x / BarrierCell), x1 = (int)math.floor(hi.x / BarrierCell), y0 = (int)math.floor(lo.y / BarrierCell), y1 = (int)math.floor(hi.y / BarrierCell);
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!s_barrierCells.TryGetValue(((long)x << 32) ^ (uint)y, out var l)) continue;
                    foreach (int k in l)
                    {
                        if (s_barrierSeen[k] == stamp || s_barrierKind[k] != 0) continue;
                        s_barrierSeen[k] = stamp;
                        var (mn, mx, ring) = s_barriers[k];
                        if (mn.x <= hi.x && mx.x >= lo.x && mn.y <= hi.y && mx.y >= lo.y && (Poly.Contains(ring, p) || DistanceToRing(ring, p) < d)) return true;
                    }
                }
            return false;
        }

        /// <summary>The barriers whose bounds meet these points' bounds grown by <paramref name="grow"/>.</summary>
        private static List<float2[]> BarriersNear(IReadOnlyList<float2> pts, float grow)
        {
            if (s_barriers == null || pts == null || pts.Count == 0) return new List<float2[]>();
            var lo = MinOf(pts) - grow;
            var hi = MaxOf(pts) + grow;
            var found = BarriersIn(lo, hi);
            var list = new List<float2[]>(found.Count);
            foreach (int k in found) list.Add(s_barriers[k].Ring);
            return list;
        }

        /// <summary>A road, path, track or other building lies in this land (shrunk by 0.5 m, so land that only touches one
        /// along its edge is open).</summary>
        private static bool Blocked(IReadOnlyList<float2> region)
        {
            if (s_barriers == null || s_barriers.Count == 0 || region == null || region.Count < 3) return false;
            Poly.Bounds(region, out var rlo, out var rhi);
            if (!AnyBarrierIn(rlo, rhi)) return false;
            var ring = Ccw(region.ToArray());
            var inner = Math.Abs(Poly.Area(ring)) > 4f ? Poly.Offset(ring, 0.5f).OrderByDescending(r => Poly.Area(r)).FirstOrDefault() : null;
            var test = inner != null && inner.Length >= 3 ? inner : ring;
            foreach (var b in BarriersNear(test, 0f)) if (Overlaps(test, b)) return true;
            return false;
        }

        /// <summary>The convex hull of a few points, counter-clockwise.</summary>
        private static float2[] Hull(IReadOnlyList<float2> pts)
        {
            var p = pts.Distinct().OrderBy(q => q.x).ThenBy(q => q.y).ToList();
            if (p.Count < 3) return p.ToArray();
            float Cross(float2 o, float2 a, float2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var h = new List<float2>();
            foreach (var q in p) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0f) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lower = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { var q = p[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0f) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1);
            return h.ToArray();
        }

        /// <summary>The nearest barrier along a ray (its distance), not counting <paramref name="skip"/>; water counts
        /// <paramref name="waterPull"/> nearer than it is (a garden stops a water gap short of it).</summary>
        private static bool RayToBarrier(float2 from, float2 dir, float maxT, HashSet<float2[]> skip, out float t, float waterPull = 0f)
        {
            t = float.MaxValue;
            if (s_barriers == null) return false;
            var to = from + dir * (maxT + waterPull);
            var lo = math.min(from, to);
            var hi = math.max(from, to);
            foreach (int bi in BarriersIn(lo, hi))
            {
                var ring = s_barriers[bi].Ring;
                if (skip != null && skip.Contains(ring)) continue;
                if (Poly.Contains(ring, from)) { t = 0f; return true; }
                float pull = s_barrierKind[bi] == WaterKind ? waterPull : 0f;
                for (int k = 0; k < ring.Length; k++)
                {
                    var a = ring[k];
                    var e = ring[(k + 1) % ring.Length] - a;
                    float den = dir.x * e.y - dir.y * e.x;
                    if (math.abs(den) < 1e-6f) continue;
                    var w = a - from;
                    float u = (w.x * dir.y - w.y * dir.x) / den;
                    float s = (w.x * e.y - w.y * e.x) / den;
                    if (u < 0f || u > 1f || s < 0f || s > maxT + pull) continue;
                    if (s - pull < t) t = s - pull;
                }
            }
            return t <= maxT;
        }

        /// <summary>A road, path or track lies right in front of the lot (its street, within 4 m of its front middle).</summary>
        private static bool FacesStreet(StitchHouse h)
        {
            if (s_barriers == null || s_barriers.Count == 0) return false;
            var from = h.Position + h.Forward * (h.HalfLot.y - 0.5f);
            if (!RayToBarrier(from, h.Forward, 4.5f, null, out _)) return false;
            // a road, not another building's lot
            var at = from + h.Forward * 2.5f;
            foreach (int k in BarriersIn(at - 2.5f, at + 2.5f)) if (s_barrierKind[k] == 0 && Overlaps(new[] { from, from + h.Forward * 4.5f + h.Right * 0.1f, from + h.Forward * 4.5f - h.Right * 0.1f }, s_barriers[k].Ring)) return true;
            return false;
        }

        /// <summary>
        /// A house's free side (0 left, 1 right) or back (2): rays straight out from along the edge meet a road, path, track or
        /// building within reach (never its own street, never past another house's lot): the cut along what they met, just
        /// clear of it, and how far it reaches.
        /// </summary>
        private static bool Hug(IReadOnlyList<StitchHouse> houses, int i, int e, StitchSettings s, out Cut cut, out float reach)
        {
            cut = null;
            reach = 0f;
            var h = houses[i];
            float maxReach = s.Knob("hugReach", 80f), margin = s.Knob("barrierMargin", 0.8f);
            var (e0, e1, nrm) = Edge(h, e);
            // its own street (just in front of its lot) is never hugged from a side: the street it faces, every piece of it
            // (its halves and its ends at the junctions: a cul-de-sac's turning circle is still its own street)
            var skip = new HashSet<float2[]>();
            var own = new HashSet<long>();
            foreach (var p in new[] { h.Position + h.Forward * (h.HalfLot.y + 1.5f), h.Position + h.Forward * (h.HalfLot.y + 3f) })
                foreach (int k in BarriersIn(p, p)) if (Poly.Contains(s_barriers[k].Ring, p)) own.Add(s_barrierGroup[k]);
            foreach (long g in own) foreach (int k in s_barrierGroupMembers[g]) skip.Add(s_barriers[k].Ring);
            var hits = new List<float2>();
            float far = 0f;
            const int samples = 7;
            for (int k = 0; k < samples; k++)
            {
                // a side from a little behind the street to the back; the back across its width
                float f = e == 2 ? (k + 0.5f) / samples : 0.15f + 0.85f * k / (samples - 1);
                var from = math.lerp(e0, e1, f) + nrm * 0.05f;
                if (!RayToBarrier(from, nrm, maxReach, skip, out float t, math.max(0f, WaterGap(h, s) - margin)) || t < 1.5f) continue;
                // another house's lot in the way: not this ray
                var hit = from + nrm * t;
                bool lotFirst = false;
                for (int j = 0; j < houses.Count && !lotFirst; j++)
                    if (j != i && math.distance(houses[j].Position, from) < t + math.length(houses[j].HalfLot) + 1f)
                    {
                        var lot = Lot(houses[j]);
                        var inside = Poly.ClipSegmentIntervals(lot, from, hit);
                        if (inside.Any(iv => iv.y - iv.x > 0.01f)) lotFirst = true;
                    }
                if (lotFirst) continue;
                hits.Add(hit);
                far = math.max(far, t);
            }
            if (hits.Count < 2) return false;
            var d = math.normalizesafe(hits[hits.Count - 1] - hits[0], math.normalizesafe(e1 - e0));
            var cn = new float2(-d.y, d.x);
            if (math.dot(cn, h.Position - hits[0]) < 0f) cn = -cn;
            // the line along what was met, clear of every hit (the nearest one decides where)
            float push = hits.Max(p => math.dot(hits[0] - p, cn));
            var at = hits[0] - cn * push + cn * margin;
            cut = new Cut { P = at, D = d, Keep = h.Position };
            reach = far;
            return true;
        }

        /// <summary>
        /// The plot minus every road, path, track and other building near it (grown by <paramref name="margin"/>), every deck
        /// overhead (grown by <paramref name="deckMargin"/>) and the water (grown by <paramref name="waterGap"/>, round: a smooth
        /// line), never cutting the house's own lot; the part that holds the house. Null when nothing is in the way.
        /// </summary>
        private static float2[] ClearOfBarriers(float2[] plot, float2[] lot, float2 house, float margin, float waterGap, float deckMargin)
        {
            float wet = math.max(margin, waterGap), far = math.max(math.max(margin, deckMargin), wet);
            Poly.Bounds(plot, out var plo, out var phi);
            var grown = new List<IReadOnlyList<float2>>();
            foreach (int k in BarriersIn(plo - (far + 0.5f), phi + (far + 0.5f)))
            {
                var (blo, bhi, b) = s_barriers[k];
                if (s_barrierKind[k] == WaterKind)
                {
                    foreach (var g in GrownWater(b, wet)) if (Overlaps(plot, g)) grown.Add(Ccw(g));
                    continue;
                }
                float bm = s_barrierKind[k] == DeckKind ? deckMargin : margin;
                // (grown by the margin a barrier reaches at most four margins out, a miter's limit: one further away is never grown)
                float reach = bm * 4f + 0.01f, m = bm + 0.5f;
                if (bhi.x < plo.x - m || blo.x > phi.x + m || bhi.y < plo.y - m || blo.y > phi.y + m) continue;
                if (!Overlaps(plot, b) && !(Within(plot, b, reach) && Grown(b, bm).Any(g => Overlaps(plot, g)))) continue;
                foreach (var g in Grown(b, bm)) grown.Add(Ccw(g));
            }
            if (grown.Count == 0) return null;
            var left = Poly.Boolean(new[] { (IReadOnlyList<float2>)Ccw(plot) }, grown, BoolOp.Difference, FillRule.NonZero);
            var withLot = Poly.Boolean(left.Select(r => r).ToList(), new List<Region> { new Region(Ccw(lot)) }, BoolOp.Union);
            var keep = withLot.OrderByDescending(r => Poly.Contains(r.Outer, house) ? 1 : 0).ThenByDescending(r => r.Area).FirstOrDefault();
            if (keep == null) return null;
            var ring = keep.HasHoles ? Poly.CutHoles(keep, 0f).OrderByDescending(r => Poly.Area(r)).First() : keep.Outer;
            return ring != null && ring.Length >= 3 && Poly.Area(ring) > 1f ? ring : null;
        }

        private static bool Overlaps(IReadOnlyList<float2> a, IReadOnlyList<float2> b)
        {
            Poly.Bounds(a, out var alo, out var ahi);
            Poly.Bounds(b, out var blo, out var bhi);
            if (ahi.x < blo.x || alo.x > bhi.x || ahi.y < blo.y || alo.y > bhi.y) return false;
            // (the same answer with less looking: a corner outside the other's bounds is not inside it, and two edges cross
            // only inside the box both shapes share, so only edges reaching into that box are tried against each other; this
            // is asked of every lot, garden and road near every pair of gardens)
            var olo = math.max(alo, blo) - 0.001f;
            var ohi = math.min(ahi, bhi) + 0.001f;
            for (int i = 0; i < a.Count; i++)
            {
                var p = a[i];
                if (p.x >= blo.x && p.x <= bhi.x && p.y >= blo.y && p.y <= bhi.y && Poly.Contains(b, p)) return true;
            }
            for (int j = 0; j < b.Count; j++)
            {
                var p = b[j];
                if (p.x >= alo.x && p.x <= ahi.x && p.y >= alo.y && p.y <= ahi.y && Poly.Contains(a, p)) return true;
            }
            for (int i = 0; i < a.Count; i++)
            {
                var p = a[i];
                var q = a[(i + 1) % a.Count];
                if (math.max(p.x, q.x) < olo.x || math.min(p.x, q.x) > ohi.x || math.max(p.y, q.y) < olo.y || math.min(p.y, q.y) > ohi.y) continue;
                for (int j = 0; j < b.Count; j++)
                {
                    var r = b[j];
                    var s = b[(j + 1) % b.Count];
                    if (math.max(r.x, s.x) < olo.x || math.min(r.x, s.x) > ohi.x || math.max(r.y, s.y) < olo.y || math.min(r.y, s.y) > ohi.y) continue;
                    if (SegmentsCross(p, q, r, s)) return true;
                }
            }
            return false;
        }

        private static bool SegmentsCross(float2 p, float2 p2, float2 q, float2 q2)
        {
            float C(float2 o, float2 x, float2 y) => (x.x - o.x) * (y.y - o.y) - (x.y - o.y) * (y.x - o.x);
            return C(p, p2, q) * C(p, p2, q2) < 0f && C(q, q2, p) * C(q, q2, p2) < 0f;
        }

        /// <summary>The closest pair of points between two polygons' outlines.</summary>
        private static (float2, float2) Closest(float2[] a, float2[] b)
        {
            float best = float.MaxValue;
            (float2, float2) r = (a[0], b[0]);
            void Try(float2[] from, float2[] to, bool swap)
            {
                foreach (var p in from)
                    for (int i = 0; i < to.Length; i++)
                    {
                        var s0 = to[i];
                        var e = to[(i + 1) % to.Length] - s0;
                        float t = math.saturate(math.dot(p - s0, e) / math.max(1e-6f, math.dot(e, e)));
                        var q = s0 + e * t;
                        float d = math.distancesq(p, q);
                        if (d < best) { best = d; r = swap ? (q, p) : (p, q); }
                    }
            }
            Try(a, b, false);
            Try(b, a, true);
            return r;
        }

        private static IEnumerable<StitchLine> SideLines(StitchHouse h, int side, StitchSettings s)
        {
            float x = side * h.HalfLot.x;
            foreach (var l in h.Lines)
            {
                var a = h.ToLocal(l.A);
                var b = h.ToLocal(l.B);
                if (math.abs(a.x - x) < s.EdgeTolerance && math.abs(b.x - x) < s.EdgeTolerance && math.abs(a.y - b.y) > 1f) yield return l;
            }
        }

        /// <summary>A house's lines lying along its back edge (or front edge).</summary>
        private static IEnumerable<StitchLine> EdgeLines(StitchHouse h, bool back, StitchSettings s)
        {
            float z = back ? -h.HalfLot.y : h.HalfLot.y;
            foreach (var l in h.Lines)
            {
                var a = h.ToLocal(l.A);
                var b = h.ToLocal(l.B);
                if (math.abs(a.y - z) < s.EdgeTolerance && math.abs(b.y - z) < s.EdgeTolerance && math.abs(a.x - b.x) > 1f) yield return l;
            }
        }

        /// <summary>A line's new place reads as the same fence carried on: it turns at most a little and grows at most a little.</summary>
        private static bool NaturalStretch((float2 A, float2 B) was, (float2 A, float2 B) next, StitchSettings s, float maxTurnDegrees = 20f, float maxGrowth = -1f)
        {
            if (maxGrowth < 0f) maxGrowth = s.Knob("maxStretch", 12f);
            float l0 = math.distance(was.A, was.B), l1 = math.distance(next.A, next.B);
            if (l0 < 0.5f || l1 < 0.5f) return l1 - l0 <= maxGrowth;
            float cos = math.dot((was.B - was.A) / l0, (next.B - next.A) / l1);
            // a fence carried straight on reads as the same fence however far (a garden made deeper)
            if (cos >= math.cos(math.radians(3f))) maxGrowth = math.max(maxGrowth, s.Knob("maxStraightStretch", 30f));
            return l1 - l0 <= maxGrowth && cos >= math.cos(math.radians(maxTurnDegrees));
        }

        /// <summary>The merged region that still holds the original lawn (a piece that does not touch it never replaces it).</summary>
        private static Region Keeping(List<Region> merged, float2[] ring)
        {
            var probe = Poly.Centroid(ring);
            if (!Poly.Contains(ring, probe)) probe = ring[0] + (ring[1] - ring[0]) * 0.5f + (ring[2] - ring[1]) * 0.01f;
            return merged.FirstOrDefault(r => Poly.Contains(r.Outer, probe)) ?? merged.OrderByDescending(r => r.Area).First();
        }

        private static float2[] Ccw(float2[] r) => Poly.IsCcw(r) ? r : Enumerable.Reverse(r).ToArray();   // (not the span Reverse a newer C# picks)
        private static float2[] Ccw(List<float2> r) => Ccw(r.ToArray());

        /// <summary>The corners of an outline that turn more than 10° (wiggles under 30 cm left out).</summary>
        private static int Corners(float2[] ring)
        {
            var r = Poly.Simplify(ring, 0.3f);
            int n = r.Length, c = 0;
            for (int k = 0; k < n; k++)
            {
                var d1 = math.normalizesafe(r[k] - r[(k + n - 1) % n]);
                var d2 = math.normalizesafe(r[(k + 1) % n] - r[k]);
                if (math.dot(d1, d2) < 0.985f) c++;
            }
            return c;
        }
    }
}

// rf-shops3 — the architect's kit for the commercial types (docs/refactor/SHOPS-ARCHITECT.md, "The architect's concepts").
//
// A composition (strip mall rows, the mall district grid, the mall park ring) decides the roads, the plots and the lots.
// This kit designs everything round them, so the site reads as a designed place and not as lots with lawn left over:
//   • KEPT LAND: the composition keeps land out of the lots before they are planned (a planted frontage along the main
//     street, a pedestrian spine through the lot, a pocket plaza at the shopfront, a retention pond) and the kit dresses it
//     after the lots (DressKept);
//   • LANDSCAPE: every leftover piece is planted as a designed edge (tree groves with shrubs, flowerbeds at the drives, a
//     low hedge screening the lot from the street, mulch painted under the groves), not a lawn with a loose tree line;
//   • WALKS: footpaths and bike paths as nets (a spine attached to the street, a meandering promenade through the
//     frontage, crosswalks where a walk joins a lane at a node), bike racks and food trucks at the plazas;
//   • GROUND: terrain paint — dirt on every plot that has not grown yet (a building site), desire paths where people
//     would cut a corner, mulch under the groves, lush ground in the rain gardens.
// Every piece is an option (panel group "Extras", shared keys Placemaker.Commercial.<id>); a type without these controls
// keeps its old behaviour (Extras all off: Buffer instead of Landscape).

using System;
using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Park;
using Placemaker.Core.Layers.Plaza;
using Placemaker.Core.Paint;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        // ───────────── the extras (options) ─────────────

        /// <summary>The architect's extras, read once from the type's settings (all off when the type has none of the controls).</summary>
        public sealed class Extras
        {
            /// <summary>"walkways": the spine through the lot, walks in from the streets, the frontage promenade, crosswalks.</summary>
            public bool Walkways;
            /// <summary>"pocketPlaza": a paved plaza with seating where the spine meets the shops.</summary>
            public bool Plaza;
            /// <summary>"bikes": none | racks | paths (paths include racks).</summary>
            public string Bikes = "none";
            /// <summary>"foodTrucks": food carts parked at the plazas.</summary>
            public int Trucks;
            /// <summary>"pads": pad plots (a restaurant, a bank) by the main street.</summary>
            public bool Pads;
            /// <summary>"landscape": the leftovers as designed planting (groves, flowerbeds, hedges) instead of lawn and a tree line.</summary>
            public bool Designed;
            /// <summary>"hedges": a low hedge between the lots and the streets.</summary>
            public bool Hedges;
            /// <summary>"buildingSites": dirt painted on the plots that have not grown yet.</summary>
            public bool Sites;
            /// <summary>"wornGround": desire paths, mulch under the groves, worn ground at the walk ends (terrain paint).</summary>
            public bool Worn;
            /// <summary>"water": none | pond | rainGardens | both.</summary>
            public string Water = "none";
            /// <summary>"plotTrees": trees and shrubs at the back of every plot (a building that grows clears what is in its way; the rest stays green).</summary>
            public bool PlotTrees;

            public bool Racks => Bikes == "racks" || Bikes == "paths";
            public bool BikePaths => Bikes == "paths";
            public bool Pond => Water == "pond" || Water == "both";
            public bool RainGardens => Water == "rainGardens" || Water == "both";
            public bool Paint => Sites || Worn || RainGardens || PlotTrees;

            public static Extras Read(LayerContext ctx)
            {
                var s = ctx?.Settings;
                if (s == null) return new Extras();
                return new Extras
                {
                    Walkways = s.B("walkways", false),
                    Plaza = s.B("pocketPlaza", false),
                    Bikes = s.S("bikes", "none"),
                    Trucks = (int)math.round(s.Num("foodTrucks", 0)),
                    Pads = s.B("pads", false),
                    Designed = s.S("landscape", "plain") == "designed",
                    Hedges = s.B("hedges", false),
                    Sites = s.B("buildingSites", false),
                    Worn = s.B("wornGround", false),
                    Water = s.S("water", "none"),
                    PlotTrees = s.B("plotTrees", false),
                };
            }
        }

        private Extras _extras;
        /// <summary>The extras of this build (read from the settings on first use).</summary>
        public Extras X => _extras ?? (_extras = Extras.Read(Ctx));

        // ───────────── kept land ─────────────

        public enum KeepKind { Frontage, Spine, Plaza, Pond, Island }

        /// <summary>Land the design keeps out of the lots until it is dressed (<see cref="DressKept"/>).</summary>
        public sealed class Kept
        {
            public float2[] Ring;
            public KeepKind Kind;
            /// <summary>The frame it was laid out in (frontage: along the street, t inward; spine: along the walk).</summary>
            public StreetShops.Run Frame;
            public float S0, S1, T0, T1;
            /// <summary>Points the dressing's walks must pass through (the spines crossing a frontage).</summary>
            public readonly List<float2> Anchors = new List<float2>();
            /// <summary>The walk laid through it (spine), for the tree rows either side.</summary>
            public readonly List<float2> Walk = new List<float2>();
            /// <summary>Where the shops are, seen from a plaza (unit vector).</summary>
            public float2 Facing;
            /// <summary>The pond's own outline inside the kept ring (organic), for a pond.</summary>
            public float2[] Inner;
            public uint Salt;
        }

        public readonly List<Kept> KeptLand = new List<Kept>();

        /// <summary>(rf-shops3) Forgets the design counters and lists a composition left behind (a variant tried and taken back).</summary>
        public void ResetDesign()
        {
            KeptLand.Clear(); RowHolds.Clear(); SpineFronts.Clear(); Drives.Clear(); KerbDrives.Clear();
            BikeRackCount = 0; FoodCartCount = 0; _paint = null;
        }

        /// <summary>Holds a convex piece of land for the design; null when it is not free.</summary>
        public Kept Keep(float2[] ring, KeepKind kind, uint salt)
        {
            if (ring == null || ring.Length < 3 || !Fits(ring)) return null;
            var k = new Kept { Ring = Hold(ring), Kind = kind, Salt = salt };
            KeptLand.Add(k);
            return k;
        }

        /// <summary>A rectangle of a frame kept for the design (frame coordinates kept for the dressing).</summary>
        public Kept KeepRect(StreetShops.Run frame, float s0, float s1, float t0, float t1, KeepKind kind, uint salt)
        {
            var k = Keep(Rect(frame, s0, s1, t0, t1), kind, salt);
            if (k == null) return null;
            k.Frame = frame; k.S0 = s0; k.S1 = s1; k.T0 = t0; k.T1 = t1;
            return k;
        }

        /// <summary>Gives a kept piece back (a composition that changed its mind).</summary>
        public void Unkeep(Kept k)
        {
            if (k == null) return;
            Release(k.Ring);
            KeptLand.Remove(k);
        }

        /// <summary>Dresses every kept piece (after the lots): frontage, spine, plaza, pond.</summary>
        public void DressKept()
        {
            // Ponds first (the water's margin is planting), plazas before the frontage (the frontage walks keep off them).
            var order = new List<Kept>(KeptLand);
            order.Sort((a, b) => Rank(a.Kind).CompareTo(Rank(b.Kind)));
            int Rank(KeepKind k) => k == KeepKind.Pond ? 0 : k == KeepKind.Plaza ? 1 : k == KeepKind.Spine ? 2 : 3;
            foreach (var k in order)
            {
                Ctx.Check();
                Release(k.Ring);
                switch (k.Kind)
                {
                    case KeepKind.Pond: DressPond(k); break;
                    case KeepKind.Plaza: DressPlaza(k); break;
                    case KeepKind.Spine: DressSpine(k); break;
                    case KeepKind.Island: Landscape(k.Ring, k.Salt); break;
                    default: DressFrontage(k); break;
                }
            }
            KeptLand.Clear();
        }

        // ───────────── walks ─────────────

        /// <summary>The pathway asset of the walks (automatic pool), or the bike path names for a bike route.</summary>
        private static AssetRef WalkAsset(bool bike) => bike ? new AssetRef("", ParkPathNets.BikeNames) : new AssetRef("");

        /// <summary>
        /// Emits a walk along <paramref name="pts"/> as straight pieces (identical nodes): the first point joined to the street
        /// it faces when <paramref name="streetOutward"/> is given (the walk then starts on the street: a crossing in the
        /// city's sidewalk), the last joined to <paramref name="endOn"/> at a node (the lane is split there at Finish: a
        /// crosswalk). <paramref name="splitAt"/>: interior points where other walks meet this one (kept as vertices).
        /// Returns the points as emitted (the street end moved onto the street), or null.
        /// </summary>
        public List<float2> PathChain(IList<float2> pts, float2? streetOutward, Lane endOn, bool bike = false, Lane startOn = null)
        {
            if (pts == null || pts.Count < 2) return null;
            var p = new List<float2>(pts);
            long attach = -1; float attachT = 0f;
            if (streetOutward.HasValue)
            {
                if (StreetShops.StreetEnd(Ctx.Input, p[0], streetOutward.Value, out var hit, out attach, out attachT)) p[0] = hit;
                else attach = -1;
            }
            if (startOn != null) p[0] = OnNode(startOn, Project(startOn, p[0], out _));
            if (endOn != null) p[p.Count - 1] = OnNode(endOn, Project(endOn, p[p.Count - 1], out _));
            // Drop pieces shorter than a metre (a node closer than that is the same node to the game).
            for (int i = p.Count - 2; i >= 1; i--) if (math.distance(p[i], p[i + 1]) < 1f) p.RemoveAt(i);
            if (p.Count < 2 || math.distance(p[0], p[p.Count - 1]) < 2f) return null;
            var role = bike ? NetRole.PathwayWide : NetRole.Pathway;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                if (math.distance(p[i], p[i + 1]) < 0.5f) continue;
                var net = Ctx.StraightNet(p[i], p[i + 1], role, WalkAsset(bike));
                if (bike) net.Category = AssetCategories.BikePath;
                if (i == 0 && attach >= 0) { net.StartAttach = attach; net.StartAttachT = attachT; }
            }
            if (startOn != null) startOn.Joins.Add(p[0]);
            if (endOn != null) endOn.Joins.Add(p[p.Count - 1]);
            for (int i = 0; i + 1 < p.Count; i++) Walks.Add((p[i], p[i + 1]));
            return p;
        }

        /// <summary>
        /// A meandering line from <paramref name="a"/> to <paramref name="b"/> (frame of the straight line), swinging up to
        /// <paramref name="amp"/> m either side with a wavelength of about <paramref name="wave"/> m, passing exactly through
        /// <paramref name="anchors"/> (points on or near the straight line; they become vertices). Points every ~3 m.
        /// </summary>
        public static List<float2> Meander(float2 a, float2 b, float amp, float wave, IList<float2> anchors, uint seed)
        {
            var res = new List<float2>();
            float len = math.distance(a, b);
            if (len < 1f) { res.Add(a); res.Add(b); return res; }
            var d = (b - a) / len; var n = new float2(-d.y, d.x);
            // Stations: the ends and the anchors, in order along the line.
            var st = new List<(float u, float v)> { (0f, 0f), (len, 0f) };
            if (anchors != null)
                foreach (var q in anchors)
                {
                    float u = math.dot(q - a, d);
                    if (u > 1f && u < len - 1f) st.Add((u, math.dot(q - a, n)));
                }
            st.Sort((x, y) => x.u.CompareTo(y.u));
            float phase = Hashing.Unit(seed) > 0.5f ? 1f : -1f;
            for (int k = 0; k + 1 < st.Count; k++)
            {
                float u0 = st[k].u, u1 = st[k + 1].u, span = u1 - u0;
                int halves = math.max(1, (int)math.round(span / (0.5f * math.max(8f, wave))));
                int m = math.max(2, (int)math.ceil(span / 3f));
                for (int i = 0; i < m; i++)
                {
                    float t = (float)i / m;
                    float u = u0 + span * t;
                    // The base line runs from one station's offset to the next; the swing is zero at both.
                    float v = math.lerp(st[k].v, st[k + 1].v, t) + phase * amp * math.sin(math.PI * halves * t);
                    res.Add(a + d * u + n * v);
                }
                if (halves % 2 == 1) phase = -phase;
            }
            res.Add(b);
            return res;
        }

        // ───────────── dressing the kept pieces ─────────────

        private void DressFrontage(Kept k)
        {
            var piece = k.Ring;
            var region = LayerRegion.From(piece);
            if (region == null) return;
            List<float2> walk = null;
            if (X.Walkways && k.S1 - k.S0 >= 30f && k.T1 - k.T0 >= 7f)
            {
                // The promenade: from one end of the frontage to the other, through the spines' anchors, swinging within the band.
                float mid = 0.5f * (k.T0 + k.T1);
                float amp = math.clamp(0.5f * (k.T1 - k.T0) - 2.6f, 0f, 3.2f);
                var a = k.Frame.At(k.S0 + 2.5f, mid); var b = k.Frame.At(k.S1 - 2.5f, mid);
                var line = Meander(a, b, amp, 44f, k.Anchors, Hashing.Mix(Ctx.Seed, k.Salt));
                // Never along the street's edge: the swing stays 4.5 m in from the street side of the band (a walk running a few
                // metres off the outline reads as a spike along the kerb), 2 m from the lot side.
                float tLo = k.T0 + 4.5f, tHi = math.max(tLo, k.T1 - 2f);
                for (int i = 0; i < line.Count; i++)
                {
                    float su = math.dot(line[i] - k.Frame.A, k.Frame.Dir), tv = math.dot(line[i] - k.Frame.A, k.Frame.In);
                    line[i] = k.Frame.At(su, math.clamp(tv, tLo, tHi));
                }
                // A curved frontage (a round site): the walk keeps to the piece, its ends where the piece narrows.
                while (line.Count > 2 && (!region.Contains(line[0]) || region.BoundaryDistance(line[0], 4f) < 2.2f)) line.RemoveAt(0);
                while (line.Count > 2 && (!region.Contains(line[line.Count - 1]) || region.BoundaryDistance(line[line.Count - 1], 4f) < 2.2f)) line.RemoveAt(line.Count - 1);
                if (line.Count >= 2) { a = line[0]; b = line[line.Count - 1]; }
                // Split at the anchors: each piece ends on a spine's node (a T junction of walks).
                var pieceLine = new List<float2>();
                walk = new List<float2>();
                foreach (var p in line)
                {
                    pieceLine.Add(p);
                    bool anchor = false;
                    foreach (var an in k.Anchors) if (math.distancesq(an, p) < 0.01f) anchor = true;
                    if (anchor && pieceLine.Count >= 2)
                    {
                        var e = PathChain(pieceLine, null, null, X.BikePaths);
                        if (e != null) walk.AddRange(e);
                        pieceLine = new List<float2> { p };
                    }
                }
                if (pieceLine.Count >= 2) { var e = PathChain(pieceLine, null, null, X.BikePaths); if (e != null) walk.AddRange(e); }
                if (X.Worn)
                {
                    // Desire lines: from both dead ends to the street (people cut across the lawn to the corner).
                    var kit = PaintKit();
                    foreach (var end in new[] { a, b })
                    {
                        var onStreet = k.Frame.At(math.dot(end - k.Frame.A, k.Frame.Dir), k.T0 - 1.5f);
                        TrailPaint.DesireLine(kit, end, math.lerp(onStreet, end, 0.08f), 1.6f, 0.75f, TerrainMaterials.Dirt, Hashing.Mix(Ctx.Seed, k.Salt + 7u));
                    }
                }
            }
            if (walk != null && walk.Count >= 2)
            {
                // Benches looking at the street every ~28 m, a low garden light between them.
                var reserved = Reserved();
                float total = 0f;
                for (int i = 0; i + 1 < walk.Count; i++) total += math.distance(walk[i], walk[i + 1]);
                int bn = 0;
                for (float at = 10f; at < total - 6f; at += 14f)
                {
                    var (p, dir) = Along(walk, at);
                    var nrm = new float2(-dir.y, dir.x);
                    bool bench = ((int)(at / 14f) & 1) == 0;
                    var q = p + nrm * (bench ? 2.3f : -2.0f) * (math.dot(nrm, k.Frame.In) > 0f ? -1f : 1f);
                    if (!region.Contains(q) || reserved.Blocks(q, 0.7f)) continue;
                    if (bench) Ctx.Object(q, math.atan2(-k.Frame.In.x, -k.Frame.In.y), ObjectRole.Bench, AssetMix.Pick(PlazaArt.Benches, (uint)bn), Hashing.Mix(Ctx.Seed, k.Salt + 0x90u + (uint)bn));
                    else Ctx.Object(q, 0f, ObjectRole.Lamp, AssetMix.Pick(ParkArt.LowLights, (uint)bn), Hashing.Mix(Ctx.Seed, k.Salt + 0xA0u + (uint)bn));
                    reserved.AddDisc(q, 0.8f);
                    bn++;
                }
            }
            {
                // Street trees along the drive behind the frontage (a boulevard into the centre), and on a long frontage a pergola
                // beside the promenade where people wait and meet.
                var reserved = Reserved();
                if (walk != null) for (int i = 0; i + 1 < walk.Count; i++) reserved.AddCorridor(walk[i], walk[i + 1], 2.4f, "walk");
                int st = 0;
                for (float s0 = k.S0 + 5f; s0 <= k.S1 - 5f; s0 += 11f)
                {
                    var p = k.Frame.At(s0, k.T1 - 2.4f);
                    if (!region.Contains(p) || region.BoundaryDistance(p, 3f) < 1.6f || reserved.Blocks(p, 2.2f)) continue;
                    Ctx.Object(p, 0f, ObjectRole.Tree, AssetMix.Pick(PlazaArt.PlaneTrees, (uint)st), Hashing.Mix(Ctx.Seed, k.Salt + 0xC0u + (uint)st), 2);
                    reserved.AddDisc(p, 2.5f);
                    st++;
                }
                if (k.S1 - k.S0 >= 110f && k.T1 - k.T0 >= 10f && AssetAvailability.Has(AssetCategories.Shelter))
                {
                    var p = k.Frame.At(k.S0 + (k.S1 - k.S0) * 0.25f, k.T0 + (k.T1 - k.T0) * 0.35f);
                    if (region.Contains(p) && !reserved.Blocks(p, 3.2f))
                        Ctx.Object(p, math.atan2(k.Frame.In.x, k.Frame.In.y), ObjectRole.Furniture, AssetMix.Pick(PlazaArt.Pergolas, k.Salt), Hashing.Mix(Ctx.Seed, k.Salt + 0xD0u), -1, AssetCategories.Shelter);
                }
            }
            Ctx.Plan.Facts["frontageDressed"] = (Ctx.Plan.Facts.TryGetValue("frontageDressed", out var fd) ? fd : 0) + 1;
            if (walk != null) Ctx.Plan.Facts["promenade"] = (Ctx.Plan.Facts.TryGetValue("promenade", out var pm) ? pm : 0) + walk.Count;
            Landscape(piece, k.Salt, walk, k.Frame, k.T1);
        }

        private void DressSpine(Kept k)
        {
            var region = LayerRegion.From(k.Ring);
            if (region == null) return;
            Ctx.Surface(k.Ring, SurfaceRole.Grass, new AssetRef(""), AssetCategories.SurfaceGrass);
            var reserved = Reserved();
            var rng = Hashing.Rng(Hashing.Mix(Ctx.Seed, k.Salt), "spine");
            if (k.Walk.Count >= 2)
            {
                // A tree on each side of the walk every ~9 m, a lamp every other gap, a bench pair halfway.
                float total = 0f;
                for (int i = 0; i + 1 < k.Walk.Count; i++) total += math.distance(k.Walk[i], k.Walk[i + 1]);
                float off = math.clamp(0.5f * (k.S1 - k.S0) - 1.0f, 2.2f, 3.2f);
                int n = math.max(1, (int)math.floor((total - 6f) / 9f));
                int placed = 0;
                for (int j = 0; j <= n; j++)
                {
                    float at = 3f + (total - 6f) * j / math.max(1, n);
                    var (p, dir) = Along(k.Walk, at);
                    var side = new float2(-dir.y, dir.x);
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        var q = p + side * sgn * off;
                        if (!region.Contains(q) || reserved.Blocks(q, 1.6f)) continue;
                        Ctx.Object(q, rng.NextFloat(0f, 6.283f), ObjectRole.Tree, AssetMix.Pick(PlazaArt.PlaneTrees, (uint)placed), Hashing.Mix(Ctx.Seed, k.Salt + (uint)placed), 2);
                        placed++;
                    }
                    if (j % 2 == 1 && j < n)
                    {
                        var (lp, ld) = Along(k.Walk, at + 4.5f);
                        var q = lp + new float2(-ld.y, ld.x) * (j % 4 == 1 ? 1f : -1f) * 2.2f;
                        if (region.Contains(q) && !reserved.Blocks(q, 0.4f)) Ctx.Object(q, 0f, ObjectRole.Lamp, new AssetRef(""), Hashing.Mix(Ctx.Seed, k.Salt + 0x40u + (uint)j));
                    }
                }
                var (bp, bd) = Along(k.Walk, total * 0.5f);
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var side = new float2(-bd.y, bd.x) * sgn;
                    var q = bp + side * 2.3f + bd * 4.5f;
                    if (region.Contains(q) && !reserved.Blocks(q, 0.8f))
                        Ctx.Object(q, math.atan2(-side.x, -side.y), ObjectRole.Bench, AssetMix.Pick(PlazaArt.Benches, (uint)(sgn + 2)), Hashing.Mix(Ctx.Seed, k.Salt + 0x80u + (uint)(sgn + 2)));
                }
                if (X.Worn)
                {
                    // Worn ground where the walk meets the street (many feet, the lawn gives way).
                    var kit = PaintKit();
                    kit.Radial(k.Walk[0], TerrainMaterials.Dirt, 0.45f, 1.5f, 4.5f, Falloff.Smooth, 1.2f, 3f);
                    // Desire lines: people cut from their cars across the planted strip to the walk, between the trees.
                    int dl = 0;
                    for (float at = 8f; at < total - 8f; at += 17f)
                    {
                        var (p, dir) = Along(k.Walk, at + 4.5f);
                        var side = new float2(-dir.y, dir.x) * ((dl & 1) == 0 ? 1f : -1f);
                        var a = p + side * 1.6f; var b = p + side * (0.5f * (k.S1 - k.S0) + 0.6f) + dir * 2.2f;
                        TrailPaint.DesireLine(kit, a, b, 1.2f, 0.7f, TerrainMaterials.Dirt, Hashing.Mix(Ctx.Seed, k.Salt + 0xE0u + (uint)dl));
                        dl++;
                    }
                }
            }
            Take(k.Ring);
        }

        private void DressPlaza(Kept k)
        {
            var ring = k.Ring;
            Ctx.Surface(ring, SurfaceRole.Plaza, new AssetRef(""), AssetCategories.SurfacePaving);
            var f = k.Frame;
            float w = k.S1 - k.S0, d = k.T1 - k.T0;
            float sm = 0.5f * (k.S0 + k.S1);
            // Frame: s across the plaza, t from the lot side (T0) to the shop lane (T1).
            uint salt = k.Salt;
            var reserved = Reserved();
            float yawToShops = math.atan2(f.In.x, f.In.y);
            // The centre: a fountain on a big plaza, else a tree in a ring of benches.
            var c = f.At(sm, k.T0 + d * 0.45f);
            if (w * d >= 260f && AssetAvailability.Has(AssetCategories.CenterpieceFountain))
                Ctx.Object(c, yawToShops, ObjectRole.Centerpiece, AssetMix.Pick(new[] { "SmallFountain01", "SmallFountain02", "SmallFountain03" }, salt), Hashing.Mix(Ctx.Seed, salt), -1, AssetCategories.CenterpieceFountain);
            else
            {
                Ctx.Object(c, 0f, ObjectRole.Tree, AssetMix.Pick(PlazaArt.PlaneTrees, salt), Hashing.Mix(Ctx.Seed, salt), 2);
                for (int i = 0; i < 3; i++)
                {
                    float ang = yawToShops + 2.094f * i + 0.5f;
                    var q = c + new float2(math.sin(ang), math.cos(ang)) * 2.6f;
                    Ctx.Object(q, ang + math.PI, ObjectRole.Bench, AssetMix.Pick(PlazaArt.Benches, (uint)i), Hashing.Mix(Ctx.Seed, salt + 1u + (uint)i));
                }
            }
            Decal(c + f.In * 1.5f, AssetCategories.DecalLeaves, salt + 0x90u);
            Decal(f.At(sm, k.T0 + 1.2f), "decal.wear", salt + 0x91u, 0.8f);
            // Food carts first (they need the room): along the lot side, either side of where the spine comes in.
            if (X.Trucks > 0)
            {
                int carts = 0;
                foreach (float u in new[] { 0.2f, 0.8f, 0.5f })
                {
                    if (carts >= X.Trucks) break;
                    if (u == 0.5f && w < 26f) continue;
                    carts += FoodCarts(f.At(math.lerp(k.S0, k.S1, u), k.T0 + 2.4f), f.Dir, 1, 4f, f.In, salt + 0x60u + (uint)carts);
                }
                reserved = Reserved();
            }
            // Café tables in the two corners by the shops (under parasols), planters along the lot edge.
            int cafes = 0;
            if (AssetAvailability.Has(AssetCategories.CafeSeating))
                foreach (float u in new[] { 0.18f, 0.82f })
                    for (int j = 0; j < (w >= 18f ? 2 : 1); j++)
                    {
                        var q = f.At(math.lerp(k.S0, k.S1, u) + (j == 0 ? 0f : (u < 0.5f ? 3.6f : -3.6f)), k.T1 - 2.4f);
                        if (reserved.Blocks(q, 1.6f)) continue;
                        Ctx.Object(q, yawToShops, ObjectRole.Furniture, AssetMix.Pick(PlazaArt.Patios, (uint)cafes, "TableSet01", "TableSet02"), Hashing.Mix(Ctx.Seed, salt + 0x10u + (uint)cafes), -1, AssetCategories.CafeSeating);
                        cafes++;
                    }
            for (float s = k.S0 + 1.5f; s <= k.S1 - 1.5f; s += math.max(3f, (w - 3f) / 4f))
            {
                var q = f.At(s, k.T0 + 0.9f);
                if (math.abs(s - sm) < 2.5f || reserved.Blocks(q, 0.5f)) continue;   // the spine comes in at the middle
                Ctx.Object(q, 0f, ObjectRole.Planter, AssetMix.Pick(PlazaArt.CityBushes, (uint)(s * 7f)), Hashing.Mix(Ctx.Seed, salt + 0x20u + (uint)(s * 3f)));
            }
            // A vending machine against the shop-side edge, by the café corner.
            if (AssetAvailability.Has(AssetCategories.Vending))
            {
                var q = f.At(math.lerp(k.S0, k.S1, 0.5f) + 4.5f, k.T1 - 0.9f);
                if (!reserved.Blocks(q, 0.6f)) Ctx.Object(q, yawToShops + math.PI, ObjectRole.Furniture, new AssetRef("", "VendingMachine01", "VendingMachine02"), Hashing.Mix(Ctx.Seed, salt + 0x70u), -1, AssetCategories.Vending);
            }
            // Lamps at the two lot-side corners.
            foreach (float u in new[] { 0f, 1f })
                Ctx.Object(f.At(math.lerp(k.S0 + 1f, k.S1 - 1f, u), k.T0 + 1f), 0f, ObjectRole.Lamp, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + 0x30u + (uint)(u * 5f)));
            // Bike racks along one side, food carts along the other.
            if (X.Racks) BikeRacks(f.At(k.S0 + 1.3f, k.T0 + d * 0.55f), f.In, math.min(5, (int)(d / 1.8f)), salt + 0x50u);
            Take(ring);
        }

        private void DressPond(Kept k)
        {
            var inner = k.Inner;
            bool water = inner != null && Fill(inner, PondLayer.LayerId, "plain", null, k.Salt, false, b =>
            {
                b.Values["depth"] = 2.0;
                b.Values["islands"] = 0.0;
                b.Values["jets"] = 0.0;
                b.Values["promenade"] = false;
                b.Values["connect"] = false;
            });
            // The margin round the water: a rain garden (lush ground, wet trees and shrubs), the rest of the kept ring.
            List<Region> rest = null;
            try { rest = water ? Poly.Boolean(new[] { (IReadOnlyList<float2>)k.Ring }, new[] { (IReadOnlyList<float2>)Poly.EnsureCcw(inner) }, BoolOp.Difference) : null; }
            catch (Exception) { rest = null; }
            if (rest == null) { Landscape(k.Ring, k.Salt + 3u, null, default, 0f, true); return; }
            uint j = 0;
            foreach (var r in Simple(rest, 20f)) Landscape(r, k.Salt + 3u + j++, null, default, 0f, true);
            if (water)
            {
                var kit = PaintKit();
                // Lush ground fading out from the water's edge.
                kit.Shape(PaintShape.Polygon(Grow(inner, 5f), 3f, 1.2f, 6f, Hashing.Mix(Ctx.Seed, k.Salt)), TerrainMaterials.Lush, 0.7f);
            }
        }

        // ───────────── landscape (every leftover) ─────────────

        /// <summary>
        /// A designed planted piece: lawn, tree groves with shrubs round them (gaps between, so it reads as planted, not
        /// stocked), mulch painted under each grove, flowerbeds where a drive enters the site beside it, a low hedge along the
        /// lots, the given walk kept clear. Pieces too thin for a grove get an irregular single row; slivers stay lawn.
        /// <paramref name="frame"/> / <paramref name="lotSideT"/>: for a frontage, the frame and the line of the lot side (hedge).
        /// <paramref name="wet"/>: a rain garden (wet trees and shrubs, lush ground).
        /// </summary>
        public void Landscape(float2[] piece, uint salt, List<float2> walk = null, StreetShops.Run frame = default, float lotSideT = 0f, bool wet = false)
        {
            if (!X.Designed && !wet) { Buffer(piece, salt); return; }
            var region = LayerRegion.From(piece);
            if (region == null) return;
            Lawn(piece);
            var rng = Hashing.Rng(Hashing.Mix(Ctx.Seed, salt), "landscape");
            var reserved = Reserved();
            if (walk != null) for (int i = 0; i + 1 < walk.Count; i++) reserved.AddCorridor(walk[i], walk[i + 1], 2.2f, "walk");
            region.DeepestPoint(out float clearance, null, 16);
            bool rain = wet || (X.RainGardens && TouchesLot(piece));
            string[] trees = rain ? new[] { "BirchTree01", "EU_AlderTree01", "EU_PoplarTree01" } : ParkArt.ParkTrees;
            var placed = new List<float2>();
            var kit = X.Paint ? PaintKit() : null;
            bool TryTree(float2 p, float edge, float spacing, int age)
            {
                if (!region.Contains(p) || region.BoundaryDistance(p, 6f) < edge || reserved.Blocks(p, 2.0f)) return false;
                foreach (var q in placed) if (math.distancesq(q, p) < spacing * spacing) return false;
                placed.Add(p);
                Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Tree, AssetMix.Pick(trees, (uint)placed.Count), Hashing.Mix(Ctx.Seed, salt + (uint)placed.Count), age);
                return true;
            }
            int bushes = 0;
            void Bush(float2 p)
            {
                if (!region.Contains(p) || region.BoundaryDistance(p, 4f) < 1.2f || reserved.Blocks(p, 1.0f)) return;
                foreach (var q in placed) if (math.distancesq(q, p) < 6.25f) return;
                Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Bush, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + 0x400u + (uint)bushes++));
            }
            if (clearance >= 5.5f)
            {
                // Groves: centres on a jittered 16 m grid, about two in three kept (the gaps are the lawn's), 2-5 trees each.
                float step = 16f;
                for (float y = region.Min.y + step * 0.5f; y < region.Max.y; y += step)
                    for (float x = region.Min.x + step * 0.5f; x < region.Max.x; x += step)
                    {
                        var c = new float2(x, y) + new float2(rng.NextFloat(-4f, 4f), rng.NextFloat(-4f, 4f));
                        if (rng.NextFloat() < 0.33f || !region.Contains(c) || region.BoundaryDistance(c, 6f) < 3.2f) continue;
                        int n = 2 + (int)(rng.NextUInt() % 4u);
                        int got = 0;
                        for (int i = 0; i < n * 3 && got < n; i++)
                        {
                            float ang = rng.NextFloat(0f, 6.283f), rad = got == 0 ? rng.NextFloat(0f, 1.5f) : rng.NextFloat(3.5f, 6f);
                            if (TryTree(c + new float2(math.cos(ang), math.sin(ang)) * rad, 3.0f, 4.2f, 1 + (int)(rng.NextUInt() % 3u))) got++;
                        }
                        if (got == 0) continue;
                        for (int i = 0; i < 2 + got / 2; i++)
                        {
                            float ang = rng.NextFloat(0f, 6.283f);
                            Bush(c + new float2(math.cos(ang), math.sin(ang)) * rng.NextFloat(5.5f, 8f));
                        }
                        if (kit != null && X.Worn) kit.Radial(c, TerrainMaterials.Dirt, 0.32f, 2f, 7f, Falloff.Smooth, 1.5f, 4f);
                        if (kit != null && rain) kit.Radial(c, TerrainMaterials.Lush, 0.6f, 3f, 9f, Falloff.Smooth, 1.5f, 5f);
                    }
            }
            else if (clearance >= 2.4f)
            {
                // A strip: one irregular row along its spine (trees 7-13 m apart, a shrub in some gaps, a few gaps left open).
                var size = region.Max - region.Min;
                bool alongX = size.x >= size.y;
                float len = alongX ? size.x : size.y;
                float u = (alongX ? region.Min.x : region.Min.y) + rng.NextFloat(2f, 6f);
                float end = (alongX ? region.Max.x : region.Max.y) - 2f;
                while (u < end)
                {
                    float lo = alongX ? region.Min.y : region.Min.x, hi = alongX ? region.Max.y : region.Max.x;
                    float2 best = default; float bestD = -1f;
                    for (float v = lo; v <= hi; v += 0.5f)
                    {
                        var p = alongX ? new float2(u, v) : new float2(v, u);
                        if (!region.Contains(p)) continue;
                        float dd = region.BoundaryDistance(p, 8f);
                        if (dd > bestD) { bestD = dd; best = p; }
                    }
                    if (bestD > 0f)
                    {
                        float roll = rng.NextFloat();
                        if (roll < 0.62f) TryTree(best, math.min(2.4f, bestD - 0.1f), 6f, 1 + (int)(rng.NextUInt() % 3u));
                        else if (roll < 0.85f) Bush(best);
                    }
                    u += rng.NextFloat(7f, 13f);
                }
            }
            // A low hedge in front of the lots (frontage pieces: the line t = lotSideT of the frame), clear of walks and drives.
            if (X.Hedges && lotSideT > 0f && math.lengthsq(frame.Dir) > 0.5f)
            {
                float s0 = float.MaxValue, s1 = float.MinValue;
                foreach (var v in piece) { float s = math.dot(v - frame.A, frame.Dir); s0 = math.min(s0, s); s1 = math.max(s1, s); }
                float hedgeT = lotSideT - 1.3f;
                float a = float.NaN;
                for (float s = s0 + 1f; s <= s1 - 1f; s += 1f)
                {
                    var p = frame.At(s, hedgeT);
                    bool ok = region.Contains(p) && region.BoundaryDistance(p, 3f) >= 0.6f && !reserved.Blocks(p, 1.6f);
                    if (ok && float.IsNaN(a)) a = s;
                    if ((!ok || s + 1f > s1 - 1f) && !float.IsNaN(a))
                    {
                        float b = ok ? s : s - 1f;
                        if (b - a >= 4f) GroundCover.HedgeRow(Ctx, frame.At(a, hedgeT), frame.At(b, hedgeT), PlazaArt.LowHedge, AssetCategories.Hedge, ObjectRole.Bush, Hashing.Mix(Ctx.Seed, salt + 0x700u));
                        a = float.NaN;
                    }
                }
            }
            // Flowerbeds where a drive (a lane mouth) enters beside the piece: a pair either side of the mouth's first metres.
            FlowerbedsAtMouths(region, reserved, salt);
            Take(piece);
        }

        /// <summary>Two flowerbeds beside every lane mouth (a lane starting on a street) that runs along this piece.</summary>
        private void FlowerbedsAtMouths(LayerRegion region, ReservedSpace reserved, uint salt)
        {
            if (!AssetAvailability.Has(AssetCategories.Flowerbed)) return;
            int n = 0;
            foreach (var lane in Lanes)
            {
                for (int end = 0; end < 2; end++)
                {
                    bool street = end == 0 ? lane.StartAttach >= 0 : lane.EndAttach >= 0;
                    if (!street || lane.Points.Count < 2) continue;
                    var m = end == 0 ? lane.Points[0] : lane.Points[lane.Points.Count - 1];
                    var q = end == 0 ? lane.Points[1] : lane.Points[lane.Points.Count - 2];
                    var d = math.normalizesafe(q - m); var side = new float2(-d.y, d.x);
                    foreach (float sgn in new[] { -1f, 1f })
                        foreach (float along in new[] { 9f, 13f })
                        {
                            var p = m + d * along + side * sgn * (lane.Half + 2.6f);
                            if (!region.Contains(p) || region.BoundaryDistance(p, 3f) < 1.3f || reserved.Blocks(p, 1.3f)) continue;
                            Ctx.Object(p, math.atan2(d.x, d.y), ObjectRole.Planter, AssetMix.Pick(PlazaArt.Flowerbeds, (uint)n), Hashing.Mix(Ctx.Seed, salt + 0x600u + (uint)n), -1, AssetCategories.Flowerbed);
                            reserved.AddDisc(p, 1.4f);
                            n++;
                        }
                }
            }
            // (rf-shops3) The player's drives too: a pair either side where the drive crosses the planted edge.
            foreach (var (outer, inner) in Drives)
            {
                var d = math.normalizesafe(inner - outer); var side = new float2(-d.y, d.x);
                foreach (float sgn in new[] { -1f, 1f })
                    foreach (float along in new[] { 3.5f, 7.5f })
                    {
                        var p = outer + d * along + side * sgn * 6.2f;
                        if (!region.Contains(p) || region.BoundaryDistance(p, 3f) < 1.3f || reserved.Blocks(p, 1.3f)) continue;
                        Ctx.Object(p, math.atan2(d.x, d.y), ObjectRole.Planter, AssetMix.Pick(PlazaArt.Flowerbeds, (uint)n), Hashing.Mix(Ctx.Seed, salt + 0x640u + (uint)n), -1, AssetCategories.Flowerbed);
                        reserved.AddDisc(p, 1.4f);
                        n++;
                    }
            }
        }

        /// <summary>
        /// (rf-shops3) The player's footpath entrances, dressed where they meet the street: a bollard either side of the walk, a
        /// signpost, a short row of bike racks beside it and a bin, wherever the ground there stayed planted (never on a lot or a
        /// plot, never on a net).
        /// </summary>
        public void DressGates()
        {
            if (Gates == null) return;
            var reserved = Reserved();
            bool Open(float2 p, float r)
            {
                if (!Region.Contains(p) || reserved.Blocks(p, r)) return false;
                foreach (var l in Lots) if (Poly.Contains(l, p)) return false;
                foreach (var sh in Shops) if (Poly.Contains(sh, p) || Poly.DistanceToBoundary(sh, p) < r) return false;
                return true;
            }
            uint k = 0;
            foreach (var g in Gates)
            {
                if (g.Kind != ShopEntrances.Footpath || g.Landed != "walk") continue;
                var side = new float2(-g.In.y, g.In.x);
                foreach (float sgn in new[] { -1f, 1f })
                {
                    var b = g.At + g.In * 1.2f + side * sgn * 2.2f;
                    if (Open(b, 0.3f)) { Ctx.Object(b, 0f, ObjectRole.Furniture, new AssetRef("", "Bollard01", "Bollard02"), Hashing.Mix(Ctx.Seed, 0x6A0u + k++), -1, AssetCategories.Bollard); reserved.AddDisc(b, 0.3f); }
                }
                var sp = g.At + g.In * 3.5f + side * 2.6f;
                if (AssetAvailability.Has(AssetCategories.Wayfinding) && Open(sp, 0.5f)) { Ctx.Object(sp, math.atan2(-g.In.x, -g.In.y), ObjectRole.Furniture, new AssetRef(""), Hashing.Mix(Ctx.Seed, 0x6B0u + k++), -1, AssetCategories.Wayfinding); reserved.AddDisc(sp, 0.5f); }
                if (X.Racks)
                {
                    var rack = g.At + g.In * 4f - side * 3.2f;
                    if (Open(rack, 0.6f) && Open(rack + g.In * 4.8f, 0.6f)) BikeRacks(rack, g.In, 4, 0x6C0u + k++);
                }
                Decal(g.At + g.In * 2.5f, "decal.wear", 0x6E0u + k);
                var bin = g.At + g.In * 6.5f + side * 2.4f;
                if (Open(bin, 0.4f)) Ctx.Object(bin, 0f, ObjectRole.Bin, new AssetRef(""), Hashing.Mix(Ctx.Seed, 0x6D0u + k++));
            }
        }

        private bool TouchesLot(float2[] piece)
        {
            foreach (var lot in Lots)
                foreach (var v in piece)
                    if (Poly.DistanceToBoundary(lot, v) < 1.5f) return true;
            return false;
        }

        // ───────────── furniture ─────────────

        /// <summary>A row of bike racks from <paramref name="at"/> along <paramref name="along"/> (racks 1.6 m apart, across the row).</summary>
        public int BikeRacks(float2 at, float2 along, int count, uint salt)
        {
            if (count <= 0 || !AssetAvailability.Has(AssetCategories.BikeRack)) return 0;
            along = math.normalizesafe(along);
            float yaw = math.atan2(along.x, along.y) + math.PI * 0.5f;
            int n = 0;
            var reserved = Reserved();
            for (int i = 0; i < count; i++)
            {
                var p = at + along * (1.6f * i);
                if (reserved.Blocks(p, 0.5f)) continue;
                Ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(new[] { "BikeRack01", "BikeRack02", "BikeRack03" }, salt), Hashing.Mix(Ctx.Seed, salt + (uint)i), -1, AssetCategories.BikeRack);
                n++;
            }
            BikeRackCount += n;
            return n;
        }

        /// <summary>Bike racks placed so far (plan fact "bikeRacks").</summary>
        public int BikeRackCount;

        /// <summary>Food carts parked in a line from <paramref name="at"/> along <paramref name="along"/>, facing <paramref name="facing"/>.</summary>
        public int FoodCarts(float2 at, float2 along, int count, float room, float2 facing, uint salt)
        {
            if (count <= 0) return 0;
            string cat = AssetAvailability.Has(AssetCategories.EventFoodTruck) ? AssetCategories.EventFoodTruck : AssetAvailability.Has(AssetCategories.Kiosk) ? AssetCategories.Kiosk : null;
            if (cat == null) return 0;
            along = math.normalizesafe(along);
            int fit = math.min(count, math.max(1, (int)(room / 5.5f) + 1));
            float yaw = math.atan2(facing.x, facing.y);
            int n = 0;
            var reserved = Reserved();
            for (int i = 0; i < fit; i++)
            {
                var p = at + along * (5.5f * i - 5.5f * (fit - 1) * 0.5f);
                if (reserved.Blocks(p, 1.6f)) continue;
                Ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(PlazaArt.Carts, salt + (uint)i), Hashing.Mix(Ctx.Seed, salt + (uint)i), -1, cat);
                n++;
            }
            FoodCartCount += n;
            return n;
        }

        /// <summary>Food carts placed so far (plan fact "foodCarts").</summary>
        public int FoodCartCount;

        // ───────────── ground paint ─────────────

        private PaintKit _paint;
        private PaintKit PaintKit()
        {
            if (_paint != null) return _paint;
            _paint = new PaintKit(Ctx.Plan.Paint, Ctx.H, Hashing.Mix(Ctx.Seed, 0x5A17u)) { Outline = Region.Ring, MaxPaints = 600 };
            TerrainPaint.Warn(Ctx);
            return _paint;
        }

        /// <summary>
        /// (rf-shops3) Building sites: dirt painted on every shop plot (inset, soft edge), so a plot that has not grown yet reads as
        /// land being built on; the game's building covers it when it grows. Off without the "Building sites" extra.
        /// </summary>
        public int PaintSites()
        {
            if ((!X.Sites && !X.PlotTrees) || Shops.Count == 0) return 0;
            var kit = PaintKit();
            int n = 0;
            foreach (var s in Shops)
            {
                if (!PlotFrame(s, out var f, out float w, out float dep)) continue;
                // The front of the plot (where the building stands) is a prepared site; with plot trees the back is a lawn.
                float siteDepth = X.PlotTrees && dep >= 3f * Cell - 0.5f ? dep - 1.5f * Cell : dep;
                if (X.Sites)
                {
                    var r = Rect(f, 0.9f, w - 0.9f, 0.6f, siteDepth - 0.6f);
                    if (Poly.Area(r) >= 30f && kit.Fill(r, TerrainMaterials.Dirt, 0.8f, 1.6f, 0.7f, 5f) != null) n++;
                }
                if (X.PlotTrees && siteDepth < dep - 1f)
                    kit.Fill(Rect(f, 0.6f, w - 0.6f, siteDepth - 1f, dep - 0.5f), TerrainMaterials.Lush, 0.55f, 2f, 0.8f, 6f);
            }
            Ctx.Plan.Facts["buildingSites"] = n;
            return n;
        }

        /// <summary>
        /// (rf-shops3) Service yards: a dumpster behind every other deep plot (behind the shop, never in front of it), where the
        /// ground behind stayed planted: shops have a back side, and it shows.
        /// </summary>
        public int ServiceYards()
        {
            if ((!X.Designed && !BackOfHouse) || Shops.Count == 0) return 0;
            var reserved = Reserved();
            int n = 0, k = 0;
            foreach (var s in Shops)
            {
                if (!PlotFrame(s, out var f, out float w, out float dep) || dep < 3f * Cell - 0.5f || w < 14f) continue;
                if ((k++ & 1) == 1) continue;
                var p = f.At(w * 0.7f, dep + 2.6f);
                bool ok = Region.Contains(p) && Region.BoundaryDistance(p, 4f) > 1.8f && !reserved.Blocks(p, 1.4f);
                if (ok) foreach (var l in Lots) if (Poly.Contains(l, p) || Poly.DistanceToBoundary(l, p) < 1.5f) { ok = false; break; }
                if (ok) foreach (var o in Shops) if (Poly.Contains(o, p) || Poly.DistanceToBoundary(o, p) < 1.2f) { ok = false; break; }
                if (!ok) continue;
                Ctx.Object(p, math.atan2(f.Dir.x, f.Dir.y), ObjectRole.Furniture, new AssetRef("", "TrashContainerEmpty01", "TrashContainerEmpty02", "TrashContainerEmpty03"), Hashing.Mix(Ctx.Seed, 0x5E0u + (uint)n), -1, "industrial.skip");
                reserved.AddDisc(p, 1.6f);
                Decal(p + f.Dir * 2.2f, AssetCategories.DecalDirt, 0x5F0u + (uint)n, 0.8f);
                n++;
            }
            if (n > 0) Ctx.Plan.Facts["serviceYards"] = n;
            return n;
        }

        /// <summary>Plot planting: plot trees and shrubs as their own categories, kept on the zoning cutouts (Cutouts.KeepsOnPlot).</summary>
        public const string PlotTree = "tree.plot", PlotBush = "bush.plot";

        /// <summary>
        /// (rf-shops3, the user via the lead) Trees and shrubs ON the plots: a plot drawn 3 x 3 may grow a 2 x 2 building; the
        /// growing building is placed over what is in its way (the game hides overridable trees under it) and the rest stays
        /// green, which reads better than bare ground. Trees along the back of plots three cells deep or more, a shrub in each
        /// back corner. Never surfaces on plots (buildings cannot grow on them). Returns the trees planted.
        /// </summary>
        public int PlantPlots()
        {
            if (!X.PlotTrees || Shops.Count == 0) return 0;
            int trees = 0, bushes = 0;
            foreach (var s in Shops)
            {
                if (!PlotFrame(s, out var f, out float w, out float dep)) continue;
                var cen = Poly.Centroid(s);
                uint v = Hashing.Mix(Ctx.Seed, (uint)(int)(cen.x * 13f) ^ (uint)(int)(cen.y * 7f));
                if (dep >= 3f * Cell - 0.5f)
                {
                    int n = math.max(1, (int)math.floor((w - 3f) / 7.5f));
                    for (int i = 0; i < n; i++)
                    {
                        float u = w * (i + 0.5f) / n + (Hashing.Unit(v + (uint)i) - 0.5f) * 1.6f;
                        var p = f.At(math.clamp(u, 2.2f, w - 2.2f), dep - 2.6f - Hashing.Unit(v + 31u + (uint)i) * 1.2f);
                        Ctx.Object(p, Hashing.Unit(v + 7u + (uint)i) * 6.283f, ObjectRole.Tree, AssetMix.Pick(ParkArt.ParkTrees, v + (uint)i), Hashing.Mix(v, (uint)i), 1 + (int)((v >> (i + 3)) % 3u), PlotTree);
                        trees++;
                    }
                }
                if (w >= 6f)
                    foreach (float u in new[] { 1.3f, w - 1.3f })
                    {
                        Ctx.Object(f.At(u, dep - 1.3f), Hashing.Unit(v + 77u) * 6.283f, ObjectRole.Bush, new AssetRef(""), Hashing.Mix(v, 0x88u + (uint)(u > 2f ? 1 : 0)), -1, PlotBush);
                        bushes++;
                    }
            }
            Ctx.Plan.Facts["plotTrees"] = trees;
            Ctx.Plan.Facts["plotBushes"] = bushes;
            return trees;
        }

        /// <summary>
        /// A plot's frame: s along its front (the edge nearest a lane or a street, where the building stands), t into the plot;
        /// its width and depth. False when the plot is not a four-sided ring.
        /// </summary>
        public bool PlotFrame(float2[] plot, out StreetShops.Run frame, out float width, out float depth)
        {
            frame = default; width = depth = 0f;
            if (plot == null || plot.Length != 4) return false;
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            var ring = Poly.EnsureCcw((float2[])plot.Clone());
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var m = math.lerp(ring[i], ring[(i + 1) % 4], 0.5f);
                float dm = float.MaxValue;
                foreach (var l in Lanes)
                    for (int j = 0; j + 1 < l.Points.Count; j++) dm = math.min(dm, math.sqrt(SegmentIndex.DistSq(m, l.Points[j], l.Points[j + 1])) - l.Half);
                foreach (var rd in roads) dm = math.min(dm, math.sqrt(SegmentIndex.DistSq(m, rd.A, rd.B)) - rd.HalfWidth);
                if (dm < bestD) { bestD = dm; best = i; }
            }
            if (best < 0) return false;
            frame = Frame(ring[best], ring[(best + 1) % 4]);   // In = left of a CCW edge = into the plot
            width = frame.To;
            depth = math.abs(math.dot(ring[(best + 2) % 4] - ring[best], frame.In));
            return width > 1f && depth > 1f;
        }

        /// <summary>(rf-shops3) A subtle ground decal (worn patch, leaves, a stain): only with the Worn ground extra and where the catalog has one (asset packs).</summary>
        private void Decal(float2 p, string category, uint salt, float scale = 1f)
        {
            if (!X.Worn || !AssetAvailability.Has(category) || AssetAvailability.CountOf(category) == 0) return;
            var o = Ctx.Object(p, Hashing.Unit(salt) * 6.283f, ObjectRole.Decal, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt), -1, category);
            o.Scale = scale;
        }

        // ───────────── helpers ─────────────

        private ReservedSpace Reserved()
        {
            var r = new ReservedSpace();
            Composer.Reserve(Ctx.Plan, r);
            return r;
        }

        /// <summary>The point <paramref name="at"/> metres along a polyline and its direction there.</summary>
        public static (float2 p, float2 dir) Along(List<float2> line, float at)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float l = math.distance(line[i], line[i + 1]);
                if (at <= l || i + 2 == line.Count)
                {
                    var d = l > 1e-4f ? (line[i + 1] - line[i]) / l : new float2(1f, 0f);
                    return (line[i] + d * math.clamp(at, 0f, l), d);
                }
                at -= l;
            }
            return (line[0], new float2(1f, 0f));
        }

        /// <summary>A polygon grown by <paramref name="by"/> (largest outer ring of the offset), or the polygon itself.</summary>
        public static float2[] Grow(float2[] ring, float by)
        {
            try
            {
                float2[] best = null; float area = 0f;
                foreach (var r in Poly.Offset(ring, -by, OffsetJoin.Round))
                    if (r?.Outer != null && r.Outer.Length >= 3 && Poly.Area(r.Outer) > area) { area = Poly.Area(r.Outer); best = r.Outer; }
                return best != null ? Poly.EnsureCcw(best) : ring;
            }
            catch (Exception) { return ring; }
        }
    }
}

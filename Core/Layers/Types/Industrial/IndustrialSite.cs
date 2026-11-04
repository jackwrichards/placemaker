// Industrial category (2026-10-19, the user: "it shouldn't just look like a random scattered blotchy patch of garbage ... it
// should be like someone handcrafted it"). The layout every industrial yard shares, drawn the way a yard is designed:
//
//   * one frame for everything, square to the street: U runs along the street at the main gate, V goes in. Rows, stacks,
//     bays and aisles all follow it, so nothing sits at a stray angle;
//   * a tidy edge band inside the fence (trees, a wall of concrete blocks, a wall of containers, or plain gravel);
//   * a front yard at the gate: the way in (boom barrier, gate booth, weighbridge), the office row on one side and the staff
//     parking on the other;
//   * a spine from the gate to the back, a header aisle across the yard behind the front yard, cross aisles between the
//     storage bands and, on wide yards, column aisles so no block is longer than a forklift wants to drive;
//   * the storage blocks: rectangles between the aisles that lie wholly inside the yard. The type fills each one (rows,
//     stacks, bays, heaps, a plant), so a block is always one thing, laid out to the centimetre.
//
// What does not fit a rectangle (corners of odd outlines) stays plain yard ground: tidy, never scattered with junk.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    /// <summary>A storage block: a rectangle in the site frame between aisles.</summary>
    public sealed class YardBlock
    {
        public float U0, U1, V0, V1;
        /// <summary>An aisle runs along its street-side edge (V0) / its back edge (V1).</summary>
        public bool AisleFront, AisleBack;
        /// <summary>Storage band (0 = nearest the street) and side of the spine (-1 left, +1 right).</summary>
        public int Band, Side;
        /// <summary>What the type put here ("stacks", "cars", "bays", "plant"…); "" = left as yard ground.</summary>
        public string Use = "";
        public int Index;
        public float Length => U1 - U0;
        public float Depth => V1 - V0;
        public float UMid => (U0 + U1) * 0.5f;
        public float VMid => (V0 + V1) * 0.5f;
        /// <summary>Distance of the block's near end from the spine.</summary>
        public float Near => math.min(math.abs(U0), math.abs(U1));
    }

    /// <summary>Layout numbers a type gives the site (metres).</summary>
    public struct SiteSpec
    {
        public float Aisle;        // drive aisle width
        public float Edge;         // tidy band inside the fence
        public float Front;        // depth of the front yard (gate, office, parking)
        public float BandDepth;    // depth of a storage band between two aisles
        public float LastBand;     // depth of the band against the back fence (0 = same as BandDepth)
        public float MaxBlock;     // longest block before a column aisle cuts it
        public float MinBlock;     // shortest block worth filling
        public float BlockGap;     // clearance between a block and the aisles round it
        public float FrontZone;    // half width (from the spine) of the front yard's office and parking zone
    }

    public sealed class IndustrialSite
    {
        public LayerContext Ctx;
        public LayerRegion Region, Inner;
        public Occupancy Occ;
        /// <summary>Only what other layers reserved (paths, a feature's disc): walls and decals must respect it too.</summary>
        public Occupancy ReservedOnly;
        public List<Gate> Gates;
        public SiteSpec Spec;
        /// <summary>Frame: origin at the main gate on the outline, U along the street, V into the yard.</summary>
        public float2 O, U, V;
        public bool HasGate;
        /// <summary>Extent of the inner yard (inside the edge band) in the frame.</summary>
        public float UMin, UMax, VMin, VMax;
        public float SpineEnd;
        /// <summary>Side of the spine the office row stands on (+1 / -1); the staff parking takes the other.</summary>
        public int OfficeSide = 1;
        public readonly List<YardBlock> Blocks = new List<YardBlock>();
        /// <summary>Aisle centre lines (world), the spine first.</summary>
        public readonly List<(float2 a, float2 b)> Aisles = new List<(float2, float2)>();
        /// <summary>Points where aisles meet the edge band (floodlight masts).</summary>
        public readonly List<float2> AisleEnds = new List<float2>();
        /// <summary>Things a type wants grime / machines next to (heaps, crusher, stacks).</summary>
        public readonly List<(float2 p, float r)> Anchors = new List<(float2, float)>();
        /// <summary>Organic yards: the tracks as smooth polylines (their segments are also the Aisles).</summary>
        public readonly List<List<float2>> Tracks = new List<List<float2>>();
        public bool Organic;
        /// <summary>Terrain mounds the types raise (the scrap mountain stands on one).</summary>
        public readonly List<Mound> Mounds = new List<Mound>();
        public int Items, Containers, Wrecks, Heaps, Bays, Machines, Lights, Offices, Parked, Banks, Stones, Strewn;
        /// <summary>The organic yard's work pad (OrganicYard.WorkPad): its centre and radius; 0 = none.</summary>
        public float2 PadAt;
        public float PadR;

        public float2 W(float u, float v) => O + U * u + V * v;
        public float2 F(float2 p) { var d = p - O; return new float2(math.dot(d, U), math.dot(d, V)); }
        public OrientedRect Rect(float u0, float u1, float v0, float v1) => new OrientedRect
        {
            Center = W((u0 + u1) * 0.5f, (v0 + v1) * 0.5f), Axis = U, HalfLength = (u1 - u0) * 0.5f, HalfWidth = (v1 - v0) * 0.5f,
        };
        public OrientedRect Rect(YardBlock b) => Rect(b.U0, b.U1, b.V0, b.V1);
        public uint Salt(uint k) => Hashing.Mix(Ctx.Seed, k);

        // ───────────── frame and layout ─────────────

        public static IndustrialSite Plan(LayerContext ctx, List<Gate> gates, SiteSpec spec)
        {
            var s = new IndustrialSite
            {
                Ctx = ctx, Region = ctx.LayerRegion, Gates = gates, Spec = spec,
                Occ = new Occupancy(ctx.LayerRegion, ctx.Input.Reserved, ctx.Site),
                ReservedOnly = new Occupancy(ctx.LayerRegion, ctx.Input.Reserved, ctx.Site),
            };
            s.MakeFrame();
            s.MakeInner();
            s.Layout();
            return s;
        }

        /// <summary>The organic layout (OrganicYard): the same frame, edge and front yard, tracks instead of aisles, no blocks.</summary>
        public static IndustrialSite PlanOrganic(LayerContext ctx, List<Gate> gates, SiteSpec spec, uint salt)
        {
            var s = new IndustrialSite
            {
                Ctx = ctx, Region = ctx.LayerRegion, Gates = gates, Spec = spec, Organic = true,
                Occ = new Occupancy(ctx.LayerRegion, ctx.Input.Reserved, ctx.Site),
                ReservedOnly = new Occupancy(ctx.LayerRegion, ctx.Input.Reserved, ctx.Site),
            };
            s.MakeFrame();
            s.MakeInner();
            float depth = s.VMax - math.max(0f, s.VMin);
            s.Spec.Front = math.clamp(math.min(spec.Front, depth * 0.34f), math.min(12f, depth * 0.3f), spec.Front);
            var mid = Around(s.Chord(s.Spec.Front * 0.5f), 0f);
            if (mid != null) s.OfficeSide = mid.Value.y >= -mid.Value.x ? 1 : -1;
            OrganicYard.Tracks(s, salt);
            return s;
        }

        /// <summary>
        /// U/V from the outline's dominant edges (YardKit.GridFrame), turned so V is the one closest to the main gate's inward
        /// normal: a slanted bit of street edge never tilts the whole yard.
        /// </summary>
        private void MakeFrame()
        {
            YardKit.GridFrame(Region, out var centre, out var axis, out var side, out _, out _);
            HasGate = Gates != null && Gates.Count > 0;
            var inward = HasGate ? GateApproach.Inward(Region, Gates[0].Position) : side;
            // The frame direction that points into the yard from the gate with the most yard ahead of it and the least
            // behind it (a gate on a slanted bit of a round outline must not leave half the yard behind the gate line).
            float2 best = axis; float bestScore = float.MinValue;
            foreach (var c in new[] { axis, -axis, side, -side })
            {
                float d = math.dot(c, inward);
                if (d < 0.3f && HasGate) continue;
                float ahead = 0f, behind = 0f;
                var from = HasGate ? Gates[0].Position : centre;
                foreach (var q in Region.Ring) { float t = math.dot(q - from, c); ahead = math.max(ahead, t); behind = math.max(behind, -t); }
                float score = HasGate ? ahead - 2f * behind + 20f * d : d;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            V = math.normalizesafe(best, new float2(0, 1));
            U = new float2(V.y, -V.x);   // OrientedRect.Side of U is then V
            if (HasGate) O = Gates[0].Position;
            else
            {
                // No gate: the origin is the middle of the outline's lowest edge in V.
                float vmin = float.MaxValue;
                foreach (var p in Region.Ring) vmin = math.min(vmin, math.dot(p - centre, V));
                O = centre + V * vmin;
            }
        }

        private void MakeInner()
        {
            Inner = null;
            var inset = Region.InsetVertices(Spec.Edge, 2.5f);
            if (inset != null && inset.Length >= 3 && Poly.IsSimple(inset) && Poly.SignedArea(inset) > Region.Area * 0.3f)
            {
                var r = LayerRegion.From(inset);
                if (r != null) Inner = r;
            }
            if (Inner == null) { Inner = Region; Spec.Edge = 0.5f; }
            UMin = VMin = float.MaxValue; UMax = VMax = float.MinValue;
            foreach (var p in Inner.Ring)
            {
                var f = F(p);
                UMin = math.min(UMin, f.x); UMax = math.max(UMax, f.x);
                VMin = math.min(VMin, f.y); VMax = math.max(VMax, f.y);
            }
        }

        /// <summary>Intervals (u0, u1) where the line v = const runs inside the inner yard.</summary>
        public List<float2> Chord(float v)
        {
            var list = new List<float2>();
            var a = W(UMin - 20f, v); var b = W(UMax + 20f, v);
            foreach (var (p, q) in Poly.ClipSegmentToPolygon(Inner.Ring, a, b))
            {
                float u0 = math.dot(p - O, U), u1 = math.dot(q - O, U);
                if (u1 < u0) { var t = u0; u0 = u1; u1 = t; }
                if (u1 - u0 > 0.5f) list.Add(new float2(u0, u1));
            }
            list.Sort((x, y) => x.x.CompareTo(y.x));
            return list;
        }

        private static List<float2> Intersect(List<float2> a, List<float2> b)
        {
            var r = new List<float2>();
            foreach (var x in a)
                foreach (var y in b)
                {
                    float lo = math.max(x.x, y.x), hi = math.min(x.y, y.y);
                    if (hi - lo > 0.5f) r.Add(new float2(lo, hi));
                }
            r.Sort((p, q) => p.x.CompareTo(q.x));
            return r;
        }

        /// <summary>Where a strip v0..v1 lies wholly inside the inner yard (sampled every 2 m across it).</summary>
        public List<float2> StripInside(float v0, float v1)
        {
            var r = Chord(v0 + 0.05f);
            int n = math.max(2, (int)math.ceil((v1 - v0) / 2f));
            for (int i = 1; i <= n && r.Count > 0; i++) r = Intersect(r, Chord(math.min(v1 - 0.05f, v0 + (v1 - v0) * i / n)));
            return r;
        }

        /// <summary>The interval of a chord list that contains u (or the nearest one), null when empty.</summary>
        private static float2? Around(List<float2> list, float u)
        {
            float2? best = null; float bd = float.MaxValue;
            foreach (var x in list)
            {
                float d = u < x.x ? x.x - u : u > x.y ? u - x.y : 0f;
                if (d < bd) { bd = d; best = x; }
            }
            return best;
        }

        private void Layout()
        {
            float a = Spec.Aisle, gap = Spec.BlockGap;
            // The front yard shrinks on shallow sites (never below a gate apron), the bands keep their depth.
            float depth = VMax - math.max(0f, VMin);
            float front = math.clamp(math.min(Spec.Front, depth * 0.34f), math.min(12f, depth * 0.3f), Spec.Front);
            Spec.Front = front;
            float header = front + a * 0.5f;

            // Bands behind the header aisle: band, aisle, band, aisle… the last band leans on the back fence.
            var depths = new List<float>();
            float v = front + a;
            float lastDepth = Spec.LastBand > 0f ? Spec.LastBand : Spec.BandDepth;
            while (true)
            {
                float room = VMax - v;
                if (room >= Spec.BandDepth + a + math.min(Spec.BandDepth, lastDepth) * 0.6f)
                {
                    depths.Add(Spec.BandDepth);
                    v += Spec.BandDepth + a;
                    continue;
                }
                // The last band takes what is left up to its own depth (or a band's, if more is left).
                float d = math.min(room, math.max(lastDepth, math.min(room, Spec.BandDepth)));
                if (d >= math.min(lastDepth, Spec.BandDepth) * 0.6f && d >= 5f) { depths.Add(d); v += d; }
                else if (depths.Count > 0) v -= a;
                break;
            }
            // What is left behind the last band is shared out over the bands (each a little deeper), so the last band meets
            // the back fence instead of leaving a strip of nothing.
            if (depths.Count > 0)
            {
                float extra = math.clamp((VMax - 0.3f - v) / depths.Count, 0f, Spec.BandDepth * 0.5f);
                for (int i = 0; i < depths.Count; i++) depths[i] += extra;
            }
            var bands = new List<float2>();
            v = front + a;
            foreach (var d in depths) { bands.Add(new float2(v, v + d)); v += d + a; }

            // Column aisles cut long blocks into equal pieces on each side of the spine, staggered band by band like brickwork:
            // an odd band's cuts fall half a block along, so the lanes meet in T-junctions, not a grid of crossroads (the user,
            // 2026-10-19: "things are just too well laid out").
            List<float> ColumnsFor(int band)
            {
                var cols = new List<float>();
                foreach (int side in new[] { -1, 1 })
                {
                    float span = (side > 0 ? UMax : -UMin) - a * 0.5f;
                    if (span <= Spec.MaxBlock) continue;
                    int n = (int)math.ceil((span + a) / (Spec.MaxBlock + a));
                    float len = (span - (n - 1) * a) / n;
                    float shift = (band & 1) == 1 ? (len + a) * 0.5f : 0f;
                    for (int k = 1; k < n + ((band & 1) == 1 ? 1 : 0); k++)
                    {
                        float c = a * 0.5f + k * len + (k - 0.5f) * a - shift;
                        if (c > a + 4f && c < span - 4f) cols.Add(side * c);
                    }
                }
                cols.Sort();
                return cols;
            }

            // Blocks: inside the band, clear of the spine and the columns.
            int index = 0;
            for (int bi = 0; bi < bands.Count; bi++)
            {
                float v0 = bands[bi].x + gap, v1 = bands[bi].y - (bi < bands.Count - 1 ? gap : 0.3f);
                bool last = bi == bands.Count - 1;
                foreach (var iv in StripInside(v0, v1))
                {
                    // Cut by the spine and the columns.
                    var cuts = new List<float2> { new float2(-a * 0.5f - gap, a * 0.5f + gap) };
                    foreach (var c in ColumnsFor(bi)) cuts.Add(new float2(c - a * 0.5f - gap, c + a * 0.5f + gap));
                    var pieces = new List<float2> { iv };
                    foreach (var cut in cuts)
                    {
                        var next = new List<float2>();
                        foreach (var p in pieces)
                        {
                            if (cut.y <= p.x || cut.x >= p.y) { next.Add(p); continue; }
                            if (cut.x - p.x > 0.5f) next.Add(new float2(p.x, cut.x));
                            if (p.y - cut.y > 0.5f) next.Add(new float2(cut.y, p.y));
                        }
                        pieces = next;
                    }
                    foreach (var p in pieces)
                    {
                        if (p.y - p.x < Spec.MinBlock) continue;
                        Blocks.Add(new YardBlock
                        {
                            U0 = p.x, U1 = p.y, V0 = v0, V1 = v1, Band = bi, Side = p.x + p.y < 0f ? -1 : 1,
                            AisleFront = true, AisleBack = !last, Index = index++,
                        });
                    }
                }
            }

            // Bands past the last one that holds a block are dropped (no aisle may lead to nothing); the new last band leans
            // on the back fence.
            int lastUsed = -1;
            foreach (var b in Blocks) lastUsed = math.max(lastUsed, b.Band);
            if (lastUsed < bands.Count - 1) bands.RemoveRange(lastUsed + 1, bands.Count - lastUsed - 1);
            foreach (var b in Blocks) if (b.Band == lastUsed) b.AisleBack = false;

            // The front corners beside the office and the parking: a band reached from the header aisle.
            float v0f = math.max(VMin, 0f) + 0.4f, v1f = front - gap;
            if (v1f - v0f >= 6f)
                foreach (var iv in StripInside(v0f, v1f))
                {
                    var pieces = new List<float2>();
                    if (iv.x < -Spec.FrontZone) pieces.Add(new float2(iv.x, math.min(iv.y, -Spec.FrontZone)));
                    if (iv.y > Spec.FrontZone) pieces.Add(new float2(math.max(iv.x, Spec.FrontZone), iv.y));
                    foreach (var p in pieces)
                    {
                        if (p.y - p.x < Spec.MinBlock) continue;
                        Blocks.Add(new YardBlock
                        {
                            U0 = p.x, U1 = p.y, V0 = v0f, V1 = v1f, Band = -1, Side = p.x + p.y < 0f ? -1 : 1,
                            AisleFront = false, AisleBack = true, Index = index++,
                        });
                    }
                }

            // Aisles: the header and the cross aisles run from the spine to the farthest block they serve, on the stretch of
            // the inner yard that holds the spine (never across a notch of the outline).
            float spineEnd = header;
            var crossVs = new List<float> { header };
            for (int bi = 0; bi < bands.Count - 1; bi++) crossVs.Add(bands[bi].y + a * 0.5f);
            foreach (var cv in crossVs)
            {
                float lo = 0f, hi = 0f; bool any = false;
                foreach (var b in Blocks)
                    if (math.abs(b.V0 - gap - a * 0.5f - cv) < 0.6f || math.abs(b.V1 + gap + a * 0.5f - cv) < 0.6f)
                    { lo = math.min(lo, b.U0); hi = math.max(hi, b.U1); any = true; }
                if (!any && cv != header) continue;
                var room = StripInside(cv - a * 0.5f, cv + a * 0.5f);
                var span = Around(room, 0f);
                if (span == null) continue;
                lo = math.max(span.Value.x + 0.5f, any ? lo : -a);
                hi = math.min(span.Value.y - 0.5f, any ? hi : a);
                if (hi - lo < a) continue;
                Aisles.Add((W(lo, cv), W(hi, cv)));
                spineEnd = math.max(spineEnd, cv);
                // Mark the blocks the aisle does not reach as reached from the other side only.
                foreach (var b in Blocks)
                {
                    if (math.abs(b.V0 - gap - a * 0.5f - cv) < 0.6f && (b.U1 < lo - 0.5f || b.U0 > hi + 0.5f)) b.AisleFront = false;
                    if (math.abs(b.V1 + gap + a * 0.5f - cv) < 0.6f && (b.U1 < lo - 0.5f || b.U0 > hi + 0.5f)) b.AisleBack = false;
                }
                // Where the aisle meets the edge band: masts.
                if (span.Value.x > UMin - 1f && lo <= span.Value.x + 1f) AisleEnds.Add(W(lo - 1.2f, cv));
                if (span.Value.y < UMax + 1f && hi >= span.Value.y - 1f) AisleEnds.Add(W(hi + 1.2f, cv));
            }
            // A block with no aisle reached becomes plain ground (nothing would get to it).
            Blocks.RemoveAll(b => !b.AisleFront && !b.AisleBack);
            SpineEnd = spineEnd;

            // Spine from the gate to the last cross aisle; columns from the header to the last band they cut.
            Aisles.Insert(0, (W(0f, -1.5f), W(0f, spineEnd)));
            // Each band's column lanes run across that band only, from the lane in front of it to the one behind (or to the
            // back of the last band), and only where a block stands beside them.
            for (int bi = 0; bi < bands.Count; bi++)
            {
                float vf = bands[bi].x - a * 0.5f, vb = bi < bands.Count - 1 ? bands[bi].y + a * 0.5f : bands[bi].y;
                foreach (var c in ColumnsFor(bi))
                {
                    bool beside = false;
                    foreach (var b in Blocks)
                        if (b.Band == bi && (math.abs(b.U0 - gap - a * 0.5f - c) < 0.6f || math.abs(b.U1 + gap + a * 0.5f - c) < 0.6f)) { beside = true; break; }
                    if (!beside) continue;
                    float2? span = null;
                    for (float t = vb; t > vf + 1f; t -= 2f)
                        if (StripInsideAlong(c, vf, t)) { span = new float2(vf, t); break; }
                    if (span == null) continue;
                    Aisles.Add((W(c, span.Value.x), W(c, span.Value.y)));
                }
            }

            // Office side: the side with more room in the front yard.
            var mid = Around(Chord(front * 0.5f), 0f);
            if (mid != null) OfficeSide = mid.Value.y >= -mid.Value.x ? 1 : -1;

            foreach (var (p, q) in Aisles) Occ.TakeCorridor(p, q, a * 0.5f, YardKit.KindHaul);
        }

        private bool StripInsideAlong(float u, float v0, float v1)
        {
            float hw = Spec.Aisle * 0.5f;
            for (float v = v0; v <= v1; v += 2f)
                foreach (var du in new[] { -hw, hw })
                    if (!Inner.Contains(W(u + du, v))) return false;
            return Inner.Contains(W(u, v1));
        }

        // ───────────── placing ─────────────

        /// <summary>
        /// One prop with its footprint centred on c, its local +Z along <paramref name="dirZ"/>. Checked against the outline and
        /// everything placed (aisles included) unless <paramref name="check"/> is false; the footprint is taken.
        /// </summary>
        public bool Put(in YardItem item, AssetRef asset, float2 c, float2 dirZ, uint v, float gap = 0.05f, bool check = true,
            ObjectRole role = ObjectRole.Furniture, float scale = 1f)
        {
            var dir = math.normalizesafe(dirZ, V);
            var r = new OrientedRect { Center = c, Axis = dir, HalfLength = item.Z * 0.5f * scale, HalfWidth = item.X * 0.5f * scale };
            if (check && !Fits(r, gap)) return false;
            if (!check && !ReservedOnly.Free(c, math.min(item.X, item.Z) * 0.5f * scale, 0f, 0f)) return false;
            // The footprint is centred on c; the pivot sits CZ behind it.
            var o = Ctx.Object(c - dir * item.CZ * scale, math.atan2(dir.x, dir.y), role, asset, v, -1, item.Category);
            if (scale != 1f) o.Scale = scale;
            if (role != ObjectRole.Decal) Claim(r);
            Items++;
            return true;
        }

        /// <summary>
        /// The rectangle lies inside the outline and is free: probed with discs on a grid inset by the probe radius, so a prop
        /// may stand right next to another (LeisureKit.RectFits also walks every claimed segment for every prop, which made
        /// a 5 ha car yard take a second).
        /// </summary>
        public bool Fits(OrientedRect r, float gap)
        {
            var ring = r.Ring;
            for (int i = 0; i < 4; i++)
                if (!Region.Contains(ring[i]) || Region.CrossesBoundary(ring[i], ring[(i + 1) % 4], false)) return false;
            float probe = math.min(0.7f, math.min(r.HalfLength, r.HalfWidth));
            float su = math.max(0f, r.HalfLength - probe), sv = math.max(0f, r.HalfWidth - probe);
            int nu = math.max(1, (int)math.ceil(su * 2f / 1.0f)), nv = math.max(1, (int)math.ceil(sv * 2f / 1.0f));
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    var q = r.Local(-su + 2f * su * i / nu, -sv + 2f * sv * j / nv);
                    if (Occ.Discs.Overlaps(q, probe, gap) || Occ.Corridors.Overlaps(q, probe, gap) || Occ.HitsPolygon(q, probe)) return false;
                }
            // The corners, which the inset probes miss: a turned prop's corner must not poke into its neighbour.
            foreach (var q in ring)
                if (Occ.Discs.Overlaps(q, 0.15f, gap) || Occ.Corridors.Overlaps(q, 0.15f, gap)) return false;
            return true;
        }

        /// <summary>
        /// Claims a rectangle as a capsule that ends flush with the rectangle's short sides (LeisureKit.TakeRect's capsule
        /// overshoots them by half the width, which kept back-to-back rows and end-to-end props apart).
        /// </summary>
        public void Claim(OrientedRect r)
        {
            float hl = r.HalfLength, hw = r.HalfWidth;
            if (math.abs(hl - hw) < 0.05f) { Occ.Take(r.Center, hw, YardKit.KindStuff); return; }
            if (hl > hw) Occ.TakeCorridor(r.Local(-(hl - hw), 0f), r.Local(hl - hw, 0f), hw, YardKit.KindStuff);
            else Occ.TakeCorridor(r.Local(0f, -(hw - hl)), r.Local(0f, hw - hl), hl, YardKit.KindStuff);
            // The corners the capsule leaves out, so a turned neighbour cannot slide its corner in there.
            float cr = math.min(hl, hw) * 0.3f;
            foreach (var q in r.Ring) Occ.Take(q - math.normalizesafe(q - r.Center) * cr, cr, YardKit.KindStuff);
        }

        /// <summary>A surface patch (clipped to the outline).</summary>
        public void Pad(OrientedRect r, SurfaceRole role, string category) => YardKit.Patch(Ctx, r.Ring, role, AssetMix.One(""), category);

        /// <summary>A line of concrete blocks from a to b (laid end to end, long side along the line).</summary>
        public int BlockWall(float2 a, float2 b, uint salt)
        {
            float len = math.distance(a, b);
            if (len < 1f) return 0;
            var dir = (b - a) / len;
            var item = IndustrialAssets.Block;
            int n = math.max(1, (int)math.floor((len + 0.05f) / (item.X + 0.05f)));
            float pitch = len / n;
            int placed = 0;
            // Blocks face across the wall (local X along it).
            var face = new float2(-dir.y, dir.x);
            for (int i = 0; i < n; i++)
            {
                var p = a + dir * (pitch * (i + 0.5f));
                if (!Region.ContainsDisc(p, 0.4f) || !ReservedOnly.Free(p, 0.65f, 0f, 0f)) continue;
                uint v = Hashing.Mix(salt, (uint)i);
                // Walls are meant to touch: no occupancy check, but the outline is respected.
                Ctx.Object(p, math.atan2(face.x, face.y), ObjectRole.Furniture, item.Pick(salt), v, -1, item.Category);
                placed++;
            }
            Occ.TakeCorridor(a, b, 0.35f, YardKit.KindStuff);
            Items += placed;
            return placed;
        }
    }
}

// Designed sports parks (2026-10-29, the user: "we just make amazingly detailed plazas by looking into how ground work
// works and looking into more assets in the game ... let's now channel this effort into making sports parks just as
// amazing ... explode the box"). The classic sports park (SportsParkComposer) composes generic parts round a hero
// field. A designed sports park lays a real sports ground's plan on the site's own axes, in the largest rectangle that
// fits (PlazaDesigns.Kit.BestBox), on its own ground (PlazaDesigns.Levels: level floors cut into the ground, banks at
// 45 % with a stone plinth at the foot and a railing on top, stone stairs with a lip on every tread, seating steps), and
// dresses it with the game's own sports props (SportsProps: grandstands, 22 m light masts, dugouts, scoreboards). The
// fields themselves are the shared field add-ons (pitch, track, tennis, basketball, skate): each design carves a sized
// place for them ("place:<id>" + hostSized) and seats and lights them itself (hostStands, hostLights).
//
//   arena        Stadium bowl: a pitch sunk into the ground inside a ring of grandstands (covered grandstands on a big
//                site), light masts in the four corners, stairs down from a concourse of food stalls and kiosks, flags.
//   centreCourt  Centre court: a tennis show court at the bottom of an amphitheatre of seating steps on all four sides,
//                practice courts in cages round the rim, a pergola terrace with cafe tables, a grass hill with trees.
//   hillside     Terraced sports ground: three or four level terraces stepping up the site (up the slope on a hill), a
//                pitch, tennis, basketball and skate on their own terrace, seating steps on the bank above the pitch,
//                walled banks and stone stairs between the levels.
//   cageCourts   Urban cage courts: a grid of chain-link cages (five-a-side and basketball) on dark asphalt, small stands
//                between them, light masts on the cage corners, a raised viewing deck with cafe tables and food carts.
//   athletics    Athletics park: a running track with a grandstand along the home straight and small stands opposite,
//                light masts, an avenue of flags to an "Olympic flame" fountain, a warm-up lawn of hurdles and cones.
//
// Every design works from a city block up; a site too small or too odd for it falls back to the classic composition.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.AddOns.Fields;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Leisure
{
    internal static class SportsDesigns
    {
        public static readonly string[] Ids = { "arena", "centreCourt", "hillside", "cageCourts", "athletics" };
        public static bool Handles(string subtype) => Array.IndexOf(Ids, subtype) >= 0;

        private const int PFeature = PlazaDesigns.PFeature, PKerb = PlazaDesigns.PKerb, PGreen = PlazaDesigns.PGreen,
            PFrame = PlazaDesigns.PFrame, PWalk = PlazaDesigns.PWalk, PPromenade = PlazaDesigns.PPromenade;

        private static readonly AssetRef Asphalt = new AssetRef("", "Asphalt Surface 01", "Pavement Surface 01");
        private static readonly AssetRef Concrete = new AssetRef("", "Concrete Surface 01", "Pavement Surface 01");
        private static readonly AssetRef Sand = new AssetRef("", "Sand Surface 01", "Sand Surface 02");
        private static readonly string[] HillTrees = { "OakTree01", "BirchTree01", "EU_ChestnutTree01", "EU_AlderTree01" };

        /// <summary>The design's working frame: the kit, the ground, the box inside the perimeter promenade.</summary>
        private sealed class Site
        {
            public PlazaDesigns.Kit K;
            public LayerContext Ctx;
            public float IA, IB;           // half sizes of the design area (inside the promenade and its trees)
            public float Edge;             // the perimeter band between the design area and the box's edge
            public PlazaDesigns.Levels Lv;
            public float2 Entry;           // where most people arrive (the main gate, else the box's −B side)
            public int Places;
            /// <summary>Every facility's place (the walks join each one).</summary>
            public readonly List<OrientedRect> PlaceRects = new List<OrientedRect>();
            public float2 A => K.A;
            public float2 S => K.S;
            public float2 W(float u, float v) => K.W(u, v);
            public uint V(uint salt) => K.V(salt);
        }

        /// <summary>Lays the design; false when the site cannot take it (the caller composes the classic park instead).</summary>
        public static bool Plan(LayerContext ctx, List<Gate> gates, string design, float2 axis)
        {
            var region = ctx.LayerRegion;
            var occ = new Occupancy(region, ctx.Input.Reserved, ctx.Site);
            var palette = GroundPalettes.Find(design == "cageCourts" ? GroundPalettes.Modern : GroundPalettes.Classic) ?? GroundPalettes.Default;
            var pave = new PavingPlan { Axis = axis };
            var k = new PlazaDesigns.Kit(ctx, occ, region, gates, palette, pave, axis, false);
            // The design's long axis runs along the box's long side (an L's arm, a long block).
            if (k.EA < k.EB - 0.5f) k.TurnQuarter();
            float span = math.sqrt(region.Area);
            // The perimeter: a kerb, a 3.2 m promenade, a row of trees; the design starts behind them.
            const float kerb = 0.6f, prom = 3.2f, treeRow = kerb + prom + 1.7f;
            float edge = math.max(treeRow + 1.8f, math.clamp(span * 0.04f, 7f, 9f));
            var s = new Site { K = k, Ctx = ctx, IA = k.EA - edge, IB = k.EB - edge, Edge = edge };
            if (s.IA < 30f || s.IB < 24f) return false;
            // A design is one composed piece: space reserved inside it (a monument, a kept path) sends the site to the classic park,
            // which plans round it.
            if (Reserved(s, -s.IA, s.IA, -s.IB, s.IB)) return false;
            s.Entry = MainGate(k, gates);
            ctx.Plan.Facts["sportsDesign"] = Array.IndexOf(Ids, design);
            ctx.Plan.Facts["designBoxA"] = Math.Round(s.IA * 2f);
            ctx.Plan.Facts["designBoxB"] = Math.Round(s.IB * 2f);

            Lawn.Cover(ctx, region.Ring, AssetMix.One(""));
            // The perimeter: a pale band at the outline, a paved promenade inside it (every entrance lands on it), a row of
            // trees inside the promenade with benches and lamps between them.
            k.Lay(PFrame, PavingShapes.Frame(region.Ring, kerb), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(region.Ring, kerb + prom), k.P.Path);
            // The designed ground: the whole box, blending back to the ground as it is along its edge.
            s.Lv = new PlazaDesigns.Levels(k, k.EA - 0.3f, k.EB - 0.3f);
            // The skirt: wide enough that the base plane meets the ground as it is at 40 % at most (hills leave a residual).
            float resid = 0f;
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                foreach (var (u, v) in new[] { (math.lerp(-k.EA, k.EA, t), -k.EB), (math.lerp(-k.EA, k.EA, t), k.EB), (-k.EA, math.lerp(-k.EB, k.EB, t)), (k.EA, math.lerp(-k.EB, k.EB, t)) })
                    resid = math.max(resid, math.abs(s.Lv.Base(u, v) - ctx.H(k.W(u, v))));
            }
            s.Lv.Skirt = math.clamp(resid / 0.34f, edge * 0.5f, edge - 0.5f);
            s.Lv.FineCorners = true;
            ctx.Plan.Facts["designSkirt"] = Math.Round(s.Lv.Skirt, 2);

            bool ok;
            switch (design)
            {
                case "arena": ok = Arena(s); break;
                case "centreCourt": ok = CentreCourt(s); break;
                case "hillside": ok = Hillside(s); break;
                case "cageCourts": ok = CageCourts(s); break;
                default: ok = Athletics(s); break;
            }
            if (!ok)
            {
                // Nothing has been planted yet: drop what the frame laid and let the classic park take the site.
                ctx.Plan.Facts.Remove("sportsDesign");
                ctx.Plan.Surfaces.Clear();
                ctx.Plan.Terraform.Clear();
                ctx.Plan.Objects.Clear();
                ctx.Plan.Regions.Clear();
                ctx.Plan.Nets.Clear();
                return false;
            }
            ctx.Check();
            Walks(s, gates, kerb + prom * 0.5f);
            // (the tree row clear of the promenade walk: a crown 2.2 m round, the walk half its width beyond; at the promenade's
            // edge every tree was refused and the ring stood empty on a city block)
            k.RingTrees(math.max(treeRow, kerb + prom * 0.5f + Composer.DefaultHalfWidth(NetRole.Pathway) + 0.4f + 2.2f + 0.6f), 9.5f, false, 1, true);
            k.Leftover(treeRow + 2f, "fence");
            k.FillLeftover();
            ctx.Check();
            PlazaDressing.Lighting(ctx, occ, region, gates, k.C, 0f, k.Lamp);
            k.Flush();
            pave.Emit(ctx, region.Ring, SurfaceRole.Plaza, palette.Field, AssetCategories.SurfacePaving, false);
            foreach (var g in gates)
            {
                var inward = region.InwardNormal(g.Segment);
                ctx.Object(g.Position, math.atan2(inward.x, inward.y), ObjectRole.AccessPoint, new AssetRef("", "Pedestrian Access Location"), 0);
            }
            // The rest of the ground is green (the leisure layer's regions: trees, groves, the kids' features).
            ctx.AddRegion(region.Ring, "green:0", RegionTags.Green, RegionTags.Core);
            ctx.Stat("Placemaker.Stat.Facilities", s.Places, Keys.UnitCount);
            ctx.Plan.Facts["facilities"] = s.Places;
            ctx.Plan.Facts["designTrees"] = k.TreeCount;
            ctx.Plan.Facts["designBenches"] = k.BenchCount;
            ctx.Plan.Facts["designWalls"] = k.WallCount;
            ctx.Plan.Facts["designFlights"] = k.FlightCount;
            ctx.Plan.Facts["designCafes"] = k.CafeCount;
            return true;
        }

        /// <summary>
        /// The walks (P2-4; the user: sports parks "lack paths to the different facilities"): the designs were paving surfaces only,
        /// with no path a citizen or the eye could follow. A loop along the perimeter promenade, a spur in from every gate, and from
        /// the loop the shortest clear spur to each facility (never across another one). Whatever the design stood in their way
        /// (a tree, a bench, a flag) goes, and the tree rows planted after keep off them.
        /// </summary>
        private static void Walks(Site s, List<Gate> gates, float loopInset)
        {
            var ctx = s.Ctx; var region = ctx.LayerRegion;
            var asset = AssetMix.One(ctx.S("pathAsset"));
            float hw = Composer.DefaultHalfWidth(NetRole.Pathway);
            var loop = region.InsetVertices(loopInset);
            if (loop == null || loop.Length < 3) return;
            var segs = new List<(float2 a, float2 b)>();
            void Lay(float2 a, float2 b)
            {
                if (math.distance(a, b) < 1f) return;
                ctx.StraightNet(a, b, NetRole.Pathway, asset);
                s.K.Occ.TakeCorridor(a, b, hw + 0.4f, 100);
                s.K.Occ.Paths.Add(a, b, hw, 100);
                segs.Add((a, b));
            }
            // the loop, its corners broken where it cuts across them (a gate or a spur lands on the nearest straight)
            for (int i = 0; i < loop.Length; i++) Lay(loop[i], loop[(i + 1) % loop.Length]);
            float2 OnLoop(float2 p)
            {
                float2 best = loop[0]; float bd = float.MaxValue;
                for (int i = 0; i < loop.Length; i++)
                {
                    var q = SegmentIndex.ClosestPoint(p, loop[i], loop[(i + 1) % loop.Length]);
                    float d = math.distancesq(p, q);
                    if (d < bd) { bd = d; best = q; }
                }
                return best;
            }
            foreach (var g in gates)
                if (EntranceKinds.Walks(g.Kind)) Lay(g.Position, OnLoop(g.Position));
            int spurs = 0;
            foreach (var r in s.PlaceRects)
            {
                float best = float.MaxValue; float2 ba = default, bb = default;
                var ring = r.Ring;
                for (int e = 0; e < 4; e++)
                {
                    var mid = math.lerp(ring[e], ring[(e + 1) % 4], 0.5f);
                    var outN = math.normalizesafe(mid - r.Center);
                    var a = mid + outN * 0.8f;
                    var hit = OnLoop(a);
                    float len = math.distance(a, hit);
                    if (len < 1.5f) { best = 0f; break; }   // the facility sits on the loop already
                    // the spur leaves square to the facility's side and turns onto the loop at most once
                    var bend = a + outN * math.max(0f, math.dot(hit - a, outN));
                    if (len >= best || region.CrossesBoundary(a, bend, false) || region.CrossesBoundary(bend, hit, false)) continue;
                    bool clear = true;
                    foreach (var o in s.PlaceRects)
                    {
                        if (o.Center.Equals(r.Center)) continue;
                        if (Crosses(o, a, bend, hw + 0.6f) || Crosses(o, bend, hit, hw + 0.6f)) { clear = false; break; }
                    }
                    if (!clear) continue;
                    best = len; ba = a; bb = bend;
                    if (math.distance(bend, hit) < 0.5f) bb = hit;
                }
                if (best <= 0f || best == float.MaxValue) continue;
                Lay(ba, bb);
                var end = OnLoop(bb);
                if (math.distance(bb, end) > 0.5f) Lay(bb, end);
                spurs++;
            }
            // clear what stands on the walks
            ctx.Plan.Objects.RemoveAll(o =>
            {
                if (o.Role == ObjectRole.Decal || o.Role == ObjectRole.AccessPoint) return false;
                float keep = hw + (o.Role == ObjectRole.Tree ? 2f : 0.7f);
                foreach (var (a, b) in segs) if (SegmentIndex.DistSq(o.Position.xz, a, b) < keep * keep) return true;
                return false;
            });
            ctx.Plan.Facts["designWalkSpurs"] = spurs;
        }

        /// <summary>The segment a-b passes within <paramref name="pad"/> of the rectangle.</summary>
        private static bool Crosses(OrientedRect r, float2 a, float2 b, float pad)
        {
            int n = math.max(2, (int)(math.distance(a, b) / 1.5f));
            for (int i = 0; i <= n; i++)
            {
                var p = math.lerp(a, b, i / (float)n) - r.Center;
                if (math.abs(math.dot(p, r.Axis)) < r.HalfLength + pad && math.abs(math.dot(p, r.Side)) < r.HalfWidth + pad) return true;
            }
            return false;
        }

        private static float2 MainGate(PlazaDesigns.Kit k, List<Gate> gates)
        {
            foreach (var g in gates) if (EntranceKinds.Normalize(g.Kind) == EntranceKinds.Main) return g.Position;
            foreach (var g in gates) if (EntranceKinds.Walks(g.Kind)) return g.Position;
            return k.W(0f, -k.EB - 10f);
        }

        // ───────────── shared pieces ─────────────

        /// <summary>
        /// A designed bank for a height change: 40 % (the plaza kit's 45 % is the limit; a mitred corner or a bank over the
        /// slope's plane comes out a few points steeper than its run).
        /// </summary>
        private static float SBank(float h) => math.max(2f, math.abs(h) / 0.40f);

        /// <summary>True when space the recipe reserved (a disc, a corridor, a polygon) reaches into the box u0..u1 × v0..v1.</summary>
        private static bool Reserved(Site s, float u0, float u1, float v0, float v1)
        {
            var occ = s.K.Occ;
            int nu = math.max(1, (int)math.ceil((u1 - u0) / 2.5f)), nv = math.max(1, (int)math.ceil((v1 - v0) / 2.5f));
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    var p = s.W(math.lerp(u0, u1, i / (float)nu), math.lerp(v0, v1, j / (float)nv));
                    if (occ.Discs.Overlaps(p, 1.3f, 0f) || occ.Corridors.Overlaps(p, 1.3f, 0f) || occ.HitsPolygon(p, 1.3f)) return true;
                }
            return false;
        }

        private static bool Wants(Site s, string layer) => s.Ctx.Input.HasChildTarget(Amenities.Place(layer));

        /// <summary>A sized place for a field add-on (it lays the field inside), seated and lit by the design.</summary>
        private static void Place(Site s, string layer, OrientedRect r, bool stands = true, bool lights = true, string extra = null)
        {
            var tags = new List<string> { Amenities.Place(layer), SportsParkLayer.SizedTag };
            if (stands) tags.Add(FieldAddOn.HostStandsTag);
            if (lights) tags.Add(FieldAddOn.HostLightsTag);
            if (!string.IsNullOrEmpty(extra)) tags.Add(extra);
            s.Ctx.AddRegion(r.Ring, Amenities.Place(layer) + ":" + s.Places, tags.ToArray());
            s.K.Occ.TakePolygon(r.Ring);
            s.PlaceRects.Add(r);
            s.Places++;
        }

        /// <summary>Grandstands along the front line a → b looking at <paramref name="toward"/>.</summary>
        private static int Stands(Site s, float2 a, float2 b, float2 toward, float depth, bool grand) =>
            SportsProps.StandRow(s.Ctx, s.K.Occ, a, b, toward, depth, grand, s.V(0x57A9u));

        private static void Mast(Site s, float2 p, float2 aim, bool big = true) => SportsProps.Mast(s.Ctx, s.K.Occ, p, aim, big, s.V(0x11A5u), null, false);

        /// <summary>A chain-link cage round a rectangle, a door gap in the middle of the side nearest <paramref name="door"/>.</summary>
        private static void Cage(Site s, OrientedRect r, float2 door)
        {
            var ring = r.Ring;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < 4; i++) { float d = math.distancesq(math.lerp(ring[i], ring[(i + 1) % 4], 0.5f), door); if (d < bd) { bd = d; best = i; } }
            var fence = AssetMix.One("", "FenceIndustrialHigh01", "FenceIndustrialHigh02");
            for (int i = 0; i < 4; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % 4];
                if (i != best) { s.Ctx.StraightNet(a, b, NetRole.Fence, fence); continue; }
                var m = math.lerp(a, b, 0.5f); var d = math.normalizesafe(b - a);
                s.Ctx.StraightNet(a, m - d * 1.4f, NetRole.Fence, fence);
                s.Ctx.StraightNet(m + d * 1.4f, b, NetRole.Fence, fence);
            }
        }

        /// <summary>Seating steps down a bank from its top (<paramref name="top"/> line) to its foot: a stone lip every tread, rows of benches.</summary>
        private static void SeatingSteps(Site s, float2 topA, float2 topB, float2 down, float width, float pitch = 1.1f, float benchPitch = 3.4f)
        {
            var k = s.K;
            var d = math.normalizesafe(topB - topA);
            for (float t = pitch; t < width - 0.3f; t += pitch)
            {
                var a = topA + down * t + d * 0.4f; var b = topB + down * t - d * 0.4f;
                k.Lay(PFeature - 1, PavingShapes.Band(a, b, 0.46f), k.P.Band);
                k.Wall(a, b, 1, null, PlazaDesigns.WallH - 0.22f, false);
            }
            int row = 0;
            for (float t = pitch * 1.7f; t < width - 0.8f; t += pitch * 2f, row++)
                foreach (var p in LeisureKit.Along(topA + down * t + d * 2f, topB + down * t - d * 2f, benchPitch))
                    k.Seat(p + d * ((row & 1) * 1.7f), down, PlazaArt.ModernBenches);
        }

        /// <summary>Concourse dressing along a line: food stalls and kiosks every so often facing <paramref name="facing"/>, bins, vending machines.</summary>
        private static void Concourse(Site s, float2 a, float2 b, float2 facing, float pitch)
        {
            var k = s.K;
            int i = 0;
            foreach (var p in LeisureKit.Along(a, b, pitch))
            {
                switch (i++ % 4)
                {
                    case 0: k.Stall(p, facing, false); break;
                    case 1: SportsProps.Prop(s.Ctx, k.Occ, p, facing, "VendingMachine01", ObjectRole.Furniture, 0.8f, s.V(0x7E4Du), AssetCategories.Kiosk, true, "VendingMachine02"); break;
                    case 2: k.Stall(p, facing, true); break;
                    default: k.Light(p); break;
                }
            }
        }

        /// <summary>A row of flags on tall poles from a to b.</summary>
        private static void Flags(Site s, float2 a, float2 b, float pitch, bool tall = false)
        {
            foreach (var p in LeisureKit.Along(a, b, pitch)) s.K.BigFlag(p, tall);
        }

        /// <summary>A walled bank line: the stone plinth at its foot, a railing on its top edge.</summary>
        private static void BankEdge(Site s, float2 footA, float2 footB, float2 topA, float2 topB, IEnumerable<(float at, float half)> gaps = null,
            IEnumerable<(float at, float half)> topGaps = null)
        {
            s.K.WallWithGaps(footA, footB, gaps, 1);
            gaps = topGaps ?? gaps;
            if (gaps == null) { s.K.Railing(topA, topB); return; }
            float len = math.distance(topA, topB);
            var d = (topB - topA) / math.max(1e-3f, len);
            var cuts = new List<(float, float)>();
            foreach (var (at, half) in gaps) cuts.Add((at - half, at + half));
            cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            float t = 0f;
            foreach (var (c0, c1) in cuts) { if (c0 > t + 0.9f) s.K.Railing(topA + d * t, topA + d * c0); t = math.max(t, c1); }
            if (len - t > 0.9f) s.K.Railing(topA + d * t, topB);
        }

        /// <summary>Trees on a loose grid in a box (local), kept off what is taken.</summary>
        private static void Grove(Site s, float u0, float u1, float v0, float v1, float pitch, string[] ids = null)
        {
            if (u1 - u0 < 4f || v1 - v0 < 4f) return;
            int j = 0;
            for (float v = v0 + pitch * 0.5f; v < v1; v += pitch * 0.87f, j++)
                for (float u = u0 + pitch * 0.5f + ((j & 1) != 0 ? pitch * 0.5f : 0f); u < u1; u += pitch)
                    s.K.Tree(s.W(u, v), false, ids ?? PlazaArt.PlaneTrees);
        }

        /// <summary>The height the box's ground has at a local point (before the design).</summary>
        private static float Ground(Site s, float u, float v) => s.Ctx.H(s.W(u, v));

        /// <summary>Lowest ground over a local box (sampled).</summary>
        private static float MinGround(Site s, float u0, float u1, float v0, float v1)
        {
            float m = float.MaxValue;
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 8; j++) m = math.min(m, Ground(s, math.lerp(u0, u1, i / 8f), math.lerp(v0, v1, j / 8f)));
            return m;
        }

        private static float MeanGround(Site s, float u0, float u1, float v0, float v1)
        {
            float m = 0f; int n = 0;
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 8; j++) { m += Ground(s, math.lerp(u0, u1, i / 8f), math.lerp(v0, v1, j / 8f)); n++; }
            return m / n;
        }

        /// <summary>The side (+1 / −1 along S) people arrive from.</summary>
        private static float EntrySide(Site s) => math.dot(s.Entry - s.K.C, s.S) >= 0f ? 1f : -1f;

        /// <summary>
        /// A level floor sunk <paramref name="depth"/> below the lowest ground round the rim box (centre <paramref name="cu"/>,
        /// half sizes ru × rv, the bank inside it). On a slope the floor rises until the high side's bank fits
        /// <paramref name="maxBank"/>: a bowl dug into a hillside. Returns the floor height; the bank width it needs.
        /// </summary>
        private static float Dig(Site s, float cu, float ru, float rv, float depth, float maxBank, out float bank)
        {
            var lv = s.Lv;
            bank = SBank(depth);
            float h = 0f;
            for (int it = 0; it < 3; it++)
            {
                var rr = lv.RimRange(cu - ru - bank, cu + ru + bank, -rv - bank, rv + bank);
                h = rr.x - depth;
                if ((rr.y - h) / 0.40f > maxBank) h = rr.y - maxBank * 0.40f;
                float need = math.max(SBank(depth), math.max(rr.y - h, h - rr.x) / 0.40f);
                bank = math.min(maxBank, need);
            }
            return h;
        }

        // ───────────── arena (stadium bowl) ─────────────

        private static SportsProps.Stand? SmallStand(float room)
        {
            var st = SportsProps.SmallFor(room);
            return st.Id == SportsProps.BenchRow.Id ? (SportsProps.Stand?)null : st;
        }

        /// <summary>The cross slope (m/m) from which the arena is laid as a hillside stadium.</summary>
        private const float HillGrade = 0.06f;

        /// <summary>
        /// The largest pitch and stands the box takes. On a hillside (<paramref name="gv"/>: the slope across the pitch) only the
        /// downhill long side has a grandstand (the uphill side is seating on the hill), and a format fits only when the banks
        /// the hill leaves can stay near the seating rake; the smallest format that fits the box is the last resort.
        /// </summary>
        private static bool ArenaFit(Site s, float2 g, out float hu, out float hv, out float D, out SportsProps.Stand side, out SportsProps.Stand? end, out bool hillOk)
        {
            const float concV = 5f;
            float slack = math.max(0f, s.Edge - s.Lv.Skirt - 0.5f);     // the ends' banks may climb into the perimeter band (no concourse there)
            float gv = g.y;
            bool hill = math.abs(gv) >= HillGrade;
            hu = hv = D = 0f; side = default; end = null; hillOk = false;
            bool found = false;
            foreach (var f in FieldSpecs.Pitch)
            {
                foreach (bool grand in new[] { true, false })
                {
                    float d = grand ? 2.6f : 1.8f, b = SBank(d) + 0.4f;
                    float fh = f.Length * 0.5f, fw = f.Width * 0.5f;
                    float roomV = s.IB - fw - b - concV - 0.8f;
                    SportsProps.Stand? sd = grand
                        ? (roomV >= SportsProps.Large04.Depth ? SportsProps.Large04 : roomV >= SportsProps.Large02.Depth ? SportsProps.Large02 : (SportsProps.Stand?)null)
                        : SmallStand(roomV);
                    if (sd == null) continue;
                    float roomU = s.IA + slack - fh - b - 0.8f;
                    SportsProps.Stand? ed = grand && roomU >= SportsProps.Large03.Depth ? SportsProps.Large03 : SmallStand(roomU);
                    if (ed == null && roomU < 3f) continue;
                    hu = fh; hv = fw; D = d; side = sd.Value; end = ed; found = true;
                    if (!hill) { hillOk = true; return true; }
                    if (HillFloor(s, gv > 0f ? 1 : 0, fw, sd.Value.Depth + 0.8f, fh + (ed.HasValue ? ed.Value.Depth + 0.8f : 3f), d).Ok) { hillOk = true; return true; }
                }
            }
            return found;
        }

        /// <summary>Width of the level concourse terrace along a hill stand's top.</summary>
        private const float HillTerrace = 5f;

        /// <summary>
        /// A hillside stadium's ground: the floor's height, the bank widths (uphill: the whole slope from the pitch apron to the top
        /// concourse), and up the hill the stand: <see cref="SeatW"/> of seating rake at <see cref="G"/>, a level terrace, a wooded
        /// bank up to the hill (<see cref="BankUp"/>).
        /// </summary>
        private struct HillCut { public float FloorH, BUp, BDn, BU, SeatW, BankUp, G; public bool Ok; }

        /// <summary>The ground of a hill stand's core at a local point: level on the floor, the seating rake from the floor's edge
        /// (<paramref name="foot"/>) to its top, the terrace level above it.</summary>
        private static float Rake(float floorH, float g, float su, float foot, float top, float v) =>
            floorH + g * (math.clamp(v * su, foot, top) - foot);

        /// <summary>
        /// The hillside stadium's ground for a pitch hv wide with a stand <paramref name="sdep"/> deep on the downhill side and a floor
        /// fu long. The stand up the hill is sized to the pitch (rows for about half its width, 12-26 m), with a level terrace on top
        /// and the rest of the slope a wooded bank up to the hill; the downhill bank and the ends run out to the perimeter band. The
        /// floor goes as deep into the hill as the rake and the bank above it allow, sunk under the downhill edge where the hill is
        /// gentle, never so high the downhill bank passes the bank grade. Ok: every bank within it.
        /// </summary>
        private static HillCut HillFloor(Site s, int up, float hv, float sdep, float fu, float D)
        {
            const float concV = 5f;
            var lv = s.Lv;
            float slack = math.max(0f, s.Edge - lv.Skirt - 0.5f), su = up == 1 ? 1f : -1f;
            float fvUp = hv + 3f, fvDn = hv + sdep;
            var c = new HillCut { BUp = math.max(HillTerrace + 15f, s.IB - concV - fvUp), BDn = math.max(2f, s.IB - concV - fvDn), BU = math.max(2f, s.IA + slack - fu) };
            c.SeatW = math.clamp(hv * 0.55f, 12f, math.min(26f, c.BUp - HillTerrace - 3f));
            c.BankUp = c.BUp - c.SeatW - HillTerrace;
            float hiUp = float.MinValue, loDn = float.MaxValue, loOut = float.MaxValue;
            for (int i = 0; i <= 12; i++)
            {
                float u = math.lerp(-fu, fu, i / 12f);
                hiUp = math.max(hiUp, lv.H(u, su * (fvUp + c.BUp)));
                loDn = math.min(loDn, lv.H(u, -su * fvDn));
                // the downhill bank's outer foot to its corners: the floor's corner falls to the lowest of the hill there
                loOut = math.min(loOut, lv.H(u * (fu + c.BU) / fu, -su * (fvDn + c.BDn)));
            }
            // (the seating at its rake, the plain banks at the plaza kit's steepest: they are planted)
            float climb = c.SeatW * 0.40f + c.BankUp * 0.42f;   // (the bank under the kit's steepest: its corners meet the end banks)
            float lo = hiUp - climb, hi = loOut + c.BDn * PlazaDesigns.BankGrade;
            c.FloorH = lo <= hi ? math.clamp(math.max(lo, loDn - D), lo, hi) : (hiUp * c.BDn + loOut * climb / PlazaDesigns.BankGrade) / (c.BDn + climb / PlazaDesigns.BankGrade);
            // the rake climbs as the hill does, at 40 % at most, never down
            float rise = hiUp - c.FloorH;
            c.G = math.clamp(rise / (c.SeatW + c.BankUp), 0f, 0.40f);
            if (rise > climb) c.G = math.min(0.55f, c.G + (rise - climb) / c.SeatW);   // a hill too steep for the box: the seating takes it
            // the ends: from the core's own height (the floor, the seating, the terrace; the floor's corners on the downhill bank) out
            float worst = 0f, top = fvUp + c.SeatW, terrace = top + HillTerrace;
            for (int i = 0; i <= 16; i++)
            {
                float v = su * math.lerp(-fvDn - c.BDn * 0.5f, terrace, i / 16f);   // into the downhill corners (a slope corner to corner)
                foreach (float e in new[] { -1f, 1f })
                    worst = math.max(worst, math.abs(lv.H(e * (fu + c.BU), v) - Rake(c.FloorH, c.G, su, fvUp, top, v)));
            }
            c.Ok = lo <= hi + 0.3f && worst <= 0.45f * c.BU;
            return c;
        }

        /// <summary>Turns the design a quarter (or back: three more quarters) with its ground frame.</summary>
        private static void Turn(Site s, bool back = false)
        {
            for (int i = back ? 3 : 1; i > 0; i--) s.K.TurnQuarter();
            float ia = s.IA; s.IA = s.IB; s.IB = ia;
            var k = s.K;
            s.Lv = new PlazaDesigns.Levels(k, k.EA - 0.3f, k.EB - 0.3f) { Skirt = s.Lv.Skirt, FineCorners = s.Lv.FineCorners };
            s.Ctx.Plan.Facts["designBoxA"] = Math.Round(s.IA * 2f);
            s.Ctx.Plan.Facts["designBoxB"] = Math.Round(s.IB * 2f);
        }

        private static bool Arena(Site s)
        {
            if (!Wants(s, "pitch")) return false;
            // P2-4 round 3 (the lead: "step it into the hill like a real stadium, with stands on the slope"): on a hillside the pitch
            // lies along the contour, so one long side faces up the hill; that side's stand is seating cut into the hill.
            // A slope corner to corner: whichever way round lets every bank stay near the rake.
            var g = s.Lv.Grad;
            if (math.abs(g.x) >= HillGrade && math.abs(g.x) > math.abs(g.y) * 1.25f)
            {
                Turn(s);
                if (!ArenaFit(s, s.Lv.Grad, out _, out _, out _, out _, out _, out _)) Turn(s, true);
            }
            else if (math.abs(g.x) >= HillGrade && ArenaFit(s, g, out _, out _, out _, out _, out _, out bool asIs) && !asIs)
            {
                Turn(s);
                if (!ArenaFit(s, s.Lv.Grad, out _, out _, out _, out _, out _, out bool turned) || !turned) Turn(s, true);
            }
            float gv = s.Lv.Grad.y;
            if (!ArenaFit(s, s.Lv.Grad, out float hu, out float hv, out float D, out var side, out var end, out _)) return false;
            var k = s.K;
            const float concV = 5f;
            float slack = math.max(0f, s.Edge - s.Lv.Skirt - 0.5f);
            bool hill = math.abs(gv) >= HillGrade;
            bool grandSides = side.Height > 10f;
            float sdep = side.Depth + 0.8f, edep = end.HasValue ? end.Value.Depth + 0.8f : 3f;
            float fu = hu + edep;
            // Per long side (0: −S, 1: +S): the floor's edge, the bank's width, a grandstand or seating on the hill.
            int up = gv > 0f ? 1 : 0, dn = 1 - up;
            float[] fv = { hv + sdep, hv + sdep }, bv = new float[2];
            float floorH, bu;
            HillCut cut = default;
            var lv = s.Lv;
            if (!hill)
            {
                float maxBank = math.min(s.IA + slack - fu, s.IB - concV - fv[0]);
                floorH = Dig(s, 0f, fu, fv[0], D, maxBank, out float bank);
                bv[0] = bv[1] = bu = bank;
            }
            else
            {
                cut = HillFloor(s, up, hv, sdep, fu, D);
                fv[up] = hv + 3f;
                bv[up] = cut.BUp; bv[dn] = cut.BDn; bu = cut.BU; floorH = cut.FloorH;
                s.Ctx.Plan.Facts["bowlHillFits"] = cut.Ok ? 1 : 0;
            }
            float[] rv = { fv[0] + bv[0], fv[1] + bv[1] };
            float ru = fu + bu;
            if (!hill) lv.AddFloor(-ru, ru, -rv[0], rv[1], floorH, new float4(bu, bu, bv[0], bv[1]));
            else
            {
                // The core: the level floor, up the hill from its edge the seating rake, the level terrace on top; every bank runs
                // at one grade from the core out to the hill (the ends from the stand's own height, so its corners are no cliffs,
                // the wooded bank above the terrace up to the hill).
                float su = up == 1 ? 1f : -1f, foot = fv[up], top = foot + cut.SeatW, t0 = floorH, rake = cut.G;
                lv.AddCore(-ru, ru, -rv[0], rv[1], new float4(bu, bu, up == 0 ? cut.BankUp : bv[0], up == 1 ? cut.BankUp : bv[1]),
                    (u, v) => Rake(t0, rake, su, foot, top, v));
                lv.AddBreak(false, su * foot); lv.AddBreak(false, su * top); lv.AddBreak(false, su * (top + HillTerrace));
                s.Ctx.Plan.Facts["bowlRake"] = Math.Round(rake * 100f);
            }
            lv.Emit();
            s.Ctx.Plan.Facts["bowlDepth"] = Math.Round(D, 2);
            s.Ctx.Plan.Facts["bowlBank"] = Math.Round(math.max(bv[0], bv[1]), 2);
            s.Ctx.Plan.Facts["bowlStands"] = grandSides ? 2 : 1;
            s.Ctx.Plan.Facts["bowlHillside"] = hill ? 1 : 0;
            float SV(int i) => i == 1 ? 1f : -1f;

            // The pitch (its place is the field add-on's own format), the stand ring floor in concrete, the banks in grass.
            Place(s, "pitch", k.Box(-hu, hu, -hv, hv));
            var pitchCut = new List<float2[]> { k.Box(-hu - 0.2f, hu + 0.2f, -hv - 0.2f, hv + 0.2f).Ring };
            k.Lay(PFeature + 4, k.Pieces(k.Box(-fu, fu, -fv[0], fv[1]).Ring, pitchCut, 4f), Concrete, SurfaceRole.Paving);
            k.Lay(PGreen - 5, k.Pieces(k.Box(-ru, ru, -rv[0], rv[1]).Ring, new List<float2[]> { k.Box(-fu - 0.1f, fu + 0.1f, -fv[0] - 0.1f, fv[1] + 0.1f).Ring }, 4f),
                k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
            float cu = hu + edep * 0.5f;
            int terraced = 0, seatRows = 0;
            if (hill)
            {
                // The hill stand: seating steps from the top concourse down to the pitch apron, cut by aisles (stairs) at the corners
                // and the quarter points, a plinth at its foot and a railing along its top.
                float sv = SV(up);
                float top = fv[up] + cut.SeatW, terrace = top + HillTerrace;
                float pitch = math.max(1.1f, cut.SeatW / 22f);
                var aisles = new[] { -cu, -hu * 0.5f, hu * 0.5f, cu };
                // the stand's two ends are stairs too: a finished edge (stepped cheek walls) where the seating meets the end banks
                float flank = fu - 1.9f;
                float a0 = -flank + 2.2f;
                foreach (float au in aisles.Concat(new[] { flank }))
                {
                    float a1 = au - 2.2f;
                    if (a1 - a0 > 6f) SeatingSteps(s, s.W(a0, sv * top), s.W(a1, sv * top), -s.S * sv, cut.SeatW, pitch, 2.6f);
                    a0 = au + 2.2f;
                }
                foreach (float au in aisles) k.Flight(s.W(au, sv * (fv[up] + 0.2f)), s.W(au, sv * (top + 0.4f)), 3.2f);
                foreach (float e in new[] { -flank, flank }) k.Flight(s.W(e, sv * (fv[up] + 0.2f)), s.W(e, sv * (top + 0.4f)), 2.6f);
                var footGaps = aisles.Concat(new[] { -flank, flank }).Select(au => (au + fu, 2.2f)).ToList();
                BankEdge(s, s.W(-fu, sv * (fv[up] + 0.15f)), s.W(fu, sv * (fv[up] + 0.15f)), s.W(-fu, sv * (top + 0.3f)), s.W(fu, sv * (top + 0.3f)), footGaps);
                // the stand reads as one: concrete terraces from the apron to its top
                k.Lay(PFeature + 4, k.Box(-fu, fu, sv > 0 ? fv[up] : -top, sv > 0 ? top : -fv[up]).Ring, Concrete, SurfaceRole.Paving);
                // the terrace along its top: the stand's own concourse (stalls, kiosks), two wide stairs up the wooded bank to the top
                k.Lay(PWalk, k.Box(-fu, fu, sv > 0 ? top + 0.2f : -terrace, sv > 0 ? terrace : -top - 0.2f).Ring, k.P.Path);
                Concourse(s, s.W(-fu + 6f, sv * (terrace - 1.4f)), s.W(fu - 6f, sv * (terrace - 1.4f)), -s.S * sv, 9f);
                if (cut.BankUp > 3f)
                    foreach (float au in new[] { -hu * 0.5f, hu * 0.5f }) k.Flight(s.W(au, sv * (terrace + 0.2f)), s.W(au, sv * (rv[up] + 0.4f)), 3.6f);
                terraced = 1;
                seatRows = (int)(cut.SeatW / pitch);
            }
            else
            {
                // P2-4 (the lead: on a hill the arena stood on one big green plinth): where the hill rises behind a bank, the bank is
                // seating cut into the slope (terrace steps and benches looking down at the pitch), not a plain grass bank
                foreach (float sv in new[] { -1f, 1f })
                    if (s.Ctx.H(s.W(0f, sv * (rv[0] + 2f))) - floorH > 1.5f && bv[0] > 3f)
                    { SeatingSteps(s, s.W(-fu + 2f, sv * rv[0]), s.W(fu - 2f, sv * rv[0]), -s.S * sv, bv[0]); terraced++; }
                foreach (float su in new[] { -1f, 1f })
                    if (s.Ctx.H(s.W(su * (ru + 2f), 0f)) - floorH > 1.5f && bu > 3f)
                    { SeatingSteps(s, s.W(su * ru, -fv[0] + 2f), s.W(su * ru, fv[0] - 2f), -s.A * su, bu); terraced++; }
            }
            s.Ctx.Plan.Facts["bowlTerracedBanks"] = terraced;
            s.Ctx.Plan.Facts["bowlSeatingRows"] = seatRows;
            // Grandstands: the long sides (the downhill one only on a hillside), then the ends.
            int stands = 0;
            float2 centre = k.C;
            for (int i = 0; i < 2; i++)
                if (!hill || i == dn)
                    stands += Stands(s, s.W(-hu + 0.5f, SV(i) * (hv + 0.6f)), s.W(hu - 0.5f, SV(i) * (hv + 0.6f)), centre, sdep - 0.6f, grandSides);
            if (end.HasValue)
                foreach (float su in new[] { -1f, 1f })
                    stands += Stands(s, s.W(su * (hu + 0.6f), -hv + 0.5f), s.W(su * (hu + 0.6f), hv - 0.5f), centre, edep - 0.6f, end.Value.Height > 10f);
            s.Ctx.Plan.Facts["stands"] = stands;
            // The corners: a light mast each (on the apron below the hill stand), a stair up (down) the stand side's bank.
            for (int i = 0; i < 2; i++)
                foreach (float su in new[] { -1f, 1f })
                {
                    bool seats = hill && i == up;
                    Mast(s, s.W(su * cu, SV(i) * (seats ? hv + 1.5f : hv + sdep * 0.35f)), centre, true);
                    if (seats) continue;
                    var inner = s.W(su * cu, SV(i) * (fv[i] - 0.3f)); var outer = s.W(su * cu, SV(i) * (rv[i] + 0.4f));
                    // a bank down from a floor above the ground (a stand on fill) is climbed from its outer foot
                    if (floorH > s.Ctx.H(outer) + 0.5f) k.Flight(outer, inner, 3.2f); else k.Flight(inner, outer, 3.2f);
                }
            // Scoreboards on the end banks' tops looking down the pitch (on a hillside one over the hill stand).
            if (hill)
                SportsProps.Prop(s.Ctx, k.Occ, s.W(0f, SV(up) * (rv[up] + 2.6f)), -s.S * SV(up), SportsProps.Scoreboard, ObjectRole.Furniture, 1.3f, s.V(0x5C0Bu), AssetCategories.SportsStand, true);
            else
                foreach (float su in new[] { -1f, 1f })
                    SportsProps.Prop(s.Ctx, k.Occ, s.W(su * (ru + 1.2f), 0f), -s.A * su, SportsProps.Scoreboard, ObjectRole.Furniture, 1.3f, s.V(0x5C0Bu), AssetCategories.SportsStand, true);
            // The banks: a plinth at the foot, a railing on the top (open at the stairs). Over a floor on fill the railing runs along
            // the floor's edge and the plinth along the bank's outer foot.
            for (int i = 0; i < 2; i++)
            {
                if (hill && i == up) continue;
                float sv = SV(i);
                var gaps = new List<(float, float)> { (ru - cu, 2.2f), (ru + cu, 2.2f) };
                bool fill = floorH > s.Ctx.H(s.W(0f, sv * (rv[i] + 1f))) + 0.5f;
                if (fill) BankEdge(s, s.W(-ru, sv * (rv[i] + 0.3f)), s.W(ru, sv * (rv[i] + 0.3f)), s.W(-fu, sv * (fv[i] + 0.15f)), s.W(fu, sv * (fv[i] + 0.15f)), gaps,
                    new List<(float, float)> { (fu - cu, 2.2f), (fu + cu, 2.2f) });
                else BankEdge(s, s.W(-fu, sv * (fv[i] + 0.15f)), s.W(fu, sv * (fv[i] + 0.15f)), s.W(-ru, sv * (rv[i] + 0.3f)), s.W(ru, sv * (rv[i] + 0.3f)), gaps);
            }
            foreach (float su in new[] { -1f, 1f })
                BankEdge(s, s.W(su * (fu + 0.15f), -fv[0]), s.W(su * (fu + 0.15f), fv[1]), s.W(su * (ru + 0.3f), -rv[0]), s.W(su * (ru + 0.3f), rv[1]));
            // The concourses along the long sides: paved, food stalls, kiosks and vending machines facing the rim; flags.
            float es = EntrySide(s);
            float[] cv = { rv[0] + concV, rv[1] + concV };
            for (int i = 0; i < 2; i++)
            {
                float sv = SV(i);
                k.Lay(PWalk, k.Box(-ru, ru, sv > 0 ? rv[i] + 0.2f : -cv[i], sv > 0 ? cv[i] : -rv[i] - 0.2f).Ring, k.P.Path);
                if (sv == es) Flags(s, s.W(-ru + 4f, sv * (cv[i] - 1.1f)), s.W(ru - 4f, sv * (cv[i] - 1.1f)), 6f, true);
                else Concourse(s, s.W(-ru + 5f, sv * (cv[i] - 1.6f)), s.W(ru - 5f, sv * (cv[i] - 1.6f)), -s.S * sv, 8f);
            }
            int ei = es > 0 ? 1 : 0;
            if (end.HasValue == false || s.IB - cv[ei] > 6f)
                Concourse(s, s.W(-ru * 0.4f, es * (cv[ei] - 3.6f)), s.W(ru * 0.4f, es * (cv[ei] - 3.6f)), -s.S * es, 9f);
            EntrancePlaza(s, SV(ei), rv[ei], cv[ei], ru);
            // On a hillside the plain banks (downhill, the ends) are wooded slopes round the bowl.
            if (hill)
            {
                float sd = SV(dn);
                float d0 = fv[dn] + 4f, d1 = rv[dn] - 1f;
                if (d1 - d0 > 6f) Grove(s, -ru + 2f, ru - 2f, sd > 0 ? d0 : -d1, sd > 0 ? d1 : -d0, 8f, HillTrees);
                float stTop = fv[up] + cut.SeatW + HillTerrace;
                float vLo = sd > 0 ? -stTop : -fv[dn], vHi = sd > 0 ? fv[dn] : stTop;
                Grove(s, fu + 4f, ru - 1f, vLo, vHi, 8f, HillTrees);
                Grove(s, -ru + 1f, -fu - 4f, vLo, vHi, 8f, HillTrees);
                // the bank above the stand's terrace, up to the top concourse
                float b0 = stTop + 3f, b1 = rv[up] - 1.5f;
                if (b1 - b0 > 5f) Grove(s, -ru + 2f, ru - 2f, sd > 0 ? -b1 : b0, sd > 0 ? -b0 : b1, 8f, HillTrees);
            }
            // Trees beyond the concourses and the ends.
            Grove(s, -s.IA, s.IA, cv[1] + 2f, s.IB, 9f);
            Grove(s, -s.IA, s.IA, -s.IB, -cv[0] - 2f, 9f);
            Grove(s, -s.IA, -ru - 2.5f, -cv[0], cv[1], 9f);
            Grove(s, ru + 2.5f, s.IA, -cv[0], cv[1], 9f);
            return true;
        }

        /// <summary>
        /// (P2-4 round 4) The stadium's front door: on the side people arrive from, a paved forecourt off the concourse in front of
        /// the main stand (as deep as the box allows, up to 18 m), lined with trees in pits, ticket kiosks either side of the middle,
        /// a pair of tall flags at its front corners and benches facing in. Taken, so the groves keep off it.
        /// </summary>
        private static void EntrancePlaza(Site s, float sv, float rv, float cv, float ru)
        {
            var k = s.K;
            float depth = math.min(18f, s.IB - rv - 0.5f);
            if (depth < 8f) return;
            float half = math.min(22f, ru - 2f);
            float v0 = rv + 0.2f, v1 = rv + depth;
            var box = k.Box(-half, half, sv > 0 ? v0 : -v1, sv > 0 ? v1 : -v0);
            k.Lay(PWalk - 1, box.Ring, k.P.Field);
            k.Lay(PFeature, PavingShapes.Frame(box.Ring, 0.6f), k.P.Band);
            float vf = v1 - 1.6f;   // the plaza's front edge (away from the stand)
            foreach (float su in new[] { -1f, 1f })
            {
                k.BigFlag(s.W(su * (half - 1.5f), sv * vf), true);
                for (float v = cv + 2.5f; v < vf - 2f; v += 7f) k.Tree(s.W(su * (half - 2f), sv * v), true, PlazaArt.PlaneTrees);
                k.Stall(s.W(su * 5f, sv * (cv + 1.8f)), -s.S * sv, su > 0);
                k.Seat(s.W(su * (half - 4.5f), sv * (vf - 3f)), s.A * -su);
            }
            k.Occ.TakePolygon(box.Ring);
            s.Ctx.Plan.Facts["entrancePlaza"] = Math.Round(half * 2f * depth);
        }

        // ───────────── centre court (amphitheatre) ─────────────

        private static bool CentreCourt(Site s)
        {
            if (!Wants(s, "tennisCourts")) return false;
            var k = s.K;
            // The show court: one doubles court (38.6 x 20.3 m place) at the bottom of the bowl.
            float cu = 19.3f, cv = 10.15f;
            float rows = math.clamp(math.floor(math.min(s.IA - cu - 9f, s.IB - cv - 9f) / 1.1f), 4f, 8f);
            float D = rows * 0.4f;
            float bs = rows * 1.1f + 0.4f;                    // seating bank width (≈ 36 %)
            float apron = 2.5f;
            float fu = cu + apron, fv = cv + apron;
            if (fu + bs + 4f > s.IA || fv + bs + 4f > s.IB) return false;
            // On a long site the bowl sits at one end, the practice courts at the other.
            float shift = s.IA - (fu + bs) > 50f ? -(s.IA - (fu + bs) - 4f) * 0.5f : 0f;
            var lv = s.Lv;
            float floorH = Dig(s, shift, fu, fv, D, math.min(s.IB - fv - 3f, bs + 6f), out float bank);
            bank = math.max(bank, bs);
            float ru = fu + bank, rv = fv + bank;
            lv.AddFloor(shift - ru, shift + ru, -rv, rv, floorH, new float4(bank));
            // A grass hill with trees beyond the bowl on a big site.
            float hillU0 = shift + ru + 10f, hillU1 = math.min(s.IA - 2f, hillU0 + 34f);
            bool hill = hillU1 - hillU0 > 20f && s.IB > 40f;
            float hv0 = -s.IB + 4f, hv1 = -s.IB + 4f + math.min(26f, s.IB * 0.5f);
            if (hill) lv.Add(hillU0, hillU1, hv0, hv1, 2.2f, SBank(2.2f) + 0.5f);
            lv.Emit();
            s.Ctx.Plan.Facts["bowlDepth"] = Math.Round(D, 2);
            s.Ctx.Plan.Facts["seatingRows"] = rows;

            var court = k.Box(shift - cu, shift + cu, -cv, cv);
            Place(s, "tennisCourts", court, true, true, FieldAddOn.SurfaceTagPrefix + "grass");
            k.Lay(PFeature + 4, k.Pieces(k.Box(shift - fu, shift + fu, -fv, fv).Ring, new List<float2[]> { k.Box(shift - cu - 0.1f, shift + cu + 0.1f, -cv - 0.1f, cv + 0.1f).Ring }, 3f), k.P.Accent);
            // Seating steps all round, looking down at the court; a wide stair down on the entry side's axis, a narrow one opposite.
            float es = EntrySide(s);
            foreach (float sv in new[] { -1f, 1f })
                SeatingSteps(s, s.W(shift - fu - 1f, sv * rv), s.W(shift + fu + 1f, sv * rv), -s.S * sv, bank);
            foreach (float su in new[] { -1f, 1f })
                SeatingSteps(s, s.W(shift + su * ru, -fv + 1.5f), s.W(shift + su * ru, fv - 1.5f), -s.A * su, bank);
            k.Lay(PFeature + 2, k.Pieces(k.Box(shift - ru, shift + ru, -rv, rv).Ring, new List<float2[]> { k.Box(shift - fu, shift + fu, -fv, fv).Ring }, 4f), k.P.Field);
            k.Flight(s.W(shift, es * (fv + 0.2f)), s.W(shift, es * (rv + 0.5f)), 3.4f);
            k.Flight(s.W(shift, -es * (fv + 0.2f)), s.W(shift, -es * (rv + 0.5f)), 2.6f);
            // The rim: a promenade with railings, tall flags at the corners, four light masts.
            float pu0 = shift - ru - 4f, pu1 = shift + ru + 4f, pvr = rv + 4f;
            k.Lay(PWalk, k.Pieces(k.Box(pu0, pu1, -pvr, pvr).Ring, new List<float2[]> { k.Box(shift - ru - 0.2f, shift + ru + 0.2f, -rv - 0.2f, rv + 0.2f).Ring }, 10f), k.P.Path);
            foreach (float sv in new[] { -1f, 1f })
            {
                float va = sv * (rv + 0.4f);
                k.Railing(s.W(shift - ru, va), s.W(shift - 2.2f, va));
                k.Railing(s.W(shift + 2.2f, va), s.W(shift + ru, va));
            }
            foreach (float su in new[] { -1f, 1f })
            {
                k.Railing(s.W(shift + su * (ru + 0.4f), -rv), s.W(shift + su * (ru + 0.4f), rv));
                foreach (float sv in new[] { -1f, 1f })
                {
                    Mast(s, s.W(shift + su * (ru + 2.2f), sv * (rv + 2.2f)), s.W(shift, 0f), true);
                    k.BigFlag(s.W(shift + su * (ru + 2.4f), sv * (rv - 3f)), true);
                }
            }
            // The other end: practice courts in cages on clay.
            float u0 = pu1 + 3f, u1 = s.IA - 1f;
            int courts = 0;
            if (u1 - u0 >= 40f)
            {
                float bu = 38.6f, bv = 35.5f;
                int nu = math.min(2, (int)math.floor((u1 - u0 + 2f) / (bu + 2f)));   // P2-4: at most 2 x 2 blocks of practice courts
                for (int i = 0; i < nu; i++)
                    for (float v = -s.IB + 1f; v + bv <= s.IB - 1f && courts < 4; v += bv + 2f)
                    {
                        float a0 = u0 + i * (bu + 2f);
                        if (hill && a0 < hillU1 + 2f && v < hv1 + 2f) continue;
                        var blk = k.Box(a0, a0 + bu, v, v + bv);
                        if (!k.Fits(blk, 0.5f)) continue;
                        Place(s, "tennisCourts", blk, false, false, FieldAddOn.SurfaceTagPrefix + "clay");
                        courts++;
                    }
            }
            else if (s.IB - pvr > 22f)
            {
                var blk = k.Box(shift - 19.3f, shift + 19.3f, -es > 0 ? pvr + 1f : -pvr - 21.3f, -es > 0 ? pvr + 21.3f : -pvr - 1f);
                if (k.Fits(blk, 0.5f)) { Place(s, "tennisCourts", blk, false, false, FieldAddOn.SurfaceTagPrefix + "clay"); courts++; }
            }
            s.Ctx.Plan.Facts["practiceCourts"] = courts;
            // The members' terrace: a pergola canopy on the entry side of the rim, tables under it looking at the bowl.
            if (s.IB - pvr > 7f)
            {
                float vt = es * (pvr + 0.5f);
                var a = s.W(shift - ru * 0.8f, vt); var b = s.W(shift + ru * 0.8f, vt);
                var strip = es > 0 ? k.Canopy(b, a, 2, "PergolaModular07", true) : k.Canopy(a, b, 2, "PergolaModular07", true);
                k.Lay(PFeature + 5, strip.Ring, k.P.Focal);
                foreach (var p in LeisureKit.Along(strip.Local(-strip.HalfLength + 2f, 0f), strip.Local(strip.HalfLength - 2f, 0f), 3f)) k.CafeSet(p, k.Phase, false, true);
            }
            if (hill)
            {
                k.Lay(PGreen - 4, k.Box(hillU0, hillU1, hv0, hv1).Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                Grove(s, hillU0 + 4f, hillU1 - 4f, hv0 + 4f, hv1 - 4f, 9f, PlazaArt.GardenTrees);
                foreach (var p in LeisureKit.Along(s.W(hillU0 + 3f, hv1 + 1.4f), s.W(hillU1 - 3f, hv1 + 1.4f), 5f)) k.Seat(p, s.S);
            }
            Grove(s, -s.IA, pu0 - 2f, -s.IB, s.IB, 15f, PlazaArt.LimeTrees);
            foreach (float sv in new[] { -1f, 1f }) Grove(s, pu0, pu1, sv > 0 ? pvr + 2f : -s.IB, sv > 0 ? s.IB : -pvr - 2f, 9f, PlazaArt.LimeTrees);
            return true;
        }

        // ───────────── hillside (terraced sports ground) ─────────────

        private static bool Hillside(Site s)
        {
            var k = s.K;
            // Uphill along +A (on flat land the terraces still step up: a ground built into a bank).
            k.FaceUphill();
            var lv = s.Lv;
            // The terraces bank down to the perimeter inside the design area: an estimate of that bank keeps the fields off it.
            float sbE = SBank(2.5f) + 0.5f;
            float ib = s.IB - sbE;
            float lenA = (s.IA - sbE) * 2f;
            var plan = new List<(string layer, float len)>();
            bool pitch = Wants(s, "pitch"), tennis = Wants(s, "tennisCourts"), courts = Wants(s, "basketballCourt") || Wants(s, "skateBowl");
            // The pitch lies across the terraces (its long side along S): the bank above it is one long grandstand of steps.
            float pitchLen = 0f, pitchW = 0f;
            foreach (var f in FieldSpecs.Pitch)
                if (f.Length <= ib * 2f - 1f && f.Width <= lenA * 0.5f) { pitchLen = f.Width; pitchW = f.Length; break; }
            if (pitch && pitchLen > 0f) plan.Add(("pitch", pitchLen + 4f));
            if (tennis) plan.Add(("tennisCourts", 39f));
            if (courts) plan.Add(("courts", 34f));
            if (plan.Count < 2) return false;
            // The layout and the step: the terraces' floors rise by the ground's own rise between their middles (a hillside),
            // never by less than 1.8 m (a level change must read from the camera); the banks between them are sized for it.
            int n = plan.Count;
            float bankH = 2.4f, bankW = 0f, room = 0f, extra = 0f;
            bool cafe = false;
            var levels = new List<(string layer, float u0, float u1, float h)>();
            for (int pass = 0; pass < 4; pass++)
            {
                n = plan.Count;
                float total = 0f; foreach (var p in plan) total += p.len;
                bankW = SBank(bankH) + 2.4f;                 // the bank plus a walk along its top
                room = lenA - (n - 1) * bankW;
                if (room < total)
                {
                    if (plan.Count <= 2) return false;
                    plan.RemoveAt(plan.Count - 1);
                    continue;
                }
                cafe = room - total >= 8f;
                extra = (room - total - (cafe ? 8f : 0f)) / n;
                levels.Clear();
                float uu = -s.IA + sbE;
                for (int i = 0; i < n; i++)
                {
                    float u0 = uu, u1 = uu + plan[i].len + extra + (cafe && i == n - 1 ? 8f : 0f);
                    levels.Add((plan[i].layer, u0, u1, lv.H((u0 + u1) * 0.5f, 0f)));
                    uu = u1 + bankW;
                }
                float step = math.max(2.4f, (levels[n - 1].h - levels[0].h) / math.max(1, n - 1));
                if (math.abs(step - bankH) < 0.05f) break;
                bankH = step;
            }
            if (levels.Count != n || room < 0f) return false;
            // Heights: one step apart, centred on the ground as it is (as much cut as fill; on flat land the lowest terrace
            // is dug in and the top one raised).
            float h0 = 0f;
            for (int i = 0; i < n; i++) h0 += levels[i].h - i * bankH;
            h0 /= n;
            for (int i = 0; i < n; i++) { var q = levels[i]; levels[i] = (q.layer, q.u0, q.u1, h0 + i * bankH); }
            // Floors: each terrace runs on under the bank above it (the next floor rises from it), and banks down (or up)
            // to the promenade on its outer edges, inside the design area.
            float worstSide = 0f, bankRun = SBank(bankH);
            for (int i = 0; i < n; i++)
            {
                var (_, u0, u1, h) = levels[i];
                float lo = i == 0 ? -s.IA - 0.3f : u0 - bankW;
                float hi = i == n - 1 ? s.IA + 0.3f : u1 + bankRun + 0.6f;
                float v0 = -s.IB - 0.3f, v1 = s.IB + 0.3f;
                float dSide = 0f, dLo = 0f, dHi = 0f;
                for (int j = 0; j <= 16; j++)
                {
                    float t = j / 16f;
                    float uu = math.lerp(lo, hi, t), vv = math.lerp(v0, v1, t);
                    dSide = math.max(dSide, math.max(math.abs(lv.H(uu, v0) - h), math.abs(lv.H(uu, v1) - h)));
                    dLo = math.max(dLo, math.abs(lv.H(lo, vv) - h));
                    dHi = math.max(dHi, math.abs(lv.H(hi, vv) - h));
                }
                float sb = math.min(dSide < 0.05f ? 0f : SBank(dSide), sbE + 8f);
                float bLo = i == 0 ? math.min(dLo < 0.05f ? 0f : SBank(dLo), sbE + 8f) : bankRun;
                float bHi = i == n - 1 ? math.min(dHi < 0.05f ? 0f : SBank(dHi), sbE + 8f) : 0f;
                worstSide = math.max(worstSide, math.max(sb, math.max(i == 0 ? bLo : 0f, bHi)));
                lv.AddFloor(lo, hi, v0, v1, h, new float4(bLo, bHi, sb, sb));
            }
            lv.Emit();
            s.Ctx.Plan.Facts["terraces"] = n;
            s.Ctx.Plan.Facts["terraceStep"] = Math.Round(bankH, 2);
            s.Ctx.Plan.Facts["terraceEdgeBank"] = Math.Round(worstSide, 2);

            for (int i = 0; i < n; i++)
            {
                var (layer, u0, u1, h) = levels[i];
                float mid = (u0 + u1 - (cafe && i == n - 1 ? 8f : 0f)) * 0.5f;
                switch (layer)
                {
                    case "pitch":
                    {
                        var c = s.W(mid, 0f);
                        Place(s, "pitch", new OrientedRect { Center = c, Axis = s.S, HalfLength = pitchW * 0.5f, HalfWidth = pitchLen * 0.5f }, true, true);
                        foreach (float sv in new[] { -1f, 1f })
                            foreach (float su in new[] { -1f, 1f })
                                Mast(s, s.W(mid + su * (pitchLen * 0.5f + 1.2f), sv * (pitchW * 0.5f - 6f)), c, true);
                        // Small stands on the low edge, looking across the pitch at the seating bank.
                        float low = (mid - pitchLen * 0.5f) - u0;
                        if (low > SportsProps.Small02.Depth + 1.6f)
                            Stands(s, s.W(mid - pitchLen * 0.5f - 0.8f, -pitchW * 0.35f), s.W(mid - pitchLen * 0.5f - 0.8f, pitchW * 0.35f), c, low - 1.4f, false);
                        break;
                    }
                    case "tennisCourts":
                    {
                        float bv = 35.5f;
                        int blocks = math.max(1, (int)math.floor((ib * 2f - 2f + 3f) / (bv + 3f)));
                        float spanV = blocks * bv + (blocks - 1) * 3f;
                        for (int b = 0; b < blocks; b++)
                        {
                            float v0 = -spanV * 0.5f + b * (bv + 3f);
                            if (v0 + bv > ib + 0.5f) break;
                            Place(s, "tennisCourts", k.Box(mid - 19.3f, mid + 19.3f, v0, v0 + bv), false, false);
                        }
                        break;
                    }
                    default:
                    {
                        // Basketball courts in cages and a skate bowl side by side across the terrace.
                        bool skate = Wants(s, "skateBowl");
                        int slots = (int)math.floor((ib * 2f - 2f + 2f) / 27f);
                        float spanV = slots * 25f + (slots - 1) * 2f;
                        for (int q = 0; q < slots; q++)
                        {
                            float v = -spanV * 0.5f + q * 27f;
                            if (skate && q == slots / 2)
                            {
                                Place(s, "skateBowl", k.Box(mid - 13f, mid + 13f, v - 0.5f, v + 25.5f), false, false);
                                continue;
                            }
                            if (!Wants(s, "basketballCourt")) continue;
                            var court = k.Box(mid - 17f, mid + 17f, v, v + 25f);
                            Place(s, "basketballCourt", court, false, false);
                            Cage(s, new OrientedRect { Center = court.Center, Axis = court.Axis, HalfLength = court.HalfLength - 0.3f, HalfWidth = court.HalfWidth - 0.3f }, s.W(mid - 30f, v + 12.5f));
                        }
                        break;
                    }
                }
                // The bank above this terrace: a plinth at its foot, a railing on top, seating steps above the pitch, three stairs.
                if (i == n - 1) continue;
                float footU = u1, topU = u1 + SBank(bankH);
                var gaps = new List<(float, float)> { (ib - 1f, 2.4f), (8f, 2.4f), (ib * 2f - 10f, 2.4f) };
                BankEdge(s, s.W(footU + 0.15f, -ib + 1f), s.W(footU + 0.15f, ib - 1f), s.W(topU + 0.3f, -ib + 1f), s.W(topU + 0.3f, ib - 1f), gaps);
                if (layer == "pitch") SeatingSteps(s, s.W(topU, -ib + 10f), s.W(topU, ib - 10f), -s.A, topU - footU - 0.3f, 1.15f);
                else k.Lay(PGreen - 3, k.Box(footU + 0.4f, topU - 0.2f, -ib + 1f, ib - 1f).Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);
                foreach (float vv in new[] { 0f, -ib + 9f, ib - 9f })
                    k.Flight(s.W(footU + 0.3f, vv), s.W(topU + 0.4f, vv), vv == 0f ? 4f : 2.8f);
                // A walk along the top of the bank, lamps and benches looking down.
                k.Lay(PWalk, k.Box(topU + 0.5f, topU + 2.6f, -ib + 1f, ib - 1f).Ring, k.P.Path);
                foreach (var p in LeisureKit.Along(s.W(topU + 1.6f, -ib + 4f), s.W(topU + 1.6f, ib - 4f), 16f)) k.Light(p);
            }
            // The top terrace's far end: a pergola terrace with cafe tables looking down the ground.
            if (cafe)
            {
                var last = levels[n - 1];
                float uc = last.u1 - 6.5f;
                k.Lay(PFeature + 5, k.Box(last.u1 - 7.5f, last.u1 - 0.3f, -ib * 0.6f, ib * 0.6f).Ring, k.P.Focal);
                var strip = k.Canopy(s.W(uc, ib * 0.55f), s.W(uc, -ib * 0.55f), 2, "PergolaModular07", true);
                foreach (var p in LeisureKit.Along(strip.Local(-strip.HalfLength + 2f, 0f), strip.Local(strip.HalfLength - 2f, 0f), 3.2f)) k.CafeSet(p, k.Phase, false, true);
                foreach (float sv in new[] { -1f, 1f }) k.Stall(s.W(last.u1 - 3.5f, sv * (ib * 0.6f + 2.5f)), -s.A, sv > 0);
            }
            return true;
        }

        // ───────────── cage courts (urban) ─────────────

        private static bool CageCourts(Site s)
        {
            if (!Wants(s, "basketballCourt")) return false;
            var k = s.K;
            bool five = Wants(s, "pitch");
            // The deck along the entry side: a raised platform with railings, a stair, cafe tables and food carts.
            float es = EntrySide(s);
            float dh = 1.8f, db = SBank(dh);
            float deckW = math.clamp(s.IB * 0.2f, 10f, 15f) + db;
            float v0 = es > 0 ? -s.IB : -s.IB + deckW + 1.5f, v1 = es > 0 ? s.IB - deckW - 1.5f : s.IB;
            // The grid: columns of cages along A, rows across; five-a-side cages (47 x 39) in the first column.
            const float gap = 4f;
            var cages = new List<(string layer, OrientedRect r)>();
            float u = -s.IA;
            int col = 0;
            float alley = 0f;
            // P2-4: a big site filled edge to edge with 80 cages read as a car park of courts; a cage park is a block of up to
            // 16 (four rows), the rest of the site its gardens
            const int MaxCages = 16, MaxRows = 4;
            float gv0 = float.MaxValue, gv1 = float.MinValue;
            while (col < 8 && cages.Count < MaxCages)
            {
                bool pitchCol = five && col == 0 && v1 - v0 >= 39f;
                float cl = pitchCol ? 47f : 34f, cw = pitchCol ? 39f : 25f;
                if (u + cl > s.IA) break;
                int rows = math.min(MaxRows, (int)math.floor((v1 - v0 + gap) / (cw + gap)));
                if (rows < 1 || cages.Count + rows > MaxCages && cages.Count >= 8) break;
                float span = rows * cw + (rows - 1) * gap;
                float vs = (v0 + v1) * 0.5f - span * 0.5f;
                for (int r = 0; r < rows; r++)
                    cages.Add((pitchCol ? "pitch" : "basketballCourt", k.Box(u, u + cl, vs + r * (cw + gap), vs + r * (cw + gap) + cw)));
                gv0 = math.min(gv0, vs); gv1 = math.max(gv1, vs + span);
                float wide = col == 0 ? SportsProps.Small02.Depth + 2.4f : gap;   // a wide alley after the first column for stands
                if (col == 0) alley = u + cl;
                u += cl + wide;
                col++;
            }
            if (cages.Count < 2) return false;
            float used = u - (col == 1 ? SportsProps.Small02.Depth + 2.4f : gap) + s.IA;
            float shift = (s.IA * 2f - used) * 0.5f;
            var lv = s.Lv;
            float vd0 = es > 0 ? s.IB - deckW : -s.IB, vd1 = es > 0 ? s.IB : -s.IB + deckW;
            float ud = math.min(s.IA - 2f, math.clamp(used * 0.45f, 22f, 34f));
            // A level deck (on a slope too): 1.8 m over the highest ground under it, a bank all round.
            var dr = lv.RimRange(-ud, ud, vd0, vd1);
            float deckH = dr.y + dh;
            lv.AddFloor(-ud, ud, vd0, vd1, deckH, new float4(math.max(db, SBank(deckH - dr.x))));
            db = math.max(db, SBank(deckH - dr.x));
            lv.Emit();
            s.Ctx.Plan.Facts["deckHeight"] = dh;
            // Dark asphalt over the whole grid, the cages, their fields and the masts on the shared corners.
            // (game runner: laid over the five-a-side cages it covered their turf) Holes where a pitch lays its own turf.
            var turf = new List<float2[]>();
            foreach (var (layer, r0) in cages)
                if (layer == "pitch") turf.Add(new OrientedRect { Center = r0.Center + s.A * shift, Axis = r0.Axis, HalfLength = r0.HalfLength - 1f, HalfWidth = r0.HalfWidth - 1f }.Ring);
            k.Lay(PFeature + 8, k.Pieces(k.Box(math.max(-s.IA, -used * 0.5f - 3f), math.min(s.IA, used * 0.5f + 3f), math.max(v0, gv0 - 3f), math.min(v1, gv1 + 3f)).Ring, turf, 20f), Asphalt, SurfaceRole.Asphalt, AssetCategories.SurfaceAsphalt);
            var masts = new HashSet<(int, int)>();
            foreach (var (layer, r0) in cages)
            {
                var r = new OrientedRect { Center = r0.Center + s.A * shift, Axis = r0.Axis, HalfLength = r0.HalfLength, HalfWidth = r0.HalfWidth };
                Place(s, layer, r, true, true);
                Cage(s, new OrientedRect { Center = r.Center, Axis = r.Axis, HalfLength = r.HalfLength - 0.3f, HalfWidth = r.HalfWidth - 0.3f }, r.Center + s.S * es * 100f);
                foreach (var q in r.Ring)
                {
                    var key = ((int)math.round(math.dot(q - k.C, s.A) / 3f), (int)math.round(math.dot(q - k.C, s.S) / 3f));
                    if (!masts.Add(key)) continue;
                    Mast(s, q + math.normalizesafe(q - r.Center) * 1.4f, r.Center, false);
                }
            }
            // Stands in the wide alley after the first column, looking at it.
            if (col > 1)
                Stands(s, s.W(alley + shift + 0.6f, v0 + 2f), s.W(alley + shift + 0.6f, v1 - 2f), s.W(alley + shift - 20f, (v0 + v1) * 0.5f), SportsProps.Small02.Depth + 0.6f, false);
            // The deck: a plinth along its foot, a railing on its edge, a wide stair in the middle, cafe tables, carts.
            float foot = es > 0 ? vd0 + 0.15f : vd1 - 0.15f;
            float edgeV = es > 0 ? vd0 + db : vd1 - db;
            float topV0 = es > 0 ? edgeV : vd0 + db, topV1 = es > 0 ? vd1 - db : edgeV;
            k.Lay(PFeature + 3, k.Box(-ud + db, ud - db, topV0, topV1).Ring, k.P.Focal);
            k.Wall(s.W(-ud + db, foot), s.W(ud - db, foot), 1);
            k.Railing(s.W(-ud + db, edgeV - es * 0.3f), s.W(-3.5f, edgeV - es * 0.3f));
            k.Railing(s.W(3.5f, edgeV - es * 0.3f), s.W(ud - db, edgeV - es * 0.3f));
            k.Flight(s.W(0f, foot - es * 0.3f), s.W(0f, edgeV + es * 0.6f), 5f);
            float vm = (topV0 + topV1) * 0.5f;
            k.CafeGrid(-ud + db + 2f, -6f, topV0 + 1.6f, topV1 - 1.6f, 5.2f, true);
            k.CafeGrid(6f, ud - db - 2f, topV0 + 1.6f, topV1 - 1.6f, 5.2f, true);
            for (int i = 0; i < 3; i++)
                SportsProps.Prop(s.Ctx, k.Occ, s.W(-4.5f + i * 4.5f, vm + es * 2.2f), -s.S * es, PlazaArt.Carts[i % 3], ObjectRole.Furniture, 1.8f, s.V(0xCA27u), AssetCategories.Kiosk, true);
            // Vending machines and bins along the grid's edge, trees in pits along the far side.
            float ve = es > 0 ? v1 + 0.8f : v0 - 0.8f;
            int m = 0;
            foreach (var p in LeisureKit.Along(s.W(-s.IA + 5f, ve), s.W(s.IA - 5f, ve), 11f))
                if ((m++ & 1) == 0) SportsProps.Prop(s.Ctx, k.Occ, p, s.S * -es, "VendingMachine01", ObjectRole.Furniture, 0.8f, s.V(0x7E4Du), AssetCategories.Kiosk, true, "VendingMachine02");
            float vf = es > 0 ? v0 - 1.8f : v1 + 1.8f;
            foreach (var p in LeisureKit.Along(s.W(-s.IA + 4f, vf), s.W(s.IA - 4f, vf), 8f)) k.Tree(p, true, PlazaArt.PlaneTrees);
            return true;
        }

        // ───────────── athletics park ─────────────

        private static bool Athletics(Site s)
        {
            if (!Wants(s, "runningTrack")) return false;
            var k = s.K;
            // compact and training (a 150 m loop with a sprint straight) keep a hectare-sized block an athletics ground
            var ladder = new List<FieldFormat>(FieldSpecs.Track) { new FieldFormat("compact", 84f, 50f), new FieldFormat("training", 70f, 40f) };
            float es = EntrySide(s);
            FieldFormat f = null; float sdMain = 0f, sdBack = 0f; bool grand = false;
            foreach (var t in ladder)
            {
                float hu = t.Length * 0.5f, hv = t.Width * 0.5f;
                if (hu + 2f > s.IA) continue;
                float roomMain = s.IB - hv - 3f, roomBack = s.IB - hv - 2f;
                if (roomMain < SportsProps.Small02.Depth + 1.2f) continue;
                f = t;
                grand = roomMain >= SportsProps.Large04.Depth + 1.2f;
                sdMain = grand ? SportsProps.Large04.Depth + 0.8f : SportsProps.SmallFor(roomMain - 1f).Depth + 0.8f;
                float back = roomBack - sdMain >= SportsProps.Small02.Depth + 1.2f ? roomBack - sdMain : 0f;
                sdBack = back > 0f ? SportsProps.SmallFor(back - 1f).Depth + 0.8f : 0f;
                break;
            }
            if (f == null) return false;
            float pu = f.Length * 0.5f, pv = f.Width * 0.5f;
            // The track sits towards the far side so the main grandstand and the flag avenue face the gate.
            float freeV = s.IB * 2f - (2f * pv + sdMain + sdBack + 2f);
            float vc = -es * (s.IB - pv - sdBack - 1f) + es * 0f;
            vc = -es * math.max(0f, (s.IB - pv - sdBack - 1.5f) - math.max(0f, freeV * 0.5f));
            var lv = s.Lv;
            var rr = lv.RimRange(-pu - 2f, pu + 2f, vc - pv - 2f, vc + pv + 2f);
            float floorH = (rr.x + rr.y) * 0.5f;
            float bank = (rr.y - rr.x) * 0.5f > 0.15f ? SBank((rr.y - rr.x) * 0.5f) : 0f;
            if (bank > 0f) lv.AddFloor(-pu - 2f - bank, pu + 2f + bank, vc - pv - 2f - bank, vc + pv + 2f + bank, floorH, new float4(bank));
            // The stands' ground: level with the track.
            float sv0 = vc + es * (pv + 2f), sv1 = vc + es * (pv + sdMain + 1.5f);
            var rs = lv.RimRange(-pu * 0.8f, pu * 0.8f, math.min(sv0, sv1), math.max(sv0, sv1));
            if (rs.y - rs.x > 0.3f || math.abs(rs.x - floorH) > 0.3f)
            {
                float b2 = SBank(math.max(rs.y - floorH, floorH - rs.x));
                lv.AddFloor(-pu * 0.8f - b2, pu * 0.8f + b2, math.min(sv0, sv1) - (es > 0 ? 0f : b2), math.max(sv0, sv1) + (es > 0 ? b2 : 0f), floorH, new float4(b2, b2, es > 0 ? 0f : b2, es > 0 ? b2 : 0f));
            }
            lv.Emit();
            var track = k.Box(-pu, pu, vc - pv, vc + pv);
            Place(s, "runningTrack", track);
            // The main grandstand along the home straight (entry side), small stands opposite.
            float vMain = vc + es * (pv + 0.6f);
            int st = Stands(s, s.W(-pu * 0.78f, vMain), s.W(pu * 0.78f, vMain), s.W(0f, vc), sdMain - 0.6f, grand);
            if (sdBack > 0f) st += Stands(s, s.W(-pu * 0.55f, vc - es * (pv + 0.6f)), s.W(pu * 0.55f, vc - es * (pv + 0.6f)), s.W(0f, vc), sdBack - 0.6f, false);
            s.Ctx.Plan.Facts["stands"] = st;
            // Light masts round the oval, two behind each end, one behind the back straight.
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    Mast(s, s.W(su * (pu - 6f), vc + sv * (pv + (sv == es ? sdMain + 1.2f : (sdBack > 0f ? sdBack + 1.2f : 1.6f)))), s.W(0f, vc), true);
            // The flag avenue between the main stand and the gate: two rows of flags to an "Olympic flame" (a fountain drum under a tall flag).
            float va0 = vc + es * (pv + sdMain + 1.5f), va1 = es * (s.IB - 1f);
            if (math.abs(va1 - va0) > 9f)
            {
                float vf = (va0 + va1) * 0.5f;
                // A paved boulevard between lawns, two rows of flags along it, lime trees and benches on the lawns.
                k.Lay(PFeature + 5, k.Box(-pu * 0.85f, pu * 0.85f, vf - 3.6f, vf + 3.6f).Ring, k.P.Path);
                k.Lay(PKerb, PavingShapes.Frame(k.Box(-pu * 0.85f, pu * 0.85f, vf - 3.6f, vf + 3.6f).Ring, 0.6f), k.P.Band);
                foreach (float off in new[] { -2.8f, 2.8f })
                    Flags(s, s.W(-pu * 0.7f, vf + off), s.W(pu * 0.7f, vf + off), 4.5f);
                foreach (float sg in new[] { -1f, 1f })
                {
                    float vt = vf + sg * 6.5f;
                    if (math.abs(vt - vf) + 2f > math.abs(va1 - va0) * 0.5f) continue;
                    int t = 0;
                    foreach (var p in LeisureKit.Along(s.W(-pu * 0.8f, vt), s.W(pu * 0.8f, vt), 8f))
                    {
                        if (k.Tree(p, false, PlazaArt.LimeTrees) && (t & 1) == 0) k.Seat(p - s.S * (sg * 2.2f), -s.S * sg);
                        t++;
                    }
                }
                k.FountainDrum(s.W(math.min(pu * 0.85f + 5f, s.IA - 5f), vf), 3.2f, true);
                k.FountainDrum(s.W(-math.min(pu * 0.85f + 5f, s.IA - 5f), vf), 3.2f, true);
            }
            else Flags(s, s.W(-pu * 0.7f, va0 + es * 1.2f), s.W(pu * 0.7f, va0 + es * 1.2f), 6f);
            // Food stalls under the main stand's flanks.
            Concourse(s, s.W(-pu * 0.95f, vc + es * (pv + 3f)), s.W(-pu * 0.8f, vc + es * (pv + 3f)), -es * s.S, 4f);
            Concourse(s, s.W(pu * 0.8f, vc + es * (pv + 3f)), s.W(pu * 0.95f, vc + es * (pv + 3f)), -es * s.S, 4f);
            // The warm-up lawns at the ends: lanes of hurdles at one, a sand pit and training dummies at the other.
            float wu0 = pu + 3f, wu1 = s.IA - 1f;
            if (wu1 - wu0 >= 10f)
            {
                for (int lane = 0; lane < 4; lane++)
                {
                    float vv = vc - 6f + lane * 4f;
                    foreach (var p in LeisureKit.Along(s.W(wu0 + 2f, vv), s.W(wu1 - 2f, vv), 3.5f))
                        SportsProps.Prop(s.Ctx, k.Occ, p, s.A, SportsProps.Training[1], ObjectRole.Furniture, 0.4f, s.V(0x7A1Eu), AssetCategories.SportsStand, true);
                }
                var pit = k.Box(-wu1 + 2f, -wu0 - 2f, vc - 2f, vc + 2f);
                k.Lay(PFeature + 2, pit.Ring, Sand, SurfaceRole.Ground, AssetCategories.SurfaceDirt);
                k.Lay(PKerb, PavingShapes.Frame(pit.Ring, 0.5f), k.P.Band);
                int d = 0;
                foreach (var p in LeisureKit.Along(s.W(-wu1 + 2f, vc + 5f), s.W(-wu0 - 2f, vc + 5f), 2.5f))
                    SportsProps.Prop(s.Ctx, k.Occ, p, s.S, SportsProps.Training[(d++ & 1) == 0 ? 3 : 2], ObjectRole.Furniture, 0.4f, s.V(0x7A1Fu), AssetCategories.SportsStand, true);
            }
            Grove(s, -s.IA, s.IA, es > 0 ? -s.IB : vc + pv + sdBack + 2.5f, es > 0 ? vc - pv - sdBack - 2.5f : s.IB, 9f, PlazaArt.LimeTrees);
            return true;
        }
    }
}

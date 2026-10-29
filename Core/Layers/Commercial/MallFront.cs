// Strip malls, 2026-10-28 (the user's photo of a corner strip mall in Los Angeles: a single row of shopfronts in an L behind one aisle
// of parking, a low fence along the sidewalk broken only at the drives, a tall tenant sign by the entrance, a shade tree, bins at the
// kerb). The strip mall's street front and the backs of its shops:
//   • STREET FRONT: a low hedge along the sidewalk wherever the lot meets a city street, open at every drive and footpath; a pylon
//     sign beside the main car entrance; bins by it;
//   • BACK OF HOUSE: the ground behind and beside the rows is the shops' back side, not a park: dry ground (Lawn), a loose line of
//     trees along the property line only, dumpsters behind the deep shops (ServiceYards). It was a forest (Buffer: a tree every
//     9 m over the whole piece; game run show-5-builds-1028-220502).

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Plaza;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public sealed partial class Mall
    {
        /// <summary>The leftovers are the shops' back side (Buffer plants a property-line row, not a grove); ServiceYards without Designed.</summary>
        public bool BackOfHouse;

        /// <summary>(m, &gt; 0) The lots' planted edge instead of the malls' 1 m: room for the street front's hedge and sign.</summary>
        public float LotSetback;

        /// <summary>The vanilla pylon signs (only branded variants exist; the brand is the game's own).</summary>
        public static readonly AssetRef Pylon = new AssetRef("", "GasStationPylon01 - PeacentPlanks", "GasStationPylon02 - SpinningSpool", "GasStationPylon03 - PeacentPlanks",
            "GasStationPylon01 - SpinningSpool", "GasStationPylon02 - PeacentPlanks", "GasStationPylon03 - SpinningSpool");

        /// <summary>Back of house: trees only along the site's own boundary (12-18 m apart, some gaps), a shrub between some of them.</summary>
        private void BackYard(float2[] ring, uint salt)
        {
            var region = LayerRegion.From(ring);
            if (region == null) return;
            Lawn(ring);
            var rng = Hashing.Rng(Hashing.Mix(Ctx.Seed, salt), "backyard");
            var placed = new List<float2>();
            var pts = Poly.EnsureCcw(ring);
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Length];
                float len = math.distance(a, b);
                if (len < 6f) continue;
                var dir = (b - a) / len; var inward = new float2(-dir.y, dir.x);
                for (float u = rng.NextFloat(3f, 8f); u < len - 3f; u += rng.NextFloat(12f, 18f))
                {
                    var p = a + dir * u + inward * 3.4f;
                    // on the site's own boundary (the property line), never beside a shop's back or a lane
                    if (Region.BoundaryDistance(p, 8f) > 4.5f || region.BoundaryDistance(p, 4f) < 2.6f) continue;
                    bool near = false;
                    foreach (var q in placed) if (math.distancesq(q, p) < 100f) { near = true; break; }
                    if (near) continue;
                    float roll = rng.NextFloat();
                    if (roll < 0.62f)
                    {
                        placed.Add(p);
                        Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Tree, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + (uint)placed.Count), 1 + (int)(rng.NextUInt() % 3));
                    }
                    else if (roll < 0.85f) Ctx.Object(p, rng.NextFloat(0f, 6.283f), ObjectRole.Bush, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + 0x300u + (uint)placed.Count));
                }
            }
            Take(ring);
        }

        /// <summary>
        /// The street front of the lots: a low hedge along every stretch of the outline where a lot meets a city street (0.9 m in,
        /// never on the asphalt: <see cref="LotSetback"/>), open 7 m either side of every entrance; a pylon sign beside the first car entrance that comes into
        /// a lot, two bins at its foot. Returns the hedge metres.
        /// </summary>
        public float StreetFront(bool hedge, bool pylon, uint salt)
        {
            if (Lots.Count == 0) return 0f;
            var roads = _roads ?? (_roads = StreetFrontage.Roads(Ctx.Input));
            var reserved = FrontReserved();
            var gaps = new List<float2>();
            if (Gates != null) foreach (var g in Gates) gaps.Add(g.At);
            // ── the sign ──
            if (pylon && Gates != null)
                foreach (var g in Gates)
                {
                    if (!g.Cars || g.Landed != "lot") continue;
                    var side = new float2(-g.In.y, g.In.x);
                    bool done = false;
                    foreach (var (sgn, off) in new[] { (1f, 8.5f), (-1f, 8.5f), (1f, 11.5f), (-1f, 11.5f), (1f, 14.5f), (-1f, 14.5f) })
                    {
                        var p = g.At + g.In * 1.3f + side * sgn * off;
                        if (!Region.Contains(p) || OnAsphalt(p, 0.5f) || reserved.Blocks(p, 1.5f) || InShop(p, 2f)) continue;
                        // facing the street, across the traffic (its faces read from both ways along the street)
                        Ctx.Object(p, math.atan2(side.x, side.y), ObjectRole.Furniture, Pylon, Hashing.Mix(Ctx.Seed, salt + 0x10u), -1, "commercial.sign");
                        reserved.AddDisc(p, 1.6f);
                        gaps.Add(p);
                        var bin = p + side * sgn * 2.4f;
                        if (Region.Contains(bin) && !OnAsphalt(bin, 0.4f)) Ctx.Object(bin, 0f, ObjectRole.Bin, new AssetRef(""), Hashing.Mix(Ctx.Seed, salt + 0x11u));
                        Ctx.Plan.Facts["pylon"] = 1;
                        done = true;
                        break;
                    }
                    if (done) break;
                }
            if (!hedge) return 0f;
            // ── the hedge ──
            float metres = 0f;
            var ring = Poly.EnsureCcw(Region.Ring);
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Length];
                float len = math.distance(a, b);
                if (len < 6f) continue;
                var dir = (b - a) / len; var inward = new float2(-dir.y, dir.x);
                bool street = false;
                var mid = math.lerp(a, b, 0.5f);
                foreach (var rd in roads)
                    if (math.abs(math.dot(math.normalizesafe(rd.B - rd.A), dir)) > 0.9f && math.sqrt(SegmentIndex.DistSq(mid, rd.A, rd.B)) < rd.HalfWidth + 12f) { street = true; break; }
                if (!street) continue;
                float runA = float.NaN;
                for (float u = 2f; u <= len - 2f + 0.01f; u += 1f)
                {
                    var p = a + dir * u + inward * 1.1f;
                    bool ok = u <= len - 2.5f && BesideLot(p) && !OnAsphalt(p, 0.4f) && !reserved.Blocks(p, 0.6f) && !InShop(p, 1f);
                    if (ok) foreach (var gp in gaps) if (math.distancesq(gp, p) < 49f) { ok = false; break; }
                    if (ok && float.IsNaN(runA)) runA = u;
                    if (!ok && !float.IsNaN(runA))
                    {
                        if (u - 1f - runA >= 4f)
                        {
                            GroundCover.HedgeRow(Ctx, a + dir * runA + inward * 1.1f, a + dir * (u - 1f) + inward * 1.1f, PlazaArt.LowHedge, AssetCategories.Hedge, ObjectRole.Bush, Hashing.Mix(Ctx.Seed, salt + 0x20u + (uint)i));
                            metres += u - 1f - runA;
                        }
                        runA = float.NaN;
                    }
                }
            }
            if (metres > 0f) Ctx.Plan.Facts["frontHedge"] = math.round(metres);
            return metres;
        }

        /// <summary>The plan's nets and objects (not its lot areas, which cover the planted edge too: Composer.Reserve).</summary>
        private ReservedSpace FrontReserved()
        {
            var r = new ReservedSpace();
            foreach (var n in Ctx.Plan.Nets)
            {
                float hw = Composer.DefaultHalfWidth(n.Role);
                r.AddCorridor(n.A.xz, n.B.xz, hw, n.Role.ToString());
                r.AddCorridor(n.B.xz, n.C.xz, hw, n.Role.ToString());
                r.AddCorridor(n.C.xz, n.D.xz, hw, n.Role.ToString());
            }
            foreach (var o in Ctx.Plan.Objects)
                if (o.Role == ObjectRole.Tree || o.Role == ObjectRole.Lamp || o.Role == ObjectRole.Furniture || o.Role == ObjectRole.Bin) r.AddDisc(o.Position.xz, o.Role == ObjectRole.Tree ? 1.2f : 0.5f, o.Role.ToString());
            return r;
        }

        private bool BesideLot(float2 p)
        {
            foreach (var l in Lots) if (Poly.Contains(l, p) || Poly.DistanceToBoundary(l, p) < 3f) return true;
            return false;
        }

        private bool InShop(float2 p, float margin)
        {
            foreach (var s in Shops) if (Poly.Contains(s, p) || Poly.DistanceToBoundary(s, p) < margin) return true;
            return false;
        }

        /// <summary>Within <paramref name="margin"/> of the plan's asphalt (the lots' surfaces: stalls and aisles).</summary>
        private bool OnAsphalt(float2 p, float margin)
        {
            foreach (var s in Ctx.Plan.Surfaces)
            {
                if (s.Role != SurfaceRole.Asphalt || s.Ring == null || s.Ring.Length < 3) continue;
                var r2 = Poly.To2D(s.Ring);
                if (Poly.Contains(r2, p) || Poly.DistanceToBoundary(r2, p) < margin) return true;
            }
            return false;
        }
    }
}

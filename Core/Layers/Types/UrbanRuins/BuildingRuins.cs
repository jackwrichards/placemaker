// Placemaker — urban ruins, "Building ruins": what is left of a building that came down years ago (docs/runbooks/rundown.md).
// GPL-3.0.
//
// The footprint's floor slab, broken; the rooms' walls standing in stretches (half walls, a taller piece at a corner,
// column stumps where the frame was); one part of the floor gone into the basement, a pit with old rainwater standing in it;
// the rubble heaped inside the rooms and spilt out of the door gaps; weeds in every crack and young trees inside the walls.
// The user, 2026-10-29: "urban ruins (foundations, half walls, a basement pit with standing water)".

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.Ruins;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.UrbanRuins
{
    internal static class BuildingRuins
    {
        internal sealed class Result
        {
            public OrientedRect Footprint;
            public DecayKit.Slab Floor;
            public float2[] Pit;
            public readonly List<(float2 a, float2 b)> Walls = new List<(float2, float2)>();
            public readonly List<float2> Rubble = new List<float2>();
            public int WallPieces, Stumps;
        }

        // Vanilla walls: tall garden walls for the surviving taller stretches, low walls for the half walls.
        // (lab-rundown-walls: of the game's garden walls only 01, 05 and 08 are dark plain masonry; 03 is a white balustrade)
        public static readonly string[] Tall = { "EU_Wall01", "EU_Wall05", "EU_Wall08" };
        public static readonly string[] Low = { "EU_WallLow01", "EU_WallLow05", "EU_WallLow08" };
        private static readonly string[] Stumps = { "RubblePylonLarge01", "RubblePylonLarge02", "RubblePylonLarge03", "RubblePylonLarge04" };
        private static readonly string[] Heaps = { "RubblePileConcrete01", "RubblePileConcrete02", "RubblePileConcrete03", "RubblePileMixed01", "RubblePileMixed03", "RubblePileMixed04" };

        public static Result Plan(LayerContext ctx, LayerRegion region, Occupancy occ, Zones all, Zones keep, float2 centre, float2 axis, float major, float minor, float decay)
        {
            var res = new Result();
            // the building: about 60 % of the plot's span, 12-60 m long, rooms of 6-9 m
            float hl = math.clamp(major * 0.32f, 6f, 30f), hw = math.clamp(minor * 0.3f, 5f, 18f);
            if (!DecayKit.FitRect(region, occ, centre, axis, hl, hw, 3f, out var fp, 6f, 4.5f)) return null;
            res.Footprint = fp;
            all.Add(fp.Ring);
            uint seed = Hashing.Mix(ctx.Seed, 0xB01Du);
            res.Floor = DecayKit.BrokenSlab(ctx, fp, math.saturate(0.45f + 0.5f * decay), SurfaceRole.Paving, AssetMix.One(""), AssetCategories.SurfacePaving, seed);

            // rooms: a grid of 6-9 m cells
            int nu = math.max(1, (int)math.round(fp.HalfLength * 2f / 7.5f)), nv = math.max(1, (int)math.round(fp.HalfWidth * 2f / 7f));
            float cu = fp.HalfLength * 2f / nu, cv = fp.HalfWidth * 2f / nv;
            // the basement: one or two cells at the end farther from the street fell in
            int pu = Hashing.Unit(Hashing.Mix(seed, 1u)) < 0.5f ? 0 : nu - 1;
            int pw = math.min(nu, math.max(1, nu / 3));
            int pu0 = pu == 0 ? 0 : nu - pw;
            var pit = new OrientedRect
            {
                Center = fp.Local(-fp.HalfLength + (pu0 + pw * 0.5f) * cu, 0f), Axis = fp.Axis,
                HalfLength = pw * cu * 0.5f - 1.2f, HalfWidth = fp.HalfWidth - 1.2f,
            };
            if (pit.HalfLength > 2f && pit.HalfWidth > 2f)
            {
                res.Pit = pit.Ring;
                float baseH = ctx.H(pit.Center);
                foreach (var q in pit.Ring) baseH = math.min(baseH, ctx.H(q));
                ctx.Plan.Facts["intentionalPit"] = 1;   // dug on purpose: the pit guard must leave it
                // two steps: the floor 2.6 m down, the rim softened at 1.3 m
                var outer = new OrientedRect { Center = pit.Center, Axis = pit.Axis, HalfLength = pit.HalfLength + 0.6f, HalfWidth = pit.HalfWidth + 0.6f };
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(outer.Ring, ctx.H), Kind = TerraformKind.Lower, TargetHeight = baseH - 1.3f, Permanent = true });
                ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = Poly.To3D(pit.Ring, ctx.H), Kind = TerraformKind.Lower, TargetHeight = baseH - 2.6f, Permanent = true });
                DirtJumps.WarnPermanent(ctx);
                keep.Add(outer.Ring);
                occ.TakeCorridor(pit.Local(-pit.HalfLength, 0f), pit.Local(pit.HalfLength, 0f), pit.HalfWidth + 0.6f, 50);
            }

            // walls along the outline and the room lines, in stretches; door gaps; taller pieces near the corners
            var lines = new List<(float2 a, float2 b, bool outer)>();
            var ring = fp.Ring;
            for (int e = 0; e < 4; e++) lines.Add((ring[e], ring[(e + 1) % 4], true));
            for (int i = 1; i < nu; i++) lines.Add((fp.Local(-fp.HalfLength + i * cu, -fp.HalfWidth), fp.Local(-fp.HalfLength + i * cu, fp.HalfWidth), false));
            for (int j = 1; j < nv; j++) lines.Add((fp.Local(-fp.HalfLength, -fp.HalfWidth + j * cv), fp.Local(fp.HalfLength, -fp.HalfWidth + j * cv), false));
            float survive = math.lerp(0.75f, 0.35f, decay);
            int li = 0;
            foreach (var (a, b, outer) in lines)
            {
                li++;
                var t = math.normalizesafe(b - a);
                float yaw = math.atan2(t.x, t.y) + math.PI * 0.5f;
                float len = math.distance(a, b);
                for (float s = 1.4f; s < len - 1.2f; s += 2.8f)
                {
                    var p = a + t * s;
                    if (res.Pit != null && Poly.Contains(res.Pit, p)) continue;
                    uint v = Hashing.Mix(seed, (uint)li * 977u + (uint)(s * 10f));
                    float n = ValueNoise.Clumps(p, 7f, Hashing.Mix(seed, 3u));
                    if (n < 1f - survive * (outer ? 1f : 0.7f)) continue;
                    // near a corner the walls stand taller; elsewhere half walls
                    float corner = math.min(s, len - s);
                    bool tall = outer && corner < 5f && Hashing.Unit(Hashing.Mix(v, 1u)) < 0.6f;
                    if (!occ.Free(p, 0.6f, 0.2f, 0f)) continue;
                    var o = ctx.Object(p, yaw, ObjectRole.Furniture, AssetMix.Pick(tall ? Tall : Low, v), v, -1, AssetCategories.RuinsWall);
                    // the garden walls are 0.9 m: a ruin's surviving stretch near a corner stands up to twice that
                    if (tall) o.Scale = 1.4f + 0.6f * Hashing.Unit(Hashing.Mix(v, 2u));
                    occ.Take(p, 0.8f, 2);
                    res.WallPieces++;
                    res.Walls.Add((p - t * 1.4f, p + t * 1.4f));
                }
            }
            // column stumps where the frame was: at the room corners that are not in the pit
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    uint v = Hashing.Mix(seed, (uint)(i * 31 + j * 7 + 500));
                    if (Hashing.Unit(v) > 0.45f) continue;
                    var p = fp.Local(-fp.HalfLength + i * cu, -fp.HalfWidth + j * cv);
                    if (res.Pit != null && Poly.Contains(res.Pit, p)) continue;
                    if (!occ.Free(p, 0.9f, 0.2f, 0f)) continue;
                    var o = ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 1u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(Stumps, v), v, -1, AssetCategories.RuinsPillar);
                    o.Scale = 0.55f + 0.35f * Hashing.Unit(Hashing.Mix(v, 2u));
                    occ.Take(p, 1f, 2);
                    res.Stumps++;
                }
            // rubble heaped inside the rooms and spilt out through the gaps
            int heaps = (int)math.round(nu * nv * (0.5f + decay * 0.6f));
            for (int k = 0, tries = 0; k < heaps && tries < heaps * 6; tries++)
            {
                uint v = Hashing.Mix(seed, 0x4EA9u + (uint)tries);
                var p = fp.Local((Hashing.Unit(v) - 0.5f) * fp.HalfLength * 2.2f, (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * fp.HalfWidth * 2.2f);
                if (!region.ContainsDisc(p, 3f) || (res.Pit != null && Poly.Contains(res.Pit, p))) continue;
                if (!LeisureKit.Put(ctx, occ, p, Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, ObjectRole.Furniture, AssetMix.Pick(Heaps, v), AssetCategories.RuinsRubble, 3.2f, v, 0.2f, 0.1f)) continue;
                res.Rubble.Add(p);
                k++;
            }
            return res;
        }
    }
}

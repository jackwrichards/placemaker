// The open-air theatre (the plazas makeover, 2026-10-29, docs/runbooks/plazas.md). The user graded the amphitheatre F:
// "it just looks horrible" (a fan of loose benches on beige ground in square terrain cells, a small stage in a big lawn).
// This is the amphitheatre rebuilt on the designed squares' kit, as a real stone theatre:
//
//   cavea      a half bowl of stone seating steps rising from the orchestra (a smooth cone of sloped terraform quads, one
//              per 6 degrees, never cells), every tier a stone lip (a sunk wall showing 0.3 m), radial stairs up it, a
//              bank back down to the ground behind the top tier, a railed promenade along the top with lamps
//   orchestra  a paved round at the foot, ringed in dark stone
//   stage      a paved stage with the game's stage on it, a stone back wall with tall flags, poplars behind
//   setting    a lawn round it, trees ringing the bowl, two paved walks (the parodoi) into the orchestra from the sides,
//              benches under the trees looking at the stage
// On a slope the bowl faces up the hill (the seats climb it, as the Greeks built them). Its ground is the design: the
// site harmony pass leaves it alone.

using System;
using System.Collections.Generic;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Plaza
{
    internal static partial class PlazaDesigns
    {
        private static readonly string[] Poplars = { "EU_PoplarTree01", "NA_LindenTree01" };
        private const float TierDepth = 1.15f, TierRise = 0.42f;

        private static bool InRect(OrientedRect r, float2 p)
        {
            var d = p - r.Center;
            return math.abs(math.dot(d, r.Axis)) <= r.HalfLength && math.abs(math.dot(d, r.Side)) <= r.HalfWidth;
        }

        private static void Theatre(Kit k)
        {
            float fw = k.Fw;
            k.WallId = "EU_Wall03";
            k.Lay(PFrame, PavingShapes.Frame(k.Region.Ring, fw), k.P.Band);
            k.Lay(PPromenade, PavingShapes.Frame(k.Region.Ring, fw + 3.5f), k.P.Path);
            // on a slope the bowl faces down it (the seats climb the hill): turn the frame so A runs up or down the fall
            {
                float hA = math.abs(k.Ctx.H(k.W(k.EA * 0.6f, 0f)) - k.Ctx.H(k.W(-k.EA * 0.6f, 0f))) / math.max(1f, k.EA);
                float hS = math.abs(k.Ctx.H(k.W(0f, k.EB * 0.6f)) - k.Ctx.H(k.W(0f, -k.EB * 0.6f))) / math.max(1f, k.EB);
                if (hS > hA + 0.01f) k.TurnQuarter();
            }
            k.FaceUphill();
            float ea = k.EA, eb = k.EB;
            if (ea < 14f || eb < 14f) { Piazza(k); return; }
            // The long side of the box carries the bowl's width.
            float stageDepth = math.clamp(ea * 0.22f, 7f, 12f);
            float R = 0f, bank = 2f, slope = TierRise / TierDepth;
            for (int it = 0; it < 4; it++)
            {
                R = math.min(2f * ea - stageDepth - 6f, eb - 5f) - bank;
                float h = math.max(0f, R - math.clamp(R * 0.26f, 5f, 11f)) * slope;
                if (h > 6f) slope = 6f / math.max(1f, R - math.clamp(R * 0.26f, 5f, 11f));
                bank = math.max(2f, h / 0.42f);
            }
            if (R < 14f) { Piazza(k); return; }
            float r0 = math.clamp(R * 0.26f, 5f, 11f);
            int rows = (int)math.floor((R - r0) / TierDepth);
            R = r0 + rows * TierDepth;
            float top = rows * TierDepth * slope;
            // the whole piece (stage, bowl, bank and a tree's room) centred along the box
            float len = stageDepth + R + bank + 5f;
            float s0 = -math.min(ea, len * 0.5f + 2f);
            float uF = s0 + stageDepth;
            var F = k.W(uF, 0f);
            float hf = k.Ctx.H(F);
            const float ext = 0.22f;   // the bowl's ends fade back to the ground over ~13 degrees
            float2 Dir(float a) => k.A * math.cos(a) + k.S * math.sin(a);
            float2 P(float r, float a) => F + Dir(a) * r;
            float Fade(float a) => 1f - math.saturate((math.abs(a) - math.PI * 0.5f) / ext);
            float Cone(float r, float a) => hf + (math.clamp(r, r0, R) - r0) * slope * Fade(a);
            float Ground(float2 p)
            {
                var d = p - F;
                float r = math.length(d);
                float a = math.atan2(math.dot(d, k.S), math.dot(d, k.A));
                if (r <= r0 + 0.01f) return hf;
                if (math.abs(a) > math.PI * 0.5f + ext) return float.NaN;
                if (r <= R) return Cone(r, a);
                if (r <= R + bank) return math.lerp(Cone(R, a), k.Ctx.H(p), (r - R) / bank);
                return float.NaN;
            }
            // ── the ground: an orchestra disc, the cone in 6-degree quads, the bank behind it, the stage floor ──
            float sv = r0 + 4f;
            var stage = k.Box(s0 + 1.2f, uF - r0 * 0.15f, -sv, sv);
            int patches = 0;
            void Patch(float3[] ring, float target)
            {
                var flat = Poly.To2D(ring);
                // a patch that reaches past the outline (the bowl's ends on an L or a narrow site) is cut to it; the cut's new
                // corners take the design's own height there
                bool inside = true;
                foreach (var q in flat) if (!k.Region.Contains(q)) { inside = false; break; }
                if (!inside)
                {
                    var parts = Poly.Intersection(flat, k.Region.Ring);
                    if (parts.Count == 0 || parts[0].Outer == null || parts[0].Outer.Length < 3) return;
                    var cut = parts[0].Outer;
                    var nr = new float3[cut.Length];
                    for (int i = 0; i < cut.Length; i++)
                    {
                        float h = InRect(stage, cut[i]) ? hf : Ground(cut[i]);
                        nr[i] = new float3(cut[i].x, math.isfinite(h) ? h : k.Ctx.H(cut[i]), cut[i].y);
                    }
                    ring = nr; flat = cut;
                }
                if (Poly.SignedArea(flat) < 0f) Array.Reverse(ring);
                k.Ctx.Plan.Terraform.Add(new PlannedTerraform { Ring = ring, Kind = target >= k.Ctx.H(Poly.Centroid(Poly.To2D(ring))) ? TerraformKind.Raise : TerraformKind.Lower, TargetHeight = target, Permanent = true, Sloped = true });
                patches++;
            }
            float3 V3(float2 p, float h) => new float3(p.x, h, p.y);
            var disc = new List<float3>();
            for (int i = 0; i < 24; i++) disc.Add(V3(F + Dir(i * math.PI / 12f) * (r0 + 0.2f), hf));
            Patch(disc.ToArray(), hf);
            float step = math.radians(6f);
            for (float a = -math.PI * 0.5f - ext; a < math.PI * 0.5f + ext - 1e-3f; a += step)
            {
                float a1 = math.min(a + step, math.PI * 0.5f + ext);
                Patch(new[] { V3(P(r0, a), Cone(r0, a)), V3(P(r0, a1), Cone(r0, a1)), V3(P(R, a1), Cone(R, a1)), V3(P(R, a), Cone(R, a)) }, Cone((r0 + R) * 0.5f, (a + a1) * 0.5f));
                var o0 = P(R + bank, a); var o1 = P(R + bank, a1);
                Patch(new[] { V3(P(R, a), Cone(R, a)), V3(P(R, a1), Cone(R, a1)), V3(o1, k.Ctx.H(o1)) }, Cone(R, a));
                Patch(new[] { V3(P(R, a), Cone(R, a)), V3(o1, k.Ctx.H(o1)), V3(o0, k.Ctx.H(o0)) }, k.Ctx.H(o0));
            }
            Patch(Poly.To3D(stage.Ring, p => hf), hf);
            k.Ctx.Ground.Override(k.Region.Min, k.Region.Max, p => InRect(stage, p) ? hf : Ground(p));
            k.Ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
            k.Ctx.Plan.Facts[Placemaker.Core.Build.SiteHarmony.OwnGroundFact] = 1;
            k.Ctx.Plan.Facts["designTerrainPatches"] = patches;
            k.Ctx.Plan.Facts["theatreRows"] = rows;
            k.Ctx.Plan.Facts["theatreRise"] = (float)Math.Round(top, 2);

            // ── paving: the cavea in stone, a dark band under every tier, the orchestra round, the stage ──
            var cavea = new List<float2>();
            for (float a = -math.PI * 0.5f; a <= math.PI * 0.5f + 1e-3f; a += step) cavea.Add(P(R + 0.4f, a));
            for (float a = math.PI * 0.5f; a >= -math.PI * 0.5f - 1e-3f; a -= step) cavea.Add(P(r0, a));
            k.Lay(PFeature + 2, Poly.EnsureCcw(cavea.ToArray()), k.P.Accent);
            k.Lay(PFeature - 2, PavingShapes.Polygon(F, r0 - 0.9f, 32, k.Phase), k.P.Focal);
            k.Lay(PFeature - 3, PavingShapes.Annulus(F, r0 - 0.1f, 0.8f, 32), k.P.Band);
            k.Lay(PFeature - 1, stage.Ring, k.P.Focal);
            k.Lay(PKerb, PavingShapes.Frame(stage.Ring, 0.6f), k.P.Band);
            // the lawn round it all, the parodoi from both sides into the orchestra
            foreach (float s in new[] { -1f, 1f })
                k.Lay(PFeature + 6, PavingShapes.Band(k.W(uF, s * (r0 + 0.5f)), k.W(uF, s * (eb + 1f)), 3.6f), k.P.Path);
            k.Lay(PGreen + 4, k.Box(-ea, ea, -eb, eb).Ring, k.Grass, SurfaceRole.Grass, AssetCategories.SurfaceGrass);

            // ── radial stairs and the stone lips of the tiers ──
            int aisles = R > 26f ? 5 : 4;
            var aisleAngles = new List<float>();
            for (int i = 0; i < aisles; i++) aisleAngles.Add(-math.PI * 0.5f + math.PI * (i + 0.5f) / aisles);
            foreach (float a in aisleAngles) k.Flight(P(r0 + 0.3f, a), P(R - 0.2f, a), 1.5f, false, false, k.P.Path);
            for (int t = 1; t <= rows; t++)
            {
                float r = r0 + t * TierDepth;
                // lip pieces about 3 m of arc, left out where an aisle climbs
                float da = math.clamp(5f / r, 0.04f, 0.4f);
                for (float a = -math.PI * 0.5f; a < math.PI * 0.5f - 1e-3f; a += da)
                {
                    float a1 = math.min(a + da, math.PI * 0.5f);
                    float mid = (a + a1) * 0.5f;
                    bool hitsAisle = false;
                    foreach (float aa in aisleAngles) if (math.abs(mid - aa) * r < 1.6f) { hitsAisle = true; break; }
                    if (hitsAisle) continue;
                    k.Lay(PFeature - 4, PavingShapes.Band(P(r - 0.1f, a), P(r - 0.1f, a1), 0.42f), k.P.Band);
                    k.Wall(P(r, a), P(r, a1), 1, null, WallH - 0.3f, false);
                }
            }
            // the stone ends of the bowl: low cheek walls along both ends
            foreach (float s in new[] { -1f, 1f }) k.Wall(P(r0 + 0.5f, s * math.PI * 0.5f) + k.A * 0.15f * -1f, P(R, s * math.PI * 0.5f) - k.A * 0.15f, 1, null, 0.3f, false);
            var taken = new List<float2>();
            for (float a = -math.PI * 0.5f - ext; a <= math.PI * 0.5f + ext + 1e-3f; a += step) taken.Add(P(R + bank, a));
            k.Occ.TakePolygon(taken.ToArray());
            k.Occ.TakePolygon(stage.Ring);

            // ── the top: a railing along the top tier, lamps, trees ringing the bowl ──
            for (float a = -math.PI * 0.5f; a < math.PI * 0.5f - 1e-3f; a += math.clamp(4.6f / R, 0.05f, 0.3f))
            {
                float a1 = math.min(a + math.clamp(4.6f / R, 0.05f, 0.3f), math.PI * 0.5f);
                bool gap = false;
                foreach (float aa in aisleAngles) if (math.abs((a + a1) * 0.5f - aa) * R < 1.4f) { gap = true; break; }
                if (!gap) k.Railing(P(R + 0.9f, a), P(R + 0.9f, a1));
            }
            for (float a = -math.PI * 0.5f + 0.2f; a < math.PI * 0.5f; a += math.clamp(14f / R, 0.12f, 0.6f))
                k.Ctx.Object(P(R + 1.8f, a), 0f, ObjectRole.Lamp, k.Lamp, k.V(0x7EA1u), -1, null);
            float tr = R + bank + 3.5f;
            for (float a = -math.PI * 0.5f - 0.3f; a <= math.PI * 0.5f + 0.3f; a += math.clamp(9f / tr, 0.1f, 0.5f))
            {
                var p = P(tr, a);
                if (k.Region.ContainsDisc(p, k.Fw + 3.5f)) k.Tree(p, false, PlazaArt.GardenTrees);
            }

            // ── the stage: the game's stage, a stone back wall, tall flags, poplars behind ──
            var sc = k.W((s0 + 1.2f + uF - r0 * 0.15f) * 0.5f, 0f);
            float yaw = math.atan2(k.A.x, k.A.y);
            k.Ctx.Object(sc, yaw, ObjectRole.Furniture, new AssetRef(k.Ctx.S("stageAsset") is string st && st.Length > 0 ? st : "StageSmall01", "StageSmall01"), k.V(0x57A6u), -1, AssetCategories.Stage);
            k.Occ.Take(sc, 4.5f, 0);
            var wa = k.W(s0 + 1.6f, -sv + 0.5f); var wb = k.W(s0 + 1.6f, sv - 0.5f);
            k.Wall(wa, wb, 3);
            // a colonnade along the back of the stage (a pergola row), the skene of a real theatre
            k.Canopy(k.W(s0 + 1.9f, sv - 1f), k.W(s0 + 1.9f, -sv + 1f), 1, "PergolaModular07", false);
            foreach (float s in new[] { -1f, 1f }) k.BigFlag(k.W(s0 + 2.6f, s * (sv - 1.2f)), true);
            foreach (var p in LeisureKit.Along(k.W(s0 - 1f, -sv), k.W(s0 - 1f, sv), 4.5f))
                if (k.Region.ContainsDisc(p, 2f)) k.Tree(p, false, Poplars, 1.2f);

            // ── benches under the trees on the lawn, looking at the stage ──
            foreach (float s in new[] { -1f, 1f })
                for (float u = s0 + 3f; u < uF - 2f; u += 5f)
                    k.Seat(k.W(u, s * (sv + 3.2f)), -k.S * s);
            if (s0 + ea > 10f)
            {
                k.Lay(PFeature + 7, k.Box(-ea, s0 - 0.5f, -sv - 4f, sv + 4f).Ring, k.P.Path);
                foreach (var p in LeisureKit.Along(k.W(s0 - 5f, -sv - 2f), k.W(s0 - 5f, sv + 2f), 8f)) k.Tree(p, true, PlazaArt.PlaneTrees);
            }
            // the lawn's far side: trees in loose groups with benches
            for (float u = uF + R + bank + 4f; u < ea - 2f; u += 9f)
                for (float v = -eb + 4f; v < eb - 3f; v += 10f)
                    if (Hashing.Unit(k.V(0x7EA2u)) < 0.55f) k.Tree(k.W(u, v + (Hashing.Unit(k.V(0x7EA3u)) - 0.5f) * 4f), false, PlazaArt.GardenTrees);
            k.Core = r0;
            k.Ctx.Plan.Facts["theatreRadius"] = (float)Math.Round(R, 1);
        }
    }
}

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
            // (the user, 2026-10-30: "doesn't look quite right") solid stone everywhere: EU_Wall03 draws as a see-through
            // balustrade, so the tiers read as rows of railings
            k.WallId = "EU_Wall01";
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
                // (round 3, the lead: "a huge flat white fan") the bowl takes about 70 % of the box's width, at most 34 m
                R = math.min(math.min(2f * ea - stageDepth - 14f, eb * 0.72f) - bank, ea * eb > 5000f ? 40f : 34f);   // up to 80 m across on a big site
                float h = math.max(0f, R - math.clamp(R * 0.32f, 7f, 12f)) * slope;
                if (h > 6f) slope = 6f / math.max(1f, R - math.clamp(R * 0.32f, 7f, 12f));
                bank = math.max(2f, h / 0.42f);
            }
            if (R < 14f) { Piazza(k); return; }
            float r0 = math.clamp(R * 0.32f, 7f, 12f);
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
            float sv = r0 + 7f;
            const float StageRise = WallH;
            float sf = uF - r0 * 0.55f;   // the stage front: a chord across the back of the orchestra
            var stage = k.Box(s0 + 1.2f, sf, -sv, sv);
            float hs = hf + StageRise;
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
                        float h = InRect(stage, cut[i]) ? hs : Ground(cut[i]);
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
            for (int i = 0; i < 24; i++)
            {
                var q = F + Dir(i * math.PI / 12f) * (r0 + 0.2f);
                float du = math.dot(q - F, k.A);
                if (du < sf - uF) q += k.A * (sf - uF - du);
                disc.Add(V3(q, hf));
            }
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
            Patch(Poly.To3D(stage.Ring, p => hs), hs);
            // two ramps of steps up to the stage at its front corners (sloped quads cut into the platform's edge)
            var stairs = new List<(float2 foot, float2 head)>();
            foreach (float s in new[] { -1f, 1f })
            {
                float v = s * (sv - 2.2f);
                var foot = k.W(sf + 2.6f, v); var head = k.W(sf - 0.2f, v);
                stairs.Add((foot, head));
                var nn = k.S * 1.3f;
                Patch(new[] { V3(head - nn, hs), V3(head + nn, hs), V3(foot + nn, hf), V3(foot - nn, hf) }, (hf + hs) * 0.5f);
            }
            bool OnStair(float2 p, out float h)
            {
                h = 0f;
                foreach (var (foot, head) in stairs)
                {
                    var d = math.normalizesafe(foot - head); float l = math.distance(foot, head);
                    float t = math.dot(p - head, d), w = math.abs(math.dot(p - head, new float2(-d.y, d.x)));
                    if (t >= 0f && t <= l && w <= 1.3f) { h = math.lerp(hs, hf, t / l); return true; }
                }
                return false;
            }
            k.Ctx.Ground.Override(k.Region.Min, k.Region.Max, p => OnStair(p, out var hh) ? hh : InRect(stage, p) ? hs : Ground(p));
            k.Ctx.Issue(IssueSeverity.Warning, TerrainShaper.PermanentKey);
            k.Ctx.Plan.Facts[Placemaker.Core.Build.SiteHarmony.OwnGroundFact] = 1;
            k.Ctx.Plan.Facts["designTerrainPatches"] = patches;
            k.Ctx.Plan.Facts["theatreRows"] = rows;
            k.Ctx.Plan.Facts["theatreRise"] = (float)Math.Round(top, 2);
            k.Ctx.Plan.Facts["theatreFx"] = F.x; k.Ctx.Plan.Facts["theatreFz"] = F.y;

            // ── paving: the cavea in stone, a dark band under every tier, the orchestra round, the stage ──
            var cavea = new List<float2>();
            for (float a = -math.PI * 0.5f; a <= math.PI * 0.5f + 1e-3f; a += step) cavea.Add(P(R + 0.4f, a));
            for (float a = math.PI * 0.5f; a >= -math.PI * 0.5f - 1e-3f; a -= step) cavea.Add(P(r0, a));
            k.Lay(PFeature + 2, Poly.EnsureCcw(cavea.ToArray()), k.P.Accent);
            // every tier a full ring of stone, alternate tiers a shade darker: continuous stone tiers that read from 45 degrees
            for (int t = 0; t < rows; t++)
            {
                if ((t & 1) == 0) continue;
                float ra = r0 + t * TierDepth, rb = ra + TierDepth;
                var ring = new List<float2>();
                for (float a = -math.PI * 0.5f; a <= math.PI * 0.5f + 1e-3f; a += step * 0.5f) ring.Add(P(rb, a));
                for (float a = math.PI * 0.5f; a >= -math.PI * 0.5f - 1e-3f; a -= step * 0.5f) ring.Add(P(ra, a));
                k.Lay(PFeature + 1, Poly.EnsureCcw(ring.ToArray()), k.P.Path);
            }
            k.Lay(PFeature - 2, PavingShapes.Polygon(F, r0 - 0.9f, 32, k.Phase), k.P.Focal);
            k.Lay(PFeature - 3, PavingShapes.Annulus(F, r0 - 0.1f, 0.8f, 32), k.P.Band);
            k.Lay(PFeature - 4, PavingShapes.Annulus(F, r0 * 0.55f, 0.6f, 32), k.P.Band);
            k.Lay(PFeature - 5, PavingShapes.Polygon(F, 1.6f, 16, k.Phase), k.P.Inlay);
            k.Occ.Take(F, r0 - 0.5f, 0);   // the orchestra stays open
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
            var bounds = new List<float> { -math.PI * 0.5f };
            bounds.AddRange(aisleAngles);
            bounds.Add(math.PI * 0.5f);
            int benches = 0;
            for (int t = 1; t <= rows; t++)
            {
                float r = r0 + t * TierDepth;
                float gap = 1.05f / r;   // half the aisle (1.5 m) plus a hand's breadth, as an angle at this tier
                for (int sgm = 0; sgm + 1 < bounds.Count; sgm++)
                {
                    float a0 = bounds[sgm] + (sgm == 0 ? 0f : gap), a1 = bounds[sgm + 1] - (sgm + 1 == bounds.Count - 1 ? 0f : gap);
                    if (a1 - a0 < 0.01f) continue;
                    int pieces = math.max(1, (int)math.round((a1 - a0) * r / 5f));
                    for (int i = 0; i < pieces; i++)
                    {
                        float b0 = math.lerp(a0, a1, i / (float)pieces), b1 = math.lerp(a0, a1, (i + 1) / (float)pieces);
                        // the riser: a stone lip showing the tier's rise
                        k.Wall(P(r, b0), P(r, b1), 1, null, WallH - TierRise, false);
                    }
                    // stone benches on every third tier, facing the stage
                    if (t % 3 == 2)
                        for (float a = a0 + 1.4f / r; a < a1 - 1.2f / r; a += 3.2f / r)
                        {
                            var q = P(r - TierDepth * 0.45f, a);
                            k.Ctx.Object(q, math.atan2(F.x - q.x, F.y - q.y), ObjectRole.Bench, new AssetRef(PlazaArt.ModernBenches[0], PlazaArt.ModernBenches), k.V(0xBE7Au), -1, AssetCategories.Bench);
                            benches++;
                        }
                }
            }
            k.Ctx.Plan.Facts["theatreBenches"] = benches;
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
            // the stage's stone faces: the front wall (left open at the stairs) and the two sides, one course to the platform's top
            {
                var gaps = new List<(float at, float half)>();
                foreach (var (foot, head) in stairs) gaps.Add((math.dot(head - k.W(sf + 0.15f, -sv), k.S), 1.5f));
                k.WallWithGaps(k.W(sf + 0.15f, -sv), k.W(sf + 0.15f, sv), gaps, 1);
                foreach (float s in new[] { -1f, 1f }) k.Wall(k.W(s0 + 1.2f, s * (sv + 0.15f)), k.W(sf, s * (sv + 0.15f)), 1);
                foreach (var (foot, head) in stairs) k.Flight(foot, head, 2.4f, true, false, k.P.Path);
                foreach (float s in new[] { -1f, 1f }) k.Light(k.W(sf + 1.2f, s * (sv - 4.6f)));
            }
            var sc = k.W((s0 + 1.2f + sf) * 0.5f + 0.6f, 0f);
            float yaw = math.atan2(k.A.x, k.A.y);
            k.Ctx.Object(sc, yaw, ObjectRole.Furniture, new AssetRef(k.Ctx.S("stageAsset") is string st && st.Length > 0 ? st : "StageSmall01", "StageSmall01"), k.V(0x57A6u), -1, AssetCategories.Stage);
            k.Occ.Take(sc, 4.5f, 0);
            var wa = k.W(s0 + 1.6f, -sv + 0.5f); var wb = k.W(s0 + 1.6f, sv - 0.5f);
            // the skene: a solid stone back wall five courses high (4.5 m) with wings returning toward the orchestra
            k.Wall(wa, wb, 5);
            foreach (float s in new[] { -1f, 1f }) k.Wall(k.W(s0 + 1.6f, s * (sv - 0.5f)), k.W(s0 + 6f, s * (sv - 0.5f)), 4);
            foreach (float s in new[] { -1f, 1f })
            {
                // the skene's towers: a gazebo on a walled base at each end of the stage
                var tc = k.W(s0 + 3f, s * (sv + 3.4f));
                for (int e = 0; e < 4; e++)
                {
                    float an = k.Phase + e * math.PI * 0.5f + math.PI * 0.25f;
                    var q0 = tc + new float2(math.cos(an), math.sin(an)) * 3f; var q1 = tc + new float2(math.cos(an + math.PI * 0.5f), math.sin(an + math.PI * 0.5f)) * 3f;
                    k.Wall(q0, q1, 2, null, 0f, false);
                }
                k.Gazebo(tc, k.Phase, s > 0 ? 1u : 2u, true);
            }
            foreach (float s in new[] { -1f, 1f }) k.BigFlag(k.W(s0 + 2.6f, s * (sv - 1.2f)), true);
            foreach (var p in LeisureKit.Along(k.W(s0 - 1f, -sv), k.W(s0 - 1f, sv), 4.5f))
                if (k.Region.ContainsDisc(p, 2f)) k.Tree(p, false, Poplars, 1.2f);

            // ── benches under the trees on the lawn, looking at the stage ──
            foreach (float s in new[] { -1f, 1f })
                for (float u = s0 + 3f; u < uF - 2f; u += 5f)
                    k.Seat(k.W(u, s * (sv + 3.2f)), -k.S * s);
            if (s0 + ea > 8f)
            {
                // the entrance court behind the stage: an avenue of trees in pits on both sides, a kiosk at each end, benches
                float cu0 = -ea, cu1 = s0 - 0.8f, cvw = math.min(sv + 6f, eb - 2f);
                k.Lay(PFeature + 7, k.Box(cu0, cu1, -cvw, cvw).Ring, k.P.Path);
                k.Lay(PKerb, PavingShapes.Frame(k.Box(cu0, cu1, -cvw, cvw).Ring, 0.8f), k.P.Band);
                k.Lay(PFeature + 5, k.Box(cu0, cu1, -2.4f, 2.4f).Ring, k.P.Band);   // the processional way, in dark stone
                foreach (float s in new[] { -1f, 1f })
                {
                    // a tree avenue either side of the processional way, benches between the trees, kiosks at the outer end
                    for (float u = cu0 + 3f; u <= cu1 - 3f; u += 7f) k.Tree(k.W(u, s * 5.5f), true, PlazaArt.PlaneTrees);
                    if (cvw > 14f) for (float u = cu0 + 6.5f; u <= cu1 - 3f; u += 7f) k.Tree(k.W(u, s * 11f), true, PlazaArt.PlaneTrees);
                    foreach (var q in LeisureKit.Along(k.W(cu0 + 6.5f, s * 3.4f), k.W(cu1 - 3f, s * 3.4f), 7f)) k.Seat(q, -k.S * s);
                    k.Stall(k.W(cu0 + 4f, s * (cvw - 3f)), -k.S * s, true);
                }
            }
            // allées radiating from the bowl across its lawns (the grounds belong to the theatre), benches facing the stage
            foreach (float deg in new[] { -80f, -50f, -20f, 20f, 50f, 80f })
            {
                float an = math.radians(deg);
                for (float r = R + bank + 5f; ; r += 9f)
                {
                    var q = P(r, an);
                    if (!k.Region.ContainsDisc(q, k.Fw + 3f)) break;
                    k.Tree(q, false, PlazaArt.GardenTrees);
                    if (((int)(r / 9f) & 1) == 0) k.Seat(q + Dir(an + 0.09f) * 0f + new float2(-Dir(an).y, Dir(an).x) * 2.2f, -Dir(an));
                }
            }
            // the lawns either side of the bowl: grass terraces of picnic blankets facing the stage, tree groups beyond
            foreach (float s in new[] { -1f, 1f })
            {
                for (float u = s0 + 4f; u < uF + R * 0.6f; u += 3.2f)
                    for (float v = R + bank + 3f; v < eb - 6f; v += 3.4f)
                    {
                        var q = k.W(u, s * v);
                        if (Hashing.Unit(k.V(0x7EB1u)) < 0.35f)
                            LeisureKit.Put(k.Ctx, k.Occ, q, math.atan2(-k.S.x * s, -k.S.y * s), ObjectRole.Furniture, new AssetRef(AssetMix.Pick(Blanket, k.V(0x7EB2u)).Id), AssetCategories.Picnic, 1.2f, k.V(0x7EB3u), 0.1f, 0.05f);
                    }
            }
            // the lawn's far side: trees in loose groups with benches
            for (float u = uF + R + bank + 4f; u < ea - 2f; u += 9f)
                for (float v = -eb + 4f; v < eb - 3f; v += 10f)
                    if (Hashing.Unit(k.V(0x7EA2u)) < 0.12f) k.Tree(k.W(u, v + (Hashing.Unit(k.V(0x7EA3u)) - 0.5f) * 4f), false, PlazaArt.GardenTrees);
            k.Core = r0;
            k.Ctx.Plan.Facts["theatreRadius"] = (float)Math.Round(R, 1);
        }
    }
}

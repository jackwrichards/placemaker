// Beach landforms (p2-beaches, 2026-10-31): the bold terrain the user asked for ("more aggressive and confident with moving
// terrain around"), laid on top of the shore profile (BeachLayer.Terrain) as functions of the distance from the real waterline
// (dw) and the arc length along the shore (s), so every form follows the curved shore and its bay.
//
//   berm        the crest the waves build at the top of the wet sand, a soft runnel behind it (real beach profile)
//   storm ridges  two or three shingle ridges stepping up the beach, beach cusps along the water (shingle)
//   blowouts    U-shaped notches cut through the dune ridge by the wind, the sand thrown in a lobe behind each
//   hummocks    the dunes broken into knobs and hollows behind the foredune (no smooth roll)
//   headlands   rock promontories built out into the water (fill), rounded, highest at their root, piled with boulders
//   platforms   flat rock shelves just above the water along stretches of the shore, a step up behind
//   bluff       the land behind the beach raised into a high bank (a scarp), cut by ravines (barrancas) down to the water
//   ravines     deep gullies through the bluff (or the bank), floor sloping out to the water, walls steep
//   knolls      rounded granite domes on the beach and in the shallows (Tahoe shore)
//   channels    tidal creeks meandering in from the water through a marsh flat, cut below the water so they fill, salt pans
//
// Each form records what it made (crests, axes, channel lines, pans, knolls) so the layout and the ground design (BeachGround)
// put boulders, reeds, bridges and decals exactly where the land says.

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Beach
{
    internal sealed class BeachForms
    {
        public float Level;
        public float Berm, BermAt, BermW = 4.5f;
        public readonly List<float> Ridges = new List<float>();
        public float RidgeH;
        public bool Cusps;
        public float DuneStart, DuneEnd, DuneH;
        public readonly List<(float s, float w)> Blowouts = new List<(float, float)>();
        public float Hummocks;
        public readonly List<(float s, float len, float h, float w)> Headlands = new List<(float, float, float, float)>();
        public readonly List<float2> Platforms = new List<float2>();
        public float PlatformTo = 7f, PlatformFrom = -6f;
        public float Bluff, BluffAt, BluffW = 9f;
        public readonly List<(float s, float w, float len)> Ravines = new List<(float, float, float)>();
        public readonly List<(float2 c, float r, float h)> Knolls = new List<(float2, float, float)>();
        /// <summary>Islands out in the water (a footbridge reaches each from the beach): centre, radius, height over the water, its arc length.</summary>
        public readonly List<(float2 c, float r, float h, float s)> Islands = new List<(float2, float, float, float)>();
        public float MarshTo;
        public readonly List<float2[]> Channels = new List<float2[]>();
        public readonly List<(float2 c, float r)> Pans = new List<(float2, float)>();
        public uint Seed;

        public float MaxFill = 1.5f, MaxCut = 5f, MaxGrade = 0.33f;
        /// <summary>Grade the shaped ground may climb from the outline's fixed border (a 10 m bluff at 25 % lost 40 m each side).</summary>
        public float BorderGrade = 0.25f;
        /// <summary>How far past the waterline the sculpt must reach (headlands and knolls out in the water).</summary>
        public float Reach;

        public bool Any => Berm > 0f || Ridges.Count > 0 || Blowouts.Count > 0 || Hummocks > 0f || Headlands.Count > 0 || Platforms.Count > 0
            || Bluff > 0f || Ravines.Count > 0 || Knolls.Count > 0 || Channels.Count > 0 || Cusps;

        /// <summary>Plans the forms the settings ask for (before the sculpt exists, so the foreshore can reach far enough).</summary>
        public static BeachForms Plan(LayerContext ctx, Shoreline shore, float wet, float depth, uint seed)
        {
            var f = new BeachForms { Seed = seed };
            float len = shore.Length;
            f.Berm = ctx.F("berm");
            f.BermAt = wet + 2.5f;
            int storms = ctx.I("storms");
            for (int i = 0; i < storms; i++) f.Ridges.Add(wet + 3f + i * math.max(5f, (depth - wet - 6f) / math.max(1, storms)));
            f.RidgeH = 0.7f;
            f.Cusps = storms > 0;
            // headlands: one at each end of the shore (a cove between them), or one in the middle; inset from the outline's ends so
            // the sculpt's border cap does not flatten them
            float hl = ctx.F("headlands");   // how far they reach out (0 = none); two, one at each end of the cove
            int heads = hl >= 8f ? 2 : 0;
            if (heads > 0 && len > 60f)
            {
                // a fifth in from each end: the cove lies between them, and the sculpt's border cap at the outline's sides leaves them whole
                float inset = math.max(30f, len * 0.2f);
                var at = heads == 1 || len < 2f * inset + 30f ? new[] { len * (0.3f + 0.4f * Hashing.Unit(Hashing.Mix(seed, 0x4EADu))) } : new[] { inset, len - inset };
                foreach (float s in at)
                {
                    uint v = Hashing.Mix(seed, (uint)(s * 10f));
                    f.Headlands.Add((s, hl * (0.85f + 0.3f * Hashing.Unit(v)), 3.4f + 1.4f * Hashing.Unit(Hashing.Mix(v, 1u)), 8f + 3f * Hashing.Unit(Hashing.Mix(v, 2u))));
                }
                f.Reach = math.max(f.Reach, hl * 1.15f + 18f);
                f.MaxFill = math.max(f.MaxFill, 8f);
                f.MaxGrade = math.max(f.MaxGrade, 0.55f);   // rock: steep flanks (at 33 % a 16 m wide ridge stood only 2.6 m)
            }
            float plat = ctx.F("platforms") / 100f;
            if (plat > 0f)
            {
                // stretches where a value noise along the shore passes the share: 25-60 m long
                float start = float.NaN;
                for (float s = 0f; s <= len; s += 2f)
                {
                    bool on = ValueNoise.Sample(new float2(s / 38f, 0.37f), Hashing.Mix(seed, 0x9A7u)) < plat;
                    if (on && float.IsNaN(start)) start = s;
                    if ((!on || s + 2f > len) && !float.IsNaN(start)) { if (s - start > 10f) f.Platforms.Add(new float2(start, s)); start = float.NaN; }
                }
                f.Reach = math.max(f.Reach, 14f);
                f.MaxFill = math.max(f.MaxFill, 3f);
            }
            f.Bluff = ctx.F("bluff");
            // the foot of the bluff behind the promenade's bench (on it, the walk's level climbed the face and lifted the sand 3 m)
            f.BluffAt = depth + 7f;
            int ravines = ctx.I("ravines");
            if (ravines > 0 && len > 50f)
                for (int i = 0; i < ravines; i++)
                {
                    // spread over the middle 40 %: towards the ends the bluff tapers down to the border and the walls lose their height
                    float s = len * (ravines == 1 ? 0.5f : 0.3f + 0.4f * i / (ravines - 1)) + (Hashing.Unit(Hashing.Mix(seed, 0x2A71u + (uint)i)) - 0.5f) * len * 0.06f;
                    f.Ravines.Add((s, 10f + 4f * Hashing.Unit(Hashing.Mix(seed, 0x2A72u + (uint)i)), 200f));
                }
            if (f.Bluff > 0f || f.Ravines.Count > 0) { f.MaxFill = math.max(f.MaxFill, f.Bluff + 1.5f); f.MaxCut = math.max(f.MaxCut, 8f); f.MaxGrade = math.max(f.MaxGrade, 0.75f); }
            // p2-beaches: a bluff cut by ravines is a rock face (~110 %), and the ravine walls near-vertical rock: at 45-75 % both
            // read as brown slopes (the game runner's rocky ravine)
            if (f.Ravines.Count > 0) { f.BluffW = 5f; f.MaxGrade = math.max(f.MaxGrade, 1.8f); }
            if (f.Bluff >= 6f) f.BorderGrade = 0.5f;
            // islands: a low rocky islet off the beach, reached by a footbridge (the user: bridges out to an island must join)
            int islands = ctx.I("islands");
            for (int i = 0; i < islands && len > 50f; i++)
            {
                uint v = Hashing.Mix(seed, 0x15A0u + (uint)i);
                float s = len * (islands == 1 ? 0.35f + 0.3f * Hashing.Unit(v) : (i + 0.5f) / islands);
                float r = 9f + 5f * Hashing.Unit(Hashing.Mix(v, 1u));
                var w = shore.WaterAt(s, out var inw);
                var ic = w - inw * (r + 14f + 8f * Hashing.Unit(Hashing.Mix(v, 2u)));
                // only where the player drew the outline out over the water far enough to hold it
                if (!shore.Outline.ContainsDisc(ic, r + 3f)) continue;
                f.Islands.Add((ic, r, 1.4f + 0.8f * Hashing.Unit(Hashing.Mix(v, 3u)), s));
            }
            // the player asked for an island but drew the outline along the shore: say how to get one
            if (islands > 0 && len > 50f && f.Islands.Count == 0) ctx.Issue(IssueSeverity.Info, BeachLayer.IslandsOutsideKey);
            if (f.Islands.Count > 0) { f.Reach = math.max(f.Reach, 46f); f.MaxFill = math.max(f.MaxFill, 7f); f.MaxGrade = math.max(f.MaxGrade, 0.5f); }
            int knolls = ctx.I("knolls");
            for (int i = 0; i < knolls; i++)
            {
                uint v = Hashing.Mix(seed, 0x6A0Fu + (uint)i);
                float s = len * (i + 0.2f + 0.6f * Hashing.Unit(v)) / math.max(1, knolls);
                float dw = -7f + Hashing.Unit(Hashing.Mix(v, 1u)) * (depth * 0.7f + 7f);
                var w = shore.WaterAt(s, out var inw);
                float r = 7f + 6f * Hashing.Unit(Hashing.Mix(v, 2u));
                f.Knolls.Add((w + inw * dw, r, 1.2f + 2.2f * Hashing.Unit(Hashing.Mix(v, 3u))));
            }
            if (knolls > 0) { f.Reach = math.max(f.Reach, 22f); f.MaxFill = math.max(f.MaxFill, 6f); f.MaxGrade = math.max(f.MaxGrade, 0.6f); }
            int channels = ctx.I("channels");
            if (channels > 0)
            {
                f.MarshTo = math.max(18f, depth);
                for (int i = 0; i < channels; i++)
                {
                    uint v = Hashing.Mix(seed, 0xC4A0u + (uint)i);
                    float s0 = len * (i + 0.5f) / channels + (Hashing.Unit(v) - 0.5f) * len * 0.3f / channels;
                    float reach = f.MarshTo * (0.55f + 0.4f * Hashing.Unit(Hashing.Mix(v, 1u)));
                    var pts = new List<float2>();
                    float phase = Hashing.Unit(Hashing.Mix(v, 2u)) * 6.283f, amp = 4f + 5f * Hashing.Unit(Hashing.Mix(v, 3u));
                    for (float d = -6f; d <= reach; d += 2f)
                    {
                        float s = s0 + amp * math.sin(d / 11f + phase) * math.saturate((d + 6f) / 10f);
                        var w = shore.WaterAt(math.clamp(s, 2f, len - 2f), out var inw);
                        pts.Add(w + inw * d);
                    }
                    f.Channels.Add(pts.ToArray());
                    // a salt pan or two off each creek
                    for (int k = 0; k < 2; k++)
                    {
                        uint pv = Hashing.Mix(v, 10u + (uint)k);
                        float d = reach * (0.35f + 0.5f * Hashing.Unit(pv));
                        float s = s0 + (Hashing.Unit(Hashing.Mix(pv, 1u)) < 0.5f ? -1f : 1f) * (8f + 6f * Hashing.Unit(Hashing.Mix(pv, 2u)));
                        var w = shore.WaterAt(math.clamp(s, 2f, len - 2f), out var inw);
                        f.Pans.Add((w + inw * d, 3.5f + 3f * Hashing.Unit(Hashing.Mix(pv, 3u))));
                    }
                }
                f.Reach = math.max(f.Reach, 8f);
            }
            return f;
        }

        /// <summary>The dune belt (set once the walk's distance is known): blowouts through it, hummocks behind the foredune.</summary>
        public void Dunes(LayerContext ctx, Shoreline shore, float start, float end, float height)
        {
            DuneStart = start; DuneEnd = end; DuneH = height;
            // a dune's sea face stands near the sand's angle of repose (at 33 % a 5 m foredune was a smooth hump)
            if (height >= 2f) MaxGrade = math.max(MaxGrade, 0.6f);
            int n = ctx.I("blowouts");
            for (int i = 0; i < n; i++)
            {
                uint v = Hashing.Mix(Seed, 0xB10Bu + (uint)i);
                float s = shore.Length * (i + 0.25f + 0.5f * Hashing.Unit(v)) / math.max(1, n);
                Blowouts.Add((s, 7f + 6f * Hashing.Unit(Hashing.Mix(v, 1u))));
            }
            Hummocks = ctx.F("hummocks");
            if (height > 1.5f) MaxFill = math.max(MaxFill, height + 1f + Hummocks);
        }

        private static float Bump(float x) => math.exp(-x * x);

        /// <summary>The ground of one node after the forms (t = the shore profile's target there).</summary>
        public float Apply(float t, float dw, float s, float h0, float2 p, float r0)
        {
            float level = r0;
            // berm: steep to the sea, a long gentle back, a shallow runnel behind it
            if (Berm > 0f && dw > -2f)
            {
                float x = (dw - BermAt) / BermW;
                t += Berm * (x < 0f ? Bump(x * 1.6f) : Bump(x * 0.8f));   // (no runnel cut behind it: "Carve terrain" off must never lower the beach)
            }
            if (Ridges.Count > 0 && dw > -1f)
                for (int i = 0; i < Ridges.Count; i++)
                {
                    float x = (dw - Ridges[i]) / 2.6f;
                    t += RidgeH * (0.75f + 0.35f * i) * (x < 0f ? Bump(x * 1.3f) : Bump(x * 0.7f));
                }
            if (Cusps && dw > -4f && dw < Ridges[0] + 2f)
                t += 0.32f * math.cos(s / 17f * 6.283f) * Bump((dw - Ridges[0] * 0.5f) / math.max(2f, Ridges[0] * 0.6f));
            // dunes: blowouts take the crest down to near the beach, their sand forms a lobe landward; hummocks break the back dunes
            if (DuneH > 0f && dw > DuneStart && dw < DuneEnd + 14f)
            {
                float u = math.saturate((dw - DuneStart) / math.max(1f, DuneEnd - DuneStart));
                float duneNow = math.max(0f, t - h0);
                foreach (var (bs, bw) in Blowouts)
                {
                    float cross = math.abs(s - bs) / (bw * (0.6f + 0.6f * u));
                    if (cross > 2f) continue;
                    t -= duneNow * 0.85f * Bump(cross) * (u < 0.75f ? 1f : math.saturate((1f - u) * 4f));
                    t += DuneH * 0.55f * Bump(cross * 1.3f) * Bump((dw - DuneEnd - 4f) / 5f);
                }
                if (Hummocks > 0f && u > 0.3f)
                {
                    float hn = ValueNoise.Sample(p / 9f, Hashing.Mix(Seed, 0x40u)) - 0.5f + 0.5f * (ValueNoise.Sample(p / 4.5f, Hashing.Mix(Seed, 0x5u)) - 0.5f);
                    t += Hummocks * 2f * hn * math.saturate((u - 0.3f) * 3f) * math.saturate((DuneEnd + 10f - dw) / 10f);
                }
            }
            // bluff: the land behind the beach stands as a high bank (a scarp ~45 %)
            if (Bluff > 0f && dw > BluffAt - 2f)
                t = math.max(t, h0 + Bluff * Sculpt.Smooth01((dw - BluffAt) / BluffW));
            // ravines: a gully cut through the bank down to the water, floor gently rising inland, walls steep
            foreach (var (rs, rw, rlen) in Ravines)
            {
                if (dw < -3f || dw > rlen) continue;
                float wiggle = 3f * math.sin(dw / 13f + rs);
                float cross = math.abs(s - rs - wiggle);
                float top = rw * (0.75f + 0.35f * math.saturate(dw / 40f));
                if (cross > top + 4f) continue;
                float floor = level + 0.25f + 0.045f * math.max(0f, dw);
                // p2-beaches: a flat boulder floor and walls climbing over the last 4 m (they spread over the whole width and read as a
                // shallow brown dip, the game runner's rocky ravine)
                float half = math.max(2.2f + 0.04f * math.max(0f, dw), top - 4.5f);
                float w = math.saturate((cross - half) / math.max(1f, top - half));
                float wall = math.lerp(floor, t, Sculpt.Smooth01(w));
                // rough walls: ledges and buttresses
                if (w > 0.05f && w < 0.95f) wall -= 0.7f * (ValueNoise.Sample(p / 3f, Hashing.Mix(Seed, 0x7u)) - 0.3f);
                t = math.min(t, wall);
            }
            // platforms: rock shelves just above the water, a step up behind
            foreach (var pl in Platforms)
            {
                if (s < pl.x || s > pl.y || dw < PlatformFrom || dw > PlatformTo + 6f) continue;
                float edge = math.min(s - pl.x, pl.y - s);
                float a = math.saturate(edge / 4f);
                float shelf = dw < 1.5f ? level + 0.35f : level + 0.85f;
                shelf += 0.12f * (ValueNoise.Sample(p / 2.5f, Hashing.Mix(Seed, 0x8u)) - 0.5f);
                float back = math.saturate((dw - PlatformTo) / 6f);
                float target = math.lerp(shelf, t, back);
                t = math.lerp(t, dw < PlatformTo ? math.max(target, dw < PlatformFrom + 2f ? t : target) : target, a);
            }
            // headlands: a rounded rock ridge from the shore out into the water, highest at its root
            foreach (var (hs, hlen, hh, hw) in Headlands)
            {
                if (dw > 18f || dw < -hlen - 8f) continue;
                float u = math.saturate((dw + hlen) / (hlen + 18f));   // 0 at the tip, 1 at the root
                float bend = 4f * math.sin(dw / 9f + hs) * (1f - u);
                float cross = math.abs(s - hs - bend);
                float w = hw * (0.55f + 0.45f * u) * (0.85f + 0.3f * ValueNoise.Sample(new float2(dw / 7f, hs), Hashing.Mix(Seed, 0x9u)));
                if (cross > w + 7f) continue;
                float topH = level + math.lerp(0.9f, hh, Sculpt.Smooth01(u)) + 0.5f * (ValueNoise.Sample(p / 3.5f, Hashing.Mix(Seed, 0xAu)) - 0.5f);
                float c = cross / w;
                float prof = c < 1f ? math.pow(1f - c * c, 0.6f) : 0f;
                float flank = c < 1f ? 0f : math.saturate((cross - w) / 7f);
                float h = c < 1f ? math.lerp(level - 0.2f, topH, prof) : math.lerp(level - 0.2f, math.min(t, level - 1f), flank);
                float tipFade = math.saturate((dw + hlen + 6f) / 6f);
                if (c < 1f) t = math.max(t, math.lerp(t, h, tipFade));
            }
            // islands: a flat-topped rocky islet, steep at its rim
            foreach (var (ic, ir, ih, _) in Islands)
            {
                float d = math.distance(p, ic) / ir;
                if (d > 1.6f) continue;
                float lump = 0.85f + 0.3f * ValueNoise.Sample(p / 3.2f, Hashing.Mix(Seed, 0xDu));
                float top = level + ih * lump * (1f - 0.25f * d * d);
                float rim = math.saturate((1.6f - d) / 0.6f);
                t = math.max(t, math.lerp(math.min(t, level - 1.2f), top, Sculpt.Smooth01(rim)));
            }
            // granite knolls: lumpy domes (on land and out in the shallows)
            foreach (var (kc, kr, kh) in Knolls)
            {
                float d = math.distance(p, kc) / kr;
                if (d > 1.15f) continue;
                float lump = 0.75f + 0.5f * ValueNoise.Sample(p / 2.8f, Hashing.Mix(Seed, 0xBu));
                float baseH = math.min(h0, t);
                t = math.max(t, math.max(baseH, level - 0.6f) + kh * lump * math.pow(math.saturate(1f - d * d), 0.55f));
            }
            // marsh: a near-level flat just above the water, creeks cut below it (they fill), salt pans in hollows
            if (MarshTo > 0f && dw > -4f && dw < MarshTo + 10f)
            {
                float a = 1f - math.saturate((dw - MarshTo) / 10f);
                float flat = level + 0.22f + 0.004f * math.max(0f, dw) + 0.12f * (ValueNoise.Sample(p / 6f, Hashing.Mix(Seed, 0xCu)) - 0.5f);
                if (dw > 0f) t = math.lerp(t, flat, a);
                foreach (var (pc, pr) in Pans)
                {
                    float d = math.distance(p, pc) / pr;
                    if (d < 1.2f) t = math.lerp(t, level - 0.35f, Sculpt.Smooth01(1.2f - d) * a);   // pools that hold water
                }
                float dc = ChannelDistance(p, out float tw);
                if (dc < tw + 2f)
                {
                    float bed = level - 1.4f * (0.6f + 0.4f * math.saturate(tw / 6f));
                    t = math.lerp(bed, t, Sculpt.Smooth01(math.saturate((dc - tw * 0.5f) / (tw * 0.5f + 2f))));
                }
            }
            return t;
        }

        /// <summary>Distance to the nearest creek and its half-width there (wider at the mouth).</summary>
        public float ChannelDistance(float2 p, out float halfWidth)
        {
            float best = 1e9f; halfWidth = 1.2f;
            if (_boxes == null || _boxes.Length != Channels.Count)
            {
                _boxes = new float4[Channels.Count];
                for (int c = 0; c < Channels.Count; c++) { Geometry.Poly.Bounds(Channels[c], out var lo, out var hi); _boxes[c] = new float4(lo, hi); }
            }
            for (int c = 0; c < Channels.Count; c++)
            {
                var bx = _boxes[c];
                float dx = math.max(0f, math.max(bx.x - p.x, p.x - bx.z)), dz = math.max(0f, math.max(bx.y - p.y, p.y - bx.w));
                if (dx * dx + dz * dz >= best * best) continue;   // (perf) a creek whose box is farther than the best so far
                var l = Channels[c];
                for (int i = 1; i < l.Length; i++)
                {
                    var a = l[i - 1]; var b = l[i]; var ab = b - a;
                    float t = math.saturate(math.dot(p - a, ab) / math.max(1e-4f, math.dot(ab, ab)));
                    float d = math.distance(p, a + ab * t);
                    if (d < best) { best = d; halfWidth = math.lerp(6.5f, 3.2f, (i - 1 + t) / math.max(1, l.Length - 1)); }   // (3.6 m wide creeks vanished on the 3.5 m heightmap)
                }
            }
            return best;
        }

        private float4[] _boxes;

        public bool InRavine(float s, float dw, float pad = 0f)
        {
            foreach (var (rs, rw, rlen) in Ravines)
            {
                if (dw < -3f || dw > rlen) continue;
                float wiggle = 3f * math.sin(dw / 13f + rs);
                if (math.abs(s - rs - wiggle) < rw * (0.75f + 0.35f * math.saturate(dw / 40f)) + pad) return true;
            }
            return false;
        }

        public bool OnHeadland(float s, float dw, out float u)
        {
            u = 0f;
            foreach (var (hs, hlen, hh, hw) in Headlands)
            {
                if (dw > 18f || dw < -hlen) continue;
                u = math.saturate((dw + hlen) / (hlen + 18f));
                float bend = 4f * math.sin(dw / 9f + hs) * (1f - u);
                if (math.abs(s - hs - bend) < hw * (0.55f + 0.45f * u)) return true;
            }
            return false;
        }

        public bool OnIsland(float2 p, float pad = 0f)
        {
            foreach (var (ic, ir, _, _) in Islands) if (math.distance(p, ic) < ir + pad) return true;
            return false;
        }

        public bool OnPlatform(float s, float dw)
        {
            foreach (var pl in Platforms) if (s >= pl.x && s <= pl.y && dw >= PlatformFrom && dw <= PlatformTo) return true;
            return false;
        }
    }
}

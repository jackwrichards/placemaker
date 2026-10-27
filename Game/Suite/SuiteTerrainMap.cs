// Game runner: a terrain map picture (the "terrain" step). The ground's height, read from the game's own height data,
// over the build's outline and a margin around it, seen from straight above (north up): colour by height around the
// outline's typical ground (blue below, red above), shaded by the slope, contour lines every 0.5 m (wider on hills: 1, 2, 5 or 10 m; darker every 4th),
// the outline in white. Only the ground: no buildings, trees or roads. It measures nothing else and judges nothing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Simulation;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Geometry;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {


        private IEnumerator StepTerrainMap(ScenarioState st, JObject step, JObject rec)
        {
            yield return Frames(30);   // the ground after a build reaches the CPU height data a few frames later
            string name = (string)step["name"] ?? "terrain";
            float margin = (float?)step["margin"] ?? 20f;
            var b = st.S.Block;
            float2 lo, hi;
            if (st.Ring != null && st.Ring.Length > 2) (lo, hi) = Bounds(st.Ring, margin);
            else { lo = new float2(b.X0, b.Z0) - margin; hi = new float2(b.X1, b.Z1) + margin; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string err = null;
            JObject info = null;
            string file = ShotFile(st, name);
            try { info = TerrainMap(lo, hi, st.Ring, Path.Combine(_shots, file)); }
            catch (Exception e) { err = e.GetType().Name + ": " + e.Message; }
            _suiteMsThisFrame += (float)sw.Elapsed.TotalMilliseconds;
            string rel = err == null ? "shots/" + file : "ERROR " + err;
            // probe: [[x, z], ...] metres from the south-west corner of the outline's box (the AI builder's coordinates):
            // the live ground and water surface there, one line each
            if (step["probe"] is JArray probe && st.Ring != null && st.Ring.Length > 2)
            {
                var corner = Bounds(st.Ring, 0f).Item1;
                var hd = _world.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
                var water = _world.GetOrCreateSystemManaged<WaterSystem>().GetSurfacesData(out var deps);
                deps.Complete();
                var outp = new JArray();
                foreach (var q in probe.OfType<JArray>())
                {
                    var p = corner + new float2((float)q[0], (float)q[1]);
                    var at = new float3(p.x, 0f, p.y);
                    float ground = TerrainUtils.SampleHeight(ref hd, at), depth = WaterUtils.SampleDepth(ref water, at);
                    outp.Add(new JObject { ["at"] = q, ["ground"] = Math.Round(ground, 2), ["water"] = depth > 0.01f ? Math.Round(ground + depth, 2) : (double?)null });
                    Say($"terrain probe ({q[0]}, {q[1]}): ground {ground:F2} m" + (depth > 0.01f ? $", water surface {ground + depth:F2} m ({depth:F2} m deep)" : ", dry"));
                }
                rec["probe"] = outp;
            }
            rec["shot"] = rel;
            rec["shotName"] = name;
            if (info != null) rec["terrain"] = info;
            if (err != null) rec["error"] = "terrain map: " + err;
            else Say($"terrain map {name}: ground {info["minM"]} to +{info["maxM"]} m around {info["groundM"]} m (colour range ±{info["rangeM"]} m, contours every {info["contourM"]} m)");
            st.Shots[name] = rel;
            _contact.Add(new ShotRow
            {
                Scenario = st.FileIndex, Id = st.S.Id, Step = st.StepIndex, Do = "terrain", Name = name, File = err == null ? rel : null, Mode = "terrain map",
                Settled = true, SettleMs = (int)sw.ElapsedMilliseconds, Error = err,
                Camera = info == null ? null : new JObject { ["target"] = "ground", ["view"] = $"from above, {info["metresPerPixel"]} m per pixel; ±{info["rangeM"]} m around {info["groundM"]} m" },
            });
        }

        /// <summary>Writes the terrain map PNG of the box lo..hi; returns its numbers (ground level, range, min, max).</summary>
        private JObject TerrainMap(float2 lo, float2 hi, float2[] ring, string path)
        {
            var terrain = _world.GetOrCreateSystemManaged<TerrainSystem>();
            var hd = terrain.GetHeightData(true);
            float2 size = hi - lo;
            float cell = math.max(size.x, size.y) / 1000f;
            int w = math.max(2, (int)math.ceil(size.x / cell)), h = math.max(2, (int)math.ceil(size.y / cell));
            var z = new float[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    z[j * w + i] = TerrainUtils.SampleHeight(ref hd, new float3(lo.x + (i + 0.5f) * cell, 0f, lo.y + (j + 0.5f) * cell));

            // the typical ground: the median inside the outline (else of the whole box)
            var inside = new List<float>();
            for (int j = 0; j < h; j += 4)
                for (int i = 0; i < w; i += 4)
                    if (ring == null || ring.Length < 3 || Poly.Contains(ring, new float2(lo.x + (i + 0.5f) * cell, lo.y + (j + 0.5f) * cell))) inside.Add(z[j * w + i]);
            if (inside.Count == 0) inside.AddRange(z);
            inside.Sort();
            float ground = inside[inside.Count / 2];
            var dev = z.Select(v => math.abs(v - ground)).OrderBy(v => v).ToList();
            float range = math.max(1f, dev[(int)(dev.Count * 0.99f)]);
            // contour spacing: 0.5 m on gentle ground, wider on hills and mountains (about 30 lines over the range at most)
            float ContourM = 0.5f;
            foreach (var s in new[] { 0.5f, 1f, 2f, 5f, 10f }) { ContourM = s; if (2f * range / s <= 30f) break; }
            float MajorContourM = ContourM * 4f;

            var px = new Color32[w * h];
            var light = math.normalize(new float3(-1f, 1.4f, 1f));   // from the north-west, as maps are lit
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float v = z[j * w + i];
                    float t = math.clamp((v - ground) / range, -1f, 1f);
                    var neutral = new float3(0.93f, 0.93f, 0.89f);
                    var c = t < 0f ? math.lerp(neutral, new float3(0.12f, 0.32f, 0.72f), math.pow(-t, 0.7f)) : math.lerp(neutral, new float3(0.78f, 0.16f, 0.10f), math.pow(t, 0.7f));
                    float dx = (z[j * w + math.min(w - 1, i + 1)] - z[j * w + math.max(0, i - 1)]) / (2f * cell);
                    float dz = (z[math.min(h - 1, j + 1) * w + i] - z[math.max(0, j - 1) * w + i]) / (2f * cell);
                    float shade = 0.72f + 0.28f * math.saturate(math.dot(math.normalize(new float3(-dx, 1f, -dz)), light) / light.y * 0.8f);
                    c *= shade;
                    // contour lines: the 0.5 m band changes towards the right or upper neighbour; the bands are centred on the
                    // typical ground, so flat ground at that level draws no line, a rise or drop of 0.25 m does
                    if (i + 1 < w && j + 1 < h)
                    {
                        float Band(float hgt, float step) => math.round((hgt - ground) / step);
                        float right = z[j * w + i + 1], above = z[(j + 1) * w + i];
                        if (Band(v, ContourM) != Band(right, ContourM) || Band(v, ContourM) != Band(above, ContourM))
                        {
                            bool major = Band(v, MajorContourM) != Band(right, MajorContourM) || Band(v, MajorContourM) != Band(above, MajorContourM);
                            c *= major ? 0.35f : 0.65f;
                        }
                    }
                    px[j * w + i] = new Color32((byte)(math.saturate(c.x) * 255f), (byte)(math.saturate(c.y) * 255f), (byte)(math.saturate(c.z) * 255f), 255);
                }

            // the outline: white with a dark edge
            if (ring != null && ring.Length > 1)
                for (int k = 0; k < ring.Length; k++)
                {
                    var p = (ring[k] - lo) / cell;
                    var q = (ring[(k + 1) % ring.Length] - lo) / cell;
                    int n = (int)math.ceil(math.length(q - p)) + 1;
                    for (int s = 0; s <= n; s++)
                    {
                        var m = math.lerp(p, q, s / (float)n);
                        for (int oy = -2; oy <= 2; oy++)
                            for (int ox = -2; ox <= 2; ox++)
                            {
                                int x = (int)m.x + ox, y = (int)m.y + oy;
                                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                                bool core = math.abs(ox) <= 1 && math.abs(oy) <= 1;
                                var cur = px[y * w + x];
                                px[y * w + x] = core ? new Color32(255, 255, 255, 255) : (cur.r == 255 && cur.g == 255 ? cur : new Color32(40, 40, 40, 255));
                            }
                    }
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(tex); }
            float min = z.Min() - ground, max = z.Max() - ground;
            return new JObject
            {
                ["groundM"] = Math.Round(ground, 2), ["minM"] = Math.Round(min, 2), ["maxM"] = Math.Round(max, 2), ["rangeM"] = Math.Round(range, 2),
                ["contourM"] = ContourM, ["majorContourM"] = MajorContourM, ["metresPerPixel"] = Math.Round(cell, 3), ["size"] = $"{w}x{h}",
                ["box"] = new JArray(Math.Round(lo.x), Math.Round(lo.y), Math.Round(hi.x), Math.Round(hi.y)),
            };
        }
    }
}

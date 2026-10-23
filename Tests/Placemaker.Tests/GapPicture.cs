using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Placemaker.Core.Geometry;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.Tests
{
    /// <summary>A plan drawn from above as a PNG, 2 px a metre (north up): roads grey, lots dark, each plot in its own
    /// colour, fence and hedge pieces white, bare land between lots red. For looking at a planner test, not for checks.</summary>
    internal static class GapPicture
    {
        public static void Write(string path, IReadOnlyList<StitchHouse> houses, StitchPlan plan, IReadOnlyList<float2[]> roads, float pxPerM = 2f, float margin = 30f, float2[] inside = null)
        {
            var lots = houses.Select(h => new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft }).ToList();
            var all = lots.SelectMany(l => l).Concat(plan.Plots.Values.SelectMany(p => p)).ToList();
            var lo = all.Aggregate(new float2(float.MaxValue), math.min) - margin;
            var hi = all.Aggregate(new float2(float.MinValue), math.max) + margin;
            int w = (int)math.ceil((hi.x - lo.x) * pxPerM), h = (int)math.ceil((hi.y - lo.y) * pxPerM);
            var px = new byte[w * h * 3];
            var plots = houses.Select((x, i) => (Ring: plan.Plots.TryGetValue(x.Id, out var p) ? p : null, I: i)).Where(t => t.Ring != null).ToList();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new float2(lo.x + (x + 0.5f) / pxPerM, hi.y - (y + 0.5f) / pxPerM);
                    (byte r, byte g, byte b) c = (70, 96, 52);   // open land
                    if (roads.Any(r => Poly.Contains(r, p))) c = (128, 128, 128);
                    else if (lots.Any(l => Poly.Contains(l, p))) c = (40, 40, 48);
                    else
                    {
                        var pl = plots.FirstOrDefault(t => Poly.Contains(t.Ring, p));
                        if (pl.Ring != null) c = Hue(pl.I);
                        else
                        {
                            var d = lots.Select(l => Dist(l, p)).OrderBy(v => v).Take(2).ToArray();
                            if (inside != null ? Poly.Contains(inside, p) : d.Length == 2 && d[1] <= 30f) c = (220, 40, 40);
                        }
                    }
                    int o = (y * w + x) * 3;
                    px[o] = c.r; px[o + 1] = c.g; px[o + 2] = c.b;
                }
            foreach (var f in plan.FencePieces)
            {
                int x = (int)((f.Position.x - lo.x) * pxPerM), y = (int)((hi.y - f.Position.y) * pxPerM);
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                int o = (y * w + x) * 3;
                px[o] = px[o + 1] = px[o + 2] = 255;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Png(w, h, px));
        }

        private static (byte, byte, byte) Hue(int i)
        {
            float t = (i * 0.618034f) % 1f;
            float3 c = math.saturate(math.abs(math.frac(t + new float3(0f, 2f / 3f, 1f / 3f)) * 6f - 3f) - 1f);
            c = math.lerp(new float3(0.55f), c, 0.55f) * 0.9f + 0.1f;
            return ((byte)(c.x * 255), (byte)(c.y * 255), (byte)(c.z * 255));
        }

        private static float Dist(float2[] ring, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var e = ring[(i + 1) % ring.Length] - a;
                float u = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * u));
            }
            return best;
        }

        private static byte[] Png(int w, int h, byte[] rgb)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) { raw.Position = 0; raw.CopyTo(zs); }
            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            void Chunk(string type, byte[] data)
            {
                var t = System.Text.Encoding.ASCII.GetBytes(type);
                png.Write(BigEndian((uint)data.Length));
                png.Write(t);
                png.Write(data);
                png.Write(BigEndian(Crc(t.Concat(data).ToArray())));
            }
            var ihdr = new List<byte>();
            ihdr.AddRange(BigEndian((uint)w));
            ihdr.AddRange(BigEndian((uint)h));
            ihdr.AddRange(new byte[] { 8, 2, 0, 0, 0 });
            Chunk("IHDR", ihdr.ToArray());
            Chunk("IDAT", z.ToArray());
            Chunk("IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static byte[] BigEndian(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        private static uint Crc(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (var b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFFu;
        }
    }
}

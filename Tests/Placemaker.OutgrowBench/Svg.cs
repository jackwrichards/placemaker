// Outgrow benchmark: one planned pass drawn (--svg): what the planner was given and what it made.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Svg
    {
        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public static string Draw(List<StitchHouse> houses, StitchSettings settings, StitchPlan plan)
        {
            // the picture: around the edited houses' gardens and lots, 30 m more
            var lo = new float2(float.MaxValue);
            var hi = new float2(float.MinValue);
            void Take(float2 p) { lo = math.min(lo, p); hi = math.max(hi, p); }
            foreach (var h in houses.Where(h => h.Edit))
            {
                Take(h.FrontLeft); Take(h.BackRight); Take(h.FrontRight); Take(h.BackLeft);
                if (plan.Plots.TryGetValue(h.Id, out var g) && g != null) foreach (var p in g) Take(p);
            }
            if (lo.x > hi.x) foreach (var h in houses) { Take(h.FrontLeft); Take(h.BackRight); }
            lo -= 30f;
            hi += 30f;
            var size = hi - lo;
            // map: x right, z up
            string X(float2 p) => F(p.x - lo.x);
            string Y(float2 p) => F(hi.y - p.y);
            string Pts(IEnumerable<float2> ring) => string.Join(" ", ring.Select(p => X(p) + "," + Y(p)));
            bool In(float2[] ring) => ring != null && ring.Any(p => p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y);
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(size.x)} {F(size.y)}\" width=\"1400\" height=\"{F(1400f * size.y / size.x)}\">\n");
            sb.Append($"<rect width=\"{F(size.x)}\" height=\"{F(size.y)}\" fill=\"#eef2e6\"/>\n");
            // barriers: roads and the like dark grey, water blue, lots light grey
            if (settings.Barriers != null)
                for (int k = 0; k < settings.Barriers.Count; k++)
                {
                    var r = settings.Barriers[k];
                    if (!In(r)) continue;
                    int kind = settings.BarrierKinds != null && k < settings.BarrierKinds.Count ? settings.BarrierKinds[k] : 0;
                    sb.Append($"<polygon points=\"{Pts(r)}\" fill=\"{(kind == 0 ? "#9a9a9a" : kind == 2 ? "#9cc3e6" : "#cfcfcf")}\" stroke=\"none\"/>\n");
                }
            // gardens: edited filled in their colour, fixed ones outlined
            // (the ids differ mostly in their high bits: mixed, or neighbours came out the same colour)
            string Colour(long id)
            {
                ulong z = (ulong)id;
                z ^= z >> 33; z *= 0xff51afd7ed558ccdUL; z ^= z >> 33; z *= 0xc4ceb9fe1a85ec53UL; z ^= z >> 33;
                return $"hsl({z % 360},55%,{55 + (z >> 20) % 20}%)";
            }
            foreach (var h in houses)
            {
                var g = h.Edit && plan.Plots.TryGetValue(h.Id, out var planned) ? planned : h.Garden;
                if (g == null || !In(g)) continue;
                sb.Append(h.Edit
                    ? $"<polygon data-id=\"{h.Id % 1000}\" points=\"{Pts(g)}\" fill=\"{Colour(h.Id)}\" fill-opacity=\"0.45\" stroke=\"{Colour(h.Id)}\" stroke-width=\"0.4\"/>\n"
                    : $"<polygon points=\"{Pts(g)}\" fill=\"none\" stroke=\"#6d8f5a\" stroke-width=\"0.3\" stroke-dasharray=\"1,1\"/>\n");
            }
            // lots, with an arrow to the street and the house id's last digits
            foreach (var h in houses)
            {
                var lot = new[] { h.BackLeft, h.BackRight, h.FrontRight, h.FrontLeft };
                if (!In(lot)) continue;
                sb.Append($"<polygon points=\"{Pts(lot)}\" fill=\"none\" stroke=\"{(h.Edit ? "#333" : "#999")}\" stroke-width=\"0.3\"/>\n");
                var front = (h.FrontLeft + h.FrontRight) * 0.5f;
                sb.Append($"<line x1=\"{X(front)}\" y1=\"{Y(front)}\" x2=\"{X(front + h.Forward * 2f)}\" y2=\"{Y(front + h.Forward * 2f)}\" stroke=\"#333\" stroke-width=\"0.3\"/>\n");
                sb.Append($"<text x=\"{X(h.Position)}\" y=\"{Y(h.Position)}\" font-size=\"3\" text-anchor=\"middle\" fill=\"#222\">{h.Id % 1000}</text>\n");
            }
            // fence and hedge lines: the houses' own as the plan leaves them (moved; hidden ones left out), fixed houses' standing
            foreach (var h in houses)
            {
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = plan.MoveLines.TryGetValue(l.Id, out var m) ? m : (l.A, l.B);
                    sb.Append($"<line x1=\"{X(a)}\" y1=\"{Y(a)}\" x2=\"{X(b)}\" y2=\"{Y(b)}\" stroke=\"#5a3a1a\" stroke-width=\"0.6\"/>\n");
                }
                if (!h.Edit && h.Standing != null)
                    foreach (var (a, b) in h.Standing)
                        sb.Append($"<line x1=\"{X(a)}\" y1=\"{Y(a)}\" x2=\"{X(b)}\" y2=\"{Y(b)}\" stroke=\"#8a7a6a\" stroke-width=\"0.4\"/>\n");
            }
            // the plan's fence and hedge pieces
            foreach (var (house, at, yaw, name) in plan.FencePieces)
            {
                var d = new float2(math.sin(yaw), math.cos(yaw)) * 1f;
                sb.Append($"<line x1=\"{X(at - d)}\" y1=\"{Y(at - d)}\" x2=\"{X(at + d)}\" y2=\"{Y(at + d)}\" stroke=\"{(name != null && name.Contains("Hedge") ? "#2f6b2f" : "#7a4a1a")}\" stroke-width=\"0.7\"/>\n");
            }
            sb.Append("</svg>\n");
            return sb.ToString();
        }
    }
}
